using System.Text;
using System.Text.RegularExpressions;

namespace BookMyFun.Services.Security
{
    /// <summary>
    /// Lightweight, robust HTML allow-list sanitizer without external dependencies.
    /// Strips active scripting, dangerous event handlers, non-allowlisted tags, and non-whitelisted attributes.
    /// </summary>
    public class HtmlSanitizerService : IHtmlSanitizerService
    {
        private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6",
            "strong", "b", "em", "i", "u", "s", "strike",
            "ul", "ol", "li", "blockquote", "pre", "code",
            "table", "thead", "tbody", "tfoot", "tr", "th", "td",
            "span", "div", "a"
        };

        private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
        {
            "class", "title", "href", "target", "rel", "align", "valign"
        };

        private static readonly Regex TagRegex = new(@"</?([a-zA-Z0-9]+)(\s+[^>]*)?>", RegexOptions.Compiled);
        private static readonly Regex AttrRegex = new(@"([a-zA-Z0-9\-]+)(?:\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+)))?", RegexOptions.Compiled);

        public string Sanitize(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            // Remove all script and style blocks entirely
            html = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);

            return TagRegex.Replace(html, match =>
            {
                string tagName = match.Groups[1].Value.ToLowerInvariant();
                bool isClosing = match.Value.StartsWith("</");

                if (!AllowedTags.Contains(tagName))
                {
                    // Disallowed tag - strip it completely
                    return string.Empty;
                }

                if (isClosing)
                {
                    return $"</{tagName}>";
                }

                string attrString = match.Groups[2].Value;
                var safeAttrs = new StringBuilder();

                if (!string.IsNullOrWhiteSpace(attrString))
                {
                    var attrMatches = AttrRegex.Matches(attrString);
                    foreach (Match attrMatch in attrMatches)
                    {
                        string attrName = attrMatch.Groups[1].Value.ToLowerInvariant();

                        // Disallow any event handlers (e.g. onclick, onerror, onload)
                        if (attrName.StartsWith("on"))
                            continue;

                        if (!AllowedAttributes.Contains(attrName))
                            continue;

                        string attrVal = attrMatch.Groups[2].Success ? attrMatch.Groups[2].Value
                                       : attrMatch.Groups[3].Success ? attrMatch.Groups[3].Value
                                       : attrMatch.Groups[4].Value;

                        // Sanitize URLs in href
                        if (attrName == "href")
                        {
                            string trimmedVal = attrVal.Trim().ToLowerInvariant();
                            if (trimmedVal.StartsWith("javascript:") || trimmedVal.StartsWith("data:") || trimmedVal.StartsWith("vbscript:"))
                            {
                                continue;
                            }
                        }

                        // Escape quotes inside attribute value
                        string encodedVal = System.Net.WebUtility.HtmlEncode(attrVal);
                        safeAttrs.Append($" {attrName}=\"{encodedVal}\"");
                    }
                }

                // If it's an anchor, enforce rel="noopener noreferrer"
                if (tagName == "a" && !safeAttrs.ToString().Contains("rel="))
                {
                    safeAttrs.Append(" rel=\"noopener noreferrer\"");
                }

                bool isSelfClosing = match.Value.EndsWith("/>") || tagName == "br" || tagName == "hr";
                return isSelfClosing ? $"<{tagName}{safeAttrs} />" : $"<{tagName}{safeAttrs}>";
            });
        }
    }
}
