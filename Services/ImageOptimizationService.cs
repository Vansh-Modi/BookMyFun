using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using BookMyFun.Services.Exceptions;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services
{
    public interface IImageOptimizationService
    {
        Task<(byte[] optimizedBytes, string extension, string contentType)> OptimizeImageAsync(
            Stream inputStream,
            string originalExtension,
            int maxWidth = 1920,
            int maxHeight = 1080,
            long quality = 82L);

        bool IsImageExtension(string extension);
        bool IsModernImageFormat(string extension);
    }

    public class ImageOptimizationService : IImageOptimizationService
    {
        private readonly ILogger<ImageOptimizationService> _logger;

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".avif", ".bmp"
        };

        private static readonly HashSet<string> ModernFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".webp", ".avif"
        };

        public ImageOptimizationService(ILogger<ImageOptimizationService> logger)
        {
            _logger = logger;
        }

        public bool IsImageExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return false;
            return ImageExtensions.Contains(extension);
        }

        public bool IsModernImageFormat(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return false;
            return ModernFormats.Contains(extension);
        }

        public async Task<(byte[] optimizedBytes, string extension, string contentType)> OptimizeImageAsync(
            Stream inputStream,
            string originalExtension,
            int maxWidth = 1920,
            int maxHeight = 1080,
            long quality = 82L)
        {
            if (inputStream == null || inputStream.Length == 0)
                throw new ValidationException("file", "Image stream is empty.");

            // Reset stream position if possible
            if (inputStream.CanSeek)
            {
                inputStream.Position = 0;
            }

            string ext = originalExtension.ToLowerInvariant();
            if (!ext.StartsWith('.')) ext = "." + ext;

            // If the image is already in a modern compressed format (WebP or AVIF), read bytes and preserve directly
            if (IsModernImageFormat(ext))
            {
                using var ms = new MemoryStream();
                await inputStream.CopyToAsync(ms);
                byte[] raw = ms.ToArray();
                string mime = ext == ".webp" ? "image/webp" : "image/avif";
                return (raw, ext, mime);
            }

            // On Windows 6.1+ platforms, optimize dimensions and compress quality with GDI+
            if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            {
                try
                {
                    using var originalBitmap = new Bitmap(inputStream);
                    int originalWidth = originalBitmap.Width;
                    int originalHeight = originalBitmap.Height;

                    int targetWidth = originalWidth;
                    int targetHeight = originalHeight;

                    if (originalWidth > maxWidth || originalHeight > maxHeight)
                    {
                        double ratioX = (double)maxWidth / originalWidth;
                        double ratioY = (double)maxHeight / originalHeight;
                        double ratio = Math.Min(ratioX, ratioY);

                        targetWidth = Math.Max(1, (int)(originalWidth * ratio));
                        targetHeight = Math.Max(1, (int)(originalHeight * ratio));
                    }

                    using var targetBitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
                    using (var graphics = Graphics.FromImage(targetBitmap))
                    {
                        graphics.CompositingQuality = CompositingQuality.HighQuality;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.SmoothingMode = SmoothingMode.HighQuality;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                        // Fill white background for transparent PNG conversion to JPEG
                        graphics.Clear(Color.White);
                        graphics.DrawImage(originalBitmap, 0, 0, targetWidth, targetHeight);
                    }

                    // Compress using JPEG encoder with specified quality
                    var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                    if (jpegEncoder != null)
                    {
                        using var encoderParameters = new EncoderParameters(1);
                        encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 40L, 100L));

                        using var outputMs = new MemoryStream();
                        targetBitmap.Save(outputMs, jpegEncoder, encoderParameters);
                        byte[] optimized = outputMs.ToArray();

                        if (inputStream.CanSeek)
                        {
                            double saved = 100.0 - (optimized.Length * 100.0 / Math.Max(1, inputStream.Length));
                            _logger.LogInformation(
                                "Optimized image ({Width}x{Height} -> {TargetWidth}x{TargetHeight}): {OldSize} bytes -> {NewSize} bytes ({Saved:F1}% storage reduction)",
                                originalWidth, originalHeight, targetWidth, targetHeight, inputStream.Length, optimized.Length, saved);
                        }

                        return (optimized, ".jpg", "image/jpeg");
                    }

                    // Fallback to default PNG stream if JPEG encoder unavailable
                    using var fallbackMs = new MemoryStream();
                    targetBitmap.Save(fallbackMs, ImageFormat.Png);
                    return (fallbackMs.ToArray(), ".png", "image/png");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to optimize image server-side. Falling back to original stream bytes.");
                }
            }

            // Universal fallback: Return original stream
            if (inputStream.CanSeek) inputStream.Position = 0;
            using var rawMs = new MemoryStream();
            await inputStream.CopyToAsync(rawMs);
            string fallbackMime = ext == ".png" ? "image/png" : "image/jpeg";
            return (rawMs.ToArray(), ext, fallbackMime);
        }

        [SupportedOSPlatform("windows")]
        private static ImageCodecInfo? GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageEncoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }
}
