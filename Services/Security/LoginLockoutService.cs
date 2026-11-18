using Microsoft.Extensions.Caching.Memory;

namespace BookMyFun.Services.Security
{
    /// <summary>
    /// In-memory implementation of account and IP lockout for failed authentication attempts.
    /// Locks out after 5 failures in 15 minutes per account, or 10 failures per IP address.
    /// </summary>
    public class LoginLockoutService : ILoginLockoutService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<LoginLockoutService> _logger;

        private const int MaxAccountFailures = 5;
        private const int MaxIpFailures = 10;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        private class AttemptRecord
        {
            public int FailureCount { get; set; }
            public DateTime FirstFailureUtc { get; set; }
            public DateTime? LockedUntilUtc { get; set; }
        }

        public LoginLockoutService(IMemoryCache cache, ILogger<LoginLockoutService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public bool IsLockedOut(string email, string? ipAddress, out TimeSpan remainingLockout)
        {
            remainingLockout = TimeSpan.Zero;
            var now = DateTime.UtcNow;

            // Check account lockout
            if (!string.IsNullOrWhiteSpace(email))
            {
                var accountKey = GetAccountKey(email);
                if (_cache.TryGetValue(accountKey, out AttemptRecord? accRecord) && accRecord != null)
                {
                    if (accRecord.LockedUntilUtc.HasValue && accRecord.LockedUntilUtc.Value > now)
                    {
                        remainingLockout = accRecord.LockedUntilUtc.Value - now;
                        return true;
                    }
                }
            }

            // Check IP lockout
            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                var ipKey = GetIpKey(ipAddress);
                if (_cache.TryGetValue(ipKey, out AttemptRecord? ipRecord) && ipRecord != null)
                {
                    if (ipRecord.LockedUntilUtc.HasValue && ipRecord.LockedUntilUtc.Value > now)
                    {
                        remainingLockout = ipRecord.LockedUntilUtc.Value - now;
                        return true;
                    }
                }
            }

            return false;
        }

        public void RecordFailedAttempt(string email, string? ipAddress)
        {
            var now = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(email))
            {
                var accountKey = GetAccountKey(email);
                var record = _cache.GetOrCreate(accountKey, entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = LockoutDuration;
                    return new AttemptRecord { FailureCount = 0, FirstFailureUtc = now };
                });

                if (record != null)
                {
                    record.FailureCount++;
                    if (record.FailureCount >= MaxAccountFailures)
                    {
                        record.LockedUntilUtc = now.Add(LockoutDuration);
                        _logger.LogWarning("Account {Email} locked out due to {Count} failed login attempts from IP {IP}",
                            email, record.FailureCount, ipAddress);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                var ipKey = GetIpKey(ipAddress);
                var record = _cache.GetOrCreate(ipKey, entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = LockoutDuration;
                    return new AttemptRecord { FailureCount = 0, FirstFailureUtc = now };
                });

                if (record != null)
                {
                    record.FailureCount++;
                    if (record.FailureCount >= MaxIpFailures)
                    {
                        record.LockedUntilUtc = now.Add(LockoutDuration);
                        _logger.LogWarning("IP {IP} locked out due to {Count} failed login attempts targeting accounts including {Email}",
                            ipAddress, record.FailureCount, email);
                    }
                }
            }
        }

        public void ResetLockout(string email, string? ipAddress)
        {
            if (!string.IsNullOrWhiteSpace(email))
            {
                _cache.Remove(GetAccountKey(email));
            }
            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                _cache.Remove(GetIpKey(ipAddress));
            }
        }

        private static string GetAccountKey(string email) => $"login_fail_acc_{email.Trim().ToLowerInvariant()}";
        private static string GetIpKey(string ip) => $"login_fail_ip_{ip.Trim()}";
    }
}
