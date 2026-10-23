using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class ReceiptDispatchIntegrationTests
    {
        private class SimpleTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class MockAuditConfigRepo : IConfigRepository
        {
            public readonly List<(int UserId, string Action, string EntityType, int? EntityId, string? Details)> Logs = new();

            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
            public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());

            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details)
            {
                Logs.Add((performedByUserId, action, entityType, entityId, details));
                return Task.CompletedTask;
            }

            public Task<List<AuditLog>> GetRecentAuditLogsAsync(int count = 50) => Task.FromResult(new List<AuditLog>());
        }

        private class MockBookingRepo : IBookingRepository
        {
            public readonly Dictionary<int, Booking> Bookings = new();
            public readonly Dictionary<int, List<Payment>> Payments = new();

            public Task<Booking?> GetByIdAsync(int bookingId)
            {
                Bookings.TryGetValue(bookingId, out var b);
                return Task.FromResult(b);
            }

            public Task<Booking?> GetBookingByIdAsync(int bookingId) => GetByIdAsync(bookingId);

            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId)
            {
                if (Payments.TryGetValue(bookingId, out var p)) return Task.FromResult(p);
                return Task.FromResult(new List<Payment>());
            }

            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(Bookings.Values.Where(b => b.ClientID == clientId).ToList());
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(Bookings.Values.ToList());
            public Task<int> CreateBookingAsync(Booking booking)
            {
                booking.BookingID = Bookings.Count + 1;
                Bookings[booking.BookingID] = booking;
                return Task.FromResult(booking.BookingID);
            }
            public Task<bool> UpdateBookingStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> CancelBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<int> RecordPaymentAsync(Payment payment)
            {
                if (!Payments.ContainsKey(payment.BookingID)) Payments[payment.BookingID] = new List<Payment>();
                Payments[payment.BookingID].Add(payment);
                return Task.FromResult(1);
            }
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(true);
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
        }

        private class MockPropertyRepo : IPropertyRepository
        {
            public readonly Dictionary<int, Property> Properties = new();

            public Task<Property?> GetByIdAsync(int propertyId)
            {
                Properties.TryGetValue(propertyId, out var p);
                return Task.FromResult(p);
            }

            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(Properties.Values.ToList());
            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(Properties.Values.Where(p => p.OwnerID == ownerId).ToList());
            public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests) => Task.FromResult(new List<Property>());
            public Task<int> CreatePropertyAsync(Property property)
            {
                property.PropertyID = Properties.Count + 1;
                Properties[property.PropertyID] = property;
                return Task.FromResult(property.PropertyID);
            }
            public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
            public Task<bool> DeletePropertyAsync(int propertyId) => PermanentlyDeletePropertyAsync(propertyId);
            public Task<bool> PermanentlyDeletePropertyAsync(int propertyId) => Task.FromResult(Properties.Remove(propertyId));
            public Task<bool> DeactivatePropertyAsync(int propertyId, bool deactivate = true) => Task.FromResult(true);
            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null) => Task.FromResult(true);
            public Task<List<PropertyType>> GetPropertyTypesAsync() => Task.FromResult(new List<PropertyType>());
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(new List<Occasion>());
            public Task<List<AmenityMaster>> GetAmenitiesMasterAsync() => Task.FromResult(new List<AmenityMaster>());
            public Task<bool> UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl) => Task.FromResult(true);
            public Task<int> GetOrCreatePropertyTypeByNameAsync(string name) => Task.FromResult(1);
            public Task<bool> UpdatePropertyCategoryAsync(int propertyId, int ownerId, int propertyTypeId) => Task.FromResult(true);
            public Task<PropertyEventCommission?> GetEventCommissionByPropertyIdAsync(int propertyId) => Task.FromResult<PropertyEventCommission?>(null);
            public Task<bool> SetPropertyEventCommissionAsync(int propertyId, decimal? commissionRate) => Task.FromResult(true);
            public Task<List<PropertyEventCommission>> GetAllPropertyEventCommissionsAsync() => Task.FromResult(new List<PropertyEventCommission>());
            public Task<bool> ResetPropertyEventCommissionAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> SetPropertyOccasionsAsync(int propertyId, List<int> occasionIds) => Task.FromResult(true);
            public Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId) => Task.FromResult(new List<int>());
            public Task<int> AddPropertyImageAsync(int propertyId, string imageUrl, bool isPrimary) => Task.FromResult(1);
            public Task<List<PropertyImage>> GetPropertyImagesAsync(int propertyId) => Task.FromResult(new List<PropertyImage>());
            public Task<int> AddPropertyMediaAsync(PropertyMedia media) => Task.FromResult(1);
            public Task<List<PropertyMedia>> GetPropertyMediaAsync(int propertyId) => Task.FromResult(new List<PropertyMedia>());
            public Task<bool> DeletePropertyMediaAsync(int mediaId) => Task.FromResult(true);
            public Task<List<AmenityMaster>> GetAllAmenitiesAsync() => Task.FromResult(new List<AmenityMaster>());
            public Task<List<int>> GetPropertyAmenityIdsAsync(int propertyId) => Task.FromResult(new List<int>());
            public Task<List<AmenityMaster>> GetPropertyAmenitiesAsync(int propertyId) => Task.FromResult(new List<AmenityMaster>());
            public Task<bool> SetPropertyAmenitiesAsync(int propertyId, List<int> amenityIds) => Task.FromResult(true);
            public Task<int> GetOrCreateAmenityByNameAsync(string name) => Task.FromResult(1);
            public Task<int> GetOrCreateOccasionByNameAsync(string name) => Task.FromResult(1);
            public Task<List<PropertyEventCommission>> GetPropertyEventCommissionsAsync(int propertyId) => Task.FromResult(new List<PropertyEventCommission>());
            public Task<decimal?> GetPropertyEventCommissionRateAsync(int propertyId, int packageId) => Task.FromResult<decimal?>(null);
            public Task<bool> SetPropertyEventCommissionsAsync(int propertyId, List<(int packageId, decimal commissionRate)> commissions) => Task.FromResult(true);
        }

        private class MockUserRepo : IUserRepository
        {
            public readonly Dictionary<int, User> Users = new();

            public Task<User?> GetByIdAsync(int userId)
            {
                Users.TryGetValue(userId, out var u);
                return Task.FromResult(u);
            }
            public Task<User?> GetByEmailAsync(string email) => Task.FromResult(Users.Values.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));
            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult<User?>(null);
            public Task<int> CreateUserAsync(User user)
            {
                user.UserID = Users.Count + 1;
                Users[user.UserID] = user;
                return Task.FromResult(user.UserID);
            }
            public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(Users.Values.Where(u => u.Role == role).ToList());
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(Users.Values.ToList());
            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? commissionRate) => Task.FromResult(true);
            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
        }

        private static ControllerContext CreateContext(int userId, string role)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Name, $"User_{userId}"),
                new(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            var httpContext = new DefaultHttpContext { User = principal };
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("bookmyfun.in");

            return new ControllerContext
            {
                HttpContext = httpContext
            };
        }

        [Fact]
        public async Task CommunicationService_SendBookingReceiptEmailAsync_GeneratesStructuredHtmlWithReceiptAndGatePass()
        {
            var queue = new EmailQueueService(NullLogger<EmailQueueService>.Instance);
            var comms = new CommunicationService(
                new MockAuditConfigRepo(),
                new ConfigurationBuilder().Build(),
                queue,
                NullLogger<CommunicationService>.Instance);

            var receipt = new BookingReceiptViewModel
            {
                Booking = new Booking
                {
                    BookingID = 101,
                    ClientID = 5,
                    PropertyID = 10,
                    TotalAmount = 28500m,
                    CheckInDate = new DateTime(2026, 11, 15),
                    CheckOutDate = new DateTime(2026, 11, 17),
                    SlotStartTime = new TimeSpan(14, 0, 0),
                    SlotEndTime = new TimeSpan(11, 0, 0),
                    EventPackageName = "Sunset Gazebo Theme",
                    EventPackagePriceSnapshot = 4500m,
                    SecurityDepositAmount = 5000m,
                    CreatedAt = new DateTime(2026, 10, 23, 10, 0, 0)
                },
                Property = new Property
                {
                    PropertyID = 10,
                    Title = "Skyline Panorama Villa",
                    Location = "Lonavala, Maharashtra",
                    MapUrl = "https://maps.google.com/?q=18.75,73.40"
                }
            };

            bool success = await comms.SendBookingReceiptEmailAsync(
                "guest@example.com", 
                "Rohan Sharma", 
                receipt, 
                "https://bookmyfun.in/Booking/Receipt/101");

            Assert.True(success);
            Assert.True(queue.ApproximateQueueCount > 0);

            var email = await queue.DequeueAsync(CancellationToken.None);
            Assert.Equal("guest@example.com", email.ToEmail);
            Assert.Contains("BMF-REC-000101", email.Subject);
            Assert.Contains("Official Booking Receipt &amp; Gate Pass", email.BodyHtml);
            Assert.Contains("BMF-REC-000101", email.BodyHtml);
            Assert.Contains("INV-202610-00101", email.BodyHtml);
            Assert.Contains("Skyline Panorama Villa", email.BodyHtml);
            Assert.Contains("Sunset Gazebo Theme", email.BodyHtml);
            Assert.Contains("4,500.00", email.BodyHtml);
            Assert.Contains("28,500.00", email.BodyHtml);
            Assert.Contains("https://maps.google.com/?q=18.75,73.40", email.BodyHtml);
            Assert.Contains("Gate Pass Instructions", email.BodyHtml);
            Assert.Contains("https://bookmyfun.in/Booking/Receipt/101", email.BodyHtml);
        }

        [Fact]
        public async Task CommunicationService_SendBookingReceiptWhatsAppAsync_DispatchesCompleteReceiptDetailsAndLink()
        {
            var comms = new CommunicationService(
                new MockAuditConfigRepo(),
                new ConfigurationBuilder().Build(),
                new EmailQueueService(NullLogger<EmailQueueService>.Instance),
                NullLogger<CommunicationService>.Instance);

            bool success = await comms.SendBookingReceiptWhatsAppAsync(
                "9876543210",
                "Rohan Sharma",
                "Skyline Panorama Villa",
                "101",
                "BMF-REC-000101",
                28500m,
                "https://bookmyfun.in/Booking/Receipt/101",
                new DateTime(2026, 11, 15),
                new DateTime(2026, 11, 17));

            Assert.True(success);
        }

        [Fact]
        public async Task CommunicationService_SendOwnerSettlementReceiptEmailAsync_GeneratesHostStatementWithNetPayoutAndCommission()
        {
            var queue = new EmailQueueService(NullLogger<EmailQueueService>.Instance);
            var comms = new CommunicationService(
                new MockAuditConfigRepo(),
                new ConfigurationBuilder().Build(),
                queue,
                NullLogger<CommunicationService>.Instance);

            var receipt = new BookingReceiptViewModel
            {
                Booking = new Booking
                {
                    BookingID = 101,
                    ClientID = 5,
                    PropertyID = 10,
                    TotalAmount = 28500m,
                    CheckInDate = new DateTime(2026, 11, 15),
                    CheckOutDate = new DateTime(2026, 11, 17),
                    EventPackagePriceSnapshot = 4500m,
                    CommissionRateSnapshot = 10.0m,
                    PlatformCommissionAmount = 2400m,
                    OwnerPayoutAmount = 21600m,
                    CreatedAt = new DateTime(2026, 10, 23, 10, 0, 0)
                },
                Property = new Property
                {
                    PropertyID = 10,
                    Title = "Skyline Panorama Villa",
                    Location = "Lonavala, Maharashtra"
                },
                Client = new User
                {
                    UserID = 5,
                    FullName = "Rohan Sharma"
                }
            };

            bool success = await comms.SendOwnerSettlementReceiptEmailAsync(
                "host@example.com",
                "Amit Patel",
                receipt,
                "https://bookmyfun.in/Owner/BookingReceipt/101");

            Assert.True(success);
            Assert.True(queue.ApproximateQueueCount > 0);

            var email = await queue.DequeueAsync(CancellationToken.None);
            Assert.Equal("host@example.com", email.ToEmail);
            Assert.Contains("BMF-HST-000101", email.Subject);
            Assert.Contains("Host Partner Settlement Statement", email.BodyHtml);
            Assert.Contains("BMF-HST-000101", email.BodyHtml);
            Assert.Contains("Skyline Panorama Villa", email.BodyHtml);
            Assert.Contains("Rohan Sharma", email.BodyHtml);
            Assert.Contains("24,000.00", email.BodyHtml); // Property Stay Gross
            Assert.Contains("2,400.00", email.BodyHtml); // Platform Commission
            Assert.Contains("21,600.00", email.BodyHtml); // Net Host Payout
            Assert.Contains("Celebration Décor Add-on Notice", email.BodyHtml);
            Assert.Contains("https://bookmyfun.in/Owner/BookingReceipt/101", email.BodyHtml);
        }

        [Fact]
        public async Task CommunicationService_SendOwnerBookingReceiptWhatsAppAsync_DispatchesSettlementSummaryAndLink()
        {
            var comms = new CommunicationService(
                new MockAuditConfigRepo(),
                new ConfigurationBuilder().Build(),
                new EmailQueueService(NullLogger<EmailQueueService>.Instance),
                NullLogger<CommunicationService>.Instance);

            bool success = await comms.SendOwnerBookingReceiptWhatsAppAsync(
                "9876543211",
                "Amit Patel",
                "Skyline Panorama Villa",
                "101",
                "BMF-HST-000101",
                24000m,
                21600m,
                "Rohan Sharma",
                new DateTime(2026, 11, 15),
                new DateTime(2026, 11, 17),
                true,
                4500m,
                "https://bookmyfun.in/Owner/BookingReceipt/101");

            Assert.True(success);
        }

        [Fact]
        public async Task BookingController_ResendReceipt_EnforcesIDOR_AndDispatchesToClient()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();
            var auditRepo = new MockAuditConfigRepo();
            var queue = new EmailQueueService(NullLogger<EmailQueueService>.Instance);
            var comms = new CommunicationService(
                auditRepo,
                new ConfigurationBuilder().Build(),
                queue,
                NullLogger<CommunicationService>.Instance);

            var client = new User { UserID = 7, FullName = "Sunil Verma", Email = "sunil@example.com", Phone = "9876543210" };
            userRepo.Users[7] = client;

            var otherClient = new User { UserID = 99, FullName = "Intruder User", Email = "intruder@example.com" };
            userRepo.Users[99] = otherClient;

            var host = new User { UserID = 20, FullName = "Kavita Host", Email = "kavita@example.com", Phone = "9876543211" };
            userRepo.Users[20] = host;

            var property = new Property { PropertyID = 40, Title = "Hilltop Meadow Retreat", OwnerID = 20, Location = "Karjat" };
            propRepo.Properties[40] = property;

            var booking = new Booking
            {
                BookingID = 555,
                ClientID = 7,
                PropertyID = 40,
                TotalAmount = 18000m,
                Status = "Confirmed",
                CheckInDate = DateTime.Today.AddDays(7),
                CheckOutDate = DateTime.Today.AddDays(9),
                CreatedAt = DateTime.UtcNow
            };
            bookingRepo.Bookings[555] = booking;

            var controller = new BookingController(
                propRepo, bookingRepo, null!, null!,
                null!, null!, null!, null!,
                comms, comms, userRepo, null!, null!,
                auditRepo)
            {
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            // Case A: IDOR check - Other guest is forbidden
            controller.ControllerContext = CreateContext(99, "Client");
            var forbidResult = await controller.ResendReceipt(555);
            Assert.IsType<ForbidResult>(forbidResult);

            // Case B: Legitimate guest succeeds
            controller.ControllerContext = CreateContext(7, "Client");
            var successResult = await controller.ResendReceipt(555);
            var redirectResult = Assert.IsType<RedirectToActionResult>(successResult);
            Assert.Equal("Confirmation", redirectResult.ActionName);
            Assert.Equal(555, redirectResult.RouteValues!["id"]);
            Assert.True(controller.TempData.ContainsKey("Success"));
            Assert.Contains("BMF-REC-000555", controller.TempData["Success"]!.ToString());

            // Verify email was enqueued
            Assert.True(queue.ApproximateQueueCount > 0);
            var dispatchedEmail = await queue.DequeueAsync(CancellationToken.None);
            Assert.Equal("sunil@example.com", dispatchedEmail.ToEmail);

            // Verify audit was recorded
            Assert.Single(auditRepo.Logs);
            Assert.Equal("ResendReceipt", auditRepo.Logs[0].Action);
            Assert.Equal("Booking", auditRepo.Logs[0].EntityType);
            Assert.Equal(555, auditRepo.Logs[0].EntityId);
        }

        [Fact]
        public void Views_ConfirmationAndReceipts_DeclareTransactionalDeliveryControls()
        {
            string confirmationViewPath = TestPathHelper.GetPath("Views", "Booking", "Confirmation.cshtml");
            string receiptViewPath = TestPathHelper.GetPath("Views", "Booking", "Receipt.cshtml");
            string ownerReceiptViewPath = TestPathHelper.GetPath("Views", "Owner", "BookingReceipt.cshtml");

            Assert.True(File.Exists(confirmationViewPath));
            Assert.True(File.Exists(receiptViewPath));
            Assert.True(File.Exists(ownerReceiptViewPath));

            string confirmContent = File.ReadAllText(confirmationViewPath);
            Assert.Contains("ResendReceipt", confirmContent);
            Assert.Contains("btnResendReceipt", confirmContent);
            Assert.Contains("btnDispatchReceiptChannels", confirmContent);
            Assert.Contains("Multi-Party Confirmation &amp; Receipts Dispatched", confirmContent);

            string receiptContent = File.ReadAllText(receiptViewPath);
            Assert.Contains("btnReceiptResendDelivery", receiptContent);
            Assert.Contains("ResendReceipt", receiptContent);

            string ownerReceiptContent = File.ReadAllText(ownerReceiptViewPath);
            Assert.Contains("btnHostReceiptResendDelivery", ownerReceiptContent);
            Assert.Contains("ResendBookingReceipt", ownerReceiptContent);
        }
    }
}
