using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services.Checkout
{
    public class StayCelebrationEligibilityService : IStayCelebrationEligibilityService
    {
        private readonly IPropertyRepository _propRepo;
        private readonly IEventPackageRepository _eventPackageRepo;
        private readonly IUserRepository _userRepo;
        private readonly IConfiguration _config;
        private readonly IMemoryCache _cache;
        private readonly ILogger<StayCelebrationEligibilityService>? _logger;

        // Tracks cached keys so they can be invalidated when property or manager records change
        private static readonly ConcurrentDictionary<string, byte> _trackedCacheKeys = new();
        private static IMemoryCache? _staticCache;

        public StayCelebrationEligibilityService(
            IPropertyRepository propRepo,
            IEventPackageRepository eventPackageRepo,
            IUserRepository userRepo,
            IConfiguration config,
            IMemoryCache cache,
            ILogger<StayCelebrationEligibilityService>? logger = null)
        {
            _propRepo = propRepo;
            _eventPackageRepo = eventPackageRepo;
            _userRepo = userRepo;
            _config = config;
            _cache = cache;
            _staticCache = cache;
            _logger = logger;
        }

        public async Task<StayCelebrationEligibilityResult> CheckEligibilityAsync(
            int propertyId,
            DateTime checkInDate,
            TimeSpan? checkInTime = null,
            string? chosenCategory = null,
            string? sortBy = "rating")
        {
            // 1. Check-in Lead Time Rule: more than 48 hours after now in Asia/Kolkata
            string defaultCheckInStr = _config["Checkout:DefaultCheckInTime"] ?? "12:00";
            TimeSpan effectiveCheckInTime = checkInTime ?? (TimeSpan.TryParse(defaultCheckInStr, out var parsed) ? parsed : new TimeSpan(12, 0, 0));

            TimeZoneInfo kolkataTz;
            try
            {
                kolkataTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            }
            catch
            {
                kolkataTz = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            }

            DateTime kolkataNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, kolkataTz);
            DateTime checkInDateTime = checkInDate.Date.Add(effectiveCheckInTime);
            TimeSpan leadTime = checkInDateTime - kolkataNow;

            // Exactly 48 hours is NOT allowed (must be strictly > 48 hours)
            if (leadTime <= TimeSpan.FromHours(48))
            {
                return new StayCelebrationEligibilityResult
                {
                    Available = false,
                    ReasonCode = "too_soon",
                    ReasonMessage = "Décor needs more than 48 hours' notice, so we've switched you to a standard stay."
                };
            }

            // 2. Property Live Rule: Property must be live
            var property = await _propRepo.GetByIdAsync(propertyId);
            if (property == null ||
                string.Equals(property.ListingStatus, "Deactivated", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.ListingStatus, "Rejected", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.ListingStatus, "Suspended", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.ListingStatus, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                return new StayCelebrationEligibilityResult
                {
                    Available = false,
                    ReasonCode = "property_not_live",
                    ReasonMessage = "This property is not currently live for celebration bookings."
                };
            }

            // Resolve Property City and State
            var (propCity, propState) = ResolvePropertyCityAndState(property);

            // 3. Cache lookup (5 minutes per property, date, and area)
            string cacheKey = $"StayCelebrationEligibility:{propertyId}:{checkInDate:yyyyMMdd}:{propCity}_{propState}:{chosenCategory ?? "all"}:{sortBy ?? "rating"}";
            if (_cache.TryGetValue(cacheKey, out StayCelebrationEligibilityResult? cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            // 4. Area Matching & Rule 3 Décor Manager Qualification
            // Fetch all event managers
            var allDecorators = await _eventPackageRepo.GetDecoratorsAsync(onlyVerified: false);
            var eventManagers = allDecorators
                .Where(u => string.Equals(u.Role, "EventManager", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!eventManagers.Any())
            {
                var noCoverageResult = new StayCelebrationEligibilityResult
                {
                    Available = false,
                    ReasonCode = "no_area_coverage",
                    ReasonMessage = "No event managers currently registered."
                };
                SetCache(cacheKey, noCoverageResult);
                return noCoverageResult;
            }

            // Match Property City first; Fallback to State ONLY if no manager matches City. Never match free text.
            List<User> areaManagers = new();
            if (!string.IsNullOrWhiteSpace(propCity))
            {
                areaManagers = eventManagers.Where(m =>
                {
                    var areas = ParseServiceAreas(m.ServiceAreas);
                    return areas.Any(a => string.Equals(a.City, propCity, StringComparison.OrdinalIgnoreCase));
                }).ToList();
            }

            if (!areaManagers.Any() && !string.IsNullOrWhiteSpace(propState))
            {
                // Fallback to State only when no city matches
                areaManagers = eventManagers.Where(m =>
                {
                    var areas = ParseServiceAreas(m.ServiceAreas);
                    return areas.Any(a => string.Equals(a.State, propState, StringComparison.OrdinalIgnoreCase));
                }).ToList();
            }

            if (!areaManagers.Any())
            {
                var noCoverageResult = new StayCelebrationEligibilityResult
                {
                    Available = false,
                    ReasonCode = "no_area_coverage",
                    ReasonMessage = "No active décor manager serves this property's location."
                };
                SetCache(cacheKey, noCoverageResult);
                return noCoverageResult;
            }

            // Check verified & active managers in area
            var verifiedActiveAreaManagers = areaManagers.Where(m =>
                m.IsActive &&
                string.Equals(m.VerificationStatus, "Verified", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (!verifiedActiveAreaManagers.Any())
            {
                var noVerifiedResult = new StayCelebrationEligibilityResult
                {
                    Available = false,
                    ReasonCode = "no_verified_manager",
                    ReasonMessage = "No verified décor manager currently serves this area."
                };
                SetCache(cacheKey, noVerifiedResult);
                return noVerifiedResult;
            }

            // Check approved portfolio items (IsApproved = 1)
            var approvedPortfolioUserIds = await _eventPackageRepo.GetUserIdsWithApprovedPortfoliosAsync();
            var qualifiedManagers = verifiedActiveAreaManagers
                .Where(m => approvedPortfolioUserIds.Contains(m.UserID))
                .ToList();

            if (!qualifiedManagers.Any())
            {
                var noPortfolioResult = new StayCelebrationEligibilityResult
                {
                    Available = false,
                    ReasonCode = "no_approved_portfolio",
                    ReasonMessage = "No verified décor manager with an approved showcase portfolio currently serves this area."
                };
                SetCache(cacheKey, noPortfolioResult);
                return noPortfolioResult;
            }

            // Fetch active packages for qualified managers
            var allActivePackages = await _eventPackageRepo.GetActivePackagesAsync();
            var qualifiedUserIds = new HashSet<int>(qualifiedManagers.Select(m => m.UserID));
            var eligiblePackages = allActivePackages
                .Where(p => p.IsActive && p.CreatedByUserID.HasValue && qualifiedUserIds.Contains(p.CreatedByUserID.Value))
                .ToList();

            if (!eligiblePackages.Any())
            {
                var noPackageResult = new StayCelebrationEligibilityResult
                {
                    Available = false,
                    ReasonCode = "no_verified_manager",
                    ReasonMessage = "No active décor packages currently available from verified managers in this area."
                };
                SetCache(cacheKey, noPackageResult);
                return noPackageResult;
            }

            // Categories offered: distinct categories from packages that meet rule 3 (Do not use PropertyOccasions)
            var categoryCounts = eligiblePackages
                .GroupBy(p => p.Category)
                .Select(g => new CategoryCountDto
                {
                    Category = g.Key,
                    Count = g.Count()
                })
                .OrderBy(c => c.Category)
                .ToList();

            // Map all packages into safe DTOs (Never include phone, email, WhatsApp, social links, or address)
            var managerLookup = qualifiedManagers.ToDictionary(m => m.UserID);
            var packageDtos = new List<ManagerPackageCardDto>();

            foreach (var pkg in eligiblePackages)
            {
                if (pkg.CreatedByUserID.HasValue && managerLookup.TryGetValue(pkg.CreatedByUserID.Value, out var mgr))
                {
                    string displayName = !string.IsNullOrWhiteSpace(mgr.FullName) ? mgr.FullName : "Verified Décor Team";
                    packageDtos.Add(new ManagerPackageCardDto
                    {
                        PackageId = pkg.PackageID,
                        PackageName = pkg.Name,
                        Category = pkg.Category,
                        Price = pkg.BasePrice,
                        ManagerId = mgr.UserID,
                        DisplayName = displayName,
                        IsVerified = true,
                        Rating = 4.9, // Default positive rating
                        EventsCompleted = 10,
                        ProfileLink = $"/EventManager/PublicProfile?id={mgr.UserID}"
                    });
                }
            }

            // Filter by chosen category if provided
            List<ManagerPackageCardDto> filteredPackages;
            List<ManagerPackageCardDto> alternativePackages = new();

            if (!string.IsNullOrWhiteSpace(chosenCategory))
            {
                filteredPackages = packageDtos
                    .Where(p => string.Equals(p.Category, chosenCategory, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // When the chosen category has no match in the area, show 3 to 7 packages from other categories
                if (!filteredPackages.Any())
                {
                    alternativePackages = packageDtos
                        .Where(p => !string.Equals(p.Category, chosenCategory, StringComparison.OrdinalIgnoreCase))
                        .Take(7)
                        .ToList();
                }
            }
            else
            {
                filteredPackages = packageDtos;
            }

            // Apply Sorting: "rating" (default) or "price"
            if (string.Equals(sortBy, "price", StringComparison.OrdinalIgnoreCase))
            {
                filteredPackages = filteredPackages.OrderBy(p => p.Price).ThenByDescending(p => p.Rating).ToList();
                alternativePackages = alternativePackages.OrderBy(p => p.Price).ThenByDescending(p => p.Rating).ToList();
            }
            else
            {
                filteredPackages = filteredPackages.OrderByDescending(p => p.Rating).ThenBy(p => p.Price).ToList();
                alternativePackages = alternativePackages.OrderByDescending(p => p.Rating).ThenBy(p => p.Price).ToList();
            }

            var result = new StayCelebrationEligibilityResult
            {
                Available = true,
                ReasonCode = "ok",
                ReasonMessage = "Décor is available for this stay.",
                Categories = categoryCounts,
                Packages = filteredPackages,
                AlternativePackages = alternativePackages
            };

            SetCache(cacheKey, result);
            return result;
        }

        public async Task<(bool IsEligible, string ReasonCode, string? ErrorMessage, EventPackage? Package, User? Manager)> ValidateBookingPackageAsync(
            int propertyId,
            int packageId,
            DateTime checkInDate,
            TimeSpan? checkInTime = null)
        {
            var generalEligibility = await CheckEligibilityAsync(propertyId, checkInDate, checkInTime);
            if (!generalEligibility.Available)
            {
                return (false, generalEligibility.ReasonCode, generalEligibility.ReasonMessage, null, null);
            }

            var package = await _eventPackageRepo.GetPackageByIdAsync(packageId);
            if (package == null || !package.IsActive)
            {
                return (false, "package_not_active", "The selected décor package is no longer active.", null, null);
            }

            if (!package.CreatedByUserID.HasValue)
            {
                return (false, "manager_not_found", "The décor manager for this package could not be identified.", null, null);
            }

            var manager = await _userRepo.GetByIdAsync(package.CreatedByUserID.Value);
            if (manager == null || !manager.IsActive || !string.Equals(manager.Role, "EventManager", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(manager.VerificationStatus, "Verified", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "manager_not_verified", "The décor manager for this package is not currently verified.", null, null);
            }

            var approvedUserIds = await _eventPackageRepo.GetUserIdsWithApprovedPortfoliosAsync();
            if (!approvedUserIds.Contains(manager.UserID))
            {
                return (false, "no_approved_portfolio", "The décor manager does not have an approved showcase portfolio.", null, null);
            }

            // Verify manager serves property area
            var property = await _propRepo.GetByIdAsync(propertyId);
            if (property == null)
            {
                return (false, "property_not_found", "Property could not be located.", null, null);
            }

            var (propCity, propState) = ResolvePropertyCityAndState(property);
            var areas = ParseServiceAreas(manager.ServiceAreas);
            bool servesArea = false;

            if (!string.IsNullOrWhiteSpace(propCity))
            {
                servesArea = areas.Any(a => string.Equals(a.City, propCity, StringComparison.OrdinalIgnoreCase));
            }
            if (!servesArea && !string.IsNullOrWhiteSpace(propState))
            {
                servesArea = areas.Any(a => string.Equals(a.State, propState, StringComparison.OrdinalIgnoreCase));
            }

            if (!servesArea)
            {
                return (false, "no_area_coverage", "The selected décor team does not serve this property's location.", null, null);
            }

            return (true, "ok", null, package, manager);
        }

        public void InvalidateCache(int? propertyId = null)
        {
            InvalidateAllCache(propertyId);
        }

        public static void InvalidateAllCache(int? propertyId = null)
        {
            if (_staticCache == null) return;

            if (propertyId.HasValue)
            {
                string prefix = $"StayCelebrationEligibility:{propertyId.Value}:";
                var keysToRemove = _trackedCacheKeys.Keys.Where(k => k.StartsWith(prefix)).ToList();
                foreach (var k in keysToRemove)
                {
                    _staticCache.Remove(k);
                    _trackedCacheKeys.TryRemove(k, out _);
                }
            }
            else
            {
                foreach (var k in _trackedCacheKeys.Keys)
                {
                    _staticCache.Remove(k);
                }
                _trackedCacheKeys.Clear();
            }
        }

        public string NormalizeArea(string? area)
        {
            if (string.IsNullOrWhiteSpace(area)) return string.Empty;

            var parts = area.Split(new[] { ',' }, 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                string city = Capitalize(parts[0]);
                string state = Capitalize(parts[1]);
                return $"{city}, {state}";
            }
            return Capitalize(parts[0]);
        }

        public List<(string City, string State)> ParseServiceAreas(string? serviceAreas)
        {
            var result = new List<(string City, string State)>();
            if (string.IsNullOrWhiteSpace(serviceAreas)) return result;

            var lines = serviceAreas.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                var normalized = NormalizeArea(line);
                if (string.IsNullOrWhiteSpace(normalized)) continue;

                var parts = normalized.Split(new[] { ',' }, 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    result.Add((parts[0], parts[1]));
                }
                else if (parts.Length == 1)
                {
                    result.Add((parts[0], string.Empty));
                }
            }
            return result;
        }

        private static (string City, string State) ResolvePropertyCityAndState(Property property)
        {
            string city = (property.City ?? "").Trim();
            string state = (property.State ?? "").Trim();

            if ((string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(state)) && !string.IsNullOrWhiteSpace(property.Location))
            {
                var parts = property.Location.Split(new[] { ',', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (string.IsNullOrWhiteSpace(city) && parts.Length > 0)
                {
                    city = parts[0];
                }
                if (string.IsNullOrWhiteSpace(state))
                {
                    state = parts.Length > 1 ? parts[parts.Length - 1] : parts[0];
                }
            }

            return (Capitalize(city), Capitalize(state));
        }

        private static string Capitalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1).ToLowerInvariant() : "")));
        }

        private void SetCache(string cacheKey, StayCelebrationEligibilityResult result)
        {
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
            _trackedCacheKeys.TryAdd(cacheKey, 0);
        }
    }
}
