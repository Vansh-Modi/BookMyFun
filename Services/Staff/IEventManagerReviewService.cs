using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BookMyFun.Models.ViewModels;

namespace BookMyFun.Services.Staff
{
    public interface IEventManagerReviewService
    {
        Task<EventManagerListViewModel> GetManagerListAsync(string tab = "Pending");
        Task<EventManagerDetailViewModel?> GetManagerDetailAsync(int userId);
        Task<(bool Success, string? Error)> VerifyAsync(int userId, int performedById);
        Task<(bool Success, string? Error)> RejectAsync(int userId, string reason, int performedById);
        Task<(bool Success, string? Error)> ReturnToPendingAsync(int userId, int performedById);
        Task<(bool Success, string? Error)> SuspendAsync(int userId, string? reason, int performedById);
        Task<(bool Success, string? Error)> ReactivateAsync(int userId, int performedById);
        Task<(bool Success, string NormalizedAreas, List<string> Unnormalized, string? Error)> UpdateServiceAreasAsync(int userId, string rawServiceAreas, int performedById);
        Task<(bool Success, string? Error)> ApprovePortfolioAsync(int portfolioId, int performedById);
        Task<(bool Success, string? Error)> RejectPortfolioAsync(int portfolioId, string reason, int performedById);
        Task<(Stream? Stream, string ContentType, string? FileName)> GetDocumentStreamAsync(int userId, string docType);
        Task<int> GetPendingCountAsync();
    }
}
