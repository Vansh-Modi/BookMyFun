using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Data;
using BookMyFun.Services.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class Phase6SchemaMigrationTests
    {
        [Fact]
        public void MigrationScripts_Exist_And_Follow_Naming_Conventions()
        {
            // Locate db/migrations folder
            string current = Directory.GetCurrentDirectory();
            string? migrationsDir = null;
            for (int i = 0; i < 5; i++)
            {
                string candidate = Path.Combine(current, "db", "migrations");
                if (Directory.Exists(candidate))
                {
                    migrationsDir = candidate;
                    break;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }

            Assert.NotNull(migrationsDir);

            var files = Directory.GetFiles(migrationsDir, "*.sql")
                .Select(Path.GetFileName)
                .OrderBy(f => f)
                .ToList();

            Assert.Contains("001_baseline_check.sql", files);
            Assert.Contains("002_user_notifications.sql", files);
            Assert.Contains("003_copy_legacy_property_images.sql", files);
            Assert.Contains("004_notification_outbox.sql", files);
            Assert.Contains("005_query_indexes.sql", files);
            Assert.Contains("006_confirm_media_type.sql", files);

            // Verify each script is transactional and updates SchemaVersion
            foreach (var file in files)
            {
                if (file == null) continue;
                string content = File.ReadAllText(Path.Combine(migrationsDir!, file));
                Assert.Contains("BEGIN TRANSACTION", content, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("COMMIT TRANSACTION", content, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("SchemaVersion", content, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public async Task SchemaVersionService_Degrades_Gracefully_When_Unconfigured()
        {
            // Create an unconfigured DBHelper
            var db = new DBHelper(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            var service = new SchemaVersionService(db, NullLogger<SchemaVersionService>.Instance);

            var status = await service.CheckSchemaStatusAsync();

            Assert.True(status.IsBehind);
            Assert.Equal(0, status.CurrentVersion);
            Assert.Equal(6, status.RequiredVersion);
            Assert.Equal(6, status.MissingVersions.Count);
            Assert.NotNull(status.StatusMessage);
        }
    }
}
