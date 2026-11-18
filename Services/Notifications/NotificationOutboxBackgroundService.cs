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
                        using var scope = _serviceProvider.CreateScope();
                        var outboxRepo = scope.ServiceProvider.GetRequiredService<INotificationOutboxRepository>();
                        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                        var whatsAppService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
                        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                        var batch = await outboxRepo.GetPendingBatchAsync(20);

                        foreach (var item in batch)
                        {
                            if (stoppingToken.IsCancellationRequested) break;

                            await outboxRepo.MarkProcessingAsync(item.OutboxID);

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
                                        string html = $@"
                                            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px;'>
                                                <div style='background: #0f172a; color: #f59e0b; padding: 15px; border-radius: 6px; font-weight: bold; font-size: 1.15rem;'>
                                                    BookMyFun Notification
                                                </div>
                                                <div style='padding: 20px 0;'>
                                                    <h3 style='color: #1e293b; margin-top: 0;'>{item.Title}</h3>
                                                    <p style='color: #475569; font-size: 15px; line-height: 1.6;'>{item.Body}</p>
                                                    {(!string.IsNullOrEmpty(item.LinkUrl) ? $"<p style='margin-top: 20px;'><a href='{item.LinkUrl}' style='display: inline-block; background: #059669; color: #fff; padding: 10px 20px; text-decoration: none; border-radius: 6px; font-weight: bold;'>View Details</a></p>" : "")}
                                                </div>
                                                <div style='border-top: 1px solid #e2e8f0; padding-top: 12px; font-size: 12px; color: #94a3b8;'>
                                                    You received this message based on your BookMyFun account preferences.
                                                </div>
                                            </div>";
                                        success = await emailService.SendEmailAsync(user.Email, item.Title, html);
                                    }
                                }
                                else if (string.Equals(item.Channel, "WhatsApp", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Strict compliance: only opted in and verified WhatsApp numbers
                                    if (user.NotificationWhatsAppEnabled && user.WhatsAppVerified)
                                    {
                                        string? phone = user.WhatsAppPhone ?? user.Phone;
                                        if (!string.IsNullOrWhiteSpace(phone))
                                        {
                                            // Meta-approved template name from configuration
                                            string templateKey = $"WhatsApp:Templates:{item.EventType}";
                                            string templateName = config[templateKey] ?? $"bmf_{item.EventType.ToLowerInvariant()}";

                                            // Strict security: Never send free text, contact details, or another party's private information
                                            string safeTemplateMessage = $"{templateName}: {item.Title}. Details ID #{item.EntityID}.";
                                            success = await whatsAppService.SendWhatsAppMessageAsync(phone, safeTemplateMessage);
                                        }
                                    }
                                }
                                else if (string.Equals(item.Channel, "InApp", StringComparison.OrdinalIgnoreCase))
                                {
                                    success = true; // Handled directly upon enqueue
                                }

                                if (success)
                                {
                                    await outboxRepo.MarkSentAsync(item.OutboxID);
                                }
                                else
                                {
                                    int nextRetry = item.RetryCount + 1;
                                    bool isPerm = nextRetry >= item.MaxRetries;
                                    int delaySeconds = nextRetry switch { 1 => 10, 2 => 30, _ => 90 };
                                    DateTime nextAttempt = DateTime.UtcNow.AddSeconds(delaySeconds);
                                    await outboxRepo.RecordFailureAsync(item.OutboxID, "Dispatch returned false.", nextRetry, nextAttempt, isPerm);
                                }
                            }
                            catch (Exception ex)
                            {
                                if (BackgroundServiceResilienceHelper.IsMissingTableException(ex) || BackgroundServiceResilienceHelper.IsDatabaseUnreachableException(ex))
                                {
                                    throw;
                                }

                                int nextRetry = item.RetryCount + 1;
                                bool isPerm = nextRetry >= item.MaxRetries;
                                int delaySeconds = nextRetry switch { 1 => 10, 2 => 30, _ => 90 };
                                DateTime nextAttempt = DateTime.UtcNow.AddSeconds(delaySeconds);
                                await outboxRepo.RecordFailureAsync(item.OutboxID, ex.Message, nextRetry, nextAttempt, isPerm);
                                _logger.LogWarning(ex, "Failed to dispatch notification outbox item #{OutboxId}", item.OutboxID);
                            }
                        }

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
    }
}
