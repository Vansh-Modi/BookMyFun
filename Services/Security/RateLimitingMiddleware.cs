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
        private static DateTime _lastPruneTime = DateTime.UtcNow;
        private static readonly object _pruneLock = new();

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

            // Razorpay webhooks are cryptographically signed via HMAC SHA-256 (X-Razorpay-Signature)
            // and must never be rate limited or blocked during payment event processing.
            if (path.Contains("/api/razorpaywebhook") || path.Contains("/api/webhooks/razorpay") || path.Contains("/webhook/razorpay"))
            {
                await _next(context);
                return;
            }

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
            // 3. Contact Form Submit: max 5 per minute per IP (Task A9)
            else if (HttpMethods.IsPost(method) && (path.EndsWith("/home/contact") || path == "/contact"))
            {
                limit = 5;
                rateLimitKey = $"ip:{ip}:contact_submit";
            }
            // 4. Payment Callback & Verification routes: max 60 per minute per IP (Task F2)
            else if (path.Contains("/booking/verifypayment") ||
                     path.Contains("/owner/verifysubscriptionpayment") ||
                     path.Contains("/verifypayment") ||
                     path.Contains("/paymentcallback"))
            {
                limit = 60;
                rateLimitKey = $"ip:{ip}:payment_verify";
            }
            // 5. Notification mark-as-read: 120 per minute per user
            else if (path.Contains("/notification/markasread") ||
                     path.Contains("/notification/markallasread") ||
                     path.Contains("/notifications/mark"))
            {
                limit = 120;
                rateLimitKey = $"user:{userId}:notification_mark_read";
            }
            // 6. Staff Queue: 60 per minute per staff user
            else if (path.Contains("/admin/needsattention") ||
                     path.Contains("/employee/needsattention"))
            {
                limit = 60;
                rateLimitKey = $"staff:{userId}:staff_queue";
            }
            // 7. Sensitive Account / Auth / Upload actions: 20 per minute per IP
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
                PruneStaleEntries(now);

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

        private static void PruneStaleEntries(DateTime now)
        {
            if (now - _lastPruneTime < TimeSpan.FromMinutes(5) && RequestTracker.Count <= 10000)
            {
                return;
            }

            if (System.Threading.Monitor.TryEnter(_pruneLock))
            {
                try
                {
                    if (now - _lastPruneTime >= TimeSpan.FromMinutes(5) || RequestTracker.Count > 10000)
                    {
                        foreach (var kvp in RequestTracker)
                        {
                            lock (kvp.Value)
                            {
                                kvp.Value.RemoveAll(t => now - t > Window);
                                if (kvp.Value.Count == 0)
                                {
                                    RequestTracker.TryRemove(kvp.Key, out _);
                                }
                            }
                        }
                        _lastPruneTime = now;
                    }
                }
                finally
                {
                    System.Threading.Monitor.Exit(_pruneLock);
                }
            }
        }
    }
}
