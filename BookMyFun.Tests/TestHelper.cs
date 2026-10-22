using BookMyFun.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookMyFun.Tests;

public static class TestHelper
{
    private static readonly SemaphoreSlim _initLock = new(1, 1);
    private static bool _initialized = false;

    public static IConfiguration GetTestConfig()
    {
        var config = new ConfigurationManager();
        string connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("TEST_CONNECTION_STRING")
            ?? "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";

        config["ConnectionStrings:DefaultConnection"] = connStr;
        config["Razorpay:KeyId"] = "rzp_test_bookmyfun_demo";
        config["Razorpay:KeySecret"] = "rzp_secret_demo_12345";
        return config;
    }

    public static DBHelper GetTestDb()
    {
        var config = GetTestConfig();
        var db = new DBHelper(config);
        EnsureDatabaseInitialized(db);
        return db;
    }

    public static void EnsureDatabaseInitialized(DBHelper db)
    {
        if (_initialized) return;
        _initLock.Wait();
        try
        {
            if (_initialized) return;
            var logger = NullLogger<DatabaseInitializer>.Instance;
            var initializer = new DatabaseInitializer(db, logger);
            initializer.InitializeAsync().GetAwaiter().GetResult();
            try
            {
                DatabaseSeeder.SeedInitialDataAsync(db).GetAwaiter().GetResult();
            }
            catch { }
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
