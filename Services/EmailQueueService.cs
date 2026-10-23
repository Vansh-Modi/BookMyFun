using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace BookMyFun.Services
{
    public class InAppNotificationItem
    {
        public int NotificationId { get; set; }
        public int UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string NotificationType { get; set; } = "KycRejection";
        public string? ActionUrl { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; } = false;
    }

    public class EmailQueueMessage
    {
        public string Channel { get; set; } = "Email"; // "Email", "WhatsApp", or "InApp"
        public string ToEmail { get; set; } = string.Empty;
        public string ToPhone { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string BodyHtml { get; set; } = string.Empty;
        public string MessageType { get; set; } = "General"; // OTP, Notification, Alert, KycRejection
        public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
        public int RetryCount { get; set; } = 0;
    }

    public interface IEmailQueueService
    {
        ValueTask EnqueueAsync(EmailQueueMessage message);
        ValueTask<EmailQueueMessage> DequeueAsync(CancellationToken cancellationToken);
        int ApproximateQueueCount { get; }
        void AddInAppNotification(int userId, string title, string message, string type = "KycRejection", string? actionUrl = null);
        List<InAppNotificationItem> GetInAppNotifications(int userId);
        void MarkInAppNotificationRead(int notificationId);
    }

    public class EmailQueueService : IEmailQueueService
    {
        private readonly Channel<EmailQueueMessage> _channel;
        private readonly ILogger<EmailQueueService> _logger;
        private readonly ConcurrentDictionary<int, List<InAppNotificationItem>> _inAppNotifications = new();
        private int _count = 0;
        private int _notificationIdCounter = 0;

        public EmailQueueService(ILogger<EmailQueueService> logger)
        {
            _logger = logger;
            // Unbounded channel with single reader for reliable sequential background dispatch
            _channel = Channel.CreateUnbounded<EmailQueueMessage>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }

        public int ApproximateQueueCount => Math.Max(0, Volatile.Read(ref _count));

        public void AddInAppNotification(int userId, string title, string message, string type = "KycRejection", string? actionUrl = null)
        {
            int id = Interlocked.Increment(ref _notificationIdCounter);
            var item = new InAppNotificationItem
            {
                NotificationId = id,
                UserId = userId,
                Title = title,
                Message = message,
                NotificationType = type,
                ActionUrl = actionUrl,
                CreatedAtUtc = DateTime.UtcNow,
                IsRead = false
            };

            _inAppNotifications.AddOrUpdate(userId,
                _ => new List<InAppNotificationItem> { item },
                (_, list) =>
                {
                    lock (list)
                    {
                        list.Insert(0, item);
                    }
                    return list;
                });

            _logger.LogInformation("In-app notification #{NotifId} registered for User #{UserId}: {Title}", id, userId, title);
        }

        public List<InAppNotificationItem> GetInAppNotifications(int userId)
        {
            if (_inAppNotifications.TryGetValue(userId, out var list))
            {
                lock (list)
                {
                    return list.ToList();
                }
            }
            return new List<InAppNotificationItem>();
        }

        public void MarkInAppNotificationRead(int notificationId)
        {
            foreach (var kvp in _inAppNotifications)
            {
                lock (kvp.Value)
                {
                    var found = kvp.Value.FirstOrDefault(n => n.NotificationId == notificationId);
                    if (found != null)
                    {
                        found.IsRead = true;
                        break;
                    }
                }
            }
        }

        public async ValueTask EnqueueAsync(EmailQueueMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            await _channel.Writer.WriteAsync(message);
            Interlocked.Increment(ref _count);
            if (string.Equals(message.Channel, "WhatsApp", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Enqueued WhatsApp message to {ToPhone} (Type: {Type})",
                    message.ToPhone, message.MessageType);
            }
            else if (string.Equals(message.Channel, "InApp", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Enqueued in-app notification for {Recipient} (Type: {Type})",
                    message.ToEmail, message.MessageType);
            }
            else
            {
                _logger.LogInformation("Enqueued email message to {ToEmail} with subject '{Subject}' (Type: {Type})",
                    message.ToEmail, message.Subject, message.MessageType);
            }
        }

        public async ValueTask<EmailQueueMessage> DequeueAsync(CancellationToken cancellationToken)
        {
            var msg = await _channel.Reader.ReadAsync(cancellationToken);
            Interlocked.Decrement(ref _count);
            return msg;
        }
    }

    /// <summary>
    /// Hosted Background Service that processes queued emails using an independent service scope,
    /// preventing ObjectDisposedException when callers fire-and-forget from short-lived HTTP scopes.
    /// </summary>
    public class EmailBackgroundDispatcher : BackgroundService
    {
        private readonly IEmailQueueService _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EmailBackgroundDispatcher> _logger;

        public EmailBackgroundDispatcher(
            IEmailQueueService queue,
            IServiceScopeFactory scopeFactory,
            ILogger<EmailBackgroundDispatcher> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("EmailBackgroundDispatcher trigger queue worker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var message = await _queue.DequeueAsync(stoppingToken);
                    if (message == null) continue;

                    using var scope = _scopeFactory.CreateScope();

                    if (string.Equals(message.Channel, "InApp", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("InApp notification processed for {Recipient} (Subject: {Subject})", message.ToEmail, message.Subject);
                    }
                    else if (string.Equals(message.Channel, "WhatsApp", StringComparison.OrdinalIgnoreCase))
                    {
                        var whatsAppService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
                        bool sent = await whatsAppService.SendWhatsAppMessageAsync(message.ToPhone, message.BodyHtml);
                        if (!sent && message.RetryCount < 2)
                        {
                            message.RetryCount++;
                            _logger.LogWarning("WhatsApp delivery failed for {ToPhone}. Re-enqueuing retry #{Retry}",
                                message.ToPhone, message.RetryCount);
                            await Task.Delay(1000 * message.RetryCount, stoppingToken);
                            await _queue.EnqueueAsync(message);
                        }
                    }
                    else
                    {
                        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                        bool sent = await emailService.SendEmailAsync(message.ToEmail, message.Subject, message.BodyHtml);
                        if (!sent && message.RetryCount < 2)
                        {
                            message.RetryCount++;
                            _logger.LogWarning("Email delivery failed for {ToEmail}. Re-enqueuing retry #{Retry}",
                                message.ToEmail, message.RetryCount);
                            await Task.Delay(1000 * message.RetryCount, stoppingToken);
                            await _queue.EnqueueAsync(message);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected exception in EmailBackgroundDispatcher execution loop.");
                    await Task.Delay(2000, stoppingToken);
                }
            }

            _logger.LogInformation("EmailBackgroundDispatcher trigger queue worker stopped.");
        }
    }
}
