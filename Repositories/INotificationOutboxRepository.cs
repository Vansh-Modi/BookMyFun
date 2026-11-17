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
        Task<List<NotificationOutbox>> GetPendingBatchAsync(int batchSize = 25);
        Task<bool> MarkProcessingAsync(long outboxId);
        Task<bool> MarkSentAsync(long outboxId);
        Task<bool> RecordFailureAsync(long outboxId, string error, int retryCount, DateTime nextAttemptAt, bool isPermanent);
    }
}
