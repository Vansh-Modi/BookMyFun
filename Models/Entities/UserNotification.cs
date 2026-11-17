using System;

namespace BookMyFun.Models.Entities
{
    public class UserNotification
    {
        public int NotificationID { get; set; }
        public int UserID { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? LinkUrl { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
