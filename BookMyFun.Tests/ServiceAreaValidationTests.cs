using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class ServiceAreaValidationTests
    {
        [Fact]
        public void Normalizer_AcceptsPuneMaharashtra()
        {
            bool ok = EventManagerController.TryNormalizeArea("Pune, Maharashtra", out var normalized);
            Assert.True(ok);
            Assert.Equal("Pune, Maharashtra", normalized);
        }

        [Fact]
        public void Normalizer_AcceptsGoa()
        {
            bool ok = EventManagerController.TryNormalizeArea("Goa", out var normalized);
            Assert.True(ok);
            Assert.Equal("Goa", normalized);
        }

        [Fact]
        public void Normalizer_AcceptsUntrimmedGoa_AsTitleCasedGoa()
        {
            bool ok = EventManagerController.TryNormalizeArea("  goa  ", out var normalized);
            Assert.True(ok);
            Assert.Equal("Goa", normalized);
        }

        [Fact]
        public void Normalizer_RejectsConsecutiveCommas()
        {
            bool ok = EventManagerController.TryNormalizeArea("Pune,,", out var normalized);
            Assert.False(ok);
            Assert.Equal(string.Empty, normalized);
        }

        [Fact]
        public void Normalizer_RejectsEmptyText()
        {
            bool ok = EventManagerController.TryNormalizeArea("", out var normalized);
            Assert.False(ok);
            Assert.Equal(string.Empty, normalized);

            bool okWhitespace = EventManagerController.TryNormalizeArea("   ", out var normWs);
            Assert.False(okWhitespace);
            Assert.Equal(string.Empty, normWs);
        }

        [Fact]
        public void Normalizer_RejectsNumbersAndExtraSegments()
        {
            Assert.False(EventManagerController.TryNormalizeArea("123", out _));
            Assert.False(EventManagerController.TryNormalizeArea("Pune, 123", out _));
            Assert.False(EventManagerController.TryNormalizeArea("City, State, Extra", out _));
        }

        private class FakeTempDataProvider : ITempDataProvider
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

        private class FakeEventRepo : IEventPackageRepository
        {
            public string? ServiceAreasInDb { get; set; } = "Initial State";
            public List<string> CatalogLocations { get; set; } = new() { "Pune, Maharashtra", "Panaji, Goa" };

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
            public Task<List<User>> GetDecoratorsAsync(bool onlyVerified = false) => Task.FromResult(new List<User>());
            public Task<List<string>> GetAllDistinctPropertyLocationsAsync() => Task.FromResult(CatalogLocations);
            public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>(ServiceAreasInDb);
            public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas)
            {
                ServiceAreasInDb = serviceAreas;
                return Task.FromResult(true);
            }
            public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(true);
            public Task<HashSet<int>> GetUserIdsWithApprovedPortfoliosAsync() => Task.FromResult(new HashSet<int>());
        }

        private static EventManagerController CreateController(FakeEventRepo eventRepo, FakeConfigRepo configRepo, int userId = 50)
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
                new Claim(ClaimTypes.Role, "EventManager"),
                new Claim(ClaimTypes.Name, "Decor User")
            }, "TestAuth"));

            var httpContext = new DefaultHttpContext { User = user };
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new FakeTempDataProvider());
            return controller;
        }

        [Fact]
        public async Task UpdateServiceAreas_RemovesDuplicates_BeforeSaving()
        {
            var eventRepo = new FakeEventRepo();
            var configRepo = new FakeConfigRepo();
            var controller = CreateController(eventRepo, configRepo, 50);

            string[] inputs = new[] { "Pune, Maharashtra", "pune, maharashtra", "Goa", "  goa  " };
            var result = await controller.UpdateServiceAreas(serviceAreas: inputs);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Pune, Maharashtra; Goa", eventRepo.ServiceAreasInDb);
        }

        [Fact]
        public async Task UpdateServiceAreas_WhenValueRejected_SavesNothing_AndKeepsValidValuesChecked()
        {
            string original = "Pune, Maharashtra";
            var eventRepo = new FakeEventRepo { ServiceAreasInDb = original };
            var configRepo = new FakeConfigRepo();
            var controller = CreateController(eventRepo, configRepo, 50);

            string[] inputs = new[] { "Goa", "Pune,," };
            var result = await controller.UpdateServiceAreas(serviceAreas: inputs);

            var viewResult = Assert.IsType<ViewResult>(result);
            var vm = Assert.IsType<EventManagerServiceAreasViewModel>(viewResult.Model);

            // 1. Saves nothing
            Assert.Equal(original, eventRepo.ServiceAreasInDb);

            // 2. Error lists only rejected values
            Assert.Single(vm.InvalidAreas);
            Assert.Contains("Pune,,", vm.InvalidAreas);
            Assert.DoesNotContain("Goa", vm.InvalidAreas);

            // 3. Valid values stay in SelectedAreas so they remain checked
            Assert.Contains("Goa", vm.SelectedAreas);
        }

        [Fact]
        public async Task StateAloneServiceArea_MatchesOnlyWhenNoCityLevelMatch()
        {
            var fakePropRepo = new FakePropertyRepo();
            var fakeEventRepo = new FakeEventRepoWithPackages();
            var fakeUserRepo = new FakeUserRepo();

            var service = new StayCelebrationEligibilityService(
                fakePropRepo,
                fakeEventRepo,
                fakeUserRepo,
                new ConfigurationBuilder().Build(),
                new MemoryCache(new MemoryCacheOptions()),
                null);

            // Manager 1 has specific city: "Panaji, Goa"
            var mgrCity = new User
            {
                UserID = 1,
                FullName = "Panaji Specialist",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Panaji, Goa"
            };

            // Manager 2 has state alone: "Goa"
            var mgrState = new User
            {
                UserID = 2,
                FullName = "All Goa Decorator",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Goa"
            };

            fakeUserRepo.Users.Add(mgrCity);
            fakeUserRepo.Users.Add(mgrState);
            fakeEventRepo.Decorators.Add(mgrCity);
            fakeEventRepo.Decorators.Add(mgrState);

            fakeEventRepo.Portfolios.Add(new EventManagerPortfolio { PortfolioID = 1, UserID = 1, Title = "P1", IsApproved = true });
            fakeEventRepo.Portfolios.Add(new EventManagerPortfolio { PortfolioID = 2, UserID = 2, Title = "P2", IsApproved = true });

            fakeEventRepo.Packages.Add(new EventPackage { PackageID = 10, CreatedByUserID = 1, Name = "Panaji Pkg", Category = "Birthday", BasePrice = 5000, IsActive = true });
            fakeEventRepo.Packages.Add(new EventPackage { PackageID = 20, CreatedByUserID = 2, Name = "Goa Pkg", Category = "Birthday", BasePrice = 6000, IsActive = true });

            // Case A: Property in Panaji, Goa (has city match)
            var propPanaji = new Property
            {
                PropertyID = 101,
                Title = "Panaji Villa",
                City = "Panaji",
                State = "Goa",
                ListingStatus = "Active"
            };
            fakePropRepo.Properties.Add(propPanaji);

            var checkIn = DateTime.Today.AddDays(5);
            var resultPanaji = await service.CheckEligibilityAsync(101, checkIn);
            Assert.True(resultPanaji.Available);
            // Must contain packages from mgrCity only, NOT mgrState (because Panaji has city-level match)
            Assert.Contains(resultPanaji.Packages, p => p.ManagerId == 1);
            Assert.DoesNotContain(resultPanaji.Packages, p => p.ManagerId == 2);

            // Case B: Property in Margao, Goa (no city-level match for Margao, but in Goa)
            var propMargao = new Property
            {
                PropertyID = 102,
                Title = "Margao Estate",
                City = "Margao",
                State = "Goa",
                ListingStatus = "Active"
            };
            fakePropRepo.Properties.Add(propMargao);

            var resultMargao = await service.CheckEligibilityAsync(102, checkIn);
            Assert.True(resultMargao.Available);
            // Must match mgrState (state-level match since no manager services Margao specifically)
            Assert.Contains(resultMargao.Packages, p => p.ManagerId == 2);
            Assert.DoesNotContain(resultMargao.Packages, p => p.ManagerId == 1);
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
                return Task.FromResult(Portfolios.Where(p => p.IsApproved).Select(p => p.UserID).ToHashSet());
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
