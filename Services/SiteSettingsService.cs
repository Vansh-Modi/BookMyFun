using BookMyFun.Data;
using BookMyFun.Repositories;
using System.Text.Json;

namespace BookMyFun.Services
{
    public interface ISiteSettingsService
    {
        Task<SiteSettingsModel> GetSettingsAsync();
        Task SaveSettingsAsync(SiteSettingsModel model);
        Task<List<PopularStayItem>> GetPopularStaysAsync();
        Task<List<PopularStayItem>> GetPopularStaysFallbackAsync();
        Task SavePopularStaysFallbackAsync(List<PopularStayItem> items);
    }

    public class PopularStayItem
    {
        public int PropertyID { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }

    public class SiteSettingsModel
    {
        public string SiteName { get; set; } = "BookMyFun";
        public string SiteTagline { get; set; } = "Farmhouse, Villa & Resort Rentals";
        public string LogoUrl { get; set; } = "";
        public string SiteIcon { get; set; } = "bi-house-heart-fill"; // Bootstrap Icon class
        public string SupportPhone { get; set; } = "+91 (020) 4567-8900";
        public string WhatsAppNumber { get; set; } = "+91 99999 00001";
        public string SupportEmail { get; set; } = "support@bookmyfun.com";
        public string Address { get; set; } = "Level 4, Sky Vista Tower, Viman Nagar, Pune, Maharashtra 411014";
        public string FacebookUrl { get; set; } = "https://facebook.com";
        public string InstagramUrl { get; set; } = "https://instagram.com";
        public string YouTubeUrl { get; set; } = "https://youtube.com";
        public decimal PlatformCommissionPercent { get; set; } = 10.0m;
        public decimal EventPlatformCommissionPercent { get; set; } = 10.0m;
        public string CurrencySymbol { get; set; } = "₹";
    }

    public class SiteSettingsService : ISiteSettingsService
    {
        private readonly IConfigRepository _configRepo;
        private readonly DBHelper? _db;
        private static SiteSettingsModel? _cachedSettings;
        private static DateTime _cacheExpiry = DateTime.MinValue;

        private static List<PopularStayItem>? _cachedPopularStays;
        private static DateTime _popularStaysCacheExpiry = DateTime.MinValue;
        private static readonly object _lock = new();

        private static readonly List<PopularStayItem> DefaultFallbackStays = new()
        {
            new PopularStayItem { PropertyID = 0, Title = "Lonavala Luxury Villas", Location = "Lonavala" },
            new PopularStayItem { PropertyID = 0, Title = "Alibaug Farmhouses", Location = "Alibaug" },
            new PopularStayItem { PropertyID = 0, Title = "Goa Beach Resorts", Location = "Goa" },
            new PopularStayItem { PropertyID = 0, Title = "Hillside Cottages", Location = "Mahabaleshwar" },
            new PopularStayItem { PropertyID = 0, Title = "Karjat Riverfront Stays", Location = "Karjat" }
        };

        public SiteSettingsService(IConfigRepository configRepo, DBHelper? db = null)
        {
            _configRepo = configRepo;
            _db = db;
        }

