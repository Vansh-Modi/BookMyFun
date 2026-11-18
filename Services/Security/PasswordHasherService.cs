using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BookMyFun.Services.Security
{
    /// <summary>
    /// Implements PBKDF2 with HMAC-SHA256, 128-bit salt, and 100,000 iterations (OWASP recommended standard),
    /// providing backward-compatible verification and seamless upgrade paths for legacy SHA-256 and plaintext credentials.
    /// </summary>
    public class PasswordHasherService : IPasswordHasherService
    {
        private const byte FormatMarkerV1 = 0x01;
        private const int SaltByteLength = 16;
        private const int SubkeyByteLength = 32;
        private const int DefaultIterations = 100_000;

        /// <inheritdoc />
        public string HashPassword(string password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltByteLength);
            byte[] subkey = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                DefaultIterations,
                HashAlgorithmName.SHA256,
                SubkeyByteLength);

            // Payload: [FormatMarker: 1 byte] + [Iterations: 4 bytes BE] + [Salt: 16 bytes] + [Subkey: 32 bytes] = 53 bytes
            byte[] payload = new byte[1 + sizeof(int) + SaltByteLength + SubkeyByteLength];
            payload[0] = FormatMarkerV1;
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(1, sizeof(int)), DefaultIterations);
            Buffer.BlockCopy(salt, 0, payload, 1 + sizeof(int), SaltByteLength);
            Buffer.BlockCopy(subkey, 0, payload, 1 + sizeof(int) + SaltByteLength, SubkeyByteLength);

            return Convert.ToBase64String(payload);
        }

        /// <inheritdoc />
        public bool VerifyPassword(string password, string storedHash, out bool needsRehash)
        {
            needsRehash = false;
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }

            // 1. Try modern PBKDF2 V1 format
            try
            {
                byte[] decoded = Convert.FromBase64String(storedHash);
                if (decoded.Length == (1 + sizeof(int) + SaltByteLength + SubkeyByteLength) && decoded[0] == FormatMarkerV1)
                {
                    int iterations = BinaryPrimitives.ReadInt32BigEndian(decoded.AsSpan(1, sizeof(int)));
                    byte[] salt = new byte[SaltByteLength];
                    Buffer.BlockCopy(decoded, 1 + sizeof(int), salt, 0, SaltByteLength);

                    byte[] expectedSubkey = new byte[SubkeyByteLength];
                    Buffer.BlockCopy(decoded, 1 + sizeof(int) + SaltByteLength, expectedSubkey, 0, SubkeyByteLength);

                    byte[] actualSubkey = Rfc2898DeriveBytes.Pbkdf2(
                        password,
                        salt,
                        iterations,
                        HashAlgorithmName.SHA256,
                        SubkeyByteLength);

                    if (CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey))
                    {
                        needsRehash = (iterations < DefaultIterations);
                        return true;
                    }

                    return false;
                }
            }
            catch
            {
                // Not a valid Base64 payload or not format V1 — fall through to legacy checks
            }

            // 2. Legacy SHA-256 match
            using (var sha = SHA256.Create())
            {
                var legacyBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
                var legacyHash = Convert.ToBase64String(legacyBytes);
                if (string.Equals(legacyHash, storedHash, StringComparison.Ordinal))
                {
                    needsRehash = true;
                    return true;
                }
            }

            // 3. Legacy seeded plaintext match (for development and backward compatibility)
            if (string.Equals(password, storedHash, StringComparison.Ordinal))
            {
                needsRehash = true;
                return true;
            }

            return false;
        }
    }
}
