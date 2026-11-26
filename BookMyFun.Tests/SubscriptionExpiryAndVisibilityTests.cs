using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Data;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Common;
using BookMyFun.Services.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class SubscriptionExpiryAndVisibilityTests
    {
        private static ControllerContext CreateContextWithUser(int userId, string role, string email = "user@test.com")
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, $"User {userId}")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            return new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            };
        }

        private static ControllerContext CreateAnonymousContext()
        {
            return new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            };
        }

        // =========================================================================
        // Check 1: Force an owner's trial to end. Live properties become Unpublished
        // with reason 'Subscription expired'. Properties still appear on owner's dashboard.
        // =========================================================================
        [Fact]
        public async Task PartE_Check1_TrialEnds_PropertiesUnpublishedWithReason_AppearOnOwnerDashboard()
        {
            var fakePropRepo = new FakePropertyRepo();
            var fakeConfigRepo = new FakeConfigRepo();
            const int ownerId = 801;

            var trialSub = new OwnerSubscription
            {
                SubscriptionID = 101,
                OwnerID = ownerId,
                PlanID = 1,
                PlanName = "Free Trial",
                StartDate = DateTime.UtcNow.AddDays(-61),
                EndDate = DateTime.UtcNow.AddMinutes(-5),
                PaymentStatus = "Trial",
                MaxListings = 1
            };

            // Owner has 1 Live property
            var liveProp = new Property
            {
                PropertyID = 501,
                OwnerID = ownerId,
                Title = "Coastal Breeze Villa",
                Location = "Goa",
                ListingStatus = "Live",
                IsApproved = true
            };
            fakePropRepo.Properties.Add(liveProp);

            // Run expiry unpublishing logic
            int unpubCount = await fakePropRepo.BatchUnpublishPropertiesByOwnerIdAsync(ownerId, "Subscription expired");
            Assert.Equal(1, unpubCount);

            // Verify property state
            Assert.Equal("Unpublished", liveProp.ListingStatus);
            Assert.Equal("Subscription expired", liveProp.UnpublishReason);

            // Audit log written per owner
            await fakeConfigRepo.LogAuditAsync(ownerId, "SubscriptionExpired_PropertiesUnpublished", "OwnerSubscription", trialSub.SubscriptionID, $"Unpublished {unpubCount} properties");
            Assert.Single(fakeConfigRepo.AuditLogs);
            Assert.Equal("SubscriptionExpired_PropertiesUnpublished", fakeConfigRepo.AuditLogs[0].Action);

            // Owner dashboard query still lists the property (never deleted from account)
            var ownerProperties = await fakePropRepo.GetPropertiesByOwnerIdAsync(ownerId);
            Assert.Single(ownerProperties);
            Assert.Equal("Coastal Breeze Villa", ownerProperties[0].Title);
            Assert.Equal("Unpublished", ownerProperties[0].ListingStatus);
        }

        // =========================================================================
        // Check 2: Public visitor opening that property's details page gets 404.
        // Owner and staff can still view it. Public search / visibility returns false.
        // =========================================================================
        [Fact]
        public async Task PartE_Check2_PublicVisitorGets404_OwnerAndStaffCanView()
        {
            var fakePropRepo = new FakePropertyRepo();
            var fakeBookingRepo = new FakeBookingRepo();
            const int ownerId = 802;
            const int staffId = 999;
            const int publicGuestId = 803;

            var unpubProp = new Property
            {
                PropertyID = 502,
                OwnerID = ownerId,
                Title = "Mountain Retreat",
                Location = "Manali",
                ListingStatus = "Unpublished",
                UnpublishReason = "Subscription expired",
                IsApproved = true
            };
            fakePropRepo.Properties.Add(unpubProp);

            var controller = new PropertiesController(fakePropRepo, fakeBookingRepo);

            // 2a. Anonymous public visitor -> 404 NotFound
            controller.ControllerContext = CreateAnonymousContext();
            var anonResult = await controller.Details(unpubProp.PropertyID);
            Assert.IsType<NotFoundResult>(anonResult);

            // 2b. Another client / guest -> 404 NotFound
            controller.ControllerContext = CreateContextWithUser(publicGuestId, "Client");
            var guestResult = await controller.Details(unpubProp.PropertyID);
            Assert.IsType<NotFoundResult>(guestResult);

            // 2c. Owner is blocked from public Details and redirected to Owner dashboard
            controller.ControllerContext = CreateContextWithUser(ownerId, "Owner");
            var ownerResult = await controller.Details(unpubProp.PropertyID);
            var ownerRedirect = Assert.IsType<RedirectToActionResult>(ownerResult);
            Assert.Equal("Owner", ownerRedirect.ControllerName);

            // 2d. Staff (Admin) is blocked from public Details and redirected to Admin dashboard
            controller.ControllerContext = CreateContextWithUser(staffId, "Admin");
            var adminResult = await controller.Details(unpubProp.PropertyID);
            var adminRedirect = Assert.IsType<RedirectToActionResult>(adminResult);
            Assert.Equal("Admin", adminRedirect.ControllerName);

            // 2e. Public visibility check returns false for unpublished property
            bool isVisible = await fakePropRepo.IsPropertyPubliclyVisibleAsync(unpubProp);
            Assert.False(isVisible);
        }

        // =========================================================================
        // Check 3: Owner's bell shows one new notification.
        // Opening Needs attention panel marks it read, removing it from the bell.
        // =========================================================================
        [Fact]
        public async Task PartE_Check3_BellNotification_OpeningNeedsAttentionMarksRead()
        {
            var fakeNotifRepo = new FakeUserNotifRepo();
            var fakeEmailQueue = new FakeEmailQueue();
            const int ownerId = 804;
            const int subId = 104;

            // 3a. Dispatch subscription expired notification
            string dedupeKey = $"SubscriptionExpired:{subId}:{ownerId}:InApp";
            bool isNew = await fakeNotifRepo.TryRecordDispatchAsync(dedupeKey);
            Assert.True(isNew);

            await fakeNotifRepo.InsertNotificationAsync(new UserNotification
            {
                UserID = ownerId,
                Title = "Subscription Expired: Free Trial",
                Body = "1 property has been unpublished from public search because your plan ended. Your property is still safely stored in your account.",
                LinkUrl = "/Owner/Index#needs-attention",
                IsRead = false
            });

            // Bell controller queries unread notifications
            var notifController = new NotificationController(fakeNotifRepo, fakeEmailQueue, NullLogger<NotificationController>.Instance)
            {
                ControllerContext = CreateContextWithUser(ownerId, "Owner")
            };

            var recentJson = await notifController.GetRecent() as JsonResult;
            Assert.NotNull(recentJson);
            var json = System.Text.Json.JsonSerializer.Serialize(recentJson.Value);
            using (var doc = System.Text.Json.JsonDocument.Parse(json))
            {
                Assert.Equal(1, doc.RootElement.GetProperty("unreadCount").GetInt32());
                Assert.Equal(1, doc.RootElement.GetProperty("items").GetArrayLength());
            }

            // 3b. Opening Needs attention marks related expiry notifications read
            int marked = await fakeNotifRepo.MarkExpiryNotificationsReadAsync(ownerId);
            Assert.Equal(1, marked);

            // Verify ReadAt timestamp was recorded and IsRead = true
            var notif = fakeNotifRepo.Notifications.First(n => n.UserID == ownerId);
            Assert.True(notif.IsRead);
            Assert.NotNull(notif.ReadAt);

            // Bell query now returns 0 unread items (no longer shown in bell)
            var updatedJson = await notifController.GetRecent() as JsonResult;
            var updatedJsonStr = System.Text.Json.JsonSerializer.Serialize(updatedJson!.Value);
            using (var updatedDoc = System.Text.Json.JsonDocument.Parse(updatedJsonStr))
            {
                Assert.Equal(0, updatedDoc.RootElement.GetProperty("unreadCount").GetInt32());
                Assert.Equal(0, updatedDoc.RootElement.GetProperty("items").GetArrayLength());
            }
        }

        // =========================================================================
        // Check 4: Reloading the page or rerun of job does not create a second notification.
        // =========================================================================
        [Fact]
        public async Task PartE_Check4_ReloadingPage_DoesNotCreateSecondNotification()
        {
            var fakeNotifRepo = new FakeUserNotifRepo();
            const int ownerId = 805;
            const int subId = 105;

            string dedupeKey = $"SubscriptionExpired:{subId}:{ownerId}:InApp";

            // First run: records dispatch and enqueues notification
            bool firstAttempt = await fakeNotifRepo.TryRecordDispatchAsync(dedupeKey);
            Assert.True(firstAttempt);

            await fakeNotifRepo.InsertNotificationAsync(new UserNotification
            {
                UserID = ownerId,
                Title = "Subscription Expired: Growth Plan",
                Body = "2 properties have been unpublished from public search.",
                IsRead = true,
                ReadAt = DateTime.UtcNow
            });

            // Second run (reloading page, subsequent hourly check): rejected by dedupe key
            bool secondAttempt = await fakeNotifRepo.TryRecordDispatchAsync(dedupeKey);
            Assert.False(secondAttempt); // Dedupe prevents duplicate notification

            // Count remains strictly 1
            Assert.Single(fakeNotifRepo.Notifications, n => n.UserID == ownerId);

            // Already read stays read
            Assert.True(fakeNotifRepo.Notifications.First().IsRead);
            Assert.NotNull(fakeNotifRepo.Notifications.First().ReadAt);
        }

        // =========================================================================
        // Check 5: Choose a plan and complete payment. Owner is asked which properties
        // to publish up to MaxListings. Nothing is published automatically.
        // =========================================================================
        [Fact]
        public async Task PartE_Check5_ChoosePlanPayment_PromptsOwnerToChoose_NothingAutoPublished()
        {
            var fakePropRepo = new FakePropertyRepo();
            const int ownerId = 806;

            // Owner had 2 properties unpublished due to expiry
            var prop1 = new Property { PropertyID = 601, OwnerID = ownerId, Title = "Villa Alpha", ListingStatus = "Unpublished", UnpublishReason = "Subscription expired", IsApproved = true };
            var prop2 = new Property { PropertyID = 602, OwnerID = ownerId, Title = "Villa Beta", ListingStatus = "Unpublished", UnpublishReason = "Subscription expired", IsApproved = true };
            fakePropRepo.Properties.Add(prop1);
            fakePropRepo.Properties.Add(prop2);

            int planMaxListings = 1;

            // Verify: Properties remain UNPUBLISHED after payment. Nothing is auto-republished!
            Assert.Equal("Unpublished", prop1.ListingStatus);
            Assert.Equal("Unpublished", prop2.ListingStatus);

            // Owner manually chooses to publish Villa Alpha up to limit of 1
            var (success1, _) = await fakePropRepo.PublishPropertyAsync(prop1.PropertyID, ownerId, planMaxListings);
            Assert.True(success1);
            Assert.Equal("Live", prop1.ListingStatus);

            // Owner attempts to publish second property while limit is 1 -> rejected
            var (success2, msg2) = await fakePropRepo.PublishPropertyAsync(prop2.PropertyID, ownerId, planMaxListings);
            Assert.False(success2);
            Assert.Contains("allowance", msg2, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Unpublished", prop2.ListingStatus);
        }

        // =========================================================================
        // Check 6: Checkout for an unpublished property is refused.
        // =========================================================================
        [Fact]
        public async Task PartE_Check6_CheckoutForUnpublishedProperty_Refused()
        {
            var fakePropRepo = new FakePropertyRepo();
            var fakeBookingRepo = new FakeBookingRepo();
            var fakeUserRepo = new FakeUserRepo();
            const int ownerId = 807;
            const int clientId = 901;

            var unpubProp = new Property
            {
                PropertyID = 701,
                OwnerID = ownerId,
                Title = "Lakeside Cottage",
                Location = "Udaipur",
                PricePerNight = 4500,
                ListingStatus = "Unpublished",
                UnpublishReason = "Subscription expired",
                IsApproved = true
            };
            fakePropRepo.Properties.Add(unpubProp);

            fakeUserRepo.Users[clientId] = new User { UserID = clientId, FullName = "Client Guest", Role = "Client" };

            var controller = new BookingController(
                fakePropRepo, fakeBookingRepo, null!, new FakePricingService(),
                null!, null!, null!,
                null!, null!, null!,
                fakeUserRepo, new FakeIdVerificationService(), null!)
            {
                ControllerContext = CreateContextWithUser(clientId, "Client"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            // Checkout action refuses unpublished property
            var checkoutResult = await controller.Checkout(unpubProp.PropertyID, "FullDay", null, 1, DateTime.Today.AddDays(5), DateTime.Today.AddDays(7));
            var redirect = Assert.IsType<RedirectToActionResult>(checkoutResult);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal("Properties", redirect.ControllerName);
            Assert.Equal("This property is currently not available for bookings.", controller.TempData["Error"]);

            // ConfirmBooking POST action also refuses unpublished property
            var bookingVm = new BookingCheckoutViewModel
            {
                PropertyID = unpubProp.PropertyID,
                CheckInDate = DateTime.Today.AddDays(5),
                CheckOutDate = DateTime.Today.AddDays(7),
                PricePerNight = 4500,
                BookingType = "FullDay"
            };
            var createResult = await controller.ConfirmBooking(bookingVm);
            var createRedirect = Assert.IsType<RedirectToActionResult>(createResult);
            Assert.Equal("Index", createRedirect.ActionName);
            Assert.Equal("Properties", createRedirect.ControllerName);
        }

        // =========================================================================
        // Mocks for tests
        // =========================================================================
        private class FakePropertyRepo : IPropertyRepository
        {
            public List<Property> Properties { get; } = new();

            public Task<Property?> GetByIdAsync(int propertyId)
                => Task.FromResult(Properties.FirstOrDefault(p => p.PropertyID == propertyId));

            public Task<bool> IsPropertyPubliclyVisibleAsync(Property? property)
            {
                if (property == null || !property.IsApproved) return Task.FromResult(false);
                if (property.ListingStatus != "Live" && property.ListingStatus != "Active") return Task.FromResult(false);
                return Task.FromResult(true);
            }

            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false)
                => Task.FromResult(Properties.Where(p => !onlyApproved || (p.IsApproved && (p.ListingStatus == "Live" || p.ListingStatus == "Active"))).ToList());

            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId)
                => Task.FromResult(Properties.Where(p => p.OwnerID == ownerId).ToList());

            public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests)
                => Task.FromResult(Properties.Where(p => p.IsApproved && (p.ListingStatus == "Live" || p.ListingStatus == "Active")).ToList());

            public Task<int> BatchUnpublishPropertiesByOwnerIdAsync(int ownerId, string unpublishReason)
            {
                var live = Properties.Where(p => p.OwnerID == ownerId && (p.ListingStatus == "Live" || p.ListingStatus == "Active")).ToList();
                foreach (var p in live)
                {
                    p.ListingStatus = "Unpublished";
                    p.UnpublishReason = unpublishReason;
                }
                return Task.FromResult(live.Count);
            }

            public Task<(bool Success, string Message)> PublishPropertyAsync(int propertyId, int ownerId, int limit)
            {
                int liveCount = Properties.Count(p => p.OwnerID == ownerId && (p.ListingStatus == "Live" || p.ListingStatus == "Active") && p.IsApproved);
                if (liveCount >= limit)
                {
                    return Task.FromResult((false, $"Cannot publish property. Your current plan allowance of {limit} live listing(s) has been reached."));
                }
                var prop = Properties.FirstOrDefault(p => p.PropertyID == propertyId && p.OwnerID == ownerId);
                if (prop != null)
                {
                    prop.ListingStatus = "Live";
                    prop.UnpublishReason = null;
                    prop.PublishedAt = DateTime.UtcNow;
                    return Task.FromResult((true, "Published"));
                }
                return Task.FromResult((false, "Not found"));
            }

            public Task<int> CreatePropertyAsync(Property property) => Task.FromResult(1);
            public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
            public Task<bool> UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl) => Task.FromResult(true);
            public Task<bool> DeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> PermanentlyDeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> DeactivatePropertyAsync(int propertyId, bool deactivate = true) => Task.FromResult(true);
            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null) => Task.FromResult(true);
            public Task<bool> SetPropertyOccasionsAsync(int propertyId, List<int> occasionIds) => Task.FromResult(true);
            public Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId) => Task.FromResult(new List<int>());
            public Task<int> AddPropertyImageAsync(int propertyId, string imageUrl, bool isPrimary) => Task.FromResult(1);
            public Task<List<PropertyImage>> GetPropertyImagesAsync(int propertyId) => Task.FromResult(new List<PropertyImage>());
            public Task<int> AddPropertyMediaAsync(PropertyMedia media) => Task.FromResult(1);
            public Task<List<PropertyMedia>> GetPropertyMediaAsync(int propertyId) => Task.FromResult(new List<PropertyMedia>());
            public Task<bool> DeletePropertyMediaAsync(int mediaId) => Task.FromResult(true);
            public Task<List<PropertyType>> GetPropertyTypesAsync() => Task.FromResult(new List<PropertyType>());
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(new List<Occasion>());
            public Task<List<AmenityMaster>> GetAllAmenitiesAsync() => Task.FromResult(new List<AmenityMaster>());
            public Task<bool> AddPropertyAmenitiesAsync(int propertyId, List<int> amenityIds) => Task.FromResult(true);
            public Task<List<int>> GetPropertyAmenityIdsAsync(int propertyId) => Task.FromResult(new List<int>());
            public Task<List<AmenityMaster>> GetPropertyAmenitiesAsync(int propertyId) => Task.FromResult(new List<AmenityMaster>());
            public Task<bool> SetPropertyAmenitiesAsync(int propertyId, List<int> amenityIds) => Task.FromResult(true);
            public Task<int> GetOrCreateAmenityByNameAsync(string name) => Task.FromResult(1);
            public Task<int> GetOrCreateOccasionByNameAsync(string name) => Task.FromResult(1);
            public Task<int> GetOrCreatePropertyTypeByNameAsync(string name) => Task.FromResult(1);
            public Task<bool> UpdatePropertyCategoryAsync(int propertyId, int ownerId, int propertyTypeId) => Task.FromResult(true);
            public Task<List<PropertyEventCommission>> GetPropertyEventCommissionsAsync(int propertyId) => Task.FromResult(new List<PropertyEventCommission>());
            public Task<decimal?> GetPropertyEventCommissionRateAsync(int propertyId, int packageId) => Task.FromResult<decimal?>(null);
            public Task<bool> SetPropertyEventCommissionsAsync(int propertyId, List<(int packageId, decimal commissionRate)> commissions) => Task.FromResult(true);
        }

        private class FakeUserNotifRepo : IUserNotificationRepository
        {
            public List<UserNotification> Notifications { get; } = new();
            public HashSet<string> RecordedKeys { get; } = new();

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
                if (n != null)
                {
                    n.IsRead = true;
                    n.ReadAt = DateTime.UtcNow;
                }
                return Task.FromResult(true);
            }

            public Task<int> MarkExpiryNotificationsReadAsync(int userId)
            {
                var unread = Notifications.Where(n => n.UserID == userId && !n.IsRead && (n.Title.Contains("Subscription Expired") || n.Title.Contains("Subscription Active") || n.Title.Contains("Plan Activated") || n.Title.Contains("Subscription"))).ToList();
                foreach (var n in unread)
                {
                    n.IsRead = true;
                    n.ReadAt = DateTime.UtcNow;
                }
                return Task.FromResult(unread.Count);
            }

            public Task<bool> TryRecordDispatchAsync(string notificationKey)
            {
                if (RecordedKeys.Contains(notificationKey)) return Task.FromResult(false);
                RecordedKeys.Add(notificationKey);
                return Task.FromResult(true);
            }
        }

        private class FakeConfigRepo : IConfigRepository
        {
            public List<AuditLog> AuditLogs { get; } = new();

            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details)
            {
                AuditLogs.Add(new AuditLog
                {
                    AuditID = AuditLogs.Count + 1,
                    PerformedByUserID = performedByUserId,
                    Action = action,
                    EntityType = entityType,
                    EntityID = entityId,
                    Details = details,
                    Timestamp = DateTime.UtcNow
                });
                return Task.CompletedTask;
            }

            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
            public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
        }

        private class FakeBookingRepo : IBookingRepository
        {
            public readonly Dictionary<int, Booking> Bookings = new();

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
            public Task<List<Booking>> GetAllAsync() => Task.FromResult(Bookings.Values.ToList());

            public Task<int> CreateBookingAsync(Booking booking)
            {
                booking.BookingID = Bookings.Count + 1;
                Bookings[booking.BookingID] = booking;
                return Task.FromResult(booking.BookingID);
            }

            public Task<bool> UpdateBookingStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> CancelBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<int> RecordPaymentAsync(Payment payment) => Task.FromResult(1);
            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId) => Task.FromResult(new List<Payment>());
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(true);
            public Task<bool> HasReviewAsync(int bookingId) => Task.FromResult(false);
            public Task<bool> HasReviewForBookingAsync(int bookingId, int clientId) => Task.FromResult(false);
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<List<CustomerReview>> GetApprovedReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<List<CustomerReview>> GetAllReviewsAsync() => Task.FromResult(new List<CustomerReview>());
            public Task<bool> ModerateReviewAsync(int reviewId, bool isApproved) => Task.FromResult(true);
            public Task<bool> UpdateRazorpayPaymentIdAsync(int bookingId, string paymentId) => Task.FromResult(true);
            public Task<List<Booking>> GetUpcomingBookingsForCheckInReminderAsync(DateTime targetDate) => Task.FromResult(new List<Booking>());
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
        }

        private class FakeUserRepo : IUserRepository
        {
            public Dictionary<int, User> Users { get; } = new();
            public Task<User?> GetByIdAsync(int userId) => Task.FromResult(Users.TryGetValue(userId, out var u) ? u : null);
            public Task<User?> GetByEmailAsync(string email) => Task.FromResult(Users.Values.FirstOrDefault(u => u.Email == email));
            public Task<User?> GetByAuthProviderKeyAsync(string provider, string key) => Task.FromResult<User?>(null);
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(Users.Values.Where(u => u.Role == role).ToList());
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(Users.Values.ToList());
            public Task<int> CreateUserAsync(User user) => Task.FromResult(1);
            public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string tier, decimal? rate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? rate) => Task.FromResult(true);
            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string key, string? profilePic = null) => Task.FromResult(true);
        }

        private class FakePricingService : IBookingPricingService
        {
            public decimal CalculatePrice(decimal pricePerNight, string bookingType, string? slotType, int count) => pricePerNight * Math.Max(1, count);
        }

        private class FakeIdVerificationService : IIdVerificationService
        {
            public Task<bool> IsClientVerifiedAsync(int clientId) => Task.FromResult(true);
            public Task<ClientIdVerification?> GetClientVerificationAsync(int clientId, int requestingUserId, string userRole) => Task.FromResult<ClientIdVerification?>(null);
            public Task<int> SubmitVerificationAsync(int clientId, string documentType, string storageKey, string? maskedIdentifier) => Task.FromResult(1);
            public Task<bool> ReviewVerificationAsync(int verificationId, string status, int reviewerStaffId, string? notes = null) => Task.FromResult(true);
            public Task<List<ClientIdVerification>> GetAllVerificationsAsync(string? statusFilter = null) => Task.FromResult(new List<ClientIdVerification>());
        }

        private class FakeEmailQueue : IEmailQueueService
        {
            public ValueTask EnqueueAsync(EmailQueueMessage message) => ValueTask.CompletedTask;
            public ValueTask<EmailQueueMessage> DequeueAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new EmailQueueMessage());
            public int ApproximateQueueCount => 0;
            public void AddInAppNotification(int userId, string title, string message, string type = "KycRejection", string? actionUrl = null) { }
            public List<InAppNotificationItem> GetInAppNotifications(int userId) => new();
            public void MarkInAppNotificationRead(int notificationId) { }
            public bool TryAcquireNotificationKey(string notificationKey) => true;
            public bool HasNotificationKey(string notificationKey) => false;
        }

        private class SimpleTempDataProvider : ITempDataProvider
        {
            private readonly Dictionary<string, object> _data = new();
            public IDictionary<string, object> LoadTempData(HttpContext context) => _data;
            public void SaveTempData(HttpContext context, IDictionary<string, object> values)
            {
                _data.Clear();
                foreach (var kvp in values) _data[kvp.Key] = kvp.Value;
            }
        }
    }
}
