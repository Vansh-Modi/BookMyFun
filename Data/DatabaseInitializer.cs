using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Data
{
    public class DatabaseInitializer
    {
        private readonly DBHelper _dbHelper;
        private readonly ILogger<DatabaseInitializer> _logger;

        public DatabaseInitializer(DBHelper dbHelper, ILogger<DatabaseInitializer> logger)
        {
            _dbHelper = dbHelper;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            if (!_dbHelper.IsConfigured)
            {
                _logger.LogWarning("Database initialization skipped: Configuration key 'ConnectionStrings:DefaultConnection' is missing, empty, or rejected.");
                return;
            }

            // Runtime DDL (CREATE TABLE / ALTER TABLE) is strictly disabled at application startup.
            // Schema changes must be applied via versioned SQL migrations in /db using DbMigrate.
            _logger.LogInformation("Runtime DDL initialization is disabled. Database schema is managed via /db migrations.");
            await Task.CompletedTask;
        }
    }
}
