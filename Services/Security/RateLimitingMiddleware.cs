using System.Collections.Concurrent;
using System.Net;

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

            // Apply strict rate limiting to sensitive endpoints
            if (path.Contains("/account/login") ||
                path.Contains("/account/register") ||
                path.Contains("/account/sendotp") ||
                path.Contains("/home/partner") ||
                path.Contains("/api/media/upload"))
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var key = $"{ip}:{path}";

                var now = DateTime.UtcNow;
                var timestamps = RequestTracker.GetOrAdd(key, _ => new List<DateTime>());

                lock (timestamps)
                {
                    timestamps.RemoveAll(t => now - t > Window);

                    // Threshold: 20 sensitive requests per minute
                    if (timestamps.Count >= 20)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                        context.Response.ContentType = "application/json";
                        context.Response.WriteAsync("{\"success\":false,\"error\":{\"code\":\"RATE_LIMIT_EXCEEDED\",\"message\":\"Too many requests. Please slow down and try again in a minute.\"}}");
                        return;
                    }

                    timestamps.Add(now);
                }
            }

            await _next(context);
        }
    }
}
