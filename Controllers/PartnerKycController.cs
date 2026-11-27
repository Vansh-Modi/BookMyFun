using System.Security.Claims;
using BookMyFun.Models.ViewModels;
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

        public PartnerKycController(IPartnerKycService kycService, ILogger<PartnerKycController> logger)
        {
            _kycService = kycService;
            _logger = logger;
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

            var (success, fieldErrors, status) = await _kycService.SubmitAsync(userId, model);
            foreach (var kv in fieldErrors)
            {
                ModelState.AddModelError(kv.Key, kv.Value);
            }

            if (!success || !ModelState.IsValid)
            {
                model.HasAadhaar = status.HasAadhaar;
                model.HasPan = status.HasPan;
                model.HasBankProof = status.HasBankProof;
                model.MaskedBankAccount = status.MaskedBankAccount;
                model.KycStatus = status.KycStatus;
                model.CheckFailed = status.CheckFailed;
                model.CorrelationId = status.CorrelationId;
                model.MissingItems = status.MissingItems;
                model.SubmittedAt = status.SubmittedAt;
                model.BankAccountNumber = null; // Never echo back raw account number
                return View(model);
            }

            TempData["Success"] = "Your KYC documents and bank details have been submitted securely. You can now publish listings and packages.";
            return RedirectToAction(nameof(Index));
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
