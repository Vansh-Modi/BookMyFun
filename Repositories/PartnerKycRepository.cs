using System.Data;
using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IPartnerKycRepository
    {
        Task<PartnerKycBankDetails?> GetByUserIdAsync(int userId);
        Task<bool> UpsertAsync(PartnerKycBankDetails record);
        Task<bool> MarkFirstPopupShownAsync(int userId);
    }

    public class PartnerKycRepository : IPartnerKycRepository
    {
        private readonly DBHelper _db;

        public PartnerKycRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<PartnerKycBankDetails?> GetByUserIdAsync(int userId)
        {
            const string sql = @"
                SELECT TOP 1 KycId, UserId, AadhaarDocumentKey, PanDocumentKey, BankProofDocumentKey,
                       AccountHolderName, BankAccountNumberEncrypted, BankAccountLast4, IfscCode,
                       KycStatus, FirstPopupShownAt, SubmittedAt, UpdatedAt
                FROM PartnerKycBankDetails
                WHERE UserId = @UserId";

            return await _db.ExecuteReaderSingleAsync(sql, MapRecord, new SqlParameter("@UserId", userId));
        }

        public async Task<bool> UpsertAsync(PartnerKycBankDetails record)
        {
            const string sql = @"
                IF EXISTS (SELECT 1 FROM PartnerKycBankDetails WHERE UserId = @UserId)
                BEGIN
                    UPDATE PartnerKycBankDetails
                    SET AadhaarDocumentKey         = @AadhaarDocumentKey,
                        PanDocumentKey             = @PanDocumentKey,
                        BankProofDocumentKey       = @BankProofDocumentKey,
                        AccountHolderName          = @AccountHolderName,
                        BankAccountNumberEncrypted = @BankAccountNumberEncrypted,
                        BankAccountLast4           = @BankAccountLast4,
                        IfscCode                   = @IfscCode,
                        KycStatus                  = @KycStatus,
                        SubmittedAt                = COALESCE(@SubmittedAt, SubmittedAt),
                        UpdatedAt                  = GETUTCDATE()
                    WHERE UserId = @UserId;
                END
                ELSE
                BEGIN
                    INSERT INTO PartnerKycBankDetails
                        (UserId, AadhaarDocumentKey, PanDocumentKey, BankProofDocumentKey,
                         AccountHolderName, BankAccountNumberEncrypted, BankAccountLast4, IfscCode,
                         KycStatus, FirstPopupShownAt, SubmittedAt, UpdatedAt)
                    VALUES
                        (@UserId, @AadhaarDocumentKey, @PanDocumentKey, @BankProofDocumentKey,
                         @AccountHolderName, @BankAccountNumberEncrypted, @BankAccountLast4, @IfscCode,
                         @KycStatus, @FirstPopupShownAt, @SubmittedAt, GETUTCDATE());
                END";

            int rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@UserId", record.UserId),
                new SqlParameter("@AadhaarDocumentKey", DBHelper.ToDbValue(record.AadhaarDocumentKey)),
                new SqlParameter("@PanDocumentKey", DBHelper.ToDbValue(record.PanDocumentKey)),
                new SqlParameter("@BankProofDocumentKey", DBHelper.ToDbValue(record.BankProofDocumentKey)),
                new SqlParameter("@AccountHolderName", DBHelper.ToDbValue(record.AccountHolderName)),
                new SqlParameter("@BankAccountNumberEncrypted", DBHelper.ToDbValue(record.BankAccountNumberEncrypted)),
                new SqlParameter("@BankAccountLast4", DBHelper.ToDbValue(record.BankAccountLast4)),
                new SqlParameter("@IfscCode", DBHelper.ToDbValue(record.IfscCode)),
                new SqlParameter("@KycStatus", record.KycStatus),
                new SqlParameter("@FirstPopupShownAt", DBHelper.ToDbValue(record.FirstPopupShownAt)),
                new SqlParameter("@SubmittedAt", DBHelper.ToDbValue(record.SubmittedAt)));

            return rows > 0;
        }

        public async Task<bool> MarkFirstPopupShownAsync(int userId)
        {
            const string sql = @"
                IF EXISTS (SELECT 1 FROM PartnerKycBankDetails WHERE UserId = @UserId)
                BEGIN
                    UPDATE PartnerKycBankDetails
                    SET FirstPopupShownAt = COALESCE(FirstPopupShownAt, GETUTCDATE()),
                        UpdatedAt = GETUTCDATE()
                    WHERE UserId = @UserId;
                END
                ELSE
                BEGIN
                    INSERT INTO PartnerKycBankDetails (UserId, KycStatus, FirstPopupShownAt, UpdatedAt)
                    VALUES (@UserId, 'Incomplete', GETUTCDATE(), GETUTCDATE());
                END";

            int rows = await _db.ExecuteNonQueryAsync(sql, new SqlParameter("@UserId", userId));
            return rows > 0;
        }

        private static PartnerKycBankDetails MapRecord(IDataRecord r)
        {
            return new PartnerKycBankDetails
            {
                KycId = Convert.ToInt32(r["KycId"]),
                UserId = Convert.ToInt32(r["UserId"]),
                AadhaarDocumentKey = r["AadhaarDocumentKey"] == DBNull.Value ? null : r["AadhaarDocumentKey"].ToString(),
                PanDocumentKey = r["PanDocumentKey"] == DBNull.Value ? null : r["PanDocumentKey"].ToString(),
                BankProofDocumentKey = r["BankProofDocumentKey"] == DBNull.Value ? null : r["BankProofDocumentKey"].ToString(),
                AccountHolderName = r["AccountHolderName"] == DBNull.Value ? null : r["AccountHolderName"].ToString(),
                BankAccountNumberEncrypted = r["BankAccountNumberEncrypted"] == DBNull.Value ? null : r["BankAccountNumberEncrypted"].ToString(),
                BankAccountLast4 = r["BankAccountLast4"] == DBNull.Value ? null : r["BankAccountLast4"].ToString(),
                IfscCode = r["IfscCode"] == DBNull.Value ? null : r["IfscCode"].ToString(),
                KycStatus = r["KycStatus"]?.ToString() ?? "Incomplete",
                FirstPopupShownAt = r["FirstPopupShownAt"] == DBNull.Value ? null : Convert.ToDateTime(r["FirstPopupShownAt"]),
                SubmittedAt = r["SubmittedAt"] == DBNull.Value ? null : Convert.ToDateTime(r["SubmittedAt"]),
                UpdatedAt = r["UpdatedAt"] == DBNull.Value ? DateTime.UtcNow : Convert.ToDateTime(r["UpdatedAt"])
            };
        }
    }
}
