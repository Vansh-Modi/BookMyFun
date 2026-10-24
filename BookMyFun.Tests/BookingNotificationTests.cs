using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class BookingNotificationTests
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

            public void AddInAppNotification(int userId, string title, string message, string type = "KycRejection", string? actionUrl = null)
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

            public Task<int> RecordPaymentAsync(Payment payment)
            {
                payment.PaymentID = Payments.Count + 1;
                Payments.Add(payment);
                return Task.FromResult(payment.PaymentID);
            }

            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId)
            {
                return Task.FromResult(Payments.Where(p => p.BookingID == bookingId).ToList());
            }

            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(Bookings.Values.ToList());
            public Task<int> CreateBookingAsync(Booking booking)
            {
                booking.BookingID = Bookings.Count + 1;
                Bookings[booking.BookingID] = booking;
                return Task.FromResult(booking.BookingID);
            }
            public Task<bool> CancelBookingAsync(int bookingId) => UpdateBookingStatusAsync(bookingId, "Cancelled");
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
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
            public Task<int> CreatePropertyAsync(Property property)
            {
                property.PropertyID = Properties.Count + 1;
                Properties[property.PropertyID] = property;
                return Task.FromResult(property.PropertyID);
            }
            public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
            public Task<bool> DeletePropertyAsync(int propertyId) => Task.FromResult(Properties.Remove(propertyId));
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

        private class MockWebhookEventRepo : IWebhookEventRepository
        {
            public readonly HashSet<string> ProcessedEvents = new();
            public Task<bool> HasEventBeenProcessedAsync(string eventId) => Task.FromResult(ProcessedEvents.Contains(eventId));
            public Task RecordEventProcessedAsync(string eventId, string eventType, string status, int? bookingId = null)
            {
                ProcessedEvents.Add(eventId);
                return Task.CompletedTask;
            }
        }

        private class MockRefundRepo : IRefundRepository
        {
            public readonly Dictionary<int, BookingRefund> Refunds = new();

            public Task<int> InsertRefundAsync(BookingRefund refund)
            {
                refund.RefundID = Refunds.Count + 1;
                Refunds[refund.BookingID] = refund;
                return Task.FromResult(refund.RefundID);
            }
            public Task<BookingRefund?> GetRefundByIdAsync(int refundId) => Task.FromResult(Refunds.Values.FirstOrDefault(r => r.RefundID == refundId));
            public Task<BookingRefund?> GetRefundByBookingIdAsync(int bookingId)
            {
                Refunds.TryGetValue(bookingId, out var r);
                return Task.FromResult(r);
            }
            public Task<List<BookingRefund>> GetRefundsAsync(string? status = null) => Task.FromResult(Refunds.Values.ToList());
            public Task<bool> UpdateRefundStatusAsync(int refundId, string status, int processedByUserId, string? notes)
            {
                var r = Refunds.Values.FirstOrDefault(x => x.RefundID == refundId);
                if (r != null) { r.Status = status; return Task.FromResult(true); }
                return Task.FromResult(false);
            }
        }

        [Fact]
        public async Task ConfirmedBooking_Dispatches_Client_Owner_And_EventManager_Notifications()
        {
            // Arrange
            var queue = new TestEmailQueueService();
            var comms = new CommunicationService(
                new MockConfigRepo(),
                new ConfigurationBuilder().Build(),
                queue,
                NullLogger<CommunicationService>.Instance);

            var booking = new Booking
            {
                BookingID = 101,
                PropertyID = 10,
                ClientID = 1,
                TotalAmount = 15000m,
                OwnerPayoutAmount = 13500m,
                CheckInDate = DateTime.UtcNow.AddDays(7),
                CheckOutDate = DateTime.UtcNow.AddDays(9),
                IsEventBooking = true,
                EventPackagePriceSnapshot = 3000m,
                EventPackageName = "Royal Floral Canopy",
                Status = "Confirmed"
            };

            var property = new Property
            {
                PropertyID = 10,
                OwnerID = 2,
                Title = "Whispering Pines Villa",
                Location = "Lonavala",
                FormattedAddress = "Plot 12, Valley Road, Lonavala",
                MaxGuests = 12
            };

            var client = new User
            {
                UserID = 1,
                FullName = "Alice Johnson",
                Email = "alice@example.com",
                Phone = "+919876543210"
            };

            var owner = new User
            {
                UserID = 2,
                FullName = "Bob Smith",
                Email = "bob@example.com",
                Phone = "+919876543211",
                WhatsAppNotificationsEnabled = true
            };

            var eventManager = new User
            {
                UserID = 3,
                FullName = "Charlie Events",
                Email = "charlie@example.com",
                Phone = "+919876543212"
            };

            // Act
            await comms.DispatchConfirmedBookingNotificationsAsync(booking, property, client, owner, eventManager);

            // Assert
            // 1. Client received: in-app, email, and WhatsApp
            var clientInApp = queue.GetInAppNotifications(client.UserID);
            Assert.Single(clientInApp);
            Assert.Contains("101", clientInApp[0].Message);
            Assert.Contains("Whispering Pines Villa", clientInApp[0].Message);

            var clientEmail = queue.EnqueuedMessages.Where(m => m.ToEmail == client.Email && m.Channel == "Email").ToList();
            Assert.Single(clientEmail);
            Assert.Contains("101", clientEmail[0].Subject);

            var clientWa = queue.EnqueuedMessages.Where(m => m.ToPhone == client.Phone && m.Channel == "WhatsApp").ToList();
            Assert.Single(clientWa);
            Assert.Contains("101", clientWa[0].BodyHtml);

            // 2. Owner received: in-app, email, and WhatsApp
            var ownerInApp = queue.GetInAppNotifications(owner.UserID);
            Assert.Single(ownerInApp);
            Assert.Contains("13,500", ownerInApp[0].Message);

            var ownerEmail = queue.EnqueuedMessages.Where(m => m.ToEmail == owner.Email && m.Channel == "Email").ToList();
            Assert.Single(ownerEmail);

            var ownerWa = queue.EnqueuedMessages.Where(m => m.ToPhone == owner.Phone && m.Channel == "WhatsApp").ToList();
            Assert.Single(ownerWa);

            // 3. EventManager received: in-app, email, and WhatsApp
            var emInApp = queue.GetInAppNotifications(eventManager.UserID);
            Assert.Single(emInApp);
            Assert.Contains("Royal Floral Canopy", emInApp[0].Message);

            var emEmail = queue.EnqueuedMessages.Where(m => m.ToEmail == eventManager.Email && m.Channel == "Email").ToList();
            Assert.Single(emEmail);

            var emWa = queue.EnqueuedMessages.Where(m => m.ToPhone == eventManager.Phone && m.Channel == "WhatsApp").ToList();
            Assert.Single(emWa);
        }

        [Fact]
        public async Task Eventless_Booking_Sends_No_Notification_To_EventManager()
        {
            // Arrange
            var queue = new TestEmailQueueService();
            var comms = new CommunicationService(
                new MockConfigRepo(),
                new ConfigurationBuilder().Build(),
                queue,
                NullLogger<CommunicationService>.Instance);

            var booking = new Booking
            {
                BookingID = 102,
                PropertyID = 10,
                ClientID = 1,
                TotalAmount = 8000m,
                OwnerPayoutAmount = 7200m,
                CheckInDate = DateTime.UtcNow.AddDays(3),
                CheckOutDate = DateTime.UtcNow.AddDays(4),
                IsEventBooking = false, // No event package
                EventPackagePriceSnapshot = 0m,
                Status = "Confirmed"
            };

            var property = new Property { PropertyID = 10, OwnerID = 2, Title = "Sunset Estate", Location = "Alibaug", MaxGuests = 6 };
            var client = new User { UserID = 1, FullName = "Dave Green", Email = "dave@example.com", Phone = "+919876543213" };
            var owner = new User { UserID = 2, FullName = "Eve White", Email = "eve@example.com", Phone = "+919876543214" };
            var eventManager = new User { UserID = 3, FullName = "Frank Events", Email = "frank@example.com", Phone = "+919876543215" };

            // Act
            await comms.DispatchConfirmedBookingNotificationsAsync(booking, property, client, owner, eventManager);

            // Assert: Event manager gets zero messages across all channels
            Assert.Empty(queue.GetInAppNotifications(eventManager.UserID));
            Assert.Empty(queue.EnqueuedMessages.Where(m => m.ToEmail == eventManager.Email));
            Assert.Empty(queue.EnqueuedMessages.Where(m => m.ToPhone == eventManager.Phone));
        }

        [Fact]
        public async Task Owner_With_WhatsAppNotificationsDisabled_Does_Not_Receive_WhatsApp()
        {
            // Arrange
            var queue = new TestEmailQueueService();
            var comms = new CommunicationService(
                new MockConfigRepo(),
                new ConfigurationBuilder().Build(),
                queue,
                NullLogger<CommunicationService>.Instance);

            var booking = new Booking
            {
                BookingID = 103,
                PropertyID = 10,
                ClientID = 1,
                TotalAmount = 10000m,
                CheckInDate = DateTime.UtcNow.AddDays(5),
                CheckOutDate = DateTime.UtcNow.AddDays(6),
                Status = "Confirmed"
            };

            var property = new Property { PropertyID = 10, OwnerID = 2, Title = "Hilltop Villa", Location = "Khandala", MaxGuests = 8 };
            var client = new User { UserID = 1, FullName = "Guest One", Email = "guest@example.com", Phone = "+919876543216" };
            var owner = new User
            {
                UserID = 2,
                FullName = "Owner OptOut",
                Email = "owner@example.com",
                Phone = "+919876543217",
                WhatsAppNotificationsEnabled = false // WhatsApp opt-out
            };

            // Act
            await comms.DispatchConfirmedBookingNotificationsAsync(booking, property, client, owner, null);

            // Assert
            // Owner gets in-app and email, but NO WhatsApp
            Assert.Single(queue.GetInAppNotifications(owner.UserID));
            Assert.Single(queue.EnqueuedMessages.Where(m => m.ToEmail == owner.Email && m.Channel == "Email"));
            Assert.Empty(queue.EnqueuedMessages.Where(m => m.ToPhone == owner.Phone && m.Channel == "WhatsApp"));
        }

        [Fact]
        public async Task Duplicate_Call_Or_Webhook_Sends_Nothing_New_Due_To_Idempotency()
        {
            // Arrange
            var queue = new TestEmailQueueService();
            var comms = new CommunicationService(
                new MockConfigRepo(),
                new ConfigurationBuilder().Build(),
                queue,
                NullLogger<CommunicationService>.Instance);

            var booking = new Booking
            {
                BookingID = 104,
                PropertyID = 10,
                ClientID = 1,
                TotalAmount = 12000m,
                OwnerPayoutAmount = 10800m,
                CheckInDate = DateTime.UtcNow.AddDays(10),
                CheckOutDate = DateTime.UtcNow.AddDays(12),
                IsEventBooking = true,
                EventPackagePriceSnapshot = 2500m,
                EventPackageName = "Birthday Special",
                Status = "Confirmed"
            };

            var property = new Property { PropertyID = 10, OwnerID = 2, Title = "Azure Bay", Location = "Goa", MaxGuests = 10 };
            var client = new User { UserID = 1, FullName = "Guest Two", Email = "guest2@example.com", Phone = "+919876543218" };
            var owner = new User { UserID = 2, FullName = "Owner Two", Email = "owner2@example.com", Phone = "+919876543219" };
            var eventManager = new User { UserID = 3, FullName = "Decor Two", Email = "decor2@example.com", Phone = "+919876543220" };

            // Act 1: Initial dispatch
            await comms.DispatchConfirmedBookingNotificationsAsync(booking, property, client, owner, eventManager);
            int initialEnqueuedCount = queue.EnqueuedMessages.Count;
            int initialInAppCount = queue.InAppNotifications.Values.Sum(list => list.Count);

            // Act 2: Duplicate dispatch (e.g. repeated webhook)
            await comms.DispatchConfirmedBookingNotificationsAsync(booking, property, client, owner, eventManager);

            // Assert: Absolutely no new messages enqueued
            Assert.Equal(initialEnqueuedCount, queue.EnqueuedMessages.Count);
            Assert.Equal(initialInAppCount, queue.InAppNotifications.Values.Sum(list => list.Count));
        }

        [Fact]
        public async Task RazorpayWebhook_PaymentFailed_Does_Not_Dispatch_Confirmed_Notifications()
        {
            // Arrange
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();
            var propRepo = new MockPropertyRepo();
            var queue = new TestEmailQueueService();
            var comms = new CommunicationService(
                new MockConfigRepo(),
                new ConfigurationBuilder().Build(),
                queue,
                NullLogger<CommunicationService>.Instance);

            var booking = new Booking
            {
                BookingID = 201,
                PropertyID = 1,
                ClientID = 1,
                TotalAmount = 5000m,
                Status = "Pending"
            };
            bookingRepo.Bookings[booking.BookingID] = booking;

            var inMemoryConfig = new Dictionary<string, string?>
            {
                { "Razorpay:WebhookSecret", "whsec_test123" }
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

            var controller = new RazorpayWebhookController(
                config,
                new MockConfigRepo(),
                bookingRepo,
                new MockRefundRepo(),
                new MockWebhookEventRepo(),
                NullLogger<RazorpayWebhookController>.Instance,
                userRepo,
                propRepo,
                null,
                comms,
                comms);

            string payload = @"{
                ""event"": ""payment.failed"",
                ""payload"": {
                    ""payment"": {
                        ""entity"": {
                            ""id"": ""pay_failed_123"",
                            ""amount"": 500000,
                            ""notes"": {
                                ""booking_id"": 201
                            }
                        }
                    }
                }
            }";

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["X-Razorpay-Event-Id"] = "evt_fail_999";
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("whsec_test123"));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            httpContext.Request.Headers["X-Razorpay-Signature"] = Convert.ToHexString(hash).ToLowerInvariant();

            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            // Act
            var result = await controller.HandleWebhook();

            // Assert
            Assert.IsType<OkObjectResult>(result);
            Assert.Empty(queue.EnqueuedMessages);
            Assert.Empty(queue.InAppNotifications);
            Assert.NotEqual("Confirmed", booking.Status);
        }
    }
}
