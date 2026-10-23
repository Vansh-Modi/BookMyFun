using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookMyFun.Tests
{
    public class DecorationUpsellUXTests
    {
        private class SimpleTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class StubEventPackageRepo : IEventPackageRepository
        {
            public readonly Dictionary<int, EventPackage> Packages = new();
            public EventPackage? LastUpdatedPackage { get; private set; }

            public StubEventPackageRepo()
            {
                Packages[1] = new EventPackage
                {
                    PackageID = 1,
                    Name = "Royal Garden Luxe",
                    Category = "Wedding",
                    BasePrice = 35000,
                    IsActive = true,
                    MediaUrl = "https://images.unsplash.com/royal-garden"
                };
                Packages[2] = new EventPackage
                {
                    PackageID = 2,
                    Name = "Neon Night Cocktail Party",
                    Category = "Party",
                    BasePrice = 18000,
                    IsActive = false,
                    MediaUrl = "https://images.unsplash.com/neon-night"
                };
            }

            public Task<List<EventPackage>> GetAllPackagesAsync() => Task.FromResult(Packages.Values.ToList());
            public Task<List<EventPackage>> GetActivePackagesAsync() => Task.FromResult(Packages.Values.Where(p => p.IsActive).ToList());
            public Task<EventPackage?> GetPackageByIdAsync(int packageId)
            {
                Packages.TryGetValue(packageId, out var pkg);
                return Task.FromResult(pkg);
            }

            public Task<int> CreatePackageAsync(EventPackage package)
            {
                package.PackageID = Packages.Count + 1;
                Packages[package.PackageID] = package;
                return Task.FromResult(package.PackageID);
            }

            public Task<bool> UpdatePackageAsync(EventPackage package)
            {
                Packages[package.PackageID] = package;
                LastUpdatedPackage = package;
                return Task.FromResult(true);
            }

            public Task<bool> UpdatePackageCommissionRateAsync(int packageId, decimal? commissionRate) => Task.FromResult(true);

            public Task<bool> DeletePackageAsync(int packageId)
            {
                return Task.FromResult(Packages.Remove(packageId));
            }

            public Task<List<Booking>> GetAssignedEventBookingsAsync(int decoratorUserId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllEventBookingsAsync() => Task.FromResult(new List<Booking>());
            public Task<bool> UpdateSetupStatusAsync(int bookingId, string setupStatus, string? setupProofMediaUrls, int? decoratorId) => Task.FromResult(true);
            public Task<bool> AssignDecoratorAsync(int bookingId, int decoratorUserId) => Task.FromResult(true);
            public Task<bool> CancelOrRemoveEventFromBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<List<User>> GetDecoratorsAsync() => Task.FromResult(new List<User>());
            public Task<List<string>> GetAllDistinctPropertyLocationsAsync() => Task.FromResult(new List<string> { "Lonavala", "Alibaug", "Karjat" });
            public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>("Lonavala;Alibaug");
            public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas) => Task.FromResult(true);
            public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(true);
        }

        private class StubConfigRepo : IConfigRepository
        {
            public readonly List<string> Audits = new();

            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
            public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());

            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details)
            {
                Audits.Add($"{performedByUserId}:{action}:{entityType}:{entityId}:{details}");
                return Task.CompletedTask;
            }
        }

        private class StubMediaStorageService : IMediaStorageService
        {
            public Task<(string publicUrl, string publicId)> SaveMediaFileAsync(IFormFile file, string mediaType) =>
                Task.FromResult(("/uploads/photos/test.webp", "test_id"));

            public Task<string> SavePrivateDocumentAsync(IFormFile file, int clientId) => Task.FromResult("/private/doc.pdf");
            public Task<bool> ValidateExternalUrlAsync(string url) => Task.FromResult(true);
            public string GenerateExpiringDocumentToken(string documentKey, int requestingUserId, string userRole, int expirationMinutes = 15) => "token";
            public bool ValidateExpiringDocumentToken(string documentKey, string token, int requestingUserId, string userRole) => true;
            public string GetPrivateDocumentPhysicalPath(string documentKey) => documentKey;
        }

        private static EventManagerController CreateController(StubEventPackageRepo eventRepo, StubConfigRepo configRepo)
        {
            var controller = new EventManagerController(
                eventRepo,
                new StubMediaStorageService(),
                configRepo,
                NullLogger<EventManagerController>.Instance
            );

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "42"),
                new Claim(ClaimTypes.Role, "EventManager"),
                new Claim(ClaimTypes.Name, "Premier Decor Studio")
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

        // --- TEST 70: TogglePackageStatus Toggles Active State and Logs Audit ---
        public async Task TogglePackageStatus_TogglesActiveState_AndLogsAudit()
        {
            var eventRepo = new StubEventPackageRepo();
            var configRepo = new StubConfigRepo();
            var controller = CreateController(eventRepo, configRepo);

            // Package #1 is initially Active
            if (!eventRepo.Packages[1].IsActive)
            {
                throw new Exception("Precondition failed: Package #1 should initially be active.");
            }

            var result = await controller.TogglePackageStatus(1);
            var redirect = result as RedirectToActionResult;
            if (redirect == null || redirect.ActionName != "Packages")
            {
                throw new Exception($"Expected redirect to Packages, but got: {result?.GetType().Name}");
            }

            if (eventRepo.Packages[1].IsActive)
            {
                throw new Exception("Package #1 IsActive should have toggled to false.");
            }

            if (!configRepo.Audits.Any(a => a.Contains("ToggleEventPackageStatus") && a.Contains(":1:")))
            {
                throw new Exception("Audit log for ToggleEventPackageStatus was not recorded.");
            }

            if (!controller.TempData.ContainsKey("Success") || !controller.TempData["Success"]!.ToString()!.Contains("status set to Inactive"))
            {
                throw new Exception("Expected success notification in TempData indicating package status change.");
            }
        }

        // --- TEST 71: TogglePackageStatus With ReturnUrl Redirects to ReturnUrl ---
        public async Task TogglePackageStatus_WithReturnUrl_RedirectsToReturnUrl()
        {
            var eventRepo = new StubEventPackageRepo();
            var configRepo = new StubConfigRepo();
            var controller = CreateController(eventRepo, configRepo);

            const string returnUrl = "/EventManager/Dashboard";
            var result = await controller.TogglePackageStatus(2, returnUrl);
            var redirect = result as RedirectResult;
            if (redirect == null || redirect.Url != returnUrl)
            {
                throw new Exception($"Expected redirect to returnUrl '{returnUrl}', but got: {(redirect != null ? redirect.Url : result?.GetType().Name)}");
            }

            // Package #2 was false, should now be true
            if (!eventRepo.Packages[2].IsActive)
            {
                throw new Exception("Package #2 IsActive should have toggled to true.");
            }
        }

        // --- TEST 72: TogglePackageStatus NonExistent Package Returns NotFound ---
        public async Task TogglePackageStatus_NonExistentPackage_ReturnsNotFound()
        {
            var eventRepo = new StubEventPackageRepo();
            var configRepo = new StubConfigRepo();
            var controller = CreateController(eventRepo, configRepo);

            var result = await controller.TogglePackageStatus(99999);
            if (result is not NotFoundResult)
            {
                throw new Exception($"Expected NotFoundResult for invalid package ID, but got: {result?.GetType().Name}");
            }
        }

        // --- TEST 73: DeletePackage With ReturnUrl Redirects to ReturnUrl ---
        public async Task DeletePackage_WithReturnUrl_RedirectsToReturnUrl()
        {
            var eventRepo = new StubEventPackageRepo();
            var configRepo = new StubConfigRepo();
            var controller = CreateController(eventRepo, configRepo);

            const string returnUrl = "/EventManager/Dashboard";
            var result = await controller.DeletePackage(1, returnUrl);
            var redirect = result as RedirectResult;
            if (redirect == null || redirect.Url != returnUrl)
            {
                throw new Exception($"Expected redirect to returnUrl '{returnUrl}', but got: {(redirect != null ? redirect.Url : result?.GetType().Name)}");
            }

            if (eventRepo.Packages.ContainsKey(1))
            {
                throw new Exception("Package #1 should have been deleted from repository.");
            }
        }

        // --- TEST 74: Dashboard Views Reorganized Hierarchy Elevates Offerings Showcase Above Orders and Service Areas ---
        public void DashboardViews_ReorganizedHierarchy_ElevatesOfferingsShowcaseAboveOrdersAndServiceAreas()
        {
            var viewsToTest = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "Views", "EventManager", "Dashboard.cshtml"),
                Path.Combine(Directory.GetCurrentDirectory(), "Views", "EventManager", "Index.cshtml")
            };

            foreach (var viewPath in viewsToTest)
            {
                if (!File.Exists(viewPath))
                {
                    throw new Exception($"View file not found at: {viewPath}");
                }

                var content = File.ReadAllText(viewPath);

                var offeringsIdx = content.IndexOf("id=\"decorationOfferingsShowcase\"", StringComparison.OrdinalIgnoreCase);
                if (offeringsIdx == -1)
                {
                    throw new Exception($"View {Path.GetFileName(viewPath)} does not declare the prominent '#decorationOfferingsShowcase' container.");
                }

                var ordersIdx = content.IndexOf("id=\"eventOrdersTable\"", StringComparison.OrdinalIgnoreCase);
                if (ordersIdx == -1)
                {
                    throw new Exception($"View {Path.GetFileName(viewPath)} does not contain '#eventOrdersTable'.");
                }

                var serviceAreasIdx = content.IndexOf("id=\"dashServiceAreasForm\"", StringComparison.OrdinalIgnoreCase);
                if (serviceAreasIdx == -1)
                {
                    throw new Exception($"View {Path.GetFileName(viewPath)} does not contain '#dashServiceAreasForm'.");
                }

                // Verify hierarchy order: Offerings Showcase MUST be elevated above Orders AND above Service Areas
                if (offeringsIdx >= ordersIdx)
                {
                    throw new Exception($"Hierarchy violation in {Path.GetFileName(viewPath)}: Decoration Offerings Showcase (index {offeringsIdx}) must appear before Event Orders (index {ordersIdx}).");
                }

                if (offeringsIdx >= serviceAreasIdx)
                {
                    throw new Exception($"Hierarchy violation in {Path.GetFileName(viewPath)}: Decoration Offerings Showcase (index {offeringsIdx}) must appear before Service Areas Configuration (index {serviceAreasIdx}).");
                }
            }
        }

        // --- TEST 75: Dashboard Views Offerings Showcase Declares Conversion Badges and Interactive Toggles ---
        public void DashboardViews_OfferingsShowcase_DeclaresConversionBadgesAndInteractiveToggles()
        {
            var viewsToTest = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "Views", "EventManager", "Dashboard.cshtml"),
                Path.Combine(Directory.GetCurrentDirectory(), "Views", "EventManager", "Index.cshtml")
            };

            foreach (var viewPath in viewsToTest)
            {
                var content = File.ReadAllText(viewPath);

                if (!content.Contains("Live in Guest Checkout"))
                {
                    throw new Exception($"View {Path.GetFileName(viewPath)} missing 'Live in Guest Checkout' conversion badge.");
                }

                if (!content.Contains("Featured Decoration Offerings &amp; Guest Upsells"))
                {
                    throw new Exception($"View {Path.GetFileName(viewPath)} missing prominent showcase header.");
                }

                if (!content.Contains("Create New Offering") || !content.Contains("Full Catalog"))
                {
                    throw new Exception($"View {Path.GetFileName(viewPath)} missing primary offering call-to-action buttons.");
                }

                if (!content.Contains("offering-focal-card") || !content.Contains("offering-payout-chip"))
                {
                    throw new Exception($"View {Path.GetFileName(viewPath)} missing high-converting visual components (.offering-focal-card, .offering-payout-chip).");
                }

                if (!content.Contains("asp-action=\"TogglePackageStatus\""))
                {
                    throw new Exception($"View {Path.GetFileName(viewPath)} missing 1-click status toggle action in offering cards.");
                }
            }
        }
    }
}
