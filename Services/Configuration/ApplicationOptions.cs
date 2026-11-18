namespace BookMyFun.Services.Configuration
{
    /// <summary>
    /// Strongly-typed SMTP configuration options.
    /// </summary>
    public class SmtpOptions
    {
        public const string SectionName = "Smtp";

        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string From { get; set; } = "noreply@bookmyfun.com";
        public bool EnableSsl { get; set; } = true;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Host) &&
            !string.IsNullOrWhiteSpace(Username) &&
            !string.IsNullOrWhiteSpace(Password);
    }

    /// <summary>
    /// Strongly-typed Razorpay gateway configuration options.
    /// </summary>
    public class RazorpayOptions
    {
        public const string SectionName = "Razorpay";

        public string KeyId { get; set; } = string.Empty;
        public string KeySecret { get; set; } = string.Empty;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(KeyId) &&
            !string.IsNullOrWhiteSpace(KeySecret);
    }

    /// <summary>
    /// Strongly-typed Google OAuth configuration options.
    /// </summary>
    public class GoogleAuthOptions
    {
        public const string SectionName = "Authentication:Google";

        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string ProjectEmail { get; set; } = string.Empty;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ClientId) &&
            !string.IsNullOrWhiteSpace(ClientSecret);
    }

    /// <summary>
    /// Strongly-typed WhatsApp Cloud API configuration options.
    /// </summary>
    public class WhatsAppOptions
    {
        public const string SectionName = "WhatsApp";

        public string ApiKey { get; set; } = string.Empty;
        public string PhoneNumberId { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ApiKey) &&
            !string.IsNullOrWhiteSpace(PhoneNumberId);
    }

    /// <summary>
    /// Strongly-typed Database lifecycle options.
    /// </summary>
    public class DatabaseOptions
    {
        public const string SectionName = "Database";

        /// <summary>
        /// Controls whether DatabaseInitializer executes DDL migrations at startup.
        /// Defaults to false in Production to avoid unexpected DDL locks or migration failures.
        /// </summary>
        public bool AutoMigrate { get; set; } = false;
    }
}
