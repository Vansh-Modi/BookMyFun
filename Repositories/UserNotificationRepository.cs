using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Repositories
{
    public class UserNotificationRepository : IUserNotificationRepository
    {
        private readonly DBHelper _db;
        private readonly ILogger<UserNotificationRepository> _logger;

        public UserNotificationRepository(DBHelper db, ILogger<UserNotificationRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<bool> InsertNotificationAsync(UserNotification notification)
        {
            try
            {
                const string sql = @"
                    INSERT INTO dbo.UserNotifications (UserID, Title, Body, LinkUrl, IsRead, CreatedAt)
                    VALUES (@UserID, @Title, @Body, @LinkUrl, @IsRead, GETDATE());";

                int rows = await _db.ExecuteNonQueryAsync(sql,
                    new SqlParameter("@UserID", notification.UserID),
                    new SqlParameter("@Title", notification.Title ?? string.Empty),
                    new SqlParameter("@Body", notification.Body ?? string.Empty),
                    new SqlParameter("@LinkUrl", DBHelper.ToDbValue(notification.LinkUrl)),
                    new SqlParameter("@IsRead", notification.IsRead));

                return rows > 0;
            }
            catch (SqlException ex)
            {
                _logger.LogWarning(ex, "Failed to insert UserNotification for UserID #{UserId}. Table may not exist yet in production schema.", notification.UserID);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error inserting UserNotification for UserID #{UserId}.", notification.UserID);
                return false;
            }
        }

        public async Task<List<UserNotification>> GetUserNotificationsAsync(int userId, int limit = 20)
        {
            try
            {
                string sql = $@"
                    SELECT TOP ({Math.Clamp(limit, 1, 100)}) NotificationID, UserID, Title, Body, LinkUrl, IsRead, CreatedAt
                    FROM dbo.UserNotifications
                    WHERE UserID = @UserID
                    ORDER BY CreatedAt DESC;";

                return await _db.ExecuteReaderListAsync(sql, reader => new UserNotification
                {
                    NotificationID = reader.GetInt32(reader.GetOrdinal("NotificationID")),
                    UserID = reader.GetInt32(reader.GetOrdinal("UserID")),
                    Title = reader.GetString(reader.GetOrdinal("Title")),
                    Body = reader.GetString(reader.GetOrdinal("Body")),
                    LinkUrl = reader.IsDBNull(reader.GetOrdinal("LinkUrl")) ? null : reader.GetString(reader.GetOrdinal("LinkUrl")),
                    IsRead = reader.GetBoolean(reader.GetOrdinal("IsRead")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                }, new SqlParameter("@UserID", userId));
            }
            catch (SqlException ex)
            {
                _logger.LogWarning(ex, "Failed to query UserNotifications for UserID #{UserId}. Table may not exist yet.", userId);
                return new List<UserNotification>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error querying UserNotifications for UserID #{UserId}.", userId);
                return new List<UserNotification>();
            }
        }

        public async Task<int> GetUnreadCountAsync(int userId)
        {
            try
            {
                const string sql = "SELECT COUNT(*) FROM dbo.UserNotifications WHERE UserID = @UserID AND IsRead = 0;";
                var result = await _db.ExecuteScalarAsync(sql, new SqlParameter("@UserID", userId));
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            catch (SqlException ex)
            {
                _logger.LogWarning(ex, "Failed to query unread notifications count for UserID #{UserId}.", userId);
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error querying unread count for UserID #{UserId}.", userId);
                return 0;
            }
        }

        public async Task<bool> MarkAsReadAsync(int notificationId, int userId)
        {
            try
            {
                const string sql = "UPDATE dbo.UserNotifications SET IsRead = 1 WHERE NotificationID = @NotificationID AND UserID = @UserID;";
                int rows = await _db.ExecuteNonQueryAsync(sql,
                    new SqlParameter("@NotificationID", notificationId),
                    new SqlParameter("@UserID", userId));
                return rows > 0;
            }
            catch (SqlException ex)
            {
                _logger.LogWarning(ex, "Failed to mark notification #{NotificationId} as read.", notificationId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error marking notification #{NotificationId} as read.", notificationId);
                return false;
            }
        }

        public async Task<bool> TryRecordDispatchAsync(string notificationKey)
        {
            try
            {
                const string sql = @"
                    IF NOT EXISTS (SELECT 1 FROM NotificationDispatches WHERE NotificationKey = @Key)
                    BEGIN
                        INSERT INTO NotificationDispatches (NotificationKey, DispatchedAt) VALUES (@Key, GETUTCDATE());
                        SELECT 1;
                    END
                    ELSE
                    BEGIN
                        SELECT 0;
                    END";

                var result = await _db.ExecuteScalarAsync(sql, new SqlParameter("@Key", notificationKey));
                return result != null && Convert.ToInt32(result) == 1;
            }
            catch (SqlException ex)
            {
                _logger.LogWarning(ex, "NotificationDispatches check failed for key '{Key}'. Table may be missing in production; permitting dispatch.", notificationKey);
                return true; // permit dispatch so notifications don't get stuck if table missing
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error in TryRecordDispatchAsync for key '{Key}'.", notificationKey);
                return true;
            }
        }
    }
}
