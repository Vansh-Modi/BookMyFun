using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Tests
{
    public class PropertyPermissionsTests
    {
        private class SimpleTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class MockLogger<T> : ILogger<T>
        {
            public readonly List<string> Logs = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Logs.Add($"{logLevel}:{formatter(state, exception)}");
            }
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
                InvokedActions.Add($"Deactivate_{propertyId}_{deactivate}");
                if (Properties.TryGetValue(propertyId, out var prop))
                {
                    prop.ListingStatus = deactivate ? "Deactivated" : "Active";
                    prop.IsApproved = !deactivate;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }

            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null)
            {
                InvokedActions.Add($"Approve_{propertyId}_{isApproved}_{targetStatus}");
                if (Properties.TryGetValue(propertyId, out var prop))
                {
                    prop.IsApproved = isApproved;
                    prop.ListingStatus = !string.IsNullOrEmpty(targetStatus) ? targetStatus : (isApproved ? "Active" : "Rejected");
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
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

        private static AdminController CreateAdminController(MockPropertyRepo propRepo, MockConfigRepo configRepo, int adminUserId = 1)
        {
            var controller = new AdminController(
                propRepo,
                null!, // userRepo
                null!, // bookingRepo
                null!, // subRepo
                configRepo,
                null!, // ownerReqRepo
                null!, // contactRepo
                null!, // siteSettingsService
                null!, // financialRepo
                null!, // env
                null!, // mediaStorage
                null!, // refundEngine
                null!, // partnerLeadService
                null!, // damageClaimService
                null!, // idVerificationService
                null!  // eventPackageRepo
            );

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, adminUserId.ToString()),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Name, "Master Admin")
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

        private static EmployeeController CreateEmployeeController(MockPropertyRepo propRepo, MockConfigRepo configRepo, int empUserId = 2)
        {
            var controller = new EmployeeController(
                propRepo,
                null!, // userRepo
                null!, // bookingRepo
                configRepo,
                null!, // ownerReqRepo
                null!, // contactRepo
                null!, // refundEngine
                null!, // partnerLeadService
                null!, // damageClaimService
                null!, // idVerificationService
                null!, // mediaStorage
                null!, // eventPackageRepo
                null!  // siteSettingsService
            );

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, empUserId.ToString()),
                new Claim(ClaimTypes.Role, "Employee"),
                new Claim(ClaimTypes.Name, "Staff Reviewer")
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

        public async Task AdminController_DeactivateProperty_SetsStatusToDeactivatedAndAudits()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[10] = new Property { PropertyID = 10, Title = "Serene Farm", ListingStatus = "Active", IsApproved = true };

            var controller = CreateAdminController(propRepo, configRepo, 1);

            // Deactivate
            var res1 = await controller.DeactivateProperty(10, true);
            var redirect1 = res1 as RedirectToActionResult;
            if (redirect1 == null || redirect1.ActionName != "ManageProperties")
            {
                throw new Exception("Expected redirect to ManageProperties after deactivating.");
            }

            if (propRepo.Properties[10].ListingStatus != "Deactivated" || propRepo.Properties[10].IsApproved)
            {
                throw new Exception($"Expected property to be Deactivated/Unapproved, got ListingStatus={propRepo.Properties[10].ListingStatus}, IsApproved={propRepo.Properties[10].IsApproved}");
            }

            if (!configRepo.Audits.Any(a => a.Contains("AdminDeactivateProperty") && a.Contains(":10:")))
            {
                throw new Exception("Audit log for AdminDeactivateProperty was not recorded.");
            }

            // Reactivate
            var res2 = await controller.DeactivateProperty(10, false);
            var redirect2 = res2 as RedirectToActionResult;
            if (redirect2 == null || redirect2.ActionName != "ManageProperties")
            {
                throw new Exception("Expected redirect to ManageProperties after reactivating.");
            }

            if (propRepo.Properties[10].ListingStatus != "Active" || !propRepo.Properties[10].IsApproved)
            {
                throw new Exception($"Expected property to be Active/Approved, got ListingStatus={propRepo.Properties[10].ListingStatus}, IsApproved={propRepo.Properties[10].IsApproved}");
            }

            if (!configRepo.Audits.Any(a => a.Contains("AdminActivateProperty") && a.Contains(":10:")))
            {
                throw new Exception("Audit log for AdminActivateProperty was not recorded.");
            }
        }

        public async Task EmployeeController_DeactivateProperty_SetsStatusToDeactivatedAndAudits()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[20] = new Property { PropertyID = 20, Title = "Sunset Villa", ListingStatus = "Active", IsApproved = true };

            var controller = CreateEmployeeController(propRepo, configRepo, 2);

            // Employee deactivates
            var res1 = await controller.DeactivateProperty(20, true);
            var redirect1 = res1 as RedirectToActionResult;
            if (redirect1 == null || redirect1.ActionName != "ManageProperties")
            {
                throw new Exception("Expected redirect to ManageProperties after employee deactivation.");
            }

            if (propRepo.Properties[20].ListingStatus != "Deactivated" || propRepo.Properties[20].IsApproved)
            {
                throw new Exception($"Expected property to be Deactivated, got {propRepo.Properties[20].ListingStatus}");
            }

            if (!configRepo.Audits.Any(a => a.Contains("EmployeeDeactivateProperty") && a.Contains(":20:")))
            {
                throw new Exception("Audit log for EmployeeDeactivateProperty was not recorded.");
            }

            // Employee reactivates
            var res2 = await controller.DeactivateProperty(20, false);
            var redirect2 = res2 as RedirectToActionResult;
            if (redirect2 == null || redirect2.ActionName != "ManageProperties")
            {
                throw new Exception("Expected redirect to ManageProperties after employee activation.");
            }

            if (propRepo.Properties[20].ListingStatus != "Active" || !propRepo.Properties[20].IsApproved)
            {
                throw new Exception($"Expected property to be Active, got {propRepo.Properties[20].ListingStatus}");
            }

            if (!configRepo.Audits.Any(a => a.Contains("EmployeeActivateProperty") && a.Contains(":20:")))
            {
                throw new Exception("Audit log for EmployeeActivateProperty was not recorded.");
            }
        }

        public async Task AdminController_DeleteProperty_PermanentlyDeletesPropertyAndAudits()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[30] = new Property { PropertyID = 30, Title = "Old Estate", ListingStatus = "Active" };

            var controller = CreateAdminController(propRepo, configRepo, 1);

            var result = await controller.DeleteProperty(30);
            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "ManageProperties")
            {
                throw new Exception("Expected redirect to ManageProperties after deletion.");
            }

            if (propRepo.Properties.ContainsKey(30))
            {
                throw new Exception("Property #30 should have been permanently deleted from repository.");
            }

            if (!configRepo.Audits.Any(a => a.Contains("AdminPermanentlyDeleteProperty") && a.Contains(":30:")))
            {
                throw new Exception("Audit log for AdminPermanentlyDeleteProperty was not recorded.");
            }
        }

        public async Task EmployeeController_DeleteProperty_PermanentlyDeletesPropertyAndAudits()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[40] = new Property { PropertyID = 40, Title = "Deprecated Farm", ListingStatus = "Active" };

            var controller = CreateEmployeeController(propRepo, configRepo, 2);

            var result = await controller.DeleteProperty(40);
            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "ManageProperties")
            {
                throw new Exception("Expected redirect to ManageProperties after employee deletion.");
            }

            if (propRepo.Properties.ContainsKey(40))
            {
                throw new Exception("Property #40 should have been permanently deleted from repository by employee.");
            }

            if (!configRepo.Audits.Any(a => a.Contains("EmployeePermanentlyDeleteProperty") && a.Contains(":40:")))
            {
                throw new Exception("Audit log for EmployeePermanentlyDeleteProperty was not recorded.");
            }
        }

        public async Task EmployeeController_ApproveProperty_UpdatesApprovalStatusAndAudits()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[50] = new Property { PropertyID = 50, Title = "Pending Farm", ListingStatus = "Draft", IsApproved = false };

            var controller = CreateEmployeeController(propRepo, configRepo, 2);

            var result = await controller.ApproveProperty(50, true);
            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "ManageProperties")
            {
                throw new Exception("Expected redirect to ManageProperties after employee approval.");
            }

            if (!propRepo.Properties[50].IsApproved || propRepo.Properties[50].ListingStatus != "Active")
            {
                throw new Exception("Property #50 was not approved by employee.");
            }

            if (!configRepo.Audits.Any(a => a.Contains("EmployeeApproveProperty") && a.Contains(":50:")))
            {
                throw new Exception("Audit log for EmployeeApproveProperty was not recorded.");
            }
        }

        public void Views_ManageProperties_ContainsDeactivateAndPermanentDeleteButtonsForAdminAndEmployee()
        {
            string viewPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Admin", "ManageProperties.cshtml");
            if (!File.Exists(viewPath))
            {
                viewPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "Admin", "ManageProperties.cshtml"));
            }
            if (!File.Exists(viewPath))
            {
                viewPath = @"d:\Projects\BookMyFun\Views\Admin\ManageProperties.cshtml";
            }

            string content = File.ReadAllText(viewPath);

            if (!content.Contains("asp-action=\"DeactivateProperty\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ManageProperties.cshtml does not contain DeactivateProperty action form.");
            }

            if (!content.Contains("asp-action=\"DeleteProperty\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ManageProperties.cshtml does not contain DeleteProperty action form.");
            }

            if (!content.Contains("asp-action=\"ApproveProperty\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ManageProperties.cshtml does not contain ApproveProperty action form.");
            }

            if (!content.Contains("data-filter=\"deactivated\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ManageProperties.cshtml does not contain deactivated status filter button.");
            }

            if (!content.Contains("Deactivated", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ManageProperties.cshtml does not display Deactivated status badge.");
            }
        }

        private static OwnerController CreateOwnerController(MockPropertyRepo propRepo, MockConfigRepo configRepo, int ownerUserId = 5)
        {
            var controller = new OwnerController(
                propRepo,
                null!, // userRepo
                null!, // bookingRepo
                null!, // subRepo
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

        public async Task OwnerController_DeactivateProperty_SetsStatusToDeactivatedAndAudits()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[60] = new Property { PropertyID = 60, OwnerID = 5, Title = "Peaceful Meadows", ListingStatus = "Active", IsApproved = true };

            var controller = CreateOwnerController(propRepo, configRepo, 5);

            // Owner deactivates / unpublishes their own property
            var result = await controller.DeactivateProperty(60, true);
            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "Index")
            {
                throw new Exception("Expected redirect to Index after owner deactivation.");
            }

            if (propRepo.Properties[60].ListingStatus != "Deactivated" || propRepo.Properties[60].IsApproved)
            {
                throw new Exception($"Expected property to be Deactivated and unapproved, got {propRepo.Properties[60].ListingStatus}");
            }

            if (!configRepo.Audits.Any(a => a.Contains("OwnerDeactivateProperty") && a.Contains(":60:")))
            {
                throw new Exception("Audit log for OwnerDeactivateProperty was not recorded.");
            }
        }

        public async Task OwnerController_ActivateProperty_SetsStatusToActiveAndAudits()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[65] = new Property { PropertyID = 65, OwnerID = 5, Title = "Hilltop Hideaway", ListingStatus = "Deactivated", IsApproved = false };

            var controller = CreateOwnerController(propRepo, configRepo, 5);

            // Owner publishes their deactivated property
            var result = await controller.DeactivateProperty(65, false);
            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "Index")
            {
                throw new Exception("Expected redirect to Index after owner publishing.");
            }

            if (propRepo.Properties[65].ListingStatus != "Active" || !propRepo.Properties[65].IsApproved)
            {
                throw new Exception($"Expected property to be Active and approved, got {propRepo.Properties[65].ListingStatus}");
            }

            if (!configRepo.Audits.Any(a => a.Contains("OwnerActivateProperty") && a.Contains(":65:")))
            {
                throw new Exception("Audit log for OwnerActivateProperty was not recorded.");
            }
        }

        public async Task OwnerController_DeactivateProperty_BelongingToAnotherOwner_ThrowsForbiddenException()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[70] = new Property { PropertyID = 70, OwnerID = 99, Title = "Other Host Estate", ListingStatus = "Active", IsApproved = true };

            var controller = CreateOwnerController(propRepo, configRepo, 5);

            bool exceptionCaught = false;
            try
            {
                await controller.DeactivateProperty(70, true);
            }
            catch (ForbiddenException ex)
            {
                exceptionCaught = true;
                if (!ex.Message.Contains("Cannot modify property listing belonging to another host"))
                {
                    throw new Exception($"Unexpected error message: {ex.Message}");
                }
            }

            if (!exceptionCaught)
            {
                throw new Exception("Expected ForbiddenException when Owner attempts IDOR deactivation of another host's property.");
            }
        }

        public void OwnerController_DeleteProperty_ThrowsForbiddenException()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[80] = new Property { PropertyID = 80, OwnerID = 5, Title = "Host Farm", ListingStatus = "Active", IsApproved = true };

            var controller = CreateOwnerController(propRepo, configRepo, 5);

            bool postExceptionCaught = false;
            try
            {
                controller.DeleteProperty(80);
            }
            catch (ForbiddenException ex)
            {
                postExceptionCaught = true;
                if (!ex.Message.Contains("Property deletion is strictly restricted to Admin and Employee personnel"))
                {
                    throw new Exception($"Unexpected error message: {ex.Message}");
                }
            }

            if (!postExceptionCaught)
            {
                throw new Exception("Expected ForbiddenException when Owner attempts to delete property via POST.");
            }

            bool getExceptionCaught = false;
            try
            {
                controller.DeleteProperty();
            }
            catch (ForbiddenException)
            {
                getExceptionCaught = true;
            }

            if (!getExceptionCaught)
            {
                throw new Exception("Expected ForbiddenException when Owner attempts to access DeleteProperty via GET.");
            }
        }

        public void OwnerViews_ContainDeactivateAndPublishControls_AndExplicitlyOmitDeleteProperty()
        {
            string indexViewPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Owner", "Index.cshtml");
            if (!File.Exists(indexViewPath))
            {
                indexViewPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "Owner", "Index.cshtml"));
            }
            if (!File.Exists(indexViewPath))
            {
                indexViewPath = @"d:\Projects\BookMyFun\Views\Owner\Index.cshtml";
            }

            string editViewPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Owner", "EditProperty.cshtml");
            if (!File.Exists(editViewPath))
            {
                editViewPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "Owner", "EditProperty.cshtml"));
            }
            if (!File.Exists(editViewPath))
            {
                editViewPath = @"d:\Projects\BookMyFun\Views\Owner\EditProperty.cshtml";
            }

            string indexContent = File.ReadAllText(indexViewPath);
            string editContent = File.ReadAllText(editViewPath);

            // 1. Index view contains DeactivateProperty action, Unpublish and Publish buttons
            if (!indexContent.Contains("asp-action=\"DeactivateProperty\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Owner Index.cshtml does not contain DeactivateProperty action.");
            }

            if (!indexContent.Contains("Unpublish", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Owner Index.cshtml does not contain Unpublish action.");
            }

            if (!indexContent.Contains("Publish", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Owner Index.cshtml does not contain Publish action.");
            }

            if (!indexContent.Contains("Unpublished", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Owner Index.cshtml does not display Unpublished status indicator.");
            }

            // 2. Explicitly verify NO property deletion controls exist in Owner Index
            if (indexContent.Contains("DeleteProperty", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Security violation: Owner Index.cshtml must not contain DeleteProperty action.");
            }

            // 3. Edit view contains DeactivateProperty banner controls
            if (!editContent.Contains("asp-action=\"DeactivateProperty\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Owner EditProperty.cshtml does not contain DeactivateProperty action banner.");
            }

            // 4. Explicitly verify NO property deletion controls exist in Owner EditProperty
            if (editContent.Contains("DeleteProperty", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Security violation: Owner EditProperty.cshtml must not contain DeleteProperty action.");
            }
        }
    }
}
