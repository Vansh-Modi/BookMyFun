using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IIdVerificationRepository
    {
        Task<int> InsertVerificationAsync(ClientIdVerification verification);
        Task<ClientIdVerification?> GetVerificationByIdAsync(int verificationId);
        Task<ClientIdVerification?> GetVerificationByClientIdAsync(int clientId);
        Task<List<ClientIdVerification>> GetAllVerificationsAsync(string? status = null);
        Task<bool> ReviewVerificationAsync(int verificationId, string status, int verifiedByUserId, string? rejectionReason = null);
    }

    public class IdVerificationRepository : IIdVerificationRepository
    {
        private readonly DBHelper _db;

        public IdVerificationRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<int> InsertVerificationAsync(ClientIdVerification verification)
        {
            const string sql = @"
                INSERT INTO ClientIdVerifications
                (ClientID, DocumentType, DocumentStorageKey, MaskedIdentifier, VerificationStatus, CreatedAt)
                OUTPUT INSERTED.VerificationID
                VALUES
                (@ClientID, @DocumentType, @DocumentStorageKey, @MaskedIdentifier, @VerificationStatus, GETDATE())";

            var result = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@ClientID", verification.ClientID),
                new SqlParameter("@DocumentType", verification.DocumentType),
                new SqlParameter("@DocumentStorageKey", verification.DocumentStorageKey),
                new SqlParameter("@MaskedIdentifier", DBHelper.ToDbValue(verification.MaskedIdentifier)),
                new SqlParameter("@VerificationStatus", verification.VerificationStatus ?? "PendingReview"));

            return result != null ? Convert.ToInt32(result) : 0;
        }

        public async Task<ClientIdVerification?> GetVerificationByIdAsync(int verificationId)
        {
            const string sql = @"
                SELECT civ.*, u.FullName AS ClientName, u.Email AS ClientEmail, staff.FullName AS VerifiedByUserName
                FROM ClientIdVerifications civ
                JOIN Users u ON civ.ClientID = u.UserID
                LEFT JOIN Users staff ON civ.VerifiedByUserID = staff.UserID
                WHERE civ.VerificationID = @VerificationID";

            return await _db.ExecuteReaderSingleAsync(sql, MapVerification, new SqlParameter("@VerificationID", verificationId));
        }

        public async Task<ClientIdVerification?> GetVerificationByClientIdAsync(int clientId)
        {
            const string sql = @"
                SELECT TOP 1 civ.*, u.FullName AS ClientName, u.Email AS ClientEmail, staff.FullName AS VerifiedByUserName
                FROM ClientIdVerifications civ
                JOIN Users u ON civ.ClientID = u.UserID
                LEFT JOIN Users staff ON civ.VerifiedByUserID = staff.UserID
                WHERE civ.ClientID = @ClientID
                ORDER BY civ.CreatedAt DESC";

            return await _db.ExecuteReaderSingleAsync(sql, MapVerification, new SqlParameter("@ClientID", clientId));
        }

        public async Task<List<ClientIdVerification>> GetAllVerificationsAsync(string? status = null)
        {
            string sql = @"
                SELECT civ.*, u.FullName AS ClientName, u.Email AS ClientEmail, staff.FullName AS VerifiedByUserName
                FROM ClientIdVerifications civ
                JOIN Users u ON civ.ClientID = u.UserID
                LEFT JOIN Users staff ON civ.VerifiedByUserID = staff.UserID";

            if (!string.IsNullOrEmpty(status))
            {
                sql += " WHERE civ.VerificationStatus = @Status ORDER BY civ.CreatedAt DESC";
                return await _db.ExecuteReaderListAsync(sql, MapVerification, new SqlParameter("@Status", status));
            }

            sql += " ORDER BY civ.CreatedAt DESC";
            return await _db.ExecuteReaderListAsync(sql, MapVerification);
        }

        public async Task<bool> ReviewVerificationAsync(int verificationId, string status, int verifiedByUserId, string? rejectionReason = null)
        {
            const string sql = @"
                UPDATE ClientIdVerifications
                SET VerificationStatus = @Status,
                    VerifiedByUserID = @VerifiedByUserID,
                    RejectionReason = @RejectionReason,
                    VerifiedAt = GETDATE()
                WHERE VerificationID = @VerificationID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@Status", status),
                new SqlParameter("@VerifiedByUserID", verifiedByUserId),
                new SqlParameter("@RejectionReason", DBHelper.ToDbValue(rejectionReason)),
                new SqlParameter("@VerificationID", verificationId));

            return rows > 0;
        }

        private static ClientIdVerification MapVerification(SqlDataReader reader)
        {
            return new ClientIdVerification
            {
                VerificationID = reader.GetInt32(reader.GetOrdinal("VerificationID")),
                ClientID = reader.GetInt32(reader.GetOrdinal("ClientID")),
                DocumentType = reader.GetString(reader.GetOrdinal("DocumentType")),
                DocumentStorageKey = reader.GetString(reader.GetOrdinal("DocumentStorageKey")),
                MaskedIdentifier = reader.IsDBNull(reader.GetOrdinal("MaskedIdentifier")) ? null : reader.GetString(reader.GetOrdinal("MaskedIdentifier")),
                VerificationStatus = reader.GetString(reader.GetOrdinal("VerificationStatus")),
                RejectionReason = reader.IsDBNull(reader.GetOrdinal("RejectionReason")) ? null : reader.GetString(reader.GetOrdinal("RejectionReason")),
                VerifiedByUserID = reader.IsDBNull(reader.GetOrdinal("VerifiedByUserID")) ? null : reader.GetInt32(reader.GetOrdinal("VerifiedByUserID")),
                VerifiedAt = reader.IsDBNull(reader.GetOrdinal("VerifiedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("VerifiedAt")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                ClientName = reader.IsDBNull(reader.GetOrdinal("ClientName")) ? null : reader.GetString(reader.GetOrdinal("ClientName")),
                ClientEmail = reader.IsDBNull(reader.GetOrdinal("ClientEmail")) ? null : reader.GetString(reader.GetOrdinal("ClientEmail")),
                VerifiedByUserName = reader.IsDBNull(reader.GetOrdinal("VerifiedByUserName")) ? null : reader.GetString(reader.GetOrdinal("VerifiedByUserName"))
            };
        }
    }
}
