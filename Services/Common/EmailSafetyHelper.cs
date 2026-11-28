using System.Net.Mail;

namespace BookMyFun.Services.Common
{
    public static class EmailSafetyHelper
    {
        public static string MaskEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return "***";
            string trimmed = email.Trim();
            int atIndex = trimmed.IndexOf('@');
            if (atIndex <= 0) return "***";

            string local = trimmed[..atIndex];
            string domain = trimmed[(atIndex + 1)..];
            string visiblePrefix = local.Length switch
            {
                1 => local,
                2 => local[..1],
                _ => local[..2]
            };

            return $"{visiblePrefix}***@{domain}";
        }

        public static string SanitizeShortError(Exception? ex, string? fallback = null, string? secretToRedact = null)
        {
            string raw;
            if (ex == null)
            {
                raw = string.IsNullOrWhiteSpace(fallback) ? "Email dispatch failed." : fallback.Trim();
            }
            else if (ex is SmtpException smtpEx)
            {
                string msg = ExtractFirstLine(smtpEx.InnerException?.Message ?? smtpEx.Message);
                raw = $"SmtpException ({smtpEx.StatusCode}): {msg}";
            }
            else
            {
                string msg = ExtractFirstLine(ex.InnerException?.Message ?? ex.Message);
                raw = $"{ex.GetType().Name}: {msg}";
            }

            if (!string.IsNullOrEmpty(secretToRedact))
            {
                raw = raw.Replace(secretToRedact, "***", StringComparison.Ordinal);
            }

            return Truncate(raw, 400);
        }

        public static string Truncate(string? value, int maxLength = 400)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string clean = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return clean.Length <= maxLength ? clean : clean[..maxLength];
        }

        private static string ExtractFirstLine(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Send failed.";
            var line = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return string.IsNullOrWhiteSpace(line) ? "Send failed." : line;
        }
    }
}
