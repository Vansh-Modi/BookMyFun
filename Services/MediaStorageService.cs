using System.Net;
using System.Security.Cryptography;
using System.Text;
using BookMyFun.Services.Exceptions;

namespace BookMyFun.Services
{
    public interface IMediaStorageService
    {
        Task<(string publicUrl, string publicId)> SaveMediaFileAsync(IFormFile file, string mediaType);
        Task<string> SavePrivateDocumentAsync(IFormFile file, int clientId);
        Task<bool> ValidateExternalUrlAsync(string url);
        string GenerateExpiringDocumentToken(string documentKey, int requestingUserId, string userRole, int expirationMinutes = 15);
        bool ValidateExpiringDocumentToken(string documentKey, string token, int requestingUserId, string userRole);
        string GetPrivateDocumentPhysicalPath(string documentKey);
    }

    public class MediaStorageService : IMediaStorageService
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly ILogger<MediaStorageService> _logger;
        private readonly IImageOptimizationService _imageOptimizer;
        private readonly byte[] _tokenSigningKey;

        public MediaStorageService(
            IWebHostEnvironment env,
            IConfiguration config,
            ILogger<MediaStorageService> logger,
            IImageOptimizationService imageOptimizer)
        {
            _env = env;
            _config = config;
            _logger = logger;
            _imageOptimizer = imageOptimizer;

            string secret = _config["Security:TokenSecret"] ?? "BookMyFun_Default_Secure_Secret_Key_For_Tokens_2026";
            _tokenSigningKey = Encoding.UTF8.GetBytes(secret);
        }

