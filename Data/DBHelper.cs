using System.Data;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Data
{
    public class DBHelper
    {
        private readonly string _connectionString;

        public DBHelper(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        public SqlConnection CreateConnection()
        {
            return new SqlConnection(_connectionString);
        }

        public async Task<int> ExecuteNonQueryAsync(string sql, params SqlParameter[] parameters)
        {
            await using var conn = CreateConnection();
            await using var cmd = new SqlCommand(sql, conn);
            if (parameters != null && parameters.Length > 0)
            {
                cmd.Parameters.AddRange(parameters);
            }
            await conn.OpenAsync();
            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<object?> ExecuteScalarAsync(string sql, params SqlParameter[] parameters)
        {
            await using var conn = CreateConnection();
            await using var cmd = new SqlCommand(sql, conn);
            if (parameters != null && parameters.Length > 0)
            {
                cmd.Parameters.AddRange(parameters);
            }
            await conn.OpenAsync();
            var result = await cmd.ExecuteScalarAsync();
            return result == DBNull.Value ? null : result;
        }

        public async Task<DataTable> ExecuteDataTableAsync(string sql, params SqlParameter[] parameters)
        {
            await using var conn = CreateConnection();
            await using var cmd = new SqlCommand(sql, conn);
            if (parameters != null && parameters.Length > 0)
            {
                cmd.Parameters.AddRange(parameters);
            }
            await conn.OpenAsync();
            await using var reader = await cmd.ExecuteReaderAsync();
            var dt = new DataTable();
            dt.Load(reader);
            return dt;
        }

        public async Task<List<T>> ExecuteReaderListAsync<T>(string sql, Func<SqlDataReader, T> mapFunction, params SqlParameter[] parameters)
        {
            var list = new List<T>();
            await using var conn = CreateConnection();
            await using var cmd = new SqlCommand(sql, conn);
            if (parameters != null && parameters.Length > 0)
            {
                cmd.Parameters.AddRange(parameters);
            }
            await conn.OpenAsync();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(mapFunction(reader));
            }
            return list;
        }

        public async Task<T?> ExecuteReaderSingleAsync<T>(string sql, Func<SqlDataReader, T> mapFunction, params SqlParameter[] parameters)
        {
            await using var conn = CreateConnection();
            await using var cmd = new SqlCommand(sql, conn);
            if (parameters != null && parameters.Length > 0)
            {
                cmd.Parameters.AddRange(parameters);
            }
            await conn.OpenAsync();
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return mapFunction(reader);
            }
            return default;
        }

        public static object ToDbValue(object? value) => value ?? DBNull.Value;
    }
}
