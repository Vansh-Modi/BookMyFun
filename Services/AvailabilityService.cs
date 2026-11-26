using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Models.Configuration;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using Microsoft.Extensions.Options;

namespace BookMyFun.Services
{
    public interface IAvailabilityService
    {
        Task<bool> IsAvailableAsync(
            int propertyId,
            DateTime checkIn,
            DateTime checkOut,
            int? excludeHoldId = null,
            string bookingType = "FullDay",
            string? slotType = null,
            TimeSpan? hourlyCheckInTime = null,
            int? hourlyDuration = null);

        bool IsBookingBlocking(Booking booking, DateTime? utcNow = null);

        bool DoesBookingOverlap(
            Booking existing,
            DateTime checkIn,
            DateTime checkOut,
            string bookingType = "FullDay",
            string? slotType = null,
            TimeSpan? hourlyCheckInTime = null,
            int? hourlyDuration = null);
    }

    public class AvailabilityService : IAvailabilityService
    {
        private readonly IBookingRepository _bookingRepo;
        private readonly BookingTimingOptions _timingOptions;
        private readonly BookingOptions _bookingOptions;

        public AvailabilityService(
            IBookingRepository bookingRepo,
            IOptions<BookingTimingOptions>? timingOptions = null,
            IOptions<BookingOptions>? bookingOptions = null)
        {
            _bookingRepo = bookingRepo;
            _timingOptions = timingOptions?.Value ?? new BookingTimingOptions();
            _bookingOptions = bookingOptions?.Value ?? new BookingOptions();
        }

        public bool IsBookingBlocking(Booking booking, DateTime? utcNow = null)
        {
            if (booking == null) return false;

            if (string.Equals(booking.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(booking.Status, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(booking.Status, "Hold", StringComparison.OrdinalIgnoreCase))
            {
                var now = utcNow ?? DateTime.UtcNow;
                if (booking.HoldExpiresAt.HasValue)
                {
                    return booking.HoldExpiresAt.Value > now;
                }
                return booking.CreatedAt != default &&
                       booking.CreatedAt > now.AddMinutes(-Math.Max(1, _bookingOptions.HoldMinutes));
            }

            // Cancelled, Expired, Failed, PaymentFailed, Pending never block dates
            return false;
        }

        public bool DoesBookingOverlap(
            Booking existing,
            DateTime checkIn,
            DateTime checkOut,
            string bookingType = "FullDay",
            string? slotType = null,
            TimeSpan? hourlyCheckInTime = null,
            int? hourlyDuration = null)
        {
            var (reqStart, reqEnd) = _timingOptions.GetBookingWindow(
                checkIn, checkOut, bookingType, slotType, hourlyCheckInTime, hourlyDuration);

            int? existingHours = null;
            if (existing.SlotStartTime.HasValue && existing.SlotEndTime.HasValue)
            {
                var diff = (existing.SlotEndTime.Value - existing.SlotStartTime.Value).TotalHours;
                if (diff <= 0) diff += 24;
                existingHours = Math.Max(1, (int)Math.Ceiling(diff));
            }

            var (exStart, exEnd) = _timingOptions.GetBookingWindow(
                existing.CheckInDate,
                existing.CheckOutDate,
                string.IsNullOrEmpty(existing.BookingType) ? "FullDay" : existing.BookingType,
                existing.SlotType,
                existing.SlotStartTime,
                existingHours);

            return BookingTimingOptions.Overlaps(reqStart, reqEnd, exStart, exEnd);
        }

        public async Task<bool> IsAvailableAsync(
            int propertyId,
            DateTime checkIn,
            DateTime checkOut,
            int? excludeHoldId = null,
            string bookingType = "FullDay",
            string? slotType = null,
            TimeSpan? hourlyCheckInTime = null,
            int? hourlyDuration = null)
        {
            if (string.Equals(bookingType, "Hourly", StringComparison.OrdinalIgnoreCase))
            {
                var startToCheck = hourlyCheckInTime ?? _timingOptions.CheckInTimeSpan;
                if (!_timingOptions.IsHourlyStartAllowed(startToCheck))
                {
                    return false;
                }
            }

            List<Booking> bookings;
            try
            {
                bookings = await _bookingRepo.GetBookingsByPropertyIdAsync(propertyId);
                if (bookings == null || bookings.Count == 0)
                {
                    var all = await _bookingRepo.GetAllBookingsAsync();
                    bookings = all.Where(b => b.PropertyID == propertyId).ToList();
                }
            }
            catch
            {
                bookings = new List<Booking>();
            }

            var utcNow = DateTime.UtcNow;
            foreach (var b in bookings)
            {
                if (b.PropertyID != propertyId) continue;
                if (excludeHoldId.HasValue && b.BookingID == excludeHoldId.Value) continue;
                if (!IsBookingBlocking(b, utcNow)) continue;

                if (DoesBookingOverlap(b, checkIn, checkOut, bookingType, slotType, hourlyCheckInTime, hourlyDuration))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
