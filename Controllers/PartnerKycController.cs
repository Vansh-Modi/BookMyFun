using System.Security.Claims;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    [Authorize(Roles = "Owner,EventManager")]
    [Route("Partner/Kyc")]
    public class PartnerKycController : Controller
    {
        private readonly IPartnerKycService _kycService;
        private readonly ILogger<PartnerKycController> _logger;
        private readonly IEmailService? _emailService;
        private readonly IUserRepository? _userRepo;
        private readonly IConfiguration? _configuration;

        public PartnerKycController(
            IPartnerKycService kycService,
            ILogger<PartnerKycController> logger,
            IEmailService? emailService = null,
            IUserRepository? userRepo = null,
            IConfiguration? configuration = null)
        {
            _kycService = kycService;
            _logger = logger;
            _emailService = emailService;
            _userRepo = userRepo;
            _configuration = configuration;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            ViewData["IsDashboard"] = true;
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            try
            {
                var status = await _kycService.GetStatusAsync(userId);

                var vm = new PartnerKycSubmitViewModel
                {
                    AccountHolderName = status.AccountHolderName ?? string.Empty,
                    IfscCode = status.IfscCode ?? string.Empty,
                    HasAadhaar = status.HasAadhaar,
                    HasPan = status.HasPan,
                    HasBankProof = status.HasBankProof,
                    MaskedBankAccount = status.MaskedBankAccount,
                    KycStatus = status.KycStatus,
                    RejectionReason = status.RejectionReason,
                    ReviewedAt = status.ReviewedAt,
                    CheckFailed = status.CheckFailed,
                    CorrelationId = status.CorrelationId,
                    MissingItems = status.MissingItems,
                    SubmittedAt = status.SubmittedAt
                };
                return View(vm);
            }
            catch (Exception ex)
            {
                string corrId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                _logger.LogError(ex, "Unexpected error loading Partner KYC page for UserId={UserId}, CorrelationId={CorrelationId}", userId, corrId);
                return View(new PartnerKycSubmitViewModel
                {
                    CheckFailed = true,
                    CorrelationId = corrId,
                    KycStatus = "Incomplete"
                });
            }
        }

        [HttpPost("")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(PartnerKycSubmitViewModel model)
        {
            ViewData["IsDashboard"] = true;
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            try
            {
                var (success, fieldErrors, status) = await _kycService.SubmitAsync(userId, model);
                foreach (var kv in fieldErrors)
                {
                    ModelState.AddModelError(kv.Key, kv.Value);
                }

                if (!success || !ModelState.IsValid)
                {
                    if (fieldErrors.Count == 0 && !ModelState.IsValid)
                    {
                        string corrId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                        var invalidKeys = ModelState.Where(m => m.Value?.Errors.Count > 0).Select(m => m.Key).ToArray();
                        _logger.LogWarning("Partner KYC submit failed model validation for UserId={UserId}, CorrelationId={CorrelationId}, Fields={Fields}",
                            userId, corrId, string.Join(",", invalidKeys));
                    }

                    model.HasAadhaar = status.HasAadhaar;
                    model.HasPan = status.HasPan;
                    model.HasBankProof = status.HasBankProof;
                    model.MaskedBankAccount = status.MaskedBankAccount;
                    model.KycStatus = status.KycStatus;
                    model.RejectionReason = status.RejectionReason;
                    model.ReviewedAt = status.ReviewedAt;
                    model.CheckFailed = status.CheckFailed;
                    model.CorrelationId = status.CorrelationId;
                    model.MissingItems = status.MissingItems;
                    model.SubmittedAt = status.SubmittedAt;
                    model.BankAccountNumber = null; // Never echo back raw account number
                    return View(model);
                }

                TempData["Success"] = "Details submitted. Our team will verify them shortly.";

                if (_emailService != null)
                {
                    try
                    {
                        var user = _userRepo != null ? await _userRepo.GetByIdAsync(userId) : null;
                        string? email = user?.Email ?? User?.FindFirstValue(ClaimTypes.Email);
                        string fullName = user?.FullName ?? User?.FindFirstValue(ClaimTypes.Name) ?? "Partner";
                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            DateTime submittedStamp = status.SubmittedAt ?? DateTime.UtcNow;
                            string baseUrl = (_configuration?["PublicBaseUrl"] ?? _configuration?["AppUrl"] ?? (Request?.Host.HasValue == true ? $"{Request.Scheme}://{Request.Host}" : "https://bookmyfun.in")).Trim().TrimEnd('/');
                            string kycUrl = $"{baseUrl}/Partner/Kyc";
                            string safeName = System.Net.WebUtility.HtmlEncode(fullName);

                            await _emailService.SendTemplatedEmailAsync(
                                userId,
                                userId,
                                email,
                                "PartnerKycSubmitted",
                                $"kyc-submitted-{userId}-{submittedStamp:yyyyMMddHHmm}",
                                "KYC details received",
                                "KYC details received",
                                $"<p>Hello {safeName},</p><p>We have received your details. Our team will verify them shortly.</p>",
                                "View KYC status",
                                kycUrl,
                                $"Hello {fullName}, we have received your details. Our team will verify them shortly. View status at {kycUrl}.");
                        }
                    }
                    catch (Exception emailEx)
                    {
                        _logger.LogWarning(emailEx, "Failed to send PartnerKycSubmitted email for UserId={UserId}", userId);
                    }
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                string corrId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                _logger.LogError(ex, "Unhandled exception during Partner KYC submit for UserId={UserId}, CorrelationId={CorrelationId}", userId, corrId);
                ModelState.AddModelError(string.Empty, $"We could not save your KYC and bank details right now. Please try again later. Reference ID: {corrId}.");
                model.BankAccountNumber = null;
                return View(model);
            }
        }

        [HttpPost("DismissPopup")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DismissPopup()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _kycService.MarkFirstPopupShownAsync(userId);
            return Ok(new { dismissed = true });
        }
    }
}
