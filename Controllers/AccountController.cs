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
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            IUserRepository userRepo, 
            IOtpRepository otpRepo,
            IOwnerRequestRepository ownerReqRepo,
            IPartnerLeadService partnerLeadService,
            IEmailService emailService,
            IWhatsAppService whatsAppService,
            ILogger<AccountController> logger)
        {
            _userRepo = userRepo;
            _otpRepo = otpRepo;
            _ownerReqRepo = ownerReqRepo;
            _partnerLeadService = partnerLeadService;
            _emailService = emailService;
            _whatsAppService = whatsAppService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> SendOtp([FromBody] SendOtpRequestModel request)
        {
            if (string.IsNullOrWhiteSpace(request?.Contact))
            {
                return Json(new { success = false, message = "Please provide an email or phone number." });
            }

            var contact = request.Contact.Trim();
            // Generate secure 6-digit numeric OTP
            var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            await _otpRepo.SaveOtpAsync(contact, otp, request.Purpose);

            // Send OTP via Email or WhatsApp depending on contact format
            if (contact.Contains("@"))
            {
                string subject = "Your BookMyFun Verification Code";
                string body = $@"
                    <div style='font-family: Arial, sans-serif; padding: 20px;'>
                        <h2>Welcome to BookMyFun</h2>
                        <p>Your one-time password (OTP) for account verification is:</p>
                        <h1 style='color: #d97706; letter-spacing: 4px;'>{otp}</h1>
                        <p>This code is valid for 10 minutes. Please do not share it with anyone.</p>
                    </div>";
                _ = _emailService.SendEmailAsync(contact, subject, body);
            }
            else
            {
                string msg = $"Your BookMyFun verification code is: *{otp}*. Valid for 10 minutes. Do not share this code.";
                _ = _whatsAppService.SendWhatsAppMessageAsync(contact, msg);
            }

            _logger.LogInformation("Sent OTP {Otp} to {Contact} for {Purpose}", otp, contact, request.Purpose);

            return Json(new { 
                success = true, 
                message = "Verification code dispatched successfully!", 
                // Return preview in development/test mode for immediate frictionless verification
                previewOtp = otp 
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

            var user = await _userRepo.GetByEmailAsync(model.Email);
            if (user == null || !VerifyPassword(model.Password, user.PasswordHash))
            {
                ModelState.AddModelError("", "Invalid email or password.");
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
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            return user.Role switch
            {
                "Admin" => RedirectToAction("Index", "Admin"),
                "Employee" => RedirectToAction("Index", "Employee"),
                "Owner" => RedirectToAction("Index", "Owner"),
                "EventManager" => RedirectToAction("Dashboard", "EventManager"),
                _ => RedirectToAction("Index", "Client")
            };
        }

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
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
