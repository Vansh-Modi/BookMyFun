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

        public PartnerKycController(IPartnerKycService kycService)
        {
            _kycService = kycService;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            ViewData["IsDashboard"] = true;
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
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
                MissingItems = status.MissingItems,
                SubmittedAt = status.SubmittedAt
            };
            return View(vm);
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
