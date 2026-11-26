using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Models.Configuration;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using Microsoft.Extensions.Options;

namespace BookMyFun.Services
{
    public class DateRangeSuggestion
    {
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public int DistanceDays { get; set; }
        public string FormattedRange { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty; // "Earlier" or "Later"
    }

    public class AvailabilitySuggestionResult
    {
        public int PropertyId { get; set; }
        public bool IsRequestedAvailable { get; set; }
        public DateTime RequestedCheckIn { get; set; }
        public DateTime RequestedCheckOut { get; set; }
        public int Nights { get; set; }
        public List<DateRangeSuggestion> Suggestions { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }

    public interface IAvailabilitySuggestionService
    {
        Task<AvailabilitySuggestionResult> GetNearestSuggestionsAsync(
            int propertyId,
            DateTime checkIn,
            DateTime checkOut,
            string bookingType = "FullDay",
            string? slotType = null,
            DateTime? relativeToday = null);
    }

    public class AvailabilitySuggestionService : IAvailabilitySuggestionService
    {
        private readonly IBookingRepository _bookingRepo;
        private readonly BookingTimingOptions _timingOptions;

        public AvailabilitySuggestionService(IBookingRepository bookingRepo, IOptions<BookingTimingOptions>? timingOptions = null)
        {
            _bookingRepo = bookingRepo;
            _timingOptions = timingOptions?.Value ?? new BookingTimingOptions();
        }

        public async Task<AvailabilitySuggestionResult> GetNearestSuggestionsAsync(
            int propertyId,
            DateTime checkIn,
            DateTime checkOut,
            string bookingType = "FullDay",
            string? slotType = null,
            DateTime? relativeToday = null)
        {
            var reqIn = checkIn.Date;
            var reqOut = checkOut.Date;
            int nights = (reqOut - reqIn).Days;
            if (nights <= 0) nights = 1;

            DateTime today = (relativeToday ?? DateTime.UtcNow).Date;

            // Fetch existing bookings for property to evaluate availability
            List<Booking> propertyBookings;
            try
            {
                propertyBookings = await _bookingRepo.GetBookingsByPropertyIdAsync(propertyId);
                if (propertyBookings == null || propertyBookings.Count == 0)
                {
                    var all = await _bookingRepo.GetAllBookingsAsync();
                    propertyBookings = all.Where(b => b.PropertyID == propertyId).ToList();
                }
            }
            catch
            {
                propertyBookings = new List<Booking>();
            }

            // Check if requested dates are available
            bool isRequestedAvailable = await CheckWindowAvailabilityAsync(
                propertyId, reqIn, reqOut, bookingType, slotType, propertyBookings);

            var result = new AvailabilitySuggestionResult
            {
                PropertyId = propertyId,
                IsRequestedAvailable = isRequestedAvailable,
                RequestedCheckIn = reqIn,
                RequestedCheckOut = reqOut,
                Nights = nights
            };

            if (isRequestedAvailable)
            {
                result.Message = "Requested dates are available.";
                return result;
            }

            // Search forward and backward from requested check-in, up to 30 days
            var candidates = new List<DateRangeSuggestion>();
            var visitedStarts = new HashSet<DateTime>();

            for (int d = 1; d <= 30; d++)
            {
                // Backward candidate: search earlier dates
                DateTime backStart = reqIn.AddDays(-d);
                DateTime backEnd = backStart.AddDays(nights);

                if (backStart >= today && visitedStarts.Add(backStart))
                {
                    bool backAvailable = await CheckWindowAvailabilityAsync(
                        propertyId, backStart, backEnd, bookingType, slotType, propertyBookings);

                    if (backAvailable)
                    {
                        candidates.Add(new DateRangeSuggestion
                        {
                            CheckInDate = backStart,
                            CheckOutDate = backEnd,
                            DistanceDays = d,
                            Direction = "Earlier",
                            FormattedRange = $"{backStart:dd MMM yyyy} - {backEnd:dd MMM yyyy}"
                        });
                    }
                }

                // Forward candidate: search later dates
                DateTime fwdStart = reqIn.AddDays(d);
                DateTime fwdEnd = fwdStart.AddDays(nights);

                if (visitedStarts.Add(fwdStart))
                {
                    bool fwdAvailable = await CheckWindowAvailabilityAsync(
                        propertyId, fwdStart, fwdEnd, bookingType, slotType, propertyBookings);

                    if (fwdAvailable)
                    {
                        candidates.Add(new DateRangeSuggestion
                        {
                            CheckInDate = fwdStart,
                            CheckOutDate = fwdEnd,
                            DistanceDays = d,
                            Direction = "Later",
                            FormattedRange = $"{fwdStart:dd MMM yyyy} - {fwdEnd:dd MMM yyyy}"
                        });
                    }
                }

                // If we've gathered at least 3 candidates, order them and return
                if (candidates.Count >= 3)
                {
                    break;
                }
            }

            // Order by distance from original dates, then earlier start date
            var topSuggestions = candidates
                .OrderBy(c => c.DistanceDays)
                .ThenBy(c => c.CheckInDate)
                .Take(3)
                .ToList();

            result.Suggestions = topSuggestions;

            if (topSuggestions.Count > 0)
            {
                result.Message = $"Nearest available dates: {topSuggestions[0].FormattedRange} ({nights} {(nights == 1 ? "night" : "nights")}).";
            }
            else
            {
                // STRICT specification requirement:
                result.Message = "No nearby dates available. Try a different range.";
            }

            return result;
        }

        private async Task<bool> CheckWindowAvailabilityAsync(
            int propertyId,
            DateTime start,
            DateTime end,
            string bookingType,
            string? slotType,
            List<Booking> propertyBookings)
        {
            if (propertyBookings != null && propertyBookings.Count > 0)
            {
                // Evaluate using the exact booking engine rules:
                // Excludes conflicting Pending and Confirmed bookings, ignores Cancelled bookings.
                foreach (var b in propertyBookings)
                {
                    if (b.PropertyID != propertyId) continue;

                    // Ignores Cancelled bookings
                    if (string.Equals(b.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isConfirmed = string.Equals(b.Status, "Confirmed", StringComparison.OrdinalIgnoreCase);
                    bool isActivePending = string.Equals(b.Status, "Pending", StringComparison.OrdinalIgnoreCase) &&
                        (b.CreatedAt == default || b.CreatedAt >= DateTime.UtcNow.AddMinutes(-15));

                    if (!isConfirmed && !isActivePending)
                        continue;

                    var (reqStart, reqEnd) = _timingOptions.GetBookingWindow(
                        start, end, bookingType, slotType);

                    int? existingHours = null;
                    if (b.SlotStartTime.HasValue && b.SlotEndTime.HasValue)
                    {
                        var diff = (b.SlotEndTime.Value - b.SlotStartTime.Value).TotalHours;
                        if (diff <= 0) diff += 24;
                        existingHours = Math.Max(1, (int)Math.Ceiling(diff));
                    }

                    var (exStart, exEnd) = _timingOptions.GetBookingWindow(
                        b.CheckInDate,
                        b.CheckOutDate,
                        string.IsNullOrEmpty(b.BookingType) ? "FullDay" : b.BookingType,
                        b.SlotType,
                        b.SlotStartTime,
                        existingHours);

                    if (BookingTimingOptions.Overlaps(reqStart, reqEnd, exStart, exEnd))
                    {
                        return false; // Conflicting booking found
                    }
                }

                return true;
            }

            // Fallback to repository method if no list of bookings was retrieved
            return await _bookingRepo.IsSlotAvailableAsync(propertyId, start, end, bookingType, slotType);
        }
    }
}
