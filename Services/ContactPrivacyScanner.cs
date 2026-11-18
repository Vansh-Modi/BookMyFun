using System.Text.RegularExpressions;
using BookMyFun.Data;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Services
{
    public interface IContactPrivacyScanner
    {
        (string SanitizedContent, bool HasViolations, List<string> DetectedPatterns) ScanAndMask(string? input);
        Task<string> ScanMaskAndLogAsync(int userId, string contentType, string? input);
    }

    public class ContactPrivacyScanner : IContactPrivacyScanner
    {
        private readonly DBHelper _db;
        private readonly ILogger<ContactPrivacyScanner> _logger;

        // Compiled regex patterns for contact detection
        // 1. Phone numbers: 10-digit Indian numbers, optional country code (+91, 0091, 91), spaced or hyphenated
        private static readonly Regex PhoneRegex = new(
            @"(?:(?:\+|00)91[\s.-]?)?(?:[6789]\d{9}|[6789]\d{4}[\s.-]?\d{5}|[6789]\d{2}[\s.-]?\d{3}[\s.-]?\d{4}|\b\d{5}[\s.-]?\d{5}\b)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 2. Email addresses
        private static readonly Regex EmailRegex = new(
            @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 3. Web URLs, links, social handles
        private static readonly Regex UrlRegex = new(
            @"\b(?:https?:\/\/|www\.)[^\s/$.?#].[^\s]*|\b(?:wa\.me|instagram\.com|facebook\.com|linkedin\.com|t\.me|bit\.ly|youtu\.be)\/[^\s]*|\b[a-zA-Z0-9-]+\.(?:com|in|co\.in|org|net|io|me|biz)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 4. Solicitation phrases (call me, whatsapp me, dm me, reach me on, etc.)
        private static readonly Regex SolicitationRegex = new(
            @"\b(?:call\s+me|call\s+us|reach\s+(?:me|us)|contact\s+(?:me|us)|whatsapp(?:\s+(?:me|us))?|dm\s+(?:me|us)|ping\s+(?:me|us)|message\s+on\s+whatsapp|text\s+(?:me|us)|send\s+whatsapp|dial\s+(?:my|our)|phone\s+(?:me|us)|connect\s+on\s+wa|telegram|instagram|insta)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private const string MaskReplacement = "[Contact Details Masked]";

        public ContactPrivacyScanner(DBHelper db, ILogger<ContactPrivacyScanner> logger)
        {
            _db = db;
            _logger = logger;
        }

        public (string SanitizedContent, bool HasViolations, List<string> DetectedPatterns) ScanAndMask(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return (input ?? string.Empty, false, new List<string>());
            }

            var detected = new List<string>();
            string result = input;

            // Check Emails
            var emailMatches = EmailRegex.Matches(result);
            foreach (Match m in emailMatches)
            {
                detected.Add($"Email: {m.Value}");
            }
            if (emailMatches.Count > 0)
            {
                result = EmailRegex.Replace(result, MaskReplacement);
            }

            // Check Phone Numbers
            var phoneMatches = PhoneRegex.Matches(result);
            foreach (Match m in phoneMatches)
            {
                // Filter out small pure numbers (e.g. years like 2024 or prices) that are not phone numbers
                if (m.Value.Length >= 10 || m.Value.Contains("+91") || m.Value.Contains("-"))
                {
                    detected.Add($"Phone: {m.Value}");
                }
            }
            if (detected.Any(d => d.StartsWith("Phone:")))
            {
                result = PhoneRegex.Replace(result, MaskReplacement);
            }

            // Check URLs
            var urlMatches = UrlRegex.Matches(result);
            foreach (Match m in urlMatches)
            {
                detected.Add($"URL: {m.Value}");
            }
            if (urlMatches.Count > 0)
            {
                result = UrlRegex.Replace(result, MaskReplacement);
            }

            // Check Solicitation Phrases
            var solMatches = SolicitationRegex.Matches(result);
            foreach (Match m in solMatches)
            {
                detected.Add($"Solicitation: {m.Value}");
            }
            if (solMatches.Count > 0)
            {
                result = SolicitationRegex.Replace(result, MaskReplacement);
            }

            return (result, detected.Count > 0, detected);
        }

        public async Task<string> ScanMaskAndLogAsync(int userId, string contentType, string? input)
        {
            var (sanitized, hasViolations, detected) = ScanAndMask(input);

            if (hasViolations && userId > 0)
            {
                _logger.LogWarning("Contact detail violation detected for User #{UserId} in {ContentType}: {Detected}",
                    userId, contentType, string.Join(", ", detected));

                try
                {
                    const string sql = @"
                        INSERT INTO FlaggedContactAttempts (UserID, ContentType, RawContent, FlaggedPatterns, Status, CreatedAt)
                        VALUES (@UserID, @ContentType, @RawContent, @FlaggedPatterns, 'PendingReview', GETUTCDATE());";

                    // Phase 8c: Log user ID and content type; store sanitized preview rather than cleartext sensitive data
                    string safeContentSnippet = sanitized.Length > 200 ? sanitized.Substring(0, 200) + "..." : sanitized;

                    await _db.ExecuteNonQueryAsync(sql,
                        new SqlParameter("@UserID", userId),
                        new SqlParameter("@ContentType", contentType),
                        new SqlParameter("@RawContent", safeContentSnippet),
                        new SqlParameter("@FlaggedPatterns", string.Join("; ", detected)));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to log flagged contact attempt for User #{UserId}", userId);
                }
            }

            return sanitized;
        }
    }
}
