using System.Collections.Generic;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;

namespace BookMyFun.Repositories
{
    public interface IUserNotificationRepository
    {
        Task<bool> InsertNotificationAsync(UserNotification notification);
        Task<List<UserNotification>> GetUserNotificationsAsync(int userId, int limit = 20);
        Task<int> GetUnreadCountAsync(int userId);
        Task<bool> MarkAsReadAsync(int notificationId, int userId);
        Task<int> MarkExpiryNotificationsReadAsync(int userId) => Task.FromResult(0);
        Task<bool> TryRecordDispatchAsync(string notificationKey);
    }
}
