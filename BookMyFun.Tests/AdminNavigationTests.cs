using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Xunit;

namespace BookMyFun.Tests
{
    public class AdminNavigationTests
    {
        [Fact]
        public void AdminDashboardViewModel_SummaryCards_StoreAndExposeKeyMetrics()
        {
            var vm = new AdminDashboardViewModel
            {
                BookingsTodayCount = 12,
                PendingRefundsCount = 4,
                PendingKycCount = 7,
                PendingPartnerLeadsCount = 3,
                HeldPayoutsTotal = 45000.50m,
                NewMessagesCount = 8
            };

            Assert.Equal(12, vm.BookingsTodayCount);
            Assert.Equal(4, vm.PendingRefundsCount);
            Assert.Equal(7, vm.PendingKycCount);
            Assert.Equal(3, vm.PendingPartnerLeadsCount);
            Assert.Equal(45000.50m, vm.HeldPayoutsTotal);
            Assert.Equal(8, vm.NewMessagesCount);
        }

        [Fact]
        public void PagedResult_PaginationCalculations_Default25()
        {
            var items = Enumerable.Range(1, 100).Select(i => $"Item {i}").ToList();
            int pageSize = 25;
            int page = 2;

            int totalCount = items.Count;
            int totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            var pagedItems = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var result = new PagedResult<string>
            {
                Items = pagedItems,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };

            Assert.Equal(4, totalPages);
            Assert.Equal(25, result.Items.Count);
            Assert.Equal("Item 26", result.Items.First());
            Assert.Equal("Item 50", result.Items.Last());
            Assert.True(result.HasNextPage);
            Assert.True(result.HasPreviousPage);
        }

        [Fact]
        public void SiteSettingsModel_CollapsibleSections_RetainsPlaceholdersAndDefaults()
        {
            var model = new SiteSettingsModel
            {
                // D1, D2, D3 Placeholders for open decisions
                D1_PartialCancellationCommissionPolicy = "KeepOnPaidHalf",
                D2_Under72hOwnerPolicyOverride = "NoRefundAll",
                D3_OwnerCancellationPenaltyAmount = 500.00m,
                PlatformCommissionPercent = 10.00m,
                EventPlatformCommissionPercent = 15.00m,
                RazorpayKeyId = "rzp_test_key",
                RazorpayKeySecret = "rzp_test_secret",
                SmtpSenderEmail = "admin@bookmyfun.in",
                WhatsAppSenderPhone = "918849077959",
                PrivacyPolicyVersion = "1.0",
                TermsOfServiceVersion = "1.0",
                RefundPolicyVersion = "1.0",
                GoogleLoginEnabled = true,
                InstantOnlineBookingEnabled = true,
                WhatsAppNotificationsEnabled = true
            };

            Assert.Equal("KeepOnPaidHalf", model.D1_PartialCancellationCommissionPolicy);
            Assert.Equal("NoRefundAll", model.D2_Under72hOwnerPolicyOverride);
            Assert.Equal(500.00m, model.D3_OwnerCancellationPenaltyAmount);
            Assert.Equal(10.00m, model.PlatformCommissionPercent);
            Assert.Equal("1.0", model.PrivacyPolicyVersion);
            Assert.True(model.GoogleLoginEnabled);
            Assert.True(model.InstantOnlineBookingEnabled);
        }

        [Fact]
        public async Task SiteSettingsService_CacheInvalidation_WorksCorrectly()
        {
            var stubRepo = new InMemoryConfigRepository();
            var service = new SiteSettingsService(stubRepo);

            // Fetch initial settings
            var settings1 = await service.GetSettingsAsync();
            Assert.NotNull(settings1);

            // Invalidate cache
            service.InvalidateSettingsCache();
            service.InvalidatePopularStaysCache();

            // Next call fetches afresh without throwing
            var settings2 = await service.GetSettingsAsync();
            Assert.NotNull(settings2);
        }

        private class InMemoryConfigRepository : IConfigRepository
        {
            private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);

            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);

            public Task<string?> GetSettingValueAsync(string key)
            {
                _store.TryGetValue(key, out var val);
                return Task.FromResult<string?>(val);
            }

            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false)
            {
                _store[key] = value;
                return Task.FromResult(true);
            }

            public Task<List<SiteSetting>> GetAllSettingsAsync()
            {
                var list = _store.Select(kv => new SiteSetting { SettingKey = kv.Key, SettingValue = kv.Value }).ToList();
                return Task.FromResult(list);
            }

            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details) => Task.CompletedTask;
        }
    }
}
