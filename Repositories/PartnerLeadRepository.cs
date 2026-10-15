using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IPartnerLeadRepository
    {
        Task<int> InsertLeadAsync(PartnerLead lead);
        Task<PartnerLead?> GetLeadByIdAsync(int leadId);
        Task<PartnerLead?> GetLeadByTokenAsync(string token);
        Task<List<PartnerLead>> GetAllLeadsAsync(string? status = null);
        Task<bool> UpdateLeadStatusAsync(int leadId, string status, int? reviewedByUserId, string? activationToken = null, DateTime? tokenExpiresAt = null);
        Task<bool> MarkTokenUsedAsync(int leadId);
    }

    public class PartnerLeadRepository : IPartnerLeadRepository
    {
        private readonly DBHelper _db;

        public PartnerLeadRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<int> InsertLeadAsync(PartnerLead lead)
        {
            const string sql = @"
                INSERT INTO PartnerLeads
                (FullName, Phone, Email, PropertyName, Location, AcreageOrType, Message, Status, CreatedAt)
                OUTPUT INSERTED.LeadID
                VALUES
                (@FullName, @Phone, @Email, @PropertyName, @Location, @AcreageOrType, @Message, @Status, GETDATE())";

            var result = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@FullName", lead.FullName),
                new SqlParameter("@Phone", lead.Phone),
                new SqlParameter("@Email", lead.Email),
                new SqlParameter("@PropertyName", lead.PropertyName),
                new SqlParameter("@Location", lead.Location),
                new SqlParameter("@AcreageOrType", lead.AcreageOrType),
                new SqlParameter("@Message", DBHelper.ToDbValue(lead.Message)),
                new SqlParameter("@Status", lead.Status ?? "New"));

            return result != null ? Convert.ToInt32(result) : 0;
        }

        public async Task<PartnerLead?> GetLeadByIdAsync(int leadId)
        {
            const string sql = @"
                SELECT pl.*, u.FullName AS ReviewedByUserName
                FROM PartnerLeads pl
                LEFT JOIN Users u ON pl.ReviewedByUserID = u.UserID
                WHERE pl.LeadID = @LeadID";

            return await _db.ExecuteReaderSingleAsync(sql, MapLead, new SqlParameter("@LeadID", leadId));
        }

        public async Task<PartnerLead?> GetLeadByTokenAsync(string token)
        {
            const string sql = @"
                SELECT pl.*, u.FullName AS ReviewedByUserName
                FROM PartnerLeads pl
                LEFT JOIN Users u ON pl.ReviewedByUserID = u.UserID
                WHERE pl.ActivationToken = @ActivationToken";

            return await _db.ExecuteReaderSingleAsync(sql, MapLead, new SqlParameter("@ActivationToken", token));
        }

        public async Task<List<PartnerLead>> GetAllLeadsAsync(string? status = null)
        {
            string sql = @"
                SELECT pl.*, u.FullName AS ReviewedByUserName
                FROM PartnerLeads pl
                LEFT JOIN Users u ON pl.ReviewedByUserID = u.UserID";

            if (!string.IsNullOrEmpty(status))
            {
                sql += " WHERE pl.Status = @Status ORDER BY pl.CreatedAt DESC";
                return await _db.ExecuteReaderListAsync(sql, MapLead, new SqlParameter("@Status", status));
            }

            sql += " ORDER BY pl.CreatedAt DESC";
            return await _db.ExecuteReaderListAsync(sql, MapLead);
        }

        public async Task<bool> UpdateLeadStatusAsync(int leadId, string status, int? reviewedByUserId, string? activationToken = null, DateTime? tokenExpiresAt = null)
        {
            const string sql = @"
                UPDATE PartnerLeads
                SET Status = @Status,
                    ReviewedByUserID = @ReviewedByUserID,
                    ActivationToken = COALESCE(@ActivationToken, ActivationToken),
                    TokenExpiresAt = COALESCE(@TokenExpiresAt, TokenExpiresAt)
                WHERE LeadID = @LeadID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@Status", status),
                new SqlParameter("@ReviewedByUserID", DBHelper.ToDbValue(reviewedByUserId)),
                new SqlParameter("@ActivationToken", DBHelper.ToDbValue(activationToken)),
                new SqlParameter("@TokenExpiresAt", DBHelper.ToDbValue(tokenExpiresAt)),
                new SqlParameter("@LeadID", leadId));

            return rows > 0;
        }

        public async Task<bool> MarkTokenUsedAsync(int leadId)
        {
            const string sql = @"
                UPDATE PartnerLeads
                SET ActivationToken = NULL,
                    TokenExpiresAt = NULL,
                    Status = 'Approved'
                WHERE LeadID = @LeadID";

            var rows = await _db.ExecuteNonQueryAsync(sql, new SqlParameter("@LeadID", leadId));
            return rows > 0;
        }

        private static PartnerLead MapLead(SqlDataReader reader)
        {
            return new PartnerLead
            {
                LeadID = reader.GetInt32(reader.GetOrdinal("LeadID")),
                FullName = reader.GetString(reader.GetOrdinal("FullName")),
                Phone = reader.GetString(reader.GetOrdinal("Phone")),
                Email = reader.GetString(reader.GetOrdinal("Email")),
                PropertyName = reader.GetString(reader.GetOrdinal("PropertyName")),
                Location = reader.GetString(reader.GetOrdinal("Location")),
                AcreageOrType = reader.GetString(reader.GetOrdinal("AcreageOrType")),
                Message = reader.IsDBNull(reader.GetOrdinal("Message")) ? null : reader.GetString(reader.GetOrdinal("Message")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                ActivationToken = reader.IsDBNull(reader.GetOrdinal("ActivationToken")) ? null : reader.GetString(reader.GetOrdinal("ActivationToken")),
                TokenExpiresAt = reader.IsDBNull(reader.GetOrdinal("TokenExpiresAt")) ? null : reader.GetDateTime(reader.GetOrdinal("TokenExpiresAt")),
                ReviewedByUserID = reader.IsDBNull(reader.GetOrdinal("ReviewedByUserID")) ? null : reader.GetInt32(reader.GetOrdinal("ReviewedByUserID")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                ReviewedByUserName = reader.IsDBNull(reader.GetOrdinal("ReviewedByUserName")) ? null : reader.GetString(reader.GetOrdinal("ReviewedByUserName"))
            };
        }
    }
}
