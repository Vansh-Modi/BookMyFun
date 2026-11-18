using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Services.Security
{
    /// <summary>
    /// Service for centralized application event and error handling, delivering consistent JSON for API/AJAX requests
    /// and branded error views for browser navigation.
    /// </summary>
    public interface ICustomErrorHandlerService
    {
        /// <summary>
        /// Handles unhandled exceptions, logging details with Correlation ID and returning a 500 response.
        /// </summary>
        IActionResult OnUnhandledException(HttpContext context, Exception ex);

        /// <summary>
        /// Handles 404 Not Found events, logging missing resources and returning a 404 response.
        /// </summary>
        IActionResult OnNotFound(HttpContext context, string? resource = null);

        /// <summary>
        /// Handles 403 Forbidden / Access Denied events, logging the user and returning a 403 response.
        /// </summary>
        IActionResult OnAccessDenied(HttpContext context, string? resource = null);

        /// <summary>
        /// Handles unauthenticated access by redirecting to Login while preserving the target returnUrl and query string.
        /// </summary>
        IActionResult OnLoginRequired(HttpContext context, string? returnUrl = null);

        /// <summary>
        /// Handles payment processing failures by updating booking state to PaymentFailed, logging reason, and notifying the client.
        /// </summary>
        Task OnPaymentFailedAsync(int bookingId, string errorMessage);
    }
}