        public async Task<(string publicUrl, string publicId)> SaveMediaFileAsync(IFormFile file, string mediaType)
        {
            if (file == null || file.Length == 0)
                throw new ValidationException("file", "Uploaded file is empty.");

            // 1. Size Validation
            if (mediaType.Equals("photo", StringComparison.OrdinalIgnoreCase))
            {
                if (file.Length > 10 * 1024 * 1024)
                    throw new ValidationException("file", "Photo exceeds maximum permitted size of 10MB.");
            }
            else if (mediaType.Equals("video", StringComparison.OrdinalIgnoreCase))
            {
                if (file.Length > 100 * 1024 * 1024)
                    throw new ValidationException("file", "Video exceeds maximum permitted size of 100MB.");
            }

            // 2. Magic-Byte Inspection
            string safeExtension = await ValidateMagicBytesAndGetExtensionAsync(file, mediaType);

            // 3. Filename Sanitization & Directory Generation
            string publicId = Guid.NewGuid().ToString("N");
            string safeFileName = $"{publicId}{safeExtension}";

            string subFolder = mediaType.Equals("video", StringComparison.OrdinalIgnoreCase) ? "videos" : "photos";
            string webRoot = !string.IsNullOrWhiteSpace(_env.WebRootPath) ? _env.WebRootPath : Path.Combine(_env.ContentRootPath, "wwwroot");
            string uploadsFolder = Path.Combine(webRoot, "uploads", subFolder);

            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            string fullPhysicalPath = Path.Combine(uploadsFolder, safeFileName);

            // 4. Server-Side Optimization for Image Files
            if (mediaType.Equals("photo", StringComparison.OrdinalIgnoreCase) && _imageOptimizer.IsImageExtension(safeExtension))
            {
                using var readStream = file.OpenReadStream();
                var (optimizedBytes, optimizedExt, _) = await _imageOptimizer.OptimizeImageAsync(
                    readStream,
                    safeExtension,
                    maxWidth: 1920,
                    maxHeight: 1080,
                    quality: 82L);

                safeExtension = optimizedExt;
                safeFileName = $"{publicId}{safeExtension}";
                fullPhysicalPath = Path.Combine(uploadsFolder, safeFileName);

                await File.WriteAllBytesAsync(fullPhysicalPath, optimizedBytes);
            }
            else
            {
                using (var stream = new FileStream(fullPhysicalPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
            }

            string publicUrl = $"/uploads/{subFolder}/{safeFileName}";
            return (publicUrl, publicId);
        }

        public async Task<string> SavePrivateDocumentAsync(IFormFile file, int clientId)
        {
            if (file == null || file.Length == 0)
                throw new ValidationException("file", "Document file is empty.");

            if (file.Length > 5 * 1024 * 1024)
                throw new ValidationException("file", "Document exceeds maximum permitted size of 5MB.");

            string safeExtension = await ValidateDocumentMagicBytesAsync(file);

            string documentId = Guid.NewGuid().ToString("N");
            string safeFileName = $"doc_client_{clientId}_{documentId}{safeExtension}";

            string privateRoot = Path.Combine(_env.ContentRootPath, "App_Data", "PrivateDocuments");
            if (!Directory.Exists(privateRoot))
            {
                Directory.CreateDirectory(privateRoot);
            }

            string fullPhysicalPath = Path.Combine(privateRoot, safeFileName);

            // Server-Side Optimization for raster ID documents (PNG/JPG) to minimize private storage footprint
            if ((safeExtension == ".jpg" || safeExtension == ".png") && _imageOptimizer.IsImageExtension(safeExtension))
            {
                using var readStream = file.OpenReadStream();
                var (optimizedBytes, optimizedExt, _) = await _imageOptimizer.OptimizeImageAsync(
                    readStream,
                    safeExtension,
                    maxWidth: 1600,
                    maxHeight: 1200,
                    quality: 85L);

                safeExtension = optimizedExt;
                safeFileName = $"doc_client_{clientId}_{documentId}{safeExtension}";
                fullPhysicalPath = Path.Combine(privateRoot, safeFileName);

                await File.WriteAllBytesAsync(fullPhysicalPath, optimizedBytes);
            }
            else
            {
                using (var stream = new FileStream(fullPhysicalPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
            }

            return safeFileName;
        }

        public string GetPrivateDocumentPhysicalPath(string documentKey)
        {
            // Path traversal defense
            string safeKey = Path.GetFileName(documentKey);
            return Path.Combine(_env.ContentRootPath, "App_Data", "PrivateDocuments", safeKey);
        }

        public string GenerateExpiringDocumentToken(string documentKey, int requestingUserId, string userRole, int expirationMinutes = 15)
        {
            long expiresAtUtcUnix = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes).ToUnixTimeSeconds();
            string payload = $"{documentKey}|{requestingUserId}|{userRole}|{expiresAtUtcUnix}";

            using var hmac = new HMACSHA256(_tokenSigningKey);
            byte[] signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            string hexSignature = Convert.ToHexString(signature).ToLowerInvariant();

            string rawToken = $"{payload}|{hexSignature}";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(rawToken));
        }

        public bool ValidateExpiringDocumentToken(string documentKey, string token, int requestingUserId, string userRole)
        {
            try
            {
                byte[] decodedBytes = Convert.FromBase64String(token);
                string rawToken = Encoding.UTF8.GetString(decodedBytes);

                string[] parts = rawToken.Split('|');
                if (parts.Length != 5) return false;

                string tokenDocKey = parts[0];
                int tokenUserId = int.Parse(parts[1]);
                string tokenRole = parts[2];
                long expiresAtUnix = long.Parse(parts[3]);
                string tokenSignature = parts[4];

                if (!string.Equals(documentKey, tokenDocKey, StringComparison.OrdinalIgnoreCase)) return false;
                if (tokenUserId != requestingUserId) return false;
                if (!string.Equals(userRole, tokenRole, StringComparison.OrdinalIgnoreCase)) return false;

                long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (nowUnix > expiresAtUnix) return false; // Expired

                string payload = $"{tokenDocKey}|{tokenUserId}|{tokenRole}|{expiresAtUnix}";
                using var hmac = new HMACSHA256(_tokenSigningKey);
                byte[] expectedSignature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                string hexExpected = Convert.ToHexString(expectedSignature).ToLowerInvariant();

                return CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(hexExpected),
                    Encoding.UTF8.GetBytes(tokenSignature)
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to validate expiring document token.");
                return false;
            }
        }

        public async Task<bool> ValidateExternalUrlAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ValidationException("url", "Media URL cannot be empty.");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new ValidationException("url", "Invalid URL format.");

            // 1. Enforce HTTPS only
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new SecurityViolationException("Only HTTPS media URLs are permitted.");

            string host = uri.DnsSafeHost.Trim().ToLowerInvariant();

            // 2. Reject obvious dangerous local/metadata hostnames
            if (host == "localhost" || host.EndsWith(".localhost") || host == "127.0.0.1" || host == "::1" || host == "169.254.169.254" || host == "metadata.google.internal")
            {
                throw new SecurityViolationException("Access to internal hostnames or metadata endpoints is forbidden (SSRF Protection).");
            }

            try
            {
                // 3. DNS Resolution & Private IP Range Filtering
                var hostAddresses = await Dns.GetHostAddressesAsync(host);
                if (hostAddresses == null || hostAddresses.Length == 0)
                {
                    throw new ValidationException("url", "Unable to resolve external media host.");
                }

                foreach (var ip in hostAddresses)
                {
                    if (IsPrivateOrRestrictedIp(ip))
                    {
                        throw new SecurityViolationException($"Target host resolves to restricted IP {ip}. Request aborted (SSRF Protection).");
                    }
                }

                return true;
            }
            catch (SecurityViolationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to perform DNS resolution or verification for {Url}", url);
                throw new ValidationException("url", $"External media verification failed: {ex.Message}");
            }
        }

        private static bool IsPrivateOrRestrictedIp(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip)) return true;

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                byte[] bytes = ip.GetAddressBytes();
                // 10.0.0.0/8
                if (bytes[0] == 10) return true;
                // 172.16.0.0/12 (172.16.x.x - 172.31.x.x)
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                // Link-local / AWS metadata: 169.254.0.0/16
                if (bytes[0] == 169 && bytes[1] == 254) return true;
                // 0.0.0.0 or broadcast 255.255.255.255
                if (bytes[0] == 0 || bytes[0] == 255) return true;
            }
            else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
                byte[] bytes = ip.GetAddressBytes();
                // Unique local address fc00::/7
                if ((bytes[0] & 0xFE) == 0xFC) return true;
            }

            return false;
        }

        private static async Task<string> ValidateMagicBytesAndGetExtensionAsync(IFormFile file, string mediaType)
        {
            byte[] header = new byte[16];
            int read;
            using (var stream = file.OpenReadStream())
            {
                read = await stream.ReadAsync(header, 0, header.Length);
                if (read < 4) throw new ValidationException("file", "File is corrupt or too small to inspect.");
            }

            if (mediaType.Equals("photo", StringComparison.OrdinalIgnoreCase))
            {
                // JPEG: FF D8 FF
                if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return ".jpg";

                // PNG: 89 50 4E 47
                if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) return ".png";

                // WEBP: 'RIFF' .... 'WEBP'
                if (read >= 12 &&
                    header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                    header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50) return ".webp";

                // AVIF: ....ftypavif or ....ftypavis
                if (read >= 12 &&
                    header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70 &&
                    ((header[8] == 0x61 && header[9] == 0x76 && header[10] == 0x69 && header[11] == 0x66) ||
                     (header[8] == 0x61 && header[9] == 0x76 && header[10] == 0x69 && header[11] == 0x73))) return ".avif";

                throw new ValidationException("file", "Invalid image format. Allowed formats: JPG, PNG, WEBP, AVIF with verified binary headers.");
            }
            else if (mediaType.Equals("video", StringComparison.OrdinalIgnoreCase))
            {
                // MP4: ....ftyp (bytes 4-7 = 0x66 0x74 0x79 0x70)
                if (header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70) return ".mp4";

                // WEBM: 1A 45 DF A3 (EBML Header)
                if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3) return ".webm";

                // QuickTime MOV (moov or ftyp)
                string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (ext == ".mov" || ext == ".mp4") return ext;

                throw new ValidationException("file", "Invalid video format. Allowed formats: MP4, WEBM, MOV with verified binary headers.");
            }

            throw new ValidationException("file", "Unsupported media type specified.");
        }

        private static async Task<string> ValidateDocumentMagicBytesAsync(IFormFile file)
        {
            byte[] header = new byte[16];
            int read;
            using (var stream = file.OpenReadStream())
            {
                read = await stream.ReadAsync(header, 0, header.Length);
                if (read < 4) throw new ValidationException("file", "Document file is too small or corrupt.");
            }

            // PDF: %PDF (25 50 44 46)
            if (header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46) return ".pdf";

            // JPEG: FF D8 FF
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return ".jpg";

            // PNG: 89 50 4E 47
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) return ".png";

            // WEBP: 'RIFF' .... 'WEBP'
            if (read >= 12 &&
                header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50) return ".webp";

            // AVIF: ....ftypavif or ....ftypavis
            if (read >= 12 &&
                header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70 &&
                ((header[8] == 0x61 && header[9] == 0x76 && header[10] == 0x69 && header[11] == 0x66) ||
                 (header[8] == 0x61 && header[9] == 0x76 && header[10] == 0x69 && header[11] == 0x73))) return ".avif";

            throw new ValidationException("file", "Invalid identity document format. Allowed formats: PDF, JPG, PNG, WEBP, AVIF with verified binary headers.");
        }
    }
}
