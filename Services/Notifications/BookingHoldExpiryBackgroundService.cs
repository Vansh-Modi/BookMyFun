using System;
using System.Threading;
using System.Threading.Tasks;
using BookMyFun.Repositories;
using BookMyFun.Services.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services.Notifications
{
    /// <summary>
    /// Background service running every 5 minutes to mark expired 'Hold' bookings as 'Expired'
    /// and record an AuditLogs entry without deleting any rows.
    /// </summary>
    public class BookingHoldExpiryBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BookingHoldExpiryBackgroundService> _logger;
        private readonly ServiceResilienceState _resilienceState = new();
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        public BookingHoldExpiryBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<BookingHoldExpiryBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BookingHoldExpiryBackgroundService started (interval: 5 minutes).");

            using var timer = new PeriodicTimer(Interval);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await BackgroundServiceResilienceHelper.HandleLoopExceptionAsync(
                        ex,
                        nameof(BookingHoldExpiryBackgroundService),
                        _logger,
                        _resilienceState,
                        stoppingToken);
                }

                try
                {
                    if (!await timer.WaitForNextTickAsync(stoppingToken))
                        break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("BookingHoldExpiryBackgroundService stopped.");
        }

        public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var bookingRepo = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
            int expiredCount = await bookingRepo.ExpireStaleHoldsAsync();
            if (expiredCount > 0)
            {
                _logger.LogInformation("Marked {Count} expired Hold booking(s) as Expired.", expiredCount);
            }
            return expiredCount;
        }
    }
}
