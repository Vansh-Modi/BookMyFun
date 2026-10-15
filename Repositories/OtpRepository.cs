using BookMyFun.Data;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IOtpRepository
    {
        Task<bool> SaveOtpAsync(string contact, string otp, string purpose, int expiryMinutes = 10);
        Task<bool> ValidateOtpAsync(string contact, string otp, string purpose);
    }

    public class OtpRepository : IOtpRepository
    {
        private readonly DBHelper _db;

        public OtpRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<bool> SaveOtpAsync(string contact, string otp, string purpose, int expiryMinutes = 10)
        {
            // Invalidate any existing unused OTPs for this contact & purpose
            const string invalidateSql = @"
                UPDATE OtpVerifications 
                SET IsUsed = 1 
                WHERE Contact = @Contact AND Purpose = @Purpose AND IsUsed = 0";

            await _db.ExecuteNonQueryAsync(invalidateSql,
                new SqlParameter("@Contact", contact),
                new SqlParameter("@Purpose", purpose));

            const string insertSql = @"
                INSERT INTO OtpVerifications (Contact, OtpCode, Purpose, ExpiresAt, IsUsed, CreatedAt)
                VALUES (@Contact, @OtpCode, @Purpose, DATEADD(MINUTE, @ExpiryMinutes, GETDATE()), 0, GETDATE())";

            var rows = await _db.ExecuteNonQueryAsync(insertSql,
                new SqlParameter("@Contact", contact),
                new SqlParameter("@OtpCode", otp),
                new SqlParameter("@Purpose", purpose),
                new SqlParameter("@ExpiryMinutes", expiryMinutes));

            return rows > 0;
        }

        public async Task<bool> ValidateOtpAsync(string contact, string otp, string purpose)
        {
            const string selectSql = @"
                SELECT TOP 1 OtpID 
                FROM OtpVerifications 
                WHERE Contact = @Contact 
                  AND OtpCode = @OtpCode 
                  AND Purpose = @Purpose 
                  AND IsUsed = 0 
                  AND ExpiresAt >= GETDATE()
                ORDER BY CreatedAt DESC";

            var result = await _db.ExecuteScalarAsync(selectSql,
                new SqlParameter("@Contact", contact),
                new SqlParameter("@OtpCode", otp),
                new SqlParameter("@Purpose", purpose));

            if (result != null)
            {
                int otpId = Convert.ToInt32(result);
                // Mark OTP as used
                await _db.ExecuteNonQueryAsync("UPDATE OtpVerifications SET IsUsed = 1 WHERE OtpID = @OtpID",
                    new SqlParameter("@OtpID", otpId));
                return true;
            }

            return false;
        }
    }
}
