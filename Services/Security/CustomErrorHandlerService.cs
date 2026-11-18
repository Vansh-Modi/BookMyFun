using System.Diagnostics;
using BookMyFun.Models;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BookMyFun.Services.Security
{
    /// <summary>
    /// Centralized application event and error handler service.
    /// Provides consistent JSON payloads for API/AJAX callers and branded error views for browser navigation.
    /// </summary>
    public class CustomErrorHandlerService : ICustomErrorHandlerService
    {
        private readonly ILogger<CustomErrorHandlerService> _logger;
        private readonly IHostEnvironment _env;
        private readonly IBookingRepository _bookingRepo;
        private readonly IUserRepository? _userRepo;
        private readonly IEmailService? _emailService;

        public CustomErrorHandlerService(
            ILogger<CustomErrorHandlerService> logger,
            IHostEnvironment env,
            IBookingRepository bookingRepo,
            IUserRepository? userRepo = null,
            IEmailService? emailService = null)
        {
            _logger = logger;
            _env = env;
            _bookingRepo = bookingRepo;
            _userRepo = userRepo;
            _emailService = emailService;
        }

        public IActionResult OnUnhandledException(HttpContext context, Exception ex)
        {
            string correlationId = Activity.Current?.Id ?? context.TraceIdentifier;
            _logger.LogError(ex, "[{CorrelationId}] Unhandled exception at {Method} {Path}",
                correlationId, context.Request.Method, context.Request.Path);

            if (IsApiOrAjaxRequest(context.Request))
            {
                return new JsonResult(new
                {
                    success = false,
                    message = "An unexpected error occurred. Please try again later or quote reference " + correlationId + " to support.",
                    correlationId = correlationId
                })
                {
                    StatusCode = 500
                };
            }

            var vm = new ErrorViewModel
            {
                StatusCode = 500,
                RequestId = correlationId,
                Title = "Something Went Wrong (500)",
                Message = "We apologize for the inconvenience. An unexpected system error occurred on our end. Please quote the reference ID below to our support team."
            };

            return CreateErrorViewResult(vm, 500);
        }

        public IActionResult OnNotFound(HttpContext context, string? resource = null)
        {
            string correlationId = context.TraceIdentifier;
            string targetResource = resource ?? context.Request.Path.Value ?? "/";
            _logger.LogWarning("[{CorrelationId}] Resource not found: {Path}", correlationId, targetResource);

            if (IsApiOrAjaxRequest(context.Request))
            {
                return new JsonResult(new
                {
                    success = false,
                    message = "The requested resource was not found.",
                    correlationId = correlationId
                })
                {
                    StatusCode = 404
                };
            }

            var vm = new ErrorViewModel
            {
                StatusCode = 404,
                RequestId = correlationId,
                Title = "Page Not Found (404)",
                Message = "The page or destination you are searching for might have been moved, renamed, or is temporarily unavailable."
            };

            return CreateErrorViewResult(vm, 404);
        }

        public IActionResult OnAccessDenied(HttpContext context, string? resource = null)
        {
            string correlationId = context.TraceIdentifier;
            string targetResource = resource ?? context.Request.Path.Value ?? "/";
            string userName = context.User.Identity?.Name ?? "Anonymous";

            _logger.LogWarning("[{CorrelationId}] Access restricted for user {User} targeting {Path}",
                correlationId, userName, targetResource);

            if (IsApiOrAjaxRequest(context.Request))
            {
                return new JsonResult(new
                {
                    success = false,
                    message = "Access restricted. You do not have permission to access this resource.",
                    correlationId = correlationId
                })
                {
                    StatusCode = 403
                };
            }

            var vm = new ErrorViewModel
            {
                StatusCode = 403,
                RequestId = correlationId,
                Title = "Access Restricted (403)",
                Message = "You do not have permission to view or manage this resource. If you believe this is in error, please verify that you are signed in with the correct account."
            };

            return CreateErrorViewResult(vm, 403);
        }

        public IActionResult OnLoginRequired(HttpContext context, string? returnUrl = null)
        {
            string target = string.IsNullOrWhiteSpace(returnUrl)
                ? (context.Request.Path + context.Request.QueryString)
                : returnUrl;

            if (IsApiOrAjaxRequest(context.Request))
            {
                return new JsonResult(new
                {
                    success = false,
                    message = "Authentication required. Please sign in to proceed.",
                    correlationId = context.TraceIdentifier,
                    loginUrl = $"/Account/Login?returnUrl={Uri.EscapeDataString(target)}"
                })
                {
                    StatusCode = 401
                };
            }

            return new RedirectToActionResult("Login", "Account", new { returnUrl = target });
        }

        public async Task OnPaymentFailedAsync(int bookingId, string errorMessage)
        {
            _logger.LogError("Payment processing failed for booking {BookingId}: {ErrorMessage}", bookingId, errorMessage);

            try
            {
                await _bookingRepo.UpdateBookingStatusAsync(bookingId, "PaymentFailed");

                if (_bookingRepo != null && _userRepo != null && _emailService != null)
                {
                    var booking = await _bookingRepo.GetByIdAsync(bookingId);
                    if (booking != null)
                    {
                        var client = await _userRepo.GetByIdAsync(booking.ClientID);
                        if (client != null && !string.IsNullOrWhiteSpace(client.Email))
                        {
                            string retryUrl = $"/Booking/Payment/{bookingId}";
                            string subject = $"Payment Action Required: Booking #{bookingId}";
                            string body = $@"
                                <h2>Payment Could Not Be Processed</h2>
                                <p>Dear {client.FullName},</p>
                                <p>We were unable to complete the payment for your booking #{bookingId} due to the following reason:</p>
                                <p style='color: #c0392b; font-weight: bold;'>{errorMessage}</p>
                                <p>Your reservation is on hold. You can retry your payment securely using the link below:</p>
                                <p><a href='{retryUrl}' style='display:inline-block;padding:10px 20px;background:#27ae60;color:#fff;text-decoration:none;border-radius:4px;'>Retry Payment Now</a></p>
                                <p>Thank you,<br/>BookMyFun Team</p>";

                            await _emailService.EnqueueEmailAsync(client.Email, subject, body, "PaymentAlert");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing OnPaymentFailed post-failure routine for booking {BookingId}", bookingId);
            }
        }

        private static bool IsApiOrAjaxRequest(HttpRequest request)
        {
            return request.Headers["X-Requested-With"] == "XMLHttpRequest"
                || request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
                || request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
        }

        private static ViewResult CreateErrorViewResult(ErrorViewModel vm, int statusCode)
        {
            var viewData = new ViewDataDictionary<ErrorViewModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = vm,
                ["StatusCode"] = statusCode,
                ["RequestId"] = vm.RequestId,
                ["Title"] = vm.Title
            };

            return new ViewResult
            {
                ViewName = "~/Views/Shared/Error.cshtml",
                ViewData = viewData,
                StatusCode = statusCode
            };
        }
    }
}
