using System;
using System.Threading;
using System.Threading.Tasks;
using BookMyFun.Services.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services.Notifications
{
    public class CheckInReminderBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CheckInReminderBackgroundService> _logger;
        private readonly ServiceResilienceState _resilienceState = new();

        public CheckInReminderBackgroundService(IServiceProvider serviceProvider, ILogger<CheckInReminderBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("CheckInReminderBackgroundService initialized.");

                // Initial wait of 30 seconds after startup before the first run
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
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
                        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcherService>();
                        await dispatcher.DispatchCheckInRemindersAsync();
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
                            nameof(CheckInReminderBackgroundService),
                            _logger,
                            _resilienceState,
                            stoppingToken);
                    }

                    if (iterationSuccess)
                    {
                        try
                        {
                            // 5 minutes between normal runs (within 1 to 5 minute requirement)
                            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
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
                _logger.LogError(ex, "[{ServiceName}] [{CorrelationId}] Fatal unhandled exception in ExecuteAsync.", nameof(CheckInReminderBackgroundService), correlationId);
            }
            finally
            {
                _logger.LogInformation("CheckInReminderBackgroundService stopped.");
            }
        }
    }
}
