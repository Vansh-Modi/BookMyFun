using BookMyFun.Controllers;
using BookMyFun.Data;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace BookMyFun.Tests;

public class EndToEndWorkflowTests
{
    private readonly IConfiguration _config;
    private readonly DBHelper _db;

    public EndToEndWorkflowTests()
    {
        var configManager = new ConfigurationManager();
        configManager["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";
        configManager["Razorpay:KeyId"] = "rzp_test_bookmyfun_demo";
        configManager["Razorpay:KeySecret"] = "rzp_secret_demo_12345";
        _config = configManager;
        _db = new DBHelper(_config);
    }

    private class StubMediaStorageService : IMediaStorageService
    {
        public Task<(string publicUrl, string publicId)> SaveMediaFileAsync(IFormFile file, string mediaType) 
            => Task.FromResult(("https://example.com/test-media.jpg", "media_test_123"));
        public Task<string> SavePrivateDocumentAsync(IFormFile file, int clientId) 
            => Task.FromResult("doc_test_123.pdf");
        public Task<bool> ValidateExternalUrlAsync(string url) => Task.FromResult(true);
        public string GenerateExpiringDocumentToken(string documentKey, int requestingUserId, string userRole, int expirationMinutes = 15) 
            => "token_123";
        public bool ValidateExpiringDocumentToken(string documentKey, string token, int requestingUserId, string userRole) 
            => true;
        public string GetPrivateDocumentPhysicalPath(string documentKey) => "C:\\temp\\doc.pdf";
    }

    [Fact]
    public async Task EndToEnd_Booking_RazorpayPayment_MultiStakeholderAlerts_AndCancellation()
    {
        var propRepo = new PropertyRepository(_db);
        var bookingRepo = new BookingRepository(_db);
        var configRepo = new ConfigRepository(_db);
        var userRepo = new UserRepository(_db);
        var refundRepo = new RefundRepository(_db);
        var commsLogger = NullLogger<CommunicationService>.Instance;
        var rzpLogger = NullLogger<RazorpayPaymentService>.Instance;
        var commsService = new CommunicationService(configRepo, _config, commsLogger);
        var rzpService = new RazorpayPaymentService(configRepo, _config, rzpLogger);
        var pricingService = new BookingPricingService();
        var commissionService = new CommissionService(configRepo, userRepo);
        var refundEngine = new RefundEngineService(bookingRepo, propRepo, refundRepo, configRepo, NullLogger<RefundEngineService>.Instance);

        // 1. Get or create a verified host & property
        var properties = await propRepo.GetAllPropertiesAsync(onlyApproved: true);
        Property targetProp = properties.FirstOrDefault()!;
        if (targetProp == null)
        {
            var adminUser = await userRepo.GetByEmailAsync("admin@bookmyfun.com");
            int propId = await propRepo.CreatePropertyAsync(new Property
            {
                OwnerID = adminUser!.UserID,
                PropertyTypeID = 1,
                Title = "E2E Luxury Pool Villa",
                Location = "Lonavala",
                PricePerNight = 12000.00m,
                SecurityDepositAmount = 2000.00m,
                MaxGuests = 10,
                ListingStatus = "Active",
                IsApproved = true
            });
            targetProp = (await propRepo.GetByIdAsync(propId))!;
        }

        // 2. Get client user
        var client = await userRepo.GetByEmailAsync("client@bookmyfun.com");
        Assert.NotNull(client);

        // 3. Calculate Pricing & Segregation (2 Nights + 4,000 Decor Package)
        decimal propertyStayGross = pricingService.CalculatePrice(targetProp.PricePerNight, "FullDay", null, 2);
        decimal decorPrice = 4000.00m;
        decimal totalAmount = propertyStayGross + decorPrice;

        var (commRate, platformComm, ownerPayout) = await commissionService.CalculateDynamicCommissionAsync(propertyStayGross, targetProp.CommissionRate, targetProp.OwnerID);
        Assert.Equal(propertyStayGross - platformComm, ownerPayout);

        // 4. Create Reservation
        var booking = new Booking
        {
            PropertyID = targetProp.PropertyID,
            ClientID = client.UserID,
            CheckInDate = DateTime.Today.AddDays(7),
            CheckOutDate = DateTime.Today.AddDays(9),
            BookingType = "FullDay",
            TotalAmount = totalAmount,
            SecurityDepositAmount = targetProp.SecurityDepositAmount,
            CommissionRateSnapshot = commRate,
            PlatformCommissionAmount = platformComm,
            OwnerPayoutAmount = ownerPayout,
            EventPackagePriceSnapshot = decorPrice,
            Status = "Pending",
            IdempotencyKey = $"e2e_{Guid.NewGuid():N}"
        };

        int bookingId = await bookingRepo.CreateBookingAsync(booking);
        Assert.True(bookingId > 0);

        try
        {
            // 5. Razorpay Order Creation
            var rzpOrder = await rzpService.CreateOrderAsync(totalAmount, $"b_{bookingId}", "INR");
            Assert.NotNull(rzpOrder);
            Assert.StartsWith("order_", rzpOrder.OrderId);

            // 6. Cryptographic Signature Generation & Verification
            string paymentId = $"pay_test_{Guid.NewGuid():N}".Substring(0, 18);
            string payload = $"{rzpOrder.OrderId}|{paymentId}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("rzp_secret_demo_12345"));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            string validSignature = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            bool isSignatureValid = await rzpService.VerifyPaymentSignatureAsync(rzpOrder.OrderId, paymentId, validSignature);
            Assert.True(isSignatureValid);

            // 7. Advance Booking to Confirmed & Record Payment
            await bookingRepo.UpdateBookingStatusAsync(bookingId, "Confirmed");
            int paymentRecordId = await bookingRepo.RecordPaymentAsync(new Payment
            {
                BookingID = bookingId,
                Amount = totalAmount,
                GatewayTransactionID = paymentId,
                PaymentStatus = "Success"
            });
            Assert.True(paymentRecordId > 0);

            // 8. Multi-Stakeholder WhatsApp & Email Alerts
            bool clientAlert = await commsService.SendBookingConfirmationAsync(client.Phone ?? "9876543210", client.FullName, targetProp.Title, bookingId.ToString(), totalAmount);
            bool ownerAlert = await commsService.SendOwnerBookingAlertAsync(targetProp.OwnerID.ToString(), "Host Partner", targetProp.Title, bookingId.ToString(), propertyStayGross, ownerPayout, client.FullName, booking.CheckInDate, booking.CheckOutDate, true, decorPrice);
            Assert.True(clientAlert);
            Assert.True(ownerAlert);

            // 9. Cancellation Quote & Refund Engine
            var confirmedBooking = await bookingRepo.GetBookingByIdAsync(bookingId);
            Assert.NotNull(confirmedBooking);
            var quote = refundEngine.CalculateCancellationQuote(confirmedBooking, DateTime.UtcNow);
            Assert.NotNull(quote);
            Assert.True(quote.NetRefundAmount > 0);

            // 10. Process Cancellation
            var refundResult = await refundEngine.ProcessCancellationAndRefundAsync(bookingId, client.UserID, "Client", "E2E Test Cancellation", forceOverride: true);
            Assert.NotNull(refundResult);
            Assert.Equal("Completed", refundResult.Status);

            var cancelledBooking = await bookingRepo.GetBookingByIdAsync(bookingId);
            Assert.Equal("Cancelled", cancelledBooking!.Status);
        }
        finally
        {
            await _db.ExecuteNonQueryAsync($"DELETE FROM BookingRefunds WHERE BookingID = {bookingId}");
            await _db.ExecuteNonQueryAsync($"DELETE FROM Payments WHERE BookingID = {bookingId}");
            await _db.ExecuteNonQueryAsync($"DELETE FROM Bookings WHERE BookingID = {bookingId}");
        }
    }

    [Fact]
    public async Task EndToEnd_EventManager_PackageManagement_And_SetupMilestoneProgression()
    {
        var eventRepo = new EventPackageRepository(_db);
        var userRepo = new UserRepository(_db);
        var bookingRepo = new BookingRepository(_db);

        var eventManager = await userRepo.GetByEmailAsync("eventmanager@bookmyfun.com");
        Assert.NotNull(eventManager);

        // 1. Create Event Package
        string packageName = $"Luxe Staging #{Guid.NewGuid():N}".Substring(0, 20);
        var package = new EventPackage
        {
            Name = packageName,
            Category = "Royal Celebration",
            Description = "Full poolside lighting, stage decor, and floral entryway.",
            BasePrice = 15000.00m,
            MediaUrl = "https://images.unsplash.com/photo-1519741497674-611481863552",
            IncludedItems = "LED Stage, Floral Arches, Fairy Lights",
            IsActive = true
        };

        int packageId = await eventRepo.CreatePackageAsync(package);
        Assert.True(packageId > 0);

        try
        {
            // 2. Fetch and Verify
            var fetchedPkg = await eventRepo.GetPackageByIdAsync(packageId);
            Assert.NotNull(fetchedPkg);
            Assert.Equal(packageName, fetchedPkg.Name);

            // 3. Update Package
            fetchedPkg.BasePrice = 16500.00m;
            bool updated = await eventRepo.UpdatePackageAsync(fetchedPkg);
            Assert.True(updated);

            // 4. Milestone Progression
            var allEventBookings = await eventRepo.GetAllEventBookingsAsync();
            if (allEventBookings.Any())
            {
                var targetBooking = allEventBookings.First();
                // Advance to InProgress
                bool advanced1 = await eventRepo.UpdateSetupStatusAsync(targetBooking.BookingID, "InProgress", "https://example.com/setup-prep.jpg", eventManager.UserID);
                Assert.True(advanced1);

                // Advance to Completed
                bool advanced2 = await eventRepo.UpdateSetupStatusAsync(targetBooking.BookingID, "Completed", "https://example.com/setup-finished.jpg", eventManager.UserID);
                Assert.True(advanced2);
            }
        }
        finally
        {
            await eventRepo.DeletePackageAsync(packageId);
        }
    }

    [Fact]
    public async Task EndToEnd_DamageClaim_Filing_And_Adjudication()
    {
        var bookingRepo = new BookingRepository(_db);
        var claimRepo = new DamageClaimRepository(_db);
        var propRepo = new PropertyRepository(_db);
        var userRepo = new UserRepository(_db);
        var stubMedia = new StubMediaStorageService();
        var claimService = new DamageClaimService(claimRepo, bookingRepo, propRepo, stubMedia, NullLogger<DamageClaimService>.Instance);

        var admin = await userRepo.GetByEmailAsync("admin@bookmyfun.com");
        var bookings = await bookingRepo.GetAllBookingsAsync();
        var targetBooking = bookings.FirstOrDefault();
        Assert.NotNull(targetBooking);

        // 1. Owner files damage claim
        var createModel = new DamageClaimCreateViewModel
        {
            BookingID = targetBooking.BookingID,
            ClaimAmount = 1500.00m,
            ItemizedDescription = "Broken patio glass table during pool party.",
            EvidenceUrlsInput = "https://example.com/broken-table.jpg"
        };

        int claimId = await claimService.CreateClaimAsync(createModel, admin!.UserID, isStaff: true);
        Assert.True(claimId > 0);

        try
        {
            // 2. Staff reviews and adjudicates claim
            var adjudicateModel = new DamageClaimAdjudicateViewModel
            {
                ClaimID = claimId,
                ApprovedAmount = 1200.00m,
                Action = "Approved",
                AdjudicationNotes = "Inspected contractor quote and approved replacement."
            };

            bool adjudicated = await claimService.AdjudicateClaimAsync(adjudicateModel, admin.UserID);
            Assert.True(adjudicated);

            var claim = await claimService.GetClaimByIdAsync(claimId);
            Assert.Equal("Approved", claim.Status);
            Assert.Equal(1500.00m, claim.AdjudicatedAmount);
        }
        finally
        {
            await _db.ExecuteNonQueryAsync($"DELETE FROM DamageClaims WHERE ClaimID = {claimId}");
        }
    }

    [Fact]
    public async Task EndToEnd_CustomerSupportInquiry_And_StaffAccountabilityResolution()
    {
        var contactRepo = new ContactRepository(_db);
        var userRepo = new UserRepository(_db);
        var admin = await userRepo.GetByEmailAsync("admin@bookmyfun.com");

        // 1. Customer submits inquiry
        var inquiry = new ContactUs
        {
            Name = "Aarti Sharma",
            Email = "aarti.sharma@example.com",
            Phone = "9822012345",
            Subject = "Inquiry regarding large wedding booking",
            Message = "Can we book 3 adjacent villas for 45 guests for 3 days?"
        };

        int ticketId = await contactRepo.CreateContactAsync(inquiry);
        Assert.True(ticketId > 0);

        try
        {
            // 2. Staff resolves ticket
            bool resolved = await contactRepo.UpdateStatusAsync(
                ticketId,
                "Responded",
                respondedByUserId: admin!.UserID,
                responseNotes: "Contacted customer on WhatsApp and shared customized estate cluster quotation.");

            Assert.True(resolved);

            // 3. Verify accountability fields
            var allContacts = await contactRepo.GetAllContactsAsync();
            var ticket = allContacts.FirstOrDefault(c => c.ContactID == ticketId);
            Assert.NotNull(ticket);
            Assert.Equal("Responded", ticket.Status);
            Assert.Equal(admin.UserID, ticket.RespondedByUserID);
            Assert.Equal(admin.FullName, ticket.RespondedByUserName);
            Assert.NotNull(ticket.ResponseNotes);
        }
        finally
        {
            await _db.ExecuteNonQueryAsync($"DELETE FROM ContactUs WHERE ContactID = {ticketId}");
        }
    }

    [Fact]
    public async Task EndToEnd_IdVerification_Submission_And_Review()
    {
        var idRepo = new IdVerificationRepository(_db);
        var userRepo = new UserRepository(_db);
        var idService = new IdVerificationService(idRepo, userRepo, NullLogger<IdVerificationService>.Instance);

        var client = await userRepo.GetByEmailAsync("client@bookmyfun.com");
        var admin = await userRepo.GetByEmailAsync("admin@bookmyfun.com");

        // 1. Submit KYC
        string storageKey = $"kyc_{Guid.NewGuid():N}.pdf";
        int verificationId = await idService.SubmitVerificationAsync(client!.UserID, "Aadhaar", storageKey, "XXXX-XXXX-1234");
        Assert.True(verificationId > 0);

        try
        {
            // 2. Staff approves KYC
            bool reviewed = await idService.ReviewVerificationAsync(verificationId, "Verify", admin!.UserID);
            Assert.True(reviewed);

            // 3. Verify user is now verified
            bool isVerified = await idService.IsClientVerifiedAsync(client.UserID);
            Assert.True(isVerified);
        }
        finally
        {
            await _db.ExecuteNonQueryAsync($"DELETE FROM ClientIdVerifications WHERE VerificationID = {verificationId}");
        }
    }
}
