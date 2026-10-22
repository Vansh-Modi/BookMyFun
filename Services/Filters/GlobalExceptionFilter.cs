using System.Net;
using System.Text.Json;
using BookMyFun.Services.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BookMyFun.Services.Filters
{
    public class GlobalExceptionFilter : IExceptionFilter
    {
        private readonly ILogger<GlobalExceptionFilter> _logger;
        private readonly IHostEnvironment _env;

        public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger, IHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        public void OnException(ExceptionContext context)
        {
            var exception = context.Exception;
            var request = context.HttpContext.Request;

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

                _logger.LogWarning(exception, "Handled application exception [{ErrorCode}] ({StatusCode}): {Message}", errorCode, statusCode, message);
            }
            else
            {
                _logger.LogError(exception, "Unhandled system exception on {Method} {Path}", request.Method, request.Path);
                if (_env.IsDevelopment())
                {
                    message = exception.Message;
                }
            }

            bool isApiOrAjax = request.Headers["X-Requested-With"] == "XMLHttpRequest"
                || (request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
                || request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

            if (isApiOrAjax)
            {
                var errorPayload = new
                {
                    success = false,
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
                    // Allow ASP.NET Core Developer Exception Page to render root exception in Development mode
                    return;
                }

                // For standard Razor MVC pages
                var modelMetadataProvider = context.HttpContext.RequestServices.GetService(typeof(Microsoft.AspNetCore.Mvc.ModelBinding.IModelMetadataProvider)) as Microsoft.AspNetCore.Mvc.ModelBinding.IModelMetadataProvider
                    ?? new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider();

                var viewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(modelMetadataProvider, context.ModelState)
                {
                    Model = new BookMyFun.Models.ErrorViewModel
                    {
                        RequestId = System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier
                    },
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
