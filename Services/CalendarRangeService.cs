using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;

namespace BookMyFun.Services
{
    public class CalendarValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int Nights { get; set; }
        public decimal CalculatedPrice { get; set; }
    }

    public interface ICalendarRangeService
    {
        CalendarValidationResult ValidateAndCalculate(
            DateTime checkIn,
            DateTime checkOut,
            int minStayNights,
            int maxStayNights,
            decimal basePricePerNight,
            string bookingType = "FullDay",
            string? slotType = null,
            int? hourlyDuration = null,
            decimal eventPackagePrice = 0,
            IEnumerable<string>? blockedDatesIso = null);

        Task<List<string>> GetBlockedDatesForPropertyAsync(int propertyId);
    }

    public class CalendarRangeService : ICalendarRangeService
    {
        private readonly IBookingRepository _bookingRepo;

        public CalendarRangeService(IBookingRepository bookingRepo)
        {
            _bookingRepo = bookingRepo;
        }

        public CalendarValidationResult ValidateAndCalculate(
            DateTime checkIn,
            DateTime checkOut,
            int minStayNights,
            int maxStayNights,
            decimal basePricePerNight,
            string bookingType = "FullDay",
            string? slotType = null,
            int? hourlyDuration = null,
            decimal eventPackagePrice = 0,
            IEnumerable<string>? blockedDatesIso = null)
        {
            var inDate = checkIn.Date;
            var outDate = checkOut.Date;
            int nights = Math.Max(0, (outDate - inDate).Days);

            if (string.Equals(bookingType, "FullDay", StringComparison.OrdinalIgnoreCase))
            {
                if (outDate <= inDate)
                {
                    return new CalendarValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Check-out date must be strictly after check-in date.",
                        Nights = 0,
                        CalculatedPrice = 0
                    };
                }

                if (minStayNights > 0 && nights < minStayNights)
                {
                    return new CalendarValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Minimum stay for this property is {minStayNights} {(minStayNights == 1 ? "night" : "nights")}.",
                        Nights = nights,
                        CalculatedPrice = 0
                    };
                }

                if (maxStayNights > 0 && nights > maxStayNights)
                {
                    return new CalendarValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Maximum stay for this property is {maxStayNights} nights.",
                        Nights = nights,
                        CalculatedPrice = 0
                    };
                }

                // Verify no blocked dates are included in the selected range
                if (blockedDatesIso != null)
                {
                    var blockedSet = new HashSet<string>(blockedDatesIso, StringComparer.OrdinalIgnoreCase);
                    for (var d = inDate; d <= outDate; d = d.AddDays(1))
                    {
                        var iso = d.ToString("yyyy-MM-dd");
                        if (blockedSet.Contains(iso))
                        {
                            return new CalendarValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Selected range includes an unavailable date ({d:dd MMM yyyy}). Please select a continuous available range.",
                                Nights = nights,
                                CalculatedPrice = 0
                            };
                        }
                    }
                }
            }
            else
            {
                nights = 1;
            }

            // Live Price Calculation
            decimal price = 0;
            if (string.Equals(bookingType, "FullDay", StringComparison.OrdinalIgnoreCase))
            {
                price = (basePricePerNight * nights) + eventPackagePrice;
            }
            else if (string.Equals(bookingType, "HalfDay", StringComparison.OrdinalIgnoreCase))
            {
                price = Math.Round(basePricePerNight * 0.55m) + eventPackagePrice;
            }
            else if (string.Equals(bookingType, "Hourly", StringComparison.OrdinalIgnoreCase))
            {
                int hours = hourlyDuration.HasValue && hourlyDuration.Value > 0 ? hourlyDuration.Value : 4;
                price = Math.Round((basePricePerNight / 10m) * hours) + eventPackagePrice;
            }
            else
            {
                price = (basePricePerNight * Math.Max(1, nights)) + eventPackagePrice;
            }

            return new CalendarValidationResult
            {
                IsValid = true,
                ErrorMessage = null,
                Nights = nights,
                CalculatedPrice = price
            };
        }

        public async Task<List<string>> GetBlockedDatesForPropertyAsync(int propertyId)
        {
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

            var blockedSet = new HashSet<string>();

            foreach (var b in bookings)
            {
                if (b.PropertyID != propertyId) continue;

                // Ignore Cancelled bookings
                if (string.Equals(b.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isConfirmed = string.Equals(b.Status, "Confirmed", StringComparison.OrdinalIgnoreCase);
                bool isActivePending = string.Equals(b.Status, "Pending", StringComparison.OrdinalIgnoreCase) &&
                    (b.CreatedAt == default || b.CreatedAt >= DateTime.UtcNow.AddMinutes(-15));

                if (!isConfirmed && !isActivePending)
                    continue;

                // Expand every date from CheckInDate to CheckOutDate (inclusive)
                for (var d = b.CheckInDate.Date; d <= b.CheckOutDate.Date; d = d.AddDays(1))
                {
                    blockedSet.Add(d.ToString("yyyy-MM-dd"));
                }
            }

            return blockedSet.OrderBy(d => d).ToList();
        }
    }
}
