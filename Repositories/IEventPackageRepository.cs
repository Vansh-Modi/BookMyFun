using BookMyFun.Models.Entities;

namespace BookMyFun.Repositories
{
    public interface IEventPackageRepository
    {
        Task<List<EventPackage>> GetAllPackagesAsync();
        Task<List<EventPackage>> GetActivePackagesAsync();
        Task<EventPackage?> GetPackageByIdAsync(int packageId);
        Task<int> CreatePackageAsync(EventPackage package);
        Task<bool> UpdatePackageAsync(EventPackage package);
        Task<bool> DeletePackageAsync(int packageId);

        Task<List<Booking>> GetAssignedEventBookingsAsync(int decoratorUserId);
        Task<List<Booking>> GetAllEventBookingsAsync();
        Task<bool> UpdateSetupStatusAsync(int bookingId, string setupStatus, string? setupProofMediaUrls, int? decoratorId);
        Task<bool> AssignDecoratorAsync(int bookingId, int decoratorUserId);
        Task<bool> CancelOrRemoveEventFromBookingAsync(int bookingId);
        Task<List<User>> GetDecoratorsAsync();
        Task<List<string>> GetAllDistinctPropertyLocationsAsync();
        Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId);
        Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas);
        Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation);
    }
}
