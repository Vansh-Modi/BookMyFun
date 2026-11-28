using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BookMyFun.Repositories;
using BookMyFun.Services.Common;
using Microsoft.Extensions.Configuration;
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
            var expiredIds = await bookingRepo.ExpireStaleHoldsWithIdsAsync();
            if (expiredIds.Count > 0)
            {
                _logger.LogInformation("Marked {Count} expired Hold booking(s) as Expired.", expiredIds.Count);
                await DispatchExpiredEmailsAsync(scope.ServiceProvider, bookingRepo, expiredIds);
            }
            return expiredIds.Count;
        }

        private async Task DispatchExpiredEmailsAsync(IServiceProvider sp, IBookingRepository bookingRepo, List<int> expiredIds)
        {
            var emailService = sp.GetService<IEmailService>();
            var userRepo = sp.GetService<IUserRepository>();
            var propRepo = sp.GetService<IPropertyRepository>();
            var config = sp.GetService<IConfiguration>();
            if (emailService == null || userRepo == null || propRepo == null) return;

            string baseUrl = (config?["PublicBaseUrl"] ?? config?["AppUrl"] ?? "https://bookmyfun.in").Trim().TrimEnd('/');

            foreach (int bookingId in expiredIds)
            {
                try
                {
                    var booking = await bookingRepo.GetByIdAsync(bookingId);
                    if (booking == null) continue;
                    var client = await userRepo.GetByIdAsync(booking.ClientID);
                    if (client == null || string.IsNullOrWhiteSpace(client.Email)) continue;
                    var property = await propRepo.GetByIdAsync(booking.PropertyID);
                    string propTitle = property?.Title ?? $"Property #{booking.PropertyID}";
                    string safeTitle = WebUtility.HtmlEncode(propTitle);
                    string safeName = WebUtility.HtmlEncode(client.FullName);
                    string searchUrl = $"{baseUrl}/Properties";

                    string bodyContent = $@"
                        <p>Hello {safeName},</p>
                        <p>Your booking hold #{booking.BookingID} for <strong>{safeTitle}</strong> ({booking.CheckInDate:dd MMM yyyy} to {booking.CheckOutDate:dd MMM yyyy}) has expired.</p>
                        <p><strong>Reason:</strong> Payment was not completed within the 15-minute hold window, so the dates have been released.</p>";

                    string plainText = $"Hello {client.FullName}, your booking hold #{booking.BookingID} for {propTitle} ({booking.CheckInDate:dd MMM yyyy} to {booking.CheckOutDate:dd MMM yyyy}) has expired. Reason: Payment was not completed within the 15-minute hold window.";

                    await emailService.SendTemplatedEmailAsync(
                        client.UserID,
                        booking.BookingID,
                        client.Email,
                        "BookingExpired",
                        $"booking-expired-{booking.BookingID}",
                        $"Booking hold expired for {propTitle}",
                        "Booking hold expired",
                        bodyContent,
                        "Browse properties",
                        searchUrl,
                        plainText);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send BookingExpired email for Booking #{BookingId}", bookingId);
                }
            }
        }
    }
}
