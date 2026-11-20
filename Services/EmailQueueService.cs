using System.Collections.Concurrent;
using System.Threading.Channels;
using BookMyFun.Services.Common;
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
        bool TryAcquireNotificationKey(string notificationKey) => true;
        bool HasNotificationKey(string notificationKey) => false;
    }

    public class EmailQueueService : IEmailQueueService
    {
        private readonly Channel<EmailQueueMessage> _channel;
        private readonly ILogger<EmailQueueService> _logger;
        private readonly ConcurrentDictionary<int, List<InAppNotificationItem>> _inAppNotifications = new();
        private readonly ConcurrentDictionary<string, DateTime> _dispatchedNotificationKeys = new();
        private int _count = 0;
        private int _notificationIdCounter = 0;

        private const int MaxTrackedCapacity = 50000;
        private static readonly TimeSpan RetentionPeriod = TimeSpan.FromHours(24);
        private DateTime _lastCleanupTime = DateTime.UtcNow;
        private readonly object _cleanupLock = new();

        public bool TryAcquireNotificationKey(string notificationKey)
        {
            if (string.IsNullOrWhiteSpace(notificationKey)) return false;
            var now = DateTime.UtcNow;
            CleanStaleKeys(now);
            return _dispatchedNotificationKeys.TryAdd(notificationKey, now);
        }

        public bool HasNotificationKey(string notificationKey)
        {
            if (string.IsNullOrWhiteSpace(notificationKey)) return false;
            return _dispatchedNotificationKeys.ContainsKey(notificationKey);
        }

        private void CleanStaleKeys(DateTime now)
        {
            if (now - _lastCleanupTime < TimeSpan.FromMinutes(10) && _dispatchedNotificationKeys.Count < MaxTrackedCapacity)
            {
                return;
            }

            if (System.Threading.Monitor.TryEnter(_cleanupLock))
            {
                try
                {
                    if (now - _lastCleanupTime >= TimeSpan.FromMinutes(10) || _dispatchedNotificationKeys.Count >= MaxTrackedCapacity)
                    {
                        var cutoff = now - RetentionPeriod;
                        foreach (var kvp in _dispatchedNotificationKeys)
                        {
                            if (kvp.Value < cutoff)
                            {
                                _dispatchedNotificationKeys.TryRemove(kvp.Key, out _);
                            }
                        }

                        if (_dispatchedNotificationKeys.Count >= MaxTrackedCapacity)
                        {
                            var excess = _dispatchedNotificationKeys.OrderBy(k => k.Value).Take(_dispatchedNotificationKeys.Count - MaxTrackedCapacity + 5000).ToList();
                            foreach (var old in excess)
                            {
                                _dispatchedNotificationKeys.TryRemove(old.Key, out _);
                            }
                        }

                        foreach (var kvp in _inAppNotifications)
                        {
                            lock (kvp.Value)
                            {
                                kvp.Value.RemoveAll(i => i.CreatedAtUtc < cutoff);
                            }
                            if (kvp.Value.Count == 0)
                            {
                                _inAppNotifications.TryRemove(kvp.Key, out _);
                            }
                        }

                        _lastCleanupTime = now;
                    }
                }
                finally
                {
                    System.Threading.Monitor.Exit(_cleanupLock);
                }
            }
        }

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
            var now = DateTime.UtcNow;
            CleanStaleKeys(now);
            int id = Interlocked.Increment(ref _notificationIdCounter);
            var item = new InAppNotificationItem
            {
                NotificationId = id,
                UserId = userId,
                Title = title,
                Message = message,
                NotificationType = type,
                ActionUrl = actionUrl,
                CreatedAtUtc = now,
                IsRead = false
            };

            _inAppNotifications.AddOrUpdate(userId,
                _ => new List<InAppNotificationItem> { item },
                (_, list) =>
                {
                    lock (list)
                    {
                        list.RemoveAll(i => now - i.CreatedAtUtc > RetentionPeriod);
                        list.Insert(0, item);
                        if (list.Count > 1000)
                        {
                            list.RemoveRange(1000, list.Count - 1000);
                        }
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
        private readonly ServiceResilienceState _resilienceState = new();

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
            try
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
                        await BackgroundServiceResilienceHelper.HandleLoopExceptionAsync(
                            ex,
                            nameof(EmailBackgroundDispatcher),
                            _logger,
                            _resilienceState,
                            stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful cancellation
            }
            catch (Exception ex)
            {
                var correlationId = Guid.NewGuid().ToString("N");
                _logger.LogError(ex, "[{ServiceName}] [{CorrelationId}] Fatal unhandled exception in ExecuteAsync.", nameof(EmailBackgroundDispatcher), correlationId);
            }
            finally
            {
                _logger.LogInformation("EmailBackgroundDispatcher trigger queue worker stopped.");
            }
        }
    }
}
