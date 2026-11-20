using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services.Checkout
{
    /// <summary>
    /// Service implementing occasion-based event manager filtering, public card presentation, and server-side package verification.
    /// </summary>
    public class CheckoutEventManagerService : ICheckoutEventManagerService
    {
        private readonly IEventPackageRepository _eventRepo;
        private readonly IUserRepository _userRepo;
        private readonly IPropertyRepository _propRepo;
        private readonly ILogger<CheckoutEventManagerService>? _logger;

        public CheckoutEventManagerService(
            IEventPackageRepository eventRepo,
            IUserRepository userRepo,
            IPropertyRepository propRepo,
            ILogger<CheckoutEventManagerService>? logger = null)
        {
            _eventRepo = eventRepo;
            _userRepo = userRepo;
            _propRepo = propRepo;
            _logger = logger;
        }

        public async Task<List<CheckoutEventManagerViewModel>> GetAvailableEventManagersAsync(string? propertyLocation, int? occasionId)
        {
            var results = new List<CheckoutEventManagerViewModel>();

            var decorators = await _eventRepo.GetDecoratorsAsync(onlyVerified: true);
            var verifiedManagers = decorators.Where(d =>
                string.Equals(d.Role, "EventManager", StringComparison.OrdinalIgnoreCase) &&
                d.IsActive &&
                string.Equals(d.VerificationStatus, "Verified", StringComparison.OrdinalIgnoreCase)).ToList();

            var locationManagers = verifiedManagers.Where(m => ServicesLocation(m.ServiceAreas, propertyLocation)).ToList();
            if (!locationManagers.Any())
            {
                return results;
            }

            string? occasionName = null;
            if (occasionId.HasValue && occasionId.Value > 0)
            {
                var allOccasions = await _propRepo.GetOccasionsAsync();
                var selectedOccasion = allOccasions.FirstOrDefault(o => o.OccasionID == occasionId.Value);
                occasionName = selectedOccasion?.OccasionName;
            }

            foreach (var manager in locationManagers)
            {
                var allPackages = await _eventRepo.GetPackagesByUserIdAsync(manager.UserID);
                var activePackages = allPackages.Where(p => p.IsActive).ToList();

                var matchedPackages = !string.IsNullOrWhiteSpace(occasionName)
                    ? activePackages.Where(p => MatchesOccasion(p.Category, occasionName)).ToList()
                    : activePackages;

                // Phase 6 Rule: If an event manager has no package for that occasion, do not show them.
                if (!matchedPackages.Any())
                {
                    continue;
                }

                var approvedPortfolios = await _eventRepo.GetPortfoliosByUserIdAsync(manager.UserID, onlyApproved: true);

                // Rule: Must have at least one approved portfolio item or active package
                if (!matchedPackages.Any() && !approvedPortfolios.Any())
                {
                    continue;
                }

                // Database-backed real stats following the 10-event display rule
                var assignedBookings = await _eventRepo.GetAssignedEventBookingsAsync(manager.UserID);
                int eventsDone = assignedBookings.Count(b => b.Status == "Completed" || b.SetupStatus == "Completed" || b.SetupStatus == "Inspected");
                int? mgrEvents = null;
                if (eventsDone >= AppConstants.MinCompletedEventsToShowStats)
                {
                    mgrEvents = eventsDone;
                }

                // Public-only data (Zero contact fields: no phone, no email, no address, no social links)
                var card = new CheckoutEventManagerViewModel
                {
                    UserID = manager.UserID,
                    DisplayName = manager.FullName,
                    ProfileImageUrl = "/images/default-avatar.svg", // Platform default avatar
                    ServiceAreas = manager.ServiceAreas,
                    YearsOfExperience = manager.YearsOfExperience.HasValue && manager.YearsOfExperience.Value > 0 ? manager.YearsOfExperience.Value : 3,
                    AverageRating = null,
                    ReviewCount = null,
                    EventsCompleted = mgrEvents,
                    StartingPrice = matchedPackages.Min(p => p.BasePrice),
                    PackagesForOccasion = matchedPackages.OrderBy(p => p.BasePrice).ToList(),
                    PortfolioSampleUrls = approvedPortfolios.Take(3).Select(p => p.MediaUrl).ToList()
                };

                results.Add(card);
            }

            return results.OrderBy(r => r.StartingPrice).ToList();
        }

        public async Task<(bool IsValid, string? ErrorMessage, EventPackage? Package, User? Manager)> ValidateSelectedPackageAsync(
            int packageId, int? occasionId, string? propertyLocation)
        {
            if (packageId <= 0)
            {
                return (false, "Please select a valid event decoration package.", null, null);
            }

            var package = await _eventRepo.GetPackageByIdAsync(packageId);
            if (package == null || !package.IsActive)
            {
                return (false, "The selected event decoration package is no longer active or available.", null, null);
            }

            if (!package.CreatedByUserID.HasValue || package.CreatedByUserID.Value <= 0)
            {
                return (false, "No event manager is associated with the selected decoration package.", package, null);
            }

            var manager = await _userRepo.GetByIdAsync(package.CreatedByUserID.Value);
            if (manager == null ||
                !string.Equals(manager.Role, "EventManager", StringComparison.OrdinalIgnoreCase) ||
                !manager.IsActive ||
                !string.Equals(manager.VerificationStatus, "Verified", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "The event manager for this package is not currently active and verified on the platform.", package, null);
            }

            if (!ServicesLocation(manager.ServiceAreas, propertyLocation))
            {
                return (false, "The selected event manager does not service the location of this property.", package, manager);
            }

            if (occasionId.HasValue && occasionId.Value > 0)
            {
                var occasions = await _propRepo.GetOccasionsAsync();
                var occasion = occasions.FirstOrDefault(o => o.OccasionID == occasionId.Value);
                if (occasion != null && !MatchesOccasion(package.Category, occasion.OccasionName))
                {
                    return (false, $"The package '{package.Name}' ({package.Category}) is not offered for the selected occasion '{occasion.OccasionName}'.", package, manager);
                }
            }

            return (true, null, package, manager);
        }

        public bool MatchesOccasion(string? packageCategory, string? occasionName)
        {
            if (string.IsNullOrWhiteSpace(packageCategory)) return true;
            if (string.IsNullOrWhiteSpace(occasionName)) return true;

            var cat = packageCategory.Trim();
            var occ = occasionName.Trim();

            // Broad categories that apply to any occasion
            if (string.Equals(cat, "Custom", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cat, "All", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cat, "General", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cat, "Other", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(cat, occ, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Substring checks
            if (occ.IndexOf(cat, StringComparison.OrdinalIgnoreCase) >= 0 ||
                cat.IndexOf(occ, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Tokenized checks (e.g. "Corporate Retreat" matches "Corporate", "Wedding Anniversary" matches "Wedding")
            var sep = new[] { ' ', '-', '/', '&', ',' };
            var occTokens = occ.Split(sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var catTokens = cat.Split(sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var ct in catTokens)
            {
                foreach (var ot in occTokens)
                {
                    if (string.Equals(ct, ot, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool ServicesLocation(string? serviceAreas, string? propertyLocation)
        {
            if (string.IsNullOrWhiteSpace(propertyLocation)) return true;
            if (string.IsNullOrWhiteSpace(serviceAreas)) return false;

            var cleanPropLoc = propertyLocation.Trim().ToLowerInvariant();
            var areas = serviceAreas.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var a in areas)
            {
                var cleanA = a.ToLowerInvariant();
                if (cleanA == cleanPropLoc || cleanPropLoc.Contains(cleanA) || cleanA.Contains(cleanPropLoc))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
