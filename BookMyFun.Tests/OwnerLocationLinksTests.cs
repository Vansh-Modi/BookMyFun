using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Tests
{
    public class OwnerLocationLinksTests
    {
        private class MockLogger<T> : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }

        private class MockPropertyRepo : IPropertyRepository
        {
            public readonly Dictionary<int, Property> Properties = new();
            public readonly List<string> InvokedActions = new();

            public Task<Property?> GetByIdAsync(int propertyId)
            {
                Properties.TryGetValue(propertyId, out var prop);
                return Task.FromResult(prop);
            }

            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) =>
                Task.FromResult(onlyApproved ? Properties.Values.Where(p => p.IsApproved && p.ListingStatus == "Active").ToList() : Properties.Values.ToList());

            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) =>
                Task.FromResult(Properties.Values.Where(p => p.OwnerID == ownerId).ToList());

            public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests) =>
                Task.FromResult(new List<Property>());

            public Task<int> CreatePropertyAsync(Property property)
            {
                property.PropertyID = Properties.Count + 1;
                Properties[property.PropertyID] = property;
                return Task.FromResult(property.PropertyID);
            }

            public Task<bool> UpdatePropertyAsync(Property property)
            {
                Properties[property.PropertyID] = property;
                return Task.FromResult(true);
            }

            public Task<bool> UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl)
            {
                InvokedActions.Add($"UpdateMapUrl_{propertyId}_{mapUrl}");
                if (Properties.TryGetValue(propertyId, out var prop) && prop.OwnerID == ownerId)
                {
                    prop.MapUrl = mapUrl;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }

            public Task<bool> DeletePropertyAsync(int propertyId) => PermanentlyDeletePropertyAsync(propertyId);

            public Task<bool> PermanentlyDeletePropertyAsync(int propertyId)
            {
                InvokedActions.Add($"Delete_{propertyId}");
                return Task.FromResult(Properties.Remove(propertyId));
            }

            public Task<bool> DeactivatePropertyAsync(int propertyId, bool deactivate = true)
            {
                if (Properties.TryGetValue(propertyId, out var prop))
                {
                    prop.ListingStatus = deactivate ? "Deactivated" : "Active";
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }

            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null) => Task.FromResult(true);
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
            public Task<bool> UpdatePropertyCategoryAsync(int propertyId, int ownerId, int propertyTypeId)
            {
                InvokedActions.Add($"UpdateCategory_{propertyId}_{propertyTypeId}");
                if (Properties.TryGetValue(propertyId, out var prop) && prop.OwnerID == ownerId)
                {
                    prop.PropertyTypeID = propertyTypeId;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(new List<Occasion>());
            public Task<int> GetOrCreateAmenityByNameAsync(string name) => Task.FromResult(1);
            public Task<int> GetOrCreateOccasionByNameAsync(string name) => Task.FromResult(1);
            public Task<List<PropertyEventCommission>> GetPropertyEventCommissionsAsync(int propertyId) => Task.FromResult(new List<PropertyEventCommission>());
            public Task<decimal?> GetPropertyEventCommissionRateAsync(int propertyId, int packageId) => Task.FromResult<decimal?>(null);
            public Task<bool> SetPropertyEventCommissionsAsync(int propertyId, List<(int packageId, decimal commissionRate)> commissions) => Task.FromResult(true);
        }

        private class MockConfigRepo : IConfigRepository
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

        private class MockSubRepo : ISubscriptionRepository
        {
            public Task<bool> CanOwnerAddPropertyAsync(int ownerId) => Task.FromResult(true);
            public Task<List<SubscriptionPlan>> GetAllPlansAsync(bool onlyActive = true) => Task.FromResult(new List<SubscriptionPlan>());
            public Task<SubscriptionPlan?> GetPlanByIdAsync(int planId) => Task.FromResult<SubscriptionPlan?>(null);
            public Task<int> CreatePlanAsync(SubscriptionPlan plan) => Task.FromResult(1);
            public Task<bool> UpdatePlanAsync(SubscriptionPlan plan) => Task.FromResult(true);
            public Task<int> SubscribeOwnerAsync(OwnerSubscription sub) => Task.FromResult(1);
            public Task<OwnerSubscription?> GetActiveSubscriptionByOwnerIdAsync(int ownerId) => Task.FromResult<OwnerSubscription?>(null);
            public Task<List<OwnerSubscription>> GetSubscriptionHistoryByOwnerIdAsync(int ownerId) => Task.FromResult(new List<OwnerSubscription>());
            public Task<OwnerSubscription?> GetSubscriptionByIdAsync(int subscriptionId) => Task.FromResult<OwnerSubscription?>(null);
            public Task<bool> UpdateSubscriptionPaymentStatusAsync(int subscriptionId, string paymentStatus, string? gatewayTransactionId) => Task.FromResult(true);
            public Task<int> GetActiveListingCountAsync(int ownerId) => Task.FromResult(0);
        }

        private class SimpleTempDataProvider : ITempDataProvider
        {
            private readonly Dictionary<string, object> _data = new();
            public IDictionary<string, object> LoadTempData(HttpContext context) => _data;
            public void SaveTempData(HttpContext context, IDictionary<string, object> values)
            {
                foreach (var kvp in values) _data[kvp.Key] = kvp.Value;
            }
        }

        private static OwnerController CreateOwnerController(MockPropertyRepo propRepo, MockConfigRepo configRepo, int ownerUserId = 5)
        {
            var controller = new OwnerController(
                propRepo,
                null!, // userRepo
                null!, // bookingRepo
                new MockSubRepo(),
                null!, // paymentService
                null!, // damageClaimService
                configRepo,
                new MockLogger<OwnerController>()
            );

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, ownerUserId.ToString()),
                new Claim(ClaimTypes.Role, "Owner"),
                new Claim(ClaimTypes.Name, "Farm Owner")
            }, "TestAuth"));

            var httpContext = new DefaultHttpContext { User = user };
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData()
            };
            controller.TempData = new TempDataDictionary(httpContext, new SimpleTempDataProvider());
            return controller;
        }

        public async Task CreateProperty_WithMapUrl_PersistsMapUrl()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            var controller = CreateOwnerController(propRepo, configRepo, 5);

            var vm = new PropertyEditViewModel
            {
                Title = "Sunset Valley Villa",
                Location = "Karjat Hills",
                FormattedAddress = "Survey 42, Green Valley Rd",
                Latitude = 18.9102m,
                Longitude = 73.3283m,
                MapUrl = "https://maps.app.goo.gl/xyz123456",
                PricePerNight = 12000,
                MaxGuests = 10,
                PropertyTypeID = 1
            };

            var result = await controller.CreateProperty(vm);
            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "Index")
            {
                throw new Exception("Expected redirect to Index after property creation.");
            }

            var createdProp = propRepo.Properties.Values.FirstOrDefault(p => p.Title == "Sunset Valley Villa");
            if (createdProp == null)
            {
                throw new Exception("Created property was not persisted to repository.");
            }

            if (createdProp.MapUrl != "https://maps.app.goo.gl/xyz123456")
            {
                throw new Exception($"Expected MapUrl 'https://maps.app.goo.gl/xyz123456', but got: '{createdProp.MapUrl}'");
            }
        }

        public async Task EditProperty_UpdatingMapUrl_UpdatesAndPreservesMapUrl()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[10] = new Property
            {
                PropertyID = 10,
                OwnerID = 5,
                Title = "Original Orchard Villa",
                Location = "Nashik",
                PricePerNight = 15000,
                MaxGuests = 12,
                MapUrl = "https://maps.app.goo.gl/originalMapLink"
            };

            var controller = CreateOwnerController(propRepo, configRepo, 5);

            // 1. GET EditProperty loads existing MapUrl
            var getResult = await controller.EditProperty(10) as ViewResult;
            if (getResult == null || !(getResult.Model is PropertyEditViewModel vm))
            {
                throw new Exception("Expected ViewResult with PropertyEditViewModel.");
            }

            if (vm.MapUrl != "https://maps.app.goo.gl/originalMapLink")
            {
                throw new Exception($"Expected GET EditProperty to load MapUrl 'https://maps.app.goo.gl/originalMapLink', but got: '{vm.MapUrl}'");
            }

            // 2. POST EditProperty updates MapUrl
            vm.MapUrl = "https://goo.gl/maps/updatedNewMapLink99";
            var postResult = await controller.EditProperty(vm);
            var redirect = postResult as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "Index")
            {
                throw new Exception("Expected redirect to Index after EditProperty POST.");
            }

            if (propRepo.Properties[10].MapUrl != "https://goo.gl/maps/updatedNewMapLink99")
            {
                throw new Exception($"Expected updated MapUrl 'https://goo.gl/maps/updatedNewMapLink99', but got: '{propRepo.Properties[10].MapUrl}'");
            }
        }

        public async Task UpdateLocationUrl_ValidGoogleMapsUrl_UpdatesMapUrlAndLogsAudit()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[20] = new Property
            {
                PropertyID = 20,
                OwnerID = 5,
                Title = "Lakeside Haven",
                Location = "Pawna Lake",
                MapUrl = null
            };

            var controller = CreateOwnerController(propRepo, configRepo, 5);

            var result = await controller.UpdateLocationUrl(20, "https://maps.app.goo.gl/lakesideHavenPin", "/Owner/Index");
            var redirect = result as RedirectResult;
            if (redirect == null || redirect.Url != "/Owner/Index")
            {
                throw new Exception("Expected redirect to returnUrl '/Owner/Index'.");
            }

            if (propRepo.Properties[20].MapUrl != "https://maps.app.goo.gl/lakesideHavenPin")
            {
                throw new Exception($"Expected property MapUrl to be updated to 'https://maps.app.goo.gl/lakesideHavenPin', but got: '{propRepo.Properties[20].MapUrl}'");
            }

            if (!configRepo.Audits.Any(a => a.Contains("UpdatePropertyLocationUrl") && a.Contains(":20:") && a.Contains("lakesideHavenPin")))
            {
                throw new Exception("Audit log for UpdatePropertyLocationUrl was not recorded.");
            }

            if (!controller.TempData.ContainsKey("Success"))
            {
                throw new Exception("Expected TempData['Success'] to be populated.");
            }
        }

        public async Task UpdateLocationUrl_InvalidUrlScheme_RejectsAndSetsTempDataError()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[25] = new Property
            {
                PropertyID = 25,
                OwnerID = 5,
                Title = "Hilltop Fortress",
                Location = "Mahabaleshwar",
                MapUrl = "https://maps.app.goo.gl/originalPin"
            };

            var controller = CreateOwnerController(propRepo, configRepo, 5);

            // Malformed / script pseudo-protocol
            var result = await controller.UpdateLocationUrl(25, "javascript:alert('xss')", "/Owner/Index");
            var redirect = result as RedirectResult;
            if (redirect == null || redirect.Url != "/Owner/Index")
            {
                throw new Exception("Expected redirect to returnUrl '/Owner/Index'.");
            }

            // Property MapUrl must remain unchanged
            if (propRepo.Properties[25].MapUrl != "https://maps.app.goo.gl/originalPin")
            {
                throw new Exception("Expected property MapUrl to remain unchanged when given invalid URL scheme.");
            }

            if (!controller.TempData.ContainsKey("Error"))
            {
                throw new Exception("Expected TempData['Error'] to be set on invalid URL scheme.");
            }
        }

        public async Task UpdateLocationUrl_BelongingToAnotherOwner_ThrowsForbiddenException()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            // Property owned by Owner 99
            propRepo.Properties[30] = new Property
            {
                PropertyID = 30,
                OwnerID = 99,
                Title = "Unauthorized Access Villa",
                Location = "Alibaug",
                MapUrl = "https://maps.app.goo.gl/owner99Pin"
            };

            // Controller logged in as Owner 5
            var controller = CreateOwnerController(propRepo, configRepo, 5);

            var result = await controller.UpdateLocationUrl(30, "https://maps.app.goo.gl/attackerPin", "/Owner/Index");
            if (result is not ForbidResult)
            {
                throw new Exception("Expected ForbidResult when owner attempts IDOR update on another owner's property.");
            }

            if (propRepo.Properties[30].MapUrl != "https://maps.app.goo.gl/owner99Pin")
            {
                throw new Exception("Property MapUrl was modified despite unauthorized attempt.");
            }
        }

        public void OwnerViews_CreateEditIndexAndDetails_ContainMapUrlControlsAndDirectLinks()
        {
            string createViewPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Owner", "CreateProperty.cshtml");
            string editViewPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Owner", "EditProperty.cshtml");
            string indexViewPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Owner", "Index.cshtml");
            string detailsViewPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Properties", "Details.cshtml");

            string createContent = File.ReadAllText(createViewPath);
            string editContent = File.ReadAllText(editViewPath);
            string indexContent = File.ReadAllText(indexViewPath);
            string detailsContent = File.ReadAllText(detailsViewPath);

            // Assert CreateProperty.cshtml
            if (!createContent.Contains("asp-for=\"MapUrl\"") || !createContent.Contains("id=\"mapUrlInput\"") || !createContent.Contains("id=\"btnTestMapUrl\""))
            {
                throw new Exception("Views/Owner/CreateProperty.cshtml does not declare MapUrl input or test button.");
            }

            // Assert EditProperty.cshtml
            if (!editContent.Contains("asp-for=\"MapUrl\"") || !editContent.Contains("id=\"mapUrlInput\"") || !editContent.Contains("id=\"btnTestMapUrl\""))
            {
                throw new Exception("Views/Owner/EditProperty.cshtml does not declare MapUrl input or test button.");
            }

            // Assert Index.cshtml
            if (!indexContent.Contains("modalUpdateMap_") || !indexContent.Contains("asp-action=\"UpdateLocationUrl\"") || !indexContent.Contains("Map Link"))
            {
                throw new Exception("Views/Owner/Index.cshtml does not contain modalUpdateMap modal or UpdateLocationUrl action.");
            }

            // Assert Details.cshtml
            if (!detailsContent.Contains("hasDirectMapUrl") || !detailsContent.Contains("Model.MapUrl") || !detailsContent.Contains("Open Verified Location Link"))
            {
                throw new Exception("Views/Properties/Details.cshtml does not link directly to Model.MapUrl when available.");
            }
        }
    }
}
