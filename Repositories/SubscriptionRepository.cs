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
        Task<bool> DeactivatePlanAsync(int planId) => Task.FromResult(false);
        Task<int> GrantSubscriptionAsync(OwnerSubscription grantSub) => Task.FromResult(0);
        Task<SubscriptionPlan?> GetActiveTrialPlanAsync() => Task.FromResult<SubscriptionPlan?>(null);
        Task<bool> HasAnyTrialHistoryAsync(int ownerId) => Task.FromResult(false);
        Task<bool> IsPlanNameUniqueAsync(string planName, int? excludePlanId = null) => Task.FromResult(true);
    }

    public class SubscriptionRepository : ISubscriptionRepository
    {
        private readonly DBHelper _db;

        public SubscriptionRepository(DBHelper db)
        {
            _db = db;
        }

        private static SubscriptionPlan MapSubscriptionPlan(SqlDataReader r)
        {
            var plan = new SubscriptionPlan
            {
                PlanID = r.GetInt32(r.GetOrdinal("PlanID")),
                PlanName = r.GetString(r.GetOrdinal("PlanName")),
                Price = r.GetDecimal(r.GetOrdinal("Price")),
                DurationInDays = r.GetInt32(r.GetOrdinal("DurationInDays")),
                MaxListings = r.GetInt32(r.GetOrdinal("MaxListings")),
                IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
                CurrentPrice = r.GetDecimal(r.GetOrdinal("Price"))
            };

            try
            {
                int ord = r.GetOrdinal("PlanType");
                if (!r.IsDBNull(ord)) plan.PlanType = r.GetString(ord);
            }
            catch { }

            try
            {
                int ord = r.GetOrdinal("Description");
                if (!r.IsDBNull(ord)) plan.Description = r.GetString(ord);
            }
            catch { }

            try
            {
                int ord = r.GetOrdinal("OwnerCount");
                if (!r.IsDBNull(ord)) plan.OwnerCount = r.GetInt32(ord);
            }
            catch { }

            return plan;
        }

        public async Task<List<SubscriptionPlan>> GetAllPlansAsync(bool onlyActive = true)
        {
            var sql = @"
                SELECT sp.*,
                       (SELECT COUNT(DISTINCT os.OwnerID) 
                        FROM OwnerSubscriptions os 
                        WHERE os.PlanID = sp.PlanID) AS OwnerCount
                FROM SubscriptionPlans sp"
                + (onlyActive ? " WHERE sp.IsActive = 1" : "")
                + " ORDER BY sp.Price ASC";

            var plans = await _db.ExecuteReaderListAsync(sql, MapSubscriptionPlan);

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
            const string sql = @"
                SELECT sp.*,
                       (SELECT COUNT(DISTINCT os.OwnerID) 
                        FROM OwnerSubscriptions os 
                        WHERE os.PlanID = sp.PlanID) AS OwnerCount
                FROM SubscriptionPlans sp
                WHERE sp.PlanID = @PlanID";
            return await _db.ExecuteReaderSingleAsync(sql, MapSubscriptionPlan, new SqlParameter("@PlanID", planId));
        }

        public async Task<bool> IsPlanNameUniqueAsync(string planName, int? excludePlanId = null)
        {
            string sql = "SELECT COUNT(*) FROM SubscriptionPlans WHERE LOWER(LTRIM(RTRIM(PlanName))) = LOWER(LTRIM(RTRIM(@PlanName)))";
            if (excludePlanId.HasValue)
            {
                sql += " AND PlanID <> @ExcludePlanID";
            }
            var res = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@PlanName", planName.Trim()),
                new SqlParameter("@ExcludePlanID", (object?)excludePlanId ?? DBNull.Value));
            return res != null && Convert.ToInt32(res) == 0;
        }

        public async Task<int> CreatePlanAsync(SubscriptionPlan plan)
        {
            if (!await IsPlanNameUniqueAsync(plan.PlanName))
            {
                return -1; // Duplicate name
            }

            const string sql = @"
                IF COL_LENGTH('SubscriptionPlans', 'PlanType') IS NOT NULL
                BEGIN
                    EXEC sp_executesql N'INSERT INTO SubscriptionPlans (PlanName, PlanType, Price, DurationInDays, MaxListings, Description, IsActive)
                    OUTPUT INSERTED.PlanID
                    VALUES (@PlanName, @PlanType, @Price, @DurationInDays, @MaxListings, @Description, @IsActive)',
                    N'@PlanName NVARCHAR(100), @PlanType NVARCHAR(50), @Price DECIMAL(18,2), @DurationInDays INT, @MaxListings INT, @Description NVARCHAR(500), @IsActive BIT',
                    @PlanName, @PlanType, @Price, @DurationInDays, @MaxListings, @Description, @IsActive;
                END
                ELSE
                BEGIN
                    INSERT INTO SubscriptionPlans (PlanName, Price, DurationInDays, MaxListings, IsActive)
                    OUTPUT INSERTED.PlanID
                    VALUES (@PlanName, @Price, @DurationInDays, @MaxListings, @IsActive);
                END";

            var res = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@PlanName", plan.PlanName.Trim()),
                new SqlParameter("@PlanType", string.IsNullOrWhiteSpace(plan.PlanType) ? "Paid" : plan.PlanType),
                new SqlParameter("@Price", plan.Price),
                new SqlParameter("@DurationInDays", plan.DurationInDays),
                new SqlParameter("@MaxListings", plan.MaxListings),
                new SqlParameter("@Description", (object?)plan.Description ?? DBNull.Value),
                new SqlParameter("@IsActive", plan.IsActive));

            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<bool> UpdatePlanAsync(SubscriptionPlan plan)
        {
            if (!await IsPlanNameUniqueAsync(plan.PlanName, plan.PlanID))
            {
                return false;
            }

            const string sql = @"
                IF COL_LENGTH('SubscriptionPlans', 'PlanType') IS NOT NULL
                BEGIN
                    EXEC sp_executesql N'UPDATE SubscriptionPlans 
                    SET PlanName = @PlanName, 
                        PlanType = @PlanType,
                        Price = @Price, 
                        DurationInDays = @DurationInDays, 
                        MaxListings = @MaxListings, 
                        Description = @Description,
                        IsActive = @IsActive
                    WHERE PlanID = @PlanID',
                    N'@PlanName NVARCHAR(100), @PlanType NVARCHAR(50), @Price DECIMAL(18,2), @DurationInDays INT, @MaxListings INT, @Description NVARCHAR(500), @IsActive BIT, @PlanID INT',
                    @PlanName, @PlanType, @Price, @DurationInDays, @MaxListings, @Description, @IsActive, @PlanID;
                END
                ELSE
                BEGIN
                    UPDATE SubscriptionPlans 
                    SET PlanName = @PlanName, 
                        Price = @Price, 
                        DurationInDays = @DurationInDays, 
                        MaxListings = @MaxListings, 
                        IsActive = @IsActive
                    WHERE PlanID = @PlanID;
                END";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@PlanName", plan.PlanName.Trim()),
                new SqlParameter("@PlanType", string.IsNullOrWhiteSpace(plan.PlanType) ? "Paid" : plan.PlanType),
                new SqlParameter("@Price", plan.Price),
                new SqlParameter("@DurationInDays", plan.DurationInDays),
                new SqlParameter("@MaxListings", plan.MaxListings),
                new SqlParameter("@Description", (object?)plan.Description ?? DBNull.Value),
                new SqlParameter("@IsActive", plan.IsActive),
                new SqlParameter("@PlanID", plan.PlanID));

            return rows > 0;
        }

        public async Task<bool> DeactivatePlanAsync(int planId)
        {
            const string sql = "UPDATE SubscriptionPlans SET IsActive = 0 WHERE PlanID = @PlanID";
            var rows = await _db.ExecuteNonQueryAsync(sql, new SqlParameter("@PlanID", planId));
            return rows > 0;
        }

        public async Task<int> SubscribeOwnerAsync(OwnerSubscription sub)
        {
            const string sql = @"
                INSERT INTO OwnerSubscriptions (OwnerID, PlanID, StartDate, EndDate, PaymentStatus, GatewayTransactionID, PricePaid, PlanNameSnapshot, MaxListingsSnapshot, DurationDaysSnapshot, GrantedByUserID, GrantReason)
                OUTPUT INSERTED.SubscriptionID
                VALUES (@OwnerID, @PlanID, @StartDate, @EndDate, @PaymentStatus, @GatewayTransactionID, @PricePaid, @PlanNameSnapshot, @MaxListingsSnapshot, @DurationDaysSnapshot, @GrantedByUserID, @GrantReason);";

            var res = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@OwnerID", sub.OwnerID),
                new SqlParameter("@PlanID", sub.PlanID),
                new SqlParameter("@StartDate", sub.StartDate),
                new SqlParameter("@EndDate", sub.EndDate),
                new SqlParameter("@PaymentStatus", sub.PaymentStatus),
                new SqlParameter("@GatewayTransactionID", DBHelper.ToDbValue(sub.GatewayTransactionID)),
                new SqlParameter("@PricePaid", (object?)sub.PricePaid ?? DBNull.Value),
                new SqlParameter("@PlanNameSnapshot", (object?)sub.PlanNameSnapshot ?? DBNull.Value),
                new SqlParameter("@MaxListingsSnapshot", (object?)sub.MaxListingsSnapshot ?? DBNull.Value),
                new SqlParameter("@DurationDaysSnapshot", (object?)sub.DurationDaysSnapshot ?? DBNull.Value),
                new SqlParameter("@GrantedByUserID", (object?)sub.GrantedByUserID ?? DBNull.Value),
                new SqlParameter("@GrantReason", (object?)sub.GrantReason ?? DBNull.Value));

            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<int> GrantSubscriptionAsync(OwnerSubscription grantSub)
        {
            return await SubscribeOwnerAsync(grantSub);
        }

        public async Task<SubscriptionPlan?> GetActiveTrialPlanAsync()
        {
            const string sql = @"
                SELECT TOP 1 sp.*,
                       (SELECT COUNT(DISTINCT os.OwnerID) 
                        FROM OwnerSubscriptions os 
                        WHERE os.PlanID = sp.PlanID) AS OwnerCount
                FROM SubscriptionPlans sp 
                WHERE (sp.PlanType = 'Trial' OR sp.PlanName = 'Free Trial') AND sp.IsActive = 1 
                ORDER BY sp.PlanID ASC";
            return await _db.ExecuteReaderSingleAsync(sql, MapSubscriptionPlan);
        }

        public async Task<bool> HasAnyTrialHistoryAsync(int ownerId)
        {
            const string sql = @"
                SELECT TOP 1 1 
                FROM OwnerSubscriptions os
                LEFT JOIN SubscriptionPlans sp ON os.PlanID = sp.PlanID
                WHERE os.OwnerID = @OwnerID 
                  AND (os.PaymentStatus = 'Trial' OR sp.PlanType = 'Trial' OR sp.PlanName = 'Free Trial')";
            var res = await _db.ExecuteScalarAsync(sql, new SqlParameter("@OwnerID", ownerId));
            return res != null && Convert.ToInt32(res) == 1;
        }

        public async Task<OwnerSubscription?> GetActiveSubscriptionByOwnerIdAsync(int ownerId)
        {
            const string sql = @"
                SELECT TOP 1 os.*, sp.PlanName, sp.MaxListings, u.FullName AS OwnerName
                FROM OwnerSubscriptions os
                LEFT JOIN SubscriptionPlans sp ON os.PlanID = sp.PlanID
                JOIN Users u ON os.OwnerID = u.UserID
                WHERE os.OwnerID = @OwnerID 
                  AND os.PaymentStatus IN ('Paid', 'Trial', 'AdminGranted') 
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
                LEFT JOIN SubscriptionPlans sp ON os.PlanID = sp.PlanID
                JOIN Users u ON os.OwnerID = u.UserID
                WHERE os.OwnerID = @OwnerID 
                  AND os.PaymentStatus IN ('Paid', 'Scheduled')
                  AND (os.StartDate > SYSDATETIME() OR os.StartDate > GETUTCDATE())
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
                SELECT os.*, sp.PlanName, sp.MaxListings, u.FullName AS OwnerName
                FROM OwnerSubscriptions os
                LEFT JOIN SubscriptionPlans sp ON os.PlanID = sp.PlanID
                JOIN Users u ON os.OwnerID = u.UserID
                WHERE os.OwnerID = @OwnerID AND os.PaymentStatus <> 'Pending'
                ORDER BY os.StartDate DESC";

            return await _db.ExecuteReaderListAsync(sql, MapOwnerSub, new SqlParameter("@OwnerID", ownerId));
        }

        public async Task<OwnerSubscription?> GetSubscriptionByIdAsync(int subscriptionId)
        {
            const string sql = @"
                SELECT os.*, sp.PlanName, sp.MaxListings, u.FullName AS OwnerName
                FROM OwnerSubscriptions os
                LEFT JOIN SubscriptionPlans sp ON os.PlanID = sp.PlanID
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
            const string sql = "SELECT COUNT(*) FROM Properties WHERE OwnerID = @OwnerID AND (ListingStatus = 'Active' OR ListingStatus = 'Live') AND IsApproved = 1";
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

            // An owner with no plan can add one property. It counts toward the trial's limit of 1, and cannot be published until a plan is active.
            const int noPlanLimit = 1;
            if (count < noPlanLimit)
            {
                return (true, null, count, noPlanLimit);
            }

            return (false, "Free listing limit reached (1 property). Choose an active subscription plan to list more properties.", count, noPlanLimit);
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
                  AND os.PaymentStatus IN ('Paid', 'Trial', 'AdminGranted', 'Expired')
                  AND NOT EXISTS (
                      SELECT 1 FROM OwnerSubscriptions os2
                      WHERE os2.OwnerID = os.OwnerID
                        AND os2.PaymentStatus IN ('Paid', 'Trial', 'AdminGranted', 'Scheduled')
                        AND (os2.EndDate >= GETUTCDATE() OR os2.EndDate >= GETDATE())
                  )
                  AND EXISTS (
                      SELECT 1 FROM Properties p
                      WHERE p.OwnerID = os.OwnerID AND (p.ListingStatus = 'Live' OR p.ListingStatus = 'Active')
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
                OwnerName = r.GetString(r.GetOrdinal("OwnerName"))
            };

            try
            {
                int ord = r.GetOrdinal("PlanNameSnapshot");
                if (!r.IsDBNull(ord)) sub.PlanNameSnapshot = r.GetString(ord);
            }
            catch { }

            try
            {
                int ord = r.GetOrdinal("MaxListingsSnapshot");
                if (!r.IsDBNull(ord)) sub.MaxListingsSnapshot = r.GetInt32(ord);
            }
            catch { }

            try
            {
                int ord = r.GetOrdinal("DurationDaysSnapshot");
                if (!r.IsDBNull(ord)) sub.DurationDaysSnapshot = r.GetInt32(ord);
            }
            catch { }

            try
            {
                int ord = r.GetOrdinal("GrantedByUserID");
                if (!r.IsDBNull(ord)) sub.GrantedByUserID = r.GetInt32(ord);
            }
            catch { }

            try
            {
                int ord = r.GetOrdinal("GrantReason");
                if (!r.IsDBNull(ord)) sub.GrantReason = r.GetString(ord);
            }
            catch { }

            if (!string.IsNullOrEmpty(sub.PlanNameSnapshot))
            {
                sub.PlanName = sub.PlanNameSnapshot;
            }
            else
            {
                try { sub.PlanName = r.GetString(r.GetOrdinal("PlanName")); } catch { }
            }

            if (sub.MaxListingsSnapshot.HasValue && sub.MaxListingsSnapshot.Value > 0)
            {
                sub.MaxListings = sub.MaxListingsSnapshot.Value;
            }
            else
            {
                try
                {
                    int maxOrd = r.GetOrdinal("MaxListings");
                    if (!r.IsDBNull(maxOrd)) sub.MaxListings = r.GetInt32(maxOrd);
                }
                catch { }
            }

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
