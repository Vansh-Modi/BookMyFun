namespace BookMyFun.Services.Security
{
    /// <summary>
    /// Middleware for enforcing HTTP security headers on all responses,
    /// protecting against clickjacking, MIME-sniffing, XSS, and unauthorized external framing.
    /// </summary>
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var headers = context.Response.Headers;

            // Security Protection Headers
            headers["X-Frame-Options"] = "SAMEORIGIN";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["X-XSS-Protection"] = "1; mode=block";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";

            // Content-Security-Policy with whitelisted external origins: Google, Razorpay, Font Awesome, OpenStreetMap, YouTube, Vimeo
            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://maps.googleapis.com https://checkout.razorpay.com https://accounts.google.com; " +
                "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://fonts.googleapis.com; " +
                "font-src 'self' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://fonts.gstatic.com; " +
                "img-src 'self' data: https: blob: https://badges.razorpay.com https://*.tile.openstreetmap.org https://tile.openstreetmap.org; " +
                "media-src 'self' https: blob:; " +
                "connect-src 'self' https://api.razorpay.com https://checkout.razorpay.com https://lumberjack.razorpay.com https://maps.googleapis.com https://accounts.google.com https://oauth2.googleapis.com; " +
                "frame-src 'self' https://api.razorpay.com https://checkout.razorpay.com https://accounts.google.com https://www.youtube.com https://player.vimeo.com;";

            await _next(context);
        }
    }
}
