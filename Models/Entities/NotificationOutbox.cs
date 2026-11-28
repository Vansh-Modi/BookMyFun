using System;

namespace BookMyFun.Models.Entities
{
    public class NotificationOutbox
    {
        public long OutboxID { get; set; }
        public string EventType { get; set; } = string.Empty;
        public int EntityID { get; set; }
        public int UserID { get; set; }
        public string Channel { get; set; } = "InApp"; // "InApp", "Email", "WhatsApp"
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? LinkUrl { get; set; }
        public string Status { get; set; } = "Pending"; // "Pending", "Processing", "Sent", "Failed", "Retry"
        public int RetryCount { get; set; } = 0;
        public int MaxRetries { get; set; } = 3;
        public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
        public string? LastError { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; }
        public string? DedupeKey { get; set; }
        public string? RecipientEmail { get; set; }
    }
}
