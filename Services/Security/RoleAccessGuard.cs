using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Services.Security
{
    public static class RoleAccessGuard
    {
        private static readonly string[] BlockedBookingAndPropertyViewRoles =
        {
            "Owner", "Employee", "EventManager", "Admin"
        };

        public static bool IsBookingOrPropertyViewBlocked(ClaimsPrincipal? user)
        {
            if (user?.Identity?.IsAuthenticated != true) return false;
            return BlockedBookingAndPropertyViewRoles.Any(role => user.IsInRole(role));
        }

        public static IActionResult RedirectToRoleDashboard(Controller controller, string? message = null)
        {
            if (!string.IsNullOrWhiteSpace(message) && controller.TempData != null)
            {
                controller.TempData["Error"] = message;
            }
            if (controller.User.IsInRole("Admin")) return controller.RedirectToAction("Index", "Admin");
            if (controller.User.IsInRole("Employee")) return controller.RedirectToAction("Index", "Employee");
            if (controller.User.IsInRole("EventManager")) return controller.RedirectToAction("Index", "EventManager");
            if (controller.User.IsInRole("Owner")) return controller.RedirectToAction("Index", "Owner");
            return controller.Forbid();
        }
    }
}
