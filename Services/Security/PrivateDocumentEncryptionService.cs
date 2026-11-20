using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services.Security
{
    public interface IPrivateDocumentEncryptionService
    {
        bool IsEncryptionConfigured();
        byte[] EncryptDocument(byte[] plaintextBytes);
        byte[] DecryptDocument(byte[] fileBytes);
        bool IsEncrypted(byte[] fileBytes);
    }

    public class PrivateDocumentEncryptionService : IPrivateDocumentEncryptionService
    {
        private static readonly byte[] MagicHeader = Encoding.ASCII.GetBytes("BMFENC01"); // 8-byte framing header
        private const int NonceSize = 12; // 96-bit nonce for AES-GCM
        private const int TagSize = 16;   // 128-bit authentication tag
        private readonly ILogger<PrivateDocumentEncryptionService> _logger;

        public PrivateDocumentEncryptionService(ILogger<PrivateDocumentEncryptionService> logger)
        {
            _logger = logger;
        }

        private byte[]? GetKey()
        {
            var raw = Environment.GetEnvironmentVariable("ID_ENCRYPTION_KEY");
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try
            {
                byte[] key = Convert.FromBase64String(raw.Trim());
                return key.Length == 32 ? key : null;
            }
            catch
            {
                return null;
            }
        }

        public bool IsEncryptionConfigured()
        {
            return GetKey() != null;
        }

        public byte[] EncryptDocument(byte[] plaintextBytes)
        {
            var key = GetKey();
            if (key == null)
            {
                _logger.LogError("Private document encryption refused: ID_ENCRYPTION_KEY environment variable is missing or invalid.");
                throw new InvalidOperationException("Private document encryption is not configured on this server.");
            }

            byte[] nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);

            byte[] tag = new byte[TagSize];
            byte[] ciphertext = new byte[plaintextBytes.Length];

            using var aesGcm = new AesGcm(key, TagSize);
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            // Stored framing: MagicHeader (8) + Nonce (12) + Tag (16) + Ciphertext (N)
            using var ms = new MemoryStream(MagicHeader.Length + NonceSize + TagSize + ciphertext.Length);
            ms.Write(MagicHeader, 0, MagicHeader.Length);
            ms.Write(nonce, 0, nonce.Length);
            ms.Write(tag, 0, tag.Length);
            ms.Write(ciphertext, 0, ciphertext.Length);
            return ms.ToArray();
        }

        public bool IsEncrypted(byte[] fileBytes)
        {
            if (fileBytes == null || fileBytes.Length < MagicHeader.Length + NonceSize + TagSize)
                return false;

            for (int i = 0; i < MagicHeader.Length; i++)
            {
                if (fileBytes[i] != MagicHeader[i]) return false;
            }
            return true;
        }

        public byte[] DecryptDocument(byte[] fileBytes)
        {
            if (!IsEncrypted(fileBytes))
            {
                // Unencrypted legacy file from before encryption existed
                return fileBytes;
            }

            var key = GetKey();
            if (key == null)
            {
                _logger.LogError("Private document decryption failed: ID_ENCRYPTION_KEY environment variable is missing or invalid.");
                throw new InvalidOperationException("Private document decryption is not configured on this server.");
            }

            int headerLen = MagicHeader.Length;
            byte[] nonce = new byte[NonceSize];
            Array.Copy(fileBytes, headerLen, nonce, 0, NonceSize);

            byte[] tag = new byte[TagSize];
            Array.Copy(fileBytes, headerLen + NonceSize, tag, 0, TagSize);

            int ciphertextOffset = headerLen + NonceSize + TagSize;
            int cipherLen = fileBytes.Length - ciphertextOffset;
            byte[] ciphertext = new byte[cipherLen];
            Array.Copy(fileBytes, ciphertextOffset, ciphertext, 0, cipherLen);

            byte[] plaintext = new byte[cipherLen];
            using var aesGcm = new AesGcm(key, TagSize);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

            return plaintext;
        }
    }
}
