using BookMyFun.Models.Entities;
using BookMyFun.Repositories;

namespace BookMyFun.Services
{
    /// <summary>
    /// Service for calculating booking pricing based on duration, slot type, and base property rate.
    /// </summary>
    public interface IBookingPricingService
    {
        decimal CalculatePrice(decimal pricePerNight, string bookingType, string? slotType, int daysOrHours);
    }

    /// <summary>
    /// Implementation of booking pricing calculations for FullDay, HalfDay, and Hourly reservation types.
    /// </summary>
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
}