        public async Task<SiteSettingsModel> GetSettingsAsync()
        {
            // Fast in-memory cached return to prevent database roundtrips on every page render
            if (_cachedSettings != null && DateTime.UtcNow < _cacheExpiry)
            {
                return _cachedSettings;
            }

            var settings = await _configRepo.GetAllSettingsAsync();
            var dict = settings.ToDictionary(s => s.SettingKey, s => s.SettingValue ?? "");

            var model = new SiteSettingsModel();

            if (dict.TryGetValue("SiteName", out var name) && !string.IsNullOrWhiteSpace(name)) model.SiteName = name;
            if (dict.TryGetValue("SiteTagline", out var tag) && !string.IsNullOrWhiteSpace(tag)) model.SiteTagline = tag;
            if (dict.TryGetValue("LogoUrl", out var logo)) model.LogoUrl = logo;
            if (dict.TryGetValue("SiteIcon", out var icon) && !string.IsNullOrWhiteSpace(icon)) model.SiteIcon = icon;
            if (dict.TryGetValue("SupportPhone", out var phone) && !string.IsNullOrWhiteSpace(phone)) model.SupportPhone = phone;
            if (dict.TryGetValue("WhatsAppNumber", out var wa) && !string.IsNullOrWhiteSpace(wa)) model.WhatsAppNumber = wa;
            if (dict.TryGetValue("SupportEmail", out var email) && !string.IsNullOrWhiteSpace(email)) model.SupportEmail = email;
            if (dict.TryGetValue("Address", out var addr) && !string.IsNullOrWhiteSpace(addr)) model.Address = addr;
            if (dict.TryGetValue("FacebookUrl", out var fb) && !string.IsNullOrWhiteSpace(fb)) model.FacebookUrl = fb;
            if (dict.TryGetValue("InstagramUrl", out var insta) && !string.IsNullOrWhiteSpace(insta)) model.InstagramUrl = insta;
            if (dict.TryGetValue("YouTubeUrl", out var yt) && !string.IsNullOrWhiteSpace(yt)) model.YouTubeUrl = yt;
            if (dict.TryGetValue("CurrencySymbol", out var cur) && !string.IsNullOrWhiteSpace(cur)) model.CurrencySymbol = cur;

            if (dict.TryGetValue("PlatformCommissionPercent", out var comm) && decimal.TryParse(comm, out var cVal))
            {
                model.PlatformCommissionPercent = cVal;
            }
            if (dict.TryGetValue("EventPlatformCommissionPercent", out var eComm) && decimal.TryParse(eComm, out var ecVal))
            {
                model.EventPlatformCommissionPercent = ecVal;
            }

            lock (_lock)
            {
                _cachedSettings = model;
                _cacheExpiry = DateTime.UtcNow.AddMinutes(15);
            }

            return model;
        }

        public async Task SaveSettingsAsync(SiteSettingsModel model)
        {
            await _configRepo.SetSettingValueAsync("SiteName", model.SiteName ?? "BookMyFun");
            await _configRepo.SetSettingValueAsync("SiteTagline", model.SiteTagline ?? "");
            await _configRepo.SetSettingValueAsync("LogoUrl", model.LogoUrl ?? "");
            await _configRepo.SetSettingValueAsync("SiteIcon", model.SiteIcon ?? "bi-house-heart-fill");
            await _configRepo.SetSettingValueAsync("SupportPhone", model.SupportPhone ?? "");
            await _configRepo.SetSettingValueAsync("WhatsAppNumber", model.WhatsAppNumber ?? "");
            await _configRepo.SetSettingValueAsync("SupportEmail", model.SupportEmail ?? "");
            await _configRepo.SetSettingValueAsync("Address", model.Address ?? "");
            await _configRepo.SetSettingValueAsync("FacebookUrl", model.FacebookUrl ?? "");
            await _configRepo.SetSettingValueAsync("InstagramUrl", model.InstagramUrl ?? "");
            await _configRepo.SetSettingValueAsync("YouTubeUrl", model.YouTubeUrl ?? "");
            await _configRepo.SetSettingValueAsync("CurrencySymbol", model.CurrencySymbol ?? "₹");
            await _configRepo.SetSettingValueAsync("PlatformCommissionPercent", model.PlatformCommissionPercent.ToString("0.##"));
            await _configRepo.SetSettingValueAsync("EventPlatformCommissionPercent", model.EventPlatformCommissionPercent.ToString("0.##"));

            // Immediately invalidate in-memory cache
            lock (_lock)
            {
                _cachedSettings = null;
                _cacheExpiry = DateTime.MinValue;
            }
        }

