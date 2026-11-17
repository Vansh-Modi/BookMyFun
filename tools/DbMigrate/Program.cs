using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace BookMyFun.Tools.DbMigrate
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("BookMyFun Database Migration Tool (CLI)");
            Console.WriteLine("=================================================");

            // 1. Resolve configuration from Environment Variables and User Secrets ONLY (never from tracked file)
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>(optional: true)
                .Build();

            string? connectionString = null;

            // Check CLI args
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("--connection", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    connectionString = args[i + 1];
                }
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                                   ?? Environment.GetEnvironmentVariable("DefaultConnection")
                                   ?? config.GetConnectionString("DefaultConnection");
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERROR: No connection string found.");
                Console.ResetColor();
                Console.WriteLine("Please set the 'ConnectionStrings__DefaultConnection' environment variable, or configure dotnet user-secrets.");
                return 1;
            }

            // Extract Database Name for logging (never log full connection string or credentials)
            var builder = new SqlConnectionStringBuilder(connectionString);
            Console.WriteLine($"Target Database: {builder.InitialCatalog} on DataSource: {builder.DataSource}");

            // 2. Locate migrations directory
            string? migrationsDir = FindMigrationsDirectory();
            if (migrationsDir == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERROR: Could not locate 'db/migrations' directory.");
                Console.ResetColor();
                return 1;
            }

            Console.WriteLine($"Migrations Directory: {migrationsDir}");

            // 3. Connect to database and ensure dbo.SchemaVersion exists
            try
            {
                await using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();

                await EnsureSchemaVersionTableAsync(conn);

                // Read already applied versions
                var appliedMigrations = await GetAppliedMigrationsAsync(conn);
                Console.WriteLine($"Current applied migration count in DB: {appliedMigrations.Count}");
                if (appliedMigrations.Any())
                {
                    Console.WriteLine($"Latest applied version: {appliedMigrations.Keys.Max()}");
                }

                // 4. Discover and order migration scripts
                var scriptFiles = Directory.GetFiles(migrationsDir, "*.sql")
                    .OrderBy(Path.GetFileName)
                    .ToList();

                if (!scriptFiles.Any())
                {
                    Console.WriteLine("No SQL migration files found in directory.");
                    return 0;
                }

                int appliedCount = 0;

                foreach (var scriptPath in scriptFiles)
                {
                    string fileName = Path.GetFileName(scriptPath);
                    var match = Regex.Match(fileName, @"^(\d{3})_");
                    if (!match.Success)
                    {
                        Console.WriteLine($"Skipping non-numbered script: {fileName}");
                        continue;
                    }

                    int version = int.Parse(match.Groups[1].Value);
                    string scriptContent = await File.ReadAllTextAsync(scriptPath);
                    string checksum = ComputeSha256(scriptContent);

                    if (appliedMigrations.ContainsKey(version))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"[SKIPPED] {fileName} (already applied at version {version})");
                        Console.ResetColor();
                        continue;
                    }

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[APPLYING] {fileName} (Version: {version})...");
                    Console.ResetColor();

                    // Execute statements in script
                    var statements = SplitSqlStatements(scriptContent);
                    await using var tx = conn.BeginTransaction();

                    try
                    {
                        foreach (var stmt in statements)
                        {
                            if (string.IsNullOrWhiteSpace(stmt)) continue;
                            await using var cmd = new SqlCommand(stmt, conn, tx);
                            cmd.CommandTimeout = 120;
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // Ensure SchemaVersion has this record
                        const string recordSql = @"
                            IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = @Version)
                            BEGIN
                                INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
                                VALUES (@Version, @Name, SYSUTCDATETIME(), @Checksum);
                            END
                            ELSE
                            BEGIN
                                UPDATE dbo.SchemaVersion
                                SET AppliedAt = SYSUTCDATETIME(), Checksum = @Checksum
                                WHERE Version = @Version;
                            END";

                        await using (var recordCmd = new SqlCommand(recordSql, conn, tx))
                        {
                            recordCmd.Parameters.AddWithValue("@Version", version);
                            recordCmd.Parameters.AddWithValue("@Name", fileName);
                            recordCmd.Parameters.AddWithValue("@Checksum", checksum);
                            await recordCmd.ExecuteNonQueryAsync();
                        }

                        await tx.CommitAsync();

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[SUCCESS] {fileName} applied (Checksum: {checksum})");
                        Console.ResetColor();
                        appliedCount++;
                    }
                    catch (Exception ex)
                    {
                        await tx.RollbackAsync();
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("=================================================");
                        Console.WriteLine($"MIGRATION FAILED: {fileName}");
                        Console.WriteLine($"Error: {ex.Message}");
                        Console.WriteLine("=================================================");
                        Console.ResetColor();
                        return 2;
                    }
                }

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Migration run complete. {appliedCount} new migration(s) applied.");
                Console.ResetColor();
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Database connection or execution failed: {ex.Message}");
                Console.ResetColor();
                return 3;
            }
        }

        private static string? FindMigrationsDirectory()
        {
            string current = Directory.GetCurrentDirectory();
            for (int i = 0; i < 5; i++)
            {
                string candidate = Path.Combine(current, "db", "migrations");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            return null;
        }

        private static async Task EnsureSchemaVersionTableAsync(SqlConnection conn)
        {
            const string sql = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SchemaVersion' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    CREATE TABLE dbo.SchemaVersion (
                        Version INT NOT NULL PRIMARY KEY,
                        Name NVARCHAR(100) NOT NULL,
                        AppliedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        Checksum NVARCHAR(64) NOT NULL
                    );
                END";

            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<Dictionary<int, string>> GetAppliedMigrationsAsync(SqlConnection conn)
        {
            var result = new Dictionary<int, string>();
            const string sql = "SELECT Version, Name FROM dbo.SchemaVersion ORDER BY Version ASC;";
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int v = reader.GetInt32(0);
                string name = reader.GetString(1);
                result[v] = name;
            }
            return result;
        }

        private static List<string> SplitSqlStatements(string script)
        {
            // Split on standalone GO statements
            var parts = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        }

        private static string ComputeSha256(string content)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n")));
            return BitConverter.ToString(bytes).Replace("-", "").ToUpperInvariant();
        }
    }
}
