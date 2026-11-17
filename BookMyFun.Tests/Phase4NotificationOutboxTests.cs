using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Notifications;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class Phase4NotificationOutboxTests
    {
        private class FakeNotificationOutboxRepository : INotificationOutboxRepository
        {
            public List<NotificationOutbox> EnqueuedItems = new();
            public HashSet<string> DispatchedKeys = new();

            public Task<bool> EnqueueAsync(NotificationOutbox item, SqlTransaction? tx = null)
            {
                string key = $"{item.EventType}:{item.EntityID}:{item.UserID}:{item.Channel}";
                if (DispatchedKeys.Contains(key))
                {
                    return Task.FromResult(false);
                }

                DispatchedKeys.Add(key);
                item.OutboxID = EnqueuedItems.Count + 1;
                EnqueuedItems.Add(item);
                return Task.FromResult(true);
            }

            public Task<List<NotificationOutbox>> GetPendingBatchAsync(int batchSize = 25)
            {
                var now = DateTime.UtcNow;
                return Task.FromResult(EnqueuedItems
                    .Where(x => (x.Status == "Pending" || x.Status == "Retry") && x.NextAttemptAt <= now)
                    .Take(batchSize)
                    .ToList());
            }

            public Task<bool> MarkProcessingAsync(long outboxId)
            {
                var item = EnqueuedItems.FirstOrDefault(x => x.OutboxID == outboxId);
                if (item != null) item.Status = "Processing";
                return Task.FromResult(true);
            }

            public Task<bool> MarkSentAsync(long outboxId)
            {
                var item = EnqueuedItems.FirstOrDefault(x => x.OutboxID == outboxId);
                if (item != null)
                {
                    item.Status = "Sent";
                    item.SentAt = DateTime.UtcNow;
                }
                return Task.FromResult(true);
            }

            public Task<bool> RecordFailureAsync(long outboxId, string error, int retryCount, DateTime nextAttemptAt, bool isPermanent)
            {
                var item = EnqueuedItems.FirstOrDefault(x => x.OutboxID == outboxId);
                if (item != null)
                {
                    item.Status = isPermanent ? "Failed" : "Retry";
                    item.RetryCount = retryCount;
                    item.NextAttemptAt = nextAttemptAt;
                    item.LastError = error;
                }
                return Task.FromResult(true);
            }
        }

        private class FakeUserNotificationRepository : IUserNotificationRepository
        {
            public List<UserNotification> Notifications = new();
            public HashSet<string> RecordedKeys = new();

            public Task<bool> InsertNotificationAsync(UserNotification notification)
            {
                notification.NotificationID = Notifications.Count + 1;
                Notifications.Add(notification);
                return Task.FromResult(true);
            }

            public Task<List<UserNotification>> GetUserNotificationsAsync(int userId, int limit = 20)
                => Task.FromResult(Notifications.Where(n => n.UserID == userId).Take(limit).ToList());

            public Task<int> GetUnreadCountAsync(int userId)
                => Task.FromResult(Notifications.Count(n => n.UserID == userId && !n.IsRead));

            public Task<bool> MarkAsReadAsync(int notificationId, int userId)
            {
                var n = Notifications.FirstOrDefault(x => x.NotificationID == notificationId && x.UserID == userId);
                if (n != null) n.IsRead = true;
                return Task.FromResult(true);
            }

            public Task<bool> TryRecordDispatchAsync(string notificationKey)
            {
                if (RecordedKeys.Contains(notificationKey)) return Task.FromResult(false);
                RecordedKeys.Add(notificationKey);
                return Task.FromResult(true);
            }
        }

        private class FakeUserRepository : IUserRepository
        {
            public List<User> Users = new();

            public Task<User?> GetByIdAsync(int userId) => Task.FromResult(Users.FirstOrDefault(u => u.UserID == userId));
            public Task<User?> GetByEmailAsync(string email) => Task.FromResult(Users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));
            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult(Users.FirstOrDefault(u => u.AuthProvider == provider && u.AuthProviderKey == providerKey));
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(Users.Where(u => u.Role.Equals(role, StringComparison.OrdinalIgnoreCase)).ToList());
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(Users);
            public Task<int> CreateUserAsync(User user) => Task.FromResult(1);
            public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
        }

        private class FakeBookingRepository : IBookingRepository
        {
            public Dictionary<int, Booking> Bookings = new();
            public Task<Booking?> GetByIdAsync(int bookingId)
            {
                Bookings.TryGetValue(bookingId, out var b);
                return Task.FromResult(b);
            }
            public Task<Booking?> GetBookingByIdAsync(int bookingId) => GetByIdAsync(bookingId);
            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(Bookings.Values.Where(b => b.ClientID == clientId).ToList());
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(Bookings.Values.ToList());
            public Task<List<Booking>> GetBookingsByPropertyIdAsync(int propertyId) => Task.FromResult(Bookings.Values.Where(b => b.PropertyID == propertyId).ToList());
            public Task<int> CreateBookingAsync(Booking booking) => Task.FromResult(1);
            public Task<bool> UpdateBookingStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> CancelBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(true);
            public Task<int> RecordPaymentAsync(Payment payment) => Task.FromResult(1);
            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId) => Task.FromResult(new List<Payment>());
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
        }

        private class FakePropertyRepository : IPropertyRepository
        {
            public Dictionary<int, Property> Properties = new();
            public Task<Property?> GetByIdAsync(int propertyId)
            {
                Properties.TryGetValue(propertyId, out var p);
                return Task.FromResult(p);
            }
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(new List<Occasion>());
            public Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId) => Task.FromResult(new List<int>());
            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(Properties.Values.ToList());
            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(Properties.Values.Where(p => p.OwnerID == ownerId).ToList());
            public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests) => Task.FromResult(new List<Property>());
            public Task<int> CreatePropertyAsync(Property property) => Task.FromResult(1);
            public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
            public Task<bool> UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl) => Task.FromResult(true);
            public Task<bool> DeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> PermanentlyDeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> DeactivatePropertyAsync(int propertyId, bool deactivate = true) => Task.FromResult(true);
            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null) => Task.FromResult(true);
            public Task<bool> SetPropertyOccasionsAsync(int propertyId, List<int> occasionIds) => Task.FromResult(true);
            public Task<int> AddPropertyImageAsync(int propertyId, string imageUrl, bool isPrimary) => Task.FromResult(1);
            public Task<List<PropertyImage>> GetPropertyImagesAsync(int propertyId) => Task.FromResult(new List<PropertyImage>());
            public Task<int> AddPropertyMediaAsync(PropertyMedia media) => Task.FromResult(1);
            public Task<List<PropertyMedia>> GetPropertyMediaAsync(int propertyId) => Task.FromResult(new List<PropertyMedia>());
            public Task<bool> DeletePropertyMediaAsync(int mediaId) => Task.FromResult(true);
            public Task<List<AmenityMaster>> GetAllAmenitiesAsync() => Task.FromResult(new List<AmenityMaster>());
            public Task<List<int>> GetPropertyAmenityIdsAsync(int propertyId) => Task.FromResult(new List<int>());
            public Task<List<AmenityMaster>> GetPropertyAmenitiesAsync(int propertyId) => Task.FromResult(new List<AmenityMaster>());
            public Task<bool> SetPropertyAmenitiesAsync(int propertyId, List<int> amenityIds) => Task.FromResult(true);
            public Task<List<PropertyType>> GetPropertyTypesAsync() => Task.FromResult(new List<PropertyType>());
            public Task<int> GetOrCreatePropertyTypeByNameAsync(string name) => Task.FromResult(1);
            public Task<bool> UpdatePropertyCategoryAsync(int propertyId, int ownerId, int propertyTypeId) => Task.FromResult(true);
            public Task<int> GetOrCreateAmenityByNameAsync(string name) => Task.FromResult(1);
            public Task<int> GetOrCreateOccasionByNameAsync(string name) => Task.FromResult(1);
            public Task<List<PropertyEventCommission>> GetPropertyEventCommissionsAsync(int propertyId) => Task.FromResult(new List<PropertyEventCommission>());
            public Task<decimal?> GetPropertyEventCommissionRateAsync(int propertyId, int packageId) => Task.FromResult<decimal?>(null);
            public Task<bool> SetPropertyEventCommissionsAsync(int propertyId, List<(int packageId, decimal commissionRate)> commissions) => Task.FromResult(true);
        }

        [Fact]
        public async Task EventBooking_Dispatches_WEA_ToClient_Owner_AndAssignedManager_AndShowsDecorName()
        {
            var outboxRepo = new FakeNotificationOutboxRepository();
            var notifRepo = new FakeUserNotificationRepository();
            var userRepo = new FakeUserRepository();
            var bookingRepo = new FakeBookingRepository();
            var propRepo = new FakePropertyRepository();

            // Seed Users
            userRepo.Users.Add(new User { UserID = 10, FullName = "Guest Client", Role = "Client", Email = "guest@test.com" });
            userRepo.Users.Add(new User { UserID = 20, FullName = "Farm Host", Role = "Owner", Email = "owner@test.com" });
            userRepo.Users.Add(new User { UserID = 30, FullName = "Royal Event Styling", Role = "EventManager", Email = "events@test.com" });
            userRepo.Users.Add(new User { UserID = 90, FullName = "Platform Employee", Role = "Employee", Email = "staff@test.com" });
            userRepo.Users.Add(new User { UserID = 99, FullName = "Super Admin", Role = "Admin", Email = "admin@test.com" });

            // Seed Property
            propRepo.Properties[1] = new Property { PropertyID = 1, OwnerID = 20, Title = "Mango Orchard Retreat" };

            // Seed Booking with Event Décor Assigned
            bookingRepo.Bookings[501] = new Booking
            {
                BookingID = 501,
                PropertyID = 1,
                ClientID = 10,
                CheckInDate = DateTime.Today.AddDays(5),
                CheckOutDate = DateTime.Today.AddDays(7),
                TotalAmount = 25000,
                OwnerPayoutAmount = 18000,
                EventManagerPayoutAmount = 5000,
                IsEventBooking = true,
                DecoratorAssignedID = 30
            };

            var dispatcher = new NotificationDispatcherService(
                outboxRepo, notifRepo, userRepo, bookingRepo, propRepo,
                null!, NullLogger<NotificationDispatcherService>.Instance);

            // Dispatch
            await dispatcher.DispatchBookingConfirmedAsync(501);

            // 1. Client receives WEA (InApp, Email, WhatsApp)
            var clientItems = outboxRepo.EnqueuedItems.Where(x => x.UserID == 10).ToList();
            Assert.Contains(clientItems, x => x.Channel == "InApp");
            Assert.Contains(clientItems, x => x.Channel == "Email");
            Assert.Contains(clientItems, x => x.Channel == "WhatsApp");

            // Client body shows the décor team's name
            var clientNotif = clientItems.First(x => x.Channel == "InApp");
            Assert.Contains("Royal Event Styling", clientNotif.Body);

            // 2. Owner receives WEA
            var ownerItems = outboxRepo.EnqueuedItems.Where(x => x.UserID == 20).ToList();
            Assert.Contains(ownerItems, x => x.Channel == "InApp");
            Assert.Contains(ownerItems, x => x.Channel == "Email");
            Assert.Contains(ownerItems, x => x.Channel == "WhatsApp");

            // 3. Assigned Event Manager receives WEA
            var emItems = outboxRepo.EnqueuedItems.Where(x => x.UserID == 30).ToList();
            Assert.Contains(emItems, x => x.Channel == "InApp");
            Assert.Contains(emItems, x => x.Channel == "Email");
            Assert.Contains(emItems, x => x.Channel == "WhatsApp");

            // 4. Staff receive EA (Employee EA | Admin EA)
            var empItems = outboxRepo.EnqueuedItems.Where(x => x.UserID == 90).ToList();
            Assert.Contains(empItems, x => x.Channel == "InApp");
            Assert.Contains(empItems, x => x.Channel == "Email");
            Assert.DoesNotContain(empItems, x => x.Channel == "WhatsApp");

            var adminItems = outboxRepo.EnqueuedItems.Where(x => x.UserID == 99).ToList();
            Assert.Contains(adminItems, x => x.Channel == "InApp");
            Assert.Contains(adminItems, x => x.Channel == "Email");
            Assert.DoesNotContain(adminItems, x => x.Channel == "WhatsApp");
        }

        [Fact]
        public async Task StayBooking_DoesNotDispatchToEventManager()
        {
            var outboxRepo = new FakeNotificationOutboxRepository();
            var notifRepo = new FakeUserNotificationRepository();
            var userRepo = new FakeUserRepository();
            var bookingRepo = new FakeBookingRepository();
            var propRepo = new FakePropertyRepository();

            userRepo.Users.Add(new User { UserID = 10, FullName = "Guest Client", Role = "Client" });
            userRepo.Users.Add(new User { UserID = 20, FullName = "Farm Host", Role = "Owner" });
            userRepo.Users.Add(new User { UserID = 30, FullName = "Unassigned Decorator", Role = "EventManager" });

            propRepo.Properties[2] = new Property { PropertyID = 2, OwnerID = 20, Title = "Peaceful Villa" };

            // Regular stay without event decor
            bookingRepo.Bookings[502] = new Booking
            {
                BookingID = 502,
                PropertyID = 2,
                ClientID = 10,
                CheckInDate = DateTime.Today.AddDays(3),
                CheckOutDate = DateTime.Today.AddDays(5),
                TotalAmount = 15000,
                IsEventBooking = false,
                DecoratorAssignedID = null
            };

            var dispatcher = new NotificationDispatcherService(
                outboxRepo, notifRepo, userRepo, bookingRepo, propRepo,
                null!, NullLogger<NotificationDispatcherService>.Instance);

            await dispatcher.DispatchBookingConfirmedAsync(502);

            // Client and Owner receive notifications
            Assert.Contains(outboxRepo.EnqueuedItems, x => x.UserID == 10);
            Assert.Contains(outboxRepo.EnqueuedItems, x => x.UserID == 20);

            // Unassigned Event Manager receives NOTHING (row 1: "—")
            Assert.DoesNotContain(outboxRepo.EnqueuedItems, x => x.UserID == 30);
        }

        [Fact]
        public async Task OutboxDeduplication_SkipsDuplicateEnqueue()
        {
            var outboxRepo = new FakeNotificationOutboxRepository();

            var item1 = new NotificationOutbox
            {
                EventType = "BookingConfirmed",
                EntityID = 601,
                UserID = 10,
                Channel = "Email",
                Title = "Confirmation",
                Body = "Details"
            };

            var item2 = new NotificationOutbox
            {
                EventType = "BookingConfirmed",
                EntityID = 601,
                UserID = 10,
                Channel = "Email",
                Title = "Confirmation Retry",
                Body = "Details"
            };

            bool firstResult = await outboxRepo.EnqueueAsync(item1);
            bool secondResult = await outboxRepo.EnqueueAsync(item2);

            Assert.True(firstResult);
            Assert.False(secondResult); // Deduplicated!
            Assert.Single(outboxRepo.EnqueuedItems);
        }

        [Fact]
        public async Task ContactDetailWarning_NeverSentToEmployee_OnlyToSenderAndAdmin()
        {
            var outboxRepo = new FakeNotificationOutboxRepository();
            var notifRepo = new FakeUserNotificationRepository();
            var userRepo = new FakeUserRepository();
            var bookingRepo = new FakeBookingRepository();
            var propRepo = new FakePropertyRepository();

            userRepo.Users.Add(new User { UserID = 15, FullName = "Violating Guest", Role = "Client" });
            userRepo.Users.Add(new User { UserID = 88, FullName = "Operations Staff", Role = "Employee" });
            userRepo.Users.Add(new User { UserID = 99, FullName = "Compliance Admin", Role = "Admin" });

            var dispatcher = new NotificationDispatcherService(
                outboxRepo, notifRepo, userRepo, bookingRepo, propRepo,
                null!, NullLogger<NotificationDispatcherService>.Instance);

            // Row 26: Contact detail shared in chat or inquiry
            await dispatcher.DispatchContactDetailAttemptFlaggedAsync(901, 15, null, "Inquiry");

            // 1. Violating sender receives InApp warning (A)
            var senderItem = Assert.Single(outboxRepo.EnqueuedItems, x => x.UserID == 15);
            Assert.Equal("InApp", senderItem.Channel);
            Assert.Contains("Policy Warning", senderItem.Title);

            // 2. Admin receives InApp flagged alert (A)
            var adminItem = Assert.Single(outboxRepo.EnqueuedItems, x => x.UserID == 99);
            Assert.Equal("InApp", adminItem.Channel);
            Assert.Contains("Flagged", adminItem.Title);

            // 3. Employee NEVER receives contact detail warnings (Row 26: "—")
            Assert.DoesNotContain(outboxRepo.EnqueuedItems, x => x.UserID == 88);
        }
    }
}
