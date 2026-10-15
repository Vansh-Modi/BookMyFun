using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BookMyFun.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MediaController : ControllerBase
    {
        private readonly IMediaStorageService _mediaStorage;
        private readonly IPropertyRepository _propertyRepo;
        private readonly IIdVerificationService _idVerificationService;
        private readonly ILogger<MediaController> _logger;

        public MediaController(
            IMediaStorageService mediaStorage,
            IPropertyRepository propertyRepo,
            IIdVerificationService idVerificationService,
            ILogger<MediaController> logger)
        {
            _mediaStorage = mediaStorage;
            _propertyRepo = propertyRepo;
            _idVerificationService = idVerificationService;
            _logger = logger;
        }

        [HttpPost("upload-file")]
        [Authorize(Roles = "Owner,Admin,Employee")]
        [RequestSizeLimit(105 * 1024 * 1024)] // 105MB limit for videos
        public async Task<IActionResult> UploadMediaFile([FromForm] IFormFile file, [FromForm] string mediaType, [FromForm] int? propertyId, [FromForm] bool isPrimary = false)
        {
            if (file == null || file.Length == 0)
                throw new ValidationException("file", "No file provided.");

            string type = string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "photo";

            var (publicUrl, publicId) = await _mediaStorage.SaveMediaFileAsync(file, type);

            int mediaId = 0;
            if (propertyId.HasValue && propertyId.Value > 0)
            {
                // Tenant isolation check
                var prop = await _propertyRepo.GetByIdAsync(propertyId.Value);
                if (prop != null)
                {
                    int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
                    string role = User.FindFirstValue(ClaimTypes.Role) ?? "";

                    if (role == "Owner" && prop.OwnerID != currentUserId)
                    {
                        throw new ForbiddenException("Cannot attach media to another host's property.");
                    }

                    var mediaRecord = new PropertyMedia
                    {
                        PropertyID = propertyId.Value,
                        MediaType = type,
                        SourceType = "file",
                        StorageUrl = publicUrl,
                        PublicId = publicId,
                        IsPrimary = isPrimary,
                        SortOrder = 0,
                        CreatedAt = DateTime.UtcNow
                    };
                    mediaId = await _propertyRepo.AddPropertyMediaAsync(mediaRecord);
                }
            }

            return Ok(new
            {
                success = true,
                mediaId = mediaId,
                url = publicUrl,
                publicId = publicId,
                mediaType = type,
                sourceType = "file"
            });
        }

        [HttpPost("verify-url")]
        [Authorize(Roles = "Owner,Admin,Employee")]
        public async Task<IActionResult> VerifyExternalUrl([FromBody] ExternalUrlVerifyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Url))
                throw new ValidationException("url", "Media URL is required.");

            string type = string.Equals(request.MediaType, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "photo";

            // Validate against SSRF and ensure HTTPS
            await _mediaStorage.ValidateExternalUrlAsync(request.Url);

            int mediaId = 0;
            if (request.PropertyId.HasValue && request.PropertyId.Value > 0)
            {
                var prop = await _propertyRepo.GetByIdAsync(request.PropertyId.Value);
                if (prop != null)
                {
                    int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
                    string role = User.FindFirstValue(ClaimTypes.Role) ?? "";

                    if (role == "Owner" && prop.OwnerID != currentUserId)
                    {
                        throw new ForbiddenException("Cannot attach media to another host's property.");
                    }

                    var mediaRecord = new PropertyMedia
                    {
                        PropertyID = request.PropertyId.Value,
                        MediaType = type,
                        SourceType = "url",
                        StorageUrl = request.Url.Trim(),
                        PublicId = null,
                        IsPrimary = request.IsPrimary,
                        SortOrder = 0,
                        CreatedAt = DateTime.UtcNow
                    };
                    mediaId = await _propertyRepo.AddPropertyMediaAsync(mediaRecord);
                }
            }

            return Ok(new
            {
                success = true,
                mediaId = mediaId,
                url = request.Url.Trim(),
                mediaType = type,
                sourceType = "url"
            });
        }

        [HttpDelete("delete/{mediaId}")]
        [Authorize(Roles = "Owner,Admin,Employee")]
        public async Task<IActionResult> DeleteMedia(int mediaId)
        {
            bool deleted = await _propertyRepo.DeletePropertyMediaAsync(mediaId);
            return Ok(new { success = deleted });
        }

        [HttpPost("upload-id-document")]
        [Authorize(Roles = "Client,Admin,Employee")]
        [RequestSizeLimit(6 * 1024 * 1024)] // 6MB limit
        public async Task<IActionResult> UploadIdDocument([FromForm] IFormFile file, [FromForm] string documentType, [FromForm] string? maskedIdentifier)
        {
            if (file == null || file.Length == 0)
                throw new ValidationException("file", "No identification file provided.");

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
            string safeKey = await _mediaStorage.SavePrivateDocumentAsync(file, currentUserId);

            int verificationId = await _idVerificationService.SubmitVerificationAsync(
                currentUserId,
                documentType ?? "Aadhaar",
                safeKey,
                maskedIdentifier);

            return Ok(new
            {
                success = true,
                verificationId = verificationId,
                documentType = documentType,
                message = "Government ID uploaded securely into private storage. Pending staff review."
            });
        }

        [HttpGet("view-id-document")]
        [Authorize(Roles = "Client,Admin,Employee")]
        public IActionResult ViewPrivateDocument([FromQuery] string key, [FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(token))
                throw new ValidationException("Invalid request parameters.");

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
            string userRole = User.FindFirstValue(ClaimTypes.Role) ?? "";

            // Property Owner is strictly forbidden from downloading/viewing raw ID files
            if (string.Equals(userRole, "Owner", StringComparison.OrdinalIgnoreCase))
            {
                throw new ForbiddenException("Host accounts are prohibited from accessing client identity files.");
            }

            bool validToken = _mediaStorage.ValidateExpiringDocumentToken(key, token, currentUserId, userRole);
            if (!validToken)
            {
                throw new ForbiddenException("Expired or invalid document access token. Please request a fresh signed link.");
            }

            string physicalPath = _mediaStorage.GetPrivateDocumentPhysicalPath(key);
            if (!System.IO.File.Exists(physicalPath))
            {
                throw new NotFoundException("Requested document file was not found.");
            }

            string contentType = key.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" :
                                 key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";

            return PhysicalFile(physicalPath, contentType, enableRangeProcessing: true);
        }
    }

    public class ExternalUrlVerifyRequest
    {
        public string Url { get; set; } = string.Empty;
        public string MediaType { get; set; } = "photo";
        public int? PropertyId { get; set; }
        public bool IsPrimary { get; set; }
    }
}
