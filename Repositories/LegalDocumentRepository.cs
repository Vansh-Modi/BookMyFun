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

        // In-memory fallback dictionary in case DB is unreachable or tables are initializing
        private static readonly Dictionary<string, LegalDocument> FallbackDrafts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PrivacyPolicy"] = new LegalDocument
            {
                DocumentID = 1,
                DocumentType = "PrivacyPolicy",
                Title = "Privacy Policy",
                Body = "<p><strong>DRAFT: requires legal review before publishing.</strong></p><p>This Privacy Policy outlines how BookMyFun collects, handles, stores, and protects your personal identification and reservation data.</p><h3>1. Information Collection</h3><p>We collect contact details, KYC verification documents, booking history, and transaction references.</p><h3>2. Security & Data Protection</h3><p>Guest identity documents are encrypted and accessible only with ephemeral, short-lived tokens. Host partners never receive raw identity files.</p>",
                Version = "1.0",
                EffectiveDate = DateTime.UtcNow,
                IsDraft = true,
                IsCurrent = true,
                CreatedAt = DateTime.UtcNow
            },
            ["TermsOfService"] = new LegalDocument
            {
                DocumentID = 2,
                DocumentType = "TermsOfService",
                Title = "Terms of Service",
                Body = "<p><strong>DRAFT: requires legal review before publishing.</strong></p><p>Welcome to BookMyFun. By accessing or using our marketplace for farmhouses, villas, and event spaces, you agree to these Terms of Service.</p><h3>1. Platform Role</h3><p>BookMyFun provides an intermediary marketplace connecting guests, verified hosts, and certified event decor managers.</p><h3>2. Booking and Payments</h3><p>All reservations must be completed through authorized platform payment gateways.</p>",
                Version = "1.0",
                EffectiveDate = DateTime.UtcNow,
                IsDraft = true,
                IsCurrent = true,
                CreatedAt = DateTime.UtcNow
            },
            ["RefundPolicy"] = new LegalDocument
            {
                DocumentID = 3,
                DocumentType = "RefundPolicy",
                Title = "Refund and Cancellation Policy",
                Body = "<p><strong>DRAFT: requires legal review before publishing.</strong></p><p>This policy governs guest cancellations, host cancellations, and emergency force majeure occurrences across BookMyFun reservations.</p><h3>1. Cancellation Windows</h3><p>Free cancellation is eligible up to 72 hours prior to scheduled check-in, minus non-refundable payment gateway processing fees.</p><h3>2. Payout Protection</h3><p>Cancellations occurring within 24 to 72 hours are subject to standard tiered deductions as specified during checkout.</p>",
                Version = "1.0",
                EffectiveDate = DateTime.UtcNow,
                IsDraft = true,
                IsCurrent = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        public LegalDocumentRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<LegalDocument?> GetCurrentDocumentAsync(string documentType)
        {
            try
            {
                const string sql = @"
                    SELECT TOP 1 d.*, u.FullName AS CreatedByUserName
                    FROM LegalDocuments d
                    LEFT JOIN Users u ON d.CreatedByUserID = u.UserID
                    WHERE d.DocumentType = @Type AND d.IsCurrent = 1
                    ORDER BY d.DocumentID DESC";

                var doc = await _db.ExecuteReaderSingleAsync(sql, MapLegalDocument, new SqlParameter("@Type", documentType));
                if (doc != null) return doc;
            }
            catch
            {
                // Fall back gracefully if DB table is uninitialized or unreachable
            }

            // Return in-memory draft fallback
            return FallbackDrafts.TryGetValue(documentType, out var fallback) ? fallback : null;
        }

        public async Task<List<LegalDocument>> GetDocumentHistoryAsync(string documentType)
        {
            try
            {
                const string sql = @"
                    SELECT d.*, u.FullName AS CreatedByUserName
                    FROM LegalDocuments d
                    LEFT JOIN Users u ON d.CreatedByUserID = u.UserID
                    WHERE d.DocumentType = @Type
                    ORDER BY d.DocumentID DESC";

                var list = await _db.ExecuteReaderListAsync(sql, MapLegalDocument, new SqlParameter("@Type", documentType));
                if (list != null && list.Count > 0) return list;
            }
            catch
            {
                // Fall back gracefully
            }

            return FallbackDrafts.TryGetValue(documentType, out var fb) ? new List<LegalDocument> { fb } : new List<LegalDocument>();
        }

        public async Task<LegalDocument?> GetDocumentByIdAsync(int documentId)
        {
            try
            {
                const string sql = @"
                    SELECT d.*, u.FullName AS CreatedByUserName
                    FROM LegalDocuments d
                    LEFT JOIN Users u ON d.CreatedByUserID = u.UserID
                    WHERE d.DocumentID = @Id";

                return await _db.ExecuteReaderSingleAsync(sql, MapLegalDocument, new SqlParameter("@Id", documentId));
            }
            catch
            {
                // Fall back search across in-memory drafts
                foreach (var doc in FallbackDrafts.Values)
                {
                    if (doc.DocumentID == documentId) return doc;
                }
                return null;
            }
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
            try
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
                    new SqlParameter("@CreatedByUserID", DBHelper.ToDbValue(createdByUserId)));

                if (idObj != null)
                {
                    int newId = Convert.ToInt32(idObj);
                    var created = await GetDocumentByIdAsync(newId);
                    if (created != null) return created;
                }
            }
            catch
            {
                // Fall back gracefully to in-memory update
            }

            var fallbackDoc = new LegalDocument
            {
                DocumentID = FallbackDrafts.Count + 1,
                DocumentType = documentType,
                Title = title,
                Body = body ?? "",
                Version = version,
                EffectiveDate = effectiveDate,
                IsDraft = isDraft,
                IsCurrent = !isDraft,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserID = createdByUserId
            };
            FallbackDrafts[documentType] = fallbackDoc;
            return fallbackDoc;
        }

        public async Task RecordAcceptanceAsync(PolicyAcceptance acceptance)
        {
            try
            {
                const string sql = @"
                    INSERT INTO PolicyAcceptances (UserID, LeadID, DocumentType, Version, AcceptedAt, IpAddress, PageUrl)
                    VALUES (@UserID, @LeadID, @DocumentType, @Version, GETUTCDATE(), @IpAddress, @PageUrl)";

                await _db.ExecuteNonQueryAsync(sql,
                    new SqlParameter("@UserID", DBHelper.ToDbValue(acceptance.UserID)),
                    new SqlParameter("@LeadID", DBHelper.ToDbValue(acceptance.LeadID)),
                    new SqlParameter("@DocumentType", acceptance.DocumentType),
                    new SqlParameter("@Version", acceptance.Version),
                    new SqlParameter("@IpAddress", DBHelper.ToDbValue(acceptance.IpAddress)),
                    new SqlParameter("@PageUrl", DBHelper.ToDbValue(acceptance.PageUrl)));
            }
            catch
            {
                // Non-blocking fallback for consent audit records
            }
        }

        public async Task<List<PolicyAcceptance>> GetAcceptancesByUserAsync(int userId)
        {
            try
            {
                const string sql = @"
                    SELECT * FROM PolicyAcceptances 
                    WHERE UserID = @UserID 
                    ORDER BY AcceptedAt DESC";

                return await _db.ExecuteReaderListAsync(sql, MapPolicyAcceptance, new SqlParameter("@UserID", userId));
            }
            catch
            {
                return new List<PolicyAcceptance>();
            }
        }

        public async Task<List<PolicyAcceptance>> GetAcceptancesByLeadAsync(int leadId)
        {
            try
            {
                const string sql = @"
                    SELECT * FROM PolicyAcceptances 
                    WHERE LeadID = @LeadID 
                    ORDER BY AcceptedAt DESC";

                return await _db.ExecuteReaderListAsync(sql, MapPolicyAcceptance, new SqlParameter("@LeadID", leadId));
            }
            catch
            {
                return new List<PolicyAcceptance>();
            }
        }

        private static LegalDocument MapLegalDocument(SqlDataReader reader)
        {
            return new LegalDocument
            {
                DocumentID = reader.GetInt32(reader.GetOrdinal("DocumentID")),
                DocumentType = reader.GetString(reader.GetOrdinal("DocumentType")),
                Title = reader.IsDBNull(reader.GetOrdinal("Title")) ? string.Empty : reader.GetString(reader.GetOrdinal("Title")),
                Body = reader.IsDBNull(reader.GetOrdinal("Body")) ? string.Empty : reader.GetString(reader.GetOrdinal("Body")),
                Version = reader.IsDBNull(reader.GetOrdinal("Version")) ? "1.0" : reader.GetString(reader.GetOrdinal("Version")),
                EffectiveDate = reader.IsDBNull(reader.GetOrdinal("EffectiveDate")) ? DateTime.UtcNow : reader.GetDateTime(reader.GetOrdinal("EffectiveDate")),
                IsDraft = !reader.IsDBNull(reader.GetOrdinal("IsDraft")) && reader.GetBoolean(reader.GetOrdinal("IsDraft")),
                IsCurrent = !reader.IsDBNull(reader.GetOrdinal("IsCurrent")) && reader.GetBoolean(reader.GetOrdinal("IsCurrent")),
                CreatedAt = reader.IsDBNull(reader.GetOrdinal("CreatedAt")) ? DateTime.UtcNow : reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
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
                DocumentType = reader.IsDBNull(reader.GetOrdinal("DocumentType")) ? string.Empty : reader.GetString(reader.GetOrdinal("DocumentType")),
                Version = reader.IsDBNull(reader.GetOrdinal("Version")) ? "1.0" : reader.GetString(reader.GetOrdinal("Version")),
                AcceptedAt = reader.IsDBNull(reader.GetOrdinal("AcceptedAt")) ? DateTime.UtcNow : reader.GetDateTime(reader.GetOrdinal("AcceptedAt")),
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
