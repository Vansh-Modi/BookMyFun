namespace BookMyFun.Services.Security
{
    /// <summary>
    /// Service for cryptographically secure password hashing and verification with automatic legacy migration.
    /// </summary>
    public interface IPasswordHasherService
    {
        /// <summary>
        /// Hashes a plaintext password using PBKDF2 with HMAC-SHA256 and a random salt.
        /// </summary>
        string HashPassword(string password);

        /// <summary>
        /// Verifies a plaintext password against a stored hash (supporting PBKDF2, legacy SHA-256, and plain text).
        /// </summary>
        /// <param name="password">Plaintext password to test.</param>
        /// <param name="storedHash">Stored hash from database.</param>
        /// <param name="needsRehash">Outputs true if the password matched using a legacy format and must be upgraded to PBKDF2.</param>
        /// <returns>True if password is valid, otherwise false.</returns>
        bool VerifyPassword(string password, string storedHash, out bool needsRehash);
    }
}
