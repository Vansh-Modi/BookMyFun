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
    }

    public class CommissionService : ICommissionService
    {
        private readonly IConfigRepository _configRepo;
        private readonly IUserRepository _userRepo;

        public CommissionService(IConfigRepository configRepo, IUserRepository userRepo)
        {
            _configRepo = configRepo;
            _userRepo = userRepo;
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
