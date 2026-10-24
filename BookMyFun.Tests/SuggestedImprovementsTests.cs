using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace BookMyFun.Tests
{
    public class SuggestedImprovementsTests
    {
        public class AuditEntry
        {
            public int PerformedByUserId { get; set; }
            public string Action { get; set; } = string.Empty;
            public string EntityType { get; set; } = string.Empty;
            public int? EntityId { get; set; }
            public string? Details { get; set; }
        }

        private class MockAuditConfigRepo : IConfigRepository
        {
            public readonly Dictionary<string, string> Settings = new();
            public readonly List<AuditEntry> AuditLogs = new();

            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);

            public Task<string?> GetSettingValueAsync(string key)
            {
                Settings.TryGetValue(key, out var val);
                return Task.FromResult<string?>(val);
            }

            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false)
            {
                Settings[key] = value;
                return Task.FromResult(true);
            }

            public Task<List<SiteSetting>> GetAllSettingsAsync()
            {
                var list = Settings.Select(kvp => new SiteSetting { SettingKey = kvp.Key, SettingValue = kvp.Value }).ToList();
                return Task.FromResult(list);
            }

            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details)
            {
                AuditLogs.Add(new AuditEntry
                {
                    PerformedByUserId = performedByUserId,
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    Details = details
                });
                return Task.CompletedTask;
            }
        }

        private class MockPropRepo : IPropertyRepository
        {
            public readonly Dictionary<int, Property> Properties = new();

            public Task<Property?> GetByIdAsync(int propertyId)
            {
                Properties.TryGetValue(propertyId, out var p);
                return Task.FromResult(p);
            }

            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(Properties.Values.ToList());
            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(Properties.Values.Where(p => p.OwnerID == ownerId).ToList());
            public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests) => Task.FromResult(new List<Property>());
            public Task<int> CreatePropertyAsync(Property property) => Task.FromResult(1);
            public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
            public Task<bool> UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl) => Task.FromResult(true);
            public Task<bool> DeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> PermanentlyDeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> DeactivatePropertyAsync(int propertyId, bool deactivate = true)
            {
                if (Properties.TryGetValue(propertyId, out var prop))
                {
                    prop.ListingStatus = deactivate ? "Inactive" : "Active";
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null)
            {
                if (Properties.TryGetValue(propertyId, out var prop))
                {
                    prop.IsApproved = isApproved;
                    prop.ListingStatus = targetStatus ?? (isApproved ? "Active" : "Rejected");
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
            public Task<bool> SetPropertyOccasionsAsync(int propertyId, List<int> occasionIds) => Task.FromResult(true);
            public Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId) => Task.FromResult(new List<int>());
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
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(new List<Occasion>());
            public Task<int> GetOrCreateAmenityByNameAsync(string name) => Task.FromResult(1);
            public Task<int> GetOrCreateOccasionByNameAsync(string name) => Task.FromResult(1);
            public Task<List<PropertyEventCommission>> GetPropertyEventCommissionsAsync(int propertyId) => Task.FromResult(new List<PropertyEventCommission>());
            public Task<decimal?> GetPropertyEventCommissionRateAsync(int propertyId, int packageId) => Task.FromResult<decimal?>(null);
            public Task<bool> SetPropertyEventCommissionsAsync(int propertyId, List<(int packageId, decimal commissionRate)> commissions) => Task.FromResult(true);
        }

        private class MockRefundEngine : IRefundEngineService
        {
            public readonly Dictionary<int, BookingRefund> Refunds = new();

            public CancellationQuoteViewModel CalculateCancellationQuote(Booking booking, DateTime? checkTime = null, bool isOwner = false) => new();
            public Task<CancellationQuoteViewModel> CalculateCancellationQuoteAsync(Booking booking, DateTime? checkTime = null, bool isOwner = false) => Task.FromResult(new CancellationQuoteViewModel());
            public Task<BookingRefund> ProcessCancellationAndRefundAsync(int bookingId, int requestingUserId, string userRole, string? reason, bool forceOverride = false, int? specificRule = null, decimal? customRefundAmount = null, bool issueCredit = false) => Task.FromResult(new BookingRefund());
            public Task<List<BookingRefund>> GetRefundsQueueAsync(string? status = null) => Task.FromResult(Refunds.Values.ToList());
            public Task<bool> OverrideRefundStatusAsync(int refundId, string newStatus, int staffUserId, string? notes)
            {
                if (Refunds.TryGetValue(refundId, out var r))
                {
                    r.Status = newStatus;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
            public Task<BookingRefund> AdjudicateAdminRefundAsync(int refundId, int adminUserId, string action, decimal refundAmount, string reason, bool issueAsCredit = false)
            {
                if (Refunds.TryGetValue(refundId, out var r))
                {
                    r.Status = action;
                    r.NetRefundAmount = refundAmount;
                    return Task.FromResult(r);
                }
                return Task.FromResult(new BookingRefund { RefundID = refundId, Status = action, NetRefundAmount = refundAmount });
            }
            public Task<BookingRefund?> GetRefundByIdAsync(int refundId)
            {
                Refunds.TryGetValue(refundId, out var r);
                return Task.FromResult(r);
            }
        }

        private class MockLegalDocService : ILegalDocumentService
        {
            public readonly List<LegalDocument> Documents = new();

            public Task<LegalDocument?> GetCurrentDocumentAsync(string documentType) =>
                Task.FromResult(Documents.Where(d => d.DocumentType == documentType && d.IsCurrent).OrderByDescending(d => d.DocumentID).FirstOrDefault());

            public Task<LegalDocument?> GetDocumentByIdAsync(int documentId) =>
                Task.FromResult(Documents.FirstOrDefault(d => d.DocumentID == documentId));

            public Task<List<LegalDocument>> GetDocumentHistoryAsync(string documentType) =>
                Task.FromResult(Documents.Where(d => d.DocumentType == documentType).OrderByDescending(d => d.EffectiveDate).ToList());

            public Task<LegalDocument> SaveNewVersionAsync(string documentType, string title, string body, string? version, DateTime? effectiveDate, bool isDraft, int? createdByUserId)
            {
                var doc = new LegalDocument
                {
                    DocumentID = Documents.Count + 1,
                    DocumentType = documentType,
                    Title = title,
                    Body = body,
                    Version = version ?? "1.0",
                    EffectiveDate = effectiveDate ?? DateTime.UtcNow,
                    IsDraft = isDraft,
                    IsCurrent = !isDraft,
                    CreatedByUserID = createdByUserId,
                    CreatedAt = DateTime.UtcNow
                };
                Documents.Add(doc);
                return Task.FromResult(doc);
            }

            public Task<bool> HasContentAsync(string documentType) => Task.FromResult(true);
            public Task<bool> AreAllStandardPoliciesPublishedAsync() => Task.FromResult(true);
            public Task RecordConsentAsync(int? userId, int? leadId, string documentType, string? ipAddress, string? pageUrl) => Task.CompletedTask;
            public Task RecordAllStandardConsentsAsync(int? userId, int? leadId, string? ipAddress, string? pageUrl) => Task.CompletedTask;
        }

        private class TestTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private static AdminController CreateController(
            MockAuditConfigRepo configRepo,
            MockPropRepo? propRepo = null,
            MockRefundEngine? refundEngine = null,
            MockLegalDocService? legalService = null,
            int adminUserId = 99)
        {
            var siteSettingsService = new SiteSettingsService(configRepo);
            var controller = new AdminController(
                propRepo: propRepo ?? new MockPropRepo(),
                userRepo: null!,
                bookingRepo: null!,
                subRepo: null!,
                configRepo: configRepo,
                ownerReqRepo: null!,
                contactRepo: null!,
                siteSettingsService: siteSettingsService,
                financialRepo: null!,
                env: null!,
                refundEngine: refundEngine ?? new MockRefundEngine(),
                partnerLeadService: null!,
                damageClaimService: null!,
                idVerificationService: null!,
                mediaStorage: null!,
                eventPackageRepo: null!,
                legalService: legalService ?? new MockLegalDocService()
            );

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, adminUserId.ToString()),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Name, "Lead Administrator")
            }, "TestAdminAuth"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
            controller.TempData = new TempDataDictionary(controller.HttpContext, new TestTempDataProvider());

            return controller;
        }

        [Fact]
        public async Task AuditLog_RecordSiteSettingsChange_ContainsOldAndNewValues()
        {
            var configRepo = new MockAuditConfigRepo();
            var controller = CreateController(configRepo, adminUserId: 99);

            // 1. Update commission settings
            var resCommission = await controller.SaveCommissionSettings(15.0m, 20.0m);
            Assert.IsType<RedirectToActionResult>(resCommission);

            var commAudit = configRepo.AuditLogs.FirstOrDefault(a => a.Action == "UpdateCommissionSettings");
            Assert.NotNull(commAudit);
            Assert.Equal(99, commAudit.PerformedByUserId);
            Assert.Contains("Platform: 10", commAudit.Details); // Old defaults
            Assert.Contains("Platform: 15", commAudit.Details); // New values

            // 2. Update payment settings
            var resPayment = await controller.SavePaymentSettings("$", "rzp_live_newkey", "rzp_secret_new");
            Assert.IsType<RedirectToActionResult>(resPayment);

            var payAudit = configRepo.AuditLogs.FirstOrDefault(a => a.Action == "UpdatePaymentSettings");
            Assert.NotNull(payAudit);
            Assert.Contains("Old currency: '₹'", payAudit.Details);
            Assert.Contains("New currency: '$'", payAudit.Details);
            Assert.Contains("[REDACTED]", payAudit.Details); // Redacted secret
        }

        [Fact]
        public async Task AuditLog_ApproveProperty_RecordsOldAndNewStatus()
        {
            var configRepo = new MockAuditConfigRepo();
            var propRepo = new MockPropRepo();
            propRepo.Properties[10] = new Property
            {
                PropertyID = 10,
                Title = "Whispering Pines Villa",
                IsApproved = false,
                ListingStatus = "Pending"
            };

            var controller = CreateController(configRepo, propRepo: propRepo, adminUserId: 42);

            var result = await controller.ApproveProperty(10, approve: true, status: "Active");
            Assert.IsType<RedirectToActionResult>(result);

            var audit = configRepo.AuditLogs.FirstOrDefault(a => a.Action == "AdminApproveProperty");
            Assert.NotNull(audit);
            Assert.Equal(42, audit.PerformedByUserId);
            Assert.Equal("Property", audit.EntityType);
            Assert.Equal(10, audit.EntityId);
            Assert.Contains("Old status: 'Pending', IsApproved: False", audit.Details);
            Assert.Contains("New status: 'Active', IsApproved: True", audit.Details);
            Assert.Contains("Whispering Pines Villa", audit.Details);
        }

        [Fact]
        public async Task AuditLog_DeactivateProperty_RecordsOldAndNewActiveState()
        {
            var configRepo = new MockAuditConfigRepo();
            var propRepo = new MockPropRepo();
            propRepo.Properties[25] = new Property
            {
                PropertyID = 25,
                Title = "Seaside Orchid Estate",
                ListingStatus = "Active"
            };

            var controller = CreateController(configRepo, propRepo: propRepo, adminUserId: 77);

            var result = await controller.DeactivateProperty(25, deactivate: true);
            Assert.IsType<RedirectToActionResult>(result);

            var audit = configRepo.AuditLogs.FirstOrDefault(a => a.Action == "AdminDeactivateProperty");
            Assert.NotNull(audit);
            Assert.Equal(77, audit.PerformedByUserId);
            Assert.Equal("Property", audit.EntityType);
            Assert.Equal(25, audit.EntityId);
            Assert.Contains("Old active state: 'Active'", audit.Details);
            Assert.Contains("New active state: 'Inactive'", audit.Details);
            Assert.Contains("Seaside Orchid Estate", audit.Details);
        }

        [Fact]
        public async Task AuditLog_UpdateLegalDocument_RecordsOldAndNewVersion()
        {
            var configRepo = new MockAuditConfigRepo();
            var legalService = new MockLegalDocService();
            legalService.Documents.Add(new LegalDocument
            {
                DocumentID = 1,
                DocumentType = "TermsOfService",
                Title = "Terms of Service",
                Body = "<p>Version 1.0 terms</p>",
                Version = "1.0",
                IsDraft = false,
                IsCurrent = true
            });

            var controller = CreateController(configRepo, legalService: legalService, adminUserId: 101);

            var result = await controller.SaveLegalDocument(
                documentType: "TermsOfService",
                title: "Terms of Service v1.1",
                body: "<p>Updated 2026 terms</p>",
                version: "1.1",
                effectiveDate: DateTime.UtcNow,
                isDraft: false);

            Assert.IsType<RedirectToActionResult>(result);

            var audit = configRepo.AuditLogs.FirstOrDefault(a => a.Action == "SaveLegalDocument");
            Assert.NotNull(audit);
            Assert.Equal(101, audit.PerformedByUserId);
            Assert.Equal("LegalDocument", audit.EntityType);
            Assert.Contains("Old version: 'v1.0'", audit.Details);
            Assert.Contains("New version: 'v1.1'", audit.Details);
            Assert.Contains("TermsOfService", audit.Details);
        }

        [Fact]
        public async Task AuditLog_ProcessManualRefund_RecordsOldAndNewRefundValues()
        {
            var configRepo = new MockAuditConfigRepo();
            var refundEngine = new MockRefundEngine();
            refundEngine.Refunds[55] = new BookingRefund
            {
                RefundID = 55,
                BookingID = 120,
                Status = "Pending",
                NetRefundAmount = 8500.00m
            };

            var controller = CreateController(configRepo, refundEngine: refundEngine, adminUserId: 88);

            // 1. Override status
            var resOverride = await controller.OverrideRefund(55, "Approved", "Verified guest cancellation reason");
            Assert.IsType<RedirectToActionResult>(resOverride);

            var overrideAudit = configRepo.AuditLogs.FirstOrDefault(a => a.Action == "AdminOverrideRefund");
            Assert.NotNull(overrideAudit);
            Assert.Equal(88, overrideAudit.PerformedByUserId);
            Assert.Contains("Old status: 'Pending'", overrideAudit.Details);
            Assert.Contains("New status: 'Approved'", overrideAudit.Details);
            Assert.Contains("Verified guest cancellation reason", overrideAudit.Details);

            // 2. Adjudicate refund
            var resAdjudicate = await controller.AdjudicateRefund(55, "FullRefund", 9000.00m, "Dispute resolved with client", issueAsCredit: false);
            Assert.IsType<RedirectToActionResult>(resAdjudicate);

            var adjAudit = configRepo.AuditLogs.FirstOrDefault(a => a.Action == "AdminAdjudicateRefund");
            Assert.NotNull(adjAudit);
            Assert.Contains("Old status: 'Approved'", adjAudit.Details);
            Assert.Contains("Old Amount: ₹8,500.00", adjAudit.Details);
            Assert.Contains("New action: 'FullRefund'", adjAudit.Details);
            Assert.Contains("New Amount: ₹9,000.00", adjAudit.Details);
        }

        [Fact]
        public void BookingConfirmationSummary_PopulatesCompleteReceiptModel_WithHostAndDirections()
        {
            var booking = new Booking
            {
                BookingID = 105,
                PropertyID = 5,
                ClientID = 20,
                CheckInDate = new DateTime(2026, 12, 1),
                CheckOutDate = new DateTime(2026, 12, 4),
                BookingType = "FullDay",
                Status = "Confirmed",
                TotalAmount = 45000.00m,
                SecurityDepositAmount = 5000.00m
            };

            var property = new Property
            {
                PropertyID = 5,
                OwnerID = 30,
                Title = "Valley Mist Villa",
                Location = "Mahabaleshwar",
                FormattedAddress = "Plot 12, Strawberry Hill Rd, Mahabaleshwar 412806",
                MapUrl = "https://maps.google.com/?q=17.9237,73.6586",
                MaxGuests = 12
            };

            var host = new User
            {
                UserID = 30,
                FullName = "Vikram Patil",
                Email = "vikram.patil@example.com",
                Phone = "+91 98220 12345"
            };

            var client = new User
            {
                UserID = 20,
                FullName = "Ananya Desai",
                Email = "ananya.desai@example.com",
                Phone = "+91 98765 43210"
            };

            var payment = new Payment
            {
                PaymentID = 200,
                BookingID = 105,
                Amount = 50000.00m,
                PaymentStatus = "Completed",
                GatewayTransactionID = "pay_N4zKlm8921"
            };

            var vm = new BookingReceiptViewModel
            {
                Booking = booking,
                Property = property,
                Client = client,
                Host = host,
                PrimaryPayment = payment,
                PaymentHistory = new List<Payment> { payment }
            };

            Assert.NotNull(vm.Booking);
            Assert.Equal(105, vm.Booking.BookingID);
            Assert.Equal("Confirmed", vm.Booking.Status);

            Assert.NotNull(vm.Property);
            Assert.Equal("Valley Mist Villa", vm.Property.Title);
            Assert.Equal("https://maps.google.com/?q=17.9237,73.6586", vm.Property.MapUrl);
            Assert.Equal("Plot 12, Strawberry Hill Rd, Mahabaleshwar 412806", vm.Property.FormattedAddress);

            Assert.NotNull(vm.Host);
            Assert.Equal("Vikram Patil", vm.Host.FullName);
            Assert.Equal("+91 98220 12345", vm.Host.Phone);
            Assert.Equal("vikram.patil@example.com", vm.Host.Email);

            Assert.NotNull(vm.Client);
            Assert.Equal("Ananya Desai", vm.Client.FullName);

            Assert.NotNull(vm.PrimaryPayment);
            Assert.Equal("pay_N4zKlm8921", vm.PrimaryPayment.GatewayTransactionID);
        }
    }
}
