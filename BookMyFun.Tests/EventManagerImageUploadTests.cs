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
    public class EventManagerImageUploadTests
    {
        private class TestTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class StubEventPackageRepo : IEventPackageRepository
        {
            public EventPackage? LastCreatedPackage { get; private set; }
            public EventPackage? LastUpdatedPackage { get; private set; }
            public string? LastUpdatedSetupProofUrls { get; private set; }
            public string? LastUpdatedSetupStatus { get; private set; }

            public EventPackage ExistingPackage { get; set; } = new EventPackage
            {
                PackageID = 10,
                Name = "Existing Package",
                Category = "Birthday",
                BasePrice = 15000,
                MediaUrl = "https://images.unsplash.com/existing-package",
                IsActive = true
            };

            public Task<List<EventPackage>> GetAllPackagesAsync() => Task.FromResult(new List<EventPackage> { ExistingPackage });
            public Task<List<EventPackage>> GetActivePackagesAsync() => Task.FromResult(new List<EventPackage> { ExistingPackage });
            public Task<EventPackage?> GetPackageByIdAsync(int packageId) => Task.FromResult<EventPackage?>(ExistingPackage);

            public Task<int> CreatePackageAsync(EventPackage package)
            {
                LastCreatedPackage = package;
                return Task.FromResult(99);
            }

            public Task<bool> UpdatePackageAsync(EventPackage package)
            {
                LastUpdatedPackage = package;
                return Task.FromResult(true);
            }

            public Task<bool> UpdatePackageCommissionRateAsync(int packageId, decimal? commissionRate) => Task.FromResult(true);
            public Task<bool> DeletePackageAsync(int packageId) => Task.FromResult(true);
            public Task<List<Booking>> GetAssignedEventBookingsAsync(int decoratorUserId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllEventBookingsAsync() => Task.FromResult(new List<Booking>());

            public Task<bool> UpdateSetupStatusAsync(int bookingId, string setupStatus, string? setupProofMediaUrls, int? decoratorId)
            {
                LastUpdatedSetupStatus = setupStatus;
                LastUpdatedSetupProofUrls = setupProofMediaUrls;
                return Task.FromResult(true);
            }

            public Task<bool> AssignDecoratorAsync(int bookingId, int decoratorUserId) => Task.FromResult(true);
            public Task<bool> CancelOrRemoveEventFromBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<List<User>> GetDecoratorsAsync() => Task.FromResult(new List<User>());
            public Task<List<string>> GetAllDistinctPropertyLocationsAsync() => Task.FromResult(new List<string>());
            public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>("Mumbai");
            public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas) => Task.FromResult(true);
            public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(true);
        }

        private class StubMediaStorageService : IMediaStorageService
        {
            public int UploadCount { get; private set; }
            public List<string> UploadedFileNames { get; } = new();

            public Task<(string publicUrl, string publicId)> SaveMediaFileAsync(IFormFile file, string mediaType)
            {
                UploadCount++;
                UploadedFileNames.Add(file.FileName);
                string publicId = Guid.NewGuid().ToString("N");
                string publicUrl = $"/uploads/photos/{publicId}_{file.FileName}";
                return Task.FromResult((publicUrl, publicId));
            }

            public Task<string> SavePrivateDocumentAsync(IFormFile file, int clientId) => Task.FromResult("/private/doc.pdf");
            public Task<bool> ValidateExternalUrlAsync(string url) => Task.FromResult(true);
            public string GenerateExpiringDocumentToken(string documentKey, int requestingUserId, string userRole, int expirationMinutes = 15) => "token";
            public bool ValidateExpiringDocumentToken(string documentKey, string token, int requestingUserId, string userRole) => true;
            public string GetPrivateDocumentPhysicalPath(string documentKey) => documentKey;
        }

        private class StubConfigRepo : IConfigRepository
        {
            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
            public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details) => Task.CompletedTask;
        }

        private static EventManagerController CreateController(
            StubEventPackageRepo eventRepo,
            StubMediaStorageService mediaStorage,
            StubConfigRepo configRepo)
        {
            var controller = new EventManagerController(
                eventRepo,
                mediaStorage,
                configRepo,
                NullLogger<EventManagerController>.Instance
            );

            var httpContext = new DefaultHttpContext();
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "42"),
                new Claim(ClaimTypes.Role, "EventManager"),
                new Claim(ClaimTypes.Name, "Event Specialist")
            };
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

            return controller;
        }

        private static IFormFile CreateDummyFormFile(string fileName, string contentType = "image/jpeg")
        {
            var content = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }; // Fake JPEG header
            var stream = new MemoryStream(content);
            return new FormFile(stream, 0, content.Length, "ImageFile", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }

        public async Task CreatePackage_WithDirectImageUpload_SavesFileViaMediaStorageAndSetsMediaUrl()
        {
            var eventRepo = new StubEventPackageRepo();
            var mediaStorage = new StubMediaStorageService();
            var configRepo = new StubConfigRepo();
            var controller = CreateController(eventRepo, mediaStorage, configRepo);

            var file = CreateDummyFormFile("bohemian_stage.jpg");
            var model = new EventPackageCreateViewModel
            {
                Name = "Royal Bohemian Theme",
                Category = "Wedding",
                BasePrice = 35000,
                Description = "Luxury floral and wooden staging",
                ImageFile = file,
                MediaUrl = null,
                IsActive = true
            };

            var result = await controller.CreatePackage(model) as RedirectToActionResult;

            if (result == null || result.ActionName != nameof(EventManagerController.Packages))
            {
                throw new Exception($"Expected redirect to Packages, but got {result?.ActionName}.");
            }

            if (mediaStorage.UploadCount != 1)
            {
                throw new Exception($"Expected 1 media storage upload, but got {mediaStorage.UploadCount}.");
            }

            if (eventRepo.LastCreatedPackage == null)
            {
                throw new Exception("CreatePackageAsync was not called.");
            }

            if (string.IsNullOrEmpty(eventRepo.LastCreatedPackage.MediaUrl) || !eventRepo.LastCreatedPackage.MediaUrl.Contains("bohemian_stage.jpg"))
            {
                throw new Exception($"Created package did not receive uploaded image URL: '{eventRepo.LastCreatedPackage.MediaUrl}'.");
            }
        }

        public async Task CreatePackage_WithExternalUrl_UsesMediaUrlDirectly()
        {
            var eventRepo = new StubEventPackageRepo();
            var mediaStorage = new StubMediaStorageService();
            var configRepo = new StubConfigRepo();
            var controller = CreateController(eventRepo, mediaStorage, configRepo);

            var externalUrl = "https://images.unsplash.com/photo-1519741497674-611481863552";
            var model = new EventPackageCreateViewModel
            {
                Name = "Classic Fairy Lights Package",
                Category = "Birthday",
                BasePrice = 18000,
                ImageFile = null,
                MediaUrl = externalUrl,
                IsActive = true
            };

            var result = await controller.CreatePackage(model) as RedirectToActionResult;

            if (result == null || result.ActionName != nameof(EventManagerController.Packages))
            {
                throw new Exception("Expected redirect to Packages.");
            }

            if (mediaStorage.UploadCount != 0)
            {
                throw new Exception($"Expected 0 file uploads for external URL, but got {mediaStorage.UploadCount}.");
            }

            if (eventRepo.LastCreatedPackage?.MediaUrl != externalUrl)
            {
                throw new Exception($"Created package MediaUrl does not match external URL: '{eventRepo.LastCreatedPackage?.MediaUrl}'.");
            }
        }

        public async Task EditPackage_WithDirectImageUpload_ReplacesMediaUrl()
        {
            var eventRepo = new StubEventPackageRepo();
            var mediaStorage = new StubMediaStorageService();
            var configRepo = new StubConfigRepo();
            var controller = CreateController(eventRepo, mediaStorage, configRepo);

            var file = CreateDummyFormFile("new_celebration_stage.webp", "image/webp");
            var model = new EventPackageEditViewModel
            {
                PackageID = 10,
                Name = "Updated Bohemian Theme",
                Category = "Anniversary",
                BasePrice = 28000,
                ImageFile = file,
                MediaUrl = "https://images.unsplash.com/old-url",
                IsActive = true
            };

            var result = await controller.EditPackage(model) as RedirectToActionResult;

            if (result == null || result.ActionName != nameof(EventManagerController.Packages))
            {
                throw new Exception("Expected redirect to Packages.");
            }

            if (mediaStorage.UploadCount != 1)
            {
                throw new Exception($"Expected 1 upload on edit with file, got {mediaStorage.UploadCount}.");
            }

            if (eventRepo.LastUpdatedPackage == null || !eventRepo.LastUpdatedPackage.MediaUrl!.Contains("new_celebration_stage.webp"))
            {
                throw new Exception($"Updated package did not receive newly uploaded image URL: '{eventRepo.LastUpdatedPackage?.MediaUrl}'.");
            }
        }

        public async Task EditPackage_WithoutNewUpload_PreservesExistingMediaUrl()
        {
            var eventRepo = new StubEventPackageRepo();
            var mediaStorage = new StubMediaStorageService();
            var configRepo = new StubConfigRepo();
            var controller = CreateController(eventRepo, mediaStorage, configRepo);

            var model = new EventPackageEditViewModel
            {
                PackageID = 10,
                Name = "Updated Bohemian Theme",
                Category = "Anniversary",
                BasePrice = 29000,
                ImageFile = null,
                MediaUrl = "https://images.unsplash.com/custom-cdn-image.jpg",
                IsActive = true
            };

            var result = await controller.EditPackage(model) as RedirectToActionResult;

            if (result == null || result.ActionName != nameof(EventManagerController.Packages))
            {
                throw new Exception("Expected redirect to Packages.");
            }

            if (mediaStorage.UploadCount != 0)
            {
                throw new Exception($"Expected 0 file uploads, got {mediaStorage.UploadCount}.");
            }

            if (eventRepo.LastUpdatedPackage?.MediaUrl != "https://images.unsplash.com/custom-cdn-image.jpg")
            {
                throw new Exception($"Updated package did not preserve MediaUrl: '{eventRepo.LastUpdatedPackage?.MediaUrl}'.");
            }
        }

        public async Task UpdateSetupStatus_WithProofFiles_UploadsAndMergesProofUrls()
        {
            var eventRepo = new StubEventPackageRepo();
            var mediaStorage = new StubMediaStorageService();
            var configRepo = new StubConfigRepo();
            var controller = CreateController(eventRepo, mediaStorage, configRepo);

            var file1 = CreateDummyFormFile("stage_completed_1.jpg");
            var file2 = CreateDummyFormFile("lighting_completed_2.png", "image/png");

            var model = new SetupStatusUpdateViewModel
            {
                BookingID = 104,
                SetupStatus = "Completed",
                SetupProofMediaUrls = "https://drive.google.com/stage_doc",
                ProofFiles = new List<IFormFile> { file1, file2 }
            };

            await controller.UpdateSetupStatus(model);

            if (mediaStorage.UploadCount != 2)
            {
                throw new Exception($"Expected 2 proof file uploads, but got {mediaStorage.UploadCount}.");
            }

            if (eventRepo.LastUpdatedSetupProofUrls == null)
            {
                throw new Exception("Setup proof URLs were not updated.");
            }

            if (!eventRepo.LastUpdatedSetupProofUrls.Contains("stage_completed_1.jpg") ||
                !eventRepo.LastUpdatedSetupProofUrls.Contains("lighting_completed_2.png") ||
                !eventRepo.LastUpdatedSetupProofUrls.Contains("https://drive.google.com/stage_doc"))
            {
                throw new Exception($"Merged proof URLs did not combine direct uploads and existing URLs: '{eventRepo.LastUpdatedSetupProofUrls}'.");
            }
        }

        public void Views_CreateAndEditPackage_DeclareMultipartEnctypeAndImageFileInputs()
        {
            var basePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "EventManager");
            var createViewPath = Path.GetFullPath(Path.Combine(basePath, "CreatePackage.cshtml"));
            var editViewPath = Path.GetFullPath(Path.Combine(basePath, "EditPackage.cshtml"));

            if (!File.Exists(createViewPath))
            {
                throw new FileNotFoundException($"Cannot find CreatePackage.cshtml at {createViewPath}");
            }
            if (!File.Exists(editViewPath))
            {
                throw new FileNotFoundException($"Cannot find EditPackage.cshtml at {editViewPath}");
            }

            var createHtml = File.ReadAllText(createViewPath);
            var editHtml = File.ReadAllText(editViewPath);

            if (!createHtml.Contains("enctype=\"multipart/form-data\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("CreatePackage.cshtml form is missing enctype=\"multipart/form-data\".");
            }
            if (!createHtml.Contains("asp-for=\"ImageFile\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("CreatePackage.cshtml is missing asp-for=\"ImageFile\" input.");
            }
            if (!createHtml.Contains("asp-for=\"MediaUrl\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("CreatePackage.cshtml is missing asp-for=\"MediaUrl\" input.");
            }

            if (!editHtml.Contains("enctype=\"multipart/form-data\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("EditPackage.cshtml form is missing enctype=\"multipart/form-data\".");
            }
            if (!editHtml.Contains("asp-for=\"ImageFile\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("EditPackage.cshtml is missing asp-for=\"ImageFile\" input.");
            }
            if (!editHtml.Contains("asp-for=\"MediaUrl\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("EditPackage.cshtml is missing asp-for=\"MediaUrl\" input.");
            }
        }
    }
}
