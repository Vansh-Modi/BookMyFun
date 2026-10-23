using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookMyFun.Tests
{
    public class ServiceableAreasTests
    {
        private class TestTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
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

        private class MockEventPackageRepo : IEventPackageRepository
        {
            public string? SavedServiceAreas { get; set; } = "Pune; Lonavala";
            public List<string> MockDistinctLocations { get; set; } = new() { "Pune", "Lonavala", "Alibaug", "Mahabaleshwar", "Nashik" };

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
            public Task<List<string>> GetAllDistinctPropertyLocationsAsync() => Task.FromResult(MockDistinctLocations);
            public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>(SavedServiceAreas);
            public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas)
            {
                SavedServiceAreas = serviceAreas;
                return Task.FromResult(true);
            }
            public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(true);
        }

        private static EventManagerController CreateController(MockEventPackageRepo eventRepo, MockConfigRepo configRepo, int userId = 77)
        {
            var controller = new EventManagerController(
                eventRepo,
                null!, // mediaStorage
                configRepo,
                NullLogger<EventManagerController>.Instance
            );

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, "EventManager"),
                new Claim(ClaimTypes.Name, "Decor Studio")
            }, "TestAuth"));

            var httpContext = new DefaultHttpContext { User = user };
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
            return controller;
        }

        public async Task UpdateServiceAreas_WithSelectedAreas_UpdatesRepoAndLogsAudit()
        {
            var eventRepo = new MockEventPackageRepo();
            var configRepo = new MockConfigRepo();
            var controller = CreateController(eventRepo, configRepo, 77);

            string[] selectedAreas = new[] { "Lonavala", "Alibaug", "Karjat Hills" };
            var result = await controller.UpdateServiceAreas(selectedAreas, "/EventManager/ServiceAreas");

            var redirect = result as RedirectResult;
            if (redirect == null || redirect.Url != "/EventManager/ServiceAreas")
            {
                throw new Exception("Expected redirect to returnUrl '/EventManager/ServiceAreas'");
            }

            if (eventRepo.SavedServiceAreas != "Lonavala; Alibaug; Karjat Hills")
            {
                throw new Exception($"Expected service areas 'Lonavala; Alibaug; Karjat Hills', but got: {eventRepo.SavedServiceAreas}");
            }

            if (!configRepo.Audits.Any(a => a.Contains("UpdateServiceAreas") && a.Contains("Karjat Hills")))
            {
                throw new Exception("Expected audit log for UpdateServiceAreas with new areas.");
            }
        }

        public async Task UpdateServiceAreas_WithNullOrEmpty_ClearsServiceAreas()
        {
            var eventRepo = new MockEventPackageRepo();
            var configRepo = new MockConfigRepo();
            var controller = CreateController(eventRepo, configRepo, 77);

            var result = await controller.UpdateServiceAreas(null, null);

            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "ServiceAreas")
            {
                throw new Exception("Expected redirect to ServiceAreas action.");
            }

            if (!string.IsNullOrEmpty(eventRepo.SavedServiceAreas))
            {
                throw new Exception($"Expected service areas to be cleared (empty), but got: '{eventRepo.SavedServiceAreas}'");
            }
        }

        public async Task ServiceAreas_Get_ReturnsViewModelWithDistinctLocationsAndSelectedAreas()
        {
            var eventRepo = new MockEventPackageRepo { SavedServiceAreas = "Pune; Nashik" };
            var configRepo = new MockConfigRepo();
            var controller = CreateController(eventRepo, configRepo, 77);

            var result = await controller.ServiceAreas();
            var viewResult = result as ViewResult;
            if (viewResult == null)
            {
                throw new Exception("Expected ViewResult from ServiceAreas GET action.");
            }

            var vm = viewResult.Model as EventManagerServiceAreasViewModel;
            if (vm == null)
            {
                throw new Exception("Expected EventManagerServiceAreasViewModel model.");
            }

            if (vm.AvailableLocations.Count != 5 || !vm.AvailableLocations.Contains("Mahabaleshwar"))
            {
                throw new Exception("AvailableLocations not populated properly from catalog.");
            }

            if (vm.SelectedAreas.Count != 2 || !vm.SelectedAreas.Contains("Pune") || !vm.SelectedAreas.Contains("Nashik"))
            {
                throw new Exception("SelectedAreas not populated properly from saved areas.");
            }
        }

        public void ServiceAreasView_ContainsChipsTagsInputMapBoundariesAndSearch()
        {
            string viewPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "EventManager", "ServiceAreas.cshtml");
            if (!File.Exists(viewPath))
            {
                viewPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "EventManager", "ServiceAreas.cshtml"));
            }
            if (!File.Exists(viewPath))
            {
                viewPath = @"d:\Projects\BookMyFun\Views\EventManager\ServiceAreas.cshtml";
            }

            string content = File.ReadAllText(viewPath);

            // 1. Multi-select chips grid & area chips
            if (!content.Contains("catalogChipsGrid", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ServiceAreas.cshtml missing catalogChipsGrid container.");
            }
            if (!content.Contains("area-chip", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ServiceAreas.cshtml missing area-chip elements.");
            }

            // 2. Active Selected Chips Cloud with remove tags
            if (!content.Contains("selectedChipsCloud", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ServiceAreas.cshtml missing selectedChipsCloud container.");
            }
            if (!content.Contains("btn-remove-tag", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ServiceAreas.cshtml missing btn-remove-tag buttons.");
            }

            // 3. Custom Area Tag Input & Add Button
            if (!content.Contains("customAreaInput", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ServiceAreas.cshtml missing customAreaInput element.");
            }
            if (!content.Contains("btnAddCustomArea", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ServiceAreas.cshtml missing btnAddCustomArea button.");
            }

            // 4. Map Boundary / Regional Zone Selectors
            if (!content.Contains("boundaryZonePresets", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ServiceAreas.cshtml missing boundaryZonePresets container.");
            }
            if (!content.Contains("zone-preset-pill", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ServiceAreas.cshtml missing zone-preset-pill boundary selectors.");
            }
            if (!content.Contains("data-zone=\"hills\"", StringComparison.OrdinalIgnoreCase) || !content.Contains("data-zone=\"coastal\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ServiceAreas.cshtml missing regional boundary presets (hills, coastal).");
            }

            // 5. Live Search & Control Buttons
            if (!content.Contains("catalogSearchInput", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ServiceAreas.cshtml missing catalogSearchInput.");
            }
            if (!content.Contains("btnSelectAllCatalog", StringComparison.OrdinalIgnoreCase) || !content.Contains("btnClearAllTags", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ServiceAreas.cshtml missing Select All or Clear All control buttons.");
            }

            // 6. Form Submission Synchronization
            if (!content.Contains("asp-action=\"UpdateServiceAreas\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ServiceAreas.cshtml missing form action UpdateServiceAreas.");
            }
            if (!content.Contains("hiddenInputsContainer", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ServiceAreas.cshtml missing hiddenInputsContainer for selectedAreas form synchronization.");
            }
        }

        public void DashboardAndIndexViews_ContainRedesignedMultiSelectChipsWidget()
        {
            string dashPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "EventManager", "Dashboard.cshtml");
            if (!File.Exists(dashPath))
            {
                dashPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "EventManager", "Dashboard.cshtml"));
            }
            if (!File.Exists(dashPath))
            {
                dashPath = @"d:\Projects\BookMyFun\Views\EventManager\Dashboard.cshtml";
            }

            string indexPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "EventManager", "Index.cshtml");
            if (!File.Exists(indexPath))
            {
                indexPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "EventManager", "Index.cshtml"));
            }
            if (!File.Exists(indexPath))
            {
                indexPath = @"d:\Projects\BookMyFun\Views\EventManager\Index.cshtml";
            }

            string dashContent = File.ReadAllText(dashPath);
            string indexContent = File.ReadAllText(indexPath);

            foreach (var (name, content) in new[] { ("Dashboard.cshtml", dashContent), ("Index.cshtml", indexContent) })
            {
                if (!content.Contains("dashSelectedChipsCloud", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"{name} missing dashSelectedChipsCloud element.");
                }
                if (!content.Contains("dashChipsGrid", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"{name} missing dashChipsGrid multi-select chips.");
                }
                if (!content.Contains("dashCustomAreaInput", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"{name} missing dashCustomAreaInput.");
                }
                if (!content.Contains("dashBtnAddCustom", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"{name} missing dashBtnAddCustom.");
                }
                if (!content.Contains("dashLocationSearch", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"{name} missing dashLocationSearch filter.");
                }
                if (!content.Contains("dashHiddenInputsContainer", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"{name} missing dashHiddenInputsContainer for form sync.");
                }
            }
        }
    }
}
