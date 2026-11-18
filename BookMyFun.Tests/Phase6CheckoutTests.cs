using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services.Checkout;
using Xunit;

namespace BookMyFun.Tests;

public class Phase6CheckoutTests
{
    private class FakeEventPackageRepository : IEventPackageRepository
    {
        public List<User> Decorators = new();
        public List<EventPackage> Packages = new();
        public List<EventManagerPortfolio> Portfolios = new();

        public Task<List<User>> GetDecoratorsAsync(bool onlyVerified = false)
        {
            var list = Decorators.AsEnumerable();
            if (onlyVerified)
                list = list.Where(d => d.VerificationStatus == "Verified");
            return Task.FromResult(list.ToList());
        }

        public Task<List<EventPackage>> GetPackagesByUserIdAsync(int userId)
        {
            return Task.FromResult(Packages.Where(p => p.CreatedByUserID == userId).ToList());
        }

        public Task<EventPackage?> GetPackageByIdAsync(int packageId)
        {
            return Task.FromResult(Packages.FirstOrDefault(p => p.PackageID == packageId));
        }

        public Task<List<EventManagerPortfolio>> GetPortfoliosByUserIdAsync(int userId, bool onlyApproved = true)
        {
            var list = Portfolios.Where(p => p.UserID == userId);
            if (onlyApproved)
                list = list.Where(p => p.IsApproved);
            return Task.FromResult(list.ToList());
        }

