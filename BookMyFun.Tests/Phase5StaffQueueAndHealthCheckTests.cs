using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Health;
using BookMyFun.Services.Staff;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace BookMyFun.Tests
{
    public class Phase5StaffQueueAndHealthCheckTests
    {
        #region Test Fakes

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

        private class FakeEventPackageRepository : IEventPackageRepository
        {
            public List<EventPackage> Packages = new();
            public List<EventManagerPortfolio> Portfolios = new();
            public List<User> Decorators = new();

            public Task<List<User>> GetDecoratorsAsync(bool onlyVerified = true) => Task.FromResult(Decorators);
            public Task<EventPackage?> GetPackageByIdAsync(int packageId) => Task.FromResult(Packages.FirstOrDefault(p => p.PackageID == packageId));
            public Task<List<EventPackage>> GetPackagesByUserIdAsync(int userId) => Task.FromResult(Packages.Where(p => p.CreatedByUserID == userId).ToList());
            public Task<List<EventManagerPortfolio>> GetPortfoliosByUserIdAsync(int userId, bool onlyApproved = true)
            {
                var list = Portfolios.Where(p => p.UserID == userId);
                if (onlyApproved) list = list.Where(p => p.IsApproved);
                return Task.FromResult(list.ToList());
            }
            public Task<HashSet<int>> GetUserIdsWithApprovedPortfoliosAsync() =>
                Task.FromResult(Portfolios.Where(p => p.IsApproved).Select(p => p.UserID).ToHashSet());

            public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(true);
            public Task<List<EventPackage>> GetAllPackagesAsync() => Task.FromResult(Packages);
            public Task<List<EventPackage>> GetActivePackagesAsync() => Task.FromResult(Packages.Where(p => p.IsActive).ToList());
            public Task<int> CreatePackageAsync(EventPackage package) => Task.FromResult(1);
            public Task<bool> UpdatePackageAsync(EventPackage package) => Task.FromResult(true);
            public Task<bool> UpdatePackageCommissionRateAsync(int packageId, decimal? commissionRate) => Task.FromResult(true);
            public Task<bool> DeletePackageAsync(int packageId) => Task.FromResult(true);
            public Task<List<Booking>> GetAssignedEventBookingsAsync(int decoratorUserId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllEventBookingsAsync() => Task.FromResult(new List<Booking>());
            public Task<bool> UpdateSetupStatusAsync(int bookingId, string setupStatus, string? setupProofMediaUrls, int? decoratorId) => Task.FromResult(true);
            public Task<bool> AssignDecoratorAsync(int bookingId, int decoratorUserId) => Task.FromResult(true);
            public Task<bool> CancelOrRemoveEventFromBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<List<string>> GetAllDistinctPropertyLocationsAsync() => Task.FromResult(new List<string>());
            public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>(null);
            public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas) => Task.FromResult(true);
            public Task<List<EventManagerPortfolio>> GetAllPortfoliosAsync() => Task.FromResult(Portfolios);
        }

        private class FakePropertyRepository : IPropertyRepository
        {
            public List<Property> Properties = new();
            public Dictionary<int, List<PropertyMedia>> Media = new();

            public Task<Property?> GetByIdAsync(int propertyId) => Task.FromResult(Properties.FirstOrDefault(p => p.PropertyID == propertyId));
            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(Properties);
            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(Properties.Where(p => p.OwnerID == ownerId).ToList());
            public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests) =>
                Task.FromResult(new List<Property>());
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
            public Task<List<PropertyMedia>> GetPropertyMediaAsync(int propertyId)
            {
                if (Media.TryGetValue(propertyId, out var list)) return Task.FromResult(list);
                return Task.FromResult(new List<PropertyMedia>());
            }
            public Task<bool> DeletePropertyMediaAsync(int mediaId) => Task.FromResult(true);
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(new List<Occasion>());
            public Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId) => Task.FromResult(new List<int>());
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

        private class FakeIdVerificationService : IIdVerificationService
        {
            public List<ClientIdVerification> Verifications = new();

            public Task<int> SubmitVerificationAsync(int clientId, string documentType, string storageKey, string? maskedIdentifier) => Task.FromResult(1);
            public Task<ClientIdVerification?> GetClientVerificationAsync(int clientId, int requestingUserId, string userRole) =>
                Task.FromResult(Verifications.FirstOrDefault(v => v.ClientID == clientId));
            public Task<List<ClientIdVerification>> GetAllVerificationsAsync(string? status = null)
            {
                if (string.IsNullOrEmpty(status)) return Task.FromResult(Verifications);
                return Task.FromResult(Verifications.Where(v => string.Equals(v.VerificationStatus, status, StringComparison.OrdinalIgnoreCase)).ToList());
            }
            public Task<bool> ReviewVerificationAsync(int verificationId, string action, int staffUserId, string? rejectionReason = null) => Task.FromResult(true);
            public Task<bool> IsClientVerifiedAsync(int clientId) => Task.FromResult(Verifications.Any(v => v.ClientID == clientId && v.VerificationStatus == "Verified"));
        }

        private class FakePartnerLeadService : IPartnerLeadService
        {
            public List<PartnerLead> Leads = new();

            public Task<int> CreateLeadAsync(PartnerLeadCreateViewModel model) => Task.FromResult(1);
            public Task<List<PartnerLead>> GetAllLeadsAsync(string? status = null)
            {
                if (string.IsNullOrEmpty(status)) return Task.FromResult(Leads);
                return Task.FromResult(Leads.Where(l => string.Equals(l.Status, status, StringComparison.OrdinalIgnoreCase)).ToList());
            }
            public Task<PartnerLead> GetLeadByIdAsync(int leadId) => Task.FromResult(Leads.First(l => l.LeadID == leadId));
            public Task<(bool success, string generatedPassword, string activationToken, int accountId, string roleDisplayName, string email, string? phone)> ApproveLeadAsync(int leadId, int staffUserId) =>
                Task.FromResult((true, "pass", "token", 1, "Owner", "test@test.com", (string?)"1234567890"));
            public Task<(bool success, int userId, string generatedPassword, string email, string? phone, string fullName)> ApproveOwnerRequestAsync(int requestId, int staffUserId, string? notes) =>
                Task.FromResult((true, 1, "pass", "test@test.com", (string?)"1234567890", "Full Name"));
            public Task<bool> RejectLeadAsync(int leadId, int staffUserId) => Task.FromResult(true);
            public Task<bool> ActivateOwnerAccountAsync(ActivateOwnerViewModel model) => Task.FromResult(true);
        }

        private class FakeRefundEngineService : IRefundEngineService
        {
            public List<BookingRefund> Refunds = new();

            public CancellationQuoteViewModel CalculateCancellationQuote(Booking booking, DateTime? checkTime = null, bool isOwner = false) => new();
            public Task<CancellationQuoteViewModel> CalculateCancellationQuoteAsync(Booking booking, DateTime? checkTime = null, bool isOwner = false) => Task.FromResult(new CancellationQuoteViewModel());
            public Task<BookingRefund> ProcessCancellationAndRefundAsync(int bookingId, int requestingUserId, string userRole, string? reason, bool forceOverride = false, int? specificRule = null, decimal? customRefundAmount = null, bool issueCredit = false) =>
                Task.FromResult(new BookingRefund());
            public Task<List<BookingRefund>> GetRefundsQueueAsync(string? status = null)
            {
                if (string.IsNullOrEmpty(status)) return Task.FromResult(Refunds);
                return Task.FromResult(Refunds.Where(r => string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase)).ToList());
            }
            public Task<bool> OverrideRefundStatusAsync(int refundId, string newStatus, int staffUserId, string? notes) => Task.FromResult(true);
            public Task<BookingRefund> AdjudicateAdminRefundAsync(int refundId, int adminUserId, string action, decimal refundAmount, string reason, bool issueAsCredit = false) =>
                Task.FromResult(new BookingRefund());
            public Task<BookingRefund?> GetRefundByIdAsync(int refundId) => Task.FromResult(Refunds.FirstOrDefault(r => r.RefundID == refundId));
        }

        private class FakeDamageClaimService : IDamageClaimService
        {
            public List<DamageClaim> Claims = new();

            public Task<int> CreateClaimAsync(DamageClaimCreateViewModel model, int hostUserId, bool isStaff = false) => Task.FromResult(1);
            public Task<List<DamageClaim>> GetAllClaimsAsync(string? status = null)
            {
                if (string.IsNullOrEmpty(status)) return Task.FromResult(Claims);
                return Task.FromResult(Claims.Where(c => string.Equals(c.Status, status, StringComparison.OrdinalIgnoreCase)).ToList());
            }
            public Task<List<DamageClaim>> GetClaimsByOwnerAsync(int ownerId) => Task.FromResult(Claims.Where(c => c.OwnerID == ownerId).ToList());
            public Task<DamageClaim> GetClaimByIdAsync(int claimId) => Task.FromResult(Claims.First(c => c.ClaimID == claimId));
            public Task<bool> AdjudicateClaimAsync(DamageClaimAdjudicateViewModel model, int staffUserId) => Task.FromResult(true);
        }

        private class FakeSubscriptionRepository : ISubscriptionRepository
        {
            public Dictionary<int, OwnerSubscription> ActiveSubscriptions = new();

            public Task<OwnerSubscription?> GetActiveSubscriptionByOwnerIdAsync(int ownerId)
            {
                ActiveSubscriptions.TryGetValue(ownerId, out var sub);
                return Task.FromResult(sub);
            }
            public Task<List<SubscriptionPlan>> GetAllPlansAsync(bool onlyActive = true) => Task.FromResult(new List<SubscriptionPlan>());
            public Task<SubscriptionPlan?> GetPlanByIdAsync(int planId) => Task.FromResult<SubscriptionPlan?>(null);
            public Task<int> CreatePlanAsync(SubscriptionPlan plan) => Task.FromResult(1);
            public Task<bool> UpdatePlanAsync(SubscriptionPlan plan) => Task.FromResult(true);
            public Task<int> SubscribeOwnerAsync(OwnerSubscription sub) => Task.FromResult(1);
            public Task<List<OwnerSubscription>> GetSubscriptionHistoryByOwnerIdAsync(int ownerId) => Task.FromResult(new List<OwnerSubscription>());
            public Task<OwnerSubscription?> GetSubscriptionByIdAsync(int subscriptionId) => Task.FromResult<OwnerSubscription?>(null);
            public Task<bool> UpdateSubscriptionPaymentStatusAsync(int subscriptionId, string paymentStatus, string? gatewayTransactionId) => Task.FromResult(true);
            public Task<int> GetActiveListingCountAsync(int ownerId) => Task.FromResult(1);
            public Task<bool> CanOwnerAddPropertyAsync(int ownerId) => Task.FromResult(true);
        }

        private class FakeNotificationOutboxRepository : INotificationOutboxRepository
        {
            public List<NotificationOutbox> OutboxItems = new();

            public Task<bool> EnqueueAsync(NotificationOutbox item, SqlTransaction? tx = null)
            {
                item.OutboxID = OutboxItems.Count + 1;
                OutboxItems.Add(item);
                return Task.FromResult(true);
            }
            public Task<List<NotificationOutbox>> GetPendingBatchAsync(int batchSize = 25) =>
                Task.FromResult(OutboxItems.Take(batchSize).ToList());
            public Task<bool> MarkProcessingAsync(long outboxId) => Task.FromResult(true);
            public Task<bool> MarkSentAsync(long outboxId) => Task.FromResult(true);
            public Task<bool> RecordFailureAsync(long outboxId, string error, int retryCount, DateTime nextAttemptAt, bool isPermanent) => Task.FromResult(true);
        }

        private class FakeWebHostEnvironment : IWebHostEnvironment
        {
            public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
            public IFileProvider WebRootFileProvider { get; set; } = null!;
            public string ApplicationName { get; set; } = "BookMyFun";
            public IFileProvider ContentRootFileProvider { get; set; } = null!;
            public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
            public string EnvironmentName { get; set; } = "Testing";
        }

        #endregion

        [Fact]
        public async Task StaffQueueService_Aggregates_All_7_Pending_Task_Types()
        {
            // Arrange
            var userRepo = new FakeUserRepository();
            var eventRepo = new FakeEventPackageRepository();
            var propRepo = new FakePropertyRepository();
            var idService = new FakeIdVerificationService();
            var leadService = new FakePartnerLeadService();
            var refundEngine = new FakeRefundEngineService();
            var damageService = new FakeDamageClaimService();

            var baseTime = DateTime.UtcNow;

            // 1. Pending Event Manager
            userRepo.Users.Add(new User
            {
                UserID = 1,
                FullName = "Manager Pending",
                Role = "EventManager",
                VerificationStatus = "Pending",
                CreatedAt = baseTime.AddHours(-10)
            });

            // 2. Pending Portfolio Item
            eventRepo.Portfolios.Add(new EventManagerPortfolio
            {
                PortfolioID = 101,
                UserID = 1,
                Title = "Floral Gate Setup",
                IsApproved = false,
                CreatedAt = baseTime.AddHours(-9)
            });

            // 3. Pending Property Approval
            propRepo.Properties.Add(new Property
            {
                PropertyID = 201,
                Title = "Whispering Pines Villa",
                OwnerID = 2,
                IsApproved = false,
                ListingStatus = "PendingPayment",
                CreatedAt = baseTime.AddHours(-8)
            });

            // 4. Pending Guest KYC
            idService.Verifications.Add(new ClientIdVerification
            {
                VerificationID = 301,
                ClientID = 5,
                DocumentType = "Aadhaar",
                VerificationStatus = "PendingReview",
                CreatedAt = baseTime.AddHours(-7)
            });

            // 5. New Partner Lead
            leadService.Leads.Add(new PartnerLead
            {
                LeadID = 401,
                FullName = "Rajesh Patel",
                PartnerType = "Owner",
                Status = "New",
                Location = "Surat, Gujarat",
                CreatedAt = baseTime.AddHours(-6)
            });

            // 6. Processing Refund
            refundEngine.Refunds.Add(new BookingRefund
            {
                RefundID = 501,
                BookingID = 10,
                Status = "Processing",
                NetRefundAmount = 4500m,
                CreatedAt = baseTime.AddHours(-5)
            });

            // 7. Open Damage Claim
            damageService.Claims.Add(new DamageClaim
            {
                ClaimID = 601,
                BookingID = 10,
                Status = "UnderReview",
                ClaimAmount = 2500m,
                ItemizedDescription = "Broken dining chair",
                CreatedAt = baseTime.AddHours(-4)
            });

            var queueService = new StaffQueueService(userRepo, eventRepo, propRepo, idService, leadService, refundEngine, damageService);

            // Act
            var result = await queueService.GetQueueAsync(null, 1, 25);

            // Assert
            Assert.Equal(7, result.TotalPendingCount);
            Assert.Equal(7, result.TotalItems);
            Assert.Equal(7, result.Items.Count);
            Assert.Equal(1, result.CountsByType["EventManager"]);
            Assert.Equal(1, result.CountsByType["Portfolio"]);
            Assert.Equal(1, result.CountsByType["Property"]);
            Assert.Equal(1, result.CountsByType["ClientId"]);
            Assert.Equal(1, result.CountsByType["PartnerLead"]);
            Assert.Equal(1, result.CountsByType["Refund"]);
            Assert.Equal(1, result.CountsByType["DamageClaim"]);
        }

        [Fact]
        public async Task StaffQueueService_Sorts_Oldest_First()
        {
            // Arrange
            var userRepo = new FakeUserRepository();
            var eventRepo = new FakeEventPackageRepository();
            var propRepo = new FakePropertyRepository();
            var idService = new FakeIdVerificationService();
            var leadService = new FakePartnerLeadService();
            var refundEngine = new FakeRefundEngineService();
            var damageService = new FakeDamageClaimService();

            var now = DateTime.UtcNow;

            // Item A: 3 days ago (oldest)
            userRepo.Users.Add(new User
            {
                UserID = 1,
                FullName = "Oldest Manager",
                Role = "EventManager",
                VerificationStatus = "Pending",
                CreatedAt = now.AddDays(-3)
            });

            // Item B: 1 day ago
            propRepo.Properties.Add(new Property
            {
                PropertyID = 201,
                Title = "Middle Property",
                IsApproved = false,
                ListingStatus = "Draft",
                CreatedAt = now.AddDays(-1)
            });

            // Item C: 2 hours ago (newest)
            leadService.Leads.Add(new PartnerLead
            {
                LeadID = 401,
                FullName = "Newest Lead",
                Status = "New",
                CreatedAt = now.AddHours(-2)
            });

            var queueService = new StaffQueueService(userRepo, eventRepo, propRepo, idService, leadService, refundEngine, damageService);

            // Act
            var result = await queueService.GetQueueAsync(null, 1, 25);

            // Assert: Oldest first
            Assert.Equal(3, result.Items.Count);
            Assert.Equal("EventManager", result.Items[0].TaskType);
            Assert.Equal("Property", result.Items[1].TaskType);
            Assert.Equal("PartnerLead", result.Items[2].TaskType);
            Assert.True(result.Items[0].SubmittedAt < result.Items[1].SubmittedAt);
            Assert.True(result.Items[1].SubmittedAt < result.Items[2].SubmittedAt);
        }

        [Fact]
        public async Task StaffQueueService_Filters_By_TaskType_And_Paginates_At_25()
        {
            // Arrange
            var userRepo = new FakeUserRepository();
            var eventRepo = new FakeEventPackageRepository();
            var propRepo = new FakePropertyRepository();
            var idService = new FakeIdVerificationService();
            var leadService = new FakePartnerLeadService();
            var refundEngine = new FakeRefundEngineService();
            var damageService = new FakeDamageClaimService();

            var now = DateTime.UtcNow;

            // Add 30 pending properties
            for (int i = 1; i <= 30; i++)
            {
                propRepo.Properties.Add(new Property
                {
                    PropertyID = i,
                    Title = $"Farmhouse #{i}",
                    IsApproved = false,
                    ListingStatus = "PendingApproval",
                    CreatedAt = now.AddHours(-i)
                });
            }

            // Add 5 pending refunds
            for (int i = 1; i <= 5; i++)
            {
                refundEngine.Refunds.Add(new BookingRefund
                {
                    RefundID = i,
                    BookingID = i,
                    Status = "Initiated",
                    CreatedAt = now.AddHours(-i)
                });
            }

            var queueService = new StaffQueueService(userRepo, eventRepo, propRepo, idService, leadService, refundEngine, damageService);

            // Act 1: Filter by Property page 1 (pageSize 25)
            var propPage1 = await queueService.GetQueueAsync("Property", 1, 25);

            // Assert 1
            Assert.Equal(35, propPage1.TotalPendingCount); // platform total
            Assert.Equal(30, propPage1.TotalItems); // filtered count
            Assert.Equal(2, propPage1.TotalPages);
            Assert.Equal(25, propPage1.Items.Count);
            Assert.All(propPage1.Items, item => Assert.Equal("Property", item.TaskType));

            // Act 2: Page 2
            var propPage2 = await queueService.GetQueueAsync("Property", 2, 25);

            // Assert 2
            Assert.Equal(5, propPage2.Items.Count);

            // Act 3: Filter by Refund
            var refundPage = await queueService.GetQueueAsync("Refund", 1, 25);
            Assert.Equal(5, refundPage.TotalItems);
            Assert.Equal(1, refundPage.TotalPages);
            Assert.All(refundPage.Items, item => Assert.Equal("Refund", item.TaskType));
        }

        [Fact]
        public async Task DataHealthCheckService_Detects_LocalProperty6_MissingPortfolio_And_CloudProperty4_Surat_Coverage()
        {
            // Arrange
            var propRepo = new FakePropertyRepository();
            var userRepo = new FakeUserRepository();
            var eventRepo = new FakeEventPackageRepository();
            var subRepo = new FakeSubscriptionRepository();
            var outboxRepo = new FakeNotificationOutboxRepository();
            var env = new FakeWebHostEnvironment();

            // 1. Manager Vansh: Verified, IsActive, active package, service area "Lonavala, Maharashtra", BUT ZERO approved portfolio items
            var vansh = new User
            {
                UserID = 10,
                FullName = "Vansh Decor Studio",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Lonavala, Maharashtra"
            };
            userRepo.Users.Add(vansh);

            eventRepo.Packages.Add(new EventPackage
            {
                PackageID = 1,
                CreatedByUserID = vansh.UserID,
                IsActive = true,
                Name = "Rustic Fairy Lights"
            });

            // Vansh has a portfolio item, but it is NOT approved (IsApproved = false)
            eventRepo.Portfolios.Add(new EventManagerPortfolio
            {
                PortfolioID = 1,
                UserID = vansh.UserID,
                Title = "Unapproved Setup",
                IsApproved = false
            });

            // Local Property 6: Live in Lonavala
            var localProp6 = new Property
            {
                PropertyID = 6,
                Title = "Local Farm 6",
                Location = "Lonavala, Maharashtra",
                City = "Lonavala",
                State = "Maharashtra",
                IsApproved = true,
                ListingStatus = "Active",
                OwnerID = 100
            };
            propRepo.Properties.Add(localProp6);

            // Cloud Property 4: Live in Surat, Gujarat (no manager serves Surat)
            var cloudProp4 = new Property
            {
                PropertyID = 4,
                Title = "Cloud Farm 4 Surat",
                Location = "Surat, Gujarat",
                City = "Surat",
                State = "Gujarat",
                IsApproved = true,
                ListingStatus = "Active",
                OwnerID = 101
            };
            propRepo.Properties.Add(cloudProp4);

            // Owners have active subscriptions
            subRepo.ActiveSubscriptions[100] = new OwnerSubscription { OwnerID = 100, EndDate = DateTime.UtcNow.AddMonths(1) };
            subRepo.ActiveSubscriptions[101] = new OwnerSubscription { OwnerID = 101, EndDate = DateTime.UtcNow.AddMonths(1) };

            var healthService = new DataHealthCheckService(propRepo, userRepo, eventRepo, subRepo, outboxRepo, env);

            // Act
            var report = await healthService.RunDiagnosticsAsync();

            // Assert
            // 1. Manager Vansh reported as verified with no approved portfolio
            var incompleteMgr = report.Findings.FirstOrDefault(f => f.Category == "IncompleteEventManager" && f.EntityID == vansh.UserID);
            Assert.NotNull(incompleteMgr);
            Assert.Contains("zero approved showcase portfolio items", incompleteMgr.Issue);

            // 2. Local Property 6 reported as having no eligible Stay & Celebrate coverage because manager Vansh is missing portfolio
            var prop6Coverage = report.Findings.FirstOrDefault(f => f.Category == "NoDecorCoverage" && f.EntityID == localProp6.PropertyID);
            Assert.NotNull(prop6Coverage);
            Assert.Contains("missing an approved portfolio", prop6Coverage.Issue);

            // 3. Cloud Property 4 reported as having no verified manager covering Surat
            var prop4Coverage = report.Findings.FirstOrDefault(f => f.Category == "NoDecorCoverage" && f.EntityID == cloudProp4.PropertyID);
            Assert.NotNull(prop4Coverage);
            Assert.Contains("no verified event manager covering this service area", prop4Coverage.Issue);
        }

        [Fact]
        public async Task DataHealthCheckService_Detects_MissingMedia_UnnormalizedAreas_ExpiredSubs_And_FailedOutbox()
        {
            // Arrange
            var propRepo = new FakePropertyRepository();
            var userRepo = new FakeUserRepository();
            var eventRepo = new FakeEventPackageRepository();
            var subRepo = new FakeSubscriptionRepository();
            var outboxRepo = new FakeNotificationOutboxRepository();
            var env = new FakeWebHostEnvironment();

            // 1. Missing / Placeholder Media
            var propWithPlaceholder = new Property
            {
                PropertyID = 50,
                Title = "Placeholder Villa",
                IsApproved = true,
                ListingStatus = "Active",
                OwnerID = 200,
                Location = "Pune, Maharashtra"
            };
            propRepo.Properties.Add(propWithPlaceholder);
            propRepo.Media[50] = new List<PropertyMedia>
            {
                new PropertyMedia { MediaID = 1, PropertyID = 50, StorageUrl = "/uploads/test.jpg" }
            };

            // 2. Un-normalised Service Areas
            var mgrWithBadArea = new User
            {
                UserID = 300,
                FullName = "Un-normalised Decorator",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Mumbai" // Not "City, State"
            };
            userRepo.Users.Add(mgrWithBadArea);

            // 3. Expired Owner Subscription
            subRepo.ActiveSubscriptions[200] = new OwnerSubscription
            {
                OwnerID = 200,
                EndDate = DateTime.UtcNow.AddDays(-10) // Expired 10 days ago
            };

            // 4. Failed Notification Outbox Item (RetryCount >= 3)
            outboxRepo.OutboxItems.Add(new NotificationOutbox
            {
                OutboxID = 888,
                EventType = "BookingConfirmedStay",
                Channel = "Email",
                UserID = 77,
                RetryCount = 3,
                Status = "Failed",
                LastError = "SMTP gateway timeout after 3 retries"
            });

            var healthService = new DataHealthCheckService(propRepo, userRepo, eventRepo, subRepo, outboxRepo, env);

            // Act
            var report = await healthService.RunDiagnosticsAsync();

            // Assert
            Assert.Contains(report.Findings, f => f.Category == "MissingMedia" && f.EntityID == 50);
            Assert.Contains(report.Findings, f => f.Category == "UnnormalizedServiceArea" && f.EntityID == 300);
            Assert.Contains(report.Findings, f => f.Category == "ExpiredSubscription" && f.EntityID == 50);
            Assert.Contains(report.Findings, f => f.Category == "FailedOutbox" && f.EntityID == 888);
        }
    }
}
