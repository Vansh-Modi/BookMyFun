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
        Task<OwnerSubscription?> GetScheduledSubscriptionByOwnerIdAsync(int ownerId) => Task.FromResult<OwnerSubscription?>(null);
        Task<List<OwnerSubscription>> GetSubscriptionHistoryByOwnerIdAsync(int ownerId);
        Task<OwnerSubscription?> GetSubscriptionByIdAsync(int subscriptionId);
        Task<bool> UpdateSubscriptionPaymentStatusAsync(int subscriptionId, string paymentStatus, string? gatewayTransactionId);
        Task<bool> CancelScheduledSubscriptionAsync(int subscriptionId, int ownerId) => Task.FromResult(true);
        Task<int> GetActiveListingCountAsync(int ownerId);
        Task<int> GetCountedListingCountAsync(int ownerId) => Task.FromResult(0);
        Task<int> GetLiveListingCountAsync(int ownerId) => Task.FromResult(0);
        Task<bool> HasAnySubscriptionHistoryAsync(int ownerId) => Task.FromResult(false);
        Task<(bool Allowed, string? Reason, int Count, int Limit)> CheckListingCreationAllowanceAsync(int ownerId) => Task.FromResult((true, (string?)null, 0, 1));
        Task<OwnerSubscription?> CreateTrialSubscriptionAsync(int ownerId, int trialDurationDays = 60) => Task.FromResult<OwnerSubscription?>(null);
        Task<int> GetTrialDurationDaysAsync() => Task.FromResult(60);
        Task<SubscriptionPlan?> GetPlanByNameAsync(string planName) => Task.FromResult<SubscriptionPlan?>(null);
        Task<List<OwnerSubscription>> GetExpiredSubscriptionsForHourlyProcessAsync() => Task.FromResult(new List<OwnerSubscription>());
        Task<List<OwnerSubscription>> GetExpiringSubscriptionsAsync(int daysBeforeExpiry) => Task.FromResult(new List<OwnerSubscription>());
        Task<List<OwnerSubscription>> GetNewlyActiveScheduledSubscriptionsAsync() => Task.FromResult(new List<OwnerSubscription>());
        Task<List<string>> RepublishExpiredPropertiesAsync(int ownerId, int maxListings) => Task.FromResult(new List<string>());
        Task<bool> CanOwnerAddPropertyAsync(int ownerId);
        Task<decimal> GetEffectivePlanPriceAsync(int planId, DateTime? asOfDate = null) => Task.FromResult(0m);
        Task<(decimal Price, DateTime EffectiveFrom)?> GetUpcomingPlanPriceAsync(int planId, DateTime? asOfDate = null) => Task.FromResult<(decimal Price, DateTime EffectiveFrom)?>(null);
        Task<int> CreatePlanPriceAsync(SubscriptionPlanPrice price) => Task.FromResult(1);
        Task<List<SubscriptionPlanPrice>> GetPlanPricesByPlanIdAsync(int planId) => Task.FromResult(new List<SubscriptionPlanPrice>());
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
            var plans = await _db.ExecuteReaderListAsync(sql, r => new SubscriptionPlan
            {
                PlanID = r.GetInt32(r.GetOrdinal("PlanID")),
                PlanName = r.GetString(r.GetOrdinal("PlanName")),
                Price = r.GetDecimal(r.GetOrdinal("Price")),
                DurationInDays = r.GetInt32(r.GetOrdinal("DurationInDays")),
                MaxListings = r.GetInt32(r.GetOrdinal("MaxListings")),
                IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
                CurrentPrice = r.GetDecimal(r.GetOrdinal("Price"))
            });

            // Populate effective and upcoming prices (Section 3b)
            var now = DateTime.UtcNow;
            foreach (var p in plans)
            {
                p.CurrentPrice = await GetEffectivePlanPriceAsync(p.PlanID, now);
                var upcoming = await GetUpcomingPlanPriceAsync(p.PlanID, now);
                if (upcoming.HasValue)
                {
                    p.UpcomingPrice = upcoming.Value.Price;
                    p.UpcomingPriceDate = upcoming.Value.EffectiveFrom;
                }
            }

            return plans;
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
                INSERT INTO OwnerSubscriptions (OwnerID, PlanID, StartDate, EndDate, PaymentStatus, GatewayTransactionID, PricePaid)
                OUTPUT INSERTED.SubscriptionID
                VALUES (@OwnerID, @PlanID, @StartDate, @EndDate, @PaymentStatus, @GatewayTransactionID, @PricePaid)";

            var res = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@OwnerID", sub.OwnerID),
                new SqlParameter("@PlanID", sub.PlanID),
                new SqlParameter("@StartDate", sub.StartDate),
                new SqlParameter("@EndDate", sub.EndDate),
                new SqlParameter("@PaymentStatus", sub.PaymentStatus),
                new SqlParameter("@GatewayTransactionID", DBHelper.ToDbValue(sub.GatewayTransactionID)),
                new SqlParameter("@PricePaid", (object?)sub.PricePaid ?? DBNull.Value));

            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<OwnerSubscription?> GetActiveSubscriptionByOwnerIdAsync(int ownerId)
        {
            // PaymentStatus IN ('Paid', 'Trial'), StartDate <= GETUTCDATE() so scheduled plans do NOT grant limits early
            const string sql = @"
                SELECT TOP 1 os.*, sp.PlanName, sp.MaxListings, u.FullName AS OwnerName
                FROM OwnerSubscriptions os
                JOIN SubscriptionPlans sp ON os.PlanID = sp.PlanID
                JOIN Users u ON os.OwnerID = u.UserID
                WHERE os.OwnerID = @OwnerID 
                  AND os.PaymentStatus IN ('Paid', 'Trial') 
                  AND (os.StartDate <= SYSDATETIME() OR os.StartDate <= GETUTCDATE())
                  AND (os.EndDate >= GETDATE() OR os.EndDate >= GETUTCDATE())
                ORDER BY os.EndDate DESC";

            return await _db.ExecuteReaderSingleAsync(sql, MapOwnerSub, new SqlParameter("@OwnerID", ownerId));
        }

        public async Task<OwnerSubscription?> GetScheduledSubscriptionByOwnerIdAsync(int ownerId)
        {
            const string sql = @"
                SELECT TOP 1 os.*, sp.PlanName, sp.MaxListings, u.FullName AS OwnerName
                FROM OwnerSubscriptions os
                JOIN SubscriptionPlans sp ON os.PlanID = sp.PlanID
                JOIN Users u ON os.OwnerID = u.UserID
                WHERE os.OwnerID = @OwnerID 
                  AND os.PaymentStatus IN ('Paid', 'Scheduled')
                  AND os.StartDate > SYSDATETIME()
                  AND os.StartDate > GETUTCDATE()
                ORDER BY os.StartDate ASC";

            return await _db.ExecuteReaderSingleAsync(sql, MapOwnerSub, new SqlParameter("@OwnerID", ownerId));
        }

        public async Task<bool> CancelScheduledSubscriptionAsync(int subscriptionId, int ownerId)
        {
            const string sql = @"
                UPDATE OwnerSubscriptions 
                SET PaymentStatus = 'Cancelled' 
                WHERE SubscriptionID = @SubscriptionID 
                  AND OwnerID = @OwnerID 
                  AND StartDate > GETDATE()";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@SubscriptionID", subscriptionId),
                new SqlParameter("@OwnerID", ownerId));

            return rows > 0;
        }

        public async Task<decimal> GetEffectivePlanPriceAsync(int planId, DateTime? asOfDate = null)
        {
            var date = asOfDate ?? DateTime.UtcNow;
            const string sql = @"
                SELECT TOP 1 Price
                FROM SubscriptionPlanPrices
                WHERE PlanID = @PlanID 
                  AND EffectiveFrom <= @Date 
                  AND (EffectiveTo IS NULL OR EffectiveTo > @Date)
                ORDER BY EffectiveFrom DESC";

            var priceObj = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@PlanID", planId),
                new SqlParameter("@Date", date));

            if (priceObj != null && decimal.TryParse(priceObj.ToString(), out decimal dynPrice))
            {
                return dynPrice;
            }

            var plan = await GetPlanByIdAsync(planId);
            return plan?.Price ?? 0m;
        }

        public async Task<(decimal Price, DateTime EffectiveFrom)?> GetUpcomingPlanPriceAsync(int planId, DateTime? asOfDate = null)
        {
            var date = asOfDate ?? DateTime.UtcNow;
            const string sql = @"
                SELECT TOP 1 Price, EffectiveFrom
                FROM SubscriptionPlanPrices
                WHERE PlanID = @PlanID 
                  AND EffectiveFrom > @Date
                ORDER BY EffectiveFrom ASC";

            var dt = await _db.ExecuteDataTableAsync(sql,
                new SqlParameter("@PlanID", planId),
                new SqlParameter("@Date", date));

            if (dt.Rows.Count > 0)
            {
                var row = dt.Rows[0];
                return (Convert.ToDecimal(row["Price"]), Convert.ToDateTime(row["EffectiveFrom"]));
            }

            return null;
        }

        public async Task<int> CreatePlanPriceAsync(SubscriptionPlanPrice price)
        {
            const string sql = @"
                INSERT INTO SubscriptionPlanPrices (PlanID, Price, EffectiveFrom, EffectiveTo, CreatedAt)
                OUTPUT INSERTED.PriceID
                VALUES (@PlanID, @Price, @EffectiveFrom, @EffectiveTo, GETUTCDATE())";

            var res = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@PlanID", price.PlanID),
                new SqlParameter("@Price", price.Price),
                new SqlParameter("@EffectiveFrom", price.EffectiveFrom),
                new SqlParameter("@EffectiveTo", (object?)price.EffectiveTo ?? DBNull.Value));

            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<List<SubscriptionPlanPrice>> GetPlanPricesByPlanIdAsync(int planId)
        {
            const string sql = @"
                SELECT spp.*, sp.PlanName
                FROM SubscriptionPlanPrices spp
                JOIN SubscriptionPlans sp ON spp.PlanID = sp.PlanID
                WHERE spp.PlanID = @PlanID
                ORDER BY spp.EffectiveFrom DESC";

            return await _db.ExecuteReaderListAsync(sql, r => new SubscriptionPlanPrice
            {
                PriceID = r.GetInt32(r.GetOrdinal("PriceID")),
                PlanID = r.GetInt32(r.GetOrdinal("PlanID")),
                Price = r.GetDecimal(r.GetOrdinal("Price")),
                EffectiveFrom = r.GetDateTime(r.GetOrdinal("EffectiveFrom")),
                EffectiveTo = r.IsDBNull(r.GetOrdinal("EffectiveTo")) ? null : r.GetDateTime(r.GetOrdinal("EffectiveTo")),
                CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
                PlanName = r.GetString(r.GetOrdinal("PlanName"))
            }, new SqlParameter("@PlanID", planId));
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

        public async Task<OwnerSubscription?> GetSubscriptionByIdAsync(int subscriptionId)
        {
            const string sql = @"
                SELECT os.*, sp.PlanName, u.FullName AS OwnerName
                FROM OwnerSubscriptions os
                JOIN SubscriptionPlans sp ON os.PlanID = sp.PlanID
                JOIN Users u ON os.OwnerID = u.UserID
                WHERE os.SubscriptionID = @SubscriptionID";

            return await _db.ExecuteReaderSingleAsync(sql, MapOwnerSub, new SqlParameter("@SubscriptionID", subscriptionId));
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

                public async Task<bool> HasAnySubscriptionHistoryAsync(int ownerId)
        {
            const string sql = "SELECT TOP 1 1 FROM OwnerSubscriptions WHERE OwnerID = @OwnerID";
            var res = await _db.ExecuteScalarAsync(sql, new SqlParameter("@OwnerID", ownerId));
            return res != null && Convert.ToInt32(res) == 1;
        }

        public async Task<int> GetCountedListingCountAsync(int ownerId)
        {
            const string sql = "SELECT COUNT(*) FROM Properties WHERE OwnerID = @OwnerID AND ListingStatus <> 'Rejected'";
            var res = await _db.ExecuteScalarAsync(sql, new SqlParameter("@OwnerID", ownerId));
            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<int> GetLiveListingCountAsync(int ownerId)
        {
            const string sql = "SELECT COUNT(*) FROM Properties WHERE OwnerID = @OwnerID AND ListingStatus = 'Active' AND IsApproved = 1";
            var res = await _db.ExecuteScalarAsync(sql, new SqlParameter("@OwnerID", ownerId));
            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<(bool Allowed, string? Reason, int Count, int Limit)> CheckListingCreationAllowanceAsync(int ownerId)
        {
            int count = await GetCountedListingCountAsync(ownerId);
            var activeSub = await GetActiveSubscriptionByOwnerIdAsync(ownerId);

            if (activeSub != null)
            {
                int limit = activeSub.MaxListings;
                if (count < limit)
                {
                    return (true, null, count, limit);
                }
                return (false, $"Listing allowance reached ({count}/{limit}). Please upgrade your subscription plan to add more properties.", count, limit);
            }

            bool hasHistory = await HasAnySubscriptionHistoryAsync(ownerId);
            if (!hasHistory)
            {
                // New owner eligible for Free Trial (allowance: 1)
                const int trialLimit = 1;
                if (count < trialLimit)
                {
                    return (true, null, count, trialLimit);
                }
                return (false, "Trial allowance reached (1 listing). Publish your first property to activate your trial, or upgrade to add more.", count, trialLimit);
            }

            // Prior subscription or trial has ended
            return (false, "Your subscription has ended. Choose a plan to add listings.", count, 0);
        }

        public async Task<int> GetTrialDurationDaysAsync()
        {
            const string sql = "SELECT SettingValue FROM SiteSettings WHERE SettingKey = 'TrialDurationDays'";
            var res = await _db.ExecuteScalarAsync(sql);
            if (res != null && int.TryParse(res.ToString(), out int days) && days > 0)
            {
                return days;
            }
            return 60;
        }

        public async Task<SubscriptionPlan?> GetPlanByNameAsync(string planName)
        {
            const string sql = "SELECT TOP 1 * FROM SubscriptionPlans WHERE PlanName = @PlanName";
            return await _db.ExecuteReaderSingleAsync(sql, r => new SubscriptionPlan
            {
                PlanID = r.GetInt32(r.GetOrdinal("PlanID")),
                PlanName = r.GetString(r.GetOrdinal("PlanName")),
                Price = r.GetDecimal(r.GetOrdinal("Price")),
                DurationInDays = r.GetInt32(r.GetOrdinal("DurationInDays")),
                MaxListings = r.GetInt32(r.GetOrdinal("MaxListings")),
                IsActive = r.GetBoolean(r.GetOrdinal("IsActive"))
            }, new SqlParameter("@PlanName", planName));
        }

        public async Task<OwnerSubscription?> CreateTrialSubscriptionAsync(int ownerId, int trialDurationDays = 60)
        {
            // Do not insert trial rows for owners who already have any subscription row
            if (await HasAnySubscriptionHistoryAsync(ownerId))
            {
                return null;
            }

            var trialPlan = await GetPlanByNameAsync("Free Trial");
            if (trialPlan == null)
            {
                int newPlanId = await CreatePlanAsync(new SubscriptionPlan
                {
                    PlanName = "Free Trial",
                    Price = 0,
                    DurationInDays = trialDurationDays,
                    MaxListings = 1,
                    IsActive = true
                });
                trialPlan = await GetPlanByIdAsync(newPlanId);
            }

            if (trialPlan == null) return null;

            int duration = trialDurationDays > 0 ? trialDurationDays : trialPlan.DurationInDays;
            var now = DateTime.UtcNow;

            var sub = new OwnerSubscription
            {
                OwnerID = ownerId,
                PlanID = trialPlan.PlanID,
                StartDate = now,
                EndDate = now.AddDays(duration),
                PaymentStatus = "Trial",
                GatewayTransactionID = null,
                PricePaid = 0m,
                PlanName = trialPlan.PlanName,
                MaxListings = trialPlan.MaxListings
            };

            int subId = await SubscribeOwnerAsync(sub);
            if (subId > 0)
            {
                sub.SubscriptionID = subId;
                return sub;
            }

            return null;
        }

        public async Task<List<OwnerSubscription>> GetExpiredSubscriptionsForHourlyProcessAsync()
        {
            // Owners whose latest subscription EndDate has passed (< GETUTCDATE())
            // and who have no later active or scheduled subscription (EndDate >= GETUTCDATE())
            // and who currently have Live properties (ListingStatus = 'Active')
            const string sql = @"
                SELECT os.*, sp.PlanName, sp.MaxListings, u.FullName AS OwnerName
                FROM OwnerSubscriptions os
                JOIN SubscriptionPlans sp ON os.PlanID = sp.PlanID
                JOIN Users u ON os.OwnerID = u.UserID
                WHERE (os.EndDate < GETUTCDATE() AND os.EndDate < GETDATE())
                  AND os.PaymentStatus IN ('Paid', 'Trial', 'Expired')
                  AND NOT EXISTS (
                      SELECT 1 FROM OwnerSubscriptions os2
                      WHERE os2.OwnerID = os.OwnerID
                        AND os2.PaymentStatus IN ('Paid', 'Trial', 'Scheduled')
                        AND (os2.EndDate >= GETUTCDATE() OR os2.EndDate >= GETDATE())
                  )
                  AND EXISTS (
                      SELECT 1 FROM Properties p
                      WHERE p.OwnerID = os.OwnerID AND p.ListingStatus = 'Active'
                  )
                ORDER BY os.EndDate DESC";

            return await _db.ExecuteReaderListAsync(sql, MapOwnerSub);
        }

        public async Task<List<OwnerSubscription>> GetExpiringSubscriptionsAsync(int daysBeforeExpiry)
        {
            const string sql = @"
                SELECT os.*, sp.PlanName, sp.MaxListings, u.FullName AS OwnerName
                FROM OwnerSubscriptions os
                JOIN SubscriptionPlans sp ON os.PlanID = sp.PlanID
                JOIN Users u ON os.OwnerID = u.UserID
                WHERE os.PaymentStatus IN ('Paid', 'Trial')
                  AND (os.StartDate <= SYSDATETIME() OR os.StartDate <= GETUTCDATE())
                  AND ((os.EndDate > GETDATE() AND os.EndDate <= DATEADD(DAY, @Days, GETDATE()))
                    OR (os.EndDate > GETUTCDATE() AND os.EndDate <= DATEADD(DAY, @Days, GETUTCDATE())))
                  AND NOT EXISTS (
                      SELECT 1 FROM OwnerSubscriptions os2
                      WHERE os2.OwnerID = os.OwnerID
                        AND os2.PaymentStatus IN ('Paid', 'Scheduled')
                        AND os2.StartDate >= os.EndDate
                  )";

            return await _db.ExecuteReaderListAsync(sql, MapOwnerSub, new SqlParameter("@Days", daysBeforeExpiry));
        }

        public async Task<List<OwnerSubscription>> GetNewlyActiveScheduledSubscriptionsAsync()
        {
            const string sql = @"
                SELECT os.*, sp.PlanName, sp.MaxListings, u.FullName AS OwnerName
                FROM OwnerSubscriptions os
                JOIN SubscriptionPlans sp ON os.PlanID = sp.PlanID
                JOIN Users u ON os.OwnerID = u.UserID
                WHERE os.PaymentStatus IN ('Paid', 'Scheduled')
                  AND (os.StartDate <= SYSDATETIME() OR os.StartDate <= GETUTCDATE())
                  AND (os.EndDate > GETDATE() OR os.EndDate > GETUTCDATE())
                  AND NOT EXISTS (
                      SELECT 1 FROM OwnerSubscriptions os2
                      WHERE os2.OwnerID = os.OwnerID
                        AND os2.SubscriptionID <> os.SubscriptionID
                        AND os2.PaymentStatus IN ('Paid', 'Trial')
                        AND (os2.StartDate <= SYSDATETIME() OR os2.StartDate <= GETUTCDATE())
                        AND os2.EndDate > os.StartDate
                  )";

            return await _db.ExecuteReaderListAsync(sql, MapOwnerSub);
        }

        public async Task<List<string>> RepublishExpiredPropertiesAsync(int ownerId, int maxListings)
        {
            var republished = new List<string>();
            if (maxListings <= 0) return republished;

            // Fetch properties that were unpublished with reason 'Subscription expired', oldest first
            const string selectSql = @"
                SELECT TOP (@MaxListings) PropertyID, Title
                FROM Properties
                WHERE OwnerID = @OwnerID 
                  AND ListingStatus = 'Deactivated' 
                  AND UnpublishReason = 'Subscription expired'
                ORDER BY PublishedAt ASC, PropertyID ASC";

            var dt = await _db.ExecuteDataTableAsync(selectSql,
                new SqlParameter("@MaxListings", maxListings),
                new SqlParameter("@OwnerID", ownerId));

            foreach (System.Data.DataRow row in dt.Rows)
            {
                int propId = Convert.ToInt32(row["PropertyID"]);
                string title = row["Title"].ToString() ?? "";

                const string updateSql = @"
                    UPDATE Properties 
                    SET ListingStatus = 'Active', 
                        IsApproved = 1,
                        PublishedAt = GETUTCDATE(), 
                        UnpublishReason = NULL 
                    WHERE PropertyID = @PropertyID AND OwnerID = @OwnerID";

                int affected = await _db.ExecuteNonQueryAsync(updateSql,
                    new SqlParameter("@PropertyID", propId),
                    new SqlParameter("@OwnerID", ownerId));

                if (affected > 0)
                {
                    republished.Add(title);
                }
            }

            return republished;
        }

        public async Task<bool> CanOwnerAddPropertyAsync(int ownerId)
        {
            var allowance = await CheckListingCreationAllowanceAsync(ownerId);
            return allowance.Allowed;
        }

        private static OwnerSubscription MapOwnerSub(SqlDataReader r)
        {
            var sub = new OwnerSubscription
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

            try
            {
                int maxOrd = r.GetOrdinal("MaxListings");
                if (!r.IsDBNull(maxOrd)) sub.MaxListings = r.GetInt32(maxOrd);
            }
            catch { }

            try
            {
                int ord = r.GetOrdinal("PricePaid");
                if (!r.IsDBNull(ord)) sub.PricePaid = r.GetDecimal(ord);
            }
            catch { }

            return sub;
        }
    }
}
