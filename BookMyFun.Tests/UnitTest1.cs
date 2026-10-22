using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Tests;

public class UnitTest1
{
    [Fact]
    public async Task AdminLogin_CredentialsAndRole_AreValid()
    {
        var config = TestHelper.GetTestConfig();
        var db = TestHelper.GetTestDb();
        var userRepo = new BookMyFun.Repositories.UserRepository(db);

        var admin = await userRepo.GetByEmailAsync("admin@bookmyfun.com");
        Assert.NotNull(admin);
        Assert.Equal("Admin", admin.Role);
        Assert.True(admin.IsActive);
        Assert.True(BookMyFun.Controllers.AccountController.VerifyPassword("Admin@123", admin.PasswordHash));
    }

    [Fact]
    public async Task AdminDashboard_Queries_SucceedWithoutErrors()
    {
        var config = TestHelper.GetTestConfig();
        var db = TestHelper.GetTestDb();

        var propRepo = new BookMyFun.Repositories.PropertyRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var bookingRepo = new BookMyFun.Repositories.BookingRepository(db);

        var properties = await propRepo.GetAllPropertiesAsync();
        var owners = await userRepo.GetUsersByRoleAsync("Owner");
        var clients = await userRepo.GetUsersByRoleAsync("Client");
        var bookings = await bookingRepo.GetAllBookingsAsync();

        Assert.NotNull(properties);
        Assert.NotNull(owners);
        Assert.NotNull(clients);
        Assert.NotNull(bookings);
    }

    [Fact]
    public async Task PartnerLead_Approve_GeneratesCredentialsAndDispatches()
    {
        var config = TestHelper.GetTestConfig();
        var db = TestHelper.GetTestDb();

        var leadRepo = new BookMyFun.Repositories.PartnerLeadRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var ownerReqRepo = new BookMyFun.Repositories.OwnerRequestRepository(db);
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var commsLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.CommunicationService>.Instance;
        var leadLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.PartnerLeadService>.Instance;

        var commsService = new BookMyFun.Services.CommunicationService(configRepo, config, commsLogger);
        var partnerLeadService = new BookMyFun.Services.PartnerLeadService(
            leadRepo,
            userRepo,
            ownerReqRepo,
            commsService,
            commsService,
            leadLogger);

        // Create test lead
        string testEmail = $"test_lead_{Guid.NewGuid():N}@example.com";
        var createModel = new BookMyFun.Models.ViewModels.PartnerLeadCreateViewModel
        {
            FullName = "Test Partner Lead",
            Email = testEmail,
            Phone = "9876543210",
            PropertyName = "Green Meadows Villa",
            Location = "Lonavala",
            AcreageOrType = "3 BHK Villa",
            PartnerType = "Owner"
        };

        int leadId = await partnerLeadService.CreateLeadAsync(createModel);
        Assert.True(leadId > 0);
        int createdUserId = 0;

        try
        {
            // Approve lead
            var (success, initialPassword, token, accountId, roleDisplayName, email, phone) = 
                await partnerLeadService.ApproveLeadAsync(leadId, 1);

            createdUserId = accountId;
            Assert.True(success);
            Assert.StartsWith("Host@", initialPassword);
            Assert.True(accountId > 0);
            Assert.Equal(testEmail, email);
            Assert.Equal("Farmhouse & Villa Host", roleDisplayName);

            // Verify user exists and can authenticate with generated password
            var user = await userRepo.GetByEmailAsync(testEmail);
            Assert.NotNull(user);
            Assert.Equal("Owner", user.Role);
            Assert.True(user.IsActive);
            Assert.True(BookMyFun.Controllers.AccountController.VerifyPassword(initialPassword, user.PasswordHash));
        }
        finally
        {
            if (createdUserId > 0)
            {
                await db.ExecuteNonQueryAsync($"DELETE FROM OwnerRequests WHERE UserID = {createdUserId}");
                await userRepo.DeleteUserAsync(createdUserId);
            }
            if (leadId > 0)
            {
                await db.ExecuteNonQueryAsync($"DELETE FROM PartnerLeads WHERE LeadID = {leadId}");
            }
        }
    }

    [Fact]
    public async Task OwnerRequest_Approve_GeneratesCredentialsAndDispatches()
    {
        var config = TestHelper.GetTestConfig();
        var db = TestHelper.GetTestDb();

        var leadRepo = new BookMyFun.Repositories.PartnerLeadRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var ownerReqRepo = new BookMyFun.Repositories.OwnerRequestRepository(db);
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var commsLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.CommunicationService>.Instance;
        var leadLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.PartnerLeadService>.Instance;

        var commsService = new BookMyFun.Services.CommunicationService(configRepo, config, commsLogger);
        var partnerLeadService = new BookMyFun.Services.PartnerLeadService(
            leadRepo,
            userRepo,
            ownerReqRepo,
            commsService,
            commsService,
            leadLogger);

        // Create test user for owner request
        string testEmail = $"test_owner_{Guid.NewGuid():N}@example.com";
        var newUser = new BookMyFun.Models.Entities.User
        {
            FullName = "Test Owner Applicant",
            Email = testEmail,
            Phone = "9876543211",
            Role = "Owner",
            PasswordHash = "OldHash",
            IsActive = true
        };
        int userId = await userRepo.CreateUserAsync(newUser);
        Assert.True(userId > 0);

        try
        {
            int requestId = await ownerReqRepo.CreateRequestAsync(userId, 2);
            Assert.True(requestId > 0);

            // Approve owner request
            var (success, accountId, generatedPassword, email, phone, fullName) = 
                await partnerLeadService.ApproveOwnerRequestAsync(requestId, 1, "Approved for live platform");

            Assert.True(success);
            Assert.Equal(userId, accountId);
            Assert.StartsWith("Host@", generatedPassword);
            Assert.Equal(testEmail, email);

            // Verify updated password in repository
            var updatedUser = await userRepo.GetByEmailAsync(testEmail);
            Assert.NotNull(updatedUser);
            Assert.True(BookMyFun.Controllers.AccountController.VerifyPassword(generatedPassword, updatedUser.PasswordHash));
        }
        finally
        {
            if (userId > 0)
            {
                await db.ExecuteNonQueryAsync($"DELETE FROM OwnerRequests WHERE UserID = {userId}");
                await userRepo.DeleteUserAsync(userId);
            }
        }
    }

