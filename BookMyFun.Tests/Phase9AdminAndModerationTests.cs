using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace BookMyFun.Tests;

public class Phase9AdminAndModerationTests
{
    private class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private class FakeUserRepository : IUserRepository
    {
        public List<User> Users = new();

        public Task<User?> GetByIdAsync(int userId) => Task.FromResult(Users.FirstOrDefault(u => u.UserID == userId));
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult(Users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));
        public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult(Users.FirstOrDefault(u => u.AuthProvider == provider && u.AuthProviderKey == providerKey));
        public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(Users.Where(u => u.Role.Equals(role, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<List<User>> GetAllUsersAsync() => Task.FromResult(Users);
        public Task<int> CreateUserAsync(User user) { Users.Add(user); return Task.FromResult(user.UserID); }
        public Task<bool> UpdateUserAsync(User user)
        {
            var existing = Users.FirstOrDefault(u => u.UserID == user.UserID);
            if (existing != null)
            {
                existing.FullName = user.FullName;
                existing.IsActive = user.IsActive;
                existing.VerificationStatus = user.VerificationStatus;
            }
            return Task.FromResult(true);
        }
        public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
        public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
        public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
        public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate) => Task.FromResult(true);
        public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
    }

    private class FakeEventPackageRepository : IEventPackageRepository
    {
        public List<User> Decorators = new();
        public List<EventManagerPortfolio> Portfolios = new();
        public List<FlaggedContactAttempt> FlaggedAttempts = new();

        public Task<List<User>> GetDecoratorsAsync(bool onlyVerified = false) => Task.FromResult(Decorators);
        public Task<List<EventPackage>> GetPackagesByUserIdAsync(int userId) => Task.FromResult(new List<EventPackage>());
        public Task<EventPackage?> GetPackageByIdAsync(int packageId) => Task.FromResult<EventPackage?>(null);
        public Task<List<EventManagerPortfolio>> GetPortfoliosByUserIdAsync(int userId, bool onlyApproved = true) => Task.FromResult(Portfolios.Where(p => !onlyApproved || p.IsApproved).ToList());
        public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(true);
        public Task<List<EventPackage>> GetAllPackagesAsync() => Task.FromResult(new List<EventPackage>());
        public Task<List<EventPackage>> GetActivePackagesAsync() => Task.FromResult(new List<EventPackage>());
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
        public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>("Mumbai");
        public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas) => Task.FromResult(true);
        public Task<int> AddPortfolioItemAsync(EventManagerPortfolio item) { Portfolios.Add(item); return Task.FromResult(item.PortfolioID); }
        public Task<List<EventManagerPortfolio>> GetAllPortfoliosAsync() => Task.FromResult(Portfolios);
        public Task<bool> ApprovePortfolioItemAsync(int portfolioId, bool isApproved)
        {
            var item = Portfolios.FirstOrDefault(p => p.PortfolioID == portfolioId);
            if (item != null) item.IsApproved = isApproved;
            return Task.FromResult(true);
        }
        public Task<bool> DeletePortfolioItemAsync(int portfolioId, int userId) => Task.FromResult(true);
        public Task<List<FlaggedContactAttempt>> GetFlaggedContactAttemptsAsync() => Task.FromResult(FlaggedAttempts);
        public Task<bool> UpdateFlaggedContactAttemptStatusAsync(int flagId, string status)
        {
            var attempt = FlaggedAttempts.FirstOrDefault(f => f.FlagID == flagId);
            if (attempt != null) attempt.Status = status;
            return Task.FromResult(true);
        }
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

    private AdminController CreateAdminController(
        FakeUserRepository userRepo,
        FakeEventPackageRepository eventRepo,
        FakeConfigRepository configRepo)
    {
        var controller = new AdminController(
            propRepo: null!,
            userRepo: userRepo,
            bookingRepo: null!,
            subRepo: null!,
            configRepo: configRepo,
            ownerReqRepo: null!,
            contactRepo: null!,
            siteSettingsService: null!,
            financialRepo: null!,
            env: null!,
            refundEngine: null!,
            partnerLeadService: null!,
            damageClaimService: null!,
            idVerificationService: null!,
            mediaStorage: null!,
            eventPackageRepo: eventRepo,
            legalService: null!
        );

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "99"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "AdminAuth"));

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

