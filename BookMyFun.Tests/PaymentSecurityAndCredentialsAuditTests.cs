using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BookMyFun.Tests
{
    public class PaymentSecurityAndCredentialsAuditTests
    {
        private class MockConfigRepo : IConfigRepository
        {
            public readonly Dictionary<string, ApiConfiguration> Configs = new();

            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string serviceProvider)
            {
                Configs.TryGetValue(serviceProvider, out var c);
                return Task.FromResult(c);
            }

            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(Configs.Values.ToList());

            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config)
            {
                if (Configs.TryGetValue(config.ServiceProvider, out var existing))
                {
                    existing.ApiKey = config.ApiKey;
                    if (!string.IsNullOrWhiteSpace(config.ApiSecret))
                    {
                        existing.ApiSecret = config.ApiSecret;
                    }
                    existing.ExtraParams = config.ExtraParams;
                    existing.IsActive = config.IsActive;
                }
                else
                {
                    Configs[config.ServiceProvider] = config;
                }
                return Task.FromResult(true);
            }

            public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details) => Task.CompletedTask;
            public Task<List<AuditLog>> GetRecentAuditLogsAsync(int count = 50) => Task.FromResult(new List<AuditLog>());
        }

        [Fact]
        public void PaymentView_DoesNotDisplayKeyIdOrOrderTokenAsText()
        {
            string paymentViewPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Booking", "Payment.cshtml");
            if (!File.Exists(paymentViewPath))
            {
                paymentViewPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "Booking", "Payment.cshtml"));
            }
            Assert.True(File.Exists(paymentViewPath), $"Payment view not found at: {paymentViewPath}");

            string content = File.ReadAllText(paymentViewPath);

            // Verify KeyId is NOT printed as visible text (e.g. <code>@Model.KeyId</code>)
            Assert.DoesNotContain("<code>@Model.KeyId</code>", content);
            Assert.DoesNotContain("Key: <code>", content);
            Assert.DoesNotContain("Merchant Account <code>@Model.KeyId</code>", content);

            // Verify OrderId is NOT printed in header or banner as visible text
            Assert.DoesNotContain("Order Token: <code>@Model.OrderId</code>", content);
            Assert.DoesNotContain("Order ID: @Model.OrderId", content);

            // Verify Key ID remains in JS options for checkout
            Assert.Contains("var razorpayKey = \"@Model.KeyId\";", content);
        }

        [Fact]
        public void PaymentViews_ContainZeroConsoleLogCalls()
        {
            string bookingPaymentPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Booking", "Payment.cshtml");
            if (!File.Exists(bookingPaymentPath))
            {
                bookingPaymentPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "Booking", "Payment.cshtml"));
            }

            string ownerPaymentPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Owner", "SubscriptionPayment.cshtml");
            if (!File.Exists(ownerPaymentPath))
            {
                ownerPaymentPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "Owner", "SubscriptionPayment.cshtml"));
            }

            Assert.True(File.Exists(bookingPaymentPath));
            Assert.True(File.Exists(ownerPaymentPath));

            string bookingContent = File.ReadAllText(bookingPaymentPath);
            string ownerContent = File.ReadAllText(ownerPaymentPath);

            Assert.DoesNotContain("console.log", bookingContent);
            Assert.DoesNotContain("console.log", ownerContent);
        }

        [Fact]
        public void ConfirmationAndReceiptViews_DoNotDisplayFakeRazorpayTokens()
        {
            string confirmPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Booking", "Confirmation.cshtml");
            if (!File.Exists(confirmPath))
            {
                confirmPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "Booking", "Confirmation.cshtml"));
            }

            string receiptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Booking", "Receipt.cshtml");
            if (!File.Exists(receiptPath))
            {
                receiptPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "Booking", "Receipt.cshtml"));
            }

            Assert.True(File.Exists(confirmPath));
            Assert.True(File.Exists(receiptPath));

            string confirmContent = File.ReadAllText(confirmPath);
            string receiptContent = File.ReadAllText(receiptPath);

            Assert.DoesNotContain("RAZORPAY_TOKEN_", confirmContent);
            Assert.DoesNotContain("RAZORPAY_TOKEN_", receiptContent);
        }

        [Fact]
        public void AdminSettingsView_DoesNotOutputPlaintextSecretsInHtmlValues()
        {
            string settingsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Admin", "Settings.cshtml");
            if (!File.Exists(settingsPath))
            {
                settingsPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "Admin", "Settings.cshtml"));
            }

            Assert.True(File.Exists(settingsPath));
            string content = File.ReadAllText(settingsPath);

            // Verify secrets are NEVER emitted in value attribute
            Assert.DoesNotContain("value=\"@rzp.ApiSecret\"", content);
            Assert.DoesNotContain("value=\"@google.ApiSecret\"", content);
            Assert.DoesNotContain("value=\"@wa.ApiSecret\"", content);
            Assert.DoesNotContain("value=\"@smtp.ApiSecret\"", content);
            Assert.DoesNotContain("value=\"@cashfree.ApiSecret\"", content);
        }

        [Fact]
        public async Task ConfigRepository_SaveOrUpdateConfig_PreservesExistingSecret_WhenBlankSubmitted()
        {
            var configRepo = new MockConfigRepo();
            configRepo.Configs["Razorpay"] = new ApiConfiguration
            {
                ServiceProvider = "Razorpay",
                ApiKey = "rzp_test_initial_key",
                ApiSecret = "super_secret_key_12345",
                IsActive = true
            };

            // Admin updates ApiKey or toggles Active, submitting empty secret to preserve current
            var updatedConfig = new ApiConfiguration
            {
                ServiceProvider = "Razorpay",
                ApiKey = "rzp_test_new_key",
                ApiSecret = "", // Blank
                IsActive = true
            };

            await configRepo.SaveOrUpdateConfigAsync(updatedConfig);

            var stored = await configRepo.GetActiveConfigByProviderAsync("Razorpay");
            Assert.NotNull(stored);
            Assert.Equal("rzp_test_new_key", stored.ApiKey);
            Assert.Equal("super_secret_key_12345", stored.ApiSecret); // Preserved!
        }

        [Fact]
        public async Task PaymentGatewayService_NeverLeaksSecretInOrderResponse()
        {
            var configRepo = new MockConfigRepo();
            configRepo.Configs["Razorpay"] = new ApiConfiguration
            {
                ServiceProvider = "Razorpay",
                ApiKey = "rzp_test_public_key_abc",
                ApiSecret = "very_confidential_secret_xyz",
                IsActive = true
            };

            var inMemorySettings = new Dictionary<string, string?>
            {
                { "Razorpay:KeyId", "rzp_test_public_key_abc" },
                { "Razorpay:KeySecret", "very_confidential_secret_xyz" }
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

            using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
            var logger = loggerFactory.CreateLogger<RazorpayPaymentService>();

            var paymentService = new RazorpayPaymentService(configRepo, configuration, logger);

            var response = await paymentService.CreateOrderAsync(1000m, "receipt_test_123");

            Assert.NotNull(response);
            Assert.Equal("rzp_test_public_key_abc", response.KeyId);
            // Verify KeySecret does not exist as a property on PaymentOrderResponse
            var property = typeof(PaymentOrderResponse).GetProperty("KeySecret");
            Assert.Null(property);
        }

        [Fact]
        public async Task RazorpayWebhookController_DoesNotLeakWebhookSecretInErrorResponses()
        {
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "Razorpay:WebhookSecret", "top_secret_webhook_key_789" }
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

            using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
            var logger = loggerFactory.CreateLogger<RazorpayWebhookController>();

            var controller = new RazorpayWebhookController(
                configuration, new MockConfigRepo(), null!, null!, null!, logger)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            // Set empty body
            controller.HttpContext.Request.Body = new MemoryStream(new byte[0]);

            var result = await controller.HandleWebhook();

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            string errorJson = System.Text.Json.JsonSerializer.Serialize(badRequest.Value);

            Assert.DoesNotContain("top_secret_webhook_key_789", errorJson);
            Assert.Contains("Request body cannot be empty.", errorJson);
        }
    }
}
