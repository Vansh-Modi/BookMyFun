using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface ISubscriptionRepository
    {
        Task<List<SubscriptionPlan>> GetAllPlansAsync(bool onlyActive = true);
        Task<SubscriptionPlan?> GetPlanByIdAsync(int planId);
        Task<int> CreatePlanAsync(SubscriptionPlan plan);
        Task<bool> UpdatePlanAsync(SubscriptionPlan plan);
        Task<int> SubscribeOwnerAsync(OwnerSubscription sub);
        Task<OwnerSubscription?> GetActiveSubscriptionByOwnerIdAsync(int ownerId);
        Task<List<OwnerSubscription>> GetSubscriptionHistoryByOwnerIdAsync(int ownerId);
        Task<bool> UpdateSubscriptionPaymentStatusAsync(int subscriptionId, string paymentStatus, string? gatewayTransactionId);
        Task<int> GetActiveListingCountAsync(int ownerId);
        Task<bool> CanOwnerAddPropertyAsync(int ownerId);
    }

    public class SubscriptionRepository : ISubscriptionRepository
    {
        private readonly DBHelper _db;

        public SubscriptionRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<List<SubscriptionPlan>> GetAllPlansAsync(bool onlyActive = true)
        {
            var sql = "SELECT * FROM SubscriptionPlans" + (onlyActive ? " WHERE IsActive = 1" : "") + " ORDER BY Price ASC";
            return await _db.ExecuteReaderListAsync(sql, r => new SubscriptionPlan
            {
                PlanID = r.GetInt32(r.GetOrdinal("PlanID")),
                PlanName = r.GetString(r.GetOrdinal("PlanName")),
                Price = r.GetDecimal(r.GetOrdinal("Price")),
                DurationInDays = r.GetInt32(r.GetOrdinal("DurationInDays")),
                MaxListings = r.GetInt32(r.GetOrdinal("MaxListings")),
                IsActive = r.GetBoolean(r.GetOrdinal("IsActive"))
            });
        }

        public async Task<SubscriptionPlan?> GetPlanByIdAsync(int planId)
        {
            const string sql = "SELECT * FROM SubscriptionPlans WHERE PlanID = @PlanID";
            return await _db.ExecuteReaderSingleAsync(sql, r => new SubscriptionPlan
            {
                PlanID = r.GetInt32(r.GetOrdinal("PlanID")),
                PlanName = r.GetString(r.GetOrdinal("PlanName")),
                Price = r.GetDecimal(r.GetOrdinal("Price")),
                DurationInDays = r.GetInt32(r.GetOrdinal("DurationInDays")),
                MaxListings = r.GetInt32(r.GetOrdinal("MaxListings")),
                IsActive = r.GetBoolean(r.GetOrdinal("IsActive"))
            }, new SqlParameter("@PlanID", planId));
        }

        public async Task<int> CreatePlanAsync(SubscriptionPlan plan)
        {
            const string sql = @"
                INSERT INTO SubscriptionPlans (PlanName, Price, DurationInDays, MaxListings, IsActive)
                OUTPUT INSERTED.PlanID
                VALUES (@PlanName, @Price, @DurationInDays, @MaxListings, @IsActive)";

            var res = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@PlanName", plan.PlanName),
                new SqlParameter("@Price", plan.Price),
                new SqlParameter("@DurationInDays", plan.DurationInDays),
                new SqlParameter("@MaxListings", plan.MaxListings),
                new SqlParameter("@IsActive", plan.IsActive));

            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<bool> UpdatePlanAsync(SubscriptionPlan plan)
        {
            const string sql = @"
                UPDATE SubscriptionPlans 
                SET PlanName = @PlanName, 
                    Price = @Price, 
                    DurationInDays = @DurationInDays, 
                    MaxListings = @MaxListings, 
                    IsActive = @IsActive
                WHERE PlanID = @PlanID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@PlanName", plan.PlanName),
                new SqlParameter("@Price", plan.Price),
                new SqlParameter("@DurationInDays", plan.DurationInDays),
                new SqlParameter("@MaxListings", plan.MaxListings),
                new SqlParameter("@IsActive", plan.IsActive),
                new SqlParameter("@PlanID", plan.PlanID));

            return rows > 0;
        }

        public async Task<int> SubscribeOwnerAsync(OwnerSubscription sub)
        {
            const string sql = @"
                INSERT INTO OwnerSubscriptions (OwnerID, PlanID, StartDate, EndDate, PaymentStatus, GatewayTransactionID)
                OUTPUT INSERTED.SubscriptionID
                VALUES (@OwnerID, @PlanID, @StartDate, @EndDate, @PaymentStatus, @GatewayTransactionID)";

            var res = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@OwnerID", sub.OwnerID),
                new SqlParameter("@PlanID", sub.PlanID),
                new SqlParameter("@StartDate", sub.StartDate),
                new SqlParameter("@EndDate", sub.EndDate),
                new SqlParameter("@PaymentStatus", sub.PaymentStatus),
                new SqlParameter("@GatewayTransactionID", DBHelper.ToDbValue(sub.GatewayTransactionID)));

            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<OwnerSubscription?> GetActiveSubscriptionByOwnerIdAsync(int ownerId)
        {
            const string sql = @"
                SELECT TOP 1 os.*, sp.PlanName, u.FullName AS OwnerName
                FROM OwnerSubscriptions os
                JOIN SubscriptionPlans sp ON os.PlanID = sp.PlanID
                JOIN Users u ON os.OwnerID = u.UserID
                WHERE os.OwnerID = @OwnerID 
                  AND os.PaymentStatus = 'Paid' 
                  AND os.EndDate >= GETDATE()
                ORDER BY os.EndDate DESC";

            return await _db.ExecuteReaderSingleAsync(sql, MapOwnerSub, new SqlParameter("@OwnerID", ownerId));
        }

        public async Task<List<OwnerSubscription>> GetSubscriptionHistoryByOwnerIdAsync(int ownerId)
        {
            const string sql = @"
                SELECT os.*, sp.PlanName, u.FullName AS OwnerName
                FROM OwnerSubscriptions os
                JOIN SubscriptionPlans sp ON os.PlanID = sp.PlanID
                JOIN Users u ON os.OwnerID = u.UserID
                WHERE os.OwnerID = @OwnerID
                ORDER BY os.StartDate DESC";

            return await _db.ExecuteReaderListAsync(sql, MapOwnerSub, new SqlParameter("@OwnerID", ownerId));
        }

        public async Task<bool> UpdateSubscriptionPaymentStatusAsync(int subscriptionId, string paymentStatus, string? gatewayTransactionId)
        {
            const string sql = @"
                UPDATE OwnerSubscriptions 
                SET PaymentStatus = @PaymentStatus, 
                    GatewayTransactionID = @GatewayTransactionID
                WHERE SubscriptionID = @SubscriptionID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@PaymentStatus", paymentStatus),
                new SqlParameter("@GatewayTransactionID", DBHelper.ToDbValue(gatewayTransactionId)),
                new SqlParameter("@SubscriptionID", subscriptionId));

            return rows > 0;
        }

        public async Task<int> GetActiveListingCountAsync(int ownerId)
        {
            const string sql = "SELECT COUNT(*) FROM Properties WHERE OwnerID = @OwnerID AND ListingStatus = 'Active'";
            var res = await _db.ExecuteScalarAsync(sql, new SqlParameter("@OwnerID", ownerId));
            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<bool> CanOwnerAddPropertyAsync(int ownerId)
        {
            var activeListings = await GetActiveListingCountAsync(ownerId);
            var sub = await GetActiveSubscriptionByOwnerIdAsync(ownerId);
            if (sub == null)
            {
                // Allow 1 complimentary/trial listing for approved hosts
                return activeListings < 1;
            }

            var plan = await GetPlanByIdAsync(sub.PlanID);
            if (plan == null) return activeListings < 1;

            return activeListings < plan.MaxListings;
        }

        private static OwnerSubscription MapOwnerSub(SqlDataReader r)
        {
            return new OwnerSubscription
            {
                SubscriptionID = r.GetInt32(r.GetOrdinal("SubscriptionID")),
                OwnerID = r.GetInt32(r.GetOrdinal("OwnerID")),
                PlanID = r.GetInt32(r.GetOrdinal("PlanID")),
                StartDate = r.GetDateTime(r.GetOrdinal("StartDate")),
                EndDate = r.GetDateTime(r.GetOrdinal("EndDate")),
                PaymentStatus = r.GetString(r.GetOrdinal("PaymentStatus")),
                GatewayTransactionID = r.IsDBNull(r.GetOrdinal("GatewayTransactionID")) ? null : r.GetString(r.GetOrdinal("GatewayTransactionID")),
                PlanName = r.GetString(r.GetOrdinal("PlanName")),
                OwnerName = r.GetString(r.GetOrdinal("OwnerName"))
            };
        }
    }
}