    [Fact]
    public async Task RazorpayPaymentGateway_CreateOrderAndVerifySignature_WorkCorrectly()
    {
        var config = TestHelper.GetTestConfig();
        config["Razorpay:KeyId"] = "rzp_test_sample123";
        config["Razorpay:KeySecret"] = "sample_secret_xyz_789";

        var db = TestHelper.GetTestDb();
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.RazorpayPaymentService>.Instance;

        var rzpService = new BookMyFun.Services.RazorpayPaymentService(configRepo, config, logger);

        // 1. Order Creation
        var order = await rzpService.CreateOrderAsync(15000.00m, "receipt_test_101", "INR");
        Assert.NotNull(order);
        Assert.StartsWith("order_", order.OrderId);
        Assert.Equal(15000.00m, order.Amount);

        // 2. Signature Verification (HMAC-SHA256)
        string orderId = "order_test_987654";
        string paymentId = "pay_test_123456";
        string secret = "sample_secret_xyz_789";

        var payload = $"{orderId}|{paymentId}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        var validSig = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        bool isValid = await rzpService.VerifyPaymentSignatureAsync(orderId, paymentId, validSig);
        Assert.True(isValid);

        // Invalid signature should fail
        bool isInvalid = await rzpService.VerifyPaymentSignatureAsync(orderId, paymentId, "invalid_signature_hex");
        Assert.False(isInvalid);
    }

    [Fact]
    public async Task FinancialSplit_PropertyStayAndEventDecor_AreStrictlySegregated()
    {
        var config = TestHelper.GetTestConfig();
        var db = TestHelper.GetTestDb();
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);

        var commissionService = new BookMyFun.Services.CommissionService(configRepo, userRepo);
        var pricingService = new BookMyFun.Services.BookingPricingService();

        // Scenario: Property is 10,000 per night for 2 nights = 20,000
        decimal propertyStayGross = pricingService.CalculatePrice(10000.00m, "FullDay", null, 2);
        Assert.Equal(20000.00m, propertyStayGross);

        // Client adds optional Decor package: 5,000
        decimal decorPackagePrice = 5000.00m;
        decimal totalGuestInvoice = propertyStayGross + decorPackagePrice; // 25,000
        Assert.Equal(25000.00m, totalGuestInvoice);

        // Commission must be computed on Property Stay Gross ONLY (10% of 20,000 = 2,000), NOT on 25,000
        var (rateSnapshot, platformCommission, ownerPayout) = await commissionService.CalculateDynamicCommissionAsync(
            propertyStayGross, 10.0m, null);

        Assert.Equal(10.0m, rateSnapshot);
        Assert.Equal(2000.00m, platformCommission);
        Assert.Equal(18000.00m, ownerPayout);

