using BookMyFun.Models.Entities;
using BookMyFun.Repositories;

namespace BookMyFun.Services
{
    public interface IBookingPricingService
    {
        decimal CalculatePrice(decimal pricePerNight, string bookingType, string? slotType, int daysOrHours);
    }

    public class BookingPricingService : IBookingPricingService
    {
        public decimal CalculatePrice(decimal pricePerNight, string bookingType, string? slotType, int count)
        {
            if (count <= 0) count = 1;

            return bookingType switch
            {
                "FullDay" => pricePerNight * count,
                "HalfDay" => Math.Round(pricePerNight * 0.55m * count, 2), // 55% for morning or evening slot
                "Hourly" => Math.Round((pricePerNight / 10m) * count, 2), // hourly calculated as fraction of day
                _ => pricePerNight * count
            };
        }
    }

    public interface ICommissionService
    {
        Task<(decimal commissionAmount, decimal ownerPayout)> CalculateCommissionAsync(decimal totalBookingAmount);
        Task<(decimal rateSnapshot, decimal commissionAmount, decimal ownerPayout)> CalculateDynamicCommissionAsync(decimal totalBookingAmount, decimal? propertyCommissionRate, int? ownerId = null);
        Task<(decimal rateSnapshot, decimal commissionAmount, decimal managerPayout)> CalculateEventCommissionAsync(
            decimal eventPackagePrice,
            int propertyId,
            int? packageId = null,
            decimal? propertyEventCommissionOverride = null,
            int? eventManagerUserId = null);
    }

    public class CommissionService : ICommissionService
    {
        private readonly IConfigRepository _configRepo;
        private readonly IUserRepository _userRepo;
        private readonly IPropertyRepository? _propRepo;
        private readonly IEventPackageRepository? _eventPackageRepo;

        public CommissionService(
            IConfigRepository configRepo, 
            IUserRepository userRepo,
            IPropertyRepository? propRepo = null,
            IEventPackageRepository? eventPackageRepo = null)
        {
            _configRepo = configRepo;
            _userRepo = userRepo;
            _propRepo = propRepo;
            _eventPackageRepo = eventPackageRepo;
        }

        public async Task<(decimal commissionAmount, decimal ownerPayout)> CalculateCommissionAsync(decimal totalBookingAmount)
        {
            var res = await CalculateDynamicCommissionAsync(totalBookingAmount, null, null);
            return (res.commissionAmount, res.ownerPayout);
        }

        public async Task<(decimal rateSnapshot, decimal commissionAmount, decimal ownerPayout)> CalculateDynamicCommissionAsync(decimal totalBookingAmount, decimal? propertyCommissionRate, int? ownerId = null)
        {
            decimal effectiveRate = 10.0m;

            // 1. Property-level override
            if (propertyCommissionRate.HasValue && propertyCommissionRate.Value >= 0)
            {
                effectiveRate = propertyCommissionRate.Value;
            }
            // 2. Owner-level custom rate
            else if (ownerId.HasValue)
            {
                var owner = await _userRepo.GetByIdAsync(ownerId.Value);
                if (owner?.CustomCommissionRate.HasValue == true && owner.CustomCommissionRate.Value >= 0)
                {
                    effectiveRate = owner.CustomCommissionRate.Value;
                }
                else
                {
                    effectiveRate = await GetGlobalDefaultCommissionAsync();
                }
            }
            // 3. Global settings default
            else
            {
                effectiveRate = await GetGlobalDefaultCommissionAsync();
            }

            var commission = Math.Round((totalBookingAmount * effectiveRate) / 100m, 2);
            var ownerPayout = Math.Max(0, totalBookingAmount - commission);
            return (effectiveRate, commission, ownerPayout);
        }

        public async Task<(decimal rateSnapshot, decimal commissionAmount, decimal managerPayout)> CalculateEventCommissionAsync(
            decimal eventPackagePrice,
            int propertyId,
            int? packageId = null,
            decimal? propertyEventCommissionOverride = null,
            int? eventManagerUserId = null)
        {
            if (eventPackagePrice <= 0)
            {
                return (0.00m, 0.00m, 0.00m);
            }

            decimal effectiveRate = 10.0m;
            bool rateResolved = false;

            // 1. Property-specific Event Package Override (Highest Precedence)
            if (_propRepo != null && propertyId > 0 && packageId.HasValue)
            {
                var customPkgComm = await _propRepo.GetPropertyEventCommissionRateAsync(propertyId, packageId.Value);
                if (customPkgComm.HasValue && customPkgComm.Value >= 0)
                {
                    effectiveRate = customPkgComm.Value;
                    rateResolved = true;
                }
            }

            // 2. Property-level Event Commission Override
            if (!rateResolved)
            {
                if (propertyEventCommissionOverride.HasValue && propertyEventCommissionOverride.Value >= 0)
                {
                    effectiveRate = propertyEventCommissionOverride.Value;
                    rateResolved = true;
                }
                else if (_propRepo != null && propertyId > 0)
                {
                    var prop = await _propRepo.GetByIdAsync(propertyId);
                    if (prop?.EventCommissionRate.HasValue == true && prop.EventCommissionRate.Value >= 0)
                    {
                        effectiveRate = prop.EventCommissionRate.Value;
                        rateResolved = true;
                    }
                }
            }

            // 3. Event Package-level Custom Commission Rate
            if (!rateResolved && _eventPackageRepo != null && packageId.HasValue)
            {
                var pkg = await _eventPackageRepo.GetPackageByIdAsync(packageId.Value);
                if (pkg?.CommissionRate.HasValue == true && pkg.CommissionRate.Value >= 0)
                {
                    effectiveRate = pkg.CommissionRate.Value;
                    rateResolved = true;
                }
            }

            // 4. Event Manager Custom Commission
            if (!rateResolved && eventManagerUserId.HasValue)
            {
                var mgr = await _userRepo.GetByIdAsync(eventManagerUserId.Value);
                if (mgr?.CustomCommissionRate.HasValue == true && mgr.CustomCommissionRate.Value >= 0)
                {
                    effectiveRate = mgr.CustomCommissionRate.Value;
                    rateResolved = true;
                }
            }

            // 5. Standard Global Event Platform Commission Percentage
            if (!rateResolved)
            {
                var commVal = await _configRepo.GetSettingValueAsync("EventPlatformCommissionPercent");
                if (!string.IsNullOrEmpty(commVal) && decimal.TryParse(commVal, out var parsed))
                {
                    effectiveRate = parsed;
                }
                else
                {
                    effectiveRate = await GetGlobalDefaultCommissionAsync();
                }
            }

            var commission = Math.Round((eventPackagePrice * effectiveRate) / 100m, 2);
            var managerPayout = Math.Max(0, eventPackagePrice - commission);
            return (effectiveRate, commission, managerPayout);
        }

        private async Task<decimal> GetGlobalDefaultCommissionAsync()
        {
            var commVal = await _configRepo.GetSettingValueAsync("PlatformCommissionPercent");
            if (!string.IsNullOrEmpty(commVal) && decimal.TryParse(commVal, out var parsed))
            {
                return parsed;
            }
            return 10.0m;
        }
    }
}
