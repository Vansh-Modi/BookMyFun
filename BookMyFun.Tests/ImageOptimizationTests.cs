using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using BookMyFun.Services;
using BookMyFun.Services.Exceptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookMyFun.Tests
{
    [SupportedOSPlatform("windows6.1")]
    public class ImageOptimizationTests
    {
        private readonly ImageOptimizationService _imageOptimizer;

        public ImageOptimizationTests()
        {
            _imageOptimizer = new ImageOptimizationService(NullLogger<ImageOptimizationService>.Instance);
        }

        private class TestWebHostEnvironment : IWebHostEnvironment
        {
            public string WebRootPath { get; set; } = "";
            public IFileProvider WebRootFileProvider { get; set; } = null!;
            public string ApplicationName { get; set; } = "BookMyFun";
            public IFileProvider ContentRootFileProvider { get; set; } = null!;
            public string ContentRootPath { get; set; } = "";
            public string EnvironmentName { get; set; } = "Development";
        }

        [Fact]
        public void IsImageExtension_RecognizesStandardAndModernImageFormats()
        {
            Assert.True(_imageOptimizer.IsImageExtension(".jpg"));
            Assert.True(_imageOptimizer.IsImageExtension(".jpeg"));
            Assert.True(_imageOptimizer.IsImageExtension(".png"));
            Assert.True(_imageOptimizer.IsImageExtension(".webp"));
            Assert.True(_imageOptimizer.IsImageExtension(".avif"));
            Assert.True(_imageOptimizer.IsImageExtension(".bmp"));

            Assert.False(_imageOptimizer.IsImageExtension(".pdf"));
            Assert.False(_imageOptimizer.IsImageExtension(".mp4"));
            Assert.False(_imageOptimizer.IsImageExtension(".mov"));
            Assert.False(_imageOptimizer.IsImageExtension(".exe"));
            Assert.False(_imageOptimizer.IsImageExtension(""));
        }

        [Fact]
        public void IsModernImageFormat_IdentifiesWebPAndAvif()
        {
            Assert.True(_imageOptimizer.IsModernImageFormat(".webp"));
            Assert.True(_imageOptimizer.IsModernImageFormat(".avif"));

            Assert.False(_imageOptimizer.IsModernImageFormat(".jpg"));
            Assert.False(_imageOptimizer.IsModernImageFormat(".png"));
            Assert.False(_imageOptimizer.IsModernImageFormat(".pdf"));
        }

        [Fact]
        public async Task OptimizeImageAsync_DownscalesOversizedImageAndCompresses()
        {
            // Create a high-res in-memory 2400x1600 image
            using var sourceBmp = new Bitmap(2400, 1600);
            using (var g = Graphics.FromImage(sourceBmp))
            {
                g.Clear(Color.Coral);
                using var font = new Font(FontFamily.GenericSansSerif, 48);
                using var brush = new SolidBrush(Color.White);
                g.DrawString("BookMyFun Test High-Res Media", font, brush, 100, 100);
            }

            using var inputMs = new MemoryStream();
            sourceBmp.Save(inputMs, ImageFormat.Png);

            inputMs.Position = 0;
            var (optimizedBytes, ext, contentType) = await _imageOptimizer.OptimizeImageAsync(
                inputMs, ".png", maxWidth: 1920, maxHeight: 1080, quality: 80L);

            Assert.NotNull(optimizedBytes);
            Assert.True(optimizedBytes.Length > 0);
            Assert.Equal(".jpg", ext);
            Assert.Equal("image/jpeg", contentType);

            // Verify output dimensions are within constraints
            using var resultMs = new MemoryStream(optimizedBytes);
            using var resultBmp = new Bitmap(resultMs);
            Assert.True(resultBmp.Width <= 1920, $"Width {resultBmp.Width} exceeded 1920");
            Assert.True(resultBmp.Height <= 1080, $"Height {resultBmp.Height} exceeded 1080");

            // Verify aspect ratio preservation (2400:1600 = 1.5 ratio)
            double originalRatio = (double)sourceBmp.Width / sourceBmp.Height;
            double resultRatio = (double)resultBmp.Width / resultBmp.Height;
            Assert.InRange(Math.Abs(originalRatio - resultRatio), 0.0, 0.02);
        }

        [Fact]
        public async Task OptimizeImageAsync_ModernWebPFormat_PreservedDirectly()
        {
            // Mock a WebP payload
            byte[] webpHeader = new byte[]
            {
                0x52, 0x49, 0x46, 0x46, // 'RIFF'
                0x24, 0x00, 0x00, 0x00, // file size
                0x57, 0x45, 0x42, 0x50, // 'WEBP'
                0x56, 0x50, 0x38, 0x20  // 'VP8 '
            };

            using var ms = new MemoryStream(webpHeader);
            var (optimizedBytes, ext, contentType) = await _imageOptimizer.OptimizeImageAsync(ms, ".webp");

            Assert.Equal(webpHeader.Length, optimizedBytes.Length);
            Assert.Equal(".webp", ext);
            Assert.Equal("image/webp", contentType);
            Assert.Equal(webpHeader, optimizedBytes);
        }

        [Fact]
        public async Task MediaStorageService_MagicBytes_AcceptsWebPAndAvif()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BookMyFun_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var env = new TestWebHostEnvironment
            {
                WebRootPath = tempDir,
                ContentRootPath = tempDir
            };

            var config = new ConfigurationBuilder().Build();
            var service = new MediaStorageService(
                env,
                config,
                NullLogger<MediaStorageService>.Instance,
                _imageOptimizer);

            // 1. Valid WebP file header
            byte[] webpBytes = new byte[32];
            webpBytes[0] = 0x52; webpBytes[1] = 0x49; webpBytes[2] = 0x46; webpBytes[3] = 0x46; // RIFF
            webpBytes[8] = 0x57; webpBytes[9] = 0x45; webpBytes[10] = 0x42; webpBytes[11] = 0x50; // WEBP

            var webpFile = CreateMockFormFile("property.webp", "image/webp", webpBytes);
            var (publicUrlWebp, _) = await service.SaveMediaFileAsync(webpFile, "photo");
            Assert.EndsWith(".webp", publicUrlWebp);

            // 2. Valid AVIF file header (ftypavif)
            byte[] avifBytes = new byte[32];
            avifBytes[4] = 0x66; avifBytes[5] = 0x74; avifBytes[6] = 0x79; avifBytes[7] = 0x70; // ftyp
            avifBytes[8] = 0x61; avifBytes[9] = 0x76; avifBytes[10] = 0x69; avifBytes[11] = 0x66; // avif

            var avifFile = CreateMockFormFile("property.avif", "image/avif", avifBytes);
            var (publicUrlAvif, _) = await service.SaveMediaFileAsync(avifFile, "photo");
            Assert.EndsWith(".avif", publicUrlAvif);

            // Clean up test temp files
            try { Directory.Delete(tempDir, true); } catch { }
        }

        [Fact]
        public async Task MediaStorageService_MagicBytes_RejectsExecutablePayloads()
        {
            var env = new TestWebHostEnvironment();
            var config = new ConfigurationBuilder().Build();
            var service = new MediaStorageService(
                env,
                config,
                NullLogger<MediaStorageService>.Instance,
                _imageOptimizer);

            // Windows Executable PE Header 'MZ' (0x4D 0x5A) disguised as photo
            byte[] exeBytes = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };
            var maliciousFile = CreateMockFormFile("malicious.jpg", "image/jpeg", exeBytes);

            await Assert.ThrowsAsync<ValidationException>(() => service.SaveMediaFileAsync(maliciousFile, "photo"));
        }

        [Fact]
        public void ClientSideOptimizerScript_ExistsAndDeclaresImageOptimizer()
        {
            string scriptPath = TestPathHelper.GetPath("wwwroot", "js", "imageOptimizer.js");
            Assert.True(File.Exists(scriptPath), "imageOptimizer.js must exist in wwwroot/js");

            string content = File.ReadAllText(scriptPath);
            Assert.Contains("window.ImageOptimizer = ImageOptimizer", content);
            Assert.Contains("compressAndConvertToWebP", content);
            Assert.Contains("image/webp", content);
        }

        private static IFormFile CreateMockFormFile(string fileName, string contentType, byte[] content)
        {
            var stream = new MemoryStream(content);
            var file = new FormFile(stream, 0, content.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
            return file;
        }
    }
}
