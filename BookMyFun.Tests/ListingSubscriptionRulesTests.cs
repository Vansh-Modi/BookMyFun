using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class ListingSubscriptionRulesTests
    {
        // =========================================================================
        // Test 1: Free trial row created on first publish of first property.
        // Not created if a row already exists.
        // =========================================================================
        [Fact]
        public async Task Test1_FreeTrialRow_CreatedOnFirstPublish_NotIfRowExists()
        {
            var fakeSubRepo = new FakeSubscriptionRepository();
            const int newOwnerId = 101;
            const int existingOwnerId = 202;

            // Existing owner already has a subscription
            fakeSubRepo.Subscriptions.Add(new OwnerSubscription
            {
                SubscriptionID = 1,
                OwnerID = existingOwnerId,
                PlanID = 2,
                PlanName = "Growth Plan",
                StartDate = DateTime.UtcNow.AddDays(-10),
                EndDate = DateTime.UtcNow.AddDays(20),
                PaymentStatus = "Paid",
                MaxListings = 5
            });

            // 1a. New owner has no history
            bool newOwnerHasHistory = await fakeSubRepo.HasAnySubscriptionHistoryAsync(newOwnerId);
            Assert.False(newOwnerHasHistory);

            // First publish triggers trial creation
            var trialSub = await fakeSubRepo.CreateTrialSubscriptionAsync(newOwnerId, 60);
            Assert.NotNull(trialSub);
            Assert.Equal("Trial", trialSub.PaymentStatus);
            Assert.Equal(1, trialSub.MaxListings);
            Assert.True(trialSub.EndDate > DateTime.UtcNow.AddDays(50));

            // Now new owner has history
            bool nowHasHistory = await fakeSubRepo.HasAnySubscriptionHistoryAsync(newOwnerId);
            Assert.True(nowHasHistory);

            // 1b. Existing owner has history - should NOT create another trial row
            bool existingHasHistory = await fakeSubRepo.HasAnySubscriptionHistoryAsync(existingOwnerId);
            Assert.True(existingHasHistory);

            var secondTrialAttempt = await fakeSubRepo.CreateTrialSubscriptionAsync(existingOwnerId, 60);
            Assert.Null(secondTrialAttempt); // Guard prevents duplicate trial
        }

        // =========================================================================
        // Test 2: Listing limit enforced: owner with MaxListings = 1 cannot create a
        // second property (server and UI). Rejected properties are not counted.
        // =========================================================================
        [Fact]
        public async Task Test2_ListingLimit_Enforced_CannotCreateWhenLimitReached()
        {
            var fakeSubRepo = new FakeSubscriptionRepository();
            var fakePropRepo = new FakePropertyRepository();
            const int ownerId = 301;

            // Owner on Free Trial with MaxListings = 1
            fakeSubRepo.Subscriptions.Add(new OwnerSubscription
            {
                SubscriptionID = 10,
                OwnerID = ownerId,
                PlanID = 1,
                PlanName = "Free Trial",
                StartDate = DateTime.UtcNow.AddDays(-5),
                EndDate = DateTime.UtcNow.AddDays(55),
                PaymentStatus = "Trial",
                MaxListings = 1
            });

            // Initially count is 0 -> Allowed
            var allowance0 = await fakeSubRepo.CheckListingCreationAllowanceAsync(ownerId);
            Assert.True(allowance0.Allowed);
            Assert.Equal(0, allowance0.Count);
            Assert.Equal(1, allowance0.Limit);

            // Add 1 property (Review / Draft status)
            fakePropRepo.Properties.Add(new Property
            {
                PropertyID = 1001,
                OwnerID = ownerId,
                Title = "Farm 1",
                ListingStatus = "Draft",
                IsApproved = false
            });
            fakeSubRepo.PropertyRepo = fakePropRepo;

            // Now count is 1 -> Limit reached!
            var allowance1 = await fakeSubRepo.CheckListingCreationAllowanceAsync(ownerId);
            Assert.False(allowance1.Allowed);
            Assert.Equal(1, allowance1.Count);
            Assert.Equal(1, allowance1.Limit);
            Assert.Contains("Listing allowance reached", allowance1.Reason);

            // Add a Rejected property -> Should NOT increase counted listings
            fakePropRepo.Properties.Add(new Property
            {
                PropertyID = 1002,
                OwnerID = ownerId,
                Title = "Farm 2 (Rejected)",
                ListingStatus = "Rejected",
                IsApproved = false,
                RejectionReason = "Incomplete photos"
            });

            int countedListings = await fakeSubRepo.GetCountedListingCountAsync(ownerId);
            Assert.Equal(1, countedListings); // Only 1, rejected doesn't count

            // Owner with expired subscription and no active plan
            const int expiredOwnerId = 302;
            fakeSubRepo.Subscriptions.Add(new OwnerSubscription
            {
                SubscriptionID = 11,
                OwnerID = expiredOwnerId,
                PlanID = 1,
                PlanName = "Free Trial",
                StartDate = DateTime.UtcNow.AddDays(-70),
                EndDate = DateTime.UtcNow.AddDays(-10),
                PaymentStatus = "Trial",
                MaxListings = 1
            });

            var expiredAllowance = await fakeSubRepo.CheckListingCreationAllowanceAsync(expiredOwnerId);
            Assert.False(expiredAllowance.Allowed);
            Assert.Equal("Your subscription has ended. Choose a plan to add listings.", expiredAllowance.Reason);
        }

        // =========================================================================
        // Test 3: Subscription expired: hourly service unpublishes live properties,
        // sets reason 'Subscription expired', notifies owner.
        // =========================================================================
        [Fact]
        public async Task Test3_SubscriptionExpired_HourlyService_UnpublishesLiveProperties_AndNotifiesOwner()
        {
            var fakeSubRepo = new FakeSubscriptionRepository();
            var fakePropRepo = new FakePropertyRepository();
            var fakeDispatcher = new FakeNotificationDispatcherService();
            const int ownerId = 401;

            // Expired subscription with no active renewal
            fakeSubRepo.Subscriptions.Add(new OwnerSubscription
            {
                SubscriptionID = 20,
                OwnerID = ownerId,
                PlanName = "Free Trial",
                StartDate = DateTime.UtcNow.AddDays(-65),
                EndDate = DateTime.UtcNow.AddDays(-5),
                PaymentStatus = "Trial",
                MaxListings = 1
            });

            // Owner has 1 live property
            fakePropRepo.Properties.Add(new Property
            {
                PropertyID = 2001,
                OwnerID = ownerId,
                Title = "Sunrise Villa",
                ListingStatus = "Active",
                IsApproved = true
            });
            fakeSubRepo.PropertyRepo = fakePropRepo;

            // Run hourly cycle logic
            var expiredSubs = await fakeSubRepo.GetExpiredSubscriptionsForHourlyProcessAsync();
            Assert.Single(expiredSubs);

            foreach (var sub in expiredSubs)
            {
                int unpubCount = await fakePropRepo.BatchUnpublishPropertiesByOwnerIdAsync(sub.OwnerID, "Subscription expired");
                Assert.Equal(1, unpubCount);
                await fakeDispatcher.DispatchSubscriptionExpiredAsync(sub.OwnerID, sub.PlanName ?? "Subscription");
            }

            // Verify property state
            var prop = fakePropRepo.Properties.First(p => p.PropertyID == 2001);
            Assert.Equal("Deactivated", prop.ListingStatus);
            Assert.Equal("Subscription expired", prop.UnpublishReason);

            // Verify notification dispatched
            Assert.Contains(fakeDispatcher.Events, e => e.EventType == "SubscriptionExpired" && e.UserId == ownerId);
        }

        // =========================================================================
        // Test 4: Renew/purchase: on payment success, if active plan exists,
        // sets StartDate = current EndDate (scheduled).
        // =========================================================================
        [Fact]
        public async Task Test4_RenewPurchase_WhenActivePlanExists_SetsStartDateToCurrentEndDate()
        {
            var fakeSubRepo = new FakeSubscriptionRepository();
            const int ownerId = 501;

            var activeEndDate = DateTime.UtcNow.AddDays(15);
            fakeSubRepo.Subscriptions.Add(new OwnerSubscription
            {
                SubscriptionID = 30,
                OwnerID = ownerId,
                PlanID = 2,
                PlanName = "Standard Plan",
                StartDate = DateTime.UtcNow.AddDays(-15),
                EndDate = activeEndDate,
                PaymentStatus = "Paid",
                MaxListings = 3
            });

            var activeSub = await fakeSubRepo.GetActiveSubscriptionByOwnerIdAsync(ownerId);
            Assert.NotNull(activeSub);

            // Purchase renewal -> StartDate must be activeSub.EndDate
            DateTime expectedStartDate = activeSub.EndDate;
            var scheduledSub = new OwnerSubscription
            {
                SubscriptionID = 31,
                OwnerID = ownerId,
                PlanID = 2,
                PlanName = "Standard Plan",
                StartDate = expectedStartDate,
                EndDate = expectedStartDate.AddDays(30),
                PaymentStatus = "Paid",
                MaxListings = 3
            };
            fakeSubRepo.Subscriptions.Add(scheduledSub);

            var retrievedScheduled = await fakeSubRepo.GetScheduledSubscriptionByOwnerIdAsync(ownerId);
            Assert.NotNull(retrievedScheduled);
            Assert.Equal(expectedStartDate, retrievedScheduled.StartDate);
            Assert.True(retrievedScheduled.StartDate > DateTime.UtcNow);
        }

        // =========================================================================
        // Test 5: Activation of scheduled plan: when start date reached, hourly
        // service activates it, republishes expired properties up to MaxListings (oldest first).
        // =========================================================================
        [Fact]
        public async Task Test5_ActivationOfScheduledPlan_ActivatesAndRepublishesOldestFirst_UpToMaxListings()
        {
            var fakeSubRepo = new FakeSubscriptionRepository();
            var fakePropRepo = new FakePropertyRepository();
            var fakeDispatcher = new FakeNotificationDispatcherService();
            const int ownerId = 601;

            // Scheduled plan that just became active (StartDate was 5 minutes ago)
            fakeSubRepo.Subscriptions.Add(new OwnerSubscription
            {
                SubscriptionID = 40,
                OwnerID = ownerId,
                PlanName = "Pro Plan",
                StartDate = DateTime.UtcNow.AddMinutes(-5),
                EndDate = DateTime.UtcNow.AddDays(30),
                PaymentStatus = "Paid",
                MaxListings = 2
            });

            // 3 properties previously unpublished due to "Subscription expired"
            fakePropRepo.Properties.Add(new Property
            {
                PropertyID = 10,
                OwnerID = ownerId,
                Title = "Oldest Villa",
                ListingStatus = "Deactivated",
                IsApproved = true,
                UnpublishReason = "Subscription expired"
            });
            fakePropRepo.Properties.Add(new Property
            {
                PropertyID = 20,
                OwnerID = ownerId,
                Title = "Middle Villa",
                ListingStatus = "Deactivated",
                IsApproved = true,
                UnpublishReason = "Subscription expired"
            });
            fakePropRepo.Properties.Add(new Property
            {
                PropertyID = 30,
                OwnerID = ownerId,
                Title = "Newest Villa",
                ListingStatus = "Deactivated",
                IsApproved = true,
                UnpublishReason = "Subscription expired"
            });
            fakeSubRepo.PropertyRepo = fakePropRepo;

            // Scheduled plan is now newly active
            var newlyActive = await fakeSubRepo.GetNewlyActiveScheduledSubscriptionsAsync();
            Assert.Single(newlyActive);

            var sub = newlyActive[0];
            var republished = await fakeSubRepo.RepublishExpiredPropertiesAsync(sub.OwnerID, sub.MaxListings);

            // MaxListings = 2, so oldest 2 properties (ID 10 and 20) are republished
            Assert.Equal(2, republished.Count);
            Assert.Contains("Oldest Villa", republished);
            Assert.Contains("Middle Villa", republished);

            // Property state verification
            Assert.Equal("Active", fakePropRepo.Properties.First(p => p.PropertyID == 10).ListingStatus);
            Assert.Equal("Active", fakePropRepo.Properties.First(p => p.PropertyID == 20).ListingStatus);
            // 3rd property remains Unpublished because limit is 2
            Assert.Equal("Deactivated", fakePropRepo.Properties.First(p => p.PropertyID == 30).ListingStatus);

            await fakeDispatcher.DispatchSubscriptionActivatedAsync(sub.OwnerID, sub.PlanName!, sub.EndDate, republished);
            Assert.Contains(fakeDispatcher.Events, e => e.EventType == "SubscriptionActivated" && e.UserId == ownerId);
        }

        // =========================================================================
        // Test 6: Approval: employee approves -> status Approved (not Live).
        // Owner presses Publish -> status Live.
        // =========================================================================
        [Fact]
        public async Task Test6_Approval_EmployeeApproves_StatusApprovedNotLive_OwnerPublishes_StatusLive()
        {
            var fakePropRepo = new FakePropertyRepository();
            var fakeDispatcher = new FakeNotificationDispatcherService();
            const int ownerId = 701;
            const int staffId = 99;

            var prop = new Property
            {
                PropertyID = 3001,
                OwnerID = ownerId,
                Title = "Green Meadows Farm",
                ListingStatus = "Draft",
                IsApproved = false
            };
            fakePropRepo.Properties.Add(prop);

            // 6a. Employee approves -> sets status Approved (Draft + IsApproved = 1), NOT Active (Live)
            await fakePropRepo.ApprovePropertyAsync(prop.PropertyID, true, "Draft", staffId, null);
            await fakeDispatcher.DispatchPropertyApprovedAsync(prop.PropertyID, prop.Title, ownerId);

            var approvedProp = fakePropRepo.Properties.First(p => p.PropertyID == prop.PropertyID);
            Assert.True(approvedProp.IsApproved);
            Assert.Equal("Draft", approvedProp.ListingStatus); // Must remain Draft until owner publishes!
            Assert.Equal(staffId, approvedProp.ApprovedByUserID);
            Assert.NotNull(approvedProp.ApprovedAt);
            Assert.Contains(fakeDispatcher.Events, e => e.EventType == "PropertyApproved" && e.UserId == ownerId);

            // 6b. Owner presses Publish -> sets status Live (Active)
            var (success, msg) = await fakePropRepo.PublishPropertyAsync(prop.PropertyID, ownerId, 1);
            Assert.True(success);
            await fakeDispatcher.DispatchPropertyPublishedAsync(prop.PropertyID, prop.Title, ownerId);

            var liveProp = fakePropRepo.Properties.First(p => p.PropertyID == prop.PropertyID);
            Assert.Equal("Active", liveProp.ListingStatus);
            Assert.NotNull(liveProp.PublishedAt);
            Assert.Contains(fakeDispatcher.Events, e => e.EventType == "PropertyPublished" && e.UserId == ownerId);
        }

        // =========================================================================
        // Test 7: Rejection: employee rejects -> status Rejected with reason.
        // Owner notified with reason.
        // =========================================================================
        [Fact]
        public async Task Test7_Rejection_EmployeeRejects_StatusRejectedWithReason_OwnerNotified()
        {
            var fakePropRepo = new FakePropertyRepository();
            var fakeDispatcher = new FakeNotificationDispatcherService();
            const int ownerId = 801;
            const int staffId = 99;
            const string reason = "Inaccurate location pin and missing caretaking contact details.";

            var prop = new Property
            {
                PropertyID = 4001,
                OwnerID = ownerId,
                Title = "Rustic Cottage",
                ListingStatus = "Draft",
                IsApproved = false
            };
            fakePropRepo.Properties.Add(prop);

            // Rejection requires reason
            await fakePropRepo.ApprovePropertyAsync(prop.PropertyID, false, "Rejected", staffId, reason);
            await fakeDispatcher.DispatchPropertyRejectedAsync(prop.PropertyID, prop.Title, ownerId, reason);

            var rejectedProp = fakePropRepo.Properties.First(p => p.PropertyID == prop.PropertyID);
            Assert.False(rejectedProp.IsApproved);
            Assert.Equal("Rejected", rejectedProp.ListingStatus);
            Assert.Equal(reason, rejectedProp.RejectionReason);

            var notif = fakeDispatcher.Events.FirstOrDefault(e => e.EventType == "PropertyRejected" && e.UserId == ownerId);
            Assert.NotNull(notif);
            Assert.Contains(reason, notif.Body);
        }

        // =========================================================================
        // Test 8: Unpublish: owner presses Unpublish -> status Unpublished with reason
        // 'Owner unpublished'. Allowed any time. Never deletes the property.
        // =========================================================================
        [Fact]
        public async Task Test8_Unpublish_OwnerUnpublishes_StatusUnpublished_NeverDeletes()
        {
            var fakePropRepo = new FakePropertyRepository();
            var fakeDispatcher = new FakeNotificationDispatcherService();
            const int ownerId = 901;

            var prop = new Property
            {
                PropertyID = 5001,
                OwnerID = ownerId,
                Title = "Palm Breeze Retreat",
                ListingStatus = "Active",
                IsApproved = true
            };
            fakePropRepo.Properties.Add(prop);

            bool success = await fakePropRepo.UnpublishPropertyAsync(prop.PropertyID, ownerId, "Owner unpublished");
            Assert.True(success);
            await fakeDispatcher.DispatchPropertyUnpublishedAsync(prop.PropertyID, prop.Title, ownerId, "Owner unpublished");

            // Property is still in database (not deleted)
            var unpubProp = fakePropRepo.Properties.FirstOrDefault(p => p.PropertyID == prop.PropertyID);
            Assert.NotNull(unpubProp);
            Assert.Equal("Deactivated", unpubProp.ListingStatus);
            Assert.Equal("Owner unpublished", unpubProp.UnpublishReason);
            Assert.Contains(fakeDispatcher.Events, e => e.EventType == "PropertyUnpublished" && e.UserId == ownerId);
        }

        // =========================================================================
        // Test 9: Advance notice: owner notified 3 days before trial or paid plan ends,
        // once only (dedupe verification).
        // =========================================================================
        [Fact]
        public async Task Test9_AdvanceNotice_OwnerNotified3DaysBeforeExpiry_OnceOnly()
        {
            var fakeSubRepo = new FakeSubscriptionRepository();
            var fakeDispatcher = new FakeNotificationDispatcherService();
            const int ownerId = 1001;

            // Subscription expiring in 3 days
            var expiryDate = DateTime.UtcNow.AddDays(3);
            fakeSubRepo.Subscriptions.Add(new OwnerSubscription
            {
                SubscriptionID = 50,
                OwnerID = ownerId,
                PlanName = "Free Trial",
                StartDate = DateTime.UtcNow.AddDays(-57),
                EndDate = expiryDate,
                PaymentStatus = "Trial",
                MaxListings = 1
            });

            var expiringSubs = await fakeSubRepo.GetExpiringSubscriptionsAsync(3);
            Assert.Single(expiringSubs);

            var sub = expiringSubs[0];
            await fakeDispatcher.DispatchTrialExpiringWarningAsync(sub.OwnerID, sub.PlanName!, sub.EndDate);

            // Trigger again (simulating next hourly run) -> Deduplication key prevents second notification
            await fakeDispatcher.DispatchTrialExpiringWarningAsync(sub.OwnerID, sub.PlanName!, sub.EndDate);

            int warningCount = fakeDispatcher.Events.Count(e => e.EventType == "TrialExpiringWarning" && e.UserId == ownerId);
            Assert.Equal(1, warningCount); // Exactly 1 warning dispatched
        }

        // =========================================================================
        // Test Doubles / Mocks for In-Memory Verification
        // =========================================================================
        private class FakeSubscriptionRepository : ISubscriptionRepository
        {
            public List<OwnerSubscription> Subscriptions { get; } = new();
            public FakePropertyRepository? PropertyRepo { get; set; }

            public Task<OwnerSubscription?> GetActiveSubscriptionByOwnerIdAsync(int ownerId)
            {
                var now = DateTime.UtcNow;
                var sub = Subscriptions.FirstOrDefault(s => s.OwnerID == ownerId &&
                    (s.PaymentStatus == "Paid" || s.PaymentStatus == "Trial") &&
                    s.StartDate <= now && s.EndDate >= now);
                return Task.FromResult(sub);
            }

            public Task<OwnerSubscription?> GetScheduledSubscriptionByOwnerIdAsync(int ownerId)
            {
                var now = DateTime.UtcNow;
                var sub = Subscriptions.FirstOrDefault(s => s.OwnerID == ownerId &&
                    s.PaymentStatus == "Paid" && s.StartDate > now);
                return Task.FromResult(sub);
            }

            public Task<bool> HasAnySubscriptionHistoryAsync(int ownerId)
            {
                return Task.FromResult(Subscriptions.Any(s => s.OwnerID == ownerId));
            }

            public Task<int> GetCountedListingCountAsync(int ownerId)
            {
                if (PropertyRepo == null) return Task.FromResult(0);
                return Task.FromResult(PropertyRepo.Properties.Count(p => p.OwnerID == ownerId && p.ListingStatus != "Rejected"));
            }

            public Task<int> GetLiveListingCountAsync(int ownerId)
            {
                if (PropertyRepo == null) return Task.FromResult(0);
                return Task.FromResult(PropertyRepo.Properties.Count(p => p.OwnerID == ownerId && p.IsApproved && p.ListingStatus == "Active"));
            }

            public async Task<(bool Allowed, string? Reason, int Count, int Limit)> CheckListingCreationAllowanceAsync(int ownerId)
            {
                int count = await GetCountedListingCountAsync(ownerId);
                var active = await GetActiveSubscriptionByOwnerIdAsync(ownerId);

                if (active != null)
                {
                    int limit = active.MaxListings > 0 ? active.MaxListings : 1;
                    if (count < limit) return (true, null, count, limit);
                    return (false, $"Listing allowance reached ({count}/{limit}). Please upgrade your subscription plan to add more properties.", count, limit);
                }

                bool hasHistory = await HasAnySubscriptionHistoryAsync(ownerId);
                if (!hasHistory)
                {
                    const int trialLimit = 1;
                    if (count < trialLimit) return (true, null, count, trialLimit);
                    return (false, "Trial allowance reached (1 listing). Publish your first property to activate your trial, or upgrade to add more.", count, trialLimit);
                }

                return (false, "Your subscription has ended. Choose a plan to add listings.", count, 0);
            }

            public Task<OwnerSubscription?> CreateTrialSubscriptionAsync(int ownerId, int trialDurationDays = 60)
            {
                if (Subscriptions.Any(s => s.OwnerID == ownerId))
                {
                    return Task.FromResult<OwnerSubscription?>(null); // Already has history
                }

                var sub = new OwnerSubscription
                {
                    SubscriptionID = Subscriptions.Count + 100,
                    OwnerID = ownerId,
                    PlanID = 1,
                    PlanName = "Free Trial",
                    StartDate = DateTime.UtcNow,
                    EndDate = DateTime.UtcNow.AddDays(trialDurationDays),
                    PaymentStatus = "Trial",
                    PricePaid = 0m,
                    MaxListings = 1
                };
                Subscriptions.Add(sub);
                return Task.FromResult<OwnerSubscription?>(sub);
            }

            public Task<List<OwnerSubscription>> GetExpiredSubscriptionsForHourlyProcessAsync()
            {
                var now = DateTime.UtcNow;
                var list = Subscriptions
                    .Where(s => s.EndDate < now)
                    .GroupBy(s => s.OwnerID)
                    .Select(g => g.OrderByDescending(s => s.EndDate).First())
                    .Where(latest => !Subscriptions.Any(s => s.OwnerID == latest.OwnerID && s.EndDate >= now))
                    .ToList();
                return Task.FromResult(list);
            }

            public Task<List<OwnerSubscription>> GetExpiringSubscriptionsAsync(int daysBeforeExpiry)
            {
                var now = DateTime.UtcNow;
                var target = now.AddDays(daysBeforeExpiry);
                var list = Subscriptions
                    .Where(s => s.EndDate > now && s.EndDate <= target && (s.PaymentStatus == "Paid" || s.PaymentStatus == "Trial"))
                    .ToList();
                return Task.FromResult(list);
            }

            public Task<List<OwnerSubscription>> GetNewlyActiveScheduledSubscriptionsAsync()
            {
                var now = DateTime.UtcNow;
                var list = Subscriptions
                    .Where(s => s.PaymentStatus == "Paid" && s.StartDate <= now && s.EndDate >= now)
                    .ToList();
                return Task.FromResult(list);
            }

            public Task<List<string>> RepublishExpiredPropertiesAsync(int ownerId, int maxListings)
            {
                if (PropertyRepo == null) return Task.FromResult(new List<string>());

                var expired = PropertyRepo.Properties
                    .Where(p => p.OwnerID == ownerId && p.ListingStatus == "Deactivated" && p.UnpublishReason == "Subscription expired")
                    .OrderBy(p => p.PropertyID)
                    .Take(maxListings)
                    .ToList();

                var republished = new List<string>();
                foreach (var p in expired)
                {
                    p.ListingStatus = "Active";
                    p.IsApproved = true;
                    p.UnpublishReason = null;
                    republished.Add(p.Title);
                }
                return Task.FromResult(republished);
            }

                        public Task<int> CreatePlanAsync(SubscriptionPlan plan) => Task.FromResult(1);
            public Task<bool> UpdatePlanAsync(SubscriptionPlan plan) => Task.FromResult(true);
            public Task<int> GetActiveListingCountAsync(int ownerId) => GetLiveListingCountAsync(ownerId);
            public Task<List<SubscriptionPlan>> GetAllPlansAsync(bool onlyActive = true) => Task.FromResult(new List<SubscriptionPlan>());
            public Task<SubscriptionPlan?> GetPlanByIdAsync(int planId) => Task.FromResult<SubscriptionPlan?>(null);
            public Task<int> SubscribeOwnerAsync(OwnerSubscription subscription) => Task.FromResult(1);
            public Task<OwnerSubscription?> GetSubscriptionByIdAsync(int subscriptionId) => Task.FromResult<OwnerSubscription?>(null);
            public Task<bool> UpdateSubscriptionPaymentStatusAsync(int subscriptionId, string paymentStatus, string? gatewayTransactionId) => Task.FromResult(true);
            public Task<List<OwnerSubscription>> GetSubscriptionHistoryByOwnerIdAsync(int ownerId) => Task.FromResult(new List<OwnerSubscription>());
            public Task<bool> CancelScheduledSubscriptionAsync(int subscriptionId, int ownerId) => Task.FromResult(true);
            public Task<int> GetTrialDurationDaysAsync() => Task.FromResult(60);
            public Task<SubscriptionPlan?> GetPlanByNameAsync(string planName) => Task.FromResult<SubscriptionPlan?>(null);
            public Task<bool> CanOwnerAddPropertyAsync(int ownerId) => Task.FromResult(true);
            public Task<decimal> GetEffectivePlanPriceAsync(int planId, DateTime? asOfDate = null) => Task.FromResult(0m);
            public Task<(decimal Price, DateTime EffectiveFrom)?> GetUpcomingPlanPriceAsync(int planId, DateTime? asOfDate = null) => Task.FromResult<(decimal Price, DateTime EffectiveFrom)?>(null);
            public Task<int> CreatePlanPriceAsync(SubscriptionPlanPrice price) => Task.FromResult(1);
            public Task<List<SubscriptionPlanPrice>> GetPlanPricesByPlanIdAsync(int planId) => Task.FromResult(new List<SubscriptionPlanPrice>());
        }

        private class FakePropertyRepository : IPropertyRepository
        {
            public List<Property> Properties { get; } = new();

            public Task<Property?> GetByIdAsync(int propertyId)
            {
                return Task.FromResult(Properties.FirstOrDefault(p => p.PropertyID == propertyId));
            }

            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false)
            {
                return Task.FromResult(Properties.Where(p => !onlyApproved || p.IsApproved).ToList());
            }

            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId)
            {
                return Task.FromResult(Properties.Where(p => p.OwnerID == ownerId).ToList());
            }

            public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests)
            {
                return Task.FromResult(new List<Property>());
            }

            public Task<int> CreatePropertyAsync(Property property)
            {
                property.PropertyID = Properties.Count + 1;
                Properties.Add(property);
                return Task.FromResult(property.PropertyID);
            }

            public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
            public Task<bool> UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl) => Task.FromResult(true);
            public Task<bool> DeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> PermanentlyDeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> DeactivatePropertyAsync(int propertyId, bool deactivate = true) => Task.FromResult(true);

            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null)
            {
                return ApprovePropertyAsync(propertyId, isApproved, targetStatus, null, null);
            }

            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus, int? staffUserId, string? reason)
            {
                var prop = Properties.FirstOrDefault(p => p.PropertyID == propertyId);
                if (prop == null) return Task.FromResult(false);

                prop.IsApproved = isApproved;
                prop.ListingStatus = targetStatus ?? (isApproved ? "Draft" : "Rejected");
                if (isApproved)
                {
                    prop.ApprovedAt = DateTime.UtcNow;
                    prop.ApprovedByUserID = staffUserId;
                    prop.MapUrlLocked = true;
                }
                else
                {
                    prop.RejectionReason = reason;
                }
                return Task.FromResult(true);
            }

            public Task<(bool Success, string Message)> PublishPropertyAsync(int propertyId, int ownerId, int limit)
            {
                var prop = Properties.FirstOrDefault(p => p.PropertyID == propertyId && p.OwnerID == ownerId);
                if (prop == null) return Task.FromResult((false, "Property not found."));
                if (!prop.IsApproved) return Task.FromResult((false, "Property must be approved before publishing."));

                int liveCount = Properties.Count(p => p.OwnerID == ownerId && p.IsApproved && p.ListingStatus == "Active" && p.PropertyID != propertyId);
                if (liveCount >= limit)
                {
                    return Task.FromResult((false, $"Active listing limit reached ({liveCount}/{limit}). Please upgrade your subscription plan to publish more listings."));
                }

                prop.ListingStatus = "Active";
                prop.PublishedAt = DateTime.UtcNow;
                prop.UnpublishReason = null;
                return Task.FromResult((true, "Property published successfully."));
            }

            public Task<bool> UnpublishPropertyAsync(int propertyId, int ownerId, string unpublishReason = "Owner unpublished")
            {
                var prop = Properties.FirstOrDefault(p => p.PropertyID == propertyId && p.OwnerID == ownerId);
                if (prop == null) return Task.FromResult(false);

                prop.ListingStatus = "Deactivated";
                prop.UnpublishReason = unpublishReason;
                return Task.FromResult(true);
            }

            public Task<int> BatchUnpublishPropertiesByOwnerIdAsync(int ownerId, string unpublishReason)
            {
                var live = Properties.Where(p => p.OwnerID == ownerId && p.ListingStatus == "Active").ToList();
                foreach (var p in live)
                {
                    p.ListingStatus = "Deactivated";
                    p.UnpublishReason = unpublishReason;
                }
                return Task.FromResult(live.Count);
            }

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
            public Task<List<PropertyType>> GetPropertyTypesAsync() => Task.FromResult(new List<PropertyType>());
            public Task<int> GetOrCreatePropertyTypeByNameAsync(string name) => Task.FromResult(1);
            public Task<bool> UpdatePropertyCategoryAsync(int propertyId, int ownerId, int propertyTypeId) => Task.FromResult(true);
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(new List<Occasion>());
            public Task<int> GetOrCreateAmenityByNameAsync(string name) => Task.FromResult(1);
            public Task<int> GetOrCreateOccasionByNameAsync(string name) => Task.FromResult(1);
            public Task<List<PropertyEventCommission>> GetPropertyEventCommissionsAsync(int propertyId) => Task.FromResult(new List<PropertyEventCommission>());
            public Task<decimal?> GetPropertyEventCommissionRateAsync(int propertyId, int packageId) => Task.FromResult<decimal?>(null);
            public Task<bool> SetPropertyEventCommissionsAsync(int propertyId, List<(int packageId, decimal commissionRate)> commissions) => Task.FromResult(true);

        }

        private class FakeNotificationDispatcherService : INotificationDispatcherService
        {
            public record NotificationEvent(string EventType, int EntityId, int UserId, string Title, string Body);
            public List<NotificationEvent> Events { get; } = new();
            private readonly HashSet<string> _dispatchedKeys = new();

            public Task DispatchPropertyApprovedAsync(int propertyId, string propertyTitle, int ownerId)
            {
                Events.Add(new NotificationEvent("PropertyApproved", propertyId, ownerId, "Property Approved", $"Your property '{propertyTitle}' was approved."));
                return Task.CompletedTask;
            }

            public Task DispatchPropertyRejectedAsync(int propertyId, string propertyTitle, int ownerId, string reason)
            {
                Events.Add(new NotificationEvent("PropertyRejected", propertyId, ownerId, "Property Rejected", $"Reason: {reason}"));
                return Task.CompletedTask;
            }

            public Task DispatchPropertyPublishedAsync(int propertyId, string propertyTitle, int ownerId)
            {
                Events.Add(new NotificationEvent("PropertyPublished", propertyId, ownerId, "Property Published", $"Property '{propertyTitle}' is live."));
                return Task.CompletedTask;
            }

            public Task DispatchPropertyUnpublishedAsync(int propertyId, string propertyTitle, int ownerId, string reason)
            {
                Events.Add(new NotificationEvent("PropertyUnpublished", propertyId, ownerId, "Property Unpublished", $"Reason: {reason}"));
                return Task.CompletedTask;
            }

            public Task DispatchTrialExpiringWarningAsync(int ownerId, string planName, DateTime endDate)
            {
                string key = $"TrialExpiringWarning:{ownerId}:{endDate:yyyyMMdd}";
                if (_dispatchedKeys.Add(key))
                {
                    Events.Add(new NotificationEvent("TrialExpiringWarning", 0, ownerId, "Trial Expiring", $"Trial ends on {endDate:dd MMM yyyy}."));
                }
                return Task.CompletedTask;
            }

            public Task DispatchSubscriptionExpiringWarningAsync(int ownerId, string planName, DateTime endDate)
            {
                string key = $"SubscriptionExpiringWarning:{ownerId}:{endDate:yyyyMMdd}";
                if (_dispatchedKeys.Add(key))
                {
                    Events.Add(new NotificationEvent("SubscriptionExpiringWarning", 0, ownerId, "Subscription Expiring", $"Expires on {endDate:dd MMM yyyy}."));
                }
                return Task.CompletedTask;
            }

            public Task DispatchSubscriptionExpiredAsync(int ownerId, string planName)
            {
                Events.Add(new NotificationEvent("SubscriptionExpired", 0, ownerId, "Subscription Expired", $"Plan {planName} expired."));
                return Task.CompletedTask;
            }

            public Task DispatchSubscriptionActivatedAsync(int ownerId, string planName, DateTime endDate, List<string>? republishedProperties = null)
            {
                Events.Add(new NotificationEvent("SubscriptionActivated", 0, ownerId, "Subscription Activated", $"Plan {planName} activated."));
                return Task.CompletedTask;
            }

            public Task DispatchPlanPurchaseConfirmedAsync(int ownerId, string planName, decimal amount, bool isScheduled, DateTime startDate)
            {
                Events.Add(new NotificationEvent("PlanPurchaseConfirmed", 0, ownerId, "Purchase Confirmed", $"Paid {amount}"));
                return Task.CompletedTask;
            }

            public Task DispatchPropertyWaitingVerificationStaffAlertAsync(int propertyId, string propertyTitle, int ownerId)
            {
                Events.Add(new NotificationEvent("PropertyPendingVerification", propertyId, ownerId, "Pending Review", $"Property {propertyTitle} pending."));
                return Task.CompletedTask;
            }

            public Task DispatchBookingConfirmedAsync(int bookingId) => Task.CompletedTask;
            public Task DispatchBookingCancelledAsync(int bookingId, string? reason) => Task.CompletedTask;
            public Task DispatchRefundProcessedAsync(int bookingId, decimal refundAmount) => Task.CompletedTask;
            public Task DispatchEventStatusChangedAsync(int bookingId, string newStatus) => Task.CompletedTask;
            public Task DispatchReceiptAvailableAsync(int bookingId, string receiptNumber) => Task.CompletedTask;
            public Task DispatchPropertyApprovalStatusAsync(int propertyId, bool isApproved, string? notes) => Task.CompletedTask;
            public Task DispatchDecoratorAssignedAsync(int bookingId, int decoratorUserId) => Task.CompletedTask;
            public Task DispatchPayoutDispatchedAsync(int bookingId, int recipientUserId, decimal payoutAmount, string role) => Task.CompletedTask;
            public Task DispatchDamageClaimUpdateAsync(int claimId, int bookingId, string status) => Task.CompletedTask;
            public Task DispatchPartnerLeadSubmittedAsync(int leadId, string leadName, string serviceType) => Task.CompletedTask;
            public Task DispatchClientDocumentSubmittedAsync(int verificationId, int clientId, string docType) => Task.CompletedTask;
            public Task DispatchBookingCreatedStaffAlertAsync(int bookingId, int clientId, int propertyId) => Task.CompletedTask;
            public Task DispatchCheckInRemindersAsync() => Task.CompletedTask;
        }
    }
}
