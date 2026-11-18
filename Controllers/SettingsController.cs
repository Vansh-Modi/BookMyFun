using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BookMyFun.Controllers
{
    public class BrandingSettingsResponse
    {
        public string SiteName { get; set; } = "BookMyFun";
        public string LogoUrl { get; set; } = "";
        public string CopyrightText { get; set; } = "";
        public string SiteIcon { get; set; } = "bi-house-heart-fill";
        public string SiteTagline { get; set; } = "";
    }

    [ApiController]
    [Route("api/[controller]")]
    public class SettingsController : ControllerBase
    {
        private readonly ISiteSettingsService _siteSettingsService;
        private readonly IMediaStorageService _mediaStorage;
        private readonly IConfigRepository _configRepo;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(
            ISiteSettingsService siteSettingsService,
            IMediaStorageService mediaStorage,
            IConfigRepository configRepo,
            ILogger<SettingsController> logger)
        {
            _siteSettingsService = siteSettingsService;
            _mediaStorage = mediaStorage;
            _configRepo = configRepo;
            _logger = logger;
        }

        /// <summary>
        /// Public endpoint to read current site branding and identity (Site Name, Logo, Copyright).
        /// Accessible by anyone (public site and all dashboards).
        /// </summary>
        [HttpGet("branding")]
        [AllowAnonymous]
        public async Task<IActionResult> GetBranding()
        {
            try
            {
                var settings = await _siteSettingsService.GetSettingsAsync();
                return Ok(new BrandingSettingsResponse
                {
                    SiteName = string.IsNullOrWhiteSpace(settings.SiteName) ? "BookMyFun" : settings.SiteName,
                    LogoUrl = settings.LogoUrl ?? "",
                    CopyrightText = string.IsNullOrWhiteSpace(settings.CopyrightText)
                        ? $"© {DateTime.UtcNow.Year} BookMyFun. All rights reserved."
                        : settings.CopyrightText,
                    SiteIcon = string.IsNullOrWhiteSpace(settings.SiteIcon) ? "bi-house-heart-fill" : settings.SiteIcon,
                    SiteTagline = settings.SiteTagline ?? ""
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch site settings from database, returning default fallback.");
                return Ok(new BrandingSettingsResponse
                {
                    SiteName = "BookMyFun",
                    LogoUrl = "",
                    CopyrightText = $"© {DateTime.UtcNow.Year} BookMyFun. All rights reserved.",
                    SiteIcon = "bi-house-heart-fill",
                    SiteTagline = "Farmhouse, Villa & Resort Rentals"
                });
            }
        }

        /// <summary>
        /// Admin-only endpoint to update site branding, logo, and copyright.
        /// </summary>
        [HttpPost("branding")]
        [Authorize(Roles = "Admin")]
        [RequestSizeLimit(3 * 1024 * 1024)] // 3MB request limit
        public async Task<IActionResult> UpdateBranding(
            [FromForm] string siteName,
            [FromForm] string? copyrightText,
            [FromForm] bool removeLogo = false,
            [FromForm] IFormFile? logoFile = null)
        {
            if (string.IsNullOrWhiteSpace(siteName))
            {
                return BadRequest(new { success = false, message = "Site name is required." });
            }

            siteName = siteName.Trim();
            if (siteName.Length > 100)
            {
                siteName = siteName.Substring(0, 100);
            }

            var currentSettings = await _siteSettingsService.GetSettingsAsync();
            var oldName = currentSettings.SiteName;
            var oldLogo = currentSettings.LogoUrl;

            // Handle logo reset or upload
            if (removeLogo)
            {
                currentSettings.LogoUrl = "";
            }
            else if (logoFile != null && logoFile.Length > 0)
            {
                // Validate size (max 2MB)
                if (logoFile.Length > 2 * 1024 * 1024)
                {
                    return BadRequest(new { success = false, message = "Logo file exceeds maximum permitted size of 2 MB." });
                }

                // Validate extension
                var ext = Path.GetExtension(logoFile.FileName).ToLowerInvariant();
                var allowedExts = new[] { ".png", ".jpg", ".jpeg", ".svg", ".webp" };
                if (!allowedExts.Contains(ext))
                {
                    return BadRequest(new { success = false, message = "Invalid file type. Allowed formats: PNG, JPG, SVG, WebP." });
                }

                try
                {
                    var (uploadedUrl, _) = await _mediaStorage.SaveMediaFileAsync(logoFile, "photo");
                    currentSettings.LogoUrl = uploadedUrl;
                }
                catch (ValidationException vx)
                {
                    return BadRequest(new { success = false, message = vx.Message });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error uploading branding logo.");
                    return StatusCode(500, new { success = false, message = "An error occurred while uploading the logo." });
                }
            }

            currentSettings.SiteName = siteName;
            if (!string.IsNullOrWhiteSpace(copyrightText))
            {
                currentSettings.CopyrightText = copyrightText.Trim();
                if (currentSettings.CopyrightText.Length > 200)
                {
                    currentSettings.CopyrightText = currentSettings.CopyrightText.Substring(0, 200);
                }
            }
            else
            {
                currentSettings.CopyrightText = $"© {DateTime.UtcNow.Year} {siteName}. All rights reserved.";
            }

            await _siteSettingsService.SaveSettingsAsync(currentSettings);

            // Audit log safely if user is authenticated in HTTP context
            if (User?.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                await _configRepo.LogAuditAsync(userId, "UpdateSiteBrandingApi", "SiteSettings", null,
                    $"Old: [Name='{oldName}', Logo='{oldLogo}'] -> New: [Name='{currentSettings.SiteName}', Logo='{currentSettings.LogoUrl}']");
            }

            return Ok(new
            {
                success = true,
                message = "Brand settings saved successfully.",
                siteName = currentSettings.SiteName,
                logoUrl = currentSettings.LogoUrl,
                copyrightText = currentSettings.CopyrightText,
                siteIcon = currentSettings.SiteIcon
            });
        }
    }
}
