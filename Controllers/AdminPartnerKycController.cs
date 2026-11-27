using System.Security.Claims;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    [Authorize(Roles = "Admin")]
    [Route("Admin/PartnerKyc")]
    public class AdminPartnerKycController : Controller
    {
        private readonly IPartnerKycRepository _kycRepo;
        private readonly IMediaStorageService _mediaStorage;
        private readonly ILogger<AdminPartnerKycController> _logger;

        public AdminPartnerKycController(
            IPartnerKycRepository kycRepo,
            IMediaStorageService mediaStorage,
            ILogger<AdminPartnerKycController> logger)
        {
            _kycRepo = kycRepo;
            _mediaStorage = mediaStorage;
            _logger = logger;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(string? status = "All")
        {
            string selectedStatus = string.IsNullOrWhiteSpace(status) ? "All" : status.Trim();
            var allItems = await _kycRepo.GetAllPartnerKycAsync(null);

            var filtered = string.Equals(selectedStatus, "All", StringComparison.OrdinalIgnoreCase)
                ? allItems
                : allItems.Where(x => string.Equals(x.KycStatus, selectedStatus, StringComparison.OrdinalIgnoreCase)).ToList();

            var vm = new AdminPartnerKycListViewModel
            {
                SelectedStatus = selectedStatus,
                Items = filtered,
                SubmittedCount = allItems.Count(x => string.Equals(x.KycStatus, "Submitted", StringComparison.OrdinalIgnoreCase)),
                VerifiedCount = allItems.Count(x => string.Equals(x.KycStatus, "Verified", StringComparison.OrdinalIgnoreCase)),
                RejectedCount = allItems.Count(x => string.Equals(x.KycStatus, "Rejected", StringComparison.OrdinalIgnoreCase)),
                TotalCount = allItems.Count
            };
            return View(vm);
        }

        [HttpGet("{userId:int}")]
        public async Task<IActionResult> Detail(int userId)
        {
            var detail = await _kycRepo.GetPartnerKycDetailAsync(userId);
            if (detail == null) return NotFound();

            detail.AuditLogs = await _kycRepo.GetAuditLogsForPartnerAsync(userId);
            return View(detail);
        }

        [HttpGet("{userId:int}/Document")]
        public async Task<IActionResult> ViewDocument(int userId, string type)
        {
            var record = await _kycRepo.GetByUserIdAsync(userId);
            if (record == null) return NotFound();

            string? docKey = type?.Trim().ToLowerInvariant() switch
            {
                "aadhaar" => record.AadhaarDocumentKey,
                "pan" => record.PanDocumentKey,
                "bankproof" => record.BankProofDocumentKey,
                _ => null
            };
            string canonicalType = type?.Trim().ToLowerInvariant() switch
            {
                "aadhaar" => "Aadhaar",
                "pan" => "PAN",
                "bankproof" => "BankProof",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(docKey) || string.IsNullOrEmpty(canonicalType))
                return NotFound();

            int adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            string? clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            try
            {
                byte[] fileBytes = await _mediaStorage.ReadPrivateDocumentAsync(docKey);
                await _kycRepo.LogAuditAsync(adminId, userId, "ViewDocument", canonicalType, null, clientIp);

                Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                Response.Headers["Pragma"] = "no-cache";
                Response.Headers["Content-Disposition"] = "inline";
                Response.Headers["X-Content-Type-Options"] = "nosniff";

                string contentType = DetectContentType(docKey, fileBytes);
                return File(fileBytes, contentType);
            }
            catch (FileNotFoundException)
            {
                return NotFound("Document file not found on disk.");
            }
            catch (Exception ex)
            {
                string corrId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                _logger.LogError(ex, "Failed to decrypt/serve Partner KYC document for PartnerUserId={PartnerId}, DocType={DocType}, CorrelationId={CorrelationId}",
                    userId, canonicalType, corrId);
                return StatusCode(500, $"Unable to load document right now. Reference ID: {corrId}.");
            }
        }

        [HttpPost("{userId:int}/Approve")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int userId)
        {
            int adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            string? clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            bool ok = await _kycRepo.ReviewAsync(userId, "Verified", adminId, null, clientIp);
            if (!ok)
            {
                TempData["Error"] = "Partner KYC record not found.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = $"Partner #{userId} KYC has been approved (Verified). Publishing is now enabled.";
            return RedirectToAction(nameof(Detail), new { userId });
        }

        [HttpPost("{userId:int}/Reject")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int userId, string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["Error"] = "A rejection reason is required when rejecting partner KYC.";
                return RedirectToAction(nameof(Detail), new { userId });
            }

            int adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            string? clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            bool ok = await _kycRepo.ReviewAsync(userId, "Rejected", adminId, reason.Trim(), clientIp);
            if (!ok)
            {
                TempData["Error"] = "Partner KYC record not found.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = $"Partner #{userId} KYC has been rejected with reason recorded.";
            return RedirectToAction(nameof(Detail), new { userId });
        }

        private static string DetectContentType(string docKey, byte[] bytes)
        {
            if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
                return "application/pdf";
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "image/jpeg";
            if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "image/png";
            if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[8] == 0x57 && bytes[9] == 0x45)
                return "image/webp";

            string ext = Path.GetExtension(docKey).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".avif" => "image/avif",
                _ => "image/jpeg"
            };
        }
    }
}