        public async Task<List<PopularStayItem>> GetPopularStaysFallbackAsync()
        {
            var json = await _configRepo.GetSettingValueAsync("PopularStaysFallback");
            if (string.IsNullOrWhiteSpace(json))
            {
                return DefaultFallbackStays.ToList();
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<List<PopularStayItem>>(json);
                if (parsed != null && parsed.Any())
                {
                    return parsed;
                }
            }
            catch
            {
                // In case of corrupt JSON, return defaults
            }

            return DefaultFallbackStays.ToList();
        }

        public async Task SavePopularStaysFallbackAsync(List<PopularStayItem> items)
        {
            var cleanItems = items?
                .Where(x => !string.IsNullOrWhiteSpace(x.Title))
                .Take(10)
                .ToList() ?? new List<PopularStayItem>();

            var json = JsonSerializer.Serialize(cleanItems);
            await _configRepo.SetSettingValueAsync("PopularStaysFallback", json);

            // Invalidate 15-minute popular stays cache immediately
            lock (_lock)
            {
                _cachedPopularStays = null;
                _popularStaysCacheExpiry = DateTime.MinValue;
            }
        }

        public async Task<List<PopularStayItem>> GetPopularStaysAsync()
        {
            lock (_lock)
            {
                if (_cachedPopularStays != null && DateTime.UtcNow < _popularStaysCacheExpiry)
                {
                    return _cachedPopularStays;
                }
            }

            var result = new List<PopularStayItem>();

            // 1. Fetch top properties by confirmed/completed bookings in last 30 days
            if (_db != null)
            {
                try
                {
                    const string sql = @"
                        SELECT TOP 5 p.PropertyID, p.Title, p.Location, COUNT(b.BookingID) AS BookingCount
                        FROM Properties p
                        JOIN Bookings b ON p.PropertyID = b.PropertyID
                        WHERE b.Status IN ('Confirmed', 'Completed')
                          AND b.CreatedAt >= DATEADD(DAY, -30, GETDATE())
                          AND p.IsApproved = 1
                        GROUP BY p.PropertyID, p.Title, p.Location
                        ORDER BY BookingCount DESC";

                    var dynamicTop = await _db.ExecuteReaderListAsync(sql, reader => new PopularStayItem
                    {
                        PropertyID = reader.GetInt32(reader.GetOrdinal("PropertyID")),
                        Title = reader.GetString(reader.GetOrdinal("Title")),
                        Location = reader.GetString(reader.GetOrdinal("Location"))
                    });

                    if (dynamicTop != null && dynamicTop.Any())
                    {
                        result.AddRange(dynamicTop);
                    }
                }
                catch
                {
                    // Fall back gracefully if DB fails or table unavailable
                }
            }

            // 2. If fewer than 5 have bookings, fill remaining spots from admin-editable fallback list in SiteSettings
            if (result.Count < 5)
            {
                var fallbacks = await GetPopularStaysFallbackAsync();
                foreach (var fb in fallbacks)
                {
                    if (result.Count >= 5) break;

                    bool exists = result.Any(r =>
                        (fb.PropertyID > 0 && r.PropertyID == fb.PropertyID) ||
                        string.Equals(r.Title, fb.Title, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r.Location, fb.Location, StringComparison.OrdinalIgnoreCase));

                    if (!exists)
                    {
                        result.Add(fb);
                    }
                }

                // If still fewer than 5, append remaining without duplicate check
                if (result.Count < 5)
                {
                    foreach (var fb in fallbacks)
                    {
                        if (result.Count >= 5) break;
                        if (!result.Contains(fb))
                        {
                            result.Add(fb);
                        }
                    }
                }
            }

            var finalResult = result.Take(5).ToList();

            lock (_lock)
            {
                _cachedPopularStays = finalResult;
                _popularStaysCacheExpiry = DateTime.UtcNow.AddMinutes(15);
            }

            return finalResult;
        }
    }
}
