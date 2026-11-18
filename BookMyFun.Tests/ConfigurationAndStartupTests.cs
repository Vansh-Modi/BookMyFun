using BookMyFun.Services.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BookMyFun.Tests;

public class ConfigurationAndStartupTests
{
    [Fact]
    public void SmtpOptions_IsConfigured_ReturnsTrueOnlyWhenComplete()
    {
        var options = new SmtpOptions();
        Assert.False(options.IsConfigured);

        options.Host = "smtp.example.com";
        Assert.False(options.IsConfigured);

        options.Username = "user@example.com";
        Assert.False(options.IsConfigured);

        options.Password = "secret123";
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void RazorpayOptions_IsConfigured_ReturnsTrueOnlyWhenBothKeysPresent()
    {
        var options = new RazorpayOptions();
        Assert.False(options.IsConfigured);

        options.KeyId = "rzp_test_123";
        Assert.False(options.IsConfigured);

        options.KeySecret = "secret_456";
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void GoogleAuthOptions_IsConfigured_ReturnsTrueOnlyWhenClientCredentialsPresent()
    {
        var options = new GoogleAuthOptions();
        Assert.False(options.IsConfigured);

        options.ClientId = "client_id_123";
        Assert.False(options.IsConfigured);

        options.ClientSecret = "client_secret_456";
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void WhatsAppOptions_IsConfigured_ReturnsTrueOnlyWhenApiAndPhoneSet()
    {
        var options = new WhatsAppOptions();
        Assert.False(options.IsConfigured);

        options.ApiKey = "wa_api_key";
        Assert.False(options.IsConfigured);

        options.PhoneNumberId = "1234567890";
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void DatabaseOptions_DefaultsToAutoMigrateFalse()
    {
        var options = new DatabaseOptions();
        Assert.False(options.AutoMigrate);
    }

    [Fact]
    public void ConfigurationBinding_BindsOptionsCorrectly()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "mail.myfun.com",
            ["Smtp:Port"] = "465",
            ["Smtp:Username"] = "alerts@myfun.com",
            ["Smtp:Password"] = "SuperSecret",
            ["Smtp:From"] = "alerts@myfun.com",
            ["Smtp:EnableSsl"] = "true",
            ["Razorpay:KeyId"] = "rzp_live_test",
            ["Razorpay:KeySecret"] = "rzp_sec_test",
            ["Database:AutoMigrate"] = "true"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemory)
            .Build();

        var smtp = config.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>();
        Assert.NotNull(smtp);
        Assert.Equal("mail.myfun.com", smtp.Host);
        Assert.Equal(465, smtp.Port);
        Assert.True(smtp.IsConfigured);

        var rzp = config.GetSection(RazorpayOptions.SectionName).Get<RazorpayOptions>();
        Assert.NotNull(rzp);
        Assert.True(rzp.IsConfigured);

        var db = config.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>();
        Assert.NotNull(db);
        Assert.True(db.AutoMigrate);
    }
}
