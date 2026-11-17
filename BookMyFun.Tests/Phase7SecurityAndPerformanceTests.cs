using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Security;
using BookMyFun.Services.Staff;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BookMyFun.Tests
{
    public class Phase7SecurityAndPerformanceTests
    {
        [Fact]
        public async Task RateLimitingMiddleware_Enforces_60_Per_Minute_On_Eligibility_Endpoint()
        {
            // Arrange
            RequestDelegate next = ctx => Task.CompletedTask;
            var middleware = new RateLimitingMiddleware(next);
            string testIp = "192.168.1.101";

            // Act: 60 requests succeed
            for (int i = 0; i < 60; i++)
            {
                var context = new DefaultHttpContext();
                context.Connection.RemoteIpAddress = IPAddress.Parse(testIp);
                context.Request.Path = "/booking/celebrationeligibility";
                context.Request.Method = "GET";

                await middleware.InvokeAsync(context);
                Assert.NotEqual(429, context.Response.StatusCode);
            }

            // Act: 61st request is rate limited
            var blockedContext = new DefaultHttpContext();
            blockedContext.Connection.RemoteIpAddress = IPAddress.Parse(testIp);
            blockedContext.Request.Path = "/booking/celebrationeligibility";
            blockedContext.Request.Method = "GET";
            blockedContext.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(blockedContext);

            // Assert
            Assert.Equal(429, blockedContext.Response.StatusCode);
            Assert.Equal("60", blockedContext.Response.Headers["Retry-After"]);
        }

        [Fact]
        public async Task RateLimitingMiddleware_Enforces_10_Per_Minute_On_Booking_Submit()
        {
            // Arrange
            RequestDelegate next = ctx => Task.CompletedTask;
            var middleware = new RateLimitingMiddleware(next);
            string testIp = "192.168.1.102";

            // Act: 10 POST requests succeed
            for (int i = 0; i < 10; i++)
            {
                var context = new DefaultHttpContext();
                context.Connection.RemoteIpAddress = IPAddress.Parse(testIp);
                context.Request.Path = "/booking/confirmbooking";
                context.Request.Method = "POST";

                await middleware.InvokeAsync(context);
                Assert.NotEqual(429, context.Response.StatusCode);
            }

            // Act: 11th request is blocked
            var blockedContext = new DefaultHttpContext();
            blockedContext.Connection.RemoteIpAddress = IPAddress.Parse(testIp);
            blockedContext.Request.Path = "/booking/confirmbooking";
            blockedContext.Request.Method = "POST";
            blockedContext.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(blockedContext);

            // Assert
            Assert.Equal(429, blockedContext.Response.StatusCode);
            Assert.Equal("60", blockedContext.Response.Headers["Retry-After"]);
        }

        [Fact]
        public async Task RateLimitingMiddleware_Enforces_120_Per_Minute_On_Notification_MarkAsRead()
        {
            // Arrange
            RequestDelegate next = ctx => Task.CompletedTask;
            var middleware = new RateLimitingMiddleware(next);
            string userId = "user-999";

            // Act: 120 requests succeed
            for (int i = 0; i < 120; i++)
            {
                var context = new DefaultHttpContext();
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }));
                context.Request.Path = "/notification/markasread";
                context.Request.Method = "POST";

                await middleware.InvokeAsync(context);
                Assert.NotEqual(429, context.Response.StatusCode);
            }

            // Act: 121st request is blocked
            var blockedContext = new DefaultHttpContext();
            blockedContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }));
            blockedContext.Request.Path = "/notification/markasread";
            blockedContext.Request.Method = "POST";
            blockedContext.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(blockedContext);

            // Assert
            Assert.Equal(429, blockedContext.Response.StatusCode);
            Assert.Equal("60", blockedContext.Response.Headers["Retry-After"]);
        }

        [Fact]
        public async Task RateLimitingMiddleware_Enforces_60_Per_Minute_On_Staff_Queue()
        {
            // Arrange
            RequestDelegate next = ctx => Task.CompletedTask;
            var middleware = new RateLimitingMiddleware(next);
            string staffId = "staff-555";

            // Act: 60 requests succeed
            for (int i = 0; i < 60; i++)
            {
                var context = new DefaultHttpContext();
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, staffId) }));
                context.Request.Path = "/admin/needsattention";
                context.Request.Method = "GET";

                await middleware.InvokeAsync(context);
                Assert.NotEqual(429, context.Response.StatusCode);
            }

            // Act: 61st request is blocked
            var blockedContext = new DefaultHttpContext();
            blockedContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, staffId) }));
            blockedContext.Request.Path = "/admin/needsattention";
            blockedContext.Request.Method = "GET";
            blockedContext.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(blockedContext);

            // Assert
            Assert.Equal(429, blockedContext.Response.StatusCode);
            Assert.Equal("60", blockedContext.Response.Headers["Retry-After"]);
        }

        [Fact]
        public void StaffControllers_Post_Endpoints_Require_ValidateAntiForgeryToken()
        {
            // Verify that all POST actions on Admin, Employee, Owner, and Booking controllers have ValidateAntiForgeryToken
            var controllerTypes = new[]
            {
                typeof(AdminController),
                typeof(EmployeeController),
                typeof(OwnerController),
                typeof(BookingController),
                typeof(EventManagerController)
            };

            var nonCompliantActions = new List<string>();

            foreach (var controller in controllerTypes)
            {
                var postMethods = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(m => m.GetCustomAttributes<HttpPostAttribute>().Any())
                    .ToList();

                foreach (var method in postMethods)
                {
                    bool hasToken = method.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Any() ||
                                    method.GetCustomAttributes<AutoValidateAntiforgeryTokenAttribute>().Any();

                    if (!hasToken)
                    {
                        nonCompliantActions.Add($"{controller.Name}.{method.Name}");
                    }
                }
            }

            Assert.Empty(nonCompliantActions);
        }

        [Fact]
        public async Task StaffQueueService_Clamps_PageSize_To_Maximum_25()
        {
            var userRepo = new FakeUserRepo();
            var eventRepo = new FakeEventPackageRepo();
            var propRepo = new FakePropertyRepo();
            var idService = new FakeIdService();
            var leadService = new FakeLeadService();
            var refundEngine = new FakeRefundEngine();
            var damageService = new FakeDamageService();

            var queueService = new StaffQueueService(userRepo, eventRepo, propRepo, idService, leadService, refundEngine, damageService);

            // Request pageSize = 100
            var result = await queueService.GetQueueAsync(null, 1, 100);

            // Must be clamped to 25
            Assert.Equal(25, result.PageSize);
        }

        #region Minimal Fakes for PageSize Clamping Test

        private class FakeUserRepo : IUserRepository
        {
            public Task<User?> GetByIdAsync(int userId) => Task.FromResult<User?>(null);
            public Task<User?> GetByEmailAsync(string email) => Task.FromResult<User?>(null);
            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult<User?>(null);
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(new List<User>());
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(new List<User>());
            public Task<int> CreateUserAsync(User user) => Task.FromResult(1);
            public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
        }

        private class FakeEventPackageRepo : IEventPackageRepository
        {
            public Task<List<EventPackage>> GetAllPackagesAsync() => Task.FromResult(new List<EventPackage>());
            public Task<List<EventPackage>> GetActivePackagesAsync() => Task.FromResult(new List<EventPackage>());
            public Task<List<EventPackage>> GetPackagesByUserIdAsync(int userId) => Task.FromResult(new List<EventPackage>());
            public Task<EventPackage?> GetPackageByIdAsync(int packageId) => Task.FromResult<EventPackage?>(null);
            public Task<int> CreatePackageAsync(EventPackage package) => Task.FromResult(1);
            public Task<bool> UpdatePackageAsync(EventPackage package) => Task.FromResult(true);
            public Task<bool> UpdatePackageCommissionRateAsync(int packageId, decimal? commissionRate) => Task.FromResult(true);
            public Task<bool> DeletePackageAsync(int packageId) => Task.FromResult(true);
            public Task<List<Booking>> GetAssignedEventBookingsAsync(int decoratorUserId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllEventBookingsAsync() => Task.FromResult(new List<Booking>());
            public Task<bool> UpdateSetupStatusAsync(int bookingId, string setupStatus, string? setupProofMediaUrls, int? decoratorId) => Task.FromResult(true);
            public Task<bool> AssignDecoratorAsync(int bookingId, int decoratorUserId) => Task.FromResult(true);
            public Task<bool> CancelOrRemoveEventFromBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<List<User>> GetDecoratorsAsync(bool onlyVerified = false) => Task.FromResult(new List<User>());
            public Task<List<string>> GetAllDistinctPropertyLocationsAsync() => Task.FromResult(new List<string>());
            public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>(null);
            public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas) => Task.FromResult(true);
            public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(true);
            public Task<int> AddPortfolioItemAsync(EventManagerPortfolio item) => Task.FromResult(1);
            public Task<List<EventManagerPortfolio>> GetPortfoliosByUserIdAsync(int userId, bool onlyApproved = true) => Task.FromResult(new List<EventManagerPortfolio>());
            public Task<HashSet<int>> GetUserIdsWithApprovedPortfoliosAsync() => Task.FromResult(new HashSet<int>());
            public Task<List<EventManagerPortfolio>> GetAllPortfoliosAsync() => Task.FromResult(new List<EventManagerPortfolio>());
            public Task<bool> ApprovePortfolioItemAsync(int portfolioId, bool isApproved) => Task.FromResult(true);
            public Task<bool> DeletePortfolioItemAsync(int portfolioId, int userId) => Task.FromResult(true);
            public Task<List<FlaggedContactAttempt>> GetFlaggedContactAttemptsAsync() => Task.FromResult(new List<FlaggedContactAttempt>());
            public Task<bool> UpdateFlaggedContactAttemptStatusAsync(int flagId, string status) => Task.FromResult(true);
        }

        private class FakePropertyRepo : IPropertyRepository
        {
            public Task<Property?> GetByIdAsync(int propertyId) => Task.FromResult<Property?>(null);
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
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(new List<Occasion>());
            public Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId) => Task.FromResult(new List<int>());
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

        private class FakeIdService : IIdVerificationService
        {
            public Task<int> SubmitVerificationAsync(int clientId, string documentType, string storageKey, string? maskedIdentifier) => Task.FromResult(1);
            public Task<ClientIdVerification?> GetClientVerificationAsync(int clientId, int requestingUserId, string userRole) => Task.FromResult<ClientIdVerification?>(null);
            public Task<List<ClientIdVerification>> GetAllVerificationsAsync(string? status = null) => Task.FromResult(new List<ClientIdVerification>());
            public Task<bool> ReviewVerificationAsync(int verificationId, string action, int staffUserId, string? rejectionReason = null) => Task.FromResult(true);
            public Task<bool> IsClientVerifiedAsync(int clientId) => Task.FromResult(true);
        }

        private class FakeLeadService : IPartnerLeadService
        {
            public Task<int> CreateLeadAsync(PartnerLeadCreateViewModel model) => Task.FromResult(1);
            public Task<List<PartnerLead>> GetAllLeadsAsync(string? status = null) => Task.FromResult(new List<PartnerLead>());
            public Task<PartnerLead> GetLeadByIdAsync(int leadId) => Task.FromResult(new PartnerLead());
            public Task<(bool success, string generatedPassword, string activationToken, int accountId, string roleDisplayName, string email, string? phone)> ApproveLeadAsync(int leadId, int staffUserId) =>
                Task.FromResult((true, "p", "t", 1, "Owner", "e", (string?)"p"));
            public Task<(bool success, int userId, string generatedPassword, string email, string? phone, string fullName)> ApproveOwnerRequestAsync(int requestId, int staffUserId, string? notes) =>
                Task.FromResult((true, 1, "p", "e", (string?)"p", "f"));
            public Task<bool> RejectLeadAsync(int leadId, int staffUserId) => Task.FromResult(true);
            public Task<bool> ActivateOwnerAccountAsync(ActivateOwnerViewModel model) => Task.FromResult(true);
        }

        private class FakeRefundEngine : IRefundEngineService
        {
            public CancellationQuoteViewModel CalculateCancellationQuote(Booking booking, DateTime? checkTime = null, bool isOwner = false) => new();
            public Task<CancellationQuoteViewModel> CalculateCancellationQuoteAsync(Booking booking, DateTime? checkTime = null, bool isOwner = false) => Task.FromResult(new CancellationQuoteViewModel());
            public Task<BookingRefund> ProcessCancellationAndRefundAsync(int bookingId, int requestingUserId, string userRole, string? reason, bool forceOverride = false, int? specificRule = null, decimal? customRefundAmount = null, bool issueCredit = false) =>
                Task.FromResult(new BookingRefund());
            public Task<List<BookingRefund>> GetRefundsQueueAsync(string? status = null) => Task.FromResult(new List<BookingRefund>());
            public Task<bool> OverrideRefundStatusAsync(int refundId, string newStatus, int staffUserId, string? notes) => Task.FromResult(true);
            public Task<BookingRefund> AdjudicateAdminRefundAsync(int refundId, int adminUserId, string action, decimal refundAmount, string reason, bool issueAsCredit = false) => Task.FromResult(new BookingRefund());
        }

        private class FakeDamageService : IDamageClaimService
        {
            public Task<int> CreateClaimAsync(DamageClaimCreateViewModel model, int hostUserId, bool isStaff = false) => Task.FromResult(1);
            public Task<List<DamageClaim>> GetAllClaimsAsync(string? status = null) => Task.FromResult(new List<DamageClaim>());
            public Task<List<DamageClaim>> GetClaimsByOwnerAsync(int ownerId) => Task.FromResult(new List<DamageClaim>());
            public Task<DamageClaim> GetClaimByIdAsync(int claimId) => Task.FromResult(new DamageClaim());
            public Task<bool> AdjudicateClaimAsync(DamageClaimAdjudicateViewModel model, int staffUserId) => Task.FromResult(true);
        }

        #endregion
    }
}
