using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Xunit;

namespace BookMyFun.Tests
{
    public class PopularStaysTests
    {
        private class TestConfigRepo : IConfigRepository
        {
            public readonly Dictionary<string, string> Settings = new();

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

            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details) => Task.CompletedTask;
        }

        [Fact]
        public async Task GetPopularStaysFallbackAsync_ReturnsDefaults_WhenNotConfigured()
        {
            var configRepo = new TestConfigRepo();
            var service = new SiteSettingsService(configRepo);

            var result = await service.GetPopularStaysFallbackAsync();

            Assert.NotNull(result);
            Assert.Equal(5, result.Count);
            Assert.Contains(result, x => x.Location == "Lonavala");
            Assert.Contains(result, x => x.Location == "Alibaug");
            Assert.Contains(result, x => x.Location == "Goa");
        }

        [Fact]
        public async Task SavePopularStaysFallbackAsync_PersistsAndReadsBack()
        {
            var configRepo = new TestConfigRepo();
            var service = new SiteSettingsService(configRepo);

            var customList = new List<PopularStayItem>
            {
                new PopularStayItem { PropertyID = 101, Title = "Sunset Luxury Haven", Location = "Khandala" },
                new PopularStayItem { PropertyID = 102, Title = "Sea Breeze Villa", Location = "Kashid" },
                new PopularStayItem { PropertyID = 0, Title = "Heritage Palace", Location = "Udaipur" },
                new PopularStayItem { PropertyID = 0, Title = "Forest Retreat", Location = "Coorg" },
                new PopularStayItem { PropertyID = 0, Title = "Alpine Cottage", Location = "Manali" }
            };

            await service.SavePopularStaysFallbackAsync(customList);
            var result = await service.GetPopularStaysFallbackAsync();

            Assert.Equal(5, result.Count);
            Assert.Equal("Sunset Luxury Haven", result[0].Title);
            Assert.Equal(101, result[0].PropertyID);
            Assert.Equal("Kashid", result[1].Location);
            Assert.Equal("Udaipur", result[2].Location);
        }

        [Fact]
        public async Task GetPopularStaysAsync_AlwaysReturns5ItemsPaddedFromFallback()
        {
            var configRepo = new TestConfigRepo();
            var service = new SiteSettingsService(configRepo);

            var stays = await service.GetPopularStaysAsync();

            Assert.NotNull(stays);
            Assert.Equal(5, stays.Count);
        }

        [Fact]
        public async Task SavePopularStaysFallbackAsync_EvictsCacheImmediately()
        {
            var configRepo = new TestConfigRepo();
            var service = new SiteSettingsService(configRepo);

            // Populate initial cache
            var initial = await service.GetPopularStaysAsync();
            Assert.Equal(5, initial.Count);

            // Update fallback
            var customList = new List<PopularStayItem>
            {
                new PopularStayItem { PropertyID = 50, Title = "Custom Villa 50", Location = "Nashik" },
                new PopularStayItem { PropertyID = 51, Title = "Custom Villa 51", Location = "Panchgani" },
                new PopularStayItem { PropertyID = 52, Title = "Custom Villa 52", Location = "Igatpuri" },
                new PopularStayItem { PropertyID = 53, Title = "Custom Villa 53", Location = "Wayanad" },
                new PopularStayItem { PropertyID = 54, Title = "Custom Villa 54", Location = "Munnar" }
            };
            await service.SavePopularStaysFallbackAsync(customList);

            // Next call should read new fallback because cache was evicted
            var updated = await service.GetPopularStaysAsync();
            Assert.Equal(5, updated.Count);
            Assert.Equal("Custom Villa 50", updated[0].Title);
            Assert.Equal("Nashik", updated[0].Location);
        }
    }
}
