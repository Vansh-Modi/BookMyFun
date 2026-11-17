using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace BookMyFun.Services.Security
{
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private static readonly ConcurrentDictionary<string, List<DateTime>> RequestTracker = new();
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        public RateLimitingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            var method = context.Request.Method;

            int limit = 0;
            string? rateLimitKey = null;

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? ip;

            // 1. Checkout Eligibility JSON: 60 requests per minute per IP
            if (path.Contains("/booking/celebrationeligibility") ||
                path.Contains("/booking/checkstaycelebrationeligibility") ||
                (path.StartsWith("/booking/") && path.Contains("eligibility")))
            {
                limit = 60;
                rateLimitKey = $"ip:{ip}:eligibility";
            }
            // 2. Booking Submit: 10 per minute per IP
            else if (HttpMethods.IsPost(method) &&
                     (path.EndsWith("/booking/confirmbooking") || path.EndsWith("/booking/checkout") || path.EndsWith("/booking/submit")))
            {
                limit = 10;
                rateLimitKey = $"ip:{ip}:booking_submit";
            }
            // 3. Notification mark-as-read: 120 per minute per user
            else if (path.Contains("/notification/markasread") ||
                     path.Contains("/notification/markallasread") ||
                     path.Contains("/notifications/mark"))
            {
                limit = 120;
                rateLimitKey = $"user:{userId}:notification_mark_read";
            }
            // 4. Staff Queue: 60 per minute per staff user
            else if (path.Contains("/admin/needsattention") ||
                     path.Contains("/employee/needsattention"))
            {
                limit = 60;
                rateLimitKey = $"staff:{userId}:staff_queue";
            }
            // 5. Sensitive Account / Auth / Upload actions: 20 per minute per IP
            else if (path.Contains("/account/login") ||
                     path.Contains("/account/register") ||
                     path.Contains("/account/sendotp") ||
                     path.Contains("/home/partner") ||
                     path.Contains("/api/media/upload"))
            {
                limit = 20;
                rateLimitKey = $"ip:{ip}:sensitive_auth";
            }

            if (limit > 0 && rateLimitKey != null)
            {
                var now = DateTime.UtcNow;
                var timestamps = RequestTracker.GetOrAdd(rateLimitKey, _ => new List<DateTime>());

                bool isRateLimited = false;
                lock (timestamps)
                {
                    timestamps.RemoveAll(t => now - t > Window);

                    if (timestamps.Count >= limit)
                    {
                        isRateLimited = true;
                    }
                    else
                    {
                        timestamps.Add(now);
                    }
                }

                if (isRateLimited)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests; // 429
                    context.Response.Headers["Retry-After"] = "60";
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"success\":false,\"error\":{\"code\":\"RATE_LIMIT_EXCEEDED\",\"message\":\"Too many requests. Please slow down and try again in 60 seconds.\"}}");
                    return;
                }
            }

            await _next(context);
        }
    }
}
