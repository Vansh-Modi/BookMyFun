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
        void InvalidatePopularStaysCache();
        void InvalidateSettingsCache();
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

        // Section 2: Refunds
        public int FreeCancellationWindowHours { get; set; } = 48;
        public int CancellationCutoffHours { get; set; } = 24;
        public string CancellationFeeType { get; set; } = "percentage"; // percentage or fixed
        public decimal CancellationFeeValue { get; set; } = 10.0m;
        public string RefundProcessingTimeline { get; set; } = "5 to 7 business days";

        // PENDING DECISION D1: Commission setting on 50% partial cancellation (placeholder: keep commission on the 50%)
        public string D1_PartialCancellationCommissionPolicy { get; set; } = "KeepOnPaidHalf";

        // PENDING DECISION D2: Under 72 hours cancellation owner policy (placeholder: no refund for all owners)
        public string D2_Under72hOwnerPolicyOverride { get; set; } = "NoRefundAll";

        // PENDING DECISION D3: Owner cancellation penalty amount (placeholder: fixed amount stored in SiteSettings)
        public decimal D3_OwnerCancellationPenaltyAmount { get; set; } = 500.00m;

        // Section 3: Payments
        public string RazorpayKeyId { get; set; } = "";
        public string RazorpayKeySecret { get; set; } = "";

        // Section 4: Notifications
        public string SmtpServer { get; set; } = "";
        public int SmtpPort { get; set; } = 587;
        public string SmtpUsername { get; set; } = "";
        public string SmtpPassword { get; set; } = "";
        public string SmtpSenderEmail { get; set; } = "";
        public string WhatsAppApiKey { get; set; } = "";
        public string WhatsAppSenderPhone { get; set; } = "";

        // Section 6: Legal
        public string PrivacyPolicyVersion { get; set; } = "1.0";
        public string TermsOfServiceVersion { get; set; } = "1.0";
        public string RefundPolicyVersion { get; set; } = "1.0";

        // Section 7: Feature Flags
        public bool GoogleLoginEnabled { get; set; } = false;
        public bool InstantOnlineBookingEnabled { get; set; } = true;
        public bool WhatsAppNotificationsEnabled { get; set; } = true;
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

            // Refunds
            if (dict.TryGetValue("Refund:FreeCancellationWindowHours", out var fcw) && int.TryParse(fcw, out var fcwVal)) model.FreeCancellationWindowHours = fcwVal;
            if (dict.TryGetValue("Refund:CancellationCutoffHours", out var cch) && int.TryParse(cch, out var cchVal)) model.CancellationCutoffHours = cchVal;
            if (dict.TryGetValue("Refund:CancellationFeeType", out var cft) && !string.IsNullOrWhiteSpace(cft)) model.CancellationFeeType = cft;
            if (dict.TryGetValue("Refund:CancellationFeeValue", out var cfv) && decimal.TryParse(cfv, out var cfvVal)) model.CancellationFeeValue = cfvVal;
            if (dict.TryGetValue("Refund:ProcessingTimeline", out var rpt) && !string.IsNullOrWhiteSpace(rpt)) model.RefundProcessingTimeline = rpt;

            // PENDING DECISION D1, D2, D3
            if (dict.TryGetValue("Refund:D1_PartialCancellationCommissionPolicy", out var d1) && !string.IsNullOrWhiteSpace(d1)) model.D1_PartialCancellationCommissionPolicy = d1;
            if (dict.TryGetValue("Refund:D2_Under72hOwnerPolicyOverride", out var d2) && !string.IsNullOrWhiteSpace(d2)) model.D2_Under72hOwnerPolicyOverride = d2;
            if (dict.TryGetValue("Refund:D3_OwnerCancellationPenaltyAmount", out var d3) && decimal.TryParse(d3, out var d3Val)) model.D3_OwnerCancellationPenaltyAmount = d3Val;

            // Payments
            if (dict.TryGetValue("Payment:RazorpayKeyId", out var rzpKey)) model.RazorpayKeyId = rzpKey;
            if (dict.TryGetValue("Payment:RazorpayKeySecret", out var rzpSec)) model.RazorpayKeySecret = rzpSec;

            // Notifications
            if (dict.TryGetValue("Notification:SmtpServer", out var smtpSrv)) model.SmtpServer = smtpSrv;
            if (dict.TryGetValue("Notification:SmtpPort", out var smtpPrt) && int.TryParse(smtpPrt, out var smtpPrtVal)) model.SmtpPort = smtpPrtVal;
            if (dict.TryGetValue("Notification:SmtpUsername", out var smtpUsr)) model.SmtpUsername = smtpUsr;
            if (dict.TryGetValue("Notification:SmtpPassword", out var smtpPwd)) model.SmtpPassword = smtpPwd;
            if (dict.TryGetValue("Notification:SmtpSenderEmail", out var smtpSnd)) model.SmtpSenderEmail = smtpSnd;
            if (dict.TryGetValue("Notification:WhatsAppApiKey", out var waKey)) model.WhatsAppApiKey = waKey;
            if (dict.TryGetValue("Notification:WhatsAppSenderPhone", out var waPh)) model.WhatsAppSenderPhone = waPh;

            // Legal
            if (dict.TryGetValue("Legal:PrivacyPolicyVersion", out var ppv) && !string.IsNullOrWhiteSpace(ppv)) model.PrivacyPolicyVersion = ppv;
            if (dict.TryGetValue("Legal:TermsOfServiceVersion", out var tosv) && !string.IsNullOrWhiteSpace(tosv)) model.TermsOfServiceVersion = tosv;
            if (dict.TryGetValue("Legal:RefundPolicyVersion", out var rpv) && !string.IsNullOrWhiteSpace(rpv)) model.RefundPolicyVersion = rpv;

            // Feature Flags
            if (dict.TryGetValue("Feature:GoogleLoginEnabled", out var gle) && bool.TryParse(gle, out var gleVal)) model.GoogleLoginEnabled = gleVal;
            if (dict.TryGetValue("Feature:InstantOnlineBookingEnabled", out var ibe) && bool.TryParse(ibe, out var ibeVal)) model.InstantOnlineBookingEnabled = ibeVal;
            if (dict.TryGetValue("Feature:WhatsAppNotificationsEnabled", out var wne) && bool.TryParse(wne, out var wneVal)) model.WhatsAppNotificationsEnabled = wneVal;

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

            // Refunds
            await _configRepo.SetSettingValueAsync("Refund:FreeCancellationWindowHours", model.FreeCancellationWindowHours.ToString());
            await _configRepo.SetSettingValueAsync("Refund:CancellationCutoffHours", model.CancellationCutoffHours.ToString());
            await _configRepo.SetSettingValueAsync("Refund:CancellationFeeType", model.CancellationFeeType ?? "percentage");
            await _configRepo.SetSettingValueAsync("Refund:CancellationFeeValue", model.CancellationFeeValue.ToString("0.##"));
            await _configRepo.SetSettingValueAsync("Refund:ProcessingTimeline", model.RefundProcessingTimeline ?? "5 to 7 business days");

            // PENDING DECISION D1, D2, D3
            await _configRepo.SetSettingValueAsync("Refund:D1_PartialCancellationCommissionPolicy", model.D1_PartialCancellationCommissionPolicy ?? "KeepOnPaidHalf");
            await _configRepo.SetSettingValueAsync("Refund:D2_Under72hOwnerPolicyOverride", model.D2_Under72hOwnerPolicyOverride ?? "NoRefundAll");
            await _configRepo.SetSettingValueAsync("Refund:D3_OwnerCancellationPenaltyAmount", model.D3_OwnerCancellationPenaltyAmount.ToString("0.##"));

            // Payments
            if (!string.IsNullOrWhiteSpace(model.RazorpayKeyId))
                await _configRepo.SetSettingValueAsync("Payment:RazorpayKeyId", model.RazorpayKeyId);
            if (!string.IsNullOrWhiteSpace(model.RazorpayKeySecret))
                await _configRepo.SetSettingValueAsync("Payment:RazorpayKeySecret", model.RazorpayKeySecret, isEncrypted: true);

            // Notifications
            await _configRepo.SetSettingValueAsync("Notification:SmtpServer", model.SmtpServer ?? "");
            await _configRepo.SetSettingValueAsync("Notification:SmtpPort", model.SmtpPort.ToString());
            await _configRepo.SetSettingValueAsync("Notification:SmtpUsername", model.SmtpUsername ?? "");
            if (!string.IsNullOrWhiteSpace(model.SmtpPassword))
                await _configRepo.SetSettingValueAsync("Notification:SmtpPassword", model.SmtpPassword, isEncrypted: true);
            await _configRepo.SetSettingValueAsync("Notification:SmtpSenderEmail", model.SmtpSenderEmail ?? "");
            await _configRepo.SetSettingValueAsync("Notification:WhatsAppApiKey", model.WhatsAppApiKey ?? "");
            await _configRepo.SetSettingValueAsync("Notification:WhatsAppSenderPhone", model.WhatsAppSenderPhone ?? "");

            // Legal
            await _configRepo.SetSettingValueAsync("Legal:PrivacyPolicyVersion", model.PrivacyPolicyVersion ?? "1.0");
            await _configRepo.SetSettingValueAsync("Legal:TermsOfServiceVersion", model.TermsOfServiceVersion ?? "1.0");
            await _configRepo.SetSettingValueAsync("Legal:RefundPolicyVersion", model.RefundPolicyVersion ?? "1.0");

            // Feature Flags
            await _configRepo.SetSettingValueAsync("Feature:GoogleLoginEnabled", model.GoogleLoginEnabled.ToString());
            await _configRepo.SetSettingValueAsync("Feature:InstantOnlineBookingEnabled", model.InstantOnlineBookingEnabled.ToString());
            await _configRepo.SetSettingValueAsync("Feature:WhatsAppNotificationsEnabled", model.WhatsAppNotificationsEnabled.ToString());

            // Immediately invalidate in-memory cache
            InvalidateSettingsCache();
        }

        public void InvalidateSettingsCache()
        {
            lock (_lock)
            {
                _cachedSettings = null;
                _cacheExpiry = DateTime.MinValue;
            }
        }

        public void InvalidatePopularStaysCache()
        {
            lock (_lock)
            {
                _cachedPopularStays = null;
                _popularStaysCacheExpiry = DateTime.MinValue;
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
