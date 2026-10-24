using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public class LegalDocumentRepository : ILegalDocumentRepository
    {
        private readonly DBHelper _db;

        public LegalDocumentRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<LegalDocument?> GetCurrentDocumentAsync(string documentType)
        {
            const string sql = @"
                SELECT TOP 1 d.*, u.FullName AS CreatedByUserName
                FROM LegalDocuments d
                LEFT JOIN Users u ON d.CreatedByUserID = u.UserID
                WHERE d.DocumentType = @Type AND d.IsCurrent = 1
                ORDER BY d.DocumentID DESC";

            return await _db.ExecuteReaderSingleAsync(sql, MapLegalDocument, new SqlParameter("@Type", documentType));
        }

        public async Task<List<LegalDocument>> GetDocumentHistoryAsync(string documentType)
        {
            const string sql = @"
                SELECT d.*, u.FullName AS CreatedByUserName
                FROM LegalDocuments d
                LEFT JOIN Users u ON d.CreatedByUserID = u.UserID
                WHERE d.DocumentType = @Type
                ORDER BY d.DocumentID DESC";

            return await _db.ExecuteReaderListAsync(sql, MapLegalDocument, new SqlParameter("@Type", documentType));
        }

        public async Task<LegalDocument?> GetDocumentByIdAsync(int documentId)
        {
            const string sql = @"
                SELECT d.*, u.FullName AS CreatedByUserName
                FROM LegalDocuments d
                LEFT JOIN Users u ON d.CreatedByUserID = u.UserID
                WHERE d.DocumentID = @Id";

            return await _db.ExecuteReaderSingleAsync(sql, MapLegalDocument, new SqlParameter("@Id", documentId));
        }

        public async Task<LegalDocument> CreateNewVersionAsync(
            string documentType,
            string title,
            string body,
            string version,
            DateTime effectiveDate,
            bool isDraft,
            int? createdByUserId)
        {
            // Set any previous current versions to IsCurrent = 0
            const string deactivateSql = @"
                UPDATE LegalDocuments 
                SET IsCurrent = 0 
                WHERE DocumentType = @Type";
            await _db.ExecuteNonQueryAsync(deactivateSql, new SqlParameter("@Type", documentType));

            // Insert new version (never overwrite old versions)
            const string insertSql = @"
                INSERT INTO LegalDocuments (DocumentType, Title, Body, Version, EffectiveDate, IsDraft, IsCurrent, CreatedAt, CreatedByUserID)
                OUTPUT INSERTED.DocumentID
                VALUES (@Type, @Title, @Body, @Version, @EffectiveDate, @IsDraft, 1, GETUTCDATE(), @CreatedByUserID)";

            var idObj = await _db.ExecuteScalarAsync(insertSql,
                new SqlParameter("@Type", documentType),
                new SqlParameter("@Title", title),
                new SqlParameter("@Body", body ?? ""),
                new SqlParameter("@Version", version),
                new SqlParameter("@EffectiveDate", effectiveDate),
                new SqlParameter("@IsDraft", isDraft),
                new SqlParameter("@CreatedByUserID", (object?)createdByUserId ?? DBNull.Value));

            int newId = Convert.ToInt32(idObj);
            return (await GetDocumentByIdAsync(newId))!;
        }

        public async Task RecordAcceptanceAsync(PolicyAcceptance acceptance)
        {
            const string sql = @"
                INSERT INTO PolicyAcceptances (UserID, LeadID, DocumentType, Version, AcceptedAt, IpAddress, PageUrl)
                VALUES (@UserID, @LeadID, @DocumentType, @Version, GETUTCDATE(), @IpAddress, @PageUrl)";

            await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@UserID", (object?)acceptance.UserID ?? DBNull.Value),
                new SqlParameter("@LeadID", (object?)acceptance.LeadID ?? DBNull.Value),
                new SqlParameter("@DocumentType", acceptance.DocumentType),
                new SqlParameter("@Version", acceptance.Version),
                new SqlParameter("@IpAddress", (object?)acceptance.IpAddress ?? DBNull.Value),
                new SqlParameter("@PageUrl", (object?)acceptance.PageUrl ?? DBNull.Value));
        }

        public async Task<List<PolicyAcceptance>> GetAcceptancesByUserAsync(int userId)
        {
            const string sql = @"
                SELECT * FROM PolicyAcceptances 
                WHERE UserID = @UserID 
                ORDER BY AcceptedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, MapPolicyAcceptance, new SqlParameter("@UserID", userId));
        }

        public async Task<List<PolicyAcceptance>> GetAcceptancesByLeadAsync(int leadId)
        {
            const string sql = @"
                SELECT * FROM PolicyAcceptances 
                WHERE LeadID = @LeadID 
                ORDER BY AcceptedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, MapPolicyAcceptance, new SqlParameter("@LeadID", leadId));
        }

        private static LegalDocument MapLegalDocument(SqlDataReader reader)
        {
            return new LegalDocument
            {
                DocumentID = reader.GetInt32(reader.GetOrdinal("DocumentID")),
                DocumentType = reader.GetString(reader.GetOrdinal("DocumentType")),
                Title = reader.GetString(reader.GetOrdinal("Title")),
                Body = reader.GetString(reader.GetOrdinal("Body")),
                Version = reader.GetString(reader.GetOrdinal("Version")),
                EffectiveDate = reader.GetDateTime(reader.GetOrdinal("EffectiveDate")),
                IsDraft = reader.GetBoolean(reader.GetOrdinal("IsDraft")),
                IsCurrent = reader.GetBoolean(reader.GetOrdinal("IsCurrent")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                CreatedByUserID = reader.IsDBNull(reader.GetOrdinal("CreatedByUserID")) ? null : reader.GetInt32(reader.GetOrdinal("CreatedByUserID")),
                CreatedByUserName = HasColumn(reader, "CreatedByUserName") && !reader.IsDBNull(reader.GetOrdinal("CreatedByUserName")) ? reader.GetString(reader.GetOrdinal("CreatedByUserName")) : null
            };
        }

        private static PolicyAcceptance MapPolicyAcceptance(SqlDataReader reader)
        {
            return new PolicyAcceptance
            {
                AcceptanceID = reader.GetInt32(reader.GetOrdinal("AcceptanceID")),
                UserID = reader.IsDBNull(reader.GetOrdinal("UserID")) ? null : reader.GetInt32(reader.GetOrdinal("UserID")),
                LeadID = reader.IsDBNull(reader.GetOrdinal("LeadID")) ? null : reader.GetInt32(reader.GetOrdinal("LeadID")),
                DocumentType = reader.GetString(reader.GetOrdinal("DocumentType")),
                Version = reader.GetString(reader.GetOrdinal("Version")),
                AcceptedAt = reader.GetDateTime(reader.GetOrdinal("AcceptedAt")),
                IpAddress = reader.IsDBNull(reader.GetOrdinal("IpAddress")) ? null : reader.GetString(reader.GetOrdinal("IpAddress")),
                PageUrl = reader.IsDBNull(reader.GetOrdinal("PageUrl")) ? null : reader.GetString(reader.GetOrdinal("PageUrl"))
            };
        }

        private static bool HasColumn(SqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
