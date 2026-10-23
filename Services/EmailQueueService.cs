using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace BookMyFun.Services
{
    public class EmailQueueMessage
    {
        public string ToEmail { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string BodyHtml { get; set; } = string.Empty;
        public string MessageType { get; set; } = "General"; // OTP, Notification, Alert
        public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
        public int RetryCount { get; set; } = 0;
    }

    public interface IEmailQueueService
    {
        ValueTask EnqueueAsync(EmailQueueMessage message);
        ValueTask<EmailQueueMessage> DequeueAsync(CancellationToken cancellationToken);
        int ApproximateQueueCount { get; }
    }

    public class EmailQueueService : IEmailQueueService
    {
        private readonly Channel<EmailQueueMessage> _channel;
        private readonly ILogger<EmailQueueService> _logger;
        private int _count = 0;

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

        public async ValueTask EnqueueAsync(EmailQueueMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            await _channel.Writer.WriteAsync(message);
            Interlocked.Increment(ref _count);
            _logger.LogInformation("Enqueued email message to {ToEmail} with subject '{Subject}' (Type: {Type})",
                message.ToEmail, message.Subject, message.MessageType);
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
