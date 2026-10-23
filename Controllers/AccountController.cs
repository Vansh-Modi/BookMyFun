using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    public class AccountController : Controller
    {
        private readonly IUserRepository _userRepo;
        private readonly IOtpRepository _otpRepo;
        private readonly IOwnerRequestRepository _ownerReqRepo;
        private readonly IPartnerLeadService _partnerLeadService;
        private readonly IEmailService _emailService;
        private readonly IWhatsAppService _whatsAppService;
        private readonly IBookingRepository _bookingRepo;
        private readonly IPropertyRepository _propRepo;
        private readonly IIdVerificationRepository _idRepo;
        private readonly IAuthenticationSchemeProvider _schemeProvider;
        private readonly ILogger<AccountController> _logger;
        private readonly IConfigRepository? _configRepo;
        private readonly IConfiguration? _configuration;
        private readonly IWebHostEnvironment? _env;

        public AccountController(
            IUserRepository userRepo, 
            IOtpRepository otpRepo,
            IOwnerRequestRepository ownerReqRepo,
            IPartnerLeadService partnerLeadService,
            IEmailService emailService,
            IWhatsAppService whatsAppService,
            IBookingRepository bookingRepo,
            IPropertyRepository propRepo,
            IIdVerificationRepository idRepo,
            IAuthenticationSchemeProvider schemeProvider,
            ILogger<AccountController> logger,
            IConfigRepository? configRepo = null,
            IConfiguration? configuration = null,
            IWebHostEnvironment? env = null)
        {
            _userRepo = userRepo;
            _otpRepo = otpRepo;
            _ownerReqRepo = ownerReqRepo;
            _partnerLeadService = partnerLeadService;
            _emailService = emailService;
            _whatsAppService = whatsAppService;
            _bookingRepo = bookingRepo;
            _propRepo = propRepo;
            _idRepo = idRepo;
            _schemeProvider = schemeProvider;
            _logger = logger;
            _configRepo = configRepo;
            _configuration = configuration;
            _env = env;
        }

        [HttpPost]
        public async Task<IActionResult> SendOtp([FromBody] SendOtpRequestModel request)
        {
            if (string.IsNullOrWhiteSpace(request?.Contact))
            {
                return Json(new { success = false, message = "Please provide an email or phone number." });
            }

            var contact = request.Contact.Trim();

            // Validate purpose-specific constraints
            if (string.Equals(request.Purpose, "Registration", StringComparison.OrdinalIgnoreCase))
            {
                var existingUser = await _userRepo.GetByEmailAsync(contact);
                if (existingUser != null)
                {
                    return Json(new { success = false, message = "An account with this email already exists. Please login." });
                }
            }
            else if (string.Equals(request.Purpose, "ForgotPassword", StringComparison.OrdinalIgnoreCase))
            {
                var existingUser = await _userRepo.GetByEmailAsync(contact);
                if (existingUser == null)
                {
                    return Json(new { success = false, message = "No account registered with this email address." });
                }
            }

            // Generate secure 6-digit numeric OTP
            var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            await _otpRepo.SaveOtpAsync(contact, otp, request.Purpose);

            // Send OTP via Email or WhatsApp depending on contact format
            if (contact.Contains("@"))
            {
                bool sent = await _emailService.SendOtpEmailAsync(contact, otp, request.Purpose);
                if (!sent)
                {
                    _logger.LogError("Failed to deliver OTP email to {Contact} for {Purpose}", contact, request.Purpose);
                    return Json(new { success = false, message = "Unable to dispatch verification email. Please verify your email address or try again later." });
                }
            }
            else
            {
                string msg = $"Your BookMyFun verification code is: *{otp}*. Valid for 10 minutes. Do not share this code.";
                bool sent = await _whatsAppService.SendWhatsAppMessageAsync(contact, msg);
                if (!sent)
                {
                    _logger.LogError("Failed to deliver OTP WhatsApp message to {Contact} for {Purpose}", contact, request.Purpose);
                    return Json(new { success = false, message = "Unable to dispatch verification code via WhatsApp. Please verify your phone number." });
                }
            }

            _logger.LogInformation("Sent OTP to {Contact} for {Purpose}", contact, request.Purpose);

            return Json(new { 
                success = true, 
                message = contact.Contains("@") 
                    ? "Verification code sent to your email! Please check your inbox and enter the 6-digit OTP below." 
                    : "Verification code sent to your mobile number! Please enter the 6-digit OTP below."
            });
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl)
        {
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userRepo.GetByEmailAsync(model.Email.Trim().ToLowerInvariant());
            if (user == null || !VerifyPassword(model.Password, user.PasswordHash))
            {
                ModelState.AddModelError("", "Invalid email or password.");
                return View(model);
            }

            if (!user.IsActive)
            {
                ModelState.AddModelError("", "This account is inactive or has been deactivated. Please contact support.");
                return View(model);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(14) : null
            };
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), authProperties);

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            return user.Role switch
            {
                "Admin" => new RedirectToActionResult("Index", "Admin", null),
                "Employee" => new RedirectToActionResult("Index", "Employee", null),
                "Owner" => new RedirectToActionResult("Index", "Owner", null),
                "EventManager" => new RedirectToActionResult("Index", "EventManager", null),
                _ => new RedirectToActionResult("Index", "Client", null)
            };
        }

        [HttpGet]
        [HttpPost]
        public async Task<IActionResult> ExternalLogin(string provider = "Google", string? returnUrl = null)
        {
            var schemes = await _schemeProvider.GetAllSchemesAsync();
            var isProviderAvailable = schemes.Any(s => string.Equals(s.Name, provider, StringComparison.OrdinalIgnoreCase));

            if (isProviderAvailable)
            {
                var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
                var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
                return Challenge(properties, provider);
            }

            // Resolve active project Google credentials and email
            string activeEmail = "showsgarage@gmail.com";
            string? clientId = null;
            bool isConfiguredInDb = false;

            if (_configRepo != null)
            {
                try
                {
                    var googleConfig = await _configRepo.GetActiveConfigByProviderAsync("Google");
                    if (googleConfig != null)
                    {
                        clientId = googleConfig.ApiKey;
                        isConfiguredInDb = googleConfig.IsActive && !string.IsNullOrWhiteSpace(googleConfig.ApiKey);
                        if (!string.IsNullOrWhiteSpace(googleConfig.ExtraParams))
                        {
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(googleConfig.ExtraParams);
                                if (doc.RootElement.TryGetProperty("ProjectEmail", out var pe) && !string.IsNullOrWhiteSpace(pe.GetString()))
                                {
                                    activeEmail = pe.GetString()!;
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load Google OAuth config from database.");
                }
            }

            if (_configuration != null)
            {
                var cfgEmail = _configuration["Authentication:Google:ProjectEmail"];
                if (!string.IsNullOrWhiteSpace(cfgEmail)) activeEmail = cfgEmail;

                var cfgClientId = _configuration["Authentication:Google:ClientId"];
                if (!string.IsNullOrWhiteSpace(cfgClientId)) clientId ??= cfgClientId;
            }

            // Provider not configured in authentication middleware yet (e.g. initial setup or local)
            // Display interactive direct Google sign-in/registration prompt pre-bound with active project credentials
            return View("ExternalLoginPrompt", new ExternalLoginPromptViewModel
            {
                Provider = provider,
                ReturnUrl = returnUrl,
                ActiveProjectEmail = activeEmail,
                ConfiguredClientId = clientId,
                IsConfiguredInDatabase = isConfiguredInDb
            });
        }

        [HttpGet]
        public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
        {
            if (!string.IsNullOrEmpty(remoteError))
            {
                TempData["Error"] = $"Error from external authentication provider: {remoteError}";
                return RedirectToAction(nameof(Login));
            }

            var authResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (!authResult.Succeeded || authResult.Principal == null)
            {
                TempData["Error"] = "Unable to read profile information from external login provider.";
                return RedirectToAction(nameof(Login));
            }

            var principal = authResult.Principal;
            var email = principal.FindFirstValue(ClaimTypes.Email);
            var name = principal.FindFirstValue(ClaimTypes.Name);
            var providerKey = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
            var picture = principal.FindFirstValue("picture") ?? principal.FindFirst("urn:google:picture")?.Value;

            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["Error"] = "External authentication did not return an email address.";
                return RedirectToAction(nameof(Login));
            }

            return await ProcessExternalUserLoginAsync(email, name, "Google", providerKey ?? Guid.NewGuid().ToString("N"), picture, returnUrl);
        }

        [NonAction]
        public async Task<IActionResult> DirectGoogleLogin(ExternalLoginPromptViewModel model)
        {
            if (_env != null && !_env.IsDevelopment())
            {
                return new NotFoundResult();
            }

            if (HttpContext?.Request?.Path.HasValue == true && !string.IsNullOrEmpty(HttpContext.Request.Path.Value))
            {
                return new NotFoundResult();
            }

            if (!ModelState.IsValid)
            {
                model.ActiveProjectEmail ??= "showsgarage@gmail.com";
                return View("ExternalLoginPrompt", model);
            }

            return await ProcessExternalUserLoginAsync(
                model.Email, 
                model.FullName, 
                string.IsNullOrWhiteSpace(model.Provider) ? "Google" : model.Provider, 
                "DIRECT_" + Guid.NewGuid().ToString("N")[..12], 
                null, 
                model.ReturnUrl);
        }

        private async Task<IActionResult> ProcessExternalUserLoginAsync(
            string email, 
            string? name, 
            string provider, 
            string providerKey, 
            string? profilePic, 
            string? returnUrl)
        {
            email = email.Trim().ToLowerInvariant();

            // 1. Check if user already exists with this provider key
            var user = await _userRepo.GetByAuthProviderKeyAsync(provider, providerKey);
            if (user == null)
            {
                // 2. Check if user already exists with this email
                user = await _userRepo.GetByEmailAsync(email);
            }

            if (user == null)
            {
                // 3. User does not exist -> Automatically register as Client (Guest)
                var displayName = !string.IsNullOrWhiteSpace(name) ? name.Trim() : email.Split('@')[0];
                var newUser = new User
                {
                    FullName = displayName,
                    Email = email,
                    PasswordHash = "OAUTH_" + provider.ToUpperInvariant() + "_" + Guid.NewGuid().ToString("N"),
                    Role = "Client",
                    IsActive = true,
                    AuthProvider = provider,
                    AuthProviderKey = providerKey,
                    ProfilePictureUrl = profilePic,
                    SubscriptionTier = "Free"
                };

                int newUserId = await _userRepo.CreateUserAsync(newUser);
                if (newUserId <= 0)
                {
                    TempData["Error"] = "Unable to create your account via " + provider + ". Please try again or use standard registration.";
                    return RedirectToAction(nameof(Login));
                }

                user = await _userRepo.GetByIdAsync(newUserId);
            }
            else
            {
                // User exists -> update provider linkage if needed
                if (string.IsNullOrEmpty(user.AuthProviderKey) || user.AuthProvider != provider)
                {
                    await _userRepo.LinkAuthProviderAsync(user.UserID, provider, providerKey, profilePic);
                }

                if (!user.IsActive)
                {
                    TempData["Error"] = "This account is inactive or has been deactivated. Please contact customer support.";
                    return RedirectToAction(nameof(Login));
                }
            }

            // 4. Issue authenticated cookie session
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user!.UserID.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("AuthProvider", provider)
            };

            if (!string.IsNullOrEmpty(user.ProfilePictureUrl))
            {
                claims.Add(new Claim("ProfilePicture", user.ProfilePictureUrl));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14)
            };

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), authProperties);

            TempData["Success"] = $"Welcome {user.FullName}! You have signed in successfully with {provider}.";

            if (!string.IsNullOrEmpty(returnUrl))
            {
                try
                {
                    if (Url != null && Url.IsLocalUrl(returnUrl))
                    {
                        return Redirect(returnUrl);
                    }
                }
                catch { }
            }

            return user.Role switch
            {
                "Admin" => new RedirectToActionResult("Index", "Admin", null),
                "Employee" => new RedirectToActionResult("Index", "Employee", null),
                "Owner" => new RedirectToActionResult("Index", "Owner", null),
                "EventManager" => new RedirectToActionResult("Index", "EventManager", null),
                _ => new RedirectToActionResult("Index", "Client", null)
            };
        }

        [HttpGet]
        public IActionResult OwnerLogin(string? returnUrl) => RedirectToAction(nameof(Login), new { returnUrl });

        [HttpGet]
        public IActionResult EventManagerLogin(string? returnUrl) => RedirectToAction(nameof(Login), new { returnUrl });

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View(new ForgotPasswordViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userRepo.GetByEmailAsync(model.Email.Trim());
            if (user == null)
            {
                ModelState.AddModelError("Email", "No account registered with this email address.");
                return View(model);
            }

            bool isOtpValid = await _otpRepo.ValidateOtpAsync(model.Email.Trim(), model.OtpCode.Trim(), "ForgotPassword");
            if (!isOtpValid)
            {
                ModelState.AddModelError("OtpCode", "Invalid or expired OTP code.");
                return View(model);
            }

            string newHash = HashPassword(model.NewPassword);
            bool updated = await _userRepo.ResetPasswordAsync(model.Email.Trim(), newHash);
            if (updated)
            {
                TempData["Success"] = "Password reset successfully! Please login with your new password.";
                return RedirectToAction("Login");
            }

            ModelState.AddModelError("", "Failed to update password. Please try again.");
            return View(model);
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View(new RegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var existing = await _userRepo.GetByEmailAsync(model.Email);
            if (existing != null)
            {
                ModelState.AddModelError("Email", "An account with this email already exists.");
                return View(model);
            }

            // Verify OTP against email (or phone if preferred)
            var contactToVerify = !string.IsNullOrWhiteSpace(model.Email) ? model.Email.Trim() : model.Phone?.Trim();
            bool isOtpValid = await _otpRepo.ValidateOtpAsync(contactToVerify!, model.OtpCode.Trim(), "Registration");

            if (!isOtpValid)
            {
                ModelState.AddModelError("OtpCode", "Invalid or expired OTP. Please request a new code.");
                return View(model);
            }

            // Security: Public registration is restricted to 'Client' (Guest).
            // Farm Owners must apply via the Partner Lead intake pipeline.
            if (string.Equals(model.Role, "Owner", StringComparison.OrdinalIgnoreCase))
            {
                TempData["InfoMessage"] = "Host onboarding is managed via our Partner application. Please submit your details below.";
                return RedirectToAction("Partner", "Home");
            }

            var user = new User
            {
                FullName = model.FullName,
                Email = model.Email,
                Phone = model.Phone,
                PasswordHash = HashPassword(model.Password),
                Role = "Client"
            };

            int userId = await _userRepo.CreateUserAsync(user);
            if (userId <= 0)
            {
                ModelState.AddModelError("", "Registration failed. Please try again.");
                return View(model);
            }

            TempData["Success"] = "Registration successful! Please login with your credentials.";
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult ActivateHost(string token, string email)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Invalid or missing activation link.";
                return RedirectToAction("Login");
            }

            return View(new ActivateOwnerViewModel
            {
                Token = token,
                Email = email
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActivateHost(ActivateOwnerViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            try
            {
                await _partnerLeadService.ActivateOwnerAccountAsync(model);
                TempData["Success"] = "Host account activated successfully! Please login with your new password.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(model);
            }
        }

        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out int userId))
            {
                return RedirectToAction(nameof(Login));
            }

            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null) return NotFound();

            var vm = new UserProfileViewModel
            {
                UserID = user.UserID,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                Role = user.Role,
                SubscriptionTier = user.SubscriptionTier,
                CustomCommissionRate = user.CustomCommissionRate,
                CreatedAt = user.CreatedAt
            };

            if (user.Role == "Client")
            {
                var bookings = await _bookingRepo.GetByClientIdAsync(userId);
                vm.TotalBookingsCount = bookings.Count;
                var kyc = await _idRepo.GetVerificationByClientIdAsync(userId);
                vm.KycStatus = kyc?.VerificationStatus ?? "Unverified";
            }
            else if (user.Role == "Owner")
            {
                var properties = await _propRepo.GetPropertiesByOwnerIdAsync(userId);
                vm.TotalPropertiesCount = properties.Count;
                var bookings = await _bookingRepo.GetByOwnerIdAsync(userId);
                vm.TotalBookingsCount = bookings.Count;
            }
            else
            {
                var bookings = await _bookingRepo.GetAllBookingsAsync();
                vm.TotalBookingsCount = bookings.Count;
            }

            return View(vm);
        }

        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(UserProfileViewModel model)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null) return NotFound();

            if (string.IsNullOrWhiteSpace(model.FullName))
            {
                ModelState.AddModelError("FullName", "Full name is required.");
                return View(model);
            }

            user.FullName = model.FullName.Trim();
            user.Phone = model.Phone?.Trim();

            await _userRepo.UpdateUserAsync(user);
            TempData["Success"] = "Your profile information has been successfully updated!";
            return RedirectToAction(nameof(Profile));
        }

        [Microsoft.AspNetCore.Authorization.Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["PasswordError"] = "Please fill in all password fields accurately.";
                return RedirectToAction(nameof(Profile));
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null) return NotFound();

            if (!VerifyPassword(model.CurrentPassword, user.PasswordHash))
            {
                TempData["PasswordError"] = "The current password you entered is incorrect.";
                return RedirectToAction(nameof(Profile));
            }

            string newHash = HashPassword(model.NewPassword);
            await _userRepo.ResetPasswordAsync(user.Email, newHash);

            TempData["Success"] = "Your password has been changed securely!";
            return RedirectToAction(nameof(Profile));
        }

        [AcceptVerbs("GET", "POST")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            Response.Cookies.Delete("BookMyFun.AuthCookie");
            Response.Cookies.Delete(".AspNetCore.Cookies");
            Response.Cookies.Delete(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        public IActionResult AccessDenied()
        {
            return View();
        }

        public static string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        public static bool VerifyPassword(string password, string storedHash)
        {
            return HashPassword(password) == storedHash;
        }
    }
}
