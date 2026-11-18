using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services
{
    public enum NotificationEventType
    {
        NewBooking,
        BookingCancelledByGuest,
        RefundProcessed,
        PaymentReceived,
        ReviewReceived,
        ListingStatusChange,
        VerificationStatusChange,
        SubscriptionExpiringOrExpired,
        DamageClaimRaised
    }

    public class NotificationPayload
    {
        public int RecipientUserId { get; set; }
        public string RecipientName { get; set; } = string.Empty;
        public string? RecipientEmail { get; set; }
        public string? RecipientPhone { get; set; }
        public NotificationEventType EventType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? ActionUrl { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// WhatsApp Business API adapter interface wrapping the project's provider (Meta WhatsApp Cloud API).
    /// Required Meta templates documentation:
    /// - bookmyfun_new_booking_owner_v1 (Parameters: OwnerName, PropertyName, BookingId, Dates, StayTotal, YourPayout)
    /// - bookmyfun_booking_cancelled_v1 (Parameters: OwnerName, PropertyName, BookingId, PayoutAdjustment)
    /// - bookmyfun_refund_processed_v1 (Parameters: RecipientName, BookingId, RefundAmount, Timeline)
    /// - bookmyfun_payment_received_v1 (Parameters: RecipientName, Amount, ReferenceId)
    /// - bookmyfun_review_received_v1 (Parameters: OwnerName, PropertyName, Rating, ReviewText)
    /// - bookmyfun_listing_status_v1 (Parameters: OwnerName, PropertyName, Status, Notes)
    /// - bookmyfun_verification_status_v1 (Parameters: UserName, Status, Reason)
    /// - bookmyfun_subscription_status_v1 (Parameters: OwnerName, PlanName, ExpiryDate, Status)
    /// - bookmyfun_damage_claim_v1 (Parameters: OwnerName, BookingId, ClaimAmount, Status)
    /// </summary>
    public interface IWhatsAppProvider
    {
        Task<bool> SendTemplateMessageAsync(string phoneNumber, string templateName, Dictionary<string, string> parameters);
        Task<bool> SendTextMessageAsync(string phoneNumber, string message);
    }

    public class MetaWhatsAppProvider : IWhatsAppProvider
    {
        private readonly IWhatsAppService _whatsAppService;
        private readonly ILogger<MetaWhatsAppProvider> _logger;

        public MetaWhatsAppProvider(IWhatsAppService whatsAppService, ILogger<MetaWhatsAppProvider> logger)
        {
            _whatsAppService = whatsAppService;
            _logger = logger;
        }

        public async Task<bool> SendTemplateMessageAsync(string phoneNumber, string templateName, Dictionary<string, string> parameters)
        {
            _logger.LogInformation("Dispatching WhatsApp template '{TemplateName}' to {Phone}", templateName, phoneNumber);
            // Construct a formatted alert message adhering to Meta approved HSM template structure
            var formattedBody = $"*BookMyFun Alert* [{templateName}]\n" +
                                string.Join("\n", parameters.Select(kv => $"• {kv.Key}: {kv.Value}"));
            return await _whatsAppService.SendWhatsAppMessageAsync(phoneNumber, formattedBody);
        }

        public async Task<bool> SendTextMessageAsync(string phoneNumber, string message)
        {
            return await _whatsAppService.SendWhatsAppMessageAsync(phoneNumber, message);
        }
    }

    public interface INotificationFanoutService
    {
        Task FanoutEventAsync(NotificationPayload payload);
    }

    public class NotificationFanoutService : INotificationFanoutService
    {
        private readonly IUserRepository _userRepo;
        private readonly IEmailQueueService _emailQueue;
        private readonly IWhatsAppProvider _whatsAppProvider;
        private readonly ILogger<NotificationFanoutService> _logger;

        public NotificationFanoutService(
            IUserRepository userRepo,
            IEmailQueueService emailQueue,
            IWhatsAppProvider whatsAppProvider,
            ILogger<NotificationFanoutService> logger)
        {
            _userRepo = userRepo;
            _emailQueue = emailQueue;
            _whatsAppProvider = whatsAppProvider;
            _logger = logger;
        }

        public Task FanoutEventAsync(NotificationPayload payload)
        {
            // CRITICAL REQUIREMENT (Section 2A): Never block the request that triggered the notification
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessFanoutInternalAsync(payload);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fanout notification for event {EventType} to User #{UserId}",
                        payload.EventType, payload.RecipientUserId);
                }
            });

            return Task.CompletedTask;
        }

        private async Task ProcessFanoutInternalAsync(NotificationPayload payload)
        {
            var user = await _userRepo.GetByIdAsync(payload.RecipientUserId);
            if (user == null)
            {
                _logger.LogWarning("Aborting notification fanout: User #{UserId} not found.", payload.RecipientUserId);
                return;
            }

            string email = !string.IsNullOrWhiteSpace(payload.RecipientEmail) ? payload.RecipientEmail : user.Email;
            string? phone = !string.IsNullOrWhiteSpace(payload.RecipientPhone) ? payload.RecipientPhone : (user.WhatsAppPhone ?? user.Phone);

            // Channel 1: In-App (Enabled by default)
            if (user.NotificationInAppEnabled)
            {
                try
                {
                    _emailQueue.AddInAppNotification(
                        user.UserID,
                        payload.Title,
                        payload.Message,
                        payload.EventType.ToString(),
                        payload.ActionUrl);

                    _logger.LogInformation("In-app notification dispatched to User #{UserId} ({Title})", user.UserID, payload.Title);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to register in-app notification for User #{UserId}", user.UserID);
                }
            }

            // Channel 2: Email (Enabled by default)
            if (user.NotificationEmailEnabled && !string.IsNullOrWhiteSpace(email))
            {
                try
                {
                    string htmlBody = $@"
                        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px;'>
                            <div style='background: #0f172a; color: #f59e0b; padding: 15px; border-radius: 6px; font-weight: bold; font-size: 1.2rem;'>
                                BookMyFun Platform Alert
                            </div>
                            <div style='padding: 20px 0;'>
                                <h3 style='color: #1e293b; margin-top: 0;'>{payload.Title}</h3>
                                <p style='color: #475569; font-size: 15px; line-height: 1.6;'>{payload.Message}</p>
                                {(payload.Metadata.Any() ? "<div style='background: #f8fafc; padding: 12px; border-radius: 6px; font-size: 14px; color: #334155; margin: 15px 0;'>" +
                                    string.Join("", payload.Metadata.Select(m => $"<p style='margin: 4px 0;'><strong>{m.Key}:</strong> {m.Value}</p>")) + "</div>" : "")}
                                {(!string.IsNullOrEmpty(payload.ActionUrl) ? $"<p style='margin-top: 20px;'><a href='{payload.ActionUrl}' style='display: inline-block; background: #f59e0b; color: #000; padding: 10px 20px; text-decoration: none; border-radius: 6px; font-weight: bold;'>View in Dashboard</a></p>" : "")}
                            </div>
                            <div style='border-top: 1px solid #e2e8f0; padding-top: 12px; font-size: 12px; color: #94a3b8;'>
                                You received this notification based on your BookMyFun Partner account preferences.
                            </div>
                        </div>";

                    await _emailQueue.EnqueueAsync(new EmailQueueMessage
                    {
                        Channel = "Email",
                        ToEmail = email,
                        Subject = payload.Title,
                        BodyHtml = htmlBody,
                        MessageType = payload.EventType.ToString()
                    });

                    _logger.LogInformation("Enqueued email alert to {Email} for {EventType}", email, payload.EventType);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to enqueue email alert for User #{UserId}", user.UserID);
                }
            }

            // Channel 3: WhatsApp (Requires Opt-In AND Verified Phone via OTP)
            if (user.NotificationWhatsAppEnabled && user.WhatsAppVerified && !string.IsNullOrWhiteSpace(phone))
            {
                try
                {
                    string templateName = GetMetaTemplateName(payload.EventType);
                    var templateParams = new Dictionary<string, string>(payload.Metadata)
                    {
                        ["Alert"] = payload.Title,
                        ["Details"] = payload.Message
                    };

                    await _emailQueue.EnqueueAsync(new EmailQueueMessage
                    {
                        Channel = "WhatsApp",
                        ToPhone = phone,
                        Subject = templateName,
                        BodyHtml = payload.Message,
                        MessageType = payload.EventType.ToString()
                    });

                    _logger.LogInformation("Enqueued WhatsApp alert to {Phone} ({TemplateName})", phone, templateName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to enqueue WhatsApp notification for User #{UserId}", user.UserID);
                }
            }
        }

        private static string GetMetaTemplateName(NotificationEventType eventType)
        {
            return eventType switch
            {
                NotificationEventType.NewBooking => "bookmyfun_new_booking_owner_v1",
                NotificationEventType.BookingCancelledByGuest => "bookmyfun_booking_cancelled_v1",
                NotificationEventType.RefundProcessed => "bookmyfun_refund_processed_v1",
                NotificationEventType.PaymentReceived => "bookmyfun_payment_received_v1",
                NotificationEventType.ReviewReceived => "bookmyfun_review_received_v1",
                NotificationEventType.ListingStatusChange => "bookmyfun_listing_status_v1",
                NotificationEventType.VerificationStatusChange => "bookmyfun_verification_status_v1",
                NotificationEventType.SubscriptionExpiringOrExpired => "bookmyfun_subscription_status_v1",
                NotificationEventType.DamageClaimRaised => "bookmyfun_damage_claim_v1",
                _ => "bookmyfun_general_alert_v1"
            };
        }
    }
}
