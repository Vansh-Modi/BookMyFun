using System.Security.Claims;

namespace BookMyFun.Services.Common
{
    /// <summary>
    /// Extension methods for safe, strongly-typed retrieval of identity claims from ClaimsPrincipal.
    /// </summary>
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Retrieves the current user's integer identifier (ClaimTypes.NameIdentifier). Returns 0 if missing or invalid.
        /// </summary>
        public static int GetUserId(this ClaimsPrincipal? principal)
        {
            if (principal == null) return 0;
            var claim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out int id) ? id : 0;
        }

        /// <summary>
        /// Retrieves the current user's role (ClaimTypes.Role), or empty string if not authenticated.
        /// </summary>
        public static string GetUserRole(this ClaimsPrincipal? principal)
        {
            return principal?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        }

        /// <summary>
        /// Retrieves the current user's email address (ClaimTypes.Email), or empty string if missing.
        /// </summary>
        public static string GetUserEmail(this ClaimsPrincipal? principal)
        {
            return principal?.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        }

        /// <summary>
        /// Retrieves the current user's display name, or empty string if missing.
        /// </summary>
        public static string GetUserName(this ClaimsPrincipal? principal)
        {
            return principal?.Identity?.Name ?? string.Empty;
        }

        /// <summary>
        /// Checks whether the current user is in the Owner role.
        /// </summary>
        public static bool IsOwner(this ClaimsPrincipal? principal) =>
            string.Equals(principal?.GetUserRole(), "Owner", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Checks whether the current user is in the Admin role.
        /// </summary>
        public static bool IsAdmin(this ClaimsPrincipal? principal) =>
            string.Equals(principal?.GetUserRole(), "Admin", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Checks whether the current user is in the EventManager role.
        /// </summary>
        public static bool IsEventManager(this ClaimsPrincipal? principal) =>
            string.Equals(principal?.GetUserRole(), "EventManager", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Checks whether the current user is in the Client role.
        /// </summary>
        public static bool IsClient(this ClaimsPrincipal? principal) =>
            string.Equals(principal?.GetUserRole(), "Client", StringComparison.OrdinalIgnoreCase);
    }
}
