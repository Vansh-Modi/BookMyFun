using System.Collections.Concurrent;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class CancellationNotificationTests
    {
        private class TestEmailQueueService : IEmailQueueService
        {
            public readonly List<EmailQueueMessage> EnqueuedMessages = new();
            public readonly ConcurrentDictionary<int, List<InAppNotificationItem>> InAppNotifications = new();
            public readonly ConcurrentDictionary<string, DateTime> NotificationKeys = new();

            public int ApproximateQueueCount => EnqueuedMessages.Count;

            public ValueTask EnqueueAsync(EmailQueueMessage message)
            {
                EnqueuedMessages.Add(message);
                return ValueTask.CompletedTask;
            }

            public ValueTask<EmailQueueMessage> DequeueAsync(CancellationToken cancellationToken)
            {
                return ValueTask.FromResult(new EmailQueueMessage());
            }

            public void AddInAppNotification(int userId, string title, string message, string type = "BookingCancellation", string? actionUrl = null)
            {
                var list = InAppNotifications.GetOrAdd(userId, _ => new List<InAppNotificationItem>());
                list.Add(new InAppNotificationItem
                {
                    UserId = userId,
                    Title = title,
                    Message = message,
                    NotificationType = type,
                    ActionUrl = actionUrl
                });
            }

            public List<InAppNotificationItem> GetInAppNotifications(int userId)
            {
                return InAppNotifications.TryGetValue(userId, out var list) ? list : new List<InAppNotificationItem>();
            }

            public void MarkInAppNotificationRead(int notificationId) { }

            public bool TryAcquireNotificationKey(string notificationKey)
            {
                if (string.IsNullOrWhiteSpace(notificationKey)) return false;
                return NotificationKeys.TryAdd(notificationKey, DateTime.UtcNow);
            }

            public bool HasNotificationKey(string notificationKey)
            {
                if (string.IsNullOrWhiteSpace(notificationKey)) return false;
                return NotificationKeys.ContainsKey(notificationKey);
            }
        }

        private class MockBookingRepo : IBookingRepository
        {
            public readonly Dictionary<int, Booking> Bookings = new();
            public readonly List<Payment> Payments = new();

            public Task<Booking?> GetByIdAsync(int bookingId)
            {
                Bookings.TryGetValue(bookingId, out var b);
                return Task.FromResult(b);
            }

            public Task<Booking?> GetBookingByIdAsync(int bookingId) => GetByIdAsync(bookingId);

            public Task<bool> UpdateBookingStatusAsync(int bookingId, string status)
            {
                if (Bookings.TryGetValue(bookingId, out var b))
                {
                    b.Status = status;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }

            public Task<bool> CancelBookingAsync(int bookingId) => UpdateBookingStatusAsync(bookingId, "Cancelled");

            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status)
            {
                if (Bookings.TryGetValue(bookingId, out var b))
                {
                    b.SecurityDepositStatus = status;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }

            public Task<bool> UpdateOwnerPayoutAsync(int bookingId, decimal newPayout)
            {
                if (Bookings.TryGetValue(bookingId, out var b))
                {
                    b.OwnerPayoutAmount = newPayout;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }

            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId)
            {
                return Task.FromResult(Payments.Where(p => p.BookingID == bookingId).ToList());
            }

            public Task<int> RecordPaymentAsync(Payment payment)
            {
                payment.PaymentID = Payments.Count + 1;
                Payments.Add(payment);
                return Task.FromResult(payment.PaymentID);
            }

            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(Bookings.Values.ToList());
            public Task<int> CreateBookingAsync(Booking booking) => Task.FromResult(booking.BookingID);
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(true);
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
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
            public Task<int> CreatePropertyAsync(Property property) => Task.FromResult(property.PropertyID);
            public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
            public Task<bool> DeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> PermanentlyDeletePropertyAsync(int propertyId) => Task.FromResult(true);
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

        private class MockRefundRepo : IRefundRepository
        {
            public readonly Dictionary<int, BookingRefund> Refunds = new();

            public Task<int> InsertRefundAsync(BookingRefund refund)
            {
                refund.RefundID = Refunds.Count + 1;
                Refunds[refund.RefundID] = refund;
                return Task.FromResult(refund.RefundID);
            }
            public Task<BookingRefund?> GetRefundByIdAsync(int refundId) => Task.FromResult(Refunds.TryGetValue(refundId, out var r) ? r : null);
            public Task<BookingRefund?> GetRefundByBookingIdAsync(int bookingId) => Task.FromResult(Refunds.Values.FirstOrDefault(r => r.BookingID == bookingId));
            public Task<List<BookingRefund>> GetRefundsAsync(string? status = null)
            {
                if (string.IsNullOrEmpty(status)) return Task.FromResult(Refunds.Values.ToList());
                return Task.FromResult(Refunds.Values.Where(r => r.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList());
            }
            public Task<bool> UpdateRefundStatusAsync(int refundId, string status, int processedByUserId, string? notes)
            {
                if (Refunds.TryGetValue(refundId, out var r))
                {
                    r.Status = status;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
        }

        private class MockConfigRepo : IConfigRepository
        {
            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
            public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details) => Task.CompletedTask;
        }

        private class MockPaymentGatewayService : IPaymentGatewayService
        {
            public bool ShouldFailRefund { get; set; } = false;

            public Task<PaymentOrderResponse> CreateOrderAsync(decimal amount, string receipt, string currency = "INR")
                => Task.FromResult(new PaymentOrderResponse { OrderId = "order_123", Amount = amount, Currency = currency });

            public Task<bool> VerifyPaymentSignatureAsync(string orderId, string paymentId, string signature)
                => Task.FromResult(true);

            public Task<PaymentRefundResult> ProcessRefundAsync(string paymentId, decimal amount, string? reason = null)
            {
                if (ShouldFailRefund)
                {
                    return Task.FromResult(new PaymentRefundResult { Success = false, ErrorMessage = "Gateway connection timeout" });
                }
                return Task.FromResult(new PaymentRefundResult { Success = true, RefundId = "rfnd_test_999", IsPending = false });
            }
        }

        [Fact]
        public async Task CancellationConfirmed_Sends_ThreeParty_Notifications_With_Exact_Details()
        {
            // Arrange
            var queue = new TestEmailQueueService();
            var comms = new CommunicationService(
                new MockConfigRepo(),
                new ConfigurationBuilder().Build(),
                queue,
                NullLogger<CommunicationService>.Instance);

            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropertyRepo();
            var userRepo = new MockUserRepo();
            var refundRepo = new MockRefundRepo();
            var gateway = new MockPaymentGatewayService();

            var client = new User { UserID = 1, FullName = "Neha Sharma", Email = "neha@example.com", Phone = "+919988776655", Role = "Client" };
            var owner = new User { UserID = 2, FullName = "Rajesh Gupta", Email = "rajesh@example.com", Phone = "+919988776656", Role = "Owner", WhatsAppNotificationsEnabled = true };
            var eventManager = new User { UserID = 3, FullName = "Manish Decor", Email = "manish@example.com", Phone = "+919988776657", Role = "EventManager" };

            userRepo.Users[1] = client;
            userRepo.Users[2] = owner;
            userRepo.Users[3] = eventManager;

            var property = new Property { PropertyID = 10, OwnerID = 2, Title = "Hilltop Meadow", Location = "Lonavala", MaxGuests = 8 };
            propRepo.Properties[10] = property;

            var booking = new Booking
            {
                BookingID = 501,
                PropertyID = 10,
                ClientID = 1,
                TotalAmount = 20000m,
                OwnerPayoutAmount = 18000m,
                CheckInDate = DateTime.UtcNow.AddDays(10), // Scenario 1: > 48h before check-in
                CheckOutDate = DateTime.UtcNow.AddDays(12),
                IsEventBooking = true,
                EventPackagePriceSnapshot = 4000m,
                EventPackageName = "Grand Arch Floral",
                DecoratorAssignedID = 3,
                Status = "Confirmed"
            };
            bookingRepo.Bookings[501] = booking;
            bookingRepo.Payments.Add(new Payment { BookingID = 501, Amount = 20000m, PaymentStatus = "Success", GatewayTransactionID = "PAY_501" });

            var refundEngine = new RefundEngineService(
                bookingRepo,
                propRepo,
                refundRepo,
                new MockConfigRepo(),
                NullLogger<RefundEngineService>.Instance,
                gateway,
                comms,
                userRepo);

            // Act: Process cancellation under Scenario 1
            var refund = await refundEngine.ProcessCancellationAndRefundAsync(501, 1, "Client", "Change of plans");

            // Assert
            Assert.NotNull(refund);
            Assert.Equal("Completed", refund.Status);

            // 1. Client notifications (In-app, email, WhatsApp)
            var clientInApp = queue.GetInAppNotifications(client.UserID);
            Assert.Single(clientInApp);
            Assert.Contains("Scenario #1", clientInApp[0].Message);
            Assert.Contains("20,000", clientInApp[0].Message);

            var clientEmail = queue.EnqueuedMessages.Where(m => m.ToEmail == client.Email && m.Channel == "Email").ToList();
            Assert.Single(clientEmail);
            Assert.Contains("501", clientEmail[0].Subject);

            var clientWa = queue.EnqueuedMessages.Where(m => m.ToPhone == client.Phone && m.Channel == "WhatsApp").ToList();
            Assert.Single(clientWa);
            Assert.Contains("Scenario #1", clientWa[0].BodyHtml);

            // 2. Owner notifications (In-app, email, WhatsApp)
            var ownerInApp = queue.GetInAppNotifications(owner.UserID);
            Assert.Single(ownerInApp);
            Assert.Contains("Change of plans", ownerInApp[0].Message);

            var ownerEmail = queue.EnqueuedMessages.Where(m => m.ToEmail == owner.Email && m.Channel == "Email").ToList();
            Assert.Single(ownerEmail);

            var ownerWa = queue.EnqueuedMessages.Where(m => m.ToPhone == owner.Phone && m.Channel == "WhatsApp").ToList();
            Assert.Single(ownerWa);

            // 3. EventManager notifications (In-app, email, WhatsApp - package cancelled, no setup needed)
            var emInApp = queue.GetInAppNotifications(eventManager.UserID);
            Assert.Single(emInApp);
            Assert.Contains("no setup needed", emInApp[0].Message);
            Assert.Contains("Grand Arch Floral", emInApp[0].Message);

            var emEmail = queue.EnqueuedMessages.Where(m => m.ToEmail == eventManager.Email && m.Channel == "Email").ToList();
            Assert.Single(emEmail);
            Assert.Contains("No setup needed", emEmail[0].BodyHtml, StringComparison.OrdinalIgnoreCase);

            var emWa = queue.EnqueuedMessages.Where(m => m.ToPhone == eventManager.Phone && m.Channel == "WhatsApp").ToList();
            Assert.Single(emWa);
            Assert.Contains("No setup needed", emWa[0].BodyHtml, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Eventless_Cancellation_Sends_No_Notification_To_EventManager()
        {
            // Arrange
            var queue = new TestEmailQueueService();
            var comms = new CommunicationService(
                new MockConfigRepo(),
                new ConfigurationBuilder().Build(),
                queue,
                NullLogger<CommunicationService>.Instance);

            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropertyRepo();
            var userRepo = new MockUserRepo();
            var refundRepo = new MockRefundRepo();

            var client = new User { UserID = 1, FullName = "Client One", Email = "c1@example.com", Phone = "+919988771122", Role = "Client" };
            var owner = new User { UserID = 2, FullName = "Owner One", Email = "o1@example.com", Phone = "+919988771133", Role = "Owner" };
            var eventManager = new User { UserID = 3, FullName = "Decor One", Email = "em1@example.com", Phone = "+919988771144", Role = "EventManager" };

            userRepo.Users[1] = client;
            userRepo.Users[2] = owner;
            userRepo.Users[3] = eventManager;

            var property = new Property { PropertyID = 11, OwnerID = 2, Title = "Serene Villa", Location = "Goa" };
            propRepo.Properties[11] = property;

            var booking = new Booking
            {
                BookingID = 502,
                PropertyID = 11,
                ClientID = 1,
                TotalAmount = 10000m,
                OwnerPayoutAmount = 9000m,
                CheckInDate = DateTime.UtcNow.AddDays(5),
                CheckOutDate = DateTime.UtcNow.AddDays(7),
                IsEventBooking = false, // Eventless
                EventPackagePriceSnapshot = 0m,
                Status = "Confirmed"
            };
            bookingRepo.Bookings[502] = booking;

            var refundEngine = new RefundEngineService(
                bookingRepo,
                propRepo,
                refundRepo,
                new MockConfigRepo(),
                NullLogger<RefundEngineService>.Instance,
                new MockPaymentGatewayService(),
                comms,
                userRepo);

            // Act
            await refundEngine.ProcessCancellationAndRefundAsync(502, 1, "Client", "Emergency");

            // Assert: Event manager gets zero messages across all channels
            Assert.Empty(queue.GetInAppNotifications(eventManager.UserID));
            Assert.DoesNotContain(queue.EnqueuedMessages, m => m.ToEmail == eventManager.Email);
            Assert.DoesNotContain(queue.EnqueuedMessages, m => m.ToPhone == eventManager.Phone);
        }

        [Fact]
        public async Task Rows_5_And_6_Cancellations_Are_Placed_In_Admin_Refund_Queue()
        {
            // Arrange
            var queue = new TestEmailQueueService();
            var comms = new CommunicationService(
                new MockConfigRepo(),
                new ConfigurationBuilder().Build(),
                queue,
                NullLogger<CommunicationService>.Instance);

            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropertyRepo();
            var userRepo = new MockUserRepo();
            var refundRepo = new MockRefundRepo();

            var client = new User { UserID = 1, FullName = "Client Five", Email = "c5@example.com", Phone = "+919988775501", Role = "Client" };
            var owner = new User { UserID = 2, FullName = "Owner Five", Email = "o5@example.com", Phone = "+919988775502", Role = "Owner" };
            userRepo.Users[1] = client;
            userRepo.Users[2] = owner;

            var property = new Property { PropertyID = 15, OwnerID = 2, Title = "Mansion Five", Location = "Pune" };
            propRepo.Properties[15] = property;

            var booking = new Booking
            {
                BookingID = 505,
                PropertyID = 15,
                ClientID = 1,
                TotalAmount = 25000m,
                OwnerPayoutAmount = 22500m,
                CheckInDate = DateTime.UtcNow.AddDays(2),
                CheckOutDate = DateTime.UtcNow.AddDays(4),
                Status = "Confirmed"
            };
            bookingRepo.Bookings[505] = booking;

            var refundEngine = new RefundEngineService(
                bookingRepo,
                propRepo,
                refundRepo,
                new MockConfigRepo(),
                NullLogger<RefundEngineService>.Instance,
                new MockPaymentGatewayService(),
                comms,
                userRepo);

            // Act: Request cancellation under Row 5 (Listing mismatch) as client (non-staff)
            var refundRow5 = await refundEngine.ProcessCancellationAndRefundAsync(505, 1, "Client", "Listing mismatch", specificRule: 5);

            // Assert: Placed in admin refund queue with status Pending
            Assert.Equal("Pending", refundRow5.Status);
            var queueItems = await refundEngine.GetRefundsQueueAsync("Pending");
            Assert.Contains(queueItems, r => r.BookingID == 505);
        }

        [Fact]
        public async Task Cancellation_Retried_After_Gateway_Failure_Does_Not_Send_Duplicate_Notifications()
        {
            // Arrange
            var queue = new TestEmailQueueService();
            var comms = new CommunicationService(
                new MockConfigRepo(),
                new ConfigurationBuilder().Build(),
                queue,
                NullLogger<CommunicationService>.Instance);

            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropertyRepo();
            var userRepo = new MockUserRepo();
            var refundRepo = new MockRefundRepo();
            var gateway = new MockPaymentGatewayService { ShouldFailRefund = true }; // Initial gateway failure

            var client = new User { UserID = 1, FullName = "Retry Client", Email = "retry@example.com", Phone = "+919988776601", Role = "Client" };
            var owner = new User { UserID = 2, FullName = "Retry Owner", Email = "retryowner@example.com", Phone = "+919988776602", Role = "Owner", WhatsAppNotificationsEnabled = true };
            userRepo.Users[1] = client;
            userRepo.Users[2] = owner;

            var property = new Property { PropertyID = 20, OwnerID = 2, Title = "Retry Palace", Location = "Jaipur" };
            propRepo.Properties[20] = property;

            var booking = new Booking
            {
                BookingID = 510,
                PropertyID = 20,
                ClientID = 1,
                TotalAmount = 14000m,
                OwnerPayoutAmount = 12600m,
                CheckInDate = DateTime.UtcNow.AddDays(5),
                CheckOutDate = DateTime.UtcNow.AddDays(7),
                Status = "Confirmed"
            };
            bookingRepo.Bookings[510] = booking;

            var refundEngine = new RefundEngineService(
                bookingRepo,
                propRepo,
                refundRepo,
                new MockConfigRepo(),
                NullLogger<RefundEngineService>.Instance,
                gateway,
                comms,
                userRepo);

            // Act 1: Initial cancellation attempt with gateway failure
            var refund = await refundEngine.ProcessCancellationAndRefundAsync(510, 1, "Client", "Cancel initial");
            Assert.Equal("Pending", refund.Status); // Pending because gateway failed

            int initialEmailCount = queue.EnqueuedMessages.Count(m => m.Channel == "Email");
            int initialWaCount = queue.EnqueuedMessages.Count(m => m.Channel == "WhatsApp");
            int initialInAppCount = queue.InAppNotifications.Values.Sum(l => l.Count);

            // Act 2: Gateway is now recovered, Admin adjudicates / retries the cancellation
            gateway.ShouldFailRefund = false;
            var adjudicatedRefund = await refundEngine.AdjudicateAdminRefundAsync(refund.RefundID, 999, "Approve", 14000m, "Gateway recovered, approved");
            Assert.Equal("Completed", adjudicatedRefund.Status);

            // Assert: No duplicate notifications sent on retry!
            int retriedEmailCount = queue.EnqueuedMessages.Count(m => m.Channel == "Email");
            int retriedWaCount = queue.EnqueuedMessages.Count(m => m.Channel == "WhatsApp");
            int retriedInAppCount = queue.InAppNotifications.Values.Sum(l => l.Count);

            Assert.Equal(initialEmailCount, retriedEmailCount);
            Assert.Equal(initialWaCount, retriedWaCount);
            Assert.Equal(initialInAppCount, retriedInAppCount);
        }
    }
}
