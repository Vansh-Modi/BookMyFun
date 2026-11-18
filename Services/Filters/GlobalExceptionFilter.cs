using System.Diagnostics;
using System.Net;
using BookMyFun.Models;
using BookMyFun.Services.Exceptions;
using BookMyFun.Services.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BookMyFun.Services.Filters
{
    /// <summary>
    /// Global exception filter for handling unhandled exceptions across controllers.
    /// Returns standardized JSON payloads for API/AJAX callers and branded error views for browser navigation.
    /// </summary>
    public class GlobalExceptionFilter : IExceptionFilter
    {
        private readonly ILogger<GlobalExceptionFilter> _logger;
        private readonly IHostEnvironment _env;
        private readonly ICustomErrorHandlerService? _errorHandler;

        public GlobalExceptionFilter(
            ILogger<GlobalExceptionFilter> logger,
            IHostEnvironment env,
            ICustomErrorHandlerService? errorHandler = null)
        {
            _logger = logger;
            _env = env;
            _errorHandler = errorHandler;
        }

        public void OnException(ExceptionContext context)
        {
            var exception = context.Exception;
            var request = context.HttpContext.Request;
            string correlationId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

            if (_errorHandler != null && !(exception is AppException))
            {
                context.Result = _errorHandler.OnUnhandledException(context.HttpContext, exception);
                context.ExceptionHandled = true;
                return;
            }

            int statusCode = (int)HttpStatusCode.InternalServerError;
            string errorCode = "INTERNAL_SERVER_ERROR";
            string message = "An unexpected error occurred. Please try again later or contact support.";
            IDictionary<string, string[]>? validationErrors = null;

            if (exception is AppException appEx)
            {
                statusCode = appEx.StatusCode;
                errorCode = appEx.ErrorCode;
                message = appEx.Message;
                validationErrors = appEx.ValidationErrors;

                _logger.LogWarning(exception, "[{CorrelationId}] Handled application exception [{ErrorCode}] ({StatusCode}): {Message}",
                    correlationId, errorCode, statusCode, message);
            }
            else
            {
                _logger.LogError(exception, "[{CorrelationId}] Unhandled system exception on {Method} {Path}",
                    correlationId, request.Method, request.Path);

                if (_env.IsDevelopment())
                {
                    message = exception.Message;
                }
            }

            bool isApiOrAjax = request.Headers["X-Requested-With"] == "XMLHttpRequest"
                || request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
                || request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

            if (isApiOrAjax)
            {
                var errorPayload = new
                {
                    success = false,
                    message = message,
                    correlationId = correlationId,
                    error = new
                    {
                        code = errorCode,
                        message = message,
                        details = validationErrors
                    }
                };

                context.Result = new ObjectResult(errorPayload)
                {
                    StatusCode = statusCode
                };
            }
            else
            {
                if (_env.IsDevelopment())
                {
                    // Allow Developer Exception Page to render in Development mode
                    return;
                }

                var vm = new ErrorViewModel
                {
                    StatusCode = statusCode,
                    RequestId = correlationId,
                    Title = statusCode == 404 ? "Page Not Found (404)" :
                            statusCode == 403 ? "Access Restricted (403)" :
                            "Something Went Wrong (500)",
                    Message = message
                };

                var viewData = new ViewDataDictionary<ErrorViewModel>(new EmptyModelMetadataProvider(), context.ModelState)
                {
                    Model = vm,
                    ["ErrorMessage"] = message,
                    ["ErrorCode"] = errorCode,
                    ["StatusCode"] = statusCode
                };

                context.Result = new ViewResult
                {
                    ViewName = "~/Views/Shared/Error.cshtml",
                    ViewData = viewData,
                    StatusCode = statusCode
                };
            }

            context.ExceptionHandled = true;
        }
    }
}
