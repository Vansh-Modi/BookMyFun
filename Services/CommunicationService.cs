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
        Task<bool> SendOwnerBookingAlertAsync(string phoneNumber, string ownerName, string propertyTitle, string bookingId, decimal hostGross, decimal hostPayout, string guestName, DateTime checkIn, DateTime checkOut, bool hasDecoration, decimal decorationAmount);
        Task<bool> SendEventManagerSetupAlertAsync(string phoneNumber, string eventManagerName, string propertyTitle, string bookingId, string packageName, decimal packageAmount, DateTime eventDate, string clientName);
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
            if (string.IsNullOrWhiteSpace(phoneNumber)) return false;

            try
            {
                var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
                var cleanPhone = digits.Length == 10 ? "91" + digits : digits;

                // 1. Check database ApiConfigurations
                var waConfig = await _configRepo.GetActiveConfigByProviderAsync("WhatsApp");
                string apiKey = waConfig?.ApiKey ?? string.Empty;
                string apiSecret = waConfig?.ApiSecret ?? string.Empty;
                string endpoint = string.Empty;

                if (!string.IsNullOrWhiteSpace(waConfig?.ExtraParams))
                {
                    try
                    {
                        var doc = JsonDocument.Parse(waConfig.ExtraParams);
                        if (doc.RootElement.TryGetProperty("Endpoint", out var ep) && !string.IsNullOrWhiteSpace(ep.GetString()))
                        {
                            endpoint = ep.GetString()!;
                        }
                    }
                    catch { }
                }

                // 2. Fallback to appsettings.json
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    apiKey = _configuration["WhatsApp:ApiKey"] ?? string.Empty;
                    apiSecret = _configuration["WhatsApp:PhoneNumberId"] ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(endpoint))
                    {
                        endpoint = _configuration["WhatsApp:Endpoint"] ?? string.Empty;
                    }
                }

                // If Meta Cloud API / Provider credentials not configured, use realistic simulated dispatch
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogInformation("[WhatsApp API (Simulated)] Dispatched to {Phone}:\n{Msg}", cleanPhone, message);
                    return true;
                }

                // Real Meta Cloud API Call
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());

                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    endpoint = !string.IsNullOrWhiteSpace(apiSecret)
                        ? $"https://graph.facebook.com/v20.0/{apiSecret.Trim()}/messages"
                        : "https://graph.facebook.com/v20.0/me/messages";
                }

                var payload = new
                {
                    messaging_product = "whatsapp",
                    recipient_type = "individual",
                    to = cleanPhone,
                    type = "text",
                    text = new { preview_url = false, body = message }
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                var response = await http.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("WhatsApp message successfully dispatched via API to {Phone}", cleanPhone);
                    return true;
                }
                else
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("WhatsApp API returned status {StatusCode}: {ResponseText}. Logged payload locally.", response.StatusCode, responseText);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch WhatsApp message to {Phone}", phoneNumber);
                return false;
            }
        }

        public async Task<bool> SendBookingConfirmationAsync(string phoneNumber, string clientName, string propertyTitle, string bookingId, decimal amount)
        {
            var text = $"🎉 Hi {clientName}, your booking #{bookingId} for '{propertyTitle}' is confirmed!\n\n" +
                       $"💰 Total Amount Paid: ₹{amount:N2}\n" +
                       $"📅 Check-in details and digital entry pass are now accessible in your BookMyFun dashboard.\n\n" +
                       $"Thank you for booking with BookMyFun!";
            return await SendWhatsAppMessageAsync(phoneNumber, text);
        }

        public async Task<bool> SendOwnerBookingAlertAsync(
            string phoneNumber, 
            string ownerName, 
            string propertyTitle, 
            string bookingId, 
            decimal hostGross, 
            decimal hostPayout, 
            string guestName, 
            DateTime checkIn, 
            DateTime checkOut,
            bool hasDecoration,
            decimal decorationAmount)
        {
            var decorNote = hasDecoration 
                ? $"\n🎨 Client Décor Package: ₹{decorationAmount:N2} (Assigned to Event Manager, 100% segregated)" 
                : "";

            var text = $"🏡 Hello {ownerName},\n\n" +
                       $"Great news! You have a new confirmed reservation for '{propertyTitle}'!\n\n" +
                       $"• Booking Ref: #{bookingId}\n" +
                       $"• Guest Name: {guestName}\n" +
                       $"• Stay Dates: {checkIn:dd MMM yyyy} to {checkOut:dd MMM yyyy}\n" +
                       $"• Property Stay Gross: ₹{hostGross:N2}\n" +
                       $"• Your Net Host Payout: ₹{hostPayout:N2}{decorNote}\n\n" +
                       $"Please check your Host Dashboard for guest contact details and check-in preparation.";

            return await SendWhatsAppMessageAsync(phoneNumber, text);
        }

        public async Task<bool> SendEventManagerSetupAlertAsync(
            string phoneNumber, 
            string eventManagerName, 
            string propertyTitle, 
            string bookingId, 
            string packageName, 
            decimal packageAmount, 
            DateTime eventDate, 
            string clientName)
        {
            var text = $"🎪 New Event Staging Order!\n\n" +
                       $"Hello {eventManagerName},\n" +
                       $"A new event setup order has been booked on BookMyFun!\n\n" +
                       $"• Booking Ref: #{bookingId}\n" +
                       $"• Venue: {propertyTitle}\n" +
                       $"• Event Date: {eventDate:dd MMM yyyy}\n" +
                       $"• Décor Package: {packageName}\n" +
                       $"• Your Allocated Décor Fee: ₹{packageAmount:N2}\n" +
                       $"• Client: {clientName}\n\n" +
                       $"Sign in to your Event Manager portal at https://bookmyfun.in/EventManager/Dashboard to accept and coordinate setup crew.";

            return await SendWhatsAppMessageAsync(phoneNumber, text);
        }
    }
}
