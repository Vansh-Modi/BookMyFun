using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;

namespace BookMyFun.Services
{
    public class LegalDocumentService : ILegalDocumentService
    {
        private readonly ILegalDocumentRepository _repo;

        public LegalDocumentService(ILegalDocumentRepository repo)
        {
            _repo = repo;
        }

        public async Task<LegalDocument?> GetCurrentDocumentAsync(string documentType)
        {
            return await _repo.GetCurrentDocumentAsync(documentType);
        }

        public async Task<List<LegalDocument>> GetDocumentHistoryAsync(string documentType)
        {
            return await _repo.GetDocumentHistoryAsync(documentType);
        }

        public async Task<LegalDocument?> GetDocumentByIdAsync(int documentId)
        {
            return await _repo.GetDocumentByIdAsync(documentId);
        }

        public async Task<LegalDocument> SaveNewVersionAsync(
            string documentType,
            string title,
            string body,
            string? version,
            DateTime? effectiveDate,
            bool isDraft,
            int? createdByUserId)
        {
            var current = await _repo.GetCurrentDocumentAsync(documentType);

            // Compute version number if not explicitly specified
            string targetVersion = version?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(targetVersion))
            {
                if (current != null && double.TryParse(current.Version, out double curVer))
                {
                    targetVersion = (curVer + 0.1).ToString("0.0");
                }
                else
                {
                    targetVersion = current != null ? $"{current.Version}.1" : "1.0";
                }
            }

            DateTime effDate = effectiveDate ?? DateTime.UtcNow;

            return await _repo.CreateNewVersionAsync(
                documentType,
                title,
                body,
                targetVersion,
                effDate,
                isDraft,
                createdByUserId);
        }

        public async Task<bool> HasContentAsync(string documentType)
        {
            var doc = await _repo.GetCurrentDocumentAsync(documentType);
            return doc != null && !string.IsNullOrWhiteSpace(doc.Body);
        }

        public async Task<bool> AreAllStandardPoliciesPublishedAsync()
        {
            bool hasPrivacy = await HasContentAsync("PrivacyPolicy");
            bool hasTerms = await HasContentAsync("TermsOfService");
            bool hasRefund = await HasContentAsync("RefundPolicy");

            return hasPrivacy && hasTerms && hasRefund;
        }

        public async Task RecordConsentAsync(int? userId, int? leadId, string documentType, string? ipAddress, string? pageUrl)
        {
            var doc = await _repo.GetCurrentDocumentAsync(documentType);
            if (doc == null || string.IsNullOrWhiteSpace(doc.Body))
            {
                throw new InvalidOperationException("Policy not yet published. Please contact support.");
            }

            var record = new PolicyAcceptance
            {
                UserID = userId,
                LeadID = leadId,
                DocumentType = documentType,
                Version = doc.Version,
                AcceptedAt = DateTime.UtcNow,
                IpAddress = ipAddress,
                PageUrl = pageUrl
            };

            await _repo.RecordAcceptanceAsync(record);
        }

        public async Task RecordAllStandardConsentsAsync(int? userId, int? leadId, string? ipAddress, string? pageUrl)
        {
            if (!await AreAllStandardPoliciesPublishedAsync())
            {
                throw new InvalidOperationException("Policy not yet published. Please contact support.");
            }

            await RecordConsentAsync(userId, leadId, "TermsOfService", ipAddress, pageUrl);
            await RecordConsentAsync(userId, leadId, "PrivacyPolicy", ipAddress, pageUrl);
            await RecordConsentAsync(userId, leadId, "RefundPolicy", ipAddress, pageUrl);
        }
    }
}