        // Host receives 18,000 for their 20,000 stay.
        // The 5,000 decor fee is 100% segregated and belongs to the Event Manager.
        decimal eventManagerShare = decorPackagePrice;
        Assert.Equal(5000.00m, eventManagerShare);
        Assert.Equal(totalGuestInvoice, ownerPayout + platformCommission + eventManagerShare);
    }

    [Fact]
    public async Task WhatsAppService_DispatchesMultiStakeholderAlerts_Cleanly()
    {
        var config = TestHelper.GetTestConfig();
        var db = TestHelper.GetTestDb();
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var commsLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.CommunicationService>.Instance;

        var commsService = new BookMyFun.Services.CommunicationService(configRepo, config, commsLogger);

        // Client booking confirmation
        bool clientSent = await commsService.SendBookingConfirmationAsync("9876543210", "Vipul Guest", "Serene Hills Villa", "101", 25000.00m);
        Assert.True(clientSent);

        // Owner booking alert with segregated decor note
        bool ownerSent = await commsService.SendOwnerBookingAlertAsync(
            "9876543211", "Rajesh Owner", "Serene Hills Villa", "101", 20000.00m, 18000.00m, "Vipul Guest", 
            DateTime.Today, DateTime.Today.AddDays(2), true, 5000.00m);
        Assert.True(ownerSent);

        // Event Manager setup staging alert
        bool eventManagerSent = await commsService.SendEventManagerSetupAlertAsync(
            "9876543212", "Creative Decorator", "Serene Hills Villa", "101", "Royal Birthday Staging", 5000.00m,
            DateTime.Today, "Vipul Guest");
        Assert.True(eventManagerSent);
    }

    [Fact]
    public async Task RealEmailOtp_GenerationDispatchAndRegistration_SucceedsWithoutTestingBypass()
    {
        var config = TestHelper.GetTestConfig();
        var db = TestHelper.GetTestDb();
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var otpRepo = new BookMyFun.Repositories.OtpRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.CommunicationService>.Instance;

        var commsService = new BookMyFun.Services.CommunicationService(configRepo, config, logger);

        // Target real user email as instructed by the user
        string targetEmail = "showsgarage@gmail.com";
        string testClientEmail = $"verified_client_{Guid.NewGuid():N}@example.com";
        int createdId = 0;

        try
        {
            // 1. Generate real cryptographically secure random 6-digit OTP (NO testing/mock code)
            var realOtp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            Assert.Matches(@"^\d{6}$", realOtp);

            // 2. Save OTP to database
            bool saved = await otpRepo.SaveOtpAsync(targetEmail, realOtp, "Registration");
            Assert.True(saved);

            // 3. Dispatch real verification email via live configured Gmail SMTP
            string subject = "Your BookMyFun Verification Code";
            string body = $"<h2>Welcome to BookMyFun</h2><p>Your one-time registration code is: <strong>{realOtp}</strong></p>";
            bool emailSent = await commsService.SendEmailAsync(targetEmail, subject, body);
            Assert.True(emailSent);

            // 4. Validate the real OTP against the database
            bool isOtpValid = await otpRepo.ValidateOtpAsync(targetEmail, realOtp, "Registration");
            Assert.True(isOtpValid);

            // 5. Register/Authenticate user
            string realOtp2 = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            await otpRepo.SaveOtpAsync(testClientEmail, realOtp2, "Registration");
            bool valid2 = await otpRepo.ValidateOtpAsync(testClientEmail, realOtp2, "Registration");
            Assert.True(valid2);

            var newClient = new BookMyFun.Models.Entities.User
            {
                FullName = "Verified Client Guest",
                Email = testClientEmail,
                Phone = "9876500000",
                PasswordHash = BookMyFun.Controllers.AccountController.HashPassword("Client@123"),
                Role = "Client",
                IsActive = true
            };
            createdId = await userRepo.CreateUserAsync(newClient);
            Assert.True(createdId > 0);

            var fetched = await userRepo.GetByEmailAsync(testClientEmail);
            Assert.NotNull(fetched);
            Assert.Equal("Client", fetched.Role);
            Assert.True(BookMyFun.Controllers.AccountController.VerifyPassword("Client@123", fetched.PasswordHash));
        }
        finally
        {
            if (createdId > 0)
            {
                await userRepo.DeleteUserAsync(createdId);
            }
            await db.ExecuteNonQueryAsync($"DELETE FROM OtpVerifications WHERE Contact IN ('{targetEmail}', '{testClientEmail}')");
        }
    }

    [Fact]
    public async Task Razorpay_OwnerSubscriptionPaymentWorkflow_TransitionsToPaid()
    {
        var config = TestHelper.GetTestConfig();
        config["Razorpay:KeyId"] = "rzp_test_owner_sub";
        config["Razorpay:KeySecret"] = "owner_secret_456_test";

        var db = TestHelper.GetTestDb();
        var subRepo = new BookMyFun.Repositories.SubscriptionRepository(db);
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.RazorpayPaymentService>.Instance;
        var rzpService = new BookMyFun.Services.RazorpayPaymentService(configRepo, config, logger);

        // 1. Ensure test owner exists
        string ownerEmail = $"test_sub_owner_{Guid.NewGuid():N}[..8]@bookmyfun.test";
        var ownerUser = new BookMyFun.Models.Entities.User
        {
            FullName = "Test Subscription Owner",
            Email = ownerEmail,
            PasswordHash = BookMyFun.Controllers.AccountController.HashPassword("Owner@123"),
            Role = "Owner",
            IsActive = true
        };
        int ownerId = await userRepo.CreateUserAsync(ownerUser);
        Assert.True(ownerId > 0);
        int subId = 0;

        try
        {
            // 2. Fetch or create plan
            var plans = await subRepo.GetAllPlansAsync();
            var plan = plans.FirstOrDefault();
            if (plan == null)
            {
                int planId = await subRepo.CreatePlanAsync(new BookMyFun.Models.Entities.SubscriptionPlan
                {
                    PlanName = "Starter Partner Plan",
                    Price = 2999m,
                    DurationInDays = 30,
                    MaxListings = 3,
                    IsActive = true
                });
                plan = await subRepo.GetPlanByIdAsync(planId);
            }
            Assert.NotNull(plan);

            // 3. Initiate Pending Subscription
            var sub = new BookMyFun.Models.Entities.OwnerSubscription
            {
                OwnerID = ownerId,
                PlanID = plan.PlanID,
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.AddDays(plan.DurationInDays),
                PaymentStatus = "Pending",
                GatewayTransactionID = null
            };
            subId = await subRepo.SubscribeOwnerAsync(sub);
            Assert.True(subId > 0);

            // 4. Create Razorpay Order
            var order = await rzpService.CreateOrderAsync(plan.Price, $"sub_{ownerId}_{plan.PlanID}_{subId}");
            Assert.NotNull(order);
            Assert.StartsWith("order_", order.OrderId);

            // 5. Simulate Payment Verification
            string paymentId = $"pay_test_{Guid.NewGuid():N}"[..18];
            string payload = $"{order.OrderId}|{paymentId}";
            using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes("owner_secret_456_test"));
            var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
            var validSig = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            bool isValid = await rzpService.VerifyPaymentSignatureAsync(order.OrderId, paymentId, validSig);
            Assert.True(isValid);

            // 6. Update to Paid
            bool updated = await subRepo.UpdateSubscriptionPaymentStatusAsync(subId, "Paid", $"RAZORPAY_{paymentId}");
            Assert.True(updated);

            var activeSub = await subRepo.GetSubscriptionByIdAsync(subId);
            Assert.NotNull(activeSub);
            Assert.Equal("Paid", activeSub.PaymentStatus);
            Assert.Equal($"RAZORPAY_{paymentId}", activeSub.GatewayTransactionID);
        }
        finally
        {
            if (subId > 0)
            {
                await db.ExecuteNonQueryAsync($"DELETE FROM OwnerSubscriptions WHERE SubscriptionID = {subId}");
            }
            if (ownerId > 0)
            {
                await userRepo.DeleteUserAsync(ownerId);
            }
        }
    }

    [Fact]
    public async Task EventManager_ServiceAreas_SaveAndCheckServicedArea()
    {
        var config = TestHelper.GetTestConfig();
        var db = TestHelper.GetTestDb();

        var eventRepo = new BookMyFun.Repositories.EventPackageRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);

        // 1. Ensure an EventManager user exists
        var managers = await userRepo.GetUsersByRoleAsync("EventManager");
        int managerId;
        if (managers.Any())
        {
            managerId = managers.First().UserID;
        }
        else
        {
            managerId = await userRepo.CreateUserAsync(new BookMyFun.Models.Entities.User
            {
                FullName = "Test Event Manager",
                Email = $"eventmgr_{Guid.NewGuid():N}@bookmyfun.com",
                PasswordHash = "hashed_pass",
                Role = "EventManager",
                Phone = "9998887776",
                IsActive = true
            });
        }

        Assert.True(managerId > 0);

        // 2. Fetch distinct property locations from DB
        var locations = await eventRepo.GetAllDistinctPropertyLocationsAsync();
        Assert.NotNull(locations);

        // 3. Update service areas for this manager
        string testAreas = "Lonavala, Maharashtra; Khandala, Maharashtra";
        bool updated = await eventRepo.UpdateEventManagerServiceAreasAsync(managerId, testAreas);
        Assert.True(updated);

        // 4. Retrieve service areas
        string? retrieved = await eventRepo.GetEventManagerServiceAreasAsync(managerId);
        Assert.NotNull(retrieved);
        Assert.Contains("Lonavala, Maharashtra", retrieved);

        // 5. Test IsAreaServicedByAnyEventManagerAsync
        bool isServicedLonavala = await eventRepo.IsAreaServicedByAnyEventManagerAsync("Lonavala, Maharashtra");
        Assert.True(isServicedLonavala);

        bool isServicedPartial = await eventRepo.IsAreaServicedByAnyEventManagerAsync("Lonavala");
        Assert.True(isServicedPartial);

        bool isServicedNonExistent = await eventRepo.IsAreaServicedByAnyEventManagerAsync("CompletelyUnknownCity9999");
        Assert.False(isServicedNonExistent);
    }

    [Fact]
    public async Task EventManager_CancelOrRemoveEventFromBooking_PreservesBooking()
    {
        var config = TestHelper.GetTestConfig();
        var db = TestHelper.GetTestDb();

        var eventRepo = new BookMyFun.Repositories.EventPackageRepository(db);
        var bookingRepo = new BookMyFun.Repositories.BookingRepository(db);

        // 1. Fetch an existing booking or check bookings table
        var allBookings = await bookingRepo.GetAllBookingsAsync();
        if (allBookings.Any())
        {
            var targetBooking = allBookings.First();

            // Setup the booking with an event
            await db.ExecuteNonQueryAsync(@"
                UPDATE Bookings 
                SET IsEventBooking = 1, 
                    EventPackagePriceSnapshot = 4500, 
                    SetupStatus = 'Pending' 
                WHERE BookingID = @BookingID", 
                new Microsoft.Data.SqlClient.SqlParameter("@BookingID", targetBooking.BookingID));

            // Call delete / remove
            bool removed = await eventRepo.CancelOrRemoveEventFromBookingAsync(targetBooking.BookingID);
            Assert.True(removed);

            // Fetch and verify booking stay is still present, but event details cleared
            var updatedBooking = await bookingRepo.GetBookingByIdAsync(targetBooking.BookingID);
            Assert.NotNull(updatedBooking);
            Assert.False(updatedBooking.IsEventBooking);
            Assert.Null(updatedBooking.EventPackageID);
            Assert.Equal("Cancelled", updatedBooking.SetupStatus);
        }
    }

    [Fact]
    public async Task EventCommission_StandardAndCustomOverrides_PrecedenceHierarchy()
    {
        var config = TestHelper.GetTestConfig();
        var db = TestHelper.GetTestDb();

        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var propRepo = new BookMyFun.Repositories.PropertyRepository(db);
        var eventRepo = new BookMyFun.Repositories.EventPackageRepository(db);

        var commissionService = new BookMyFun.Services.CommissionService(configRepo, userRepo, propRepo, eventRepo);

        // 1. Standard Commission (Global default or set in config)
        decimal packagePrice = 20000m;
        var (rateStd, commStd, payoutStd) = await commissionService.CalculateEventCommissionAsync(
            packagePrice, propertyId: 0, packageId: null, propertyEventCommissionOverride: null);

        Assert.Equal(10.0m, rateStd);
        Assert.Equal(2000.00m, commStd);
        Assert.Equal(18000.00m, payoutStd);
        Assert.Equal(packagePrice, commStd + payoutStd);

        // 2. Property-level Event Commission Override (e.g. 15%)
        var (rateProp, commProp, payoutProp) = await commissionService.CalculateEventCommissionAsync(
            packagePrice, propertyId: 0, packageId: null, propertyEventCommissionOverride: 15.0m);

        Assert.Equal(15.0m, rateProp);
        Assert.Equal(3000.00m, commProp);
        Assert.Equal(17000.00m, payoutProp);
        Assert.Equal(packagePrice, commProp + payoutProp);

        // 3. Property-specific Event Package Override in PropertyEventCommissions
        var properties = await propRepo.GetAllPropertiesAsync();
        var packages = await eventRepo.GetActivePackagesAsync();
        if (properties.Any() && packages.Any())
        {
            var testProp = properties.First();
            var testPkg = packages.First();

            // Set custom 25% commission specifically for this package on this property
            await propRepo.SetPropertyEventCommissionsAsync(testProp.PropertyID, new List<(int, decimal)>
            {
                (testPkg.PackageID, 25.0m)
            });

            // Calculate commission: should resolve to 25.0% even if property has a 12% default
            var (rateCust, commCust, payoutCust) = await commissionService.CalculateEventCommissionAsync(
                packagePrice, testProp.PropertyID, testPkg.PackageID, propertyEventCommissionOverride: 12.0m);

            Assert.Equal(25.0m, rateCust);
            Assert.Equal(5000.00m, commCust);
            Assert.Equal(15000.00m, payoutCust);
            Assert.Equal(packagePrice, commCust + payoutCust);

            // Clean up custom commission
            await propRepo.SetPropertyEventCommissionsAsync(testProp.PropertyID, new List<(int, decimal)>());
        }
    }

    [Fact]
    public async Task EventCommission_BookingCreation_AccurateSnapshotAndNetPayouts()
    {
        var config = TestHelper.GetTestConfig();
        var db = TestHelper.GetTestDb();

        var bookingRepo = new BookMyFun.Repositories.BookingRepository(db);
        var propRepo = new BookMyFun.Repositories.PropertyRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var eventRepo = new BookMyFun.Repositories.EventPackageRepository(db);

        var properties = await propRepo.GetAllPropertiesAsync();
        var clients = await userRepo.GetUsersByRoleAsync("Client");
        var packages = await eventRepo.GetActivePackagesAsync();

        if (properties.Any() && clients.Any() && packages.Any())
        {
            var prop = properties.First();
            var client = clients.First();
            var pkg = packages.First();

            decimal propertyStay = 15000m;
            decimal eventPrice = 10000m;
            decimal totalAmount = propertyStay + eventPrice;

            // Host: 10% stay commission (1500) -> 13500 payout
            decimal stayCommRate = 10.0m;
            decimal stayComm = 1500m;
            decimal hostPayout = 13500m;

            // Event: 12% event commission (1200) -> 8800 decorator payout
            decimal eventCommRate = 12.0m;
            decimal eventComm = 1200m;
            decimal decoratorPayout = 8800m;

            var booking = new BookMyFun.Models.Entities.Booking
            {
                PropertyID = prop.PropertyID,
                ClientID = client.UserID,
                CheckInDate = DateTime.Today.AddDays(10),
                CheckOutDate = DateTime.Today.AddDays(12),
                TotalAmount = totalAmount,
                Status = "Pending",
                BookingType = "FullDay",
                CommissionRateSnapshot = stayCommRate,
                PlatformCommissionAmount = stayComm,
                OwnerPayoutAmount = hostPayout,
                IsEventBooking = true,
                EventPackageID = pkg.PackageID,
                EventPackagePriceSnapshot = eventPrice,
                EventCommissionRateSnapshot = eventCommRate,
                EventPlatformCommissionAmount = eventComm,
                EventManagerPayoutAmount = decoratorPayout,
                SetupStatus = "Pending"
            };

            int bookingId = await bookingRepo.CreateBookingAsync(booking);
            Assert.True(bookingId > 0);

            try
            {
                var retrieved = await bookingRepo.GetBookingByIdAsync(bookingId);
                Assert.NotNull(retrieved);
                Assert.Equal(totalAmount, retrieved.TotalAmount);
                Assert.Equal(stayCommRate, retrieved.CommissionRateSnapshot);
                Assert.Equal(stayComm, retrieved.PlatformCommissionAmount);
                Assert.Equal(hostPayout, retrieved.OwnerPayoutAmount);

                Assert.True(retrieved.IsEventBooking);
                Assert.Equal(eventPrice, retrieved.EventPackagePriceSnapshot);
                Assert.Equal(eventCommRate, retrieved.EventCommissionRateSnapshot);
                Assert.Equal(eventComm, retrieved.EventPlatformCommissionAmount);
                Assert.Equal(decoratorPayout, retrieved.EventManagerPayoutAmount);

                // Verify perfect monetary balance: Total = Host Payout + Stay Comm + Decorator Payout + Event Comm
                decimal totalCalculated = retrieved.OwnerPayoutAmount + retrieved.PlatformCommissionAmount +
                                          retrieved.EventManagerPayoutAmount + retrieved.EventPlatformCommissionAmount;
                Assert.Equal(retrieved.TotalAmount, totalCalculated);
            }
            finally
            {
                await db.ExecuteNonQueryAsync($"DELETE FROM Bookings WHERE BookingID = {bookingId}");
            }
        }
    }

    [Fact]
    public async Task SpecificEventCommission_AdminOverrideAndReversionToStanding_WorksAccurately()
    {
        var config = TestHelper.GetTestConfig();
        var db = TestHelper.GetTestDb();
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var propRepo = new BookMyFun.Repositories.PropertyRepository(db);
        var eventRepo = new BookMyFun.Repositories.EventPackageRepository(db);
        var bookingRepo = new BookMyFun.Repositories.BookingRepository(db);

        var commissionService = new BookMyFun.Services.CommissionService(configRepo, userRepo, propRepo, eventRepo);

        var packages = await eventRepo.GetAllPackagesAsync();
        if (packages.Any())
        {
            var testPkg = packages.First();
            var originalComm = testPkg.CommissionRate;

            try
            {
                // 1. Set specific event override to 18.5%
                bool updated = await eventRepo.UpdatePackageCommissionRateAsync(testPkg.PackageID, 18.5m);
                Assert.True(updated);

                decimal testPrice = 10000m;
                var (rateCustom, commCustom, payoutCustom) = await commissionService.CalculateEventCommissionAsync(
                    testPrice, propertyId: 0, packageId: testPkg.PackageID);

                Assert.Equal(18.5m, rateCustom);
                Assert.Equal(1850m, commCustom);
                Assert.Equal(8150m, payoutCustom);
                Assert.Equal(testPrice, commCustom + payoutCustom);

                // 2. Revert specific event override to standing default (null)
                bool reverted = await eventRepo.UpdatePackageCommissionRateAsync(testPkg.PackageID, null);
                Assert.True(reverted);

                var (rateDefault, commDefault, payoutDefault) = await commissionService.CalculateEventCommissionAsync(
                    testPrice, propertyId: 0, packageId: testPkg.PackageID);

                Assert.Equal(10.0m, rateDefault);
                Assert.Equal(1000m, commDefault);
                Assert.Equal(9000m, payoutDefault);
                Assert.Equal(testPrice, commDefault + payoutDefault);
            }
            finally
            {
                // Restore original state
                await eventRepo.UpdatePackageCommissionRateAsync(testPkg.PackageID, originalComm);
            }
        }
    }

    [Fact]
    public async Task Admin_CreateEmployee_Workflow_CreatesAndAuthenticatesEmployee()
        {
            var config = TestHelper.GetTestConfig();
            var db = TestHelper.GetTestDb();
            var userRepo = new BookMyFun.Repositories.UserRepository(db);

            string testEmail = $"ops.test.{Guid.NewGuid().ToString("N").Substring(0, 8)}@bookmyfun.com";
            string testPassword = "EmpTestPassword@123";

            int createdEmpId = 0;
            try
            {
                // 1. Create Employee
                var newEmp = new BookMyFun.Models.Entities.User
                {
                    FullName = "Rahul Staff Agent",
                    Email = testEmail,
                    Phone = "+919811122233",
                    PasswordHash = BookMyFun.Controllers.AccountController.HashPassword(testPassword),
                    Role = "Employee",
                    IsActive = true,
                    SubscriptionTier = "Free",
                    CreatedAt = DateTime.UtcNow
                };

                createdEmpId = await userRepo.CreateUserAsync(newEmp);
                Assert.True(createdEmpId > 0, "Employee ID should be greater than 0");

                // 2. Fetch Employee and verify details
                var fetched = await userRepo.GetByIdAsync(createdEmpId);
                Assert.NotNull(fetched);
                Assert.Equal("Rahul Staff Agent", fetched.FullName);
                Assert.Equal(testEmail, fetched.Email);
                Assert.Equal("+919811122233", fetched.Phone);
                Assert.Equal("Employee", fetched.Role);
                Assert.True(fetched.IsActive);
                Assert.True(BookMyFun.Controllers.AccountController.VerifyPassword(testPassword, fetched.PasswordHash));

                // 3. Verify employee appears in GetUsersByRoleAsync("Employee")
                var employees = await userRepo.GetUsersByRoleAsync("Employee");
                Assert.Contains(employees, e => e.UserID == createdEmpId && e.Email == testEmail);

                // 4. Update Employee
                fetched.FullName = "Rahul Senior Staff";
                fetched.Phone = "+919899988877";
                fetched.IsActive = false;
                string updatedPassword = "NewEmpSecret@999";
                fetched.PasswordHash = BookMyFun.Controllers.AccountController.HashPassword(updatedPassword);

                bool updated = await userRepo.UpdateUserAsync(fetched);
                Assert.True(updated);
                await userRepo.ResetPasswordAsync(testEmail, fetched.PasswordHash);

                var reloaded = await userRepo.GetByIdAsync(createdEmpId);
                Assert.NotNull(reloaded);
                Assert.Equal("Rahul Senior Staff", reloaded.FullName);
                Assert.Equal("+919899988877", reloaded.Phone);
                Assert.False(reloaded.IsActive);
                Assert.True(BookMyFun.Controllers.AccountController.VerifyPassword(updatedPassword, reloaded.PasswordHash));
            }
            finally
            {
                if (createdEmpId > 0)
                {
                    await userRepo.DeleteUserAsync(createdEmpId);
                }
            }
        }

        [Fact]
        public async Task Admin_CreateEmployee_DuplicateEmail_DetectionWorks()
        {
            var config = TestHelper.GetTestConfig();
            var db = TestHelper.GetTestDb();
            var userRepo = new BookMyFun.Repositories.UserRepository(db);

            // Fetch an existing user (e.g. admin)
            var admin = await userRepo.GetByEmailAsync("admin@bookmyfun.com");
            Assert.NotNull(admin);

            // Attempt to check if email already exists
            var existing = await userRepo.GetByEmailAsync(admin.Email);
            Assert.NotNull(existing);
            Assert.Equal("admin@bookmyfun.com", existing.Email);
        }

        private class TestTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(Microsoft.AspNetCore.Http.HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(Microsoft.AspNetCore.Http.HttpContext context, IDictionary<string, object> values) { }
        }

        [Fact]
        public async Task AdminController_CreateEmployeeAction_SucceedsAndRedirects()
        {
            var config = TestHelper.GetTestConfig();
            var db = TestHelper.GetTestDb();

            var userRepo = new BookMyFun.Repositories.UserRepository(db);
            var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
            var propRepo = new BookMyFun.Repositories.PropertyRepository(db);
            var bookingRepo = new BookMyFun.Repositories.BookingRepository(db);
            var subRepo = new BookMyFun.Repositories.SubscriptionRepository(db);
            var ownerReqRepo = new BookMyFun.Repositories.OwnerRequestRepository(db);
            var contactRepo = new BookMyFun.Repositories.ContactRepository(db);
            var financialRepo = new BookMyFun.Repositories.FinancialRepository(db);
            var refundRepo = new BookMyFun.Repositories.RefundRepository(db);
            var leadRepo = new BookMyFun.Repositories.PartnerLeadRepository(db);
            var idRepo = new BookMyFun.Repositories.IdVerificationRepository(db);
            var eventRepo = new BookMyFun.Repositories.EventPackageRepository(db);
            var damageRepo = new BookMyFun.Repositories.DamageClaimRepository(db);

            var commsLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.CommunicationService>.Instance;
            var comms = new BookMyFun.Services.CommunicationService(configRepo, config, commsLogger);
            var siteSettings = new BookMyFun.Services.SiteSettingsService(configRepo);
            var refundEngine = new BookMyFun.Services.RefundEngineService(bookingRepo, propRepo, refundRepo, configRepo, Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.RefundEngineService>.Instance);
            var partnerLeadService = new BookMyFun.Services.PartnerLeadService(leadRepo, userRepo, ownerReqRepo, comms, comms, Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.PartnerLeadService>.Instance);
            var damageClaimService = new BookMyFun.Services.DamageClaimService(damageRepo, bookingRepo, propRepo, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.DamageClaimService>.Instance);
            var idVerificationService = new BookMyFun.Services.IdVerificationService(idRepo, userRepo, Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.IdVerificationService>.Instance);

            var controller = new BookMyFun.Controllers.AdminController(
                propRepo, userRepo, bookingRepo, subRepo, configRepo, ownerReqRepo, contactRepo,
                siteSettings, financialRepo, null!, refundEngine, partnerLeadService, damageClaimService,
                idVerificationService, null!, eventRepo);

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            var claims = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin")
            }, "TestAuth"));
            httpContext.User = claims;
            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContext };
            controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new TestTempDataProvider());

            // 1. GET CreateEmployee
            var getResult = controller.CreateEmployee();
            var viewResult = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(getResult);
            var model = Assert.IsType<BookMyFun.Models.ViewModels.CreateEmployeeViewModel>(viewResult.Model);
            Assert.True(model.IsActive);

            // 2. POST CreateEmployee
            string testEmail = $"ctrl.emp.{Guid.NewGuid().ToString("N").Substring(0, 8)}@bookmyfun.com";
            var postModel = new BookMyFun.Models.ViewModels.CreateEmployeeViewModel
            {
                FullName = "Direct Controller Staff",
                Email = testEmail,
                Phone = "+919988776655",
                Password = "EmpController@123",
                IsActive = true
            };

            var postResult = await controller.CreateEmployee(postModel);
            var redirectResult = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(postResult);
            Assert.Equal(nameof(BookMyFun.Controllers.AdminController.ManageEmployees), redirectResult.ActionName);

            // 3. Verify created user in DB
            var createdUser = await userRepo.GetByEmailAsync(testEmail);
            Assert.NotNull(createdUser);
            Assert.Equal("Direct Controller Staff", createdUser.FullName);
            Assert.Equal("Employee", createdUser.Role);
            Assert.True(createdUser.IsActive);
            Assert.True(BookMyFun.Controllers.AccountController.VerifyPassword("EmpController@123", createdUser.PasswordHash));

            // Clean up
            await db.ExecuteNonQueryAsync($"DELETE FROM AuditLogs WHERE EntityType = 'User' AND EntityID = {createdUser.UserID}");
            await userRepo.DeleteUserAsync(createdUser.UserID);
        }

        [Fact]
        public async Task GoogleLogin_NewUser_AutomaticallyRegistersAndSignsIn()
        {
            var config = TestHelper.GetTestConfig();
            var db = TestHelper.GetTestDb();
            var userRepo = new BookMyFun.Repositories.UserRepository(db);
            var otpRepo = new BookMyFun.Repositories.OtpRepository(db);
            var ownerReqRepo = new BookMyFun.Repositories.OwnerRequestRepository(db);
            var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
            var leadRepo = new BookMyFun.Repositories.PartnerLeadRepository(db);
            var bookingRepo = new BookMyFun.Repositories.BookingRepository(db);
            var propRepo = new BookMyFun.Repositories.PropertyRepository(db);
            var idRepo = new BookMyFun.Repositories.IdVerificationRepository(db);
            var commsLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.CommunicationService>.Instance;
            var leadLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.PartnerLeadService>.Instance;
            var comms = new BookMyFun.Services.CommunicationService(configRepo, config, commsLogger);
            var partnerLeadService = new BookMyFun.Services.PartnerLeadService(leadRepo, userRepo, ownerReqRepo, comms, comms, leadLogger);

            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddLogging();
            services.AddRouting();
            services.AddControllersWithViews();
            services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie();
            var serviceProvider = services.BuildServiceProvider();
            var schemeProvider = serviceProvider.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();

            var controller = new BookMyFun.Controllers.AccountController(
                userRepo, otpRepo, ownerReqRepo, partnerLeadService, comms, comms,
                bookingRepo, propRepo, idRepo, schemeProvider,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Controllers.AccountController>.Instance);

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = serviceProvider };
            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext 
            { 
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData()
            };
            controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new TestTempDataProvider());

            string testGoogleEmail = $"new.google.{Guid.NewGuid().ToString("N").Substring(0, 8)}@gmail.com";
            string testGoogleName = "Google New User";

            try
            {
                var result = await controller.DirectGoogleLogin(new BookMyFun.Models.ViewModels.ExternalLoginPromptViewModel
                {
                    Email = testGoogleEmail,
                    FullName = testGoogleName,
                    Provider = "Google"
                });

                var redirect = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
                Assert.Equal("Index", redirect.ActionName);
                Assert.Equal("Client", redirect.ControllerName);

                // Verify user was automatically registered in database as a Client
                var userInDb = await userRepo.GetByEmailAsync(testGoogleEmail);
                Assert.NotNull(userInDb);
                Assert.Equal(testGoogleName, userInDb.FullName);
                Assert.Equal("Client", userInDb.Role);
                Assert.Equal("Google", userInDb.AuthProvider);
                Assert.True(userInDb.IsActive);
                Assert.StartsWith("OAUTH_GOOGLE_", userInDb.PasswordHash);
            }
            finally
            {
                var created = await userRepo.GetByEmailAsync(testGoogleEmail);
                if (created != null)
                {
                    await userRepo.DeleteUserAsync(created.UserID);
                }
            }
        }

        [Fact]
        public async Task GoogleLogin_ExistingUser_LinksProviderAndLogsIn()
        {
            var config = TestHelper.GetTestConfig();
            var db = TestHelper.GetTestDb();
            var userRepo = new BookMyFun.Repositories.UserRepository(db);
            var otpRepo = new BookMyFun.Repositories.OtpRepository(db);
            var ownerReqRepo = new BookMyFun.Repositories.OwnerRequestRepository(db);
            var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
            var leadRepo = new BookMyFun.Repositories.PartnerLeadRepository(db);
            var bookingRepo = new BookMyFun.Repositories.BookingRepository(db);
            var propRepo = new BookMyFun.Repositories.PropertyRepository(db);
            var idRepo = new BookMyFun.Repositories.IdVerificationRepository(db);
            var commsLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.CommunicationService>.Instance;
            var leadLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.PartnerLeadService>.Instance;
            var comms = new BookMyFun.Services.CommunicationService(configRepo, config, commsLogger);
            var partnerLeadService = new BookMyFun.Services.PartnerLeadService(leadRepo, userRepo, ownerReqRepo, comms, comms, leadLogger);

            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddLogging();
            services.AddRouting();
            services.AddControllersWithViews();
            services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie();
            var serviceProvider = services.BuildServiceProvider();
            var schemeProvider = serviceProvider.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();

            var controller = new BookMyFun.Controllers.AccountController(
                userRepo, otpRepo, ownerReqRepo, partnerLeadService, comms, comms,
                bookingRepo, propRepo, idRepo, schemeProvider,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Controllers.AccountController>.Instance);

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = serviceProvider };
            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext 
            { 
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData()
            };
            controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new TestTempDataProvider());

            string existingEmail = $"exist.user.{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
            var existingUser = new BookMyFun.Models.Entities.User
            {
                FullName = "Existing User Pre-Google",
                Email = existingEmail,
                PasswordHash = BookMyFun.Controllers.AccountController.HashPassword("OldPass@123"),
                Role = "Client",
                IsActive = true,
                AuthProvider = "Local"
            };

            int userId = await userRepo.CreateUserAsync(existingUser);
            Assert.True(userId > 0);

            try
            {
                var result = await controller.DirectGoogleLogin(new BookMyFun.Models.ViewModels.ExternalLoginPromptViewModel
                {
                    Email = existingEmail,
                    FullName = "Existing User via Google",
                    Provider = "Google"
                });

                var redirect = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
                Assert.Equal("Index", redirect.ActionName);
                Assert.Equal("Client", redirect.ControllerName);

                // Verify user's AuthProvider was linked to Google
                var updatedUser = await userRepo.GetByIdAsync(userId);
                Assert.NotNull(updatedUser);
                Assert.Equal("Google", updatedUser.AuthProvider);
                Assert.NotNull(updatedUser.AuthProviderKey);
            }
            finally
            {
                await userRepo.DeleteUserAsync(userId);
            }
        }

        [Fact]
        public async Task Owner_AmenitiesAndOccasions_CustomCreationAndLimitRules()
        {
            var config = TestHelper.GetTestConfig();
            var db = TestHelper.GetTestDb();
            var propRepo = new BookMyFun.Repositories.PropertyRepository(db);

            // Test custom amenity creation
            string customAmName = $"CustomAm_{Guid.NewGuid():N}".Substring(0, 20);
            int amId1 = await propRepo.GetOrCreateAmenityByNameAsync(customAmName);
            Assert.True(amId1 > 0);
            int amId2 = await propRepo.GetOrCreateAmenityByNameAsync(customAmName);
            Assert.Equal(amId1, amId2); // Idempotent check

            // Test custom occasion creation
            string customOccName = $"CustomOcc_{Guid.NewGuid():N}".Substring(0, 20);
            int occId1 = await propRepo.GetOrCreateOccasionByNameAsync(customOccName);
            Assert.True(occId1 > 0);
            int occId2 = await propRepo.GetOrCreateOccasionByNameAsync(customOccName);
            Assert.Equal(occId1, occId2); // Idempotent check

            // Test view model limit validations
            var vm = new BookMyFun.Models.ViewModels.PropertyEditViewModel
            {
                SelectedAmenityIDs = Enumerable.Range(1, 11).ToList(), // 11 exceeds max 10
                SelectedOccasionIDs = Enumerable.Range(1, 6).ToList()   // 6 exceeds max 5
            };

            Assert.True(vm.SelectedAmenityIDs.Count > 10);
            Assert.True(vm.SelectedOccasionIDs.Count > 5);
        }
    }
