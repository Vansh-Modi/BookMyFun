using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace BookMyFun.Tests
{
    public class EmployeePropertyApprovalTests
    {
        private class SimpleTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
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

            public Task<bool> UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl) => Task.FromResult(true);
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
            public Task<bool> UpdatePropertyCategoryAsync(int propertyId, int ownerId, int propertyTypeId) => Task.FromResult(true);
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

        private static EmployeeController CreateEmployeeController(MockPropertyRepo propRepo, MockConfigRepo configRepo, int empUserId = 5)
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
                new Claim(ClaimTypes.Name, "Employee Reviewer")
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

        // --- TEST 64: Employee Approves Property -> IsApproved=true, ListingStatus='Active', Audit logged ---
        public async Task EmployeeApproveProperty_SetsIsApprovedTrue_AndListingStatusActive_AndAudits()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[101] = new Property
            {
                PropertyID = 101,
                Title = "Whispering Pines Villa",
                IsApproved = false,
                ListingStatus = "Draft"
            };

            var controller = CreateEmployeeController(propRepo, configRepo, 5);

            var result = await controller.ApproveProperty(101, approve: true);
            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "ManageProperties")
            {
                throw new Exception($"Expected redirect to ManageProperties, but got: {result?.GetType().Name}");
            }

            var prop = propRepo.Properties[101];
            if (!prop.IsApproved)
            {
                throw new Exception("Property.IsApproved should have transitioned to true on employee approval.");
            }
            if (prop.ListingStatus != "Active")
            {
                throw new Exception($"Property.ListingStatus should be 'Active' on employee approval, but got '{prop.ListingStatus}'.");
            }

            if (!configRepo.Audits.Any(a => a.Contains("EmployeeApproveProperty") && a.Contains(":101:")))
            {
                throw new Exception("Audit trail for EmployeeApproveProperty on Property #101 was not recorded.");
            }

            if (!controller.TempData.ContainsKey("Success") || !controller.TempData["Success"]!.ToString()!.Contains("approved and made live"))
            {
                throw new Exception("Expected success notification in TempData indicating listing approval.");
            }
        }

        // --- TEST 65: Employee Rejects Property -> IsApproved=false, ListingStatus='Rejected', Audit logged ---
        public async Task EmployeeRejectProperty_SetsIsApprovedFalse_AndListingStatusRejected_AndAudits()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[102] = new Property
            {
                PropertyID = 102,
                Title = "Incomplete Cabin",
                IsApproved = false,
                ListingStatus = "Draft"
            };

            var controller = CreateEmployeeController(propRepo, configRepo, 5);

            var result = await controller.ApproveProperty(102, approve: false, status: "Rejected");
            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "ManageProperties")
            {
                throw new Exception($"Expected redirect to ManageProperties, but got: {result?.GetType().Name}");
            }

            var prop = propRepo.Properties[102];
            if (prop.IsApproved)
            {
                throw new Exception("Property.IsApproved should remain false on rejection.");
            }
            if (prop.ListingStatus != "Rejected")
            {
                throw new Exception($"Property.ListingStatus should be 'Rejected', but got '{prop.ListingStatus}'.");
            }

            if (!configRepo.Audits.Any(a => a.Contains("EmployeeRejectProperty") && a.Contains(":102:")))
            {
                throw new Exception("Audit trail for EmployeeRejectProperty on Property #102 was not recorded.");
            }

            if (!controller.TempData.ContainsKey("Success") || !controller.TempData["Success"]!.ToString()!.Contains("rejected"))
            {
                throw new Exception("Expected success notification in TempData indicating listing rejection.");
            }
        }

        // --- TEST 66: Employee Revokes Property -> IsApproved=false, ListingStatus='Draft', Audit logged ---
        public async Task EmployeeRevokeProperty_SetsIsApprovedFalse_AndListingStatusDraft_AndAudits()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[103] = new Property
            {
                PropertyID = 103,
                Title = "Mountain Chalet",
                IsApproved = true,
                ListingStatus = "Active"
            };

            var controller = CreateEmployeeController(propRepo, configRepo, 5);

            var result = await controller.ApproveProperty(103, approve: false, status: "Draft");
            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "ManageProperties")
            {
                throw new Exception($"Expected redirect to ManageProperties, but got: {result?.GetType().Name}");
            }

            var prop = propRepo.Properties[103];
            if (prop.IsApproved)
            {
                throw new Exception("Property.IsApproved should be set to false on revocation.");
            }
            if (prop.ListingStatus != "Draft")
            {
                throw new Exception($"Property.ListingStatus should be 'Draft' on revocation, but got '{prop.ListingStatus}'.");
            }

            if (!configRepo.Audits.Any(a => a.Contains("EmployeeRevokeProperty") && a.Contains(":103:")))
            {
                throw new Exception("Audit trail for EmployeeRevokeProperty on Property #103 was not recorded.");
            }
        }

        // --- TEST 67: Employee Approve NonExistent Property -> Returns NotFound ---
        public async Task EmployeeApproveProperty_NonExistentProperty_ReturnsNotFound()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();

            var controller = CreateEmployeeController(propRepo, configRepo, 5);

            var result = await controller.ApproveProperty(99999, approve: true);
            if (result is not NotFoundResult)
            {
                throw new Exception($"Expected NotFoundResult for non-existent property #99999, but got: {result?.GetType().Name}");
            }
        }

        // --- TEST 68: Employee Approve With ReturnUrl -> Redirects to ReturnUrl ---
        public async Task EmployeeApproveProperty_RedirectsToReturnUrl_WhenValid()
        {
            var propRepo = new MockPropertyRepo();
            var configRepo = new MockConfigRepo();
            propRepo.Properties[104] = new Property
            {
                PropertyID = 104,
                Title = "Lakeside Cottage",
                IsApproved = false,
                ListingStatus = "Draft"
            };

            var controller = CreateEmployeeController(propRepo, configRepo, 5);

            const string returnUrl = "/Employee/Index";
            var result = await controller.ApproveProperty(104, approve: true, returnUrl: returnUrl);
            var redirect = result as RedirectResult;
            if (redirect == null || redirect.Url != returnUrl)
            {
                throw new Exception($"Expected redirect to returnUrl '{returnUrl}', but got: {(redirect != null ? redirect.Url : result?.GetType().Name)}");
            }

            var prop = propRepo.Properties[104];
            if (!prop.IsApproved || prop.ListingStatus != "Active")
            {
                throw new Exception("Property should be approved and active after approval.");
            }
        }

        // --- TEST 69: Views Contain Verification Workflow Controls & Badges ---
        public void EmployeeViews_ContainApprovalActionButtons_AndPendingReviewBadges()
        {
            var employeeIndexPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Employee", "Index.cshtml");
            if (!File.Exists(employeeIndexPath))
            {
                throw new Exception($"Employee Index view not found at: {employeeIndexPath}");
            }
            var employeeIndexContent = File.ReadAllText(employeeIndexPath);

            // Verify Pending Property Approvals Stat card and Filter Pills exist
            if (!employeeIndexContent.Contains("Pending Property Approvals"))
            {
                throw new Exception("Views/Employee/Index.cshtml should include 'Pending Property Approvals' metric card.");
            }
            if (!employeeIndexContent.Contains("Pending Review"))
            {
                throw new Exception("Views/Employee/Index.cshtml should include 'Pending Review' filter/badge.");
            }

            // Verify Approve, Reject, and Revoke action forms exist
            if (!employeeIndexContent.Contains("asp-action=\"ApproveProperty\"") ||
                !employeeIndexContent.Contains("asp-route-approve=\"true\"") ||
                !employeeIndexContent.Contains("asp-route-status=\"Rejected\""))
            {
                throw new Exception("Views/Employee/Index.cshtml must declare forms posting to ApproveProperty with approve=true and status=Rejected.");
            }

            // Verify ManageProperties view also contains comprehensive approval/rejection controls
            var managePropertiesPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Admin", "ManageProperties.cshtml");
            if (!File.Exists(managePropertiesPath))
            {
                throw new Exception($"ManageProperties view not found at: {managePropertiesPath}");
            }
            var managePropertiesContent = File.ReadAllText(managePropertiesPath);

            if (!managePropertiesContent.Contains("asp-action=\"ApproveProperty\"") ||
                !managePropertiesContent.Contains("asp-route-approve=\"true\"") ||
                !managePropertiesContent.Contains("asp-route-status=\"Rejected\""))
            {
                throw new Exception("Views/Admin/ManageProperties.cshtml must declare forms for both Approve (approve=true) and Reject (status=Rejected).");
            }
        }
    }
}
