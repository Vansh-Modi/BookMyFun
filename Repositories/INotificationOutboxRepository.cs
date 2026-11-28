using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface INotificationOutboxRepository
    {
        Task<bool> EnqueueAsync(NotificationOutbox item, SqlTransaction? tx = null);
        Task<long> EnqueueWithIdAsync(NotificationOutbox item, SqlTransaction? tx = null)
            => EnqueueAsync(item, tx).ContinueWith(t => t.Result ? (item.OutboxID > 0 ? item.OutboxID : 1L) : 0L);
        Task<List<NotificationOutbox>> GetPendingBatchAsync(int batchSize = 25);
        Task<bool> MarkProcessingAsync(long outboxId);
        Task<bool> MarkSentAsync(long outboxId);
        Task<bool> MarkSentWithAttemptsAsync(long outboxId, int attempts) => MarkSentAsync(outboxId);
        Task<bool> RecordFailureAsync(long outboxId, string error, int retryCount, DateTime nextAttemptAt, bool isPermanent);
        Task<List<NotificationOutbox>> GetRecentEmailOutboxAsync(int limit = 100) => Task.FromResult(new List<NotificationOutbox>());
        Task<NotificationOutbox?> GetByIdAsync(long outboxId) => Task.FromResult<NotificationOutbox?>(null);
        Task<bool> ResetFailedForRetryAsync(long outboxId) => Task.FromResult(false);
    }
}
