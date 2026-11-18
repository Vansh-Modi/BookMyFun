using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;

namespace BookMyFun.Services.Checkout
{
    /// <summary>
    /// Service handling occasion filtering, verified decorator discovery, and server-side package validation during checkout.
    /// </summary>
    public interface ICheckoutEventManagerService
    {
        /// <summary>
        /// Retrieves verified and active event managers who service the property location and offer packages for the given occasion.
        /// </summary>
        Task<List<CheckoutEventManagerViewModel>> GetAvailableEventManagersAsync(string? propertyLocation, int? occasionId);

        /// <summary>
        /// Re-validates server-side that the selected package exists, is active, belongs to a verified and active event manager,
        /// services the property location, and matches the chosen occasion.
        /// </summary>
        Task<(bool IsValid, string? ErrorMessage, EventPackage? Package, User? Manager)> ValidateSelectedPackageAsync(int packageId, int? occasionId, string? propertyLocation);

        /// <summary>
        /// Checks whether an event package category matches a specific occasion name.
        /// </summary>
        bool MatchesOccasion(string? packageCategory, string? occasionName);

        /// <summary>
        /// Checks whether an event manager's service areas string covers the given property location.
        /// </summary>
        bool ServicesLocation(string? serviceAreas, string? propertyLocation);
    }
}
