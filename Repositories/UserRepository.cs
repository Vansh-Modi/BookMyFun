using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IUserRepository
    {
        Task<User?> GetByIdAsync(int userId);
        Task<User?> GetByEmailAsync(string email);
        Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey);
        Task<List<User>> GetUsersByRoleAsync(string role);
        Task<List<User>> GetAllUsersAsync();
        Task<int> CreateUserAsync(User user);
        Task<bool> UpdateUserAsync(User user);
        Task<bool> DeleteUserAsync(int userId);
        Task<bool> ResetPasswordAsync(string email, string newPasswordHash);
        Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate);
        Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate);
        Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null);
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
            var cleanId = email?.Trim().TrimStart('#');
            bool isNumericId = int.TryParse(cleanId, out int accountId);

            const string sql = @"
                SELECT * FROM Users 
                WHERE LOWER(Email) = LOWER(@Email) 
                   OR Phone = @Email
                   OR (@IsNumeric = 1 AND UserID = @AccountID)";

            return await _db.ExecuteReaderSingleAsync(sql, MapUser, 
                new SqlParameter("@Email", email ?? string.Empty),
                new SqlParameter("@IsNumeric", isNumericId ? 1 : 0),
                new SqlParameter("@AccountID", accountId));
        }

        public async Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey)
        {
            const string sql = "SELECT * FROM Users WHERE AuthProvider = @Provider AND AuthProviderKey = @ProviderKey";
            return await _db.ExecuteReaderSingleAsync(sql, MapUser,
                new SqlParameter("@Provider", provider),
                new SqlParameter("@ProviderKey", providerKey));
        }

        public async Task<List<User>> GetUsersByRoleAsync(string role)
        {
            const string sql = "SELECT * FROM Users WHERE Role = @Role AND (IsActive = 1 OR IsActive IS NULL) ORDER BY CreatedAt DESC";
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
                INSERT INTO Users (FullName, Email, PasswordHash, Phone, Role, IsActive, CustomCommissionRate, SubscriptionTier, AuthProvider, AuthProviderKey, ProfilePictureUrl, CreatedAt)
                OUTPUT INSERTED.UserID
                VALUES (@FullName, @Email, @PasswordHash, @Phone, @Role, @IsActive, @CustomCommissionRate, @SubscriptionTier, @AuthProvider, @AuthProviderKey, @ProfilePictureUrl, GETDATE())";

            var result = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@FullName", user.FullName),
                new SqlParameter("@Email", user.Email),
                new SqlParameter("@PasswordHash", user.PasswordHash),
                new SqlParameter("@Phone", DBHelper.ToDbValue(user.Phone)),
                new SqlParameter("@Role", user.Role),
                new SqlParameter("@IsActive", user.IsActive),
                new SqlParameter("@CustomCommissionRate", DBHelper.ToDbValue(user.CustomCommissionRate)),
                new SqlParameter("@SubscriptionTier", user.SubscriptionTier ?? "Free"),
                new SqlParameter("@AuthProvider", user.AuthProvider ?? "Local"),
                new SqlParameter("@AuthProviderKey", DBHelper.ToDbValue(user.AuthProviderKey)),
                new SqlParameter("@ProfilePictureUrl", DBHelper.ToDbValue(user.ProfilePictureUrl)));

            return result != null ? Convert.ToInt32(result) : 0;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            const string sql = @"
                UPDATE Users 
                SET FullName = @FullName, 
                    Phone = @Phone, 
                    Role = @Role,
                    IsActive = @IsActive,
                    CustomCommissionRate = @CustomCommissionRate,
                    SubscriptionTier = @SubscriptionTier
                WHERE UserID = @UserID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@FullName", user.FullName),
                new SqlParameter("@Phone", DBHelper.ToDbValue(user.Phone)),
                new SqlParameter("@Role", user.Role),
                new SqlParameter("@IsActive", user.IsActive),
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

        public async Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate)
        {
            const string sql = @"
                UPDATE Users
                SET CustomCommissionRate = @CustomCommissionRate
                WHERE UserID = @UserID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@CustomCommissionRate", DBHelper.ToDbValue(customCommissionRate)),
                new SqlParameter("@UserID", userId));

            return rows > 0;
        }

        public async Task<bool> DeleteUserAsync(int userId)
        {
            try
            {
                const string sql = "DELETE FROM Users WHERE UserID = @UserID";
                var rows = await _db.ExecuteNonQueryAsync(sql, new SqlParameter("@UserID", userId));
                return rows > 0;
            }
            catch
            {
                // Safe soft-deactivation when child records (properties/bookings) exist
                const string deactivateSql = "UPDATE Users SET IsActive = 0 WHERE UserID = @UserID";
                var rows = await _db.ExecuteNonQueryAsync(deactivateSql, new SqlParameter("@UserID", userId));
                return rows > 0;
            }
        }

        public async Task<bool> ResetPasswordAsync(string email, string newPasswordHash)
        {
            const string sql = "UPDATE Users SET PasswordHash = @PasswordHash WHERE Email = @Email";
            var rows = await _db.ExecuteNonQueryAsync(sql, 
                new SqlParameter("@PasswordHash", newPasswordHash),
                new SqlParameter("@Email", email));
            return rows > 0;
        }

        public async Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null)
        {
            const string sql = @"
                UPDATE Users 
                SET AuthProvider = @Provider,
                    AuthProviderKey = @ProviderKey,
                    ProfilePictureUrl = COALESCE(@ProfilePic, ProfilePictureUrl)
                WHERE UserID = @UserID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@Provider", provider),
                new SqlParameter("@ProviderKey", providerKey),
                new SqlParameter("@ProfilePic", DBHelper.ToDbValue(profilePic)),
                new SqlParameter("@UserID", userId));

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

            int activeOrdinal = -1;
            try { activeOrdinal = reader.GetOrdinal("IsActive"); } catch { }
            if (activeOrdinal >= 0 && !reader.IsDBNull(activeOrdinal))
            {
                user.IsActive = reader.GetBoolean(activeOrdinal);
            }

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

            int areaOrdinal = -1;
            try { areaOrdinal = reader.GetOrdinal("ServiceAreas"); } catch { }
            if (areaOrdinal >= 0 && !reader.IsDBNull(areaOrdinal))
            {
                user.ServiceAreas = reader.GetString(areaOrdinal);
            }

            int providerOrdinal = -1;
            try { providerOrdinal = reader.GetOrdinal("AuthProvider"); } catch { }
            if (providerOrdinal >= 0 && !reader.IsDBNull(providerOrdinal))
            {
                user.AuthProvider = reader.GetString(providerOrdinal);
            }

            int keyOrdinal = -1;
            try { keyOrdinal = reader.GetOrdinal("AuthProviderKey"); } catch { }
            if (keyOrdinal >= 0 && !reader.IsDBNull(keyOrdinal))
            {
                user.AuthProviderKey = reader.GetString(keyOrdinal);
            }

            int picOrdinal = -1;
            try { picOrdinal = reader.GetOrdinal("ProfilePictureUrl"); } catch { }
            if (picOrdinal >= 0 && !reader.IsDBNull(picOrdinal))
            {
                user.ProfilePictureUrl = reader.GetString(picOrdinal);
            }

            return user;
        }
    }
}
