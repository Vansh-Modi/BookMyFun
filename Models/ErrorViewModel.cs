namespace BookMyFun.Models
{
    /// <summary>
    /// View model for displaying branded, user-friendly error pages with correlation tracking.
    /// </summary>
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        public int StatusCode { get; set; } = 500;

        public string? Title { get; set; }

        public string? Message { get; set; }

        public string? CorrelationId => RequestId;

        public bool IsNotFound => StatusCode == 404;

        public bool IsAccessDenied => StatusCode == 403;

        public bool IsServerError => StatusCode >= 500;
    }
}
