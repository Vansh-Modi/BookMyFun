using System.Reflection;
using BookMyFun.Controllers;
using BookMyFun.Data;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Checkout;
using BookMyFun.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests;

public class Phase8SelfServiceProfileTests
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
                existing.Bio = user.Bio;
                existing.CancellationPolicy = user.CancellationPolicy;
                existing.YearsOfExperience = user.YearsOfExperience;
                existing.ServiceAreas = user.ServiceAreas;
                existing.ProfilePictureUrl = user.ProfilePictureUrl;
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
        public List<EventPackage> Packages = new();
        public List<EventManagerPortfolio> Portfolios = new();
        public List<Booking> Bookings = new();

        public Task<List<User>> GetDecoratorsAsync(bool onlyVerified = false) => Task.FromResult(Decorators);
        public Task<List<EventPackage>> GetPackagesByUserIdAsync(int userId) => Task.FromResult(Packages.Where(p => p.CreatedByUserID == userId).ToList());
        public Task<EventPackage?> GetPackageByIdAsync(int packageId) => Task.FromResult(Packages.FirstOrDefault(p => p.PackageID == packageId));
        public Task<List<EventManagerPortfolio>> GetPortfoliosByUserIdAsync(int userId, bool onlyApproved = true)
        {
            var list = Portfolios.Where(p => p.UserID == userId);
            if (onlyApproved) list = list.Where(p => p.IsApproved);
            return Task.FromResult(list.ToList());
        }
        public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(true);
        public Task<List<EventPackage>> GetAllPackagesAsync() => Task.FromResult(Packages);
        public Task<List<EventPackage>> GetActivePackagesAsync() => Task.FromResult(Packages.Where(p => p.IsActive).ToList());
        public Task<int> CreatePackageAsync(EventPackage package) => Task.FromResult(1);
        public Task<bool> UpdatePackageAsync(EventPackage package) => Task.FromResult(true);
        public Task<bool> UpdatePackageCommissionRateAsync(int packageId, decimal? commissionRate) => Task.FromResult(true);
        public Task<bool> DeletePackageAsync(int packageId) => Task.FromResult(true);
        public Task<List<Booking>> GetAssignedEventBookingsAsync(int decoratorUserId) => Task.FromResult(Bookings.Where(b => b.DecoratorAssignedID == decoratorUserId).ToList());
        public Task<List<Booking>> GetAllEventBookingsAsync() => Task.FromResult(Bookings);
        public Task<bool> UpdateSetupStatusAsync(int bookingId, string setupStatus, string? setupProofMediaUrls, int? decoratorId) => Task.FromResult(true);
        public Task<bool> AssignDecoratorAsync(int bookingId, int decoratorUserId) => Task.FromResult(true);
        public Task<bool> CancelOrRemoveEventFromBookingAsync(int bookingId) => Task.FromResult(true);
        public Task<List<string>> GetAllDistinctPropertyLocationsAsync() => Task.FromResult(new List<string>());
        public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>("Mumbai, Pune");
        public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas) => Task.FromResult(true);
        public Task<int> AddPortfolioItemAsync(EventManagerPortfolio item) { Portfolios.Add(item); return Task.FromResult(item.PortfolioID > 0 ? item.PortfolioID : 1); }
        public Task<List<EventManagerPortfolio>> GetAllPortfoliosAsync() => Task.FromResult(Portfolios);
        public Task<bool> ApprovePortfolioItemAsync(int portfolioId, bool isApproved) => Task.FromResult(true);
    }

    private class FakeConfigRepository : IConfigRepository
    {
        public List<(int? UserId, string Action, string Details)> AuditLogs = new();
        public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
        public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
        public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
        public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
        public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
        public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
        public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details)
        {
            AuditLogs.Add((performedByUserId, action, details ?? string.Empty));
            return Task.CompletedTask;
        }
    }

    private class FakeMediaStorageService : IMediaStorageService
    {
        public List<string> SavedPrivateDocuments = new();
        public Task<(string publicUrl, string publicId)> SaveMediaFileAsync(IFormFile file, string mediaType) => Task.FromResult(("/uploads/sample.mp4", "sample_id"));
        public Task<string> SavePrivateDocumentAsync(IFormFile file, int clientId)
        {
            var key = $"doc_{clientId}_{file.FileName}";
            SavedPrivateDocuments.Add(key);
            return Task.FromResult(key);
        }
        public Task<bool> ValidateExternalUrlAsync(string url) => Task.FromResult(true);
        public string GenerateExpiringDocumentToken(string documentKey, int requestingUserId, string userRole, int expirationMinutes = 15) => "token";
        public bool ValidateExpiringDocumentToken(string documentKey, string token, int requestingUserId, string userRole) => true;
        public string GetPrivateDocumentPhysicalPath(string documentKey) => "C:\\private\\" + documentKey;
    }

    private class FakeContactPrivacyScanner : IContactPrivacyScanner
    {
        public List<string> LoggedViolations = new();

        public (string SanitizedContent, bool HasViolations, List<string> DetectedPatterns) ScanAndMask(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return (input ?? string.Empty, false, new List<string>());
            bool violated = false;
            var detected = new List<string>();
            var sanitized = input;

            // Mask phone numbers
            if (System.Text.RegularExpressions.Regex.IsMatch(input, @"\b\d{10}\b"))
            {
                violated = true;
                detected.Add("Phone");
                sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\b\d{10}\b", "[Contact Details Masked]");
            }

            // Mask solicitation / handles
            if (System.Text.RegularExpressions.Regex.IsMatch(input, @"(?i)\b(whatsapp|telegram|instagram|dm me|call me)\b"))
            {
                violated = true;
                detected.Add("Solicitation");
                sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"(?i)\b(whatsapp|telegram|instagram|dm me|call me)\b", "[Contact Details Masked]");
            }

            return (sanitized, violated, detected);
        }

        public Task<string> ScanMaskAndLogAsync(int userId, string contentType, string? input)
        {
            var (sanitized, violated, patterns) = ScanAndMask(input);
            if (violated)
            {
                LoggedViolations.Add($"{contentType}: {string.Join(",", patterns)}");
            }
            return Task.FromResult(sanitized);
        }
    }

    [Fact]
    public void EventManagerProfileInput_ContainsOnlyAllowedFields_NoContactOrPictureProperties()
    {
        // Rule 8b: Enforce in the model binding layer: create a dedicated EventManagerProfileInput DTO
        // without phone, WhatsApp number, email, website, social links, street address, or profile picture.
        var blockedSubstrings = new[]
        {
            "picture", "photo", "avatar", "logo", "image",
            "phone", "mobile", "whatsapp", "email", "address",
            "website", "url", "social", "instagram", "facebook", "telegram"
        };

        var properties = typeof(EventManagerProfileInput).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var propName = prop.Name.ToLowerInvariant();
            foreach (var blocked in blockedSubstrings)
            {
                Assert.DoesNotContain(blocked, propName);
            }
        }

        // Allowed fields must exist
        var propNames = properties.Select(p => p.Name).ToList();
        Assert.Contains("FullName", propNames);
        Assert.Contains("YearsOfExperience", propNames);
        Assert.Contains("ServiceAreas", propNames);
        Assert.Contains("Bio", propNames);
        Assert.Contains("EventTypesCommaSeparated", propNames);
        Assert.Contains("CancellationPolicy", propNames);
    }

    [Fact]
    public async Task PostProfile_SanitizesBio_AndEnforcesPlatformDefaultAvatar()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 5, FullName = "Original Name", Role = "EventManager", IsActive = true, VerificationStatus = "Verified", ProfilePictureUrl = "/images/custom.jpg" } }
        };
        var eventRepo = new FakeEventPackageRepository();
        var configRepo = new FakeConfigRepository();
        var privacyScanner = new FakeContactPrivacyScanner();
        var mediaStorage = new FakeMediaStorageService();

        var controller = new EventManagerController(
            eventRepo,
            mediaStorage,
            configRepo,
            NullLogger<EventManagerController>.Instance,
            userRepo,
            privacyScanner
        );

        var httpContext = new DefaultHttpContext();
        var claims = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "5"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "EventManager")
            }, "TestAuth")
        );
        httpContext.User = claims;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

        var input = new EventManagerProfileInput
        {
            FullName = "Grand Celebrations",
            YearsOfExperience = 6,
            ServiceAreas = "Pune, Lonavala",
            Bio = "Contact me at 9876543210 or WhatsApp for fast bookings!",
            CancellationPolicy = "Free cancellation 48h prior."
        };

        var result = await controller.Profile(input);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Profile", redirect.ActionName);

        var updatedUser = await userRepo.GetByIdAsync(5);
        Assert.NotNull(updatedUser);
        Assert.Equal("Grand Celebrations", updatedUser.FullName);
        Assert.Equal("/images/default-avatar.svg", updatedUser.ProfilePictureUrl); // Platform default avatar enforced
        Assert.DoesNotContain("9876543210", updatedUser.Bio);
        Assert.Contains("[Contact Details Masked]", updatedUser.Bio);
    }

    [Fact]
    public async Task PostProfile_RejectsAttemptToSubmitBlockedFields()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 6, FullName = "Safety Decor", Role = "EventManager", IsActive = true, VerificationStatus = "Verified" } }
        };
        var eventRepo = new FakeEventPackageRepository();
        var configRepo = new FakeConfigRepository();
        var privacyScanner = new FakeContactPrivacyScanner();

        var controller = new EventManagerController(
            eventRepo,
            new FakeMediaStorageService(),
            configRepo,
            NullLogger<EventManagerController>.Instance,
            userRepo,
            privacyScanner
        );

        var httpContext = new DefaultHttpContext();
        var claims = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "6")
            }, "TestAuth")
        );
        httpContext.User = claims;

        // Simulate forbidden form key injection (ProfilePictureUrl or Phone)
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            { "ProfilePictureUrl", "https://malicious.com/avatar.jpg" },
            { "Phone", "9999999999" }
        });
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

        var input = new EventManagerProfileInput
        {
            FullName = "Safety Decor"
        };

        var result = await controller.Profile(input);

        // Must return View with model validation error
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(string.Empty));
    }

    [Fact]
    public async Task UploadPortfolio_SetsIsApprovedToFalse_AndEnforcesAdminModeration()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 7, FullName = "Decor Hub", Role = "EventManager", IsActive = true, VerificationStatus = "Verified" } }
        };
        var eventRepo = new FakeEventPackageRepository();
        var configRepo = new FakeConfigRepository();
        var privacyScanner = new FakeContactPrivacyScanner();
        var mediaStorage = new FakeMediaStorageService();

        var controller = new EventManagerController(
            eventRepo,
            mediaStorage,
            configRepo,
            NullLogger<EventManagerController>.Instance,
            userRepo,
            privacyScanner
        );

        var httpContext = new DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "7")
            }, "TestAuth")
        );
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

        // Mock video upload file
        var content = "dummy-video-data"u8.ToArray();
        var file = new FormFile(new MemoryStream(content), 0, content.Length, "MediaFile", "portfolio.mp4");

        var model = new EventManagerPortfolioUploadViewModel
        {
            Title = "Stunning Wedding Canopy",
            MediaType = "video",
            MediaFile = file,
            Description = "Call me 9876543210 for decor inquiries."
        };

        var result = await controller.UploadPortfolio(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Profile", redirect.ActionName);

        // Verify newly created item in repository
        Assert.Single(eventRepo.Portfolios);
        var item = eventRepo.Portfolios[0];
        Assert.Equal("Stunning Wedding Canopy", item.Title);
        // Rule 8d: Each item goes to admin approval (IsApproved defaults to 0)
        Assert.False(item.IsApproved);
        // Rule 8c: Caption contact details masked
        Assert.DoesNotContain("9876543210", item.Description);
        Assert.Contains("[Contact Details Masked]", item.Description);
    }

    [Fact]
    public async Task RequestVerification_SavesFilesToPrivateStorage_NotWwwroot()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 8, FullName = "Verify Me", Role = "EventManager", IsActive = true, VerificationStatus = "Pending" } }
        };
        var eventRepo = new FakeEventPackageRepository();
        var configRepo = new FakeConfigRepository();
        var mediaStorage = new FakeMediaStorageService();

        var controller = new EventManagerController(
            eventRepo,
            mediaStorage,
            configRepo,
            NullLogger<EventManagerController>.Instance,
            userRepo
        );

        var httpContext = new DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "8")
            }, "TestAuth")
        );
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

        var idContent = "government-id-bytes"u8.ToArray();
        var idFile = new FormFile(new MemoryStream(idContent), 0, idContent.Length, "idProof", "aadhar.pdf");

        var bizContent = "business-proof-bytes"u8.ToArray();
        var bizFile = new FormFile(new MemoryStream(bizContent), 0, bizContent.Length, "businessProof", "gst.pdf");

        var result = await controller.RequestVerification(idFile, bizFile);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Profile", redirect.ActionName);

        // Verify saved to private document storage
        Assert.Equal(2, mediaStorage.SavedPrivateDocuments.Count);
        Assert.Contains(mediaStorage.SavedPrivateDocuments, d => d.Contains("aadhar.pdf"));
        Assert.Contains(mediaStorage.SavedPrivateDocuments, d => d.Contains("gst.pdf"));
    }

    [Fact]
    public async Task Inbox_CalculatesEarnings_StrictlyFromEventManagerPayoutAmount()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 9, FullName = "Payout Decorator", Role = "EventManager", IsActive = true, VerificationStatus = "Verified" } }
        };
        var eventRepo = new FakeEventPackageRepository
        {
            Bookings =
            {
                new Booking
                {
                    BookingID = 1001,
                    DecoratorAssignedID = 9,
                    Status = "Confirmed",
                    SetupStatus = "Completed",
                    EventPackagePriceSnapshot = 15000,
                    EventPlatformCommissionAmount = 1500,
                    EventManagerPayoutAmount = 13500 // Payout column
                },
                new Booking
                {
                    BookingID = 1002,
                    DecoratorAssignedID = 9,
                    Status = "Confirmed",
                    SetupStatus = "InProgress",
                    EventPackagePriceSnapshot = 20000,
                    EventPlatformCommissionAmount = 2000,
                    EventManagerPayoutAmount = 18000 // Payout column
                },
                new Booking
                {
                    BookingID = 1003,
                    DecoratorAssignedID = 9,
                    Status = "Cancelled", // Cancelled booking
                    SetupStatus = "Cancelled",
                    EventPackagePriceSnapshot = 10000,
                    EventManagerPayoutAmount = 0
                }
            }
        };

        var controller = new EventManagerController(
            eventRepo,
            new FakeMediaStorageService(),
            new FakeConfigRepository(),
            NullLogger<EventManagerController>.Instance,
            userRepo
        );

        var httpContext = new DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "9")
            }, "TestAuth")
        );
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Inbox();

        var viewResult = Assert.IsType<ViewResult>(result);
        var bookings = Assert.IsAssignableFrom<List<Booking>>(viewResult.Model);
        Assert.Equal(3, bookings.Count);

        // Rule 8f: Total Payout = 13500 + 18000 = 31500
        Assert.Equal(31500m, (decimal)controller.ViewBag.TotalPayout);
    }
}
