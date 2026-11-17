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
using BookMyFun.Services.Checkout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class ServiceAreasStep4VerificationTests
    {
        private class TestTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class FakeConfigRepo : IConfigRepository
        {
            public readonly List<string> Audits = new();

            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details)
            {
                Audits.Add($"{performedByUserId}:{action}:{entityType}:{entityId}:{details}");
                return Task.CompletedTask;
            }

            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
            public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
        }

        private class FakeEventPackageRepo : IEventPackageRepository
        {
            public string? ServiceAreasInDb { get; set; } = "Pune, Maharashtra; Lonavala, Maharashtra";
            public List<string> CatalogLocations { get; set; } = new()
            {
                "Pune, Maharashtra",
                "Lonavala, Maharashtra",
                "Alibaug, Maharashtra",
                "Mahabaleshwar, Maharashtra",
                "Nashik, Maharashtra"
            };

            public Task<List<EventPackage>> GetAllPackagesAsync() => Task.FromResult(new List<EventPackage>());
            public Task<List<EventPackage>> GetActivePackagesAsync() => Task.FromResult(new List<EventPackage>());
            public Task<EventPackage?> GetPackageByIdAsync(int packageId) => Task.FromResult<EventPackage?>(null);
            public Task<int> CreatePackageAsync(EventPackage package) => Task.FromResult(1);
            public Task<bool> UpdatePackageAsync(EventPackage package) => Task.FromResult(true);
            public Task<bool> UpdatePackageCommissionRateAsync(int packageId, decimal? commissionRate) => Task.FromResult(true);
            public Task<bool> DeletePackageAsync(int packageId) => Task.FromResult(true);
            public Task<List<Booking>> GetAssignedEventBookingsAsync(int decoratorUserId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllEventBookingsAsync() => Task.FromResult(new List<Booking>());
            public Task<bool> UpdateSetupStatusAsync(int bookingId, string setupStatus, string? setupProofMediaUrls, int? decoratorId) => Task.FromResult(true);
            public Task<bool> AssignDecoratorAsync(int bookingId, int decoratorUserId) => Task.FromResult(true);
            public Task<bool> CancelOrRemoveEventFromBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<List<User>> GetDecoratorsAsync() => Task.FromResult(new List<User>());
            public Task<List<string>> GetAllDistinctPropertyLocationsAsync() => Task.FromResult(CatalogLocations);
            public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>(ServiceAreasInDb);
            public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas)
            {
                ServiceAreasInDb = serviceAreas;
                return Task.FromResult(true);
            }
            public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation)
            {
                if (string.IsNullOrWhiteSpace(propertyLocation) || string.IsNullOrWhiteSpace(ServiceAreasInDb))
                    return Task.FromResult(false);
                var areas = ServiceAreasInDb.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                bool matches = areas.Any(a => propertyLocation.Contains(a, StringComparison.OrdinalIgnoreCase) || a.Contains(propertyLocation, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(matches);
            }
        }

        private static EventManagerController CreateController(FakeEventPackageRepo eventRepo, FakeConfigRepo configRepo, int userId = 77, string role = "EventManager")
        {
            var controller = new EventManagerController(
                eventRepo,
                null!,
                configRepo,
                NullLogger<EventManagerController>.Instance
            );

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim(ClaimTypes.Name, "Decor Studio User")
            }, "TestAuth"));

            var httpContext = new DefaultHttpContext { User = user };
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
            return controller;
        }

        [Fact]
        public async Task Step4_Check1_SelectThreeLocations_SaveAndReload_PersistsSameThree()
        {
            // 1. Setup controller and repo
            var eventRepo = new FakeEventPackageRepo();
            var configRepo = new FakeConfigRepo();
            var controller = CreateController(eventRepo, configRepo, 77);

            // Select three valid "City, State" locations
            string[] threeLocations = new[] { "Pune, Maharashtra", "Alibaug, Maharashtra", "Nashik, Maharashtra" };

            // Press Save Service Areas (submits ServiceAreas form control)
            var saveResult = await controller.UpdateServiceAreas(serviceAreas: threeLocations, returnUrl: null);
            var redirect = Assert.IsType<RedirectToActionResult>(saveResult);
            Assert.Equal("ServiceAreas", redirect.ActionName);

            // Verify database has them normalized and separated by semicolon
            Assert.NotNull(eventRepo.ServiceAreasInDb);
            Assert.Contains("Pune, Maharashtra", eventRepo.ServiceAreasInDb);
            Assert.Contains("Alibaug, Maharashtra", eventRepo.ServiceAreasInDb);
            Assert.Contains("Nashik, Maharashtra", eventRepo.ServiceAreasInDb);

            // Reload page (GET /EventManager/ServiceAreas)
            var reloadResult = await controller.ServiceAreas();
            var viewResult = Assert.IsType<ViewResult>(reloadResult);
            var vm = Assert.IsType<EventManagerServiceAreasViewModel>(viewResult.Model);

            // Verify the same three are in SelectedAreas from the database
            Assert.Equal(3, vm.SelectedAreas.Count);
            Assert.Contains("Pune, Maharashtra", vm.SelectedAreas);
            Assert.Contains("Alibaug, Maharashtra", vm.SelectedAreas);
            Assert.Contains("Nashik, Maharashtra", vm.SelectedAreas);
        }

        [Fact]
        public async Task Step4_Check2_SelectAll_SaveAndReload_AllLocationsChecked()
        {
            var eventRepo = new FakeEventPackageRepo();
            var configRepo = new FakeConfigRepo();
            var controller = CreateController(eventRepo, configRepo, 77);

            // "Select All" selects all catalog locations
            string[] allLocations = eventRepo.CatalogLocations.ToArray();

            // Press Save
            var saveResult = await controller.UpdateServiceAreas(serviceAreas: allLocations, returnUrl: null);
            Assert.IsType<RedirectToActionResult>(saveResult);

            // Reload page
            var reloadResult = await controller.ServiceAreas();
            var viewResult = Assert.IsType<ViewResult>(reloadResult);
            var vm = Assert.IsType<EventManagerServiceAreasViewModel>(viewResult.Model);

            // All locations must be checked after reload
            Assert.Equal(allLocations.Length, vm.SelectedAreas.Count);
            foreach (var loc in allLocations)
            {
                Assert.Contains(loc, vm.SelectedAreas);
            }
        }

        [Fact]
        public async Task Step4_Check3_DeselectAll_SaveAndReload_NoAreasChecked()
        {
            var eventRepo = new FakeEventPackageRepo { ServiceAreasInDb = "Pune, Maharashtra; Lonavala, Maharashtra" };
            var configRepo = new FakeConfigRepo();
            var controller = CreateController(eventRepo, configRepo, 77);

            // Deselect all (empty selection) and press Save
            var saveResult = await controller.UpdateServiceAreas(serviceAreas: Array.Empty<string>(), returnUrl: null);
            Assert.IsType<RedirectToActionResult>(saveResult);

            // Verify DB is cleared
            Assert.Equal(string.Empty, eventRepo.ServiceAreasInDb);

            // Reload page
            var reloadResult = await controller.ServiceAreas();
            var viewResult = Assert.IsType<ViewResult>(reloadResult);
            var vm = Assert.IsType<EventManagerServiceAreasViewModel>(viewResult.Model);

            // No areas checked after reload
            Assert.Empty(vm.SelectedAreas);
        }

        [Fact]
        public async Task Step4_Check4_SubmitUnnormalizableArea_NothingSaved_ErrorShown_SelectionKept()
        {
            string originalDbState = "Pune, Maharashtra; Lonavala, Maharashtra";
            var eventRepo = new FakeEventPackageRepo { ServiceAreasInDb = originalDbState };
            var configRepo = new FakeConfigRepo();
            var controller = CreateController(eventRepo, configRepo, 77);

            // Submit selection with an un-normalizable area (missing State)
            string[] submittedWithInvalid = new[] { "Pune, Maharashtra", "InvalidLocationNoState" };

            var saveResult = await controller.UpdateServiceAreas(serviceAreas: submittedWithInvalid, returnUrl: null);

            // Must NOT redirect; must re-render the view with error
            var viewResult = Assert.IsType<ViewResult>(saveResult);
            var vm = Assert.IsType<EventManagerServiceAreasViewModel>(viewResult.Model);

            // 1. Nothing is saved to the database
            Assert.Equal(originalDbState, eventRepo.ServiceAreasInDb);

            // 2. Error is shown / recorded in InvalidAreas and ModelState
            Assert.NotEmpty(vm.InvalidAreas);
            Assert.Contains("InvalidLocationNoState", vm.InvalidAreas);
            Assert.True(controller.ModelState.ContainsKey("ServiceAreas"));

            // 3. User's selection is kept checked
            Assert.Contains("Pune, Maharashtra", vm.SelectedAreas);
            Assert.Contains("InvalidLocationNoState", vm.SelectedAreas);
        }

        [Fact]
        public async Task Step4_Check5_EligibilityEndpoint_ReflectsNewAreasImmediately()
        {
            var eventRepo = new FakeEventPackageRepo();
            var configRepo = new FakeConfigRepo();
            var controller = CreateController(eventRepo, configRepo, 77);

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var fakePropRepo = new FakePropertyRepo();
            var fakeUserRepo = new FakeUserRepo();

            // Decorator with verified status and active package
            var manager = new User
            {
                UserID = 77,
                FullName = "Verified Decorator",
                Role = "EventManager",
                VerificationStatus = "Verified",
                IsActive = true,
                ServiceAreas = "Pune, Maharashtra"
            };
            fakeUserRepo.Users.Add(manager);

            var package = new EventPackage
            {
                PackageID = 10,
                CreatedByUserID = 77,
                IsActive = true,
                Name = "Celebration Setup",
                BasePrice = 3000
            };
            var portfolio = new EventManagerPortfolio
            {
                PortfolioID = 1,
                UserID = 77,
                IsApproved = true
            };

            var propInGoa = new Property
            {
                PropertyID = 101,
                Title = "Goa Beachfront Villa",
                Location = "Panaji, Goa",
                City = "Panaji",
                State = "Goa",
                ListingStatus = "Approved"
            };
            fakePropRepo.Properties.Add(propInGoa);

            var mockEventRepoForEligibility = new FakeEventRepoWithPackages
            {
                Decorators = new List<User> { manager },
                Packages = new List<EventPackage> { package },
                Portfolios = new List<EventManagerPortfolio> { portfolio }
            };

            var eligibilityService = new StayCelebrationEligibilityService(
                fakePropRepo,
                mockEventRepoForEligibility,
                fakeUserRepo,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                memoryCache,
                null
            );

            var checkIn = DateTime.UtcNow.AddDays(5);

            // Before update: manager only services "Pune, Maharashtra", property is in "Panaji, Goa"
            var beforeResult = await eligibilityService.CheckEligibilityAsync(101, checkIn);
            Assert.False(beforeResult.Available, "Must not be available in Panaji, Goa before manager services Panaji, Goa");
            Assert.Equal("no_area_coverage", beforeResult.ReasonCode);

            // Update service areas to include "Panaji, Goa"
            string[] newAreas = new[] { "Pune, Maharashtra", "Panaji, Goa" };
            await controller.UpdateServiceAreas(serviceAreas: newAreas, returnUrl: null);

            // Update manager's ServiceAreas in repo to reflect DB change
            manager.ServiceAreas = "Pune, Maharashtra; Panaji, Goa";

            // Next request: cache was invalidated by UpdateServiceAreas, reflects new areas!
            var afterResult = await eligibilityService.CheckEligibilityAsync(101, checkIn);
            Assert.True(afterResult.Available, "Must become available in Panaji, Goa immediately after service areas update");
            Assert.Equal("ok", afterResult.ReasonCode);
        }

        [Fact]
        public async Task Step4_Check7_Authorization_ClientOrOtherManager_ReturnsForbid()
        {
            var eventRepo = new FakeEventPackageRepo();
            var configRepo = new FakeConfigRepo();

            // Client role user attempting to update service areas
            var clientController = CreateController(eventRepo, configRepo, userId: 99, role: "Client");
            var clientResult = await clientController.UpdateServiceAreas(new[] { "Pune, Maharashtra" }, null);
            Assert.IsType<ForbidResult>(clientResult);

            // EventManager 88 attempting to update EventManager 77's profile
            var otherManagerController = CreateController(eventRepo, configRepo, userId: 88, role: "EventManager");
            var forbidResult = await otherManagerController.UpdateServiceAreas(
                new[] { "Pune, Maharashtra" },
                null,
                managerId: 77 // trying to edit user 77
            );
            Assert.IsType<ForbidResult>(forbidResult);
        }

        private class FakePropertyRepo : IPropertyRepository
        {
            public List<Property> Properties = new();
            public List<Occasion> Occasions = new();
            public Dictionary<int, List<int>> PropertyOccasions = new();

            public Task<Property?> GetByIdAsync(int propertyId) => Task.FromResult(Properties.FirstOrDefault(p => p.PropertyID == propertyId));
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(Occasions);
            public Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId)
            {
                if (PropertyOccasions.TryGetValue(propertyId, out var list))
                    return Task.FromResult(list);
                return Task.FromResult(new List<int>());
            }
            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(Properties);
            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(Properties.Where(p => p.OwnerID == ownerId).ToList());
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

        private class FakeUserRepo : IUserRepository
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

        private class FakeEventRepoWithPackages : IEventPackageRepository
        {
            public List<User> Decorators = new();
            public List<EventPackage> Packages = new();
            public List<EventManagerPortfolio> Portfolios = new();

            public Task<List<User>> GetDecoratorsAsync(bool onlyVerified = false)
            {
                var list = Decorators.AsEnumerable();
                if (onlyVerified) list = list.Where(d => d.VerificationStatus == "Verified");
                return Task.FromResult(list.ToList());
            }

            public Task<List<EventPackage>> GetPackagesByUserIdAsync(int userId) => Task.FromResult(Packages.Where(p => p.CreatedByUserID == userId).ToList());
            public Task<EventPackage?> GetPackageByIdAsync(int packageId) => Task.FromResult(Packages.FirstOrDefault(p => p.PackageID == packageId));
            public Task<List<EventManagerPortfolio>> GetPortfoliosByUserIdAsync(int userId, bool onlyApproved = true)
            {
                var list = Portfolios.Where(p => p.UserID == userId);
                if (onlyApproved) list = list.Where(p => p.IsApproved);
                return Task.FromResult(list.ToList());
            }

            public Task<HashSet<int>> GetUserIdsWithApprovedPortfoliosAsync()
            {
                if (Portfolios.Any())
                {
                    return Task.FromResult(Portfolios.Where(p => p.IsApproved).Select(p => p.UserID).ToHashSet());
                }
                return Task.FromResult(Decorators.Select(d => d.UserID).ToHashSet());
            }

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
            public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>(Decorators.FirstOrDefault(d => d.UserID == eventManagerUserId)?.ServiceAreas);
            public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas) => Task.FromResult(true);
            public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(true);
        }
    }
}
