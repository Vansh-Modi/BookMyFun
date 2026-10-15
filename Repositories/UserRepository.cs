using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IUserRepository
    {
        Task<User?> GetByIdAsync(int userId);
        Task<User?> GetByEmailAsync(string email);
        Task<List<User>> GetUsersByRoleAsync(string role);
        Task<List<User>> GetAllUsersAsync();
        Task<int> CreateUserAsync(User user);
        Task<bool> UpdateUserAsync(User user);
        Task<bool> DeleteUserAsync(int userId);
        Task<bool> ResetPasswordAsync(string email, string newPasswordHash);
        Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate);
    }

    public class UserRepository : IUserRepository
    {
        private readonly DBHelper _db;

        public UserRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<User?> GetByIdAsync(int userId)
        {
            const string sql = "SELECT * FROM Users WHERE UserID = @UserID";
            return await _db.ExecuteReaderSingleAsync(sql, MapUser, new SqlParameter("@UserID", userId));
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            const string sql = "SELECT * FROM Users WHERE Email = @Email";
            return await _db.ExecuteReaderSingleAsync(sql, MapUser, new SqlParameter("@Email", email));
        }

        public async Task<List<User>> GetUsersByRoleAsync(string role)
        {
            const string sql = "SELECT * FROM Users WHERE Role = @Role ORDER BY CreatedAt DESC";
            return await _db.ExecuteReaderListAsync(sql, MapUser, new SqlParameter("@Role", role));
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            const string sql = "SELECT * FROM Users ORDER BY CreatedAt DESC";
            return await _db.ExecuteReaderListAsync(sql, MapUser);
        }

        public async Task<int> CreateUserAsync(User user)
        {
            const string sql = @"
                INSERT INTO Users (FullName, Email, PasswordHash, Phone, Role, CustomCommissionRate, SubscriptionTier, CreatedAt)
                OUTPUT INSERTED.UserID
                VALUES (@FullName, @Email, @PasswordHash, @Phone, @Role, @CustomCommissionRate, @SubscriptionTier, GETDATE())";

            var result = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@FullName", user.FullName),
                new SqlParameter("@Email", user.Email),
                new SqlParameter("@PasswordHash", user.PasswordHash),
                new SqlParameter("@Phone", DBHelper.ToDbValue(user.Phone)),
                new SqlParameter("@Role", user.Role),
                new SqlParameter("@CustomCommissionRate", DBHelper.ToDbValue(user.CustomCommissionRate)),
                new SqlParameter("@SubscriptionTier", user.SubscriptionTier ?? "Free"));

            return result != null ? Convert.ToInt32(result) : 0;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            const string sql = @"
                UPDATE Users 
                SET FullName = @FullName, 
                    Phone = @Phone, 
                    Role = @Role,
                    CustomCommissionRate = @CustomCommissionRate,
                    SubscriptionTier = @SubscriptionTier
                WHERE UserID = @UserID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@FullName", user.FullName),
                new SqlParameter("@Phone", DBHelper.ToDbValue(user.Phone)),
                new SqlParameter("@Role", user.Role),
                new SqlParameter("@CustomCommissionRate", DBHelper.ToDbValue(user.CustomCommissionRate)),
                new SqlParameter("@SubscriptionTier", user.SubscriptionTier ?? "Free"),
                new SqlParameter("@UserID", user.UserID));

            return rows > 0;
        }

        public async Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate)
        {
            const string sql = @"
                UPDATE Users
                SET SubscriptionTier = @SubscriptionTier,
                    CustomCommissionRate = @CustomCommissionRate
                WHERE UserID = @UserID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@SubscriptionTier", subscriptionTier),
                new SqlParameter("@CustomCommissionRate", DBHelper.ToDbValue(customCommissionRate)),
                new SqlParameter("@UserID", userId));

            return rows > 0;
        }

        public async Task<bool> DeleteUserAsync(int userId)
        {
            const string sql = "DELETE FROM Users WHERE UserID = @UserID";
            var rows = await _db.ExecuteNonQueryAsync(sql, new SqlParameter("@UserID", userId));
            return rows > 0;
        }

        public async Task<bool> ResetPasswordAsync(string email, string newPasswordHash)
        {
            const string sql = "UPDATE Users SET PasswordHash = @PasswordHash WHERE Email = @Email";
            var rows = await _db.ExecuteNonQueryAsync(sql, 
                new SqlParameter("@PasswordHash", newPasswordHash),
                new SqlParameter("@Email", email));
            return rows > 0;
        }

        private static User MapUser(SqlDataReader reader)
        {
            var user = new User
            {
                UserID = reader.GetInt32(reader.GetOrdinal("UserID")),
                FullName = reader.GetString(reader.GetOrdinal("FullName")),
                Email = reader.GetString(reader.GetOrdinal("Email")),
                PasswordHash = reader.GetString(reader.GetOrdinal("PasswordHash")),
                Phone = reader.IsDBNull(reader.GetOrdinal("Phone")) ? null : reader.GetString(reader.GetOrdinal("Phone")),
                Role = reader.GetString(reader.GetOrdinal("Role")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
            };

            int commOrdinal = -1;
            try { commOrdinal = reader.GetOrdinal("CustomCommissionRate"); } catch { }
            if (commOrdinal >= 0 && !reader.IsDBNull(commOrdinal))
            {
                user.CustomCommissionRate = reader.GetDecimal(commOrdinal);
            }

            int tierOrdinal = -1;
            try { tierOrdinal = reader.GetOrdinal("SubscriptionTier"); } catch { }
            if (tierOrdinal >= 0 && !reader.IsDBNull(tierOrdinal))
            {
                user.SubscriptionTier = reader.GetString(tierOrdinal);
            }

            return user;
        }
    }
}
