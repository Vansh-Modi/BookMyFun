using System;
using System.Collections.Generic;

namespace BookMyFun.Services.Exceptions
{
    public abstract class AppException : Exception
    {
        public int StatusCode { get; }
        public string ErrorCode { get; }
        public IDictionary<string, string[]>? ValidationErrors { get; }

        protected AppException(string message, int statusCode = 500, string errorCode = "INTERNAL_ERROR", IDictionary<string, string[]>? validationErrors = null)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
            ValidationErrors = validationErrors;
        }
    }

    public class ValidationException : AppException
    {
        public ValidationException(string message, IDictionary<string, string[]>? validationErrors = null)
            : base(message, 400, "VALIDATION_ERROR", validationErrors)
        {
        }

        public ValidationException(string field, string error)
            : base("Validation failed.", 400, "VALIDATION_ERROR", new Dictionary<string, string[]> { { field, new[] { error } } })
        {
        }
    }

    public class NotFoundException : AppException
    {
        public NotFoundException(string resourceName, object key)
            : base($"{resourceName} with identifier '{key}' was not found.", 404, "NOT_FOUND")
        {
        }

        public NotFoundException(string message)
            : base(message, 404, "NOT_FOUND")
        {
        }
    }

    public class ForbiddenException : AppException
    {
        public ForbiddenException(string message = "You do not have permission to access or modify this resource.")
            : base(message, 403, "FORBIDDEN")
        {
        }
    }

    public class UnauthorizedException : AppException
    {
        public UnauthorizedException(string message = "Authentication is required to perform this action.")
            : base(message, 401, "UNAUTHORIZED")
        {
        }
    }

    public class ConflictException : AppException
    {
        public ConflictException(string message)
            : base(message, 409, "CONFLICT")
        {
        }
    }

    public class PaymentGatewayException : AppException
    {
        public PaymentGatewayException(string message, Exception? innerException = null)
            : base(message, 502, "PAYMENT_GATEWAY_ERROR")
        {
        }
    }

    public class SecurityViolationException : AppException
    {
        public SecurityViolationException(string message)
            : base(message, 400, "SECURITY_VIOLATION")
        {
        }
    }
}
