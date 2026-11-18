using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookMyFun.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services.Notifications
{
    public class SubscriptionLifecycleBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SubscriptionLifecycleBackgroundService> _logger;

        public SubscriptionLifecycleBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<SubscriptionLifecycleBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SubscriptionLifecycleBackgroundService initialized.");

            // Wait 20 seconds before first run to allow app startup to settle
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessSubscriptionLifecycleCycleAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in SubscriptionLifecycleBackgroundService hourly run.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("SubscriptionLifecycleBackgroundService stopped.");
        }

        public async Task ProcessSubscriptionLifecycleCycleAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var subRepo = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
            var propRepo = scope.ServiceProvider.GetRequiredService<IPropertyRepository>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcherService>();

            // 1. Process Newly Active Scheduled Subscriptions
            try
            {
                var newlyActiveSubs = await subRepo.GetNewlyActiveScheduledSubscriptionsAsync();
                foreach (var sub in newlyActiveSubs)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    // When a paid plan becomes active, unpublished properties are NOT republished automatically.
                    // The owner chooses which to publish, up to the plan's MaxListings.
                    var unpubProps = (await propRepo.GetPropertiesByOwnerIdAsync(sub.OwnerID))
                        .Where(p => p.ListingStatus == "Unpublished" || p.ListingStatus == "Deactivated")
                        .Select(p => p.Title)
                        .ToList();

                    int limit = sub.MaxListings > 0 ? sub.MaxListings : 1;
                    await dispatcher.DispatchPostPaymentPublishPromptAsync(
                        sub.OwnerID,
                        sub.SubscriptionID,
                        sub.PlanName ?? "Subscription",
                        unpubProps,
                        limit);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing newly active subscriptions.");
            }

            // 2. Process Expired Subscriptions (Hourly, No Grace Period)
            try
            {
                var configRepo = scope.ServiceProvider.GetRequiredService<IConfigRepository>();
                var expiredSubs = await subRepo.GetExpiredSubscriptionsForHourlyProcessAsync();
                foreach (var sub in expiredSubs)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    int unpublishedCount = await propRepo.BatchUnpublishPropertiesByOwnerIdAsync(
                        sub.OwnerID,
                        "Subscription expired");

                    // Write one row to AuditLogs per owner, with the action, the count of properties affected, and the time.
                    await configRepo.LogAuditAsync(
                        sub.OwnerID,
                        "SubscriptionExpired_PropertiesUnpublished",
                        "OwnerSubscription",
                        sub.SubscriptionID,
                        $"Subscription #{sub.SubscriptionID} ({sub.PlanName}) expired for Owner #{sub.OwnerID}. Unpublished {unpublishedCount} live listing(s). Properties remain in owner account."
                    );

                    await dispatcher.DispatchSubscriptionExpiredAsync(
                        sub.OwnerID,
                        sub.PlanName ?? "Subscription",
                        sub.SubscriptionID,
                        unpublishedCount);

                    _logger.LogInformation(
                        "Processed expired subscription for owner #{OwnerId}. Unpublished {Count} properties.",
                        sub.OwnerID, unpublishedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing expired subscriptions.");
            }

            // 3. Process 3-Day Advance Expiry Warnings (Trial or Paid)
            try
            {
                var expiringSubs = await subRepo.GetExpiringSubscriptionsAsync(3);
                foreach (var sub in expiringSubs)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    bool isTrial = sub.IsTrial || 
                                   string.Equals(sub.PaymentStatus, "Trial", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(sub.PlanName, "Free Trial", StringComparison.OrdinalIgnoreCase);

                    if (isTrial)
                    {
                        await dispatcher.DispatchTrialExpiringWarningAsync(
                            sub.OwnerID,
                            sub.PlanName ?? "Free Trial",
                            sub.EndDate);
                    }
                    else
                    {
                        await dispatcher.DispatchSubscriptionExpiringWarningAsync(
                            sub.OwnerID,
                            sub.PlanName ?? "Subscription",
                            sub.EndDate);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing 3-day subscription expiry warnings.");
            }
        }
    }
}
