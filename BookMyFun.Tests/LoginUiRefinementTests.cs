using System.ComponentModel.DataAnnotations;
using System.Reflection;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookMyFun.Tests
{
    public class LoginUiRefinementTests
    {
        private class StubUserRepo : IUserRepository
        {
            public Task<User?> GetByEmailAsync(string email) => Task.FromResult<User?>(null);
            public Task<User?> GetByIdAsync(int id) => Task.FromResult<User?>(null);
            public Task<User?> GetByPhoneAsync(string phone) => Task.FromResult<User?>(null);
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(new List<User>());
            public Task<int> CreateUserAsync(User user) => Task.FromResult(1);
            public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
            public Task<bool> UpdatePasswordAsync(int userId, string passwordHash) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
            public Task<bool> SetCustomCommissionRateAsync(int userId, decimal? commissionRate) => Task.FromResult(true);
            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult<User?>(null);
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(new List<User>());
            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string tier, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? email = null) => Task.FromResult(true);
        }

        public void LoginViewModel_RejectsNonEmailFormat()
        {
            var invalidModel1 = new LoginViewModel
            {
                Email = "100245", // Numeric User ID
                Password = "Password123!"
            };
            var context1 = new ValidationContext(invalidModel1) { MemberName = nameof(LoginViewModel.Email) };
            var results1 = new List<ValidationResult>();
            bool isValid1 = Validator.TryValidateProperty(invalidModel1.Email, context1, results1);

            if (isValid1)
            {
                throw new Exception("LoginViewModel should have rejected numeric User ID '100245'.");
            }

            var invalidModel2 = new LoginViewModel
            {
                Email = "admin_user", // Plain username / User ID
                Password = "Password123!"
            };
            var context2 = new ValidationContext(invalidModel2) { MemberName = nameof(LoginViewModel.Email) };
            var results2 = new List<ValidationResult>();
            bool isValid2 = Validator.TryValidateProperty(invalidModel2.Email, context2, results2);

            if (isValid2)
            {
                throw new Exception("LoginViewModel should have rejected plain username 'admin_user'.");
            }

            var validModel = new LoginViewModel
            {
                Email = "guest@bookmyfun.in",
                Password = "Password123!"
            };
            var contextValid = new ValidationContext(validModel) { MemberName = nameof(LoginViewModel.Email) };
            var resultsValid = new List<ValidationResult>();
            bool isValidValid = Validator.TryValidateProperty(validModel.Email, contextValid, resultsValid);

            if (!isValidValid)
            {
                throw new Exception($"LoginViewModel rejected valid email: {resultsValid.FirstOrDefault()?.ErrorMessage}");
            }
        }

        public void LoginViewModel_RequiresEmailAddress()
        {
            var emptyModel = new LoginViewModel
            {
                Email = "",
                Password = "Password123!"
            };
            var context = new ValidationContext(emptyModel) { MemberName = nameof(LoginViewModel.Email) };
            var results = new List<ValidationResult>();
            bool isValid = Validator.TryValidateProperty(emptyModel.Email, context, results);

            if (isValid)
            {
                throw new Exception("LoginViewModel should have rejected empty Email.");
            }

            var requiredError = results.FirstOrDefault()?.ErrorMessage;
            if (requiredError == null || !requiredError.Contains("email", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Expected email-focused error message, but got: '{requiredError}'");
            }
            if (requiredError.Contains("User ID", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Error message still contains 'User ID': '{requiredError}'");
            }
        }

        public void LoginViewModel_MetadataHasNoUserIdRemnants()
        {
            var property = typeof(LoginViewModel).GetProperty(nameof(LoginViewModel.Email));
            if (property == null)
            {
                throw new Exception("LoginViewModel does not have Email property.");
            }

            var displayAttr = property.GetCustomAttribute<DisplayAttribute>();
            if (displayAttr == null || displayAttr.Name != "Email Address")
            {
                throw new Exception($"Expected Display Name 'Email Address', but got: '{displayAttr?.Name}'");
            }

            var emailAttr = property.GetCustomAttribute<EmailAddressAttribute>();
            if (emailAttr == null)
            {
                throw new Exception("LoginViewModel.Email must be decorated with [EmailAddress].");
            }

            var requiredAttr = property.GetCustomAttribute<RequiredAttribute>();
            if (requiredAttr == null || requiredAttr.ErrorMessage?.Contains("User ID", StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new Exception($"LoginViewModel.Email RequiredAttribute contains User ID: '{requiredAttr?.ErrorMessage}'");
            }
        }

        public void LoginView_TemplateIsStandardizedOnEmailOnly()
        {
            var viewPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Views", "Account", "Login.cshtml");
            if (!File.Exists(viewPath))
            {
                viewPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Account", "Login.cshtml");
            }
            if (!File.Exists(viewPath))
            {
                // Fallback for bin directory execution
                viewPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Account", "Login.cshtml"));
            }

            if (!File.Exists(viewPath))
            {
                throw new FileNotFoundException($"Cannot locate Login.cshtml at '{viewPath}'.");
            }

            var html = File.ReadAllText(viewPath);

            if (html.Contains("Email or User ID", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Login.cshtml still contains 'Email or User ID'.");
            }

            if (html.Contains("User ID", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Login.cshtml still contains 'User ID'.");
            }

            if (html.Contains("or phone", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Login.cshtml still contains 'or phone' in the email input.");
            }

            if (!html.Contains("type=\"email\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Login.cshtml does not set type=\"email\" on the email input.");
            }

            if (!html.Contains("bi-envelope", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Login.cshtml should display the bi-envelope icon for the email field.");
            }

            if (!html.Contains("placeholder=\"name@example.com\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Login.cshtml does not contain the standardized email placeholder 'name@example.com'.");
            }
        }

        public async Task AccountController_LoginFailedMessage_StandardizedOnEmailOnly()
        {
            var userRepo = new StubUserRepo();
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
                null!, // schemeProvider
                NullLogger<AccountController>.Instance
            );

            var result = await controller.Login(new LoginViewModel
            {
                Email = "test@bookmyfun.in",
                Password = "WrongPassword"
            }) as ViewResult;

            if (result == null)
            {
                throw new Exception("Expected ViewResult from failed Login.");
            }

            var modelErrors = controller.ModelState[string.Empty]?.Errors;
            var errorMessage = modelErrors?.FirstOrDefault()?.ErrorMessage;

            if (errorMessage == null)
            {
                throw new Exception("Expected model error on failed login, but found none.");
            }

            if (errorMessage.Contains("user id", StringComparison.OrdinalIgnoreCase) || 
                errorMessage.Contains("/id", StringComparison.OrdinalIgnoreCase) || 
                errorMessage.Contains("email/id", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Login failure message still mentions 'ID': '{errorMessage}'");
            }

            if (!errorMessage.Contains("Invalid email or password", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Expected 'Invalid email or password.', but got: '{errorMessage}'");
            }
        }
    }
}
