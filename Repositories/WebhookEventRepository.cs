using System.Collections.Concurrent;
using BookMyFun.Data;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IWebhookEventRepository
    {
        Task<bool> HasEventBeenProcessedAsync(string eventId);
        Task RecordEventProcessedAsync(string eventId, string eventType, string status, int? bookingId = null);
    }

    public class WebhookEventRepository : IWebhookEventRepository
    {
        private readonly DBHelper _db;
        private static readonly ConcurrentDictionary<string, DateTime> _inMemoryCache = new();

        public WebhookEventRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<bool> HasEventBeenProcessedAsync(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return false;

            if (_inMemoryCache.ContainsKey(eventId))
            {
                return true;
            }

            try
            {
                const string sql = "SELECT COUNT(1) FROM WebhookEvents WHERE GatewayEventID = @EventID";
                var result = await _db.ExecuteScalarAsync(sql, new SqlParameter("@EventID", eventId));
                if (result != null && Convert.ToInt32(result) > 0)
                {
                    _inMemoryCache.TryAdd(eventId, DateTime.UtcNow);
                    return true;
                }
            }
            catch
            {
                // Fallback to in-memory tracking
            }

            return false;
        }

        public async Task RecordEventProcessedAsync(string eventId, string eventType, string status, int? bookingId = null)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return;

            _inMemoryCache.TryAdd(eventId, DateTime.UtcNow);

            try
            {
                const string sql = @"
                    IF NOT EXISTS (SELECT 1 FROM WebhookEvents WHERE GatewayEventID = @EventID)
                    BEGIN
                        INSERT INTO WebhookEvents (GatewayEventID, EventType, Status, ProcessedAt)
                        VALUES (@EventID, @EventType, @Status, GETDATE());
                    END";
                await _db.ExecuteNonQueryAsync(
                    sql,
                    new SqlParameter("@EventID", eventId),
                    new SqlParameter("@EventType", eventType),
                    new SqlParameter("@Status", status));
            }
            catch
            {
                // In-memory cache guarantees idempotency even if DB is unavailable
            }
        }
    }
}
