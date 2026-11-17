using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Repositories
{
    public class NotificationOutboxRepository : INotificationOutboxRepository
    {
        private readonly DBHelper _db;
        private readonly IUserNotificationRepository _userNotificationRepo;
        private readonly ILogger<NotificationOutboxRepository> _logger;

        // In-memory fallback queue for local development/testing before migration 004 is applied
        private static readonly ConcurrentDictionary<long, NotificationOutbox> _memoryOutbox = new();
        private static long _memorySequence = 1;

        public NotificationOutboxRepository(
            DBHelper db,
            IUserNotificationRepository userNotificationRepo,
            ILogger<NotificationOutboxRepository> logger)
        {
            _db = db;
            _userNotificationRepo = userNotificationRepo;
            _logger = logger;
        }

        public async Task<bool> EnqueueAsync(NotificationOutbox item, SqlTransaction? tx = null)
        {
            string dispatchKey = $"{item.EventType}:{item.EntityID}:{item.UserID}:{item.Channel}";
            bool isNew = await _userNotificationRepo.TryRecordDispatchAsync(dispatchKey);
            if (!isNew)
            {
                _logger.LogInformation("Skipping duplicate notification outbox enqueue: {Key}", dispatchKey);
                return false;
            }

            // For InApp notifications, write directly to UserNotifications for immediate delivery (< 5s requirement)
            if (string.Equals(item.Channel, "InApp", StringComparison.OrdinalIgnoreCase))
            {
                await _userNotificationRepo.InsertNotificationAsync(new UserNotification
                {
                    UserID = item.UserID,
                    Title = item.Title,
                    Body = item.Body,
                    LinkUrl = item.LinkUrl,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            try
            {
                const string sql = @"
                    INSERT INTO dbo.NotificationOutbox
                    (EventType, EntityID, UserID, Channel, Title, Body, LinkUrl, Status, RetryCount, MaxRetries, NextAttemptAt, CreatedAt)
                    VALUES
                    (@EventType, @EntityID, @UserID, @Channel, @Title, @Body, @LinkUrl, @Status, @RetryCount, @MaxRetries, @NextAttemptAt, GETUTCDATE());";

                var parameters = new[]
                {
                    new SqlParameter("@EventType", item.EventType),
                    new SqlParameter("@EntityID", item.EntityID),
                    new SqlParameter("@UserID", item.UserID),
                    new SqlParameter("@Channel", item.Channel),
                    new SqlParameter("@Title", item.Title ?? string.Empty),
                    new SqlParameter("@Body", item.Body ?? string.Empty),
                    new SqlParameter("@LinkUrl", DBHelper.ToDbValue(item.LinkUrl)),
                    new SqlParameter("@Status", item.Status ?? "Pending"),
                    new SqlParameter("@RetryCount", item.RetryCount),
                    new SqlParameter("@MaxRetries", item.MaxRetries),
                    new SqlParameter("@NextAttemptAt", item.NextAttemptAt)
                };

                int rows;
                if (tx != null)
                {
                    using var cmd = new SqlCommand(sql, tx.Connection, tx);
                    cmd.Parameters.AddRange(parameters);
                    rows = await cmd.ExecuteNonQueryAsync();
                }
                else
                {
                    rows = await _db.ExecuteNonQueryAsync(sql, parameters);
                }

                return rows > 0;
            }
            catch (SqlException ex)
            {
                _logger.LogWarning(ex, "NotificationOutbox table not found or query failed; enqueuing to in-memory fallback. Key: {Key}", dispatchKey);
                item.OutboxID = System.Threading.Interlocked.Increment(ref _memorySequence);
                _memoryOutbox[item.OutboxID] = item;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error enqueuing notification outbox item for UserID #{UserId}", item.UserID);
                item.OutboxID = System.Threading.Interlocked.Increment(ref _memorySequence);
                _memoryOutbox[item.OutboxID] = item;
                return true;
            }
        }

        public async Task<List<NotificationOutbox>> GetPendingBatchAsync(int batchSize = 25)
        {
            try
            {
                string sql = $@"
                    SELECT TOP ({Math.Clamp(batchSize, 1, 100)})
                        OutboxID, EventType, EntityID, UserID, Channel, Title, Body, LinkUrl, Status, RetryCount, MaxRetries, NextAttemptAt, LastError, CreatedAt, SentAt
                    FROM dbo.NotificationOutbox
                    WHERE Status IN ('Pending', 'Retry')
                      AND NextAttemptAt <= GETUTCDATE()
                    ORDER BY OutboxID ASC;";

                var dbResults = await _db.ExecuteReaderListAsync(sql, reader => new NotificationOutbox
                {
                    OutboxID = reader.GetInt64(reader.GetOrdinal("OutboxID")),
                    EventType = reader.GetString(reader.GetOrdinal("EventType")),
                    EntityID = reader.GetInt32(reader.GetOrdinal("EntityID")),
                    UserID = reader.GetInt32(reader.GetOrdinal("UserID")),
                    Channel = reader.GetString(reader.GetOrdinal("Channel")),
                    Title = reader.GetString(reader.GetOrdinal("Title")),
                    Body = reader.GetString(reader.GetOrdinal("Body")),
                    LinkUrl = reader.IsDBNull(reader.GetOrdinal("LinkUrl")) ? null : reader.GetString(reader.GetOrdinal("LinkUrl")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    RetryCount = reader.GetInt32(reader.GetOrdinal("RetryCount")),
                    MaxRetries = reader.GetInt32(reader.GetOrdinal("MaxRetries")),
                    NextAttemptAt = reader.GetDateTime(reader.GetOrdinal("NextAttemptAt")),
                    LastError = reader.IsDBNull(reader.GetOrdinal("LastError")) ? null : reader.GetString(reader.GetOrdinal("LastError")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    SentAt = reader.IsDBNull(reader.GetOrdinal("SentAt")) ? null : reader.GetDateTime(reader.GetOrdinal("SentAt"))
                });

                if (dbResults.Count > 0) return dbResults;
            }
            catch (SqlException)
            {
                // Fall back to memory queue if DB table is missing
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query NotificationOutbox batch from DB.");
            }

            // Return items from memory queue
            var now = DateTime.UtcNow;
            return _memoryOutbox.Values
                .Where(x => (x.Status == "Pending" || x.Status == "Retry") && x.NextAttemptAt <= now)
                .Take(batchSize)
                .ToList();
        }

        public async Task<bool> MarkProcessingAsync(long outboxId)
        {
            if (_memoryOutbox.TryGetValue(outboxId, out var memItem))
            {
                memItem.Status = "Processing";
                return true;
            }

            try
            {
                const string sql = "UPDATE dbo.NotificationOutbox SET Status = 'Processing' WHERE OutboxID = @ID;";
                int rows = await _db.ExecuteNonQueryAsync(sql, new SqlParameter("@ID", outboxId));
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error marking NotificationOutbox #{Id} as Processing", outboxId);
                return false;
            }
        }

        public async Task<bool> MarkSentAsync(long outboxId)
        {
            if (_memoryOutbox.TryGetValue(outboxId, out var memItem))
            {
                memItem.Status = "Sent";
                memItem.SentAt = DateTime.UtcNow;
                memItem.LastError = null;
                return true;
            }

            try
            {
                const string sql = "UPDATE dbo.NotificationOutbox SET Status = 'Sent', SentAt = GETUTCDATE(), LastError = NULL WHERE OutboxID = @ID;";
                int rows = await _db.ExecuteNonQueryAsync(sql, new SqlParameter("@ID", outboxId));
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error marking NotificationOutbox #{Id} as Sent", outboxId);
                return false;
            }
        }

        public async Task<bool> RecordFailureAsync(long outboxId, string error, int retryCount, DateTime nextAttemptAt, bool isPermanent)
        {
            string status = isPermanent ? "Failed" : "Retry";
            if (_memoryOutbox.TryGetValue(outboxId, out var memItem))
            {
                memItem.Status = status;
                memItem.RetryCount = retryCount;
                memItem.NextAttemptAt = nextAttemptAt;
                memItem.LastError = error;
                return true;
            }

            try
            {
                const string sql = @"
                    UPDATE dbo.NotificationOutbox
                    SET Status = @Status, RetryCount = @RetryCount, NextAttemptAt = @NextAttemptAt, LastError = @Error
                    WHERE OutboxID = @ID;";

                int rows = await _db.ExecuteNonQueryAsync(sql,
                    new SqlParameter("@ID", outboxId),
                    new SqlParameter("@Status", status),
                    new SqlParameter("@RetryCount", retryCount),
                    new SqlParameter("@NextAttemptAt", nextAttemptAt),
                    new SqlParameter("@Error", DBHelper.ToDbValue(error)));

                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error recording failure for NotificationOutbox #{Id}", outboxId);
                return false;
            }
        }
    }
}
