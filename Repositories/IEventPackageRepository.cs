using BookMyFun.Models.Entities;

namespace BookMyFun.Repositories
{
    public interface IEventPackageRepository
    {
        Task<List<EventPackage>> GetAllPackagesAsync();
        Task<List<EventPackage>> GetActivePackagesAsync();
        Task<List<EventPackage>> GetPackagesByUserIdAsync(int userId) => Task.FromResult(new List<EventPackage>());
        Task<EventPackage?> GetPackageByIdAsync(int packageId);
        Task<int> CreatePackageAsync(EventPackage package);
        Task<bool> UpdatePackageAsync(EventPackage package);
        Task<bool> UpdatePackageCommissionRateAsync(int packageId, decimal? commissionRate);
        Task<bool> DeletePackageAsync(int packageId);

        Task<List<Booking>> GetAssignedEventBookingsAsync(int decoratorUserId);
        Task<List<Booking>> GetAllEventBookingsAsync();
        Task<bool> UpdateSetupStatusAsync(int bookingId, string setupStatus, string? setupProofMediaUrls, int? decoratorId);
        Task<bool> AssignDecoratorAsync(int bookingId, int decoratorUserId);
        Task<bool> CancelOrRemoveEventFromBookingAsync(int bookingId);
        Task<List<User>> GetDecoratorsAsync(bool onlyVerified = false) => Task.FromResult(new List<User>());
        Task<List<string>> GetAllDistinctPropertyLocationsAsync();
        Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId);
        Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas);
        Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation);

        // Portfolio Management (Section 7)
        Task<int> AddPortfolioItemAsync(EventManagerPortfolio item) => Task.FromResult(1);
        Task<List<EventManagerPortfolio>> GetPortfoliosByUserIdAsync(int userId, bool onlyApproved = true) => Task.FromResult(new List<EventManagerPortfolio>());
        Task<HashSet<int>> GetUserIdsWithApprovedPortfoliosAsync() => Task.FromResult(new HashSet<int>());
        Task<List<EventManagerPortfolio>> GetAllPortfoliosAsync() => Task.FromResult(new List<EventManagerPortfolio>());
        Task<bool> ApprovePortfolioItemAsync(int portfolioId, bool isApproved) => Task.FromResult(true);
        Task<bool> DeletePortfolioItemAsync(int portfolioId, int userId) => Task.FromResult(true);

        // Moderation & Contact Flagging (Section 7e)
        Task<List<FlaggedContactAttempt>> GetFlaggedContactAttemptsAsync() => Task.FromResult(new List<FlaggedContactAttempt>());
        Task<bool> UpdateFlaggedContactAttemptStatusAsync(int flagId, string status) => Task.FromResult(true);
    }
}
