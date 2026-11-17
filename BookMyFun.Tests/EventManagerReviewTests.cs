using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Staff;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace BookMyFun.Tests
{
    public class EventManagerReviewTests
    {
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
            public Task<HashSet<int>> GetUserIdsWithApprovedPortfoliosAsync()
            {
                return Task.FromResult(Portfolios.Where(p => p.IsApproved).Select(p => p.UserID).ToHashSet());
            }
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
            public Task<int> AddPortfolioItemAsync(EventManagerPortfolio item) => Task.FromResult(1);
            public Task<List<EventManagerPortfolio>> GetAllPortfoliosAsync() => Task.FromResult(Portfolios);
            public Task<bool> ApprovePortfolioItemAsync(int portfolioId, bool isApproved)
            {
                var item = Portfolios.FirstOrDefault(p => p.PortfolioID == portfolioId);
                if (item != null) item.IsApproved = isApproved;
                return Task.FromResult(true);
            }
            public Task<bool> DeletePortfolioItemAsync(int portfolioId, int userId) => Task.FromResult(true);
            public Task<List<FlaggedContactAttempt>> GetFlaggedContactAttemptsAsync() => Task.FromResult(new List<FlaggedContactAttempt>());
            public Task<bool> UpdateFlaggedContactAttemptStatusAsync(int flagId, string status) => Task.FromResult(true);
        }

        private class FakeConfigRepository : IConfigRepository
        {
            public List<(int PerformedBy, string Action, string EntityType, int? EntityId, string? Details)> AuditLogs = new();

            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
            public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details)
            {
                AuditLogs.Add((performedByUserId, action, entityType, entityId, details));
                return Task.CompletedTask;
            }
        }

        private class FakeIdVerificationService : IIdVerificationService
        {
            public Dictionary<int, ClientIdVerification> Verifications { get; } = new();

            public Task<int> SubmitVerificationAsync(int clientId, string documentType, string storageKey, string? maskedIdentifier) => Task.FromResult(1);
            public Task<ClientIdVerification?> GetClientVerificationAsync(int clientId, int requestingUserId, string userRole)
            {
                Verifications.TryGetValue(clientId, out var v);
                return Task.FromResult(v);
            }
            public Task<List<ClientIdVerification>> GetAllVerificationsAsync(string? status = null) => Task.FromResult(Verifications.Values.ToList());
            public Task<bool> ReviewVerificationAsync(int verificationId, string action, int staffUserId, string? rejectionReason = null) => Task.FromResult(true);
            public Task<bool> IsClientVerifiedAsync(int clientId) => Task.FromResult(true);
        }

        private class FakeMediaStorageService : IMediaStorageService
        {
            public Dictionary<string, string> Files { get; } = new();

            public Task<(string publicUrl, string publicId)> SaveMediaFileAsync(IFormFile file, string mediaType) => Task.FromResult(("url", "id"));
            public Task<string> SavePrivateDocumentAsync(IFormFile file, int clientId) => Task.FromResult("key");
            public Task<bool> ValidateExternalUrlAsync(string url) => Task.FromResult(true);
            public string GenerateExpiringDocumentToken(string documentKey, int requestingUserId, string userRole, int expirationMinutes = 15) => "token";
            public bool ValidateExpiringDocumentToken(string documentKey, string token, int requestingUserId, string userRole) => true;

            public string GetPrivateDocumentPhysicalPath(string storageKey)
            {
                return Files.TryGetValue(storageKey, out var path) ? path : Path.Combine(Path.GetTempPath(), storageKey);
            }
        }

        private class SimpleTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class TestContext
        {
            public FakeUserRepository UserRepo { get; } = new();
            public FakeEventPackageRepository EventRepo { get; } = new();
            public FakeConfigRepository ConfigRepo { get; } = new();
            public FakeIdVerificationService IdService { get; } = new();
            public FakeMediaStorageService MediaStorage { get; } = new();
            public EventManagerReviewService ReviewService { get; }

            public TestContext()
            {
                ReviewService = new EventManagerReviewService(
                    UserRepo, EventRepo, ConfigRepo, IdService, MediaStorage);
            }
        }

        [Fact]
        public async Task GetManagerListAsync_SeparatesTabsAndCalculatesCountsCorrectly()
        {
            var ctx = new TestContext();
            ctx.UserRepo.Users.Add(new User { UserID = 1, FullName = "Pending Mgr", Role = "EventManager", VerificationStatus = "Pending", IsActive = true, CreatedAt = DateTime.UtcNow });
            ctx.UserRepo.Users.Add(new User { UserID = 2, FullName = "Verified Mgr", Role = "EventManager", VerificationStatus = "Verified", IsActive = true, CreatedAt = DateTime.UtcNow });
            ctx.UserRepo.Users.Add(new User { UserID = 3, FullName = "Rejected Mgr", Role = "EventManager", VerificationStatus = "Rejected", IsActive = true, CreatedAt = DateTime.UtcNow });
            ctx.UserRepo.Users.Add(new User { UserID = 4, FullName = "Suspended Mgr", Role = "EventManager", VerificationStatus = "Suspended", IsActive = false, CreatedAt = DateTime.UtcNow });

            var pendingResult = await ctx.ReviewService.GetManagerListAsync("Pending");
            Assert.Equal(1, pendingResult.PendingCount);
            Assert.Equal(1, pendingResult.VerifiedCount);
            Assert.Equal(1, pendingResult.RejectedCount);
            Assert.Equal(1, pendingResult.SuspendedCount);
            Assert.Single(pendingResult.Managers);
            Assert.Equal(1, pendingResult.Managers[0].UserID);

            var verifiedResult = await ctx.ReviewService.GetManagerListAsync("Verified");
            Assert.Single(verifiedResult.Managers);
            Assert.Equal(2, verifiedResult.Managers[0].UserID);

            var rejectedResult = await ctx.ReviewService.GetManagerListAsync("Rejected");
            Assert.Single(rejectedResult.Managers);
            Assert.Equal(3, rejectedResult.Managers[0].UserID);

            var suspendedResult = await ctx.ReviewService.GetManagerListAsync("Suspended");
            Assert.Single(suspendedResult.Managers);
            Assert.Equal(4, suspendedResult.Managers[0].UserID);
        }

        [Fact]
        public async Task GetManagerDetailAsync_EvaluatesChecklistWarningsWithoutBlocking()
        {
            var ctx = new TestContext();
            ctx.UserRepo.Users.Add(new User
            {
                UserID = 10,
                FullName = "Incomplete Mgr",
                Role = "EventManager",
                VerificationStatus = "Pending",
                IsActive = true,
                ServiceAreas = null // Missing service area
            });

            var detail = await ctx.ReviewService.GetManagerDetailAsync(10);
            Assert.NotNull(detail);
            Assert.False(detail.HasDocumentsUploaded);
            Assert.False(detail.HasActivePackage);
            Assert.False(detail.HasApprovedPortfolio);
            Assert.False(detail.HasServiceAreas);
            Assert.Equal(4, detail.Warnings.Count);

            // Adding an approved portfolio and service area updates the flags
            ctx.EventRepo.Portfolios.Add(new EventManagerPortfolio { PortfolioID = 101, UserID = 10, Title = "Stage", IsApproved = true });
            ctx.EventRepo.Packages.Add(new EventPackage { PackageID = 201, CreatedByUserID = 10, Name = "Basic Decor", Category = "Birthday", BasePrice = 5000, IsActive = true });
            ctx.UserRepo.Users[0].ServiceAreas = "Surat, Gujarat";

            var updatedDetail = await ctx.ReviewService.GetManagerDetailAsync(10);
            Assert.NotNull(updatedDetail);
            Assert.True(updatedDetail.HasActivePackage);
            Assert.True(updatedDetail.HasApprovedPortfolio);
            Assert.True(updatedDetail.HasServiceAreas);
            Assert.False(updatedDetail.HasDocumentsUploaded); // Still missing docs
            Assert.Single(updatedDetail.Warnings);
        }

        [Fact]
        public async Task AdjudicationActions_AuditLogged_AndInvalidatesCache()
        {
            var ctx = new TestContext();
            var manager = new User
            {
                UserID = 20,
                FullName = "Reviewable Decorator",
                Role = "EventManager",
                VerificationStatus = "Pending",
                IsActive = true
            };
            ctx.UserRepo.Users.Add(manager);

            // 1. Verify
            var (verifySuccess, _) = await ctx.ReviewService.VerifyAsync(20, 99);
            Assert.True(verifySuccess);
            Assert.Equal("Verified", manager.VerificationStatus);
            Assert.Contains(ctx.ConfigRepo.AuditLogs, l => l.Action == "ApproveEventManagerVerification" && l.EntityId == 20);

            // 2. Reject (requires reason)
            var (rejectFail, emptyErr) = await ctx.ReviewService.RejectAsync(20, "", 99);
            Assert.False(rejectFail);
            Assert.NotNull(emptyErr);

            var (rejectSuccess, _) = await ctx.ReviewService.RejectAsync(20, "Missing business registration proof", 99);
            Assert.True(rejectSuccess);
            Assert.Equal("Rejected", manager.VerificationStatus);
            Assert.Contains(ctx.ConfigRepo.AuditLogs, l => l.Action == "RejectEventManagerVerification" && l.Details != null && l.Details.Contains("Missing business registration proof"));

            // 3. Return to Pending
            var (pendingSuccess, _) = await ctx.ReviewService.ReturnToPendingAsync(20, 99);
            Assert.True(pendingSuccess);
            Assert.Equal("Pending", manager.VerificationStatus);

            // 4. Suspend
            var (suspendSuccess, _) = await ctx.ReviewService.SuspendAsync(20, "Temporary operational freeze", 99);
            Assert.True(suspendSuccess);
            Assert.Equal("Suspended", manager.VerificationStatus);
            Assert.False(manager.IsActive);

            // 5. Reactivate
            var (reactivateSuccess, _) = await ctx.ReviewService.ReactivateAsync(20, 99);
            Assert.True(reactivateSuccess);
            Assert.Equal("Verified", manager.VerificationStatus);
            Assert.True(manager.IsActive);
        }

        [Fact]
        public async Task UpdateServiceAreasAsync_NormalizesCityState_AndReportsUnnormalized()
        {
            var ctx = new TestContext();
            var manager = new User
            {
                UserID = 30,
                FullName = "Area Decorator",
                Role = "EventManager",
                ServiceAreas = "Old City, Old State"
            };
            ctx.UserRepo.Users.Add(manager);

            string rawInput = "surat, gujarat\r\nmumbai, maharashtra\r\ninvalidSingleWord\r\nnavsari, gujarat";
            var (success, normalized, unnormalized, error) = await ctx.ReviewService.UpdateServiceAreasAsync(30, rawInput, 99);

            Assert.True(success);
            Assert.Null(error);
            Assert.Contains("Surat, Gujarat", normalized);
            Assert.Contains("Mumbai, Maharashtra", normalized);
            Assert.Contains("Navsari, Gujarat", normalized);
            Assert.Single(unnormalized);
            Assert.Equal("invalidSingleWord", unnormalized[0]);
            Assert.Equal(normalized, manager.ServiceAreas);
            Assert.Contains(ctx.ConfigRepo.AuditLogs, l => l.Action == "EditServiceAreas" && l.EntityId == 30);
        }

        [Fact]
        public async Task PortfolioModeration_ApproveAndReject_EnforcesReasonOnReject()
        {
            var ctx = new TestContext();
            var portfolio = new EventManagerPortfolio
            {
                PortfolioID = 301,
                UserID = 30,
                Title = "Floral Stage",
                IsApproved = false
            };
            ctx.EventRepo.Portfolios.Add(portfolio);

            // Reject without reason fails
            var (rejectFail, err) = await ctx.ReviewService.RejectPortfolioAsync(301, "", 99);
            Assert.False(rejectFail);
            Assert.NotNull(err);

            // Approve
            var (approveSuccess, _) = await ctx.ReviewService.ApprovePortfolioAsync(301, 99);
            Assert.True(approveSuccess);
            Assert.True(portfolio.IsApproved);
            Assert.Contains(ctx.ConfigRepo.AuditLogs, l => l.Action == "ApprovePortfolioItem" && l.EntityId == 301);

            // Reject with reason
            var (rejectSuccess, _) = await ctx.ReviewService.RejectPortfolioAsync(301, "Low resolution photo", 99);
            Assert.True(rejectSuccess);
            Assert.False(portfolio.IsApproved);
            Assert.Contains(ctx.ConfigRepo.AuditLogs, l => l.Action == "RejectPortfolioItem" && l.Details != null && l.Details.Contains("Low resolution photo"));
        }

        [Fact]
        public async Task EmployeeController_Returns403_OnCommissionEndpoints_WhenUserIsEmployee()
        {
            var ctx = new TestContext();
            var siteSettings = new SiteSettingsService(ctx.ConfigRepo);
            var controller = new EmployeeController(
                null!, ctx.UserRepo, null!, ctx.ConfigRepo, null!, null!,
                null!, null!, null!, ctx.IdService, ctx.MediaStorage,
                ctx.EventRepo, siteSettings, null, null, ctx.ReviewService);

            // Set User to Employee role
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "50"),
                new Claim(ClaimTypes.Role, "Employee"),
                new Claim(ClaimTypes.Name, "Operations Staff")
            };
            var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };

            // 1. EventCommissions GET
            var getResult = await controller.EventCommissions();
            Assert.IsType<ForbidResult>(getResult);

            // 2. UpdateGlobalEventCommission POST
            var postGlobalResult = await controller.UpdateGlobalEventCommission(new UpdateGlobalEventCommissionRequest { CommissionRate = 12m });
            Assert.IsType<ForbidResult>(postGlobalResult);

            // 3. UpdateEventManagerCommission POST
            var postMgrResult = await controller.UpdateEventManagerCommission(new UpdateEventManagerCommissionRequest { UserId = 1, CommissionRate = 10m });
            Assert.IsType<ForbidResult>(postMgrResult);

            // 4. UpdateEventCommission POST
            var postPkgResult = await controller.UpdateEventCommission(new UpdateEventCommissionRequest { PackageId = 1, CommissionRate = 8m });
            Assert.IsType<ForbidResult>(postPkgResult);

            // 5. UpdateBookingEventCommission POST
            var postBookingResult = await controller.UpdateBookingEventCommission(new UpdateBookingEventCommissionRequest { BookingId = 1, CommissionRate = 5m });
            Assert.IsType<ForbidResult>(postBookingResult);
        }

        [Fact]
        public async Task EmployeeController_PermitsEventManagerReviewActions_ForEmployee()
        {
            var ctx = new TestContext();
            ctx.UserRepo.Users.Add(new User { UserID = 100, FullName = "Decor Pro", Role = "EventManager", VerificationStatus = "Pending", IsActive = true, CreatedAt = DateTime.UtcNow });

            var siteSettings = new SiteSettingsService(ctx.ConfigRepo);
            var controller = new EmployeeController(
                null!, ctx.UserRepo, null!, ctx.ConfigRepo, null!, null!,
                null!, null!, null!, ctx.IdService, ctx.MediaStorage,
                ctx.EventRepo, siteSettings, null, null, ctx.ReviewService);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "51"),
                new Claim(ClaimTypes.Role, "Employee"),
                new Claim(ClaimTypes.Name, "Operations Staff")
            };
            var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
            var httpContext = new DefaultHttpContext { User = user };
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new SimpleTempDataProvider());

            // 1. View list
            var listResult = await controller.EventManagerVerifications("Pending");
            var viewResult = Assert.IsType<ViewResult>(listResult);
            var model = Assert.IsType<EventManagerListViewModel>(viewResult.Model);
            Assert.Single(model.Managers);

            // 2. View detail
            var detailResult = await controller.EventManagerDetail(100);
            var detailView = Assert.IsType<ViewResult>(detailResult);
            var detailModel = Assert.IsType<EventManagerDetailViewModel>(detailView.Model);
            Assert.Equal(100, detailModel.Manager.UserID);

            // 3. Verify
            var approveResult = await controller.ApproveEventManagerVerification(100);
            var redirect = Assert.IsType<RedirectToActionResult>(approveResult);
            Assert.Equal("EventManagerVerifications", redirect.ActionName);

            var updatedUser = await ctx.UserRepo.GetByIdAsync(100);
            Assert.NotNull(updatedUser);
            Assert.Equal("Verified", updatedUser.VerificationStatus);
        }
    }
}
