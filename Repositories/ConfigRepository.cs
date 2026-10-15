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
                        ApiSecret = @ApiSecret, 
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
                new SqlParameter("@ApiSecret", config.ApiSecret),
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
