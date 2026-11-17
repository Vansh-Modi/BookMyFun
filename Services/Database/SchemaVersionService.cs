using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services.Database
{
    public class SchemaVersionService : ISchemaVersionService
    {
        private readonly DBHelper _db;
        private readonly ILogger<SchemaVersionService> _logger;
        public const int RequiredApplicationVersion = 6;

        private static SchemaVersionStatus? _cachedStatus;
        private static DateTime _lastCheckTime = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

        public SchemaVersionService(DBHelper db, ILogger<SchemaVersionService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<SchemaVersionStatus> CheckSchemaStatusAsync()
        {
            if (_cachedStatus != null && DateTime.UtcNow - _lastCheckTime < CacheDuration)
            {
                return _cachedStatus;
            }

            var status = new SchemaVersionStatus
            {
                RequiredVersion = RequiredApplicationVersion
            };

            if (!_db.IsConfigured)
            {
                status.CurrentVersion = 0;
                status.MissingVersions = Enumerable.Range(1, RequiredApplicationVersion).ToList();
                status.StatusMessage = "Database connection is unconfigured; running in offline/degraded mode.";
                _cachedStatus = status;
                _lastCheckTime = DateTime.UtcNow;
                return status;
            }

            try
            {
                // Check if SchemaVersion table exists and get max version
                const string sql = @"
                    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaVersion' AND schema_id = SCHEMA_ID('dbo'))
                    BEGIN
                        SELECT ISNULL(MAX(Version), 0) FROM dbo.SchemaVersion;
                    END
                    ELSE
                    BEGIN
                        SELECT -1;
                    END";

                var result = await _db.ExecuteScalarAsync(sql);
                int maxVersion = result != null ? Convert.ToInt32(result) : -1;

                if (maxVersion < 0)
                {
                    status.CurrentVersion = 0;
                    status.MissingVersions = Enumerable.Range(1, RequiredApplicationVersion).ToList();
                    status.StatusMessage = $"SchemaVersion table is missing. Expected version: {RequiredApplicationVersion}.";
                }
                else
                {
                    status.CurrentVersion = maxVersion;
                    var missing = new List<int>();
                    for (int v = 1; v <= RequiredApplicationVersion; v++)
                    {
                        if (v > maxVersion) missing.Add(v);
                    }
                    status.MissingVersions = missing;
                    status.StatusMessage = status.IsBehind
                        ? $"Schema is at version {status.CurrentVersion} (missing: {string.Join(", ", status.MissingVersions)}). Features will gracefully degrade."
                        : $"Schema is up-to-date at version {status.CurrentVersion}.";
                }

                if (status.IsBehind)
                {
                    _logger.LogWarning("Database schema is at version {CurrentVersion}, missing migrations: {MissingVersions}. Apply pending migrations using tools/DbMigrate.",
                        status.CurrentVersion, string.Join(", ", status.MissingVersions));
                }
            }
            catch (SqlException ex)
            {
                _logger.LogWarning(ex, "SchemaVersion check encountered a recoverable database error. Defaulting to degraded mode.");
                status.CurrentVersion = 0;
                status.MissingVersions = Enumerable.Range(1, RequiredApplicationVersion).ToList();
                status.StatusMessage = "Database unreachable or schema version query failed. Gracefully degrading.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error checking database schema version.");
                status.CurrentVersion = 0;
                status.MissingVersions = Enumerable.Range(1, RequiredApplicationVersion).ToList();
                status.StatusMessage = "Schema version verification failed.";
            }

            _cachedStatus = status;
            _lastCheckTime = DateTime.UtcNow;
            return status;
        }
    }
}
