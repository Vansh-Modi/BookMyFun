using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IConfigRepository
    {
        Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider);
        Task<List<ApiConfiguration>> GetAllConfigsAsync();
        Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config);
        Task<string?> GetSettingValueAsync(string key);
        Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false);
        Task<List<SiteSetting>> GetAllSettingsAsync();
        Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details);
        Task<List<AuditLog>> GetAuditLogsAsync(int limit = 100) => Task.FromResult(new List<AuditLog>());
        Task<(List<AuditLog> Items, int TotalCount)> GetAuditLogsPagedAsync(string? search = null, string? action = null, string? entityType = null, DateTime? fromDate = null, DateTime? toDate = null, int page = 1, int pageSize = 25) => Task.FromResult((new List<AuditLog>(), 0));
    }

    public class ConfigRepository : IConfigRepository
    {
        private readonly DBHelper _db;

        public ConfigRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider)
        {
            const string sql = "SELECT TOP 1 * FROM ApiConfigurations WHERE ServiceProvider = @Provider AND IsActive = 1";
            return await _db.ExecuteReaderSingleAsync(sql, MapConfig, new SqlParameter("@Provider", provider));
        }

        public async Task<List<ApiConfiguration>> GetAllConfigsAsync()
        {
            const string sql = "SELECT * FROM ApiConfigurations ORDER BY ServiceProvider";
            return await _db.ExecuteReaderListAsync(sql, MapConfig);
        }

        public async Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config)
        {
            const string sql = @"
                IF EXISTS (SELECT 1 FROM ApiConfigurations WHERE ServiceProvider = @ServiceProvider)
                BEGIN
                    UPDATE ApiConfigurations 
                    SET ApiKey = @ApiKey, 
                        ApiSecret = CASE WHEN @ApiSecret IS NOT NULL AND RTRIM(LTRIM(@ApiSecret)) <> '' THEN @ApiSecret ELSE ApiSecret END, 
                        ExtraParams = @ExtraParams, 
                        IsActive = @IsActive
                    WHERE ServiceProvider = @ServiceProvider
                END
                ELSE
                BEGIN
                    INSERT INTO ApiConfigurations (ServiceProvider, ApiKey, ApiSecret, ExtraParams, IsActive)
                    VALUES (@ServiceProvider, @ApiKey, @ApiSecret, @ExtraParams, @IsActive)
                END";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@ServiceProvider", config.ServiceProvider),
                new SqlParameter("@ApiKey", config.ApiKey),
                new SqlParameter("@ApiSecret", DBHelper.ToDbValue(config.ApiSecret)),
                new SqlParameter("@ExtraParams", DBHelper.ToDbValue(config.ExtraParams)),
                new SqlParameter("@IsActive", config.IsActive));

            return rows > 0;
        }

        public async Task<string?> GetSettingValueAsync(string key)
        {
            const string sql = "SELECT SettingValue FROM SiteSettings WHERE SettingKey = @Key";
            var result = await _db.ExecuteScalarAsync(sql, new SqlParameter("@Key", key));
            return result?.ToString();
        }

        public async Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false)
        {
            const string sql = @"
                IF EXISTS (SELECT 1 FROM SiteSettings WHERE SettingKey = @Key)
                BEGIN
                    UPDATE SiteSettings 
                    SET SettingValue = @Value, 
                        IsEncrypted = @IsEncrypted, 
                        UpdatedAt = GETDATE()
                    WHERE SettingKey = @Key
                END
                ELSE
                BEGIN
                    INSERT INTO SiteSettings (SettingKey, SettingValue, IsEncrypted, UpdatedAt)
                    VALUES (@Key, @Value, @IsEncrypted, GETDATE())
                END";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@Key", key),
                new SqlParameter("@Value", value),
                new SqlParameter("@IsEncrypted", isEncrypted));

            return rows > 0;
        }

        public async Task<List<SiteSetting>> GetAllSettingsAsync()
        {
            const string sql = "SELECT * FROM SiteSettings ORDER BY SettingKey";
            return await _db.ExecuteReaderListAsync(sql, r => new SiteSetting
            {
                SettingID = r.GetInt32(r.GetOrdinal("SettingID")),
                SettingKey = r.GetString(r.GetOrdinal("SettingKey")),
                SettingValue = r.IsDBNull(r.GetOrdinal("SettingValue")) ? null : r.GetString(r.GetOrdinal("SettingValue")),
                IsEncrypted = r.GetBoolean(r.GetOrdinal("IsEncrypted")),
                UpdatedAt = r.GetDateTime(r.GetOrdinal("UpdatedAt"))
            });
        }

        public async Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details)
        {
            try
            {
                const string sql = @"
                    INSERT INTO AuditLogs (PerformedByUserID, Action, EntityType, EntityID, Details, Timestamp)
                    VALUES (@UserID, @Action, @EntityType, @EntityID, @Details, GETDATE())";

                await _db.ExecuteNonQueryAsync(sql,
                    new SqlParameter("@UserID", performedByUserId),
                    new SqlParameter("@Action", action),
                    new SqlParameter("@EntityType", entityType),
                    new SqlParameter("@EntityID", DBHelper.ToDbValue(entityId)),
                    new SqlParameter("@Details", DBHelper.ToDbValue(details)));
            }
            catch
            {
                // Non-blocking fallback
            }
        }

        public async Task<List<AuditLog>> GetAuditLogsAsync(int limit = 100)
        {
            try
            {
                string sql = $@"
                    SELECT TOP ({limit}) a.*, u.FullName AS PerformedByUserName, u.Email AS PerformedByUserEmail
                    FROM AuditLogs a
                    LEFT JOIN Users u ON a.PerformedByUserID = u.UserID
                    ORDER BY a.Timestamp DESC";

                return await _db.ExecuteReaderListAsync(sql, r => new AuditLog
                {
                    AuditID = r.GetInt32(r.GetOrdinal("AuditID")),
                    PerformedByUserID = r.GetInt32(r.GetOrdinal("PerformedByUserID")),
                    Action = r.GetString(r.GetOrdinal("Action")),
                    EntityType = r.GetString(r.GetOrdinal("EntityType")),
                    EntityID = r.IsDBNull(r.GetOrdinal("EntityID")) ? null : r.GetInt32(r.GetOrdinal("EntityID")),
                    Details = r.IsDBNull(r.GetOrdinal("Details")) ? null : r.GetString(r.GetOrdinal("Details")),
                    Timestamp = r.GetDateTime(r.GetOrdinal("Timestamp")),
                    PerformedByUserName = r.IsDBNull(r.GetOrdinal("PerformedByUserName")) ? null : r.GetString(r.GetOrdinal("PerformedByUserName")),
                    PerformedByUserEmail = r.IsDBNull(r.GetOrdinal("PerformedByUserEmail")) ? null : r.GetString(r.GetOrdinal("PerformedByUserEmail"))
                });
            }
            catch
            {
                return new List<AuditLog>();
            }
        }

        public async Task<(List<AuditLog> Items, int TotalCount)> GetAuditLogsPagedAsync(string? search = null, string? action = null, string? entityType = null, DateTime? fromDate = null, DateTime? toDate = null, int page = 1, int pageSize = 25)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            int offset = (page - 1) * pageSize;

            try
            {
                var whereClauses = new List<string>();
                var parameters = new List<SqlParameter>();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    whereClauses.Add("(a.Action LIKE @Search OR a.EntityType LIKE @Search OR a.Details LIKE @Search OR u.FullName LIKE @Search OR u.Email LIKE @Search)");
                    parameters.Add(new SqlParameter("@Search", $"%{search.Trim()}%"));
                }
                if (!string.IsNullOrWhiteSpace(action) && !action.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    whereClauses.Add("a.Action = @ActionFilter");
                    parameters.Add(new SqlParameter("@ActionFilter", action.Trim()));
                }
                if (!string.IsNullOrWhiteSpace(entityType) && !entityType.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    whereClauses.Add("a.EntityType = @EntityTypeFilter");
                    parameters.Add(new SqlParameter("@EntityTypeFilter", entityType.Trim()));
                }
                if (fromDate.HasValue)
                {
                    whereClauses.Add("a.Timestamp >= @FromDate");
                    parameters.Add(new SqlParameter("@FromDate", fromDate.Value.Date));
                }
                if (toDate.HasValue)
                {
                    whereClauses.Add("a.Timestamp < @ToDate");
                    parameters.Add(new SqlParameter("@ToDate", toDate.Value.Date.AddDays(1)));
                }

                string whereSql = whereClauses.Any() ? " WHERE " + string.Join(" AND ", whereClauses) : "";

                string countSql = $"SELECT COUNT(*) FROM AuditLogs a LEFT JOIN Users u ON a.PerformedByUserID = u.UserID {whereSql}";
                var countParams = parameters.Select(p => new SqlParameter(p.ParameterName, p.Value)).ToArray();
                int totalCount = Convert.ToInt32(await _db.ExecuteScalarAsync(countSql, countParams) ?? 0);

                string dataSql = $@"
                    SELECT a.*, u.FullName AS PerformedByUserName, u.Email AS PerformedByUserEmail
                    FROM AuditLogs a
                    LEFT JOIN Users u ON a.PerformedByUserID = u.UserID
                    {whereSql}
                    ORDER BY a.Timestamp DESC
                    OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY";

                var items = await _db.ExecuteReaderListAsync(dataSql, r => new AuditLog
                {
                    AuditID = r.GetInt32(r.GetOrdinal("AuditID")),
                    PerformedByUserID = r.GetInt32(r.GetOrdinal("PerformedByUserID")),
                    Action = r.GetString(r.GetOrdinal("Action")),
                    EntityType = r.GetString(r.GetOrdinal("EntityType")),
                    EntityID = r.IsDBNull(r.GetOrdinal("EntityID")) ? null : r.GetInt32(r.GetOrdinal("EntityID")),
                    Details = r.IsDBNull(r.GetOrdinal("Details")) ? null : r.GetString(r.GetOrdinal("Details")),
                    Timestamp = r.GetDateTime(r.GetOrdinal("Timestamp")),
                    PerformedByUserName = r.IsDBNull(r.GetOrdinal("PerformedByUserName")) ? null : r.GetString(r.GetOrdinal("PerformedByUserName")),
                    PerformedByUserEmail = r.IsDBNull(r.GetOrdinal("PerformedByUserEmail")) ? null : r.GetString(r.GetOrdinal("PerformedByUserEmail"))
                }, parameters.ToArray());

                return (items, totalCount);
            }
            catch
            {
                return (new List<AuditLog>(), 0);
            }
        }

        private static ApiConfiguration MapConfig(SqlDataReader r)
        {
            return new ApiConfiguration
            {
                ConfigID = r.GetInt32(r.GetOrdinal("ConfigID")),
                ServiceProvider = r.GetString(r.GetOrdinal("ServiceProvider")),
                ApiKey = r.GetString(r.GetOrdinal("ApiKey")),
                ApiSecret = r.GetString(r.GetOrdinal("ApiSecret")),
                ExtraParams = r.IsDBNull(r.GetOrdinal("ExtraParams")) ? null : r.GetString(r.GetOrdinal("ExtraParams")),
                IsActive = r.GetBoolean(r.GetOrdinal("IsActive"))
            };
        }
    }
}
