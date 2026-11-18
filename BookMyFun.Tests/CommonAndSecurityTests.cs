using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BookMyFun.Services.Common;
using BookMyFun.Services.Security;
using Xunit;

namespace BookMyFun.Tests;

public class CommonAndSecurityTests
{
    private readonly PasswordHasherService _hasher = new();

    [Fact]
    public void PasswordHasher_HashPassword_ProducesValidPbkdf2Hash()
    {
        string password = "StrongPassword@123";
        string hash = _hasher.HashPassword(password);

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.NotEqual(password, hash);

        // Verify with correct password
        bool isValid = _hasher.VerifyPassword(password, hash, out bool needsRehash);
        Assert.True(isValid);
        Assert.False(needsRehash, "Modern PBKDF2 hash should not need rehash");

        // Verify with incorrect password
        bool isInvalid = _hasher.VerifyPassword("WrongPassword", hash, out _);
        Assert.False(isInvalid);
    }

    [Fact]
    public void PasswordHasher_LegacySha256_VerifiesAndSignalsNeedsRehash()
    {
        string password = "AdminPassword@123";

        // Generate legacy SHA-256 hash
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
        string legacySha256Hash = Convert.ToBase64String(bytes);

        bool isValid = _hasher.VerifyPassword(password, legacySha256Hash, out bool needsRehash);
        Assert.True(isValid);
        Assert.True(needsRehash, "Legacy SHA-256 hash must signal needsRehash = true");
    }

    [Fact]
    public void PasswordHasher_LegacyPlaintext_VerifiesAndSignalsNeedsRehash()
    {
        string plaintextPassword = "DevTestPassword123";

        bool isValid = _hasher.VerifyPassword(plaintextPassword, plaintextPassword, out bool needsRehash);
        Assert.True(isValid);
        Assert.True(needsRehash, "Seeded plain text password must signal needsRehash = true");
    }

    [Fact]
    public void ClaimsPrincipalExtensions_ExtractsUserIdAndRoleCorrectly()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "42"),
            new(ClaimTypes.Role, "Owner"),
            new(ClaimTypes.Email, "owner@example.com"),
            new(ClaimTypes.Name, "John Owner")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        Assert.Equal(42, principal.GetUserId());
        Assert.Equal("Owner", principal.GetUserRole());
        Assert.Equal("owner@example.com", principal.GetUserEmail());
        Assert.Equal("John Owner", principal.GetUserName());
        Assert.True(principal.IsOwner());
        Assert.False(principal.IsAdmin());
    }

    [Fact]
    public void DateTimeExtensions_ToTimeAgo_FormatsRelativeStringsCorrectly()
    {
        var now = DateTime.UtcNow;

        Assert.Equal("Just now", now.AddSeconds(-20).ToTimeAgo());
        Assert.Equal("5m ago", now.AddMinutes(-5).ToTimeAgo());
        Assert.Equal("2h ago", now.AddHours(-2).ToTimeAgo());
        Assert.Equal("3d ago", now.AddDays(-3).ToTimeAgo());
        var pastDate = now.AddDays(-35);
        Assert.Equal(pastDate.ToString("dd MMM yyyy"), pastDate.ToTimeAgo());
    }
}
