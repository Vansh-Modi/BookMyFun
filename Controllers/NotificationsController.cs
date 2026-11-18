using System.Security.Claims;
using System.Threading.Tasks;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly IUserNotificationRepository _notificationRepo;

        public NotificationsController(IUserNotificationRepository notificationRepo)
        {
            _notificationRepo = notificationRepo;
        }

        [HttpGet]
        public async Task<IActionResult> GetRecent(int limit = 10)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var notifications = await _notificationRepo.GetUserNotificationsAsync(userId, limit);
            var unreadNotifications = notifications.Where(n => !n.IsRead).ToList();
            int unreadCount = await _notificationRepo.GetUnreadCountAsync(userId);

            return Json(new
            {
                success = true,
                unreadCount,
                notifications = unreadNotifications
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadCount()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            int count = await _notificationRepo.GetUnreadCountAsync(userId);
            return Json(new { success = true, unreadCount = count });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsRead(int notificationId)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool updated = await _notificationRepo.MarkAsReadAsync(notificationId, userId);
            return Json(new { success = updated });
        }
    }
}
