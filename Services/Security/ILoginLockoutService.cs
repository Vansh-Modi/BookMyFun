namespace BookMyFun.Services.Security
{
    /// <summary>
    /// Service for tracking failed login attempts and enforcing account lockout to mitigate brute-force and credential-stuffing attacks.
    /// </summary>
    public interface ILoginLockoutService
    {
        /// <summary>
        /// Checks whether the specified account or IP address is currently locked out.
        /// </summary>
        /// <param name="email">Normalized account email.</param>
        /// <param name="ipAddress">Client IP address.</param>
        /// <param name="remainingLockout">Remaining lockout duration if locked out.</param>
        /// <returns>True if locked out, false otherwise.</returns>
        bool IsLockedOut(string email, string? ipAddress, out TimeSpan remainingLockout);

        /// <summary>
        /// Records a failed authentication attempt for the account and IP address.
        /// </summary>
        void RecordFailedAttempt(string email, string? ipAddress);

        /// <summary>
        /// Resets the failed attempt counter upon successful authentication.
        /// </summary>
        void ResetLockout(string email, string? ipAddress);
    }
}
