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
    public class OwnerCategoryAssignmentTests
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
            public readonly List<PropertyType> Types = new()
            {
                new PropertyType { PropertyTypeID = 1, TypeName = "Villa" },
                new PropertyType { PropertyTypeID = 2, TypeName = "Resort" },
                new PropertyType { PropertyTypeID = 3, TypeName = "Farmhouse" }
            };
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
                var pt = Types.FirstOrDefault(t => t.PropertyTypeID == property.PropertyTypeID);
                property.PropertyTypeName = pt?.TypeName;
                Properties[property.PropertyID] = property;
                return Task.FromResult(property.PropertyID);
            }

            public Task<bool> UpdatePropertyAsync(Property property)
            {
                var pt = Types.FirstOrDefault(t => t.PropertyTypeID == property.PropertyTypeID);
                property.PropertyTypeName = pt?.TypeName;
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

            public Task<List<PropertyType>> GetPropertyTypesAsync() => Task.FromResult(Types.ToList());

            public Task<int> GetOrCreatePropertyTypeByNameAsync(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return Task.FromResult(0);
                string trimmed = name.Trim();
                var existing = Types.FirstOrDefault(t => t.TypeName.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    return Task.FromResult(existing.PropertyTypeID);
                }
                int newId = Types.Count > 0 ? Types.Max(t => t.PropertyTypeID) + 1 : 1;
                var created = new PropertyType { PropertyTypeID = newId, TypeName = trimmed };
                Types.Add(created);
                InvokedActions.Add($"CreatePropertyType_{newId}_{trimmed}");
                return Task.FromResult(newId);
            }

            public Task<bool> UpdatePropertyCategoryAsync(int propertyId, int ownerId, int propertyTypeId)
            {
                InvokedActions.Add($"UpdateCategory_{propertyId}_{propertyTypeId}");
                if (Properties.TryGetValue(propertyId, out var prop) && prop.OwnerID == ownerId)
                {
                    prop.PropertyTypeID = propertyTypeId;
                    var pt = Types.FirstOrDefault(t => t.PropertyTypeID == propertyTypeId);
                    prop.PropertyTypeName = pt?.TypeName;
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

        public async Task CreateProperty_WithCustomCategory_CreatesTypeAndAssignsPropertyTypeID()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            var controller = CreateOwnerController(propRepo, configRepo, 5);

            var vm = new PropertyEditViewModel
            {
                Title = "Cloud Top Treehouse",
                Location = "Wayanad Forest",
                FormattedAddress = "Treehouse 7, Cloud Valley",
                PricePerNight = 9500,
                MaxGuests = 4,
                PropertyTypeID = 1, // Default Villa
                CustomCategory = "Treehouse Retreat" // Owner specifies custom category
            };

            var result = await controller.CreateProperty(vm);
            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "Index")
            {
                throw new Exception("Expected redirect to Index after property creation.");
            }

            var createdProp = propRepo.Properties.Values.FirstOrDefault(p => p.Title == "Cloud Top Treehouse");
            if (createdProp == null)
            {
                throw new Exception("Created property was not persisted to repository.");
            }

            // Check if Treehouse Retreat was created as a new PropertyType
            var newType = propRepo.Types.FirstOrDefault(t => t.TypeName == "Treehouse Retreat");
            if (newType == null)
            {
                throw new Exception("Expected 'Treehouse Retreat' to be added to PropertyTypes.");
            }

            if (createdProp.PropertyTypeID != newType.PropertyTypeID)
            {
                throw new Exception($"Expected PropertyTypeID to be {newType.PropertyTypeID}, but got {createdProp.PropertyTypeID}");
            }
        }

        public async Task EditProperty_UpdatingCategory_UpdatesPropertyTypeID()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[20] = new Property
            {
                PropertyID = 20,
                OwnerID = 5,
                Title = "Lakeside Haven",
                Location = "Udaipur",
                PricePerNight = 14000,
                MaxGuests = 8,
                PropertyTypeID = 1, // Villa
                PropertyTypeName = "Villa"
            };

            var controller = CreateOwnerController(propRepo, configRepo, 5);

            // 1. Host updates category to Resort (ID 2)
            var vm = new PropertyEditViewModel
            {
                PropertyID = 20,
                OwnerID = 5,
                Title = "Lakeside Haven",
                Location = "Udaipur",
                PricePerNight = 14000,
                MaxGuests = 8,
                PropertyTypeID = 2 // Resort
            };

            var postResult = await controller.EditProperty(vm);
            var redirect = postResult as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "Index")
            {
                throw new Exception("Expected redirect to Index after EditProperty.");
            }

            var updatedProp = propRepo.Properties[20];
            if (updatedProp.PropertyTypeID != 2)
            {
                throw new Exception($"Expected PropertyTypeID 2 (Resort), but got {updatedProp.PropertyTypeID}");
            }

            // 2. Host edits property again with a new custom category
            vm.CustomCategory = "Eco Glamping Dome";
            await controller.EditProperty(vm);

            var glampingType = propRepo.Types.FirstOrDefault(t => t.TypeName == "Eco Glamping Dome");
            if (glampingType == null)
            {
                throw new Exception("Expected 'Eco Glamping Dome' to be created as a new PropertyType.");
            }

            if (updatedProp.PropertyTypeID != glampingType.PropertyTypeID)
            {
                throw new Exception($"Expected PropertyTypeID {glampingType.PropertyTypeID}, but got {updatedProp.PropertyTypeID}");
            }
        }

        public async Task AssignCategory_ExistingCategory_UpdatesPropertyAndLogsAudit()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[30] = new Property
            {
                PropertyID = 30,
                OwnerID = 5,
                Title = "Serene Orchard Farm",
                Location = "Nashik",
                PropertyTypeID = 1, // Villa
                PropertyTypeName = "Villa"
            };

            var controller = CreateOwnerController(propRepo, configRepo, 5);

            // Host assigns Farmhouse (ID 3)
            var result = await controller.AssignCategory(propertyId: 30, propertyTypeId: 3, customCategory: null);
            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "Index")
            {
                throw new Exception("Expected redirect to Index after AssignCategory.");
            }

            var prop = propRepo.Properties[30];
            if (prop.PropertyTypeID != 3)
            {
                throw new Exception($"Expected PropertyTypeID 3 (Farmhouse), but got {prop.PropertyTypeID}");
            }

            // Verify audit log
            var audit = configRepo.Audits.FirstOrDefault(a => a.Contains("AssignPropertyCategory") && a.Contains("30"));
            if (audit == null)
            {
                throw new Exception("Expected audit log for AssignPropertyCategory on property 30.");
            }

            if (!controller.TempData.ContainsKey("Success"))
            {
                throw new Exception("Expected TempData['Success'] to be populated.");
            }
        }

        public async Task AssignCategory_CustomCategory_CreatesNewTypeAndAssigns()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[40] = new Property
            {
                PropertyID = 40,
                OwnerID = 5,
                Title = "Heritage Haveli Stay",
                Location = "Jaipur",
                PropertyTypeID = 1,
                PropertyTypeName = "Villa"
            };

            var controller = CreateOwnerController(propRepo, configRepo, 5);

            // Host enters custom category: "Heritage Haveli"
            var result = await controller.AssignCategory(propertyId: 40, propertyTypeId: null, customCategory: "Heritage Haveli");
            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "Index")
            {
                throw new Exception("Expected redirect to Index after AssignCategory with custom category.");
            }

            var haveliType = propRepo.Types.FirstOrDefault(t => t.TypeName == "Heritage Haveli");
            if (haveliType == null)
            {
                throw new Exception("Expected 'Heritage Haveli' to be created in PropertyTypes.");
            }

            var prop = propRepo.Properties[40];
            if (prop.PropertyTypeID != haveliType.PropertyTypeID)
            {
                throw new Exception($"Expected PropertyTypeID {haveliType.PropertyTypeID}, but got {prop.PropertyTypeID}");
            }

            // Verify audit log
            var audit = configRepo.Audits.FirstOrDefault(a => a.Contains("AssignPropertyCategory") && a.Contains("Heritage Haveli"));
            if (audit == null)
            {
                throw new Exception("Expected audit log recording category assignment of 'Heritage Haveli'.");
            }
        }

        public async Task AssignCategory_BelongingToAnotherOwner_ThrowsForbiddenException()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[50] = new Property
            {
                PropertyID = 50,
                OwnerID = 99, // Owned by Host 99
                Title = "Another Host's Mansion",
                Location = "Goa",
                PropertyTypeID = 1,
                PropertyTypeName = "Villa"
            };

            // Logged in as Host 5
            var controller = CreateOwnerController(propRepo, configRepo, 5);

            var result = await controller.AssignCategory(propertyId: 50, propertyTypeId: 2, customCategory: null);
            if (result is not ForbidResult)
            {
                throw new Exception($"Expected ForbidResult when Host 5 attempts to alter Host 99's property category, but got: {result?.GetType().Name}");
            }

            // Verify property category was untouched
            var prop = propRepo.Properties[50];
            if (prop.PropertyTypeID != 1)
            {
                throw new Exception("Property category was modified despite unauthorized attempt.");
            }

            // Verify no audit log was created
            if (configRepo.Audits.Any(a => a.Contains("AssignPropertyCategory") && a.Contains("50")))
            {
                throw new Exception("Unauthorized audit log was improperly written.");
            }
        }

        public void OwnerViews_CreateEditAndIndex_ContainCategoryControlsAndModals()
        {
            string createViewPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Owner", "CreateProperty.cshtml");
            if (!File.Exists(createViewPath))
            {
                createViewPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Views", "Owner", "CreateProperty.cshtml");
            }

            string editViewPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Owner", "EditProperty.cshtml");
            if (!File.Exists(editViewPath))
            {
                editViewPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Views", "Owner", "EditProperty.cshtml");
            }

            string indexViewPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Owner", "Index.cshtml");
            if (!File.Exists(indexViewPath))
            {
                indexViewPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Views", "Owner", "Index.cshtml");
            }

            string createContent = File.ReadAllText(createViewPath);
            if (!createContent.Contains("PropertyTypeID") || !createContent.Contains("CustomCategory"))
            {
                throw new Exception("CreateProperty.cshtml must contain PropertyTypeID dropdown and CustomCategory input.");
            }

            string editContent = File.ReadAllText(editViewPath);
            if (!editContent.Contains("PropertyTypeID") || !editContent.Contains("CustomCategory"))
            {
                throw new Exception("EditProperty.cshtml must contain PropertyTypeID dropdown and CustomCategory input.");
            }

            string indexContent = File.ReadAllText(indexViewPath);
            if (!indexContent.Contains("modalAssignCategory_"))
            {
                throw new Exception("Index.cshtml must declare modalAssignCategory_ modal for owner quick category assignment.");
            }

            if (!indexContent.Contains("AssignCategory"))
            {
                throw new Exception("Index.cshtml must submit category assignments to AssignCategory action.");
            }

            if (!indexContent.Contains("PropertyTypeName"))
            {
                throw new Exception("Index.cshtml must display PropertyTypeName badge on listing cards.");
            }
        }
    }
}
