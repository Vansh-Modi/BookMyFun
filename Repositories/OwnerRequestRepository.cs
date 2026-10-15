using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IOwnerRequestRepository
    {
        Task<int> CreateRequestAsync(int userId, int trialDays = 2);
        Task<OwnerRequest?> GetActiveRequestByUserIdAsync(int userId);
        Task<List<OwnerRequest>> GetAllPendingRequestsAsync();
        Task<List<OwnerRequest>> GetAllRequestsAsync();
        Task<bool> ApproveOwnerRequestAsync(int requestId, int reviewerUserId, string? notes);
        Task<bool> RejectOwnerRequestAsync(int requestId, int reviewerUserId, string? notes);
    }

    public class OwnerRequestRepository : IOwnerRequestRepository
    {
        private readonly DBHelper _db;

        public OwnerRequestRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<int> CreateRequestAsync(int userId, int trialDays = 2)
        {
            // Give 1-2 working days (default 2 days)
            const string sql = @"
                INSERT INTO OwnerRequests (UserID, RequestedAt, TrialDeadline, Status)
                OUTPUT INSERTED.RequestID
                VALUES (@UserID, GETDATE(), DATEADD(DAY, @TrialDays, GETDATE()), 'PendingEvaluation')";

            var res = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@UserID", userId),
                new SqlParameter("@TrialDays", trialDays));

            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<OwnerRequest?> GetActiveRequestByUserIdAsync(int userId)
        {
            const string sql = @"
                SELECT TOP 1 r.*, u.FullName, u.Email, u.Phone,
                       (SELECT COUNT(*) FROM Properties WHERE OwnerID = r.UserID) AS ListedPropertiesCount
                FROM OwnerRequests r
                JOIN Users u ON r.UserID = u.UserID
                WHERE r.UserID = @UserID
                ORDER BY r.RequestedAt DESC";

            return await _db.ExecuteReaderSingleAsync(sql, MapOwnerRequest, new SqlParameter("@UserID", userId));
        }

        public async Task<List<OwnerRequest>> GetAllPendingRequestsAsync()
        {
            const string sql = @"
                SELECT r.*, u.FullName, u.Email, u.Phone,
                       (SELECT COUNT(*) FROM Properties WHERE OwnerID = r.UserID) AS ListedPropertiesCount
                FROM OwnerRequests r
                JOIN Users u ON r.UserID = u.UserID
                WHERE r.Status = 'PendingEvaluation'
                ORDER BY r.RequestedAt ASC";

            return await _db.ExecuteReaderListAsync(sql, MapOwnerRequest);
        }

        public async Task<List<OwnerRequest>> GetAllRequestsAsync()
        {
            const string sql = @"
                SELECT r.*, u.FullName, u.Email, u.Phone,
                       (SELECT COUNT(*) FROM Properties WHERE OwnerID = r.UserID) AS ListedPropertiesCount
                FROM OwnerRequests r
                JOIN Users u ON r.UserID = u.UserID
                ORDER BY r.RequestedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, MapOwnerRequest);
        }

        public async Task<bool> ApproveOwnerRequestAsync(int requestId, int reviewerUserId, string? notes)
        {
            // 1. Mark request as Approved
            // 2. Ensure User role remains/is 'Owner'
            const string sql = @"
                DECLARE @TargetUserId INT;
                SELECT @TargetUserId = UserID FROM OwnerRequests WHERE RequestID = @RequestID;

                UPDATE OwnerRequests
                SET Status = 'Approved',
                    ReviewedByUserID = @ReviewerID,
                    ReviewedAt = GETDATE(),
                    AdminNotes = @Notes
                WHERE RequestID = @RequestID;

                IF @TargetUserId IS NOT NULL
                BEGIN
                    UPDATE Users SET Role = 'Owner' WHERE UserID = @TargetUserId;
                    -- Also approve their draft properties
                    UPDATE Properties SET IsApproved = 1, ListingStatus = 'Active' WHERE OwnerID = @TargetUserId;
                END";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@RequestID", requestId),
                new SqlParameter("@ReviewerID", reviewerUserId),
                new SqlParameter("@Notes", DBHelper.ToDbValue(notes)));

            return rows > 0;
        }

        public async Task<bool> RejectOwnerRequestAsync(int requestId, int reviewerUserId, string? notes)
        {
            // 1. Mark request as Rejected
            // 2. Requirement: 'if not then all that user's data will be removed and shifted to client'
            // We purge their property listings, listing payments, and demote User role back to 'Client'
            const string sql = @"
                DECLARE @TargetUserId INT;
                SELECT @TargetUserId = UserID FROM OwnerRequests WHERE RequestID = @RequestID;

                UPDATE OwnerRequests
                SET Status = 'Rejected',
                    ReviewedByUserID = @ReviewerID,
                    ReviewedAt = GETDATE(),
                    AdminNotes = @Notes
                WHERE RequestID = @RequestID;

                IF @TargetUserId IS NOT NULL
                BEGIN
                    -- Delete properties submitted during trial (cascades PropertyImages, PropertyOccasions)
                    DELETE FROM Properties WHERE OwnerID = @TargetUserId;

                    -- Delete owner subscriptions created during trial
                    DELETE FROM OwnerSubscriptions WHERE OwnerID = @TargetUserId;

                    -- Shift user role back to Client
                    UPDATE Users SET Role = 'Client' WHERE UserID = @TargetUserId;
                END";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@RequestID", requestId),
                new SqlParameter("@ReviewerID", reviewerUserId),
                new SqlParameter("@Notes", DBHelper.ToDbValue(notes)));

            return rows > 0;
        }

        private static OwnerRequest MapOwnerRequest(SqlDataReader r)
        {
            return new OwnerRequest
            {
                RequestID = r.GetInt32(r.GetOrdinal("RequestID")),
                UserID = r.GetInt32(r.GetOrdinal("UserID")),
                RequestedAt = r.GetDateTime(r.GetOrdinal("RequestedAt")),
                TrialDeadline = r.GetDateTime(r.GetOrdinal("TrialDeadline")),
                Status = r.GetString(r.GetOrdinal("Status")),
                AdminNotes = r.IsDBNull(r.GetOrdinal("AdminNotes")) ? null : r.GetString(r.GetOrdinal("AdminNotes")),
                ReviewedByUserID = r.IsDBNull(r.GetOrdinal("ReviewedByUserID")) ? null : r.GetInt32(r.GetOrdinal("ReviewedByUserID")),
                ReviewedAt = r.IsDBNull(r.GetOrdinal("ReviewedAt")) ? null : r.GetDateTime(r.GetOrdinal("ReviewedAt")),
                FullName = r.IsDBNull(r.GetOrdinal("FullName")) ? null : r.GetString(r.GetOrdinal("FullName")),
                Email = r.IsDBNull(r.GetOrdinal("Email")) ? null : r.GetString(r.GetOrdinal("Email")),
                Phone = r.IsDBNull(r.GetOrdinal("Phone")) ? null : r.GetString(r.GetOrdinal("Phone")),
                ListedPropertiesCount = r.IsDBNull(r.GetOrdinal("ListedPropertiesCount")) ? 0 : r.GetInt32(r.GetOrdinal("ListedPropertiesCount"))
            };
        }
    }
}
