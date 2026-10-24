using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;

namespace BookMyFun.Services
{
    public interface IEmailService
    {
        Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml);
        Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml, string? plainTextBody) => SendEmailAsync(toEmail, subject, bodyHtml);
        Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string purpose, string? recipientName = null);
        Task EnqueueEmailAsync(string toEmail, string subject, string bodyHtml, string messageType = "Notification");
        Task<bool> SendBookingReceiptEmailAsync(string toEmail, string clientName, BookingReceiptViewModel receipt, string receiptUrl) => Task.FromResult(true);
        Task<bool> SendOwnerSettlementReceiptEmailAsync(string toEmail, string ownerName, BookingReceiptViewModel receipt, string settlementReceiptUrl) => Task.FromResult(true);
        Task<bool> SendKycRejectionNotificationAsync(string toEmail, string clientName, string? rejectionReason, int clientId, string? documentType = null) => Task.FromResult(true);
        Task<bool> SendCancellationCustomerNotificationAsync(int clientId, string clientEmail, string? clientPhone, bool whatsappOptIn, int bookingId, string propertyTitle, int ruleApplied, decimal refundAmount, string timeline = "5 to 7 business days") => Task.FromResult(true);
        Task<bool> SendCancellationOwnerNotificationAsync(int ownerId, string ownerEmail, int bookingId, string propertyTitle, decimal amountReceivedOrLost, bool isReceived, string reason) => Task.FromResult(true);
        Task DispatchConfirmedBookingNotificationsAsync(BookMyFun.Models.Entities.Booking booking, BookMyFun.Models.Entities.Property property, BookMyFun.Models.Entities.User client, BookMyFun.Models.Entities.User? owner, BookMyFun.Models.Entities.User? eventManager, string? baseUrl = null) => Task.CompletedTask;
        Task DispatchCancellationNotificationsAsync(BookMyFun.Models.Entities.Booking booking, BookMyFun.Models.Entities.Property property, BookMyFun.Models.Entities.User client, BookMyFun.Models.Entities.User? owner, BookMyFun.Models.Entities.User? eventManager, int ruleApplied, decimal amountPaid, decimal gatewayDeduction, decimal netRefund, string expectedTimeline, string cancellationReason, decimal ownerPayoutAdjustment, decimal heldBalanceChange, string? baseUrl = null) => Task.CompletedTask;
    }

    public interface IWhatsAppService
    {
        Task<bool> SendWhatsAppMessageAsync(string phoneNumber, string message);
        Task EnqueueWhatsAppAsync(string phoneNumber, string message, string messageType = "Notification");
        Task<bool> SendBookingConfirmationAsync(string phoneNumber, string clientName, string propertyTitle, string bookingId, decimal amount);
        Task<bool> SendOwnerBookingAlertAsync(string phoneNumber, string ownerName, string propertyTitle, string bookingId, decimal hostGross, decimal hostPayout, string guestName, DateTime checkIn, DateTime checkOut, bool hasDecoration, decimal decorationAmount);
        Task<bool> SendEventManagerSetupAlertAsync(string phoneNumber, string eventManagerName, string propertyTitle, string bookingId, string packageName, decimal packageAmount, DateTime eventDate, string clientName);
        Task<bool> SendBookingReceiptWhatsAppAsync(string phoneNumber, string clientName, string propertyTitle, string bookingId, string receiptNumber, decimal totalAmount, string receiptUrl, DateTime checkIn, DateTime checkOut) => Task.FromResult(true);
        Task<bool> SendOwnerBookingReceiptWhatsAppAsync(string phoneNumber, string ownerName, string propertyTitle, string bookingId, string hostReceiptNumber, decimal hostGross, decimal hostPayout, string guestName, DateTime checkIn, DateTime checkOut, bool hasDecoration, decimal decorationAmount, string settlementReceiptUrl) => Task.FromResult(true);
    }

    public class CommunicationService : IEmailService, IWhatsAppService
    {
        private readonly IConfigRepository _configRepo;
        private readonly IConfiguration _configuration;
        private readonly IEmailQueueService _emailQueue;
        private readonly ILogger<CommunicationService> _logger;
        private readonly IHttpClientFactory? _httpClientFactory;

        public CommunicationService(IConfigRepository configRepo, IConfiguration configuration, ILogger<CommunicationService> logger)
            : this(configRepo, configuration, new EmailQueueService(Microsoft.Extensions.Logging.Abstractions.NullLogger<EmailQueueService>.Instance), logger, null)
        {
        }

        public CommunicationService(
            IConfigRepository configRepo, 
            IConfiguration configuration,
            IEmailQueueService emailQueue,
            ILogger<CommunicationService> logger,
            IHttpClientFactory? httpClientFactory = null)
        {
            _configRepo = configRepo;
            _configuration = configuration;
            _emailQueue = emailQueue;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml)
            => SendEmailAsync(toEmail, subject, bodyHtml, null);

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml, string? plainTextBody)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                _logger.LogWarning("Email dispatch aborted: destination address is empty.");
                return false;
            }

            // Sanitize recipient email
            string cleanToEmail = toEmail.Trim();
            if (!MailAddress.TryCreate(cleanToEmail, out var parsedToAddress))
            {
                _logger.LogError("Email dispatch aborted: '{ToEmail}' is not a valid email address.", cleanToEmail);
                return false;
            }

            try
            {
                string host = "smtp.gmail.com";
                int port = 587;
                string? apiKey = null;
                string? apiSecret = null;
                string? fromEmail = null;
                bool enableSsl = true;

                // 1. Check database ApiConfigurations
                try
                {
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
                                if (doc.RootElement.TryGetProperty("Host", out var h) && !string.IsNullOrWhiteSpace(h.GetString())) host = h.GetString()!;
                                if (doc.RootElement.TryGetProperty("Port", out var p)) port = p.GetInt32();
                                if (doc.RootElement.TryGetProperty("From", out var f) && !string.IsNullOrWhiteSpace(f.GetString())) fromEmail = f.GetString()!;
                                if (doc.RootElement.TryGetProperty("EnableSsl", out var ssl)) enableSsl = ssl.GetBoolean();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to parse SMTP ExtraParams JSON from database.");
                            }
                        }
                    }
                }
                catch (Exception dbEx)
                {
                    _logger.LogWarning(dbEx, "Database ApiConfigurations lookup failed for SMTP provider. Falling back to configuration.");
                }

                // 2. Fallback to appsettings.json
                if (string.IsNullOrEmpty(apiKey))
                {
                    var smtpSection = _configuration.GetSection("Smtp");
                    if (smtpSection.Exists())
                    {
                        apiKey = smtpSection["Username"];
                        apiSecret = smtpSection["Password"];
                        fromEmail = smtpSection["From"];
                        if (!string.IsNullOrWhiteSpace(smtpSection["Host"])) host = smtpSection["Host"]!;
                        if (int.TryParse(smtpSection["Port"], out int p)) port = p;
                        if (bool.TryParse(smtpSection["EnableSsl"], out bool ssl)) enableSsl = ssl;
                    }
                }

                // 3. Fallback to Environment Variables
                if (string.IsNullOrEmpty(apiKey))
                {
                    apiKey = Environment.GetEnvironmentVariable("SMTP_USERNAME");
                    apiSecret = Environment.GetEnvironmentVariable("SMTP_PASSWORD");
                    fromEmail = Environment.GetEnvironmentVariable("SMTP_FROM") ?? fromEmail;
                    string? envHost = Environment.GetEnvironmentVariable("SMTP_HOST");
                    if (!string.IsNullOrWhiteSpace(envHost)) host = envHost;
                    string? envPort = Environment.GetEnvironmentVariable("SMTP_PORT");
                    if (int.TryParse(envPort, out int ep)) port = ep;
                }

                // If no credentials found, simulate delivery safely in development/test
                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
                {
                    _logger.LogWarning("[Simulated Email - No SMTP Credentials] To: {ToEmail} | Subject: {Subject}", cleanToEmail, subject);
                    return true;
                }

                // Sanitize credentials: strip internal whitespace from passwords (handles Google App Passwords like 'xxxx xxxx xxxx xxxx')
                string cleanUser = apiKey.Trim();
                string cleanPass = apiSecret.Trim().Replace(" ", "");

                // Sanitize sender address: ensure strictly valid email address syntax
                string senderAddress = "no-reply@bookmyfun.in";
                if (!string.IsNullOrWhiteSpace(fromEmail) && MailAddress.TryCreate(fromEmail.Trim(), out var parsedFrom))
                {
                    senderAddress = parsedFrom.Address;
                }
                else if (MailAddress.TryCreate(cleanUser, out var parsedUserAsEmail))
                {
                    senderAddress = parsedUserAsEmail.Address;
                }

                using var client = new SmtpClient(host, port)
                {
                    Credentials = new NetworkCredential(cleanUser, cleanPass),
                    EnableSsl = enableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Timeout = 10000 // 10 seconds timeout to prevent thread-blocking hangs
                };

                var mail = new MailMessage
                {
                    From = new MailAddress(senderAddress, "BookMyFun"),
                    Subject = subject,
                    Body = bodyHtml,
                    IsBodyHtml = true
                };
                mail.To.Add(parsedToAddress);

                // Anti-spam and RFC compliance headers
                mail.Headers.Add("X-Auto-Response-Suppress", "All");
                mail.Headers.Add("Auto-Submitted", "auto-generated");

                // Provide multi-part alternative views (plain text + HTML) for maximum deliverability
                string plainText = !string.IsNullOrWhiteSpace(plainTextBody)
                    ? plainTextBody
                    : Regex.Replace(bodyHtml, "<[^>]+>", " ").Replace("&nbsp;", " ").Trim();

                var plainView = AlternateView.CreateAlternateViewFromString(plainText, System.Text.Encoding.UTF8, "text/plain");
                var htmlView = AlternateView.CreateAlternateViewFromString(bodyHtml, System.Text.Encoding.UTF8, "text/html");
                mail.AlternateViews.Add(plainView);
                mail.AlternateViews.Add(htmlView);

                await client.SendMailAsync(mail);
                _logger.LogInformation("Real email successfully delivered to {ToEmail} via {Host}:{Port}", cleanToEmail, host, port);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {ToEmail}", cleanToEmail);
                return false;
            }
        }

        public async Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string purpose, string? recipientName = null)
        {
            if (string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(otpCode))
                return false;

            string subject;
            string purposeHeadline;
            string purposeDescription;
            string securityNote;

            switch (purpose?.ToLowerInvariant())
            {
                case "forgotpassword":
                    subject = "Reset your BookMyFun password";
                    purposeHeadline = "Password Reset Request";
                    purposeDescription = "We received a request to reset the password for your BookMyFun account. Enter the verification code below to set a new password:";
                    securityNote = "If you did not request this password reset, please ignore this email or contact support immediately if you suspect unauthorized activity.";
                    break;
                case "login":
                    subject = "Your BookMyFun sign-in code";
                    purposeHeadline = "Secure Account Sign-In";
                    purposeDescription = "Use this verification code to complete your sign-in to BookMyFun:";
                    securityNote = "Never share this code with anyone. BookMyFun employees will never ask for your verification code.";
                    break;
                case "registration":
                default:
                    subject = "Your BookMyFun verification code";
                    purposeHeadline = "Account Email Verification";
                    purposeDescription = "Thank you for creating an account with BookMyFun! Please verify your email address with the verification code below:";
                    securityNote = "This code confirms your email address and activates your BookMyFun guest account.";
                    break;
            }

            string greeting = !string.IsNullOrWhiteSpace(recipientName) ? $"Hello {recipientName.Trim()}," : "Hello,";

            string bodyHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{subject}</title>
</head>
<body style='margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; background-color: #F8FAFC; color: #0F172A;'>
    <table role='presentation' width='100%' cellspacing='0' cellpadding='0' border='0' style='background-color: #F8FAFC; padding: 30px 15px;'>
        <tr>
            <td align='center'>
                <table role='presentation' width='100%' style='max-width: 540px; background-color: #FFFFFF; border-radius: 16px; border: 1px solid #E2E8F0; overflow: hidden; box-shadow: 0 4px 20px rgba(0, 0, 0, 0.05);' cellspacing='0' cellpadding='0' border='0'>
                    <!-- Header -->
                    <tr>
                        <td style='background: linear-gradient(135deg, #0B4F6C 0%, #01BAEF 100%); padding: 32px 30px; text-align: center;'>
                            <h1 style='color: #FFFFFF; margin: 0; font-size: 26px; font-weight: 800; letter-spacing: -0.02em;'>BookMyFun</h1>
                            <p style='color: #E0F2FE; font-size: 13px; font-weight: 500; margin: 6px 0 0; text-transform: uppercase; letter-spacing: 0.05em;'>Luxury Farmhouse & Resort Booking</p>
                        </td>
                    </tr>
                    <!-- Content -->
                    <tr>
                        <td style='padding: 35px 30px;'>
                            <h2 style='color: #0F172A; font-size: 20px; font-weight: 700; margin: 0 0 12px;'>{purposeHeadline}</h2>
                            <p style='font-size: 15px; line-height: 1.6; color: #334155; margin: 0 0 16px;'>{greeting}</p>
                            <p style='font-size: 15px; line-height: 1.6; color: #334155; margin: 0 0 24px;'>{purposeDescription}</p>

                            <!-- OTP Box -->
                            <div style='background: #FFFBEB; border: 2px dashed #F59E0B; border-radius: 12px; padding: 20px; text-align: center; margin: 25px 0;'>
                                <span style='display: block; font-size: 12px; font-weight: 700; text-transform: uppercase; color: #B45309; letter-spacing: 0.1em; margin-bottom: 6px;'>Verification Code</span>
                                <span style='font-family: monospace, ""Courier New"", Courier; color: #D97706; font-size: 38px; font-weight: 800; letter-spacing: 10px; display: inline-block;'>{otpCode}</span>
                                <span style='display: block; font-size: 12px; color: #78350F; margin-top: 6px;'>Valid for 150 seconds (2.5 minutes)</span>
                            </div>

                            <p style='font-size: 13px; line-height: 1.6; color: #64748B; margin: 20px 0 0;'>
                                <strong>Security Notice:</strong> {securityNote}
                            </p>
                        </td>
                    </tr>
                    <!-- Footer -->
                    <tr>
                        <td style='background-color: #F8FAFC; border-top: 1px solid #E2E8F0; padding: 20px 30px; text-align: center;'>
                            <p style='font-size: 12px; color: #94A3B8; margin: 0; line-height: 1.5;'>
                                &copy; {DateTime.UtcNow.Year} BookMyFun Technologies. All rights reserved.<br>
                                Need assistance? Visit our <a href='https://bookmyfun.in/Home/Contact' style='color: #0B4F6C; text-decoration: underline;'>Help Center</a>.
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";

            string plainTextBody = $@"{greeting}

{purposeHeadline}
{purposeDescription}

Verification Code: {otpCode}
(This code is valid for 150 seconds / 2.5 minutes)

Security Notice: {securityNote}

Need assistance? Visit https://bookmyfun.in/Home/Contact
© {DateTime.UtcNow.Year} BookMyFun Technologies. All rights reserved.";

            return await SendEmailAsync(toEmail, subject, bodyHtml, plainTextBody);
        }

        public async Task EnqueueEmailAsync(string toEmail, string subject, string bodyHtml, string messageType = "Notification")
        {
            if (string.IsNullOrWhiteSpace(toEmail)) return;

            await _emailQueue.EnqueueAsync(new EmailQueueMessage
            {
                Channel = "Email",
                ToEmail = toEmail.Trim(),
                Subject = subject,
                BodyHtml = bodyHtml,
                MessageType = messageType,
                QueuedAtUtc = DateTime.UtcNow
            });
        }

        public async Task EnqueueWhatsAppAsync(string phoneNumber, string message, string messageType = "Notification")
        {
            if (string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(message)) return;

            await _emailQueue.EnqueueAsync(new EmailQueueMessage
            {
                Channel = "WhatsApp",
                ToPhone = phoneNumber.Trim(),
                BodyHtml = message,
                MessageType = messageType,
                QueuedAtUtc = DateTime.UtcNow
            });
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
                var http = _httpClientFactory != null
                    ? _httpClientFactory.CreateClient("WhatsApp")
                    : new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
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

        public async Task<bool> SendBookingReceiptEmailAsync(string toEmail, string clientName, BookingReceiptViewModel receipt, string receiptUrl)
        {
            if (string.IsNullOrWhiteSpace(toEmail) || receipt == null) return false;

            string mapLinkHtml = !string.IsNullOrWhiteSpace(receipt.Property?.MapUrl)
                ? $"<p style='margin: 8px 0 0;'><a href='{receipt.Property.MapUrl}' target='_blank' style='color: #0b4f6c; font-weight: bold; text-decoration: underline;'>📍 View on Google Maps / Driving Directions</a></p>"
                : "";

            string decorRowHtml = receipt.EventPackagePrice > 0
                ? $@"<tr>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #e2e8f0; color: #475569;'>Celebration Décor Package ({receipt.Booking.EventPackageName ?? "Selected Theme"})</td>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #e2e8f0; text-align: right; color: #0284c7; font-weight: 600;'>₹{receipt.EventPackagePrice:N2}</td>
                     </tr>"
                : "";

            string depositRowHtml = receipt.SecurityDeposit > 0
                ? $@"<tr>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #e2e8f0; color: #475569;'>Refundable Escrow Security Deposit</td>
                        <td style='padding: 10px 12px; border-bottom: 1px solid #e2e8f0; text-align: right; color: #475569;'>₹{receipt.SecurityDeposit:N2}</td>
                     </tr>"
                : "";

            string checkInTime = receipt.Booking.SlotStartTime.HasValue 
                ? DateTime.Today.Add(receipt.Booking.SlotStartTime.Value).ToString("hh:mm tt") 
                : "02:00 PM";
            string checkOutTime = receipt.Booking.SlotEndTime.HasValue 
                ? DateTime.Today.Add(receipt.Booking.SlotEndTime.Value).ToString("hh:mm tt") 
                : "11:00 AM";

            string html = $@"
                <div style='font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; background-color: #f8fafc; padding: 24px; color: #0f172a;'>
                    <div style='max-width: 640px; margin: 0 auto; background: #ffffff; border-radius: 16px; overflow: hidden; border: 1px solid #e2e8f0; box-shadow: 0 10px 25px -5px rgba(0,0,0,0.05);'>
                        <div style='background: linear-gradient(135deg, #0b4f6c 0%, #01baef 100%); padding: 32px 28px; text-align: center; color: #ffffff;'>
                            <span style='background: rgba(255,255,255,0.2); padding: 4px 12px; border-radius: 999px; font-size: 12px; font-weight: bold; letter-spacing: 0.05em; text-transform: uppercase;'>Official Booking Receipt &amp; Gate Pass</span>
                            <h1 style='margin: 12px 0 4px; font-size: 24px; font-weight: 800; letter-spacing: -0.02em;'>BookMyFun</h1>
                            <p style='margin: 0; font-size: 14px; opacity: 0.9;'>Reservation #{receipt.Booking.BookingID} &bull; {receipt.ReceiptNumber}</p>
                        </div>

                        <div style='padding: 28px;'>
                            <p style='margin-top: 0; font-size: 16px;'>Dear <strong>{clientName}</strong>,</p>
                            <p style='color: #475569; font-size: 14px; line-height: 1.6;'>
                                Your booking for <strong>{receipt.Property?.Title}</strong> is confirmed and payment has been verified. 
                                Below is your official tax invoice receipt and digital check-in gate pass.
                            </p>

                            <div style='background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 12px; padding: 18px; margin: 20px 0;'>
                                <table style='width: 100%; font-size: 13px; color: #334155; border-collapse: collapse;'>
                                    <tr>
                                        <td style='padding: 4px 0;'><strong>Receipt Number:</strong></td>
                                        <td style='padding: 4px 0; text-align: right; font-family: monospace; font-weight: bold; color: #0b4f6c;'>{receipt.ReceiptNumber}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 4px 0;'><strong>Tax Invoice No:</strong></td>
                                        <td style='padding: 4px 0; text-align: right; font-family: monospace;'>{receipt.TaxInvoiceNumber}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 4px 0;'><strong>Check-In:</strong></td>
                                        <td style='padding: 4px 0; text-align: right;'>{receipt.Booking.CheckInDate:dd MMM yyyy} ({checkInTime})</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 4px 0;'><strong>Check-Out:</strong></td>
                                        <td style='padding: 4px 0; text-align: right;'>{receipt.Booking.CheckOutDate:dd MMM yyyy} ({checkOutTime})</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 4px 0;'><strong>Destination:</strong></td>
                                        <td style='padding: 4px 0; text-align: right;'>{receipt.Property?.Location}</td>
                                    </tr>
                                </table>
                                {mapLinkHtml}
                            </div>

                            <table style='width: 100%; border-collapse: collapse; margin: 24px 0; font-size: 14px;'>
                                <thead>
                                    <tr style='background: #f1f5f9; text-align: left; color: #475569; font-size: 12px; text-transform: uppercase;'>
                                        <th style='padding: 10px 12px; border-radius: 8px 0 0 8px;'>Description</th>
                                        <th style='padding: 10px 12px; text-align: right; border-radius: 0 8px 8px 0;'>Amount</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    <tr>
                                        <td style='padding: 10px 12px; border-bottom: 1px solid #e2e8f0; color: #475569;'>Property Stay ({receipt.Property?.Title})</td>
                                        <td style='padding: 10px 12px; border-bottom: 1px solid #e2e8f0; text-align: right; font-weight: 600;'>₹{receipt.PropertyStayGross:N2}</td>
                                    </tr>
                                    {decorRowHtml}
                                    {depositRowHtml}
                                    <tr style='background: #f8fafc;'>
                                        <td style='padding: 14px 12px; font-weight: 800; font-size: 15px; color: #0f172a;'>Total Amount Paid</td>
                                        <td style='padding: 14px 12px; text-align: right; font-weight: 800; font-size: 16px; color: #059669;'>₹{receipt.TotalPaid:N2}</td>
                                    </tr>
                                </tbody>
                            </table>

                            <div style='background: #ecfdf5; border: 1px solid #a7f3d0; border-radius: 12px; padding: 16px; margin: 20px 0;'>
                                <p style='margin: 0; color: #065f46; font-size: 13px; line-height: 1.5;'>
                                    <strong>🛡️ Gate Pass Instructions:</strong> Present this digital pass or your printed official receipt to the property host upon arrival. Valid government photo ID is required for all guests.
                                </p>
                            </div>

                            <div style='text-align: center; margin: 30px 0 10px;'>
                                <a href='{receiptUrl}' style='display: inline-block; background: #0b4f6c; color: #ffffff; font-weight: bold; text-decoration: none; padding: 14px 28px; border-radius: 10px; font-size: 14px;'>
                                    Download Official Tax Receipt
                                </a>
                            </div>
                        </div>

                        <div style='background: #f8fafc; border-top: 1px solid #e2e8f0; padding: 20px; text-align: center; font-size: 12px; color: #64748b;'>
                            <p style='margin: 0 0 6px;'>BookMyFun &bull; Luxury Farmhouses, Villas &amp; Celebration Resorts</p>
                            <p style='margin: 0;'>For instant assistance, contact our 24/7 helpdesk at <a href='mailto:support@bookmyfun.in' style='color: #0b4f6c;'>support@bookmyfun.in</a></p>
                        </div>
                    </div>
                </div>";

            string subject = $"Official Booking Receipt & Tax Invoice #{receipt.ReceiptNumber} - {receipt.Property?.Title ?? "BookMyFun"}";
            await EnqueueEmailAsync(toEmail, subject, html, "BookingReceipt");
            return true;
        }

        public async Task<bool> SendOwnerSettlementReceiptEmailAsync(string toEmail, string ownerName, BookingReceiptViewModel receipt, string settlementReceiptUrl)
        {
            if (string.IsNullOrWhiteSpace(toEmail) || receipt == null) return false;

            string decorDisclosureHtml = receipt.EventPackagePrice > 0
                ? $@"<div style='background: #f8fafc; border-left: 4px solid #0284c7; padding: 12px; margin: 16px 0; font-size: 13px; color: #334155;'>
                        <strong>🎨 Celebration Décor Add-on Notice:</strong> Client added a themed décor package for ₹{receipt.EventPackagePrice:N2}. This fee is allocated 100% to the Event Decorator Studio and does not reduce your host payout.
                     </div>"
                : "";

            string html = $@"
                <div style='font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; background-color: #f8fafc; padding: 24px; color: #0f172a;'>
                    <div style='max-width: 640px; margin: 0 auto; background: #ffffff; border-radius: 16px; overflow: hidden; border: 1px solid #e2e8f0; box-shadow: 0 10px 25px -5px rgba(0,0,0,0.05);'>
                        <div style='background: linear-gradient(135deg, #059669 0%, #10b981 100%); padding: 32px 28px; text-align: center; color: #ffffff;'>
                            <span style='background: rgba(255,255,255,0.2); padding: 4px 12px; border-radius: 999px; font-size: 12px; font-weight: bold; letter-spacing: 0.05em; text-transform: uppercase;'>Host Partner Settlement Statement</span>
                            <h1 style='margin: 12px 0 4px; font-size: 24px; font-weight: 800; letter-spacing: -0.02em;'>BookMyFun Host Partner</h1>
                            <p style='margin: 0; font-size: 14px; opacity: 0.9;'>Settlement #{receipt.HostReceiptNumber} &bull; Booking #{receipt.Booking.BookingID}</p>
                        </div>

                        <div style='padding: 28px;'>
                            <p style='margin-top: 0; font-size: 16px;'>Dear <strong>{ownerName}</strong>,</p>
                            <p style='color: #475569; font-size: 14px; line-height: 1.6;'>
                                A new reservation for <strong>{receipt.Property?.Title}</strong> has been confirmed. 
                                Below is your transparent financial settlement statement.
                            </p>

                            <div style='background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 12px; padding: 18px; margin: 20px 0;'>
                                <table style='width: 100%; font-size: 13px; color: #334155; border-collapse: collapse;'>
                                    <tr>
                                        <td style='padding: 4px 0;'><strong>Host Settlement Ref:</strong></td>
                                        <td style='padding: 4px 0; text-align: right; font-family: monospace; font-weight: bold; color: #059669;'>{receipt.HostReceiptNumber}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 4px 0;'><strong>Booking Reference:</strong></td>
                                        <td style='padding: 4px 0; text-align: right; font-family: monospace;'>#{receipt.Booking.BookingID}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 4px 0;'><strong>Primary Guest:</strong></td>
                                        <td style='padding: 4px 0; text-align: right;'>{receipt.Client?.FullName ?? "Guest"}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 4px 0;'><strong>Stay Dates:</strong></td>
                                        <td style='padding: 4px 0; text-align: right;'>{receipt.Booking.CheckInDate:dd MMM yyyy} to {receipt.Booking.CheckOutDate:dd MMM yyyy}</td>
                                    </tr>
                                </table>
                            </div>

                            <table style='width: 100%; border-collapse: collapse; margin: 24px 0; font-size: 14px;'>
                                <thead>
                                    <tr style='background: #f1f5f9; text-align: left; color: #475569; font-size: 12px; text-transform: uppercase;'>
                                        <th style='padding: 10px 12px; border-radius: 8px 0 0 8px;'>Financial Component</th>
                                        <th style='padding: 10px 12px; text-align: right; border-radius: 0 8px 8px 0;'>Amount</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    <tr>
                                        <td style='padding: 10px 12px; border-bottom: 1px solid #e2e8f0; color: #475569;'>Property Stay Gross Revenue</td>
                                        <td style='padding: 10px 12px; border-bottom: 1px solid #e2e8f0; text-align: right; font-weight: 600;'>₹{receipt.PropertyStayGross:N2}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 10px 12px; border-bottom: 1px solid #e2e8f0; color: #dc2626;'>Platform Commission Fee ({receipt.CommissionRate:0.#}%)</td>
                                        <td style='padding: 10px 12px; border-bottom: 1px solid #e2e8f0; text-align: right; color: #dc2626;'>-₹{receipt.PlatformCommission:N2}</td>
                                    </tr>
                                    <tr style='background: #ecfdf5;'>
                                        <td style='padding: 14px 12px; font-weight: 800; font-size: 15px; color: #065f46;'>Net Host Payout</td>
                                        <td style='padding: 14px 12px; text-align: right; font-weight: 800; font-size: 16px; color: #059669;'>₹{receipt.HostNetPayout:N2}</td>
                                    </tr>
                                </tbody>
                            </table>

                            {decorDisclosureHtml}

                            <div style='text-align: center; margin: 30px 0 10px;'>
                                <a href='{settlementReceiptUrl}' style='display: inline-block; background: #059669; color: #ffffff; font-weight: bold; text-decoration: none; padding: 14px 28px; border-radius: 10px; font-size: 14px;'>
                                    View Official Settlement Receipt
                                </a>
                            </div>
                        </div>

                        <div style='background: #f8fafc; border-top: 1px solid #e2e8f0; padding: 20px; text-align: center; font-size: 12px; color: #64748b;'>
                            <p style='margin: 0 0 6px;'>BookMyFun Host Partner Network</p>
                            <p style='margin: 0;'>Host support desk: <a href='mailto:partners@bookmyfun.in' style='color: #059669;'>partners@bookmyfun.in</a></p>
                        </div>
                    </div>
                </div>";

            string subject = $"Host Settlement Statement #{receipt.HostReceiptNumber} - {receipt.Property?.Title ?? "BookMyFun"}";
            await EnqueueEmailAsync(toEmail, subject, html, "OwnerSettlementReceipt");
            return true;
        }

        public async Task<bool> SendBookingConfirmationAsync(string phoneNumber, string clientName, string propertyTitle, string bookingId, decimal amount)
        {
            var text = $"🎉 Hi {clientName}, your booking #{bookingId} for '{propertyTitle}' is confirmed!\n\n" +
                       $"💰 Total Amount Paid: ₹{amount:N2}\n" +
                       $"📅 Check-in details and digital entry pass are now accessible in your BookMyFun dashboard.\n\n" +
                       $"Thank you for booking with BookMyFun!";
            await EnqueueWhatsAppAsync(phoneNumber, text, "WhatsAppNotification");
            return true;
        }

        public async Task<bool> SendBookingReceiptWhatsAppAsync(
            string phoneNumber, 
            string clientName, 
            string propertyTitle, 
            string bookingId, 
            string receiptNumber, 
            decimal totalAmount, 
            string receiptUrl, 
            DateTime checkIn, 
            DateTime checkOut)
        {
            var text = $"🎉 *Booking Confirmed & Official Receipt Issued!*\n\n" +
                       $"Dear {clientName},\n" +
                       $"Your reservation for *{propertyTitle}* is confirmed. Your official booking receipt & entry gate pass have been generated.\n\n" +
                       $"📋 *Receipt Summary:*\n" +
                       $"• Receipt No: {receiptNumber}\n" +
                       $"• Booking Ref: #{bookingId}\n" +
                       $"• Check-In: {checkIn:dd MMM yyyy}\n" +
                       $"• Check-Out: {checkOut:dd MMM yyyy}\n" +
                       $"• Total Paid: ₹{totalAmount:N2}\n\n" +
                       $"📥 *View / Download Tax Receipt & Gate Pass:*\n" +
                       $"{receiptUrl}\n\n" +
                       $"Please carry valid government photo IDs for all check-in guests. We look forward to hosting you!\n— Team BookMyFun";

            await EnqueueWhatsAppAsync(phoneNumber, text, "WhatsAppNotification");
            return true;
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

            await EnqueueWhatsAppAsync(phoneNumber, text, "WhatsAppNotification");
            return true;
        }

        public async Task<bool> SendOwnerBookingReceiptWhatsAppAsync(
            string phoneNumber, 
            string ownerName, 
            string propertyTitle, 
            string bookingId, 
            string hostReceiptNumber, 
            decimal hostGross, 
            decimal hostPayout, 
            string guestName, 
            DateTime checkIn, 
            DateTime checkOut, 
            bool hasDecoration, 
            decimal decorationAmount, 
            string settlementReceiptUrl)
        {
            var decorNote = hasDecoration 
                ? $"\n• Celebration Décor Add-on: ₹{decorationAmount:N2} (Allocated to Event Decorator Studio)" 
                : "";

            var text = $"🏡 *New Confirmed Reservation & Host Settlement Statement*\n\n" +
                       $"Hello {ownerName},\n" +
                       $"A new reservation has been confirmed for *{propertyTitle}*.\n\n" +
                       $"📋 *Settlement Statement:*\n" +
                       $"• Settlement Ref: {hostReceiptNumber}\n" +
                       $"• Booking Ref: #{bookingId}\n" +
                       $"• Guest: {guestName}\n" +
                       $"• Stay Dates: {checkIn:dd MMM yyyy} to {checkOut:dd MMM yyyy}\n" +
                       $"• Property Stay Gross: ₹{hostGross:N2}\n" +
                       $"• Net Host Payout: ₹{hostPayout:N2}{decorNote}\n\n" +
                       $"📄 *View Official Host Settlement Receipt:*\n" +
                       $"{settlementReceiptUrl}\n\n" +
                       $"Sign in to your Host Dashboard to access guest details and prepare the property for arrival.\n— Team BookMyFun";

            await EnqueueWhatsAppAsync(phoneNumber, text, "WhatsAppNotification");
            return true;
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

            await EnqueueWhatsAppAsync(phoneNumber, text, "WhatsAppNotification");
            return true;
        }

        public async Task<bool> SendKycRejectionNotificationAsync(
            string toEmail, 
            string clientName, 
            string? rejectionReason, 
            int clientId, 
            string? documentType = null)
        {
            if (string.IsNullOrWhiteSpace(toEmail)) return false;

            string docName = !string.IsNullOrWhiteSpace(documentType) ? documentType : "Government ID";
            string reason = !string.IsNullOrWhiteSpace(rejectionReason)
                ? rejectionReason
                : "The submitted identification document was unreadable, expired, or did not match registered traveler profile name.";

            // 1. Dispatch in-app notification via EmailQueueService
            _emailQueue.AddInAppNotification(
                clientId,
                "Identity Verification (KYC) Rejected",
                $"Your {docName} could not be verified: {reason}. Please check the instructions on your profile and resubmit.",
                "KycRejection",
                "/Account/Profile");

            await _emailQueue.EnqueueAsync(new EmailQueueMessage
            {
                Channel = "InApp",
                ToEmail = toEmail.Trim(),
                Subject = "Identity Verification Rejected",
                BodyHtml = $"Your {docName} was rejected: {reason}",
                MessageType = "KycRejection",
                QueuedAtUtc = DateTime.UtcNow
            });

            // 2. Dispatch transactional notification email via EmailQueueService
            string subject = "Identity Verification Update: Action Required - BookMyFun";
            string bodyHtml = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; color: #1e293b; background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px;"">
    <div style=""border-bottom: 2px solid #ef4444; padding-bottom: 16px; margin-bottom: 20px;"">
        <h2 style=""color: #dc2626; margin: 0 0 6px 0; font-size: 20px;"">Identity Verification (KYC) Update</h2>
        <p style=""margin: 0; color: #64748b; font-size: 14px;"">BookMyFun Guest Identity & Trust Compliance</p>
    </div>

    <p style=""font-size: 15px; line-height: 1.6;"">Dear <strong>{WebUtility.HtmlEncode(clientName)}</strong>,</p>

    <p style=""font-size: 14px; line-height: 1.6; color: #334155;"">
        Our verification team has reviewed your submitted <strong>{WebUtility.HtmlEncode(docName)}</strong> for your BookMyFun account. Unfortunately, your verification could not be approved at this time.
    </p>

    <div style=""background-color: #fef2f2; border: 1px solid #fecaca; border-radius: 8px; padding: 16px; margin: 20px 0;"">
        <div style=""font-weight: 700; color: #991b1b; font-size: 13px; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 6px;"">
            Status &amp; Rejection Reason
        </div>
        <div style=""font-size: 15px; color: #b91c1c; font-weight: 600; margin-bottom: 8px;"">
            Status: Rejected
        </div>
        <div style=""font-size: 14px; color: #7f1d1d; line-height: 1.5;"">
            {WebUtility.HtmlEncode(reason)}
        </div>
    </div>

    <div style=""margin: 24px 0;"">
        <h4 style=""margin: 0 0 10px 0; color: #0f172a; font-size: 15px;"">How to Resubmit Your Verification:</h4>
        <ol style=""margin: 0; padding-left: 20px; font-size: 14px; color: #475569; line-height: 1.7;"">
            <li>Ensure the image or PDF is crisp, clear, unblurred, and has all four corners visible.</li>
            <li>Verify that the document is non-expired and that the name exactly matches your account name (<strong>{WebUtility.HtmlEncode(clientName)}</strong>).</li>
            <li>Accepted documents: Government-issued Aadhaar Card, Passport, or Driving License.</li>
            <li>You can resubmit immediately from your <a href=""https://bookmyfun.com/Account/Profile"" style=""color: #2563eb; text-decoration: underline;"">Account Profile</a> or during your next reservation checkout.</li>
        </ol>
    </div>

    <div style=""border-top: 1px solid #e2e8f0; padding-top: 16px; margin-top: 24px; font-size: 12px; color: #94a3b8; text-align: center;"">
        BookMyFun Hospitality Marketplace · Private Document Vault Security<br/>
        If you have questions, reply directly to this notification or contact our support team.
    </div>
</div>";

            await _emailQueue.EnqueueAsync(new EmailQueueMessage
            {
                Channel = "Email",
                ToEmail = toEmail.Trim(),
                Subject = subject,
                BodyHtml = bodyHtml,
                MessageType = "KycRejection",
                QueuedAtUtc = DateTime.UtcNow
            });

            _logger.LogInformation("KYC rejection email and in-app notification enqueued for Client #{ClientId} ({Email})", clientId, toEmail);
            return true;
        }

        public async Task<bool> SendCancellationCustomerNotificationAsync(
            int clientId,
            string clientEmail,
            string? clientPhone,
            bool whatsappOptIn,
            int bookingId,
            string propertyTitle,
            int ruleApplied,
            decimal refundAmount,
            string timeline = "5 to 7 business days")
        {
            // 1. In-App Notification
            _emailQueue.AddInAppNotification(
                clientId,
                "Booking Cancelled",
                $"Booking #{bookingId} for {propertyTitle} has been cancelled (Rule #{ruleApplied}). Net refund of ₹{refundAmount:N2} will be credited within {timeline}.",
                "BookingCancellation",
                "/Client/Index");

            // 2. Transactional Email
            if (!string.IsNullOrWhiteSpace(clientEmail))
            {
                string subject = $"Booking #{bookingId} Cancellation & Refund Confirmation - BookMyFun";
                string bodyHtml = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; color: #1e293b; background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px;"">
    <div style=""border-bottom: 2px solid #ef4444; padding-bottom: 16px; margin-bottom: 20px;"">
        <h2 style=""color: #dc2626; margin: 0 0 6px 0; font-size: 20px;"">Booking Cancellation Notice</h2>
        <p style=""margin: 0; color: #64748b; font-size: 14px;"">BookMyFun Reservation Transparency</p>
    </div>
    <p style=""font-size: 15px; line-height: 1.6;"">Your reservation for <strong>{WebUtility.HtmlEncode(propertyTitle)}</strong> (Booking #{bookingId}) has been cancelled.</p>
    <div style=""background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 16px; margin: 20px 0;"">
        <div style=""margin-bottom: 8px;""><strong>Cancellation Policy Applied:</strong> Scenario #{ruleApplied}</div>
        <div style=""margin-bottom: 8px;""><strong>Net Refund Amount:</strong> <span style=""color: #16a34a; font-weight: bold;"">₹{refundAmount:N2}</span></div>
        <div><strong>Expected Timeline:</strong> {WebUtility.HtmlEncode(timeline)}</div>
    </div>
    <p style=""font-size: 13px; color: #64748b;"">Refunds are processed back to the original payment source. You can view full financial breakdown anytime from your client dashboard.</p>
</div>";
                string plainText = $"Booking #{bookingId} for {propertyTitle} cancelled under Policy Rule #{ruleApplied}. Net refund: Rs. {refundAmount:N2}. Expected timeline: {timeline}.";
                await EnqueueEmailAsync(clientEmail.Trim(), subject, bodyHtml, "BookingCancellation");
            }

            // 3. WhatsApp if opted in
            if (whatsappOptIn && !string.IsNullOrWhiteSpace(clientPhone))
            {
                string waMsg = $"Your booking #{bookingId} for {propertyTitle} was cancelled (Policy Rule #{ruleApplied}). Net refund of ₹{refundAmount:N2} will be credited to your account in {timeline}.";
                await EnqueueWhatsAppAsync(clientPhone, waMsg, "BookingCancellation");
            }

            return true;
        }

        public async Task<bool> SendCancellationOwnerNotificationAsync(
            int ownerId,
            string ownerEmail,
            int bookingId,
            string propertyTitle,
            decimal amountReceivedOrLost,
            bool isReceived,
            string reason)
        {
            string impactText = isReceived
                ? $"You will receive ₹{amountReceivedOrLost:N2} per the cancellation policy."
                : $"You will lose / owe ₹{amountReceivedOrLost:N2}.";

            // 1. In-App Notification
            _emailQueue.AddInAppNotification(
                ownerId,
                "Booking Cancelled by Guest or Host",
                $"Booking #{bookingId} for {propertyTitle} was cancelled. {impactText} Reason: {reason}.",
                "BookingCancellation",
                "/Owner/Index");

            // 2. Transactional Email
            if (!string.IsNullOrWhiteSpace(ownerEmail))
            {
                string subject = $"Booking #{bookingId} Cancelled - Host Update - BookMyFun";
                string bodyHtml = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; color: #1e293b; background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px;"">
    <div style=""border-bottom: 2px solid #3b82f6; padding-bottom: 16px; margin-bottom: 20px;"">
        <h2 style=""color: #1e40af; margin: 0 0 6px 0; font-size: 20px;"">Host Booking Update</h2>
        <p style=""margin: 0; color: #64748b; font-size: 14px;"">BookMyFun Host Settlement</p>
    </div>
    <p style=""font-size: 15px; line-height: 1.6;"">Booking #{bookingId} for your listing <strong>{WebUtility.HtmlEncode(propertyTitle)}</strong> has been cancelled.</p>
    <div style=""background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 16px; margin: 20px 0;"">
        <div style=""margin-bottom: 8px;""><strong>Financial Settlement:</strong> {impactText}</div>
        <div><strong>Reason:</strong> {WebUtility.HtmlEncode(reason)}</div>
    </div>
    <p style=""font-size: 13px; color: #64748b;"">Check your host dashboard for updated settlement and ledger records.</p>
</div>";
                await EnqueueEmailAsync(ownerEmail.Trim(), subject, bodyHtml, "OwnerBookingCancellation");
            }

            return true;
        }

        public async Task DispatchConfirmedBookingNotificationsAsync(
            BookMyFun.Models.Entities.Booking booking, 
            BookMyFun.Models.Entities.Property property, 
            BookMyFun.Models.Entities.User client, 
            BookMyFun.Models.Entities.User? owner, 
            BookMyFun.Models.Entities.User? eventManager, 
            string? baseUrl = null)
        {
            baseUrl ??= "https://bookmyfun.in";
            string bookingUrl = $"{baseUrl}/Booking/Receipt/{booking.BookingID}";
            int guests = property.MaxGuests > 0 ? property.MaxGuests : 1;
            decimal ownerPayout = booking.OwnerPayoutAmount > 0 
                ? booking.OwnerPayoutAmount 
                : Math.Max(0, (booking.TotalAmount - (booking.IsEventBooking ? booking.EventPackagePriceSnapshot : 0m) - booking.PlatformCommissionAmount));
            string holdStatus = "Held until guest check-in";

            // 1. Client Notifications (In-App, Email, WhatsApp)
            // 1a. In-App
            try
            {
                string key = $"booking_{booking.BookingID}_client_inapp";
                if (_emailQueue.TryAcquireNotificationKey(key))
                {
                    _emailQueue.AddInAppNotification(
                        client.UserID,
                        "Booking Confirmed!",
                        $"Your booking #{booking.BookingID} for '{property.Title}' is confirmed! Check-in: {booking.CheckInDate:dd MMM yyyy}, Check-out: {booking.CheckOutDate:dd MMM yyyy}, Guests: {guests}, Paid: ₹{booking.TotalAmount:N2}.",
                        "BookingConfirmation",
                        bookingUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch client in-app notification for booking {BookingId}", booking.BookingID);
            }

            // 1b. Email
            try
            {
                string key = $"booking_{booking.BookingID}_client_email";
                if (!string.IsNullOrWhiteSpace(client.Email) && _emailQueue.TryAcquireNotificationKey(key))
                {
                    string subject = $"Booking Confirmed: {property.Title} (Ref #{booking.BookingID})";
                    string bodyHtml = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; color: #1e293b; background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px;"">
    <div style=""border-bottom: 2px solid #0284c7; padding-bottom: 16px; margin-bottom: 20px;"">
        <h2 style=""color: #0284c7; margin: 0 0 6px 0; font-size: 20px;"">Booking Confirmed!</h2>
        <p style=""margin: 0; color: #64748b; font-size: 14px;"">Thank you for booking with BookMyFun</p>
    </div>
    <p style=""font-size: 15px; line-height: 1.6;"">Dear <strong>{WebUtility.HtmlEncode(client.FullName)}</strong>,</p>
    <p style=""font-size: 14px; line-height: 1.6; color: #334155;"">Your reservation for <strong>{WebUtility.HtmlEncode(property.Title)}</strong> has been confirmed.</p>
    <div style=""background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 16px; margin: 20px 0;"">
        <div style=""margin-bottom: 8px;""><strong>Booking Reference:</strong> #{booking.BookingID}</div>
        <div style=""margin-bottom: 8px;""><strong>Property:</strong> {WebUtility.HtmlEncode(property.Title)}</div>
        <div style=""margin-bottom: 8px;""><strong>Check-In:</strong> {booking.CheckInDate:dd MMM yyyy}</div>
        <div style=""margin-bottom: 8px;""><strong>Check-Out:</strong> {booking.CheckOutDate:dd MMM yyyy}</div>
        <div style=""margin-bottom: 8px;""><strong>Guests:</strong> {guests}</div>
        <div><strong>Amount Paid:</strong> <span style=""color: #16a34a; font-weight: bold;"">₹{booking.TotalAmount:N2}</span></div>
    </div>
    <div style=""text-align: center; margin: 25px 0;"">
        <a href=""{bookingUrl}"" style=""background-color: #0284c7; color: #ffffff; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: 600; display: inline-block;"">View Booking &amp; Gate Pass</a>
    </div>
</div>";
                    await EnqueueEmailAsync(client.Email.Trim(), subject, bodyHtml, "BookingConfirmation");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch client email notification for booking {BookingId}", booking.BookingID);
            }

            // 1c. WhatsApp
            try
            {
                string key = $"booking_{booking.BookingID}_client_whatsapp";
                if (!string.IsNullOrWhiteSpace(client.Phone) && _emailQueue.TryAcquireNotificationKey(key))
                {
                    string msg = $"🎉 *Booking Confirmed!*\n\n" +
                                 $"Dear {client.FullName},\n" +
                                 $"Your booking for *{property.Title}* is confirmed!\n\n" +
                                 $"• Booking Ref: #{booking.BookingID}\n" +
                                 $"• Property: {property.Title}\n" +
                                 $"• Check-In: {booking.CheckInDate:dd MMM yyyy}\n" +
                                 $"• Check-Out: {booking.CheckOutDate:dd MMM yyyy}\n" +
                                 $"• Guests: {guests}\n" +
                                 $"• Amount Paid: ₹{booking.TotalAmount:N2}\n\n" +
                                 $"View your booking & pass: {bookingUrl}\n— Team BookMyFun";
                    await EnqueueWhatsAppAsync(client.Phone.Trim(), msg, "BookingConfirmation");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch client WhatsApp notification for booking {BookingId}", booking.BookingID);
            }

            // 2. Owner Notifications (In-App, Email, WhatsApp if enabled)
            if (owner != null)
            {
                // 2a. In-App
                try
                {
                    string key = $"booking_{booking.BookingID}_owner_inapp";
                    if (_emailQueue.TryAcquireNotificationKey(key))
                    {
                        _emailQueue.AddInAppNotification(
                            owner.UserID,
                            "New Confirmed Reservation!",
                            $"New booking #{booking.BookingID} for '{property.Title}'! Dates: {booking.CheckInDate:dd MMM yyyy} to {booking.CheckOutDate:dd MMM yyyy}, Guests: {guests}, Guest: {client.FullName} ({client.Phone ?? "N/A"}), Payout: ₹{ownerPayout:N2} ({holdStatus}).",
                            "OwnerBookingAlert",
                            "/Owner/Bookings");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch owner in-app notification for booking {BookingId}", booking.BookingID);
                }

                // 2b. Email
                try
                {
                    string key = $"booking_{booking.BookingID}_owner_email";
                    if (!string.IsNullOrWhiteSpace(owner.Email) && _emailQueue.TryAcquireNotificationKey(key))
                    {
                        string subject = $"New Confirmed Reservation #{booking.BookingID} - {property.Title}";
                        string bodyHtml = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; color: #1e293b; background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px;"">
    <div style=""border-bottom: 2px solid #16a34a; padding-bottom: 16px; margin-bottom: 20px;"">
        <h2 style=""color: #16a34a; margin: 0 0 6px 0; font-size: 20px;"">New Confirmed Reservation</h2>
        <p style=""margin: 0; color: #64748b; font-size: 14px;"">BookMyFun Host Partner Network</p>
    </div>
    <p style=""font-size: 15px; line-height: 1.6;"">Hello <strong>{WebUtility.HtmlEncode(owner.FullName)}</strong>,</p>
    <p style=""font-size: 14px; line-height: 1.6; color: #334155;"">A new confirmed booking has been made for your listing <strong>{WebUtility.HtmlEncode(property.Title)}</strong>.</p>
    <div style=""background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 16px; margin: 20px 0;"">
        <div style=""margin-bottom: 8px;""><strong>Booking Reference:</strong> #{booking.BookingID}</div>
        <div style=""margin-bottom: 8px;""><strong>Dates:</strong> {booking.CheckInDate:dd MMM yyyy} to {booking.CheckOutDate:dd MMM yyyy}</div>
        <div style=""margin-bottom: 8px;""><strong>Guests Count:</strong> {guests}</div>
        <div style=""margin-bottom: 8px;""><strong>Client Name:</strong> {WebUtility.HtmlEncode(client.FullName)}</div>
        <div style=""margin-bottom: 8px;""><strong>Client Phone:</strong> {WebUtility.HtmlEncode(client.Phone ?? "N/A")}</div>
        <div style=""margin-bottom: 8px;""><strong>Owner Payout Amount:</strong> <span style=""color: #16a34a; font-weight: bold;"">₹{ownerPayout:N2}</span></div>
        <div><strong>Payout Hold Status:</strong> <span style=""color: #d97706; font-weight: bold;"">{holdStatus}</span></div>
    </div>
</div>";
                        await EnqueueEmailAsync(owner.Email.Trim(), subject, bodyHtml, "OwnerBookingAlert");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch owner email notification for booking {BookingId}", booking.BookingID);
                }

                // 2c. WhatsApp (only if owner has WhatsApp updates on)
                try
                {
                    string key = $"booking_{booking.BookingID}_owner_whatsapp";
                    if (owner.WhatsAppNotificationsEnabled && !string.IsNullOrWhiteSpace(owner.Phone) && _emailQueue.TryAcquireNotificationKey(key))
                    {
                        string msg = $"🏡 *New Reservation Confirmed!*\n\n" +
                                     $"Hello {owner.FullName},\n" +
                                     $"A new reservation has been confirmed for *{property.Title}*.\n\n" +
                                     $"• Booking Ref: #{booking.BookingID}\n" +
                                     $"• Dates: {booking.CheckInDate:dd MMM yyyy} to {booking.CheckOutDate:dd MMM yyyy}\n" +
                                     $"• Guests: {guests}\n" +
                                     $"• Client: {client.FullName} ({client.Phone ?? "N/A"})\n" +
                                     $"• Owner Payout: ₹{ownerPayout:N2}\n" +
                                     $"• Hold Status: {holdStatus}\n\n" +
                                     $"Please check your Host Dashboard to prepare for arrival.\n— Team BookMyFun";
                        await EnqueueWhatsAppAsync(owner.Phone.Trim(), msg, "OwnerBookingAlert");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch owner WhatsApp notification for booking {BookingId}", booking.BookingID);
                }
            }

            // 3. EventManager Notifications (only if an event package was selected and eventManager is not null)
            if (booking.IsEventBooking && booking.EventPackagePriceSnapshot > 0 && eventManager != null)
            {
                string packageName = booking.EventPackageName ?? "Selected Décor Package";
                string venue = !string.IsNullOrWhiteSpace(property.FormattedAddress)
                    ? $"{property.Title}, {property.FormattedAddress}"
                    : $"{property.Title}, {property.Location}";
                DateTime eventDate = booking.EventDate ?? booking.CheckInDate;
                DateTime setupDeadline = eventDate.AddHours(-2);
                string deadlineStr = $"{setupDeadline:dd MMM yyyy, hh:mm tt} (2 hours prior to start)";

                // 3a. In-App
                try
                {
                    string key = $"booking_{booking.BookingID}_eventmanager_inapp";
                    if (_emailQueue.TryAcquireNotificationKey(key))
                    {
                        _emailQueue.AddInAppNotification(
                            eventManager.UserID,
                            "New Event Décor Setup Order!",
                            $"New event setup for #{booking.BookingID}: {packageName} at {venue}. Event Date: {eventDate:dd MMM yyyy}, Setup Deadline: {deadlineStr}.",
                            "EventManagerSetupAlert",
                            "/EventManager/Dashboard");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch event manager in-app notification for booking {BookingId}", booking.BookingID);
                }

                // 3b. Email
                try
                {
                    string key = $"booking_{booking.BookingID}_eventmanager_email";
                    if (!string.IsNullOrWhiteSpace(eventManager.Email) && _emailQueue.TryAcquireNotificationKey(key))
                    {
                        string subject = $"New Event Décor Order #{booking.BookingID} - {packageName}";
                        string bodyHtml = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; color: #1e293b; background-color: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px;"">
    <div style=""border-bottom: 2px solid #8b5cf6; padding-bottom: 16px; margin-bottom: 20px;"">
        <h2 style=""color: #8b5cf6; margin: 0 0 6px 0; font-size: 20px;"">New Event Décor Staging Order</h2>
        <p style=""margin: 0; color: #64748b; font-size: 14px;"">BookMyFun Event Services</p>
    </div>
    <p style=""font-size: 15px; line-height: 1.6;"">Hello <strong>{WebUtility.HtmlEncode(eventManager.FullName)}</strong>,</p>
    <p style=""font-size: 14px; line-height: 1.6; color: #334155;"">A new event package has been booked on BookMyFun.</p>
    <div style=""background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 16px; margin: 20px 0;"">
        <div style=""margin-bottom: 8px;""><strong>Booking Reference:</strong> #{booking.BookingID}</div>
        <div style=""margin-bottom: 8px;""><strong>Package Name:</strong> {WebUtility.HtmlEncode(packageName)}</div>
        <div style=""margin-bottom: 8px;""><strong>Date:</strong> {eventDate:dd MMM yyyy}</div>
        <div style=""margin-bottom: 8px;""><strong>Venue:</strong> {WebUtility.HtmlEncode(venue)}</div>
        <div><strong>Setup Deadline:</strong> <span style=""color: #dc2626; font-weight: bold;"">{WebUtility.HtmlEncode(deadlineStr)}</span></div>
    </div>
</div>";
                        await EnqueueEmailAsync(eventManager.Email.Trim(), subject, bodyHtml, "EventManagerSetupAlert");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch event manager email notification for booking {BookingId}", booking.BookingID);
                }

                // 3c. WhatsApp
                try
                {
                    string key = $"booking_{booking.BookingID}_eventmanager_whatsapp";
                    if (!string.IsNullOrWhiteSpace(eventManager.Phone) && _emailQueue.TryAcquireNotificationKey(key))
                    {
                        string msg = $"🎪 *New Event Staging Order!*\n\n" +
                                     $"Hello {eventManager.FullName},\n" +
                                     $"A new event package order is booked on BookMyFun.\n\n" +
                                     $"• Booking Ref: #{booking.BookingID}\n" +
                                     $"• Package: {packageName}\n" +
                                     $"• Date: {eventDate:dd MMM yyyy}\n" +
                                     $"• Venue: {venue}\n" +
                                     $"• Setup Deadline: {deadlineStr}\n\n" +
                                     $"Please log in to your Event Manager portal to coordinate staging.\n— Team BookMyFun";
                        await EnqueueWhatsAppAsync(eventManager.Phone.Trim(), msg, "EventManagerSetupAlert");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch event manager WhatsApp notification for booking {BookingId}", booking.BookingID);
                }
            }
        }
    }
}
