using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BookMyFun.Services
{
    public sealed record RenderedEmail(string Subject, string HtmlBody, string PlainTextBody);

    public static class EmailTemplateRenderer
    {
        public static RenderedEmail Render(
            string subject,
            string headline,
            string bodyContentHtml,
            string? ctaText = null,
            string? ctaUrl = null,
            string? plainTextContent = null)
        {
            string cleanSubject = RemoveAmpersands(subject);
            string encodedSubject = WebUtility.HtmlEncode(cleanSubject);
            string encodedHeadline = WebUtility.HtmlEncode(RemoveAmpersands(headline));

            string buttonHtml = string.Empty;
            string buttonPlain = string.Empty;
            if (!string.IsNullOrWhiteSpace(ctaText) && !string.IsNullOrWhiteSpace(ctaUrl))
            {
                string cleanCta = RemoveAmpersands(ctaText);
                string encodedCta = WebUtility.HtmlEncode(cleanCta);
                string encodedUrl = WebUtility.HtmlEncode(ctaUrl.Trim());
                buttonHtml = $@"
                            <div style='margin: 24px 0 8px;'>
                                <a href='{encodedUrl}' style='display: inline-block; background-color: #0B4F6C; color: #FFFFFF; text-decoration: none; padding: 12px 24px; border-radius: 8px; font-weight: 700; font-size: 14px;'>{encodedCta}</a>
                            </div>";
                buttonPlain = $"{Environment.NewLine}{cleanCta}: {ctaUrl.Trim()}{Environment.NewLine}";
            }

            string html = $@"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{encodedSubject}</title>
</head>
<body style='margin: 0; padding: 0; background-color: #F8FAFC; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; color: #0F172A;'>
    <table role='presentation' width='100%' cellspacing='0' cellpadding='0' border='0' style='background-color: #F8FAFC; padding: 28px 16px;'>
        <tr>
            <td align='center'>
                <table role='presentation' width='100%' cellspacing='0' cellpadding='0' border='0' style='max-width: 580px; background-color: #FFFFFF; border: 1px solid #E2E8F0; border-radius: 14px; overflow: hidden;'>
                    <tr>
                        <td style='background: linear-gradient(135deg, #0B4F6C 0%, #01BAEF 100%); padding: 24px 28px; text-align: left;'>
                            <div style='color: #FFFFFF; font-size: 24px; font-weight: 800; letter-spacing: -0.02em;'>Nayesta</div>
                            <div style='color: #E0F2FE; font-size: 12px; font-weight: 500; margin-top: 4px;'>Luxury Farmhouse and Resort Stays</div>
                        </td>
                    </tr>
                    <tr>
                        <td style='padding: 28px;'>
                            <h2 style='margin: 0 0 14px; font-size: 20px; font-weight: 700; color: #0F172A;'>{encodedHeadline}</h2>
                            <div style='font-size: 15px; line-height: 1.65; color: #334155;'>
                                {bodyContentHtml}
                            </div>
                            {buttonHtml}
                        </td>
                    </tr>
                    <tr>
                        <td style='background-color: #F8FAFC; border-top: 1px solid #E2E8F0; padding: 18px 28px; text-align: left;'>
                            <p style='margin: 0; font-size: 12px; line-height: 1.5; color: #64748B;'>
                                Sent by Nayesta. Please do not reply with passwords or sensitive personal details.
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";

            string fallbackText = !string.IsNullOrWhiteSpace(plainTextContent)
                ? plainTextContent.Trim()
                : StripHtmlToPlainText(bodyContentHtml);

            var plainBuilder = new StringBuilder();
            plainBuilder.AppendLine("Nayesta");
            plainBuilder.AppendLine(RemoveAmpersands(headline));
            plainBuilder.AppendLine();
            plainBuilder.AppendLine(fallbackText);
            if (!string.IsNullOrEmpty(buttonPlain))
            {
                plainBuilder.Append(buttonPlain);
            }
            plainBuilder.AppendLine();
            plainBuilder.AppendLine("Sent by Nayesta.");

            return new RenderedEmail(cleanSubject, html, plainBuilder.ToString().Trim());
        }

        private static string RemoveAmpersands(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return text.Replace(" & ", " and ", StringComparison.Ordinal)
                       .Replace("&", " and ", StringComparison.Ordinal)
                       .Replace("  ", " ", StringComparison.Ordinal)
                       .Trim();
        }

        private static string StripHtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            string withBreaks = Regex.Replace(html, @"<(br|p|div|li|tr)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            string stripped = Regex.Replace(withBreaks, "<[^>]+>", " ");
            string decoded = WebUtility.HtmlDecode(stripped);
            return Regex.Replace(decoded, @"[ \t]+", " ").Replace("\n ", "\n").Trim();
        }
    }
}
