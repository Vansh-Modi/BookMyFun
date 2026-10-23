using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookMyFun.Tests
{
    public class GoogleOAuthConfigTests
    {
        private class SimpleTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class InMemoryUserRepo : IUserRepository
        {
            public readonly Dictionary<string, User> UsersByEmail = new(StringComparer.OrdinalIgnoreCase);
            private int _idCounter = 100;

            public Task<User?> GetByEmailAsync(string email)
            {
                UsersByEmail.TryGetValue(email, out var user);
                return Task.FromResult(user);
            }

            public Task<User?> GetByIdAsync(int id)
            {
                var user = UsersByEmail.Values.FirstOrDefault(u => u.UserID == id);
                return Task.FromResult(user);
            }

            public Task<User?> GetByPhoneAsync(string phone) => Task.FromResult<User?>(null);
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(UsersByEmail.Values.Where(u => u.Role == role).ToList());
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(UsersByEmail.Values.ToList());

            public Task<int> CreateUserAsync(User user)
            {
                user.UserID = ++_idCounter;
                UsersByEmail[user.Email] = user;
                return Task.FromResult(user.UserID);
            }

            public Task<bool> UpdateUserAsync(User user)
            {
                UsersByEmail[user.Email] = user;
                return Task.FromResult(true);
            }

            public Task<bool> UpdatePasswordAsync(int userId, string passwordHash) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
            public Task<bool> SetCustomCommissionRateAsync(int userId, decimal? commissionRate) => Task.FromResult(true);

            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey)
            {
                var user = UsersByEmail.Values.FirstOrDefault(u => u.AuthProvider == provider && u.AuthProviderKey == providerKey);
                return Task.FromResult(user);
            }

            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string tier, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate) => Task.FromResult(true);

            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null)
            {
                var user = UsersByEmail.Values.FirstOrDefault(u => u.UserID == userId);
                if (user != null)
                {
                    user.AuthProvider = provider;
                    user.AuthProviderKey = providerKey;
                    if (!string.IsNullOrEmpty(profilePic)) user.ProfilePictureUrl = profilePic;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
        }

        private class StubConfigRepo : IConfigRepository
        {
            public ApiConfiguration? ConfigToReturn { get; set; }

            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider)
            {
                if (string.Equals(provider, "Google", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(ConfigToReturn);
                }
                return Task.FromResult<ApiConfiguration?>(null);
            }

            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
            public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details) => Task.CompletedTask;
        }

        private class TestAuthService : IAuthenticationService
        {
            public ClaimsPrincipal? SignedInPrincipal { get; private set; }
            public string? SignedInScheme { get; private set; }

            public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
                Task.FromResult(AuthenticateResult.NoResult());

            public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
                Task.CompletedTask;

            public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
                Task.CompletedTask;

            public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
            {
                SignedInScheme = scheme;
                SignedInPrincipal = principal;
                return Task.CompletedTask;
            }

            public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
                Task.CompletedTask;
        }

        private static (AccountController controller, TestAuthService authService) CreateController(
            IUserRepository userRepo, 
            IConfigRepository? configRepo = null, 
            IConfiguration? configuration = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRouting();
            services.AddControllersWithViews();
            var authService = new TestAuthService();
            services.AddSingleton<IAuthenticationService>(authService);
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
            var serviceProvider = services.BuildServiceProvider();
            var schemeProvider = serviceProvider.GetRequiredService<IAuthenticationSchemeProvider>();

            var controller = new AccountController(
                userRepo,
                null!, // otpRepo
                null!, // ownerReqRepo
                null!, // partnerLeadService
                null!, // emailService
                null!, // whatsAppService
                null!, // bookingRepo
                null!, // propRepo
                null!, // idRepo
                schemeProvider,
                NullLogger<AccountController>.Instance,
                configRepo,
                configuration);

            var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData()
            };
            controller.TempData = new TempDataDictionary(httpContext, new SimpleTempDataProvider());
            return (controller, authService);
        }

        public async Task DirectGoogleLogin_ActiveProjectEmail_AuthenticatesAndLinksProvider()
        {
            var userRepo = new InMemoryUserRepo();
            var (controller, authService) = CreateController(userRepo);

            string activeProjectEmail = "showsgarage@gmail.com";
            string projectFullName = "BookMyFun Project Admin";

            var model = new ExternalLoginPromptViewModel
            {
                Email = activeProjectEmail,
                FullName = projectFullName,
                Provider = "Google",
                ActiveProjectEmail = activeProjectEmail
            };

            var result = await controller.DirectGoogleLogin(model);

            if (result is not RedirectToActionResult redirect)
            {
                throw new Exception($"Expected RedirectToActionResult from DirectGoogleLogin, got {result.GetType().Name}");
            }

            if (redirect.ControllerName != "Client" || redirect.ActionName != "Index")
            {
                throw new Exception($"Expected redirect to Client/Index, got {redirect.ControllerName}/{redirect.ActionName}");
            }

            // Verify user was registered and linked to Google AuthProvider
            var createdUser = await userRepo.GetByEmailAsync(activeProjectEmail);
            if (createdUser == null)
            {
                throw new Exception($"User with active project email {activeProjectEmail} was not found in repository.");
            }

            if (createdUser.AuthProvider != "Google")
            {
                throw new Exception($"Expected AuthProvider to be 'Google', got '{createdUser.AuthProvider}'");
            }

            if (!createdUser.PasswordHash.StartsWith("OAUTH_GOOGLE_"))
            {
                throw new Exception($"Expected OAuth password hash prefix, got '{createdUser.PasswordHash}'");
            }

            // Verify authentication session claims
            if (authService.SignedInPrincipal == null)
            {
                throw new Exception("Expected user to be signed in with ClaimsPrincipal, but SignedInPrincipal was null.");
            }

            var emailClaim = authService.SignedInPrincipal.FindFirstValue(ClaimTypes.Email);
            if (!string.Equals(emailClaim, activeProjectEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Expected email claim '{activeProjectEmail}', got '{emailClaim}'");
            }

            var providerClaim = authService.SignedInPrincipal.FindFirstValue("AuthProvider");
            if (providerClaim != "Google")
            {
                throw new Exception($"Expected AuthProvider claim 'Google', got '{providerClaim}'");
            }
        }

        public async Task DirectGoogleLogin_InvalidModel_RedisplaysPromptWithActiveProjectEmail()
        {
            var userRepo = new InMemoryUserRepo();
            var (controller, _) = CreateController(userRepo);
            controller.ModelState.AddModelError("Email", "Google Email is required.");

            var model = new ExternalLoginPromptViewModel
            {
                Email = "",
                FullName = "Invalid User",
                ActiveProjectEmail = "showsgarage@gmail.com"
            };

            var result = await controller.DirectGoogleLogin(model);

            if (result is not ViewResult viewResult)
            {
                throw new Exception($"Expected ViewResult on invalid ModelState, got {result.GetType().Name}");
            }

            if (viewResult.ViewName != "ExternalLoginPrompt")
            {
                throw new Exception($"Expected view 'ExternalLoginPrompt', got '{viewResult.ViewName}'");
            }

            if (viewResult.Model is not ExternalLoginPromptViewModel vm)
            {
                throw new Exception("Expected ExternalLoginPromptViewModel in ViewResult.Model");
            }

            if (vm.ActiveProjectEmail != "showsgarage@gmail.com")
            {
                throw new Exception($"Expected ActiveProjectEmail 'showsgarage@gmail.com', got '{vm.ActiveProjectEmail}'");
            }
        }

        public async Task ExternalLogin_WhenMiddlewareUnconfigured_SuppliesActiveProjectEmailAndConfig()
        {
            var userRepo = new InMemoryUserRepo();
            var configRepo = new StubConfigRepo
            {
                ConfigToReturn = new ApiConfiguration
                {
                    ServiceProvider = "Google",
                    ApiKey = "test-client-id-456",
                    ApiSecret = "test-client-secret-789",
                    ExtraParams = "{\"ProjectEmail\":\"showsgarage@gmail.com\"}",
                    IsActive = true
                }
            };

            var (controller, _) = CreateController(userRepo, configRepo);
            var result = await controller.ExternalLogin("Google");

            if (result is not ViewResult viewResult)
            {
                throw new Exception($"Expected ViewResult for unconfigured middleware, got {result.GetType().Name}");
            }

            if (viewResult.ViewName != "ExternalLoginPrompt")
            {
                throw new Exception($"Expected view 'ExternalLoginPrompt', got '{viewResult.ViewName}'");
            }

            if (viewResult.Model is not ExternalLoginPromptViewModel vm)
            {
                throw new Exception("Expected ExternalLoginPromptViewModel in ViewResult.Model");
            }

            if (vm.ActiveProjectEmail != "showsgarage@gmail.com")
            {
                throw new Exception($"Expected ActiveProjectEmail to be 'showsgarage@gmail.com', got '{vm.ActiveProjectEmail}'");
            }

            if (vm.ConfiguredClientId != "test-client-id-456")
            {
                throw new Exception($"Expected ConfiguredClientId 'test-client-id-456', got '{vm.ConfiguredClientId}'");
            }

            if (!vm.IsConfiguredInDatabase)
            {
                throw new Exception("Expected IsConfiguredInDatabase to be true");
            }
        }

        public void ExternalLoginPromptView_ContainsActiveProjectEmailAndCredentialBindings()
        {
            string viewPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Account", "ExternalLoginPrompt.cshtml");
            if (!File.Exists(viewPath))
            {
                viewPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "Account", "ExternalLoginPrompt.cshtml"));
            }

            if (!File.Exists(viewPath))
            {
                viewPath = @"d:\Projects\BookMyFun\Views\Account\ExternalLoginPrompt.cshtml";
            }

            string content = File.ReadAllText(viewPath);

            if (!content.Contains("showsgarage@gmail.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ExternalLoginPrompt.cshtml does not contain the active project email 'showsgarage@gmail.com'.");
            }

            if (!content.Contains("fillProjectGoogle", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ExternalLoginPrompt.cshtml does not define fillProjectGoogle() JavaScript helper.");
            }

            if (!content.Contains("Active Project Account", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ExternalLoginPrompt.cshtml does not contain the 'Active Project Account' badge.");
            }

            if (content.Contains("demo.google@bookmyfun.in", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("ExternalLoginPrompt.cshtml still contains legacy placeholder 'demo.google@bookmyfun.in'.");
            }
        }

        public void AdminSettingsView_ContainsGoogleOAuthTabAndConfigurationForm()
        {
            string viewPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Admin", "Settings.cshtml");
            if (!File.Exists(viewPath))
            {
                viewPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Views", "Admin", "Settings.cshtml"));
            }

            if (!File.Exists(viewPath))
            {
                viewPath = @"d:\Projects\BookMyFun\Views\Admin\Settings.cshtml";
            }

            string content = File.ReadAllText(viewPath);

            if (!content.Contains("data-bs-target=\"#tabGoogle\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Settings.cshtml does not contain Google OAuth tab pill button.");
            }

            if (!content.Contains("id=\"tabGoogle\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Settings.cshtml does not contain #tabGoogle pane.");
            }

            if (!content.Contains("name=\"ServiceProvider\" value=\"Google\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Settings.cshtml does not submit ServiceProvider='Google' in API config form.");
            }

            if (!content.Contains("showsgarage@gmail.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Settings.cshtml does not reference active project email 'showsgarage@gmail.com'.");
            }
        }
    }
}
