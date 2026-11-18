namespace BookMyFun.Services.Common
{
    /// <summary>
    /// Extension methods for uniform DateTime relative formatting across the application.
    /// </summary>
    public static class DateTimeExtensions
    {
        /// <summary>
        /// Converts a UTC DateTime to a user-friendly relative time string (e.g., "Just now", "5m ago", "2h ago", "3d ago", or date string).
        /// </summary>
        public static string ToTimeAgo(this DateTime utcDate)
        {
            var span = DateTime.UtcNow - utcDate;
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return utcDate.ToString("dd MMM yyyy");
        }
    }
}
