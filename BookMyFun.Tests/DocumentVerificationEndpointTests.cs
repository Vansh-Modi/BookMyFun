using System;
using System.IO;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class DocumentVerificationEndpointTests
    {
        private class FakeMediaStorageService : IMediaStorageService
        {
            public Task<(string publicUrl, string publicId)> SaveMediaFileAsync(IFormFile file, string mediaType) =>
                Task.FromResult(("/uploads/sample.jpg", "sample_id"));

            public Task<string> SavePrivateDocumentAsync(IFormFile file, int clientId) =>
                Task.FromResult($"doc_client_{clientId}_test.pdf");

            public Task<bool> ValidateExternalUrlAsync(string url) => Task.FromResult(true);

            public string GenerateExpiringDocumentToken(string documentKey, int requestingUserId, string userRole, int expirationMinutes = 15) =>
                "valid_test_token";

            public bool ValidateExpiringDocumentToken(string documentKey, string token, int requestingUserId, string userRole) =>
                token == "valid_test_token";

            public string GetPrivateDocumentPhysicalPath(string documentKey) =>
                Path.Combine(Path.GetTempPath(), "BMF_Vault_NonExistent", documentKey);
        }

        [Fact]
        public void ViewPrivateDocument_AttributesAndRoleRestrictions_AreCorrect()
        {
            var controllerType = typeof(MediaController);

            // API Route checks
            var apiAttr = controllerType.GetCustomAttribute<ApiControllerAttribute>();
            Assert.NotNull(apiAttr);

            var routeAttr = controllerType.GetCustomAttribute<RouteAttribute>();
            Assert.NotNull(routeAttr);
            Assert.Equal("api/[controller]", routeAttr.Template);

            var method = controllerType.GetMethod(nameof(MediaController.ViewPrivateDocument));
            Assert.NotNull(method);

            var actionRoute = method.GetCustomAttribute<HttpGetAttribute>();
            Assert.NotNull(actionRoute);
            Assert.Equal("view-id-document", actionRoute.Template);

            var authAttr = method.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authAttr);
            Assert.Contains("Admin", authAttr.Roles);
            Assert.Contains("Employee", authAttr.Roles);
            Assert.Contains("Client", authAttr.Roles);

            // Owner must NOT be permitted to access private identity documents
            Assert.DoesNotContain("Owner", authAttr.Roles);
        }

        [Fact]
        public async Task ViewPrivateDocument_WhenFileMissing_ReturnsFriendlySvg_NotRawPath()
        {
            var fakeStorage = new FakeMediaStorageService();
            var logger = NullLogger<MediaController>.Instance;
            var controller = new MediaController(fakeStorage, null!, null!, logger, null);

            // Setup ControllerContext for Admin / Employee
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "99"),
                new Claim(ClaimTypes.Role, "Employee")
            }, "TestAuth"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };

            var result = await controller.ViewPrivateDocument("doc_client_5_identity.pdf", "valid_test_token");

            // Must return a FileContentResult with SVG image/svg+xml
            var fileResult = Assert.IsType<FileContentResult>(result);
            Assert.Equal("image/svg+xml", fileResult.ContentType);

            var svgContent = System.Text.Encoding.UTF8.GetString(fileResult.FileContents);
            Assert.Contains("Document File Not Found in Vault", svgContent);
            Assert.Contains("Reference Key: doc_client_5_identity.pdf", svgContent);
        }
    }
}
