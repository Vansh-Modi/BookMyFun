using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IDamageClaimRepository
    {
        Task<int> InsertClaimAsync(DamageClaim claim);
        Task<DamageClaim?> GetClaimByIdAsync(int claimId);
        Task<List<DamageClaim>> GetClaimsByOwnerIdAsync(int ownerId);
        Task<List<DamageClaim>> GetClaimsByBookingIdAsync(int bookingId);
        Task<List<DamageClaim>> GetAllClaimsAsync(string? status = null);
        Task<bool> AdjudicateClaimAsync(int claimId, string status, decimal? adjudicatedAmount, int staffUserId, string? notes);
    }

    public class DamageClaimRepository : IDamageClaimRepository
    {
        private readonly DBHelper _db;

        public DamageClaimRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<int> InsertClaimAsync(DamageClaim claim)
        {
            const string sql = @"
                INSERT INTO DamageClaims
                (BookingID, PropertyID, OwnerID, ClaimAmount, ItemizedDescription, EvidenceMediaUrls, Status, CreatedAt)
                OUTPUT INSERTED.ClaimID
                VALUES
                (@BookingID, @PropertyID, @OwnerID, @ClaimAmount, @ItemizedDescription, @EvidenceMediaUrls, @Status, GETDATE())";

            var result = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@BookingID", claim.BookingID),
                new SqlParameter("@PropertyID", claim.PropertyID),
                new SqlParameter("@OwnerID", claim.OwnerID),
                new SqlParameter("@ClaimAmount", claim.ClaimAmount),
                new SqlParameter("@ItemizedDescription", claim.ItemizedDescription),
                new SqlParameter("@EvidenceMediaUrls", claim.EvidenceMediaUrls),
                new SqlParameter("@Status", claim.Status ?? "UnderReview"));

            return result != null ? Convert.ToInt32(result) : 0;
        }

        public async Task<DamageClaim?> GetClaimByIdAsync(int claimId)
        {
            const string sql = @"
                SELECT dc.*, p.Title AS PropertyTitle, o.FullName AS OwnerName, c.FullName AS ClientName,
                       b.SecurityDepositAmount, staff.FullName AS AdjudicatedByUserName
                FROM DamageClaims dc
                JOIN Properties p ON dc.PropertyID = p.PropertyID
                JOIN Users o ON dc.OwnerID = o.UserID
                JOIN Bookings b ON dc.BookingID = b.BookingID
                JOIN Users c ON b.ClientID = c.UserID
                LEFT JOIN Users staff ON dc.AdjudicatedByUserID = staff.UserID
                WHERE dc.ClaimID = @ClaimID";

            return await _db.ExecuteReaderSingleAsync(sql, MapClaim, new SqlParameter("@ClaimID", claimId));
        }

        public async Task<List<DamageClaim>> GetClaimsByOwnerIdAsync(int ownerId)
        {
            const string sql = @"
                SELECT dc.*, p.Title AS PropertyTitle, o.FullName AS OwnerName, c.FullName AS ClientName,
                       b.SecurityDepositAmount, staff.FullName AS AdjudicatedByUserName
                FROM DamageClaims dc
                JOIN Properties p ON dc.PropertyID = p.PropertyID
                JOIN Users o ON dc.OwnerID = o.UserID
                JOIN Bookings b ON dc.BookingID = b.BookingID
                JOIN Users c ON b.ClientID = c.UserID
                LEFT JOIN Users staff ON dc.AdjudicatedByUserID = staff.UserID
                WHERE dc.OwnerID = @OwnerID
                ORDER BY dc.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, MapClaim, new SqlParameter("@OwnerID", ownerId));
        }

        public async Task<List<DamageClaim>> GetClaimsByBookingIdAsync(int bookingId)
        {
            const string sql = @"
                SELECT dc.*, p.Title AS PropertyTitle, o.FullName AS OwnerName, c.FullName AS ClientName,
                       b.SecurityDepositAmount, staff.FullName AS AdjudicatedByUserName
                FROM DamageClaims dc
                JOIN Properties p ON dc.PropertyID = p.PropertyID
                JOIN Users o ON dc.OwnerID = o.UserID
                JOIN Bookings b ON dc.BookingID = b.BookingID
                JOIN Users c ON b.ClientID = c.UserID
                LEFT JOIN Users staff ON dc.AdjudicatedByUserID = staff.UserID
                WHERE dc.BookingID = @BookingID
                ORDER BY dc.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, MapClaim, new SqlParameter("@BookingID", bookingId));
        }

        public async Task<List<DamageClaim>> GetAllClaimsAsync(string? status = null)
        {
            string sql = @"
                SELECT dc.*, p.Title AS PropertyTitle, o.FullName AS OwnerName, c.FullName AS ClientName,
                       b.SecurityDepositAmount, staff.FullName AS AdjudicatedByUserName
                FROM DamageClaims dc
                JOIN Properties p ON dc.PropertyID = p.PropertyID
                JOIN Users o ON dc.OwnerID = o.UserID
                JOIN Bookings b ON dc.BookingID = b.BookingID
                JOIN Users c ON b.ClientID = c.UserID
                LEFT JOIN Users staff ON dc.AdjudicatedByUserID = staff.UserID";

            if (!string.IsNullOrEmpty(status))
            {
                sql += " WHERE dc.Status = @Status ORDER BY dc.CreatedAt DESC";
                return await _db.ExecuteReaderListAsync(sql, MapClaim, new SqlParameter("@Status", status));
            }

            sql += " ORDER BY dc.CreatedAt DESC";
            return await _db.ExecuteReaderListAsync(sql, MapClaim);
        }

        public async Task<bool> AdjudicateClaimAsync(int claimId, string status, decimal? adjudicatedAmount, int staffUserId, string? notes)
        {
            const string sql = @"
                UPDATE DamageClaims
                SET Status = @Status,
                    AdjudicatedAmount = @AdjudicatedAmount,
                    AdjudicatedByUserID = @AdjudicatedByUserID,
                    AdjudicationNotes = @AdjudicationNotes,
                    AdjudicatedAt = GETDATE()
                WHERE ClaimID = @ClaimID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@Status", status),
                new SqlParameter("@AdjudicatedAmount", DBHelper.ToDbValue(adjudicatedAmount)),
                new SqlParameter("@AdjudicatedByUserID", staffUserId),
                new SqlParameter("@AdjudicationNotes", DBHelper.ToDbValue(notes)),
                new SqlParameter("@ClaimID", claimId));

            return rows > 0;
        }

        private static DamageClaim MapClaim(SqlDataReader reader)
        {
            return new DamageClaim
            {
                ClaimID = reader.GetInt32(reader.GetOrdinal("ClaimID")),
                BookingID = reader.GetInt32(reader.GetOrdinal("BookingID")),
                PropertyID = reader.GetInt32(reader.GetOrdinal("PropertyID")),
                OwnerID = reader.GetInt32(reader.GetOrdinal("OwnerID")),
                ClaimAmount = reader.GetDecimal(reader.GetOrdinal("ClaimAmount")),
                ItemizedDescription = reader.GetString(reader.GetOrdinal("ItemizedDescription")),
                EvidenceMediaUrls = reader.GetString(reader.GetOrdinal("EvidenceMediaUrls")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                AdjudicatedAmount = reader.IsDBNull(reader.GetOrdinal("AdjudicatedAmount")) ? null : reader.GetDecimal(reader.GetOrdinal("AdjudicatedAmount")),
                AdjudicatedByUserID = reader.IsDBNull(reader.GetOrdinal("AdjudicatedByUserID")) ? null : reader.GetInt32(reader.GetOrdinal("AdjudicatedByUserID")),
                AdjudicationNotes = reader.IsDBNull(reader.GetOrdinal("AdjudicationNotes")) ? null : reader.GetString(reader.GetOrdinal("AdjudicationNotes")),
                AdjudicatedAt = reader.IsDBNull(reader.GetOrdinal("AdjudicatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("AdjudicatedAt")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                PropertyTitle = reader.IsDBNull(reader.GetOrdinal("PropertyTitle")) ? null : reader.GetString(reader.GetOrdinal("PropertyTitle")),
                OwnerName = reader.IsDBNull(reader.GetOrdinal("OwnerName")) ? null : reader.GetString(reader.GetOrdinal("OwnerName")),
                ClientName = reader.IsDBNull(reader.GetOrdinal("ClientName")) ? null : reader.GetString(reader.GetOrdinal("ClientName")),
                SecurityDepositAmount = reader.IsDBNull(reader.GetOrdinal("SecurityDepositAmount")) ? 0.00m : reader.GetDecimal(reader.GetOrdinal("SecurityDepositAmount")),
                AdjudicatedByUserName = reader.IsDBNull(reader.GetOrdinal("AdjudicatedByUserName")) ? null : reader.GetString(reader.GetOrdinal("AdjudicatedByUserName"))
            };
        }
    }
}
