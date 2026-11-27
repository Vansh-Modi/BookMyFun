using System.Data;
using BookMyFun.Data;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IPartnerKycRepository
    {
        Task<PartnerKycBankDetails?> GetByUserIdAsync(int userId);
        Task<bool> UpsertAsync(PartnerKycBankDetails record);
        Task<bool> MarkFirstPopupShownAsync(int userId);
        Task<List<AdminPartnerKycItemViewModel>> GetAllPartnerKycAsync(string? status = null)
            => Task.FromResult(new List<AdminPartnerKycItemViewModel>());
        Task<AdminPartnerKycDetailViewModel?> GetPartnerKycDetailAsync(int userId)
            => Task.FromResult<AdminPartnerKycDetailViewModel?>(null);
        Task<bool> ReviewAsync(int partnerUserId, string newStatus, int adminUserId, string? reason, string? clientIp)
            => Task.FromResult(false);
        Task LogAuditAsync(int adminUserId, int partnerUserId, string action, string? documentType, string? reason, string? clientIp)
            => Task.CompletedTask;
        Task<List<PartnerKycAuditLog>> GetAuditLogsForPartnerAsync(int partnerUserId)
            => Task.FromResult(new List<PartnerKycAuditLog>());
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
                SELECT TOP 1 *
                FROM PartnerKycBankDetails
                WHERE UserId = @UserId";

            return await _db.ExecuteReaderSingleAsync(sql, MapRecord, new SqlParameter("@UserId", userId));
        }

        public async Task<bool> UpsertAsync(PartnerKycBankDetails record)
        {
            const string sql = @"
                IF EXISTS (SELECT 1 FROM PartnerKycBankDetails WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId)
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
                        RejectionReason            = NULL,
                        ReviewedByUserId           = NULL,
                        ReviewedAt                 = NULL,
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

            await using var conn = _db.CreateConnection();
            await conn.OpenAsync();
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
            try
            {
                await using var cmd = new SqlCommand(sql, conn, tx);
                cmd.Parameters.AddRange(new[]
                {
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
                    new SqlParameter("@SubmittedAt", DBHelper.ToDbValue(record.SubmittedAt))
                });

                int rows = await cmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();
                return rows > 0;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
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

        public async Task<List<AdminPartnerKycItemViewModel>> GetAllPartnerKycAsync(string? status = null)
        {
            const string sql = @"
                SELECT k.KycId, k.UserId, u.FullName, u.Email, u.Phone, u.Role,
                       k.KycStatus, k.SubmittedAt, k.ReviewedAt, k.UpdatedAt
                FROM PartnerKycBankDetails k
                INNER JOIN Users u ON k.UserId = u.UserID
                WHERE (@Status IS NULL OR @Status = '' OR @Status = 'All' OR k.KycStatus = @Status)
                ORDER BY CASE
                    WHEN k.KycStatus = 'Submitted' THEN 0
                    WHEN k.KycStatus = 'Rejected' THEN 1
                    WHEN k.KycStatus = 'Verified' THEN 2
                    ELSE 3
                END,
                COALESCE(k.SubmittedAt, k.UpdatedAt) DESC";

            return await _db.ExecuteReaderListAsync(sql, r => new AdminPartnerKycItemViewModel
            {
                KycId = Convert.ToInt32(r["KycId"]),
                UserId = Convert.ToInt32(r["UserId"]),
                FullName = r["FullName"]?.ToString() ?? string.Empty,
                Email = r["Email"]?.ToString() ?? string.Empty,
                Phone = r["Phone"] == DBNull.Value ? null : r["Phone"].ToString(),
                Role = r["Role"]?.ToString() ?? string.Empty,
                KycStatus = r["KycStatus"]?.ToString() ?? "Incomplete",
                SubmittedAt = r["SubmittedAt"] == DBNull.Value ? null : Convert.ToDateTime(r["SubmittedAt"]),
                ReviewedAt = r["ReviewedAt"] == DBNull.Value ? null : Convert.ToDateTime(r["ReviewedAt"]),
                UpdatedAt = r["UpdatedAt"] == DBNull.Value ? DateTime.UtcNow : Convert.ToDateTime(r["UpdatedAt"])
            }, new SqlParameter("@Status", DBHelper.ToDbValue(status)));
        }

        public async Task<AdminPartnerKycDetailViewModel?> GetPartnerKycDetailAsync(int userId)
        {
            const string sql = @"
                SELECT TOP 1 k.UserId, u.FullName, u.Email, u.Phone, u.Role,
                       k.KycStatus, k.RejectionReason, k.ReviewedAt, k.SubmittedAt,
                       k.AadhaarDocumentKey, k.PanDocumentKey, k.BankProofDocumentKey,
                       k.AccountHolderName, k.IfscCode, k.BankAccountLast4,
                       rev.FullName AS ReviewedByName
                FROM PartnerKycBankDetails k
                INNER JOIN Users u ON k.UserId = u.UserID
                LEFT JOIN Users rev ON k.ReviewedByUserId = rev.UserID
                WHERE k.UserId = @UserId";

            return await _db.ExecuteReaderSingleAsync(sql, r => new AdminPartnerKycDetailViewModel
            {
                UserId = Convert.ToInt32(r["UserId"]),
                FullName = r["FullName"]?.ToString() ?? string.Empty,
                Email = r["Email"]?.ToString() ?? string.Empty,
                Phone = r["Phone"] == DBNull.Value ? null : r["Phone"].ToString(),
                Role = r["Role"]?.ToString() ?? string.Empty,
                KycStatus = r["KycStatus"]?.ToString() ?? "Incomplete",
                RejectionReason = r["RejectionReason"] == DBNull.Value ? null : r["RejectionReason"].ToString(),
                ReviewedByName = r["ReviewedByName"] == DBNull.Value ? null : r["ReviewedByName"].ToString(),
                ReviewedAt = r["ReviewedAt"] == DBNull.Value ? null : Convert.ToDateTime(r["ReviewedAt"]),
                SubmittedAt = r["SubmittedAt"] == DBNull.Value ? null : Convert.ToDateTime(r["SubmittedAt"]),
                HasAadhaar = r["AadhaarDocumentKey"] != DBNull.Value && !string.IsNullOrWhiteSpace(r["AadhaarDocumentKey"].ToString()),
                HasPan = r["PanDocumentKey"] != DBNull.Value && !string.IsNullOrWhiteSpace(r["PanDocumentKey"].ToString()),
                HasBankProof = r["BankProofDocumentKey"] != DBNull.Value && !string.IsNullOrWhiteSpace(r["BankProofDocumentKey"].ToString()),
                AccountHolderName = r["AccountHolderName"] == DBNull.Value ? null : r["AccountHolderName"].ToString(),
                IfscCode = r["IfscCode"] == DBNull.Value ? null : r["IfscCode"].ToString(),
                MaskedBankAccount = r["BankAccountLast4"] == DBNull.Value || string.IsNullOrWhiteSpace(r["BankAccountLast4"].ToString())
                    ? null
                    : $"••••••••{r["BankAccountLast4"]}"
            }, new SqlParameter("@UserId", userId));
        }

        public async Task<bool> ReviewAsync(int partnerUserId, string newStatus, int adminUserId, string? reason, string? clientIp)
        {
            const string updateSql = @"
                UPDATE PartnerKycBankDetails
                SET KycStatus = @KycStatus,
                    RejectionReason = @RejectionReason,
                    ReviewedByUserId = @AdminUserId,
                    ReviewedAt = GETUTCDATE(),
                    UpdatedAt = GETUTCDATE()
                WHERE UserId = @PartnerUserId";

            const string auditSql = @"
                INSERT INTO PartnerKycAuditLogs (AdminUserId, PartnerUserId, Action, DocumentType, Reason, ClientIp, CreatedAt)
                VALUES (@AdminUserId, @PartnerUserId, @Action, NULL, @Reason, @ClientIp, GETUTCDATE())";

            await using var conn = _db.CreateConnection();
            await conn.OpenAsync();
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
            try
            {
                int rows;
                await using (var cmd = new SqlCommand(updateSql, conn, tx))
                {
                    cmd.Parameters.AddRange(new[]
                    {
                        new SqlParameter("@KycStatus", newStatus),
                        new SqlParameter("@RejectionReason", DBHelper.ToDbValue(reason)),
                        new SqlParameter("@AdminUserId", adminUserId),
                        new SqlParameter("@PartnerUserId", partnerUserId)
                    });
                    rows = await cmd.ExecuteNonQueryAsync();
                }

                if (rows > 0)
                {
                    string action = string.Equals(newStatus, "Verified", StringComparison.OrdinalIgnoreCase) ? "Approve" : "Reject";
                    await using var auditCmd = new SqlCommand(auditSql, conn, tx);
                    auditCmd.Parameters.AddRange(new[]
                    {
                        new SqlParameter("@AdminUserId", adminUserId),
                        new SqlParameter("@PartnerUserId", partnerUserId),
                        new SqlParameter("@Action", action),
                        new SqlParameter("@Reason", DBHelper.ToDbValue(reason)),
                        new SqlParameter("@ClientIp", DBHelper.ToDbValue(clientIp))
                    });
                    await auditCmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                return rows > 0;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task LogAuditAsync(int adminUserId, int partnerUserId, string action, string? documentType, string? reason, string? clientIp)
        {
            const string sql = @"
                INSERT INTO PartnerKycAuditLogs (AdminUserId, PartnerUserId, Action, DocumentType, Reason, ClientIp, CreatedAt)
                VALUES (@AdminUserId, @PartnerUserId, @Action, @DocumentType, @Reason, @ClientIp, GETUTCDATE())";

            await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@AdminUserId", adminUserId),
                new SqlParameter("@PartnerUserId", partnerUserId),
                new SqlParameter("@Action", action),
                new SqlParameter("@DocumentType", DBHelper.ToDbValue(documentType)),
                new SqlParameter("@Reason", DBHelper.ToDbValue(reason)),
                new SqlParameter("@ClientIp", DBHelper.ToDbValue(clientIp)));
        }

        public async Task<List<PartnerKycAuditLog>> GetAuditLogsForPartnerAsync(int partnerUserId)
        {
            const string sql = @"
                SELECT a.AuditId, a.AdminUserId, u.FullName AS AdminName, a.PartnerUserId,
                       a.Action, a.DocumentType, a.Reason, a.ClientIp, a.CreatedAt
                FROM PartnerKycAuditLogs a
                LEFT JOIN Users u ON a.AdminUserId = u.UserID
                WHERE a.PartnerUserId = @PartnerUserId
                ORDER BY a.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, r => new PartnerKycAuditLog
            {
                AuditId = Convert.ToInt32(r["AuditId"]),
                AdminUserId = Convert.ToInt32(r["AdminUserId"]),
                AdminName = r["AdminName"] == DBNull.Value ? null : r["AdminName"].ToString(),
                PartnerUserId = Convert.ToInt32(r["PartnerUserId"]),
                Action = r["Action"]?.ToString() ?? string.Empty,
                DocumentType = r["DocumentType"] == DBNull.Value ? null : r["DocumentType"].ToString(),
                Reason = r["Reason"] == DBNull.Value ? null : r["Reason"].ToString(),
                ClientIp = r["ClientIp"] == DBNull.Value ? null : r["ClientIp"].ToString(),
                CreatedAt = Convert.ToDateTime(r["CreatedAt"])
            }, new SqlParameter("@PartnerUserId", partnerUserId));
        }

        private static bool HasColumn(IDataRecord r, string columnName)
        {
            for (int i = 0; i < r.FieldCount; i++)
            {
                if (string.Equals(r.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
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
                RejectionReason = HasColumn(r, "RejectionReason") && r["RejectionReason"] != DBNull.Value ? r["RejectionReason"].ToString() : null,
                ReviewedByUserId = HasColumn(r, "ReviewedByUserId") && r["ReviewedByUserId"] != DBNull.Value ? Convert.ToInt32(r["ReviewedByUserId"]) : null,
                ReviewedAt = HasColumn(r, "ReviewedAt") && r["ReviewedAt"] != DBNull.Value ? Convert.ToDateTime(r["ReviewedAt"]) : null,
                FirstPopupShownAt = r["FirstPopupShownAt"] == DBNull.Value ? null : Convert.ToDateTime(r["FirstPopupShownAt"]),
                SubmittedAt = r["SubmittedAt"] == DBNull.Value ? null : Convert.ToDateTime(r["SubmittedAt"]),
                UpdatedAt = r["UpdatedAt"] == DBNull.Value ? DateTime.UtcNow : Convert.ToDateTime(r["UpdatedAt"])
            };
        }
    }
}
