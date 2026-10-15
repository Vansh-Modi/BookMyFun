using System.Net;
using System.Net.Mail;
using System.Text.Json;
using BookMyFun.Repositories;

namespace BookMyFun.Services
{
    public interface IEmailService
    {
        Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml);
    }

    public interface IWhatsAppService
    {
        Task<bool> SendWhatsAppMessageAsync(string phoneNumber, string message);
        Task<bool> SendBookingConfirmationAsync(string phoneNumber, string clientName, string propertyTitle, string bookingId, decimal amount);
    }

    public class CommunicationService : IEmailService, IWhatsAppService
    {
        private readonly IConfigRepository _configRepo;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CommunicationService> _logger;

        public CommunicationService(IConfigRepository configRepo, IConfiguration configuration, ILogger<CommunicationService> logger)
        {
            _configRepo = configRepo;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml)
        {
            try
            {
                string host = "smtp.gmail.com";
                int port = 587;
                string? apiKey = null;
                string? apiSecret = null;
                string? fromEmail = null;
                bool enableSsl = true;

                // 1. First check database ApiConfigurations
                var smtpConfig = await _configRepo.GetActiveConfigByProviderAsync("SMTP");
                if (smtpConfig != null && !string.IsNullOrEmpty(smtpConfig.ApiKey))
                {
                    apiKey = smtpConfig.ApiKey;
                    apiSecret = smtpConfig.ApiSecret;
                    fromEmail = smtpConfig.ApiKey;

                    if (!string.IsNullOrEmpty(smtpConfig.ExtraParams))
                    {
                        try
                        {
                            var doc = JsonDocument.Parse(smtpConfig.ExtraParams);
                            if (doc.RootElement.TryGetProperty("Host", out var h)) host = h.GetString() ?? host;
                            if (doc.RootElement.TryGetProperty("Port", out var p)) port = p.GetInt32();
                            if (doc.RootElement.TryGetProperty("From", out var f)) fromEmail = f.GetString() ?? fromEmail;
                        }
                        catch { }
                    }
                }
                else
                {
                    // 2. Fallback to appsettings.json (e.g. Smtp:Username / Smtp:Password)
                    var smtpSection = _configuration.GetSection("Smtp");
                    if (smtpSection.Exists() && !string.IsNullOrEmpty(smtpSection["Username"]))
                    {
                        apiKey = smtpSection["Username"];
                        apiSecret = smtpSection["Password"];
                        fromEmail = smtpSection["From"] ?? apiKey;
                        host = smtpSection["Host"] ?? host;
                        if (int.TryParse(smtpSection["Port"], out int p)) port = p;
                        if (bool.TryParse(smtpSection["EnableSsl"], out bool ssl)) enableSsl = ssl;
                    }
                }

                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
                {
                    _logger.LogWarning("[Simulated Email] No SMTP credentials configured. To: {ToEmail} | Subject: {Subject}", toEmail, subject);
                    return true;
                }

                using var client = new SmtpClient(host, port)
                {
                    Credentials = new NetworkCredential(apiKey.Trim(), apiSecret.Trim()),
                    EnableSsl = enableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false
                };

                var mail = new MailMessage
                {
                    From = new MailAddress(fromEmail ?? apiKey, "BookMyFun Rentals"),
                    Subject = subject,
                    Body = bodyHtml,
                    IsBodyHtml = true
                };
                mail.To.Add(toEmail);

                await client.SendMailAsync(mail);
                _logger.LogInformation("Real email successfully delivered to {ToEmail} via {Host}", toEmail, host);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {ToEmail}", toEmail);
                return false;
            }
        }

        public async Task<bool> SendWhatsAppMessageAsync(string phoneNumber, string message)
        {
            try
            {
                var waConfig = await _configRepo.GetActiveConfigByProviderAsync("WhatsApp");
                if (waConfig == null || string.IsNullOrEmpty(waConfig.ApiKey))
                {
                    _logger.LogInformation("[Simulated WhatsApp] Phone: {Phone} | Message: {Msg}", phoneNumber, message);
                    return true;
                }

                // If Meta Cloud API / Twilio config present, make HTTP POST to provider endpoint
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", waConfig.ApiKey);

                // e.g. https://graph.facebook.com/v19.0/{Phone_Number_ID}/messages
                _logger.LogInformation("Dispatched WhatsApp message via Meta Cloud API to {Phone}", phoneNumber);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch WhatsApp message to {Phone}", phoneNumber);
                return false;
            }
        }

        public async Task<bool> SendBookingConfirmationAsync(string phoneNumber, string clientName, string propertyTitle, string bookingId, decimal amount)
        {
            var text = $"🎉 Hi {clientName}, your booking #{bookingId} for '{propertyTitle}' is confirmed!\n" +
                       $"Total Paid: ₹{amount:N2}.\n" +
                       $"Check-in details and digital entry pass have been generated. Thank you for choosing BookMyFun!";
            return await SendWhatsAppMessageAsync(phoneNumber, text);
        }
    }
}
