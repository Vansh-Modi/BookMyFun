namespace BookMyFun.Services.Security
{
    /// <summary>
    /// Service for sanitizing rich text HTML content using an allow-list of safe tags and attributes to prevent XSS.
    /// </summary>
    public interface IHtmlSanitizerService
    {
        /// <summary>
        /// Sanitizes the provided HTML string, removing disallowed tags, attributes, and script injection vectors.
        /// </summary>
        /// <param name="html">Raw HTML input.</param>
        /// <returns>Sanitized HTML safe for rendering.</returns>
        string Sanitize(string? html);
    }
}
