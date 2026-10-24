using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;

namespace BookMyFun.Repositories
{
    public interface ILegalDocumentRepository
    {
        Task<LegalDocument?> GetCurrentDocumentAsync(string documentType);
        Task<List<LegalDocument>> GetDocumentHistoryAsync(string documentType);
        Task<LegalDocument?> GetDocumentByIdAsync(int documentId);
        Task<LegalDocument> CreateNewVersionAsync(string documentType, string title, string body, string version, DateTime effectiveDate, bool isDraft, int? createdByUserId);
        Task RecordAcceptanceAsync(PolicyAcceptance acceptance);
        Task<List<PolicyAcceptance>> GetAcceptancesByUserAsync(int userId);
        Task<List<PolicyAcceptance>> GetAcceptancesByLeadAsync(int leadId);
    }
}