        return controller;
    }

    [Fact]
    public async Task ApproveEventManagerVerification_SetsStatusToVerified_AndLogsAudit()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 101, FullName = "Creative Floral Decor", Role = "EventManager", VerificationStatus = "Pending" } }
        };
        var eventRepo = new FakeEventPackageRepository();
        var configRepo = new FakeConfigRepository();
        var controller = CreateAdminController(userRepo, eventRepo, configRepo);

        var result = await controller.ApproveEventManagerVerification(101);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("EventManagerVerifications", redirect.ActionName);

        var user = await userRepo.GetByIdAsync(101);
        Assert.NotNull(user);
        Assert.Equal("Verified", user.VerificationStatus);

        // Audit log verification
        var log = Assert.Single(configRepo.AuditLogs, l => l.Action == "ApproveEventManagerVerification");
        Assert.Equal(99, log.PerformedBy);
        Assert.Equal("User", log.EntityType);
        Assert.Equal(101, log.EntityId);
    }

    [Fact]
    public async Task RejectEventManagerVerification_SetsStatusToRejected_AndLogsAudit()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 102, FullName = "Incomplete Proof Decor", Role = "EventManager", VerificationStatus = "Pending" } }
        };
        var eventRepo = new FakeEventPackageRepository();
        var configRepo = new FakeConfigRepository();
        var controller = CreateAdminController(userRepo, eventRepo, configRepo);

        var result = await controller.RejectEventManagerVerification(102, "Incomplete KYC document images");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("EventManagerVerifications", redirect.ActionName);

        var user = await userRepo.GetByIdAsync(102);
        Assert.NotNull(user);
        Assert.Equal("Rejected", user.VerificationStatus);

        var log = Assert.Single(configRepo.AuditLogs, l => l.Action == "RejectEventManagerVerification");
        Assert.Equal(99, log.PerformedBy);
        Assert.Equal(102, log.EntityId);
        Assert.Contains("Incomplete KYC document images", log.Details);
    }

    [Fact]
    public async Task ApprovePortfolioItem_SetsIsApprovedTrue_AndLogsAudit()
    {
        var userRepo = new FakeUserRepository();
        var eventRepo = new FakeEventPackageRepository
        {
            Portfolios =
            {
                new EventManagerPortfolio { PortfolioID = 501, UserID = 101, Title = "Grand Stage Lighting", IsApproved = false }
            }
        };
        var configRepo = new FakeConfigRepository();
        var controller = CreateAdminController(userRepo, eventRepo, configRepo);

        var result = await controller.ApprovePortfolioItem(501);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("EventManagerVerifications", redirect.ActionName);

        Assert.True(eventRepo.Portfolios[0].IsApproved);

        var log = Assert.Single(configRepo.AuditLogs, l => l.Action == "ApprovePortfolioItem");
        Assert.Equal(99, log.PerformedBy);
        Assert.Equal("EventManagerPortfolio", log.EntityType);
        Assert.Equal(501, log.EntityId);
    }

    [Fact]
    public async Task RejectPortfolioItem_SetsIsApprovedFalse_AndLogsAudit()
    {
        var userRepo = new FakeUserRepository();
        var eventRepo = new FakeEventPackageRepository
        {
            Portfolios =
            {
                new EventManagerPortfolio { PortfolioID = 502, UserID = 101, Title = "Blurry Balloon Setup", IsApproved = true }
            }
        };
        var configRepo = new FakeConfigRepository();
        var controller = CreateAdminController(userRepo, eventRepo, configRepo);

        var result = await controller.RejectPortfolioItem(502, "Low resolution photo");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("EventManagerVerifications", redirect.ActionName);

        Assert.False(eventRepo.Portfolios[0].IsApproved);

        var log = Assert.Single(configRepo.AuditLogs, l => l.Action == "RejectPortfolioItem");
        Assert.Equal(99, log.PerformedBy);
        Assert.Equal(502, log.EntityId);
        Assert.Contains("Low resolution photo", log.Details);
    }

    [Fact]
    public async Task DismissFlaggedContact_UpdatesStatusToDismissed_AndLogsAudit()
    {
        var userRepo = new FakeUserRepository();
        var eventRepo = new FakeEventPackageRepository
        {
            FlaggedAttempts =
            {
                new FlaggedContactAttempt { FlagID = 701, UserID = 101, Status = "PendingReview", RawContent = "Call me for party details" }
            }
        };
        var configRepo = new FakeConfigRepository();
        var controller = CreateAdminController(userRepo, eventRepo, configRepo);

        var result = await controller.DismissFlaggedContact(701);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("FlaggedContacts", redirect.ActionName);

        Assert.Equal("Dismissed", eventRepo.FlaggedAttempts[0].Status);

        var log = Assert.Single(configRepo.AuditLogs, l => l.Action == "DismissFlaggedContact");
        Assert.Equal(99, log.PerformedBy);
        Assert.Equal(701, log.EntityId);
    }

    [Fact]
    public async Task SuspendUserFromFlaggedContact_DeactivatesUser_SetsStatus_AndLogsAudit()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 105, FullName = "Offender Decorator", Role = "EventManager", IsActive = true } }
        };
        var eventRepo = new FakeEventPackageRepository
        {
            FlaggedAttempts =
            {
                new FlaggedContactAttempt { FlagID = 702, UserID = 105, Status = "PendingReview", RawContent = "Direct WhatsApp 9999999999" }
            }
        };
        var configRepo = new FakeConfigRepository();
        var controller = CreateAdminController(userRepo, eventRepo, configRepo);

        var result = await controller.SuspendUserFromFlaggedContact(702, 105, "Repeated off-platform contact sharing");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("FlaggedContacts", redirect.ActionName);

        var user = await userRepo.GetByIdAsync(105);
        Assert.NotNull(user);
        Assert.False(user.IsActive); // Suspended

        Assert.Equal("AccountSuspended", eventRepo.FlaggedAttempts[0].Status);

        var log = Assert.Single(configRepo.AuditLogs, l => l.Action == "SuspendUserForContactViolation");
        Assert.Equal(99, log.PerformedBy);
        Assert.Equal("User", log.EntityType);
        Assert.Equal(105, log.EntityId);
        Assert.Contains("Repeated off-platform contact sharing", log.Details);
    }
}
