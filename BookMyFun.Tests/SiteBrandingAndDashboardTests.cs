using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class SiteBrandingAndDashboardTests
    {
        [Fact]
        public void SiteSettingsModel_BrandingDefaults_AreExpected()
        {
            var model = new SiteSettingsModel();

            Assert.Equal("BookMyFun", model.SiteName);
            Assert.Equal("", model.LogoUrl);
            Assert.Equal("bi-house-heart-fill", model.SiteIcon);
            Assert.Contains("BookMyFun", model.CopyrightText);
            Assert.Contains("All rights reserved", model.CopyrightText);
        }

        [Fact]
        public async Task SiteSettingsService_BrandingPersistenceAndCacheInvalidation_Works()
        {
            var repo = new InMemoryConfigRepository();
            var service = new SiteSettingsService(repo);

            // Fetch default
            var settings = await service.GetSettingsAsync();
            Assert.Equal("BookMyFun", settings.SiteName);

            // Modify and save
            settings.SiteName = "FunStays";
            settings.LogoUrl = "/uploads/photos/test_logo.png";
            settings.CopyrightText = "© 2026 FunStays Inc. All rights reserved.";

            await service.SaveSettingsAsync(settings);

            // Invalidate and re-fetch to confirm persistence in repository
            service.InvalidateSettingsCache();
            var updated = await service.GetSettingsAsync();

            Assert.Equal("FunStays", updated.SiteName);
            Assert.Equal("/uploads/photos/test_logo.png", updated.LogoUrl);
            Assert.Equal("© 2026 FunStays Inc. All rights reserved.", updated.CopyrightText);
        }

        [Fact]
        public async Task SettingsController_GetBranding_ReturnsPublicBrandingObject()
        {
            var repo = new InMemoryConfigRepository();
            var service = new SiteSettingsService(repo);
            var mediaStorage = new StubMediaStorageService();
            var controller = new SettingsController(service, mediaStorage, repo, NullLogger<SettingsController>.Instance);

            var result = await controller.GetBranding();
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);

            var data = Assert.IsType<BrandingSettingsResponse>(okResult.Value);
            Assert.Equal("BookMyFun", data.SiteName);
            Assert.False(string.IsNullOrWhiteSpace(data.CopyrightText));
        }

        [Fact]
        public async Task SettingsController_UpdateBranding_RequiresSiteName()
        {
            var repo = new InMemoryConfigRepository();
            var service = new SiteSettingsService(repo);
            var mediaStorage = new StubMediaStorageService();
            var controller = new SettingsController(service, mediaStorage, repo, NullLogger<SettingsController>.Instance);

            var result = await controller.UpdateBranding(siteName: "   ", copyrightText: null);
            var badResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badResult.Value);
        }

        [Fact]
        public async Task SettingsController_UpdateBranding_RejectsOversizedLogo()
        {
            var repo = new InMemoryConfigRepository();
            var service = new SiteSettingsService(repo);
            var mediaStorage = new StubMediaStorageService();
            var controller = new SettingsController(service, mediaStorage, repo, NullLogger<SettingsController>.Instance);

            // 3MB mock file (limit is 2MB)
            var formFile = new FormFileStub("logo.png", 3 * 1024 * 1024);

            var result = await controller.UpdateBranding(siteName: "BookMyFun", copyrightText: null, removeLogo: false, logoFile: formFile);
            var badResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badResult.Value);
        }

        [Fact]
        public async Task SettingsController_UpdateBranding_RejectsInvalidExtension()
        {
            var repo = new InMemoryConfigRepository();
            var service = new SiteSettingsService(repo);
            var mediaStorage = new StubMediaStorageService();
            var controller = new SettingsController(service, mediaStorage, repo, NullLogger<SettingsController>.Instance);

            var formFile = new FormFileStub("malicious.exe", 1024);

            var result = await controller.UpdateBranding(siteName: "BookMyFun", copyrightText: null, removeLogo: false, logoFile: formFile);
            var badResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badResult.Value);
        }

        [Fact]
        public async Task SettingsController_UpdateBranding_ResetLogo_ClearsLogoUrl()
        {
            var repo = new InMemoryConfigRepository();
            var service = new SiteSettingsService(repo);
            var mediaStorage = new StubMediaStorageService();
            var controller = new SettingsController(service, mediaStorage, repo, NullLogger<SettingsController>.Instance);

            // Setup current logo
            var current = await service.GetSettingsAsync();
            current.LogoUrl = "/uploads/photos/old_logo.png";
            await service.SaveSettingsAsync(current);

            var result = await controller.UpdateBranding(siteName: "BookMyFun", copyrightText: "© 2026", removeLogo: true);
            var okResult = Assert.IsType<OkObjectResult>(result);

            var updated = await service.GetSettingsAsync();
            Assert.Equal("", updated.LogoUrl);
            Assert.Equal("© 2026", updated.CopyrightText);
        }

        [Fact]
        public void Layout_DashboardDropdowns_ContainOnlyMyProfileAndLogout()
        {
            var layoutPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Shared", "_Layout.cshtml");
            Assert.True(File.Exists(layoutPath), $"_Layout.cshtml not found at {layoutPath}");

            var layoutContent = File.ReadAllText(layoutPath);
            Assert.Contains("NavigationMenuDefinition.GetHeaderDropdownItems", layoutContent);

            // Verify role dropdowns via NavigationMenuDefinition
            var ownerItems = BookMyFun.Services.Navigation.NavigationMenuDefinition.GetHeaderDropdownItems("Owner");
            Assert.Contains(ownerItems, i => i.Title == "My Profile");
            Assert.Contains(ownerItems, i => i.IsLogout);

            var adminItems = BookMyFun.Services.Navigation.NavigationMenuDefinition.GetHeaderDropdownItems("Admin");
            Assert.Contains(adminItems, i => i.Title == "My Profile");
            Assert.Contains(adminItems, i => i.IsLogout);

            var clientItems = BookMyFun.Services.Navigation.NavigationMenuDefinition.GetHeaderDropdownItems("Client");
            Assert.Contains(clientItems, i => i.Title == "My Bookings");
            Assert.Contains(clientItems, i => i.Title == "Receipts");
            Assert.Contains(clientItems, i => i.Title == "My Profile");
            Assert.Contains(clientItems, i => i.IsLogout);
            Assert.DoesNotContain(clientItems, i => i.Title.Contains("Host Dashboard"));
        }

        [Fact]
        public void DashboardLayout_RepeatedBrandBlock_RemovedFromSidebar()
        {
            var dashLayoutPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Shared", "_DashboardLayout.cshtml");
            Assert.True(File.Exists(dashLayoutPath), $"_DashboardLayout.cshtml not found at {dashLayoutPath}");

            var dashLayoutContent = File.ReadAllText(dashLayoutPath);

            // Brand header block was removed from desktop sidebar
            Assert.DoesNotContain("dashboard-sidebar-header", dashLayoutContent);
        }

        private class InMemoryConfigRepository : IConfigRepository
        {
            private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);

            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);

            public Task<string?> GetSettingValueAsync(string key)
            {
                _store.TryGetValue(key, out var val);
                return Task.FromResult<string?>(val);
            }

            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false)
            {
                _store[key] = value;
                return Task.FromResult(true);
            }

            public Task<List<SiteSetting>> GetAllSettingsAsync()
            {
                var list = _store.Select(kv => new SiteSetting { SettingKey = kv.Key, SettingValue = kv.Value }).ToList();
                return Task.FromResult(list);
            }

            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details) => Task.CompletedTask;
        }

        private class StubMediaStorageService : IMediaStorageService
        {
            public Task<(string publicUrl, string publicId)> SaveMediaFileAsync(IFormFile file, string mediaType)
                => Task.FromResult(("/uploads/photos/saved_test_logo.png", "stub-id"));

            public Task<string> SavePrivateDocumentAsync(IFormFile file, int clientId) => Task.FromResult("stub-doc");
            public Task<bool> ValidateExternalUrlAsync(string url) => Task.FromResult(true);
            public string GenerateExpiringDocumentToken(string documentKey, int requestingUserId, string userRole, int expirationMinutes = 15) => "stub-token";
            public bool ValidateExpiringDocumentToken(string documentKey, string token, int requestingUserId, string userRole) => true;
            public string GetPrivateDocumentPhysicalPath(string documentKey) => "stub-path";
        }

        private class FormFileStub : IFormFile
        {
            private readonly string _fileName;
            private readonly long _length;

            public FormFileStub(string fileName, long length)
            {
                _fileName = fileName;
                _length = length;
            }

            public string ContentType => "application/octet-stream";
            public string ContentDisposition => $"form-data; name=\"logoFile\"; filename=\"{_fileName}\"";
            public IHeaderDictionary Headers => new HeaderDictionary();
            public long Length => _length;
            public string Name => "logoFile";
            public string FileName => _fileName;

            public void CopyTo(Stream target) { }
            public Task CopyToAsync(Stream target, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Stream OpenReadStream() => new MemoryStream(new byte[16]);
        }
    }
}
