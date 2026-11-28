using System;
using System.Threading;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services.Notifications
{
    public class NotificationOutboxBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<NotificationOutboxBackgroundService> _logger;
        private readonly ServiceResilienceState _resilienceState = new();

        public NotificationOutboxBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<NotificationOutboxBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("NotificationOutboxBackgroundService started.");

                // Wait 2 seconds before first polling pass
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    bool iterationSuccess = false;
                    try
                    {
                        await ProcessPendingBatchAsync(stoppingToken);
                        iterationSuccess = true;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        await BackgroundServiceResilienceHelper.HandleLoopExceptionAsync(
                            ex,
                            nameof(NotificationOutboxBackgroundService),
                            _logger,
                            _resilienceState,
                            stoppingToken);
                    }

                    if (iterationSuccess)
                    {
                        try
                        {
                            // 1 minute between normal runs
                            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }
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
                _logger.LogError(ex, "[{ServiceName}] [{CorrelationId}] Fatal unhandled exception in ExecuteAsync.", nameof(NotificationOutboxBackgroundService), correlationId);
            }
            finally
            {
                _logger.LogInformation("NotificationOutboxBackgroundService stopped.");
            }
        }

        public async Task<int> ProcessPendingBatchAsync(CancellationToken stoppingToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var outboxRepo = scope.ServiceProvider.GetRequiredService<INotificationOutboxRepository>();
            var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
            var whatsAppService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            var batch = await outboxRepo.GetPendingBatchAsync(20);
            int processed = 0;

            foreach (var item in batch)
            {
                if (stoppingToken.IsCancellationRequested) break;

                await outboxRepo.MarkProcessingAsync(item.OutboxID);
                processed++;

                try
                {
                    var user = await userRepo.GetByIdAsync(item.UserID);
                    if (user == null)
                    {
                        await outboxRepo.RecordFailureAsync(item.OutboxID, $"User #{item.UserID} not found.", item.RetryCount + 1, DateTime.UtcNow, true);
                        continue;
                    }

                    bool success = true;

                    if (string.Equals(item.Channel, "Email", StringComparison.OrdinalIgnoreCase))
                    {
                        if (user.NotificationEmailEnabled && !string.IsNullOrWhiteSpace(user.Email))
                        {
                            string baseUrl = (config["PublicBaseUrl"] ?? config["AppUrl"] ?? "https://bookmyfun.in").Trim().TrimEnd('/');
                            string? fullLink = string.IsNullOrWhiteSpace(item.LinkUrl)
                                ? null
                                : (item.LinkUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? item.LinkUrl : $"{baseUrl}{(item.LinkUrl.StartsWith("/") ? item.LinkUrl : "/" + item.LinkUrl)}");

                            string safeBodyHtml = $"<p style='margin:0 0 16px;'>{System.Net.WebUtility.HtmlEncode(item.Body)}</p>";
                            string html = $@"
                                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px;'>
                                    <div style='background: #0B4F6C; color: #FFFFFF; padding: 15px; border-radius: 6px; font-weight: bold; font-size: 1.15rem;'>
                                        Nayesta
                                    </div>
                                    <div style='padding: 20px 0;'>
                                        <h3 style='color: #1e293b; margin-top: 0;'>{System.Net.WebUtility.HtmlEncode(item.Title)}</h3>
                                        {safeBodyHtml}
                                        {(!string.IsNullOrEmpty(fullLink) ? $"<p style='margin-top: 20px;'><a href='{fullLink}' style='display: inline-block; background: #059669; color: #fff; padding: 10px 20px; text-decoration: none; border-radius: 6px; font-weight: bold;'>View details</a></p>" : "")}
                                    </div>
                                    <div style='border-top: 1px solid #e2e8f0; padding-top: 12px; font-size: 12px; color: #94a3b8;'>
                                        You received this message from Nayesta based on your account activity.
                                    </div>
                                </div>";
                            success = await emailService.SendEmailAsync(user.Email, item.Title, html, item.Body);
                        }
                    }
                    else if (string.Equals(item.Channel, "WhatsApp", StringComparison.OrdinalIgnoreCase))
                    {
                        // WhatsApp is skipped when templates are not configured/approved
                        success = true;
                    }
                    else if (string.Equals(item.Channel, "InApp", StringComparison.OrdinalIgnoreCase))
                    {
                        success = true; // Handled directly upon enqueue
                    }

                    if (success)
                    {
                        int attempts = Math.Max(1, item.RetryCount + 1);
                        await outboxRepo.MarkSentWithAttemptsAsync(item.OutboxID, attempts);
                    }
                    else
                    {
                        int maxRetries = item.MaxRetries > 0 ? item.MaxRetries : 3;
                        int nextRetry = item.RetryCount + 1;
                        bool isPerm = nextRetry >= maxRetries;
                        int delaySeconds = nextRetry switch { 1 => 10, 2 => 30, _ => 90 };
                        DateTime nextAttempt = DateTime.UtcNow.AddSeconds(delaySeconds);
                        string shortErr = EmailSafetyHelper.SanitizeShortError(null, emailService.LastSendError ?? "Email dispatch returned false.", config["Smtp:Password"]);
                        await outboxRepo.RecordFailureAsync(item.OutboxID, shortErr, nextRetry, nextAttempt, isPerm);
                    }
                }
                catch (Exception ex)
                {
                    if (BackgroundServiceResilienceHelper.IsMissingTableException(ex) || BackgroundServiceResilienceHelper.IsDatabaseUnreachableException(ex))
                    {
                        throw;
                    }

                    int maxRetries = item.MaxRetries > 0 ? item.MaxRetries : 3;
                    int nextRetry = item.RetryCount + 1;
                    bool isPerm = nextRetry >= maxRetries;
                    int delaySeconds = nextRetry switch { 1 => 10, 2 => 30, _ => 90 };
                    DateTime nextAttempt = DateTime.UtcNow.AddSeconds(delaySeconds);
                    string shortErr = EmailSafetyHelper.SanitizeShortError(ex, "Email dispatch failed.", config["Smtp:Password"]);
                    await outboxRepo.RecordFailureAsync(item.OutboxID, shortErr, nextRetry, nextAttempt, isPerm);
                    _logger.LogWarning("Failed to dispatch notification outbox item #{OutboxId}: {ShortError}", item.OutboxID, shortErr);
                }
            }

            return processed;
        }
    }
}
