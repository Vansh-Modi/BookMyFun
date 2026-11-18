using System.Reflection;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Checkout;
using BookMyFun.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests;

public class Phase7PublicProfileTests
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
        public List<User> Decorators = new();
        public List<EventPackage> Packages = new();
        public List<EventManagerPortfolio> Portfolios = new();

        public Task<List<User>> GetDecoratorsAsync(bool onlyVerified = false)
        {
            var list = Decorators.AsEnumerable();
            if (onlyVerified)
                list = list.Where(d => d.VerificationStatus == "Verified");
            return Task.FromResult(list.ToList());
        }

        public Task<List<EventPackage>> GetPackagesByUserIdAsync(int userId)
        {
            return Task.FromResult(Packages.Where(p => p.CreatedByUserID == userId).ToList());
        }

        public Task<EventPackage?> GetPackageByIdAsync(int packageId)
        {
            return Task.FromResult(Packages.FirstOrDefault(p => p.PackageID == packageId));
        }

        public Task<List<EventManagerPortfolio>> GetPortfoliosByUserIdAsync(int userId, bool onlyApproved = true)
        {
            var list = Portfolios.Where(p => p.UserID == userId);
            if (onlyApproved)
                list = list.Where(p => p.IsApproved);
            return Task.FromResult(list.ToList());
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
        public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>("Mumbai, Pune, Lonavala");
        public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas) => Task.FromResult(true);
        public Task<int> AddPortfolioItemAsync(EventManagerPortfolio item) => Task.FromResult(1);
        public Task<List<EventManagerPortfolio>> GetAllPortfoliosAsync() => Task.FromResult(Portfolios);
        public Task<bool> ApprovePortfolioItemAsync(int portfolioId, bool isApproved) => Task.FromResult(true);
    }

    private class FakePropertyRepository : IPropertyRepository
    {
        public List<Occasion> Occasions = new();
        public Dictionary<int, List<int>> PropertyOccasions = new();

        public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(Occasions);
        public Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId)
        {
            if (PropertyOccasions.TryGetValue(propertyId, out var list))
                return Task.FromResult(list);
            return Task.FromResult(new List<int>());
        }

        public Task<Property?> GetByIdAsync(int propertyId) => Task.FromResult<Property?>(new Property { PropertyID = propertyId, Title = "Sample Villa", Location = "Lonavala, Maharashtra" });
        public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(new List<Property>());
        public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Property>());
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

    private class FakeConfigRepository : IConfigRepository
    {
        public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
        public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
        public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
        public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
        public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
        public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
        public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details) => Task.CompletedTask;
        public Task<List<AuditLog>> GetAuditLogsAsync(int limit = 100) => Task.FromResult(new List<AuditLog>());
        public Task<(List<AuditLog> Items, int TotalCount)> GetAuditLogsPagedAsync(string? search = null, string? action = null, string? entityType = null, DateTime? fromDate = null, DateTime? toDate = null, int page = 1, int pageSize = 25) => Task.FromResult((new List<AuditLog>(), 0));
    }

    private class FakeMediaStorageService : IMediaStorageService
    {
        public Task<(string publicUrl, string publicId)> SaveMediaFileAsync(IFormFile file, string mediaType) => Task.FromResult(("/uploads/sample.jpg", "sample_id"));
        public Task<string> SavePrivateDocumentAsync(IFormFile file, int clientId) => Task.FromResult("private_doc_key");
        public Task<bool> ValidateExternalUrlAsync(string url) => Task.FromResult(true);
        public string GenerateExpiringDocumentToken(string documentKey, int requestingUserId, string userRole, int expirationMinutes = 15) => "fake_token";
        public bool ValidateExpiringDocumentToken(string documentKey, string token, int requestingUserId, string userRole) => true;
        public string GetPrivateDocumentPhysicalPath(string documentKey) => "C:\\private\\doc.pdf";
    }

    private class FakeContactPrivacyScanner : IContactPrivacyScanner
    {
        public (string SanitizedContent, bool HasViolations, List<string> DetectedPatterns) ScanAndMask(string? input)
        {
            if (string.IsNullOrEmpty(input)) return (input ?? string.Empty, false, new List<string>());
            var sanitized = System.Text.RegularExpressions.Regex.Replace(input, @"\b\d{10}\b", "[Contact Details Masked]");
            return (sanitized, sanitized != input, new List<string>());
        }

        public Task<string> ScanMaskAndLogAsync(int userId, string contentType, string? input)
        {
            var (sanitized, _, _) = ScanAndMask(input);
            return Task.FromResult(sanitized);
        }
    }

    private EventManagerController CreateController(
        FakeUserRepository userRepo,
        FakeEventPackageRepository eventRepo,
        FakePropertyRepository propRepo)
    {
        var configRepo = new FakeConfigRepository();
        var privacyScanner = new FakeContactPrivacyScanner();
        var sanitizer = new HtmlSanitizerService();
        var mediaStorage = new FakeMediaStorageService();
        var checkoutService = new CheckoutEventManagerService(eventRepo, userRepo, propRepo);

        return new EventManagerController(
            eventRepo,
            mediaStorage,
            configRepo,
            NullLogger<EventManagerController>.Instance,
            userRepo,
            privacyScanner,
            null,
            null,
            propRepo,
            sanitizer,
            checkoutService
        );
    }

    [Fact]
    public async Task PublicProfile_Returns404_WhenUserDoesNotExist()
    {
        var userRepo = new FakeUserRepository();
        var eventRepo = new FakeEventPackageRepository();
        var propRepo = new FakePropertyRepository();
        var controller = CreateController(userRepo, eventRepo, propRepo);

        var result = await controller.PublicProfile(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PublicProfile_Returns404_WhenUserIsNotEventManager()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 10, FullName = "Regular Client", Role = "Client", IsActive = true, VerificationStatus = "Verified" } }
        };
        var eventRepo = new FakeEventPackageRepository();
        var propRepo = new FakePropertyRepository();
        var controller = CreateController(userRepo, eventRepo, propRepo);

        var result = await controller.PublicProfile(10);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PublicProfile_Returns404_WhenEventManagerIsInactive()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 15, FullName = "Deactivated Manager", Role = "EventManager", IsActive = false, VerificationStatus = "Verified" } }
        };
        var eventRepo = new FakeEventPackageRepository();
        var propRepo = new FakePropertyRepository();
        var controller = CreateController(userRepo, eventRepo, propRepo);

        var result = await controller.PublicProfile(15);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PublicProfile_Returns404_WhenEventManagerIsNotVerified()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 20, FullName = "Pending Manager", Role = "EventManager", IsActive = true, VerificationStatus = "Pending" } }
        };
        var eventRepo = new FakeEventPackageRepository();
        var propRepo = new FakePropertyRepository();
        var controller = CreateController(userRepo, eventRepo, propRepo);

        var result = await controller.PublicProfile(20);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PublicProfile_Returns200_WithVerifiedActiveEventManager()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 25, FullName = "Elite Decorators", Role = "EventManager", IsActive = true, VerificationStatus = "Verified", Bio = "Premier event design." } }
        };
        var eventRepo = new FakeEventPackageRepository
        {
            Packages =
            {
                new EventPackage { PackageID = 101, CreatedByUserID = 25, Name = "Deluxe Birthday Decor", Category = "Birthday", BasePrice = 12000, IsActive = true }
            }
        };
        var propRepo = new FakePropertyRepository();
        var controller = CreateController(userRepo, eventRepo, propRepo);

        var result = await controller.PublicProfile(25);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<EventManagerPublicProfileViewModel>(viewResult.Model);
        Assert.Equal(25, vm.ManagerID);
        Assert.Equal("Elite Decorators", vm.FullName);
        Assert.Equal("/images/default-avatar.svg", vm.ProfilePictureUrl);
        Assert.Single(vm.Packages);
    }

    [Fact]
    public async Task PublicProfile_ExcludesUnapprovedPortfolioItems()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 30, FullName = "Creative Events", Role = "EventManager", IsActive = true, VerificationStatus = "Verified" } }
        };
        var eventRepo = new FakeEventPackageRepository
        {
            Portfolios =
            {
                new EventManagerPortfolio { PortfolioID = 1, UserID = 30, Title = "Approved Birthday Setup", MediaUrl = "/uploads/1.jpg", IsApproved = true },
                new EventManagerPortfolio { PortfolioID = 2, UserID = 30, Title = "Pending Moderation Setup", MediaUrl = "/uploads/2.jpg", IsApproved = false }
            }
        };
        var propRepo = new FakePropertyRepository();
        var controller = CreateController(userRepo, eventRepo, propRepo);

        var result = await controller.PublicProfile(30);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<EventManagerPublicProfileViewModel>(viewResult.Model);
        Assert.Single(vm.PortfolioItems);
        Assert.Equal(1, vm.PortfolioItems[0].PortfolioID);
        Assert.True(vm.PortfolioItems[0].IsApproved);
    }

    [Fact]
    public void EventManagerPublicProfileViewModel_ContainsZeroContactFields()
    {
        // Rule b: Do not show any contact details on this page, in the HTML, in JSON, or in the view model.
        // Use a dedicated public view model that does not contain contact fields at all.
        var forbiddenSubstrings = new[]
        {
            "phone", "mobile", "email", "whatsapp", "contact",
            "address", "street", "social", "instagram", "facebook", "website"
        };

        var properties = typeof(EventManagerPublicProfileViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var propName = prop.Name.ToLowerInvariant();
            foreach (var forbidden in forbiddenSubstrings)
            {
                Assert.DoesNotContain(forbidden, propName);
            }
        }
    }

    [Fact]
    public async Task PublicProfile_FiltersPackages_WhenOccasionMatches()
    {
        var userRepo = new FakeUserRepository
        {
            Users = { new User { UserID = 40, FullName = "Theme Masters", Role = "EventManager", IsActive = true, VerificationStatus = "Verified" } }
        };
        var eventRepo = new FakeEventPackageRepository
        {
            Packages =
            {
                new EventPackage { PackageID = 201, CreatedByUserID = 40, Name = "Birthday Balloon Arch", Category = "Birthday", BasePrice = 5000, IsActive = true },
                new EventPackage { PackageID = 202, CreatedByUserID = 40, Name = "Wedding Stage Mandap", Category = "Wedding", BasePrice = 25000, IsActive = true }
            }
        };
        var propRepo = new FakePropertyRepository
        {
            Occasions =
            {
                new Occasion { OccasionID = 5, OccasionName = "Birthday Celebration" }
            }
        };
        var controller = CreateController(userRepo, eventRepo, propRepo);

        var result = await controller.PublicProfile(40, propertyId: 10, occasionId: 5);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<EventManagerPublicProfileViewModel>(viewResult.Model);
        Assert.Equal(10, vm.PropertyID);
        Assert.Equal(5, vm.OccasionID);
        Assert.Equal("Birthday Celebration", vm.OccasionName);
        Assert.Single(vm.Packages);
        Assert.Equal(201, vm.Packages[0].PackageID);
    }
}
