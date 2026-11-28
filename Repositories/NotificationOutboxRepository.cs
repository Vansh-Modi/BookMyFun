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
        private static readonly ConcurrentDictionary<string, byte> _memoryDedupeKeys = new(StringComparer.OrdinalIgnoreCase);
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
            long id = await EnqueueWithIdAsync(item, tx);
            return id > 0;
        }

        public async Task<long> EnqueueWithIdAsync(NotificationOutbox item, SqlTransaction? tx = null)
        {
            string dedupeKey = !string.IsNullOrWhiteSpace(item.DedupeKey)
                ? item.DedupeKey.Trim()
                : $"{item.EventType}:{item.EntityID}:{item.UserID}:{item.Channel}";
            item.DedupeKey = dedupeKey;

            bool isNewDispatch = await _userNotificationRepo.TryRecordDispatchAsync(dedupeKey);
            if (!isNewDispatch)
            {
                _logger.LogInformation("Skipping duplicate notification outbox enqueue: {Key}", dedupeKey);
                return 0;
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
                    IF COL_LENGTH('dbo.NotificationOutbox', 'DedupeKey') IS NOT NULL
                    BEGIN
                        IF EXISTS (SELECT 1 FROM dbo.NotificationOutbox WHERE DedupeKey = @DedupeKey)
                        BEGIN
                            SELECT CAST(0 AS BIGINT);
                        END
                        ELSE
                        BEGIN
                            INSERT INTO dbo.NotificationOutbox
                            (EventType, EntityID, UserID, Channel, Title, Body, LinkUrl, Status, RetryCount, MaxRetries, NextAttemptAt, DedupeKey, CreatedAt)
                            OUTPUT INSERTED.OutboxID
                            VALUES
                            (@EventType, @EntityID, @UserID, @Channel, @Title, @Body, @LinkUrl, @Status, @RetryCount, @MaxRetries, @NextAttemptAt, @DedupeKey, GETUTCDATE());
                        END
                    END
                    ELSE
                    BEGIN
                        INSERT INTO dbo.NotificationOutbox
                        (EventType, EntityID, UserID, Channel, Title, Body, LinkUrl, Status, RetryCount, MaxRetries, NextAttemptAt, CreatedAt)
                        OUTPUT INSERTED.OutboxID
                        VALUES
                        (@EventType, @EntityID, @UserID, @Channel, @Title, @Body, @LinkUrl, @Status, @RetryCount, @MaxRetries, @NextAttemptAt, GETUTCDATE());
                    END";

                var parameters = new[]
                {
                    new SqlParameter("@EventType", item.EventType ?? string.Empty),
                    new SqlParameter("@EntityID", item.EntityID),
                    new SqlParameter("@UserID", item.UserID),
                    new SqlParameter("@Channel", item.Channel ?? "Email"),
                    new SqlParameter("@Title", item.Title ?? string.Empty),
                    new SqlParameter("@Body", item.Body ?? string.Empty),
                    new SqlParameter("@LinkUrl", DBHelper.ToDbValue(item.LinkUrl)),
                    new SqlParameter("@Status", item.Status ?? "Pending"),
                    new SqlParameter("@RetryCount", item.RetryCount),
                    new SqlParameter("@MaxRetries", item.MaxRetries > 0 ? item.MaxRetries : 3),
                    new SqlParameter("@NextAttemptAt", item.NextAttemptAt == default ? DateTime.UtcNow : item.NextAttemptAt),
                    new SqlParameter("@DedupeKey", DBHelper.ToDbValue(dedupeKey))
                };

                object? result;
                if (tx != null)
                {
                    using var cmd = new SqlCommand(sql, tx.Connection, tx);
                    cmd.Parameters.AddRange(parameters);
                    result = await cmd.ExecuteScalarAsync();
                }
                else
                {
                    result = await _db.ExecuteScalarAsync(sql, parameters);
                }

                long insertedId = result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0;
                if (insertedId <= 0)
                {
                    _logger.LogInformation("Skipping duplicate notification outbox enqueue in DB: {Key}", dedupeKey);
                    return 0;
                }

                item.OutboxID = insertedId;
                return insertedId;
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
            {
                _logger.LogInformation("Duplicate DedupeKey prevented at unique index: {Key}", dedupeKey);
                return 0;
            }
            catch (SqlException ex)
            {
                if (!_memoryDedupeKeys.TryAdd(dedupeKey, 0))
                {
                    _logger.LogInformation("Skipping duplicate in-memory notification outbox enqueue: {Key}", dedupeKey);
                    return 0;
                }
                _logger.LogWarning(ex, "NotificationOutbox table not found or query failed; enqueuing to in-memory fallback. Key: {Key}", dedupeKey);
                item.OutboxID = System.Threading.Interlocked.Increment(ref _memorySequence);
                _memoryOutbox[item.OutboxID] = item;
                return item.OutboxID;
            }
            catch (Exception ex)
            {
                if (!_memoryDedupeKeys.TryAdd(dedupeKey, 0))
                {
                    return 0;
                }
                _logger.LogWarning(ex, "Unexpected error enqueuing notification outbox item for UserID #{UserId}", item.UserID);
                item.OutboxID = System.Threading.Interlocked.Increment(ref _memorySequence);
                _memoryOutbox[item.OutboxID] = item;
                return item.OutboxID;
            }
        }

        public async Task<List<NotificationOutbox>> GetPendingBatchAsync(int batchSize = 25)
        {
            try
            {
                string sql = $@"
                    SELECT TOP ({Math.Clamp(batchSize, 1, 100)})
                        OutboxID, EventType, EntityID, UserID, Channel, Title, Body, LinkUrl, Status, RetryCount, MaxRetries, NextAttemptAt, LastError, CreatedAt, SentAt,
                        CASE WHEN COL_LENGTH('dbo.NotificationOutbox', 'DedupeKey') IS NOT NULL THEN DedupeKey ELSE NULL END AS DedupeKey
                    FROM dbo.NotificationOutbox
                    WHERE Status IN ('Pending', 'Retry')
                      AND NextAttemptAt <= GETUTCDATE()
                    ORDER BY OutboxID ASC;";

                var dbResults = await _db.ExecuteReaderListAsync(sql, MapOutboxRow);

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

        public async Task<bool> MarkSentWithAttemptsAsync(long outboxId, int attempts)
        {
            if (_memoryOutbox.TryGetValue(outboxId, out var memItem))
            {
                memItem.Status = "Sent";
                memItem.RetryCount = attempts;
                memItem.SentAt = DateTime.UtcNow;
                memItem.LastError = null;
                return true;
            }

            try
            {
                const string sql = "UPDATE dbo.NotificationOutbox SET Status = 'Sent', RetryCount = @Attempts, SentAt = GETUTCDATE(), LastError = NULL WHERE OutboxID = @ID;";
                int rows = await _db.ExecuteNonQueryAsync(sql,
                    new SqlParameter("@ID", outboxId),
                    new SqlParameter("@Attempts", attempts));
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error marking NotificationOutbox #{Id} as Sent with attempts", outboxId);
                return false;
            }
        }

        public async Task<bool> RecordFailureAsync(long outboxId, string error, int retryCount, DateTime nextAttemptAt, bool isPermanent)
        {
            string status = isPermanent ? "Failed" : "Retry";
            string safeError = BookMyFun.Services.Common.EmailSafetyHelper.Truncate(error, 450);

            if (_memoryOutbox.TryGetValue(outboxId, out var memItem))
            {
                memItem.Status = status;
                memItem.RetryCount = retryCount;
                memItem.NextAttemptAt = nextAttemptAt;
                memItem.LastError = safeError;
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
                    new SqlParameter("@Error", DBHelper.ToDbValue(safeError)));

                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error recording failure for NotificationOutbox #{Id}", outboxId);
                return false;
            }
        }

        public async Task<List<NotificationOutbox>> GetRecentEmailOutboxAsync(int limit = 100)
        {
            try
            {
                string sql = $@"
                    SELECT TOP ({Math.Clamp(limit, 1, 200)})
                        o.OutboxID, o.EventType, o.EntityID, o.UserID, o.Channel, o.Title, o.Body, o.LinkUrl,
                        o.Status, o.RetryCount, o.MaxRetries, o.NextAttemptAt, o.LastError, o.CreatedAt, o.SentAt,
                        CASE WHEN COL_LENGTH('dbo.NotificationOutbox', 'DedupeKey') IS NOT NULL THEN o.DedupeKey ELSE NULL END AS DedupeKey,
                        u.Email AS RecipientEmail
                    FROM dbo.NotificationOutbox o
                    LEFT JOIN dbo.Users u ON o.UserID = u.UserID
                    WHERE o.Channel = 'Email'
                    ORDER BY o.OutboxID DESC;";

                return await _db.ExecuteReaderListAsync(sql, reader =>
                {
                    var item = MapOutboxRow(reader);
                    int recOrd = reader.GetOrdinal("RecipientEmail");
                    item.RecipientEmail = reader.IsDBNull(recOrd) ? null : reader.GetString(recOrd);
                    return item;
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read recent Email outbox rows; returning in-memory items.");
                return _memoryOutbox.Values
                    .Where(x => string.Equals(x.Channel, "Email", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.OutboxID)
                    .Take(limit)
                    .ToList();
            }
        }

        public async Task<NotificationOutbox?> GetByIdAsync(long outboxId)
        {
            if (_memoryOutbox.TryGetValue(outboxId, out var memItem))
                return memItem;

            try
            {
                const string sql = @"
                    SELECT
                        o.OutboxID, o.EventType, o.EntityID, o.UserID, o.Channel, o.Title, o.Body, o.LinkUrl,
                        o.Status, o.RetryCount, o.MaxRetries, o.NextAttemptAt, o.LastError, o.CreatedAt, o.SentAt,
                        CASE WHEN COL_LENGTH('dbo.NotificationOutbox', 'DedupeKey') IS NOT NULL THEN o.DedupeKey ELSE NULL END AS DedupeKey,
                        u.Email AS RecipientEmail
                    FROM dbo.NotificationOutbox o
                    LEFT JOIN dbo.Users u ON o.UserID = u.UserID
                    WHERE o.OutboxID = @ID;";

                return await _db.ExecuteReaderSingleAsync(sql, reader =>
                {
                    var item = MapOutboxRow(reader);
                    int recOrd = reader.GetOrdinal("RecipientEmail");
                    item.RecipientEmail = reader.IsDBNull(recOrd) ? null : reader.GetString(recOrd);
                    return item;
                }, new SqlParameter("@ID", outboxId));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load NotificationOutbox #{Id}.", outboxId);
                return null;
            }
        }

        public async Task<bool> ResetFailedForRetryAsync(long outboxId)
        {
            if (_memoryOutbox.TryGetValue(outboxId, out var memItem))
            {
                if (!string.Equals(memItem.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                    return false;
                memItem.Status = "Pending";
                memItem.RetryCount = 0;
                memItem.NextAttemptAt = DateTime.UtcNow;
                memItem.LastError = null;
                return true;
            }

            try
            {
                const string sql = @"
                    UPDATE dbo.NotificationOutbox
                    SET Status = 'Pending', RetryCount = 0, NextAttemptAt = GETUTCDATE(), LastError = NULL
                    WHERE OutboxID = @ID AND Status = 'Failed';";
                int rows = await _db.ExecuteNonQueryAsync(sql, new SqlParameter("@ID", outboxId));
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reset NotificationOutbox #{Id} for retry.", outboxId);
                return false;
            }
        }

        private static NotificationOutbox MapOutboxRow(SqlDataReader reader)
        {
            int dedupeOrd = reader.GetOrdinal("DedupeKey");
            return new NotificationOutbox
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
                SentAt = reader.IsDBNull(reader.GetOrdinal("SentAt")) ? null : reader.GetDateTime(reader.GetOrdinal("SentAt")),
                DedupeKey = reader.IsDBNull(dedupeOrd) ? null : reader.GetString(dedupeOrd)
            };
        }
    }
}
