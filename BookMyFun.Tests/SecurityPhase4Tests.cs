using BookMyFun.Services.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests;

public class SecurityPhase4Tests
{
    [Fact]
    public void LoginLockoutService_LocksAccountAfterFiveFailures()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = NullLogger<LoginLockoutService>.Instance;
        var lockout = new LoginLockoutService(cache, logger);

        string testEmail = "test_lockout@example.com";
        string testIp = "192.168.1.100";

        // Initially not locked out
        Assert.False(lockout.IsLockedOut(testEmail, testIp, out _));

        // 4 failed attempts -> still not locked out
        for (int i = 0; i < 4; i++)
        {
            lockout.RecordFailedAttempt(testEmail, testIp);
            Assert.False(lockout.IsLockedOut(testEmail, testIp, out _));
        }

        // 5th failed attempt -> locked out
        lockout.RecordFailedAttempt(testEmail, testIp);
        bool isLocked = lockout.IsLockedOut(testEmail, testIp, out var remaining);
        Assert.True(isLocked);
        Assert.True(remaining.TotalMinutes > 0);

        // Reset lockout on successful login
        lockout.ResetLockout(testEmail, testIp);
        Assert.False(lockout.IsLockedOut(testEmail, testIp, out _));
    }

    [Fact]
    public void LoginLockoutService_LocksIpAfterTenFailuresAcrossDifferentAccounts()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = NullLogger<LoginLockoutService>.Instance;
        var lockout = new LoginLockoutService(cache, logger);

        string testIp = "10.0.0.99";

        for (int i = 0; i < 9; i++)
        {
            lockout.RecordFailedAttempt($"user{i}@example.com", testIp);
            Assert.False(lockout.IsLockedOut($"user{i}@example.com", testIp, out _));
        }

        // 10th attempt from the same IP
        lockout.RecordFailedAttempt("user10@example.com", testIp);
        bool isLocked = lockout.IsLockedOut("innocent_user@example.com", testIp, out var remaining);
        Assert.True(isLocked, "IP should be locked out across any account after 10 failures");
        Assert.True(remaining.TotalMinutes > 0);
    }

    [Fact]
    public void HtmlSanitizer_AllowsSafeFormattingTags()
    {
        var sanitizer = new HtmlSanitizerService();
        string input = "<p>Welcome to <strong>BookMyFun</strong>! We have <em>great</em> villas.</p>";
        string sanitized = sanitizer.Sanitize(input);

        Assert.Equal(input, sanitized);
    }

    [Fact]
    public void HtmlSanitizer_StripsMaliciousScriptTagsAndEventHandlers()
    {
        var sanitizer = new HtmlSanitizerService();
        string input = "<p>Hello <script>alert('xss');</script><b onclick=\"evil()\">Bold</b></p>";
        string sanitized = sanitizer.Sanitize(input);

        Assert.DoesNotContain("<script>", sanitized);
        Assert.DoesNotContain("alert", sanitized);
        Assert.DoesNotContain("onclick", sanitized);
        Assert.DoesNotContain("evil()", sanitized);
        Assert.Contains("<b>Bold</b>", sanitized);
    }

    [Fact]
    public void HtmlSanitizer_StripsJavascriptUrisInLinksAndAddsNoopener()
    {
        var sanitizer = new HtmlSanitizerService();
        string maliciousLink = "<a href=\"javascript:stealCookies()\">Click Here</a>";
        string sanitizedMalicious = sanitizer.Sanitize(maliciousLink);

        Assert.DoesNotContain("javascript:", sanitizedMalicious);
        Assert.DoesNotContain("stealCookies", sanitizedMalicious);

        string safeLink = "<a href=\"https://bookmyfun.com/terms\">Terms</a>";
        string sanitizedSafe = sanitizer.Sanitize(safeLink);

        Assert.Contains("href=\"https://bookmyfun.com/terms\"", sanitizedSafe);
        Assert.Contains("rel=\"noopener noreferrer\"", sanitizedSafe);
    }
}
