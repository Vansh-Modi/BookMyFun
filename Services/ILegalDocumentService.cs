using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;

namespace BookMyFun.Services
{
    public interface ILegalDocumentService
    {
        Task<LegalDocument?> GetCurrentDocumentAsync(string documentType);
        Task<List<LegalDocument>> GetDocumentHistoryAsync(string documentType);
        Task<LegalDocument?> GetDocumentByIdAsync(int documentId);
        Task<LegalDocument> SaveNewVersionAsync(string documentType, string title, string body, string? version, DateTime? effectiveDate, bool isDraft, int? createdByUserId);
        Task<bool> HasContentAsync(string documentType);
        Task<bool> AreAllStandardPoliciesPublishedAsync();
        Task RecordConsentAsync(int? userId, int? leadId, string documentType, string? ipAddress, string? pageUrl);
        Task RecordAllStandardConsentsAsync(int? userId, int? leadId, string? ipAddress, string? pageUrl);
    }
}
