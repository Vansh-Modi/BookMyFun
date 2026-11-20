using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;

namespace BookMyFun.Services.Checkout
{
    public class CategoryCountDto
    {
        public string Category { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class ManagerPackageCardDto
    {
        public int PackageId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int ManagerId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
        public double? Rating { get; set; }
        public int? EventsCompleted { get; set; }
        public string ProfileLink { get; set; } = string.Empty;
    }

    public class StayCelebrationEligibilityResult
    {
        public bool Available { get; set; }
        public string ReasonCode { get; set; } = "ok"; // too_soon, property_not_live, no_area_coverage, no_verified_manager, no_approved_portfolio, ok
        public string? ReasonMessage { get; set; }
        public List<CategoryCountDto> Categories { get; set; } = new();
        public List<ManagerPackageCardDto> Packages { get; set; } = new();
        public List<ManagerPackageCardDto> AlternativePackages { get; set; } = new();
    }

    public interface IStayCelebrationEligibilityService
    {
        Task<StayCelebrationEligibilityResult> CheckEligibilityAsync(
            int propertyId,
            DateTime checkInDate,
            TimeSpan? checkInTime = null,
            string? chosenCategory = null,
            string? sortBy = "rating");

        Task<(bool IsEligible, string ReasonCode, string? ErrorMessage, EventPackage? Package, User? Manager)> ValidateBookingPackageAsync(
            int propertyId,
            int packageId,
            DateTime checkInDate,
            TimeSpan? checkInTime = null);

        void InvalidateCache(int? propertyId = null);
        string NormalizeArea(string? area);
        List<(string City, string State)> ParseServiceAreas(string? serviceAreas);
    }
}
