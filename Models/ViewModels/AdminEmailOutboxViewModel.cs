namespace BookMyFun.Models.ViewModels
{
    public class AdminEmailOutboxViewModel
    {
        public string SelectedStatus { get; set; } = "All";
        public int TotalCount { get; set; }
        public int SentCount { get; set; }
        public int PendingCount { get; set; }
        public int RetryCount { get; set; }
        public int FailedCount { get; set; }
        public List<EmailOutboxRowViewModel> Items { get; set; } = new();
    }

    public class EmailOutboxRowViewModel
    {
        public long OutboxID { get; set; }
        public string Status { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string MaskedRecipient { get; set; } = "***";
        public string Subject { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public string? LastError { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }
}
