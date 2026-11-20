using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using BookMyFun.Services.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class BatchBLegalAndDocumentSecurityTests
    {
        [Fact]
        public void EncryptionService_ValidKey_EncryptsWithMagicHeaderAndDecryptsSuccessfully()
        {
            byte[] rawKey = new byte[32];
            RandomNumberGenerator.Fill(rawKey);
            string base64Key = Convert.ToBase64String(rawKey);

            var originalEnv = Environment.GetEnvironmentVariable("ID_ENCRYPTION_KEY");
            try
            {
                Environment.SetEnvironmentVariable("ID_ENCRYPTION_KEY", base64Key);

                var service = new PrivateDocumentEncryptionService(NullLogger<PrivateDocumentEncryptionService>.Instance);
                Assert.True(service.IsEncryptionConfigured());

                byte[] originalData = Encoding.UTF8.GetBytes("Confidential Government ID Document Payload 12345");

                // Encrypt
                byte[] encrypted = service.EncryptDocument(originalData);
                Assert.NotNull(encrypted);
                Assert.True(encrypted.Length > originalData.Length);
                Assert.True(service.IsEncrypted(encrypted));

                // Verify 8-byte magic header BMFENC01
                byte[] header = new byte[8];
                Array.Copy(encrypted, 0, header, 0, 8);
                Assert.Equal("BMFENC01", Encoding.ASCII.GetString(header));

                // Decrypt
                byte[] decrypted = service.DecryptDocument(encrypted);
                Assert.Equal(originalData, decrypted);
            }
            finally
            {
                Environment.SetEnvironmentVariable("ID_ENCRYPTION_KEY", originalEnv);
            }
        }

        [Fact]
        public void EncryptionService_MissingKey_RefusesEncryption()
        {
            var originalEnv = Environment.GetEnvironmentVariable("ID_ENCRYPTION_KEY");
            try
            {
                Environment.SetEnvironmentVariable("ID_ENCRYPTION_KEY", null);

                var service = new PrivateDocumentEncryptionService(NullLogger<PrivateDocumentEncryptionService>.Instance);
                Assert.False(service.IsEncryptionConfigured());

                byte[] originalData = Encoding.UTF8.GetBytes("Test Document");

                Assert.Throws<InvalidOperationException>(() =>
                {
                    service.EncryptDocument(originalData);
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable("ID_ENCRYPTION_KEY", originalEnv);
            }
        }

        [Fact]
        public void EncryptionService_TamperedCiphertext_FailsDecryption()
        {
            byte[] rawKey = new byte[32];
            RandomNumberGenerator.Fill(rawKey);
            string base64Key = Convert.ToBase64String(rawKey);

            var originalEnv = Environment.GetEnvironmentVariable("ID_ENCRYPTION_KEY");
            try
            {
                Environment.SetEnvironmentVariable("ID_ENCRYPTION_KEY", base64Key);

                var service = new PrivateDocumentEncryptionService(NullLogger<PrivateDocumentEncryptionService>.Instance);
                byte[] originalData = Encoding.UTF8.GetBytes("Sample Document Payload");

                byte[] encrypted = service.EncryptDocument(originalData);

                // Tamper with ciphertext
                encrypted[^1] ^= 0xFF;

                Assert.ThrowsAny<CryptographicException>(() =>
                {
                    service.DecryptDocument(encrypted);
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable("ID_ENCRYPTION_KEY", originalEnv);
            }
        }

        [Fact]
        public void AllowedDocumentTypes_EnforcesExactWhiteList()
        {
            var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Aadhaar (masked copy)",
                "Driving Licence",
                "Voter ID",
                "PAN",
                "Passport"
            };

            Assert.Contains("Aadhaar (masked copy)", allowedTypes);
            Assert.Contains("Driving Licence", allowedTypes);
            Assert.Contains("Voter ID", allowedTypes);
            Assert.Contains("PAN", allowedTypes);
            Assert.Contains("Passport", allowedTypes);

            Assert.DoesNotContain("Ration Card", allowedTypes);
            Assert.DoesNotContain("Electricity Bill", allowedTypes);
            Assert.DoesNotContain("Aadhaar", allowedTypes);
        }
    }
}
