using System.Globalization;

namespace BookMyFun.Models.Configuration
{
    public class BookingTimingOptions
    {
        public const string SectionName = "BookingTiming";

        public string CheckInTime { get; set; } = "11:00";
        public string CheckOutTime { get; set; } = "10:00";

        public string MorningSlotStart { get; set; } = "08:00";
        public string MorningSlotEnd { get; set; } = "14:00";

        public string AfternoonSlotStart { get; set; } = "12:00";
        public string AfternoonSlotEnd { get; set; } = "18:00";

        public string EveningSlotStart { get; set; } = "15:00";
        public string EveningSlotEnd { get; set; } = "21:00";

        public string NightStayStart { get; set; } = "21:00";
        public string NightStayEnd { get; set; } = "07:00";

        public TimeSpan CheckInTimeSpan => ParseTime(CheckInTime, new TimeSpan(11, 0, 0));
        public TimeSpan CheckOutTimeSpan => ParseTime(CheckOutTime, new TimeSpan(10, 0, 0));
        public TimeSpan MorningSlotStartSpan => ParseTime(MorningSlotStart, new TimeSpan(8, 0, 0));
        public TimeSpan MorningSlotEndSpan => ParseTime(MorningSlotEnd, new TimeSpan(14, 0, 0));
        public TimeSpan AfternoonSlotStartSpan => ParseTime(AfternoonSlotStart, new TimeSpan(12, 0, 0));
        public TimeSpan AfternoonSlotEndSpan => ParseTime(AfternoonSlotEnd, new TimeSpan(18, 0, 0));
        public TimeSpan EveningSlotStartSpan => ParseTime(EveningSlotStart, new TimeSpan(15, 0, 0));
        public TimeSpan EveningSlotEndSpan => ParseTime(EveningSlotEnd, new TimeSpan(21, 0, 0));
        public TimeSpan NightStayStartSpan => ParseTime(NightStayStart, new TimeSpan(21, 0, 0));
        public TimeSpan NightStayEndSpan => ParseTime(NightStayEnd, new TimeSpan(7, 0, 0));

        public bool IsHourlyStartAllowed(TimeSpan startTime) => startTime >= CheckInTimeSpan;

        public (DateTime Start, DateTime End) GetBookingWindow(
            DateTime checkInDate,
            DateTime checkOutDate,
            string? bookingType,
            string? slotType,
            TimeSpan? hourlyCheckInTime = null,
            int? hourlyDuration = null)
        {
            var inDate = checkInDate.Date;
            var outDate = checkOutDate.Date;

            if (string.Equals(bookingType, "HalfDay", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(slotType, "MorningSlot", StringComparison.OrdinalIgnoreCase))
                {
                    return (inDate.Add(MorningSlotStartSpan), inDate.Add(MorningSlotEndSpan));
                }
                if (string.Equals(slotType, "AfternoonSlot", StringComparison.OrdinalIgnoreCase))
                {
                    return (inDate.Add(AfternoonSlotStartSpan), inDate.Add(AfternoonSlotEndSpan));
                }
                if (string.Equals(slotType, "NightStay", StringComparison.OrdinalIgnoreCase))
                {
                    return (inDate.Add(NightStayStartSpan), inDate.AddDays(1).Add(NightStayEndSpan));
                }
                // Default / EveningSlot
                return (inDate.Add(EveningSlotStartSpan), inDate.Add(EveningSlotEndSpan));
            }

            if (string.Equals(bookingType, "Hourly", StringComparison.OrdinalIgnoreCase))
            {
                var start = inDate.Add(hourlyCheckInTime ?? CheckInTimeSpan);
                int hours = hourlyDuration.HasValue && hourlyDuration.Value > 0 ? hourlyDuration.Value : 4;
                return (start, start.AddHours(hours));
            }

            // FullDay (default)
            var effectiveOut = outDate > inDate ? outDate : inDate.AddDays(1);
            return (inDate.Add(CheckInTimeSpan), effectiveOut.Add(CheckOutTimeSpan));
        }

        public static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
            => aStart < bEnd && aEnd > bStart;

        private static TimeSpan ParseTime(string? value, TimeSpan fallback)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                TimeSpan.TryParseExact(value.Trim(), new[] { @"hh\:mm", @"h\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var ts))
            {
                return ts;
            }
            return fallback;
        }
    }
}