        public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(true);
        public Task<List<EventPackage>> GetAllPackagesAsync() => Task.FromResult(Packages);
        public Task<List<EventPackage>> GetActivePackagesAsync() => Task.FromResult(Packages.Where(p => p.IsActive).ToList());
        public Task<int> CreatePackageAsync(EventPackage package) => Task.FromResult(1);
        public Task<bool> UpdatePackageAsync(EventPackage package) => Task.FromResult(true);
        public Task<bool> UpdatePackageCommissionRateAsync(int packageId, decimal? commissionRate) => Task.FromResult(true);
        public Task<bool> DeletePackageAsync(int packageId) => Task.FromResult(true);
        public Task<List<Booking>> GetAssignedEventBookingsAsync(int decoratorUserId) => Task.FromResult(new List<Booking>());
        public Task<List<Booking>> GetAllEventBookingsAsync() => Task.FromResult(new List<Booking>());
        public Task<bool> UpdateSetupStatusAsync(int bookingId, string setupStatus, string? setupProofMediaUrls, int? decoratorId) => Task.FromResult(true);
        public Task<bool> AssignDecoratorAsync(int bookingId, int decoratorUserId) => Task.FromResult(true);
        public Task<bool> CancelOrRemoveEventFromBookingAsync(int bookingId) => Task.FromResult(true);
        public Task<List<string>> GetAllDistinctPropertyLocationsAsync() => Task.FromResult(new List<string>());
        public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>(null);
        public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas) => Task.FromResult(true);
        public Task<int> AddPortfolioItemAsync(EventManagerPortfolio item) => Task.FromResult(1);
        public Task<List<EventManagerPortfolio>> GetAllPortfoliosAsync() => Task.FromResult(Portfolios);
        public Task<bool> ApprovePortfolioItemAsync(int portfolioId, bool isApproved) => Task.FromResult(true);
        public Task<bool> DeletePortfolioItemAsync(int portfolioId, int userId) => Task.FromResult(true);
        public Task<List<FlaggedContactAttempt>> GetFlaggedContactAttemptsAsync() => Task.FromResult(new List<FlaggedContactAttempt>());
        public Task<bool> UpdateFlaggedContactAttemptStatusAsync(int flagId, string status) => Task.FromResult(true);
    }

    private class FakeUserRepository : IUserRepository
    {
        public List<User> Users = new();

        public Task<User?> GetByIdAsync(int userId) => Task.FromResult(Users.FirstOrDefault(u => u.UserID == userId));
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult(Users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));
        public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult(Users.FirstOrDefault(u => u.AuthProvider == provider && u.AuthProviderKey == providerKey));
        public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(Users.Where(u => u.Role.Equals(role, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<List<User>> GetAllUsersAsync() => Task.FromResult(Users);
        public Task<int> CreateUserAsync(User user) => Task.FromResult(1);
        public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
        public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
        public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
        public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
        public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate) => Task.FromResult(true);
        public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
    }

    private class FakePropertyRepository : IPropertyRepository
    {
        public List<Occasion> Occasions = new();
        public Dictionary<int, List<int>> PropertyOccasions = new();

        public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(Occasions);
        public Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId)
        {
            if (PropertyOccasions.TryGetValue(propertyId, out var list))
                return Task.FromResult(list);
            return Task.FromResult(new List<int>());
        }

        public Task<Property?> GetByIdAsync(int propertyId) => Task.FromResult<Property?>(new Property { PropertyID = propertyId, Title = "Sample Villa", Location = "Lonavala, Maharashtra" });
        public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(new List<Property>());
        public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Property>());
        public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests) => Task.FromResult(new List<Property>());
        public Task<int> CreatePropertyAsync(Property property) => Task.FromResult(1);
        public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
        public Task<bool> UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl) => Task.FromResult(true);
        public Task<bool> DeletePropertyAsync(int propertyId) => Task.FromResult(true);
        public Task<bool> PermanentlyDeletePropertyAsync(int propertyId) => Task.FromResult(true);
        public Task<bool> DeactivatePropertyAsync(int propertyId, bool deactivate = true) => Task.FromResult(true);
        public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null) => Task.FromResult(true);
        public Task<bool> SetPropertyOccasionsAsync(int propertyId, List<int> occasionIds) => Task.FromResult(true);
        public Task<int> AddPropertyImageAsync(int propertyId, string imageUrl, bool isPrimary) => Task.FromResult(1);
        public Task<List<PropertyImage>> GetPropertyImagesAsync(int propertyId) => Task.FromResult(new List<PropertyImage>());
        public Task<int> AddPropertyMediaAsync(PropertyMedia media) => Task.FromResult(1);
        public Task<List<PropertyMedia>> GetPropertyMediaAsync(int propertyId) => Task.FromResult(new List<PropertyMedia>());
        public Task<bool> DeletePropertyMediaAsync(int mediaId) => Task.FromResult(true);
        public Task<List<AmenityMaster>> GetAllAmenitiesAsync() => Task.FromResult(new List<AmenityMaster>());
        public Task<List<int>> GetPropertyAmenityIdsAsync(int propertyId) => Task.FromResult(new List<int>());
        public Task<List<AmenityMaster>> GetPropertyAmenitiesAsync(int propertyId) => Task.FromResult(new List<AmenityMaster>());
        public Task<bool> SetPropertyAmenitiesAsync(int propertyId, List<int> amenityIds) => Task.FromResult(true);
        public Task<List<PropertyType>> GetPropertyTypesAsync() => Task.FromResult(new List<PropertyType>());
        public Task<int> GetOrCreatePropertyTypeByNameAsync(string name) => Task.FromResult(1);
        public Task<bool> UpdatePropertyCategoryAsync(int propertyId, int ownerId, int propertyTypeId) => Task.FromResult(true);
        public Task<int> GetOrCreateAmenityByNameAsync(string name) => Task.FromResult(1);
        public Task<int> GetOrCreateOccasionByNameAsync(string name) => Task.FromResult(1);
        public Task<List<PropertyEventCommission>> GetPropertyEventCommissionsAsync(int propertyId) => Task.FromResult(new List<PropertyEventCommission>());
        public Task<decimal?> GetPropertyEventCommissionRateAsync(int propertyId, int packageId) => Task.FromResult<decimal?>(null);
        public Task<bool> SetPropertyEventCommissionsAsync(int propertyId, List<(int packageId, decimal commissionRate)> commissions) => Task.FromResult(true);
    }

    [Fact]
    public void MatchesOccasion_ExactAndTokenMatching_WorksCorrectly()
    {
        var service = new CheckoutEventManagerService(new FakeEventPackageRepository(), new FakeUserRepository(), new FakePropertyRepository());

        // Exact & case-insensitive
        Assert.True(service.MatchesOccasion("Birthday", "Birthday"));
        Assert.True(service.MatchesOccasion("wedding", "Wedding"));

        // Tokenized / Substring
        Assert.True(service.MatchesOccasion("Corporate", "Corporate Retreat"));
        Assert.True(service.MatchesOccasion("Corporate Gala", "Corporate Retreat"));
        Assert.True(service.MatchesOccasion("Family", "Family Gathering"));

        // Universal categories
        Assert.True(service.MatchesOccasion("Custom", "Wedding"));
        Assert.True(service.MatchesOccasion("All", "Birthday"));
        Assert.True(service.MatchesOccasion("General", "Outing"));

        // Non-matching
        Assert.False(service.MatchesOccasion("Wedding", "Corporate Retreat"));
        Assert.False(service.MatchesOccasion("Birthday", "Couple Stay"));
    }

    [Fact]
    public void ServicesLocation_AreaCheck_ReturnsCorrectly()
    {
        var service = new CheckoutEventManagerService(new FakeEventPackageRepository(), new FakeUserRepository(), new FakePropertyRepository());

        Assert.True(service.ServicesLocation("Lonavala; Khandala; Mumbai", "Lonavala, Maharashtra"));
        Assert.True(service.ServicesLocation("Pune, Maharashtra", "Pune"));
        Assert.False(service.ServicesLocation("Goa", "Lonavala, Maharashtra"));
        Assert.False(service.ServicesLocation(null, "Lonavala, Maharashtra"));
    }

    [Fact]
    public async Task ValidateSelectedPackage_Rejects_InvalidPackageOrUnverifiedManager()
    {
        var fakeEventRepo = new FakeEventPackageRepository();
        var fakeUserRepo = new FakeUserRepository();
        var fakePropRepo = new FakePropertyRepository();

        // 1. Unverified manager
        fakeUserRepo.Users.Add(new User
        {
            UserID = 101,
            FullName = "Unverified Decorator",
            Role = "EventManager",
            IsActive = true,
            VerificationStatus = "Pending", // NOT verified
            ServiceAreas = "Lonavala"
        });

        fakeEventRepo.Packages.Add(new EventPackage
        {
            PackageID = 1,
            Name = "Rustic Canopy",
            Category = "Wedding",
            BasePrice = 25000,
            IsActive = true,
            CreatedByUserID = 101
        });

        fakePropRepo.Occasions.Add(new Occasion { OccasionID = 1, OccasionName = "Wedding" });

        var service = new CheckoutEventManagerService(fakeEventRepo, fakeUserRepo, fakePropRepo);

        // Act
        var result = await service.ValidateSelectedPackageAsync(1, 1, "Lonavala, Maharashtra");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("not currently active and verified", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateSelectedPackage_Rejects_WhenLocationNotServiced()
    {
        var fakeEventRepo = new FakeEventPackageRepository();
        var fakeUserRepo = new FakeUserRepository();
        var fakePropRepo = new FakePropertyRepository();

        fakeUserRepo.Users.Add(new User
        {
            UserID = 102,
            FullName = "Goa Decorators",
            Role = "EventManager",
            IsActive = true,
            VerificationStatus = "Verified",
            ServiceAreas = "Goa" // Only Goa
        });

        fakeEventRepo.Packages.Add(new EventPackage
        {
            PackageID = 2,
            Name = "Beach Archway",
            Category = "Wedding",
            BasePrice = 30000,
            IsActive = true,
            CreatedByUserID = 102
        });

        fakePropRepo.Occasions.Add(new Occasion { OccasionID = 1, OccasionName = "Wedding" });

        var service = new CheckoutEventManagerService(fakeEventRepo, fakeUserRepo, fakePropRepo);

        // Act - Property is in Lonavala
        var result = await service.ValidateSelectedPackageAsync(2, 1, "Lonavala, Maharashtra");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("does not service the location", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateSelectedPackage_Rejects_WhenPackageCategoryMismatchesOccasion()
    {
        var fakeEventRepo = new FakeEventPackageRepository();
        var fakeUserRepo = new FakeUserRepository();
        var fakePropRepo = new FakePropertyRepository();

        fakeUserRepo.Users.Add(new User
        {
            UserID = 103,
            FullName = "Premier Events",
            Role = "EventManager",
            IsActive = true,
            VerificationStatus = "Verified",
            ServiceAreas = "Lonavala; Khandala"
        });

        fakeEventRepo.Packages.Add(new EventPackage
        {
            PackageID = 3,
            Name = "Corporate Stage & Podium",
            Category = "Corporate", // Corporate package
            BasePrice = 15000,
            IsActive = true,
            CreatedByUserID = 103
        });

        fakePropRepo.Occasions.Add(new Occasion { OccasionID = 5, OccasionName = "Wedding" }); // User chose Wedding

        var service = new CheckoutEventManagerService(fakeEventRepo, fakeUserRepo, fakePropRepo);

        // Act
        var result = await service.ValidateSelectedPackageAsync(3, 5, "Lonavala, Maharashtra");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("not offered for the selected occasion", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateSelectedPackage_Succeeds_ForValidPackageAndVerifiedManager()
    {
        var fakeEventRepo = new FakeEventPackageRepository();
        var fakeUserRepo = new FakeUserRepository();
        var fakePropRepo = new FakePropertyRepository();

        fakeUserRepo.Users.Add(new User
        {
            UserID = 104,
            FullName = "Royal Decorators",
            Role = "EventManager",
            IsActive = true,
            VerificationStatus = "Verified",
            ServiceAreas = "Lonavala; Khandala; Pune"
        });

        fakeEventRepo.Packages.Add(new EventPackage
        {
            PackageID = 4,
            Name = "Royal Floral Mandap",
            Category = "Wedding",
            BasePrice = 45000,
            IsActive = true,
            CreatedByUserID = 104
        });

        fakePropRepo.Occasions.Add(new Occasion { OccasionID = 1, OccasionName = "Wedding" });

        var service = new CheckoutEventManagerService(fakeEventRepo, fakeUserRepo, fakePropRepo);

        // Act
        var result = await service.ValidateSelectedPackageAsync(4, 1, "Lonavala, Maharashtra");

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Package);
        Assert.Equal(45000m, result.Package.BasePrice);
        Assert.Equal(104, result.Manager?.UserID);
    }

    [Fact]
    public async Task GetAvailableEventManagers_FiltersToVerifiedManagersWithMatchingPackages()
    {
        var fakeEventRepo = new FakeEventPackageRepository();
        var fakeUserRepo = new FakeUserRepository();
        var fakePropRepo = new FakePropertyRepository();

        // Manager 1: Verified, services Lonavala, has Birthday package
        fakeEventRepo.Decorators.Add(new User
        {
            UserID = 201,
            FullName = "Party Magic Studios",
            Role = "EventManager",
            IsActive = true,
            VerificationStatus = "Verified",
            ServiceAreas = "Lonavala, Maharashtra",
            YearsOfExperience = 4
        });
        fakeEventRepo.Packages.Add(new EventPackage
        {
            PackageID = 11,
            Name = "Neon Glow Birthday",
            Category = "Birthday",
            BasePrice = 20000,
            IsActive = true,
            CreatedByUserID = 201
        });

        // Manager 2: Verified, services Lonavala, but ONLY has Wedding package (no Birthday)
        fakeEventRepo.Decorators.Add(new User
        {
            UserID = 202,
            FullName = "Exclusive Weddings Only",
            Role = "EventManager",
            IsActive = true,
            VerificationStatus = "Verified",
            ServiceAreas = "Lonavala, Maharashtra"
        });
        fakeEventRepo.Packages.Add(new EventPackage
        {
            PackageID = 12,
            Name = "Grand Mandap",
            Category = "Wedding",
            BasePrice = 80000,
            IsActive = true,
            CreatedByUserID = 202
        });

        fakePropRepo.Occasions.Add(new Occasion { OccasionID = 2, OccasionName = "Birthday Party" });

        var service = new CheckoutEventManagerService(fakeEventRepo, fakeUserRepo, fakePropRepo);

        // Act - Query for Birthday Party (OccasionID 2)
        var managers = await service.GetAvailableEventManagersAsync("Lonavala, Maharashtra", 2);

        // Assert - Only Manager 1 should be included because Manager 2 has no packages for Birthday
        Assert.Single(managers);
        Assert.Equal(201, managers[0].UserID);
        Assert.Equal("Party Magic Studios", managers[0].DisplayName);
        Assert.Equal(20000m, managers[0].StartingPrice);
        Assert.Equal("/images/default-avatar.svg", managers[0].ProfileImageUrl);
    }

    [Fact]
    public void CheckoutEventManagerViewModel_ContainsNoContactInformation()
    {
        var props = typeof(CheckoutEventManagerViewModel).GetProperties();
        var propNames = props.Select(p => p.Name.ToLowerInvariant()).ToList();

        // Rule: Never show phone, email, WhatsApp, social links, address, or any contact field
        Assert.DoesNotContain("phone", propNames);
        Assert.DoesNotContain("email", propNames);
        Assert.DoesNotContain("whatsapp", propNames);
        Assert.DoesNotContain("social", propNames);
        Assert.DoesNotContain("address", propNames);
    }
}
