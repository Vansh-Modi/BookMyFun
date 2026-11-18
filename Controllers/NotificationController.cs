using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Controllers
{
    [Authorize]
    public class NotificationController : Controller
    {
        private readonly IUserNotificationRepository _notificationRepo;
        private readonly IEmailQueueService _emailQueue;
        private readonly ILogger<NotificationController> _logger;

        public NotificationController(
            IUserNotificationRepository notificationRepo,
            IEmailQueueService emailQueue,
            ILogger<NotificationController> logger)
        {
            _notificationRepo = notificationRepo;
            _emailQueue = emailQueue;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetRecent()
        {
            int userId = GetCurrentUserId();
            var dbNotifs = await _notificationRepo.GetUserNotificationsAsync(userId, 15);
            int unreadCount = await _notificationRepo.GetUnreadCountAsync(userId);

            // Also check memory emailQueue notifications for backwards compatibility
            var memNotifs = _emailQueue.GetInAppNotifications(userId);
            int combinedUnread = Math.Max(unreadCount, memNotifs.Count(n => !n.IsRead));

            var items = new List<object>();

            // Bell notification: shows unread notifications for the current user only
            var unreadDb = dbNotifs?.Where(n => !n.IsRead).Take(15).ToList();
            if (unreadDb != null && unreadDb.Any())
            {
                foreach (var n in unreadDb)
                {
                    items.Add(new
                    {
                        notificationId = n.NotificationID,
                        title = n.Title,
                        message = n.Body,
                        actionUrl = n.LinkUrl,
                        isRead = n.IsRead,
                        createdAt = n.CreatedAt.ToString("dd MMM yyyy, hh:mm tt"),
                        timeAgo = FormatTimeAgo(n.CreatedAt)
                    });
                }
            }
            else
            {
                var unreadMem = memNotifs.Where(n => !n.IsRead).Take(15).ToList();
                foreach (var n in unreadMem)
                {
                    items.Add(new
                    {
                        notificationId = n.NotificationId,
                        title = n.Title,
                        message = n.Message,
                        actionUrl = n.ActionUrl,
                        isRead = n.IsRead,
                        createdAt = n.CreatedAtUtc.ToString("dd MMM yyyy, hh:mm tt"),
                        timeAgo = n.CreatedAtUtc.ToTimeAgo()
                    });
                }
            }

            return Json(new
            {
                success = true,
                unreadCount = combinedUnread,
                items,
                notifications = items
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllAsRead()
        {
            int userId = GetCurrentUserId();
            var dbNotifs = await _notificationRepo.GetUserNotificationsAsync(userId, 50);
            foreach (var n in dbNotifs.Where(x => !x.IsRead))
            {
                await _notificationRepo.MarkAsReadAsync(n.NotificationID, userId);
            }

            var memNotifs = _emailQueue.GetInAppNotifications(userId);
            foreach (var n in memNotifs.Where(x => !x.IsRead))
            {
                _emailQueue.MarkInAppNotificationRead(n.NotificationId);
            }

            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsRead(int notificationId)
        {
            int userId = GetCurrentUserId();
            await _notificationRepo.MarkAsReadAsync(notificationId, userId);
            _emailQueue.MarkInAppNotificationRead(notificationId);
            return Json(new { success = true });
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(claim, out int id) ? id : 0;
        }

        private static string FormatTimeAgo(DateTime dt)
        {
            var span = DateTime.UtcNow - dt;
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return dt.ToString("dd MMM");
        }
    }
}
