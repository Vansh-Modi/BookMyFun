using System.Reflection;
using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Configuration;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Checkout;
using BookMyFun.Services.Common;
using BookMyFun.Services.Notifications;
using BookMyFun.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookMyFun.Tests
{
    public class TransactionalEmailPipelineTests
    {
        private class NullProxy : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                var rt = targetMethod!.ReturnType;
                if (rt == typeof(Task)) return Task.CompletedTask;
                if (rt == typeof(Task<bool>)) return Task.FromResult(true);
                if (rt == typeof(Task<int>)) return Task.FromResult(1);
                if (rt == typeof(Task<decimal?>)) return Task.FromResult<decimal?>(null);
                if (rt == typeof(Task<(decimal, decimal, decimal)>)) return Task.FromResult((10m, 850m, 7650m));
                if (rt == typeof(Task<List<DateTime>>)) return Task.FromResult(new List<DateTime>());
                if (rt == typeof(Task<bool>)) return Task.FromResult(true);
                if (rt == typeof(bool)) return true;
                if (rt == typeof(decimal)) return 8500m;
                if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var inner = rt.GetGenericArguments()[0];
                    object? val = inner.IsValueType ? Activator.CreateInstance(inner) : null;
                    if (inner.IsGenericType && inner.GetGenericTypeDefinition() == typeof(List<>))
                        val = Activator.CreateInstance(inner);
                    return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, new[] { val });
                }
                return rt.IsValueType ? Activator.CreateInstance(rt) : null;
            }
        }

        private static T Proxy<T>() where T : class => DispatchProxy.Create<T, NullProxy>();

        private sealed class FakeHostEnv : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = Environments.Development;
            public string ApplicationName { get; set; } = "BookMyFun";
            public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private sealed class FakeWebHostEnv : IWebHostEnvironment
        {
            public string WebRootPath { get; set; } = string.Empty;
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
            public string EnvironmentName { get; set; } = Environments.Development;
            public string ApplicationName { get; set; } = "BookMyFun";
            public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private sealed class InMemoryOutboxRepo : INotificationOutboxRepository
        {
            private long _seq = 1;
            public List<NotificationOutbox> Rows { get; } = new();
            public HashSet<string> DedupeKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

            public Task<bool> EnqueueAsync(NotificationOutbox item, SqlTransaction? tx = null)
                => EnqueueWithIdAsync(item, tx).ContinueWith(t => t.Result > 0);

            public Task<long> EnqueueWithIdAsync(NotificationOutbox item, SqlTransaction? tx = null)
            {
                string key = !string.IsNullOrWhiteSpace(item.DedupeKey)
                    ? item.DedupeKey.Trim()
                    : $"{item.EventType}:{item.EntityID}:{item.UserID}:{item.Channel}";
                item.DedupeKey = key;

                if (!DedupeKeys.Add(key))
                    return Task.FromResult(0L);

                item.OutboxID = _seq++;
                Rows.Add(item);
                return Task.FromResult(item.OutboxID);
            }

            public Task<List<NotificationOutbox>> GetPendingBatchAsync(int batchSize = 25)
            {
                var now = DateTime.UtcNow;
                return Task.FromResult(Rows
                    .Where(x => (x.Status == "Pending" || x.Status == "Retry") && x.NextAttemptAt <= now)
                    .Take(batchSize)
                    .ToList());
            }

            public Task<bool> MarkProcessingAsync(long outboxId)
            {
                var r = Rows.FirstOrDefault(x => x.OutboxID == outboxId);
                if (r != null) r.Status = "Processing";
                return Task.FromResult(r != null);
            }

            public Task<bool> MarkSentAsync(long outboxId)
            {
                var r = Rows.FirstOrDefault(x => x.OutboxID == outboxId);
                if (r != null)
                {
                    r.Status = "Sent";
                    r.SentAt = DateTime.UtcNow;
                    r.LastError = null;
                }
                return Task.FromResult(r != null);
            }

            public Task<bool> MarkSentWithAttemptsAsync(long outboxId, int attempts)
            {
                var r = Rows.FirstOrDefault(x => x.OutboxID == outboxId);
                if (r != null)
                {
                    r.Status = "Sent";
                    r.RetryCount = attempts;
                    r.SentAt = DateTime.UtcNow;
                    r.LastError = null;
                }
                return Task.FromResult(r != null);
            }

            public Task<bool> RecordFailureAsync(long outboxId, string error, int retryCount, DateTime nextAttemptAt, bool isPermanent)
            {
                var r = Rows.FirstOrDefault(x => x.OutboxID == outboxId);
                if (r != null)
                {
                    r.Status = isPermanent ? "Failed" : "Retry";
                    r.RetryCount = retryCount;
                    r.NextAttemptAt = nextAttemptAt;
                    r.LastError = EmailSafetyHelper.Truncate(error, 450);
                }
                return Task.FromResult(r != null);
            }

            public Task<List<NotificationOutbox>> GetRecentEmailOutboxAsync(int limit = 100)
                => Task.FromResult(Rows.Where(x => x.Channel == "Email").OrderByDescending(x => x.OutboxID).Take(limit).ToList());

            public Task<NotificationOutbox?> GetByIdAsync(long outboxId)
                => Task.FromResult(Rows.FirstOrDefault(x => x.OutboxID == outboxId));

            public Task<bool> ResetFailedForRetryAsync(long outboxId)
            {
                var r = Rows.FirstOrDefault(x => x.OutboxID == outboxId && x.Status == "Failed");
                if (r == null) return Task.FromResult(false);
                r.Status = "Pending";
                r.RetryCount = 0;
                r.NextAttemptAt = DateTime.UtcNow;
                r.LastError = null;
                return Task.FromResult(true);
            }
        }

        private sealed class InMemoryUserRepo : IUserRepository
        {
            private int _nextId = 100;
            private int _nextTokenId = 1;
            public List<User> Users { get; } = new();
            public List<(int TokenId, int UserId, string TokenHash, DateTime ExpiresAt, DateTime? UsedAt)> Tokens { get; } = new();

            public Task<User?> GetByIdAsync(int userId) => Task.FromResult(Users.FirstOrDefault(u => u.UserID == userId));
            public Task<User?> GetByEmailAsync(string email) => Task.FromResult(Users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));
            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult<User?>(null);
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(Users.Where(u => u.Role.Equals(role, StringComparison.OrdinalIgnoreCase)).ToList());
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(Users.ToList());

            public Task<int> CreateUserAsync(User user)
            {
                user.UserID = ++_nextId;
                Users.Add(user);
                return Task.FromResult(user.UserID);
            }

            public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);

            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash)
            {
                var u = Users.FirstOrDefault(x => x.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
                if (u == null) return Task.FromResult(false);
                u.PasswordHash = newPasswordHash;
                return Task.FromResult(true);
            }

            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);

            public Task<int> CreatePasswordResetTokenAsync(int userId, string tokenHash, DateTime expiresAt)
            {
                int id = _nextTokenId++;
                Tokens.Add((id, userId, tokenHash, expiresAt, null));
                return Task.FromResult(id);
            }

            public Task<(bool Valid, int TokenId, int UserId, string? Email, string? ErrorMessage)> ValidatePasswordResetTokenAsync(string tokenHash)
            {
                var idx = Tokens.FindIndex(t => t.TokenHash == tokenHash);
                if (idx < 0) return Task.FromResult<(bool, int, int, string?, string?)>((false, 0, 0, null, "Invalid"));
                var t = Tokens[idx];
                var u = Users.FirstOrDefault(x => x.UserID == t.UserId);
                if (t.UsedAt.HasValue) return Task.FromResult<(bool, int, int, string?, string?)>((false, t.TokenId, t.UserId, u?.Email, "Used"));
                if (DateTime.UtcNow > t.ExpiresAt) return Task.FromResult<(bool, int, int, string?, string?)>((false, t.TokenId, t.UserId, u?.Email, "Expired"));
                return Task.FromResult<(bool, int, int, string?, string?)>((true, t.TokenId, t.UserId, u?.Email, null));
            }

            public Task<bool> MarkPasswordResetTokenUsedAsync(int tokenId)
            {
                var idx = Tokens.FindIndex(t => t.TokenId == tokenId);
                if (idx < 0) return Task.FromResult(false);
                var t = Tokens[idx];
                Tokens[idx] = (t.TokenId, t.UserId, t.TokenHash, t.ExpiresAt, DateTime.UtcNow);
                return Task.FromResult(true);
            }
        }

        private sealed class FakeConfigRepo : IConfigRepository
        {
            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
            public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details) => Task.CompletedTask;
        }

        private sealed class FakeOtpRepo : IOtpRepository
        {
            public Task<bool> SaveOtpAsync(string contact, string otp, string purpose, int expirySeconds = 150) => Task.FromResult(true);
            public Task<bool> ValidateOtpAsync(string contact, string otp, string purpose) => Task.FromResult(true);
        }

        private sealed class FakeBookingRepo : IBookingRepository
        {
            private int _nextId = 500;
            public List<Booking> Bookings { get; } = new();
            public List<int> ExpiredIdsToReturn { get; } = new();

            public Task<Booking?> GetByIdAsync(int bookingId) => Task.FromResult(Bookings.FirstOrDefault(b => b.BookingID == bookingId));
            public Task<Booking?> GetBookingByIdAsync(int bookingId) => GetByIdAsync(bookingId);
            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(Bookings.Where(b => b.ClientID == clientId).ToList());
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(Bookings.ToList());
            public Task<int> CreateBookingAsync(Booking booking)
            {
                booking.BookingID = ++_nextId;
                Bookings.Add(booking);
                return Task.FromResult(booking.BookingID);
            }
            public Task<(bool Available, int BookingId)> CreateHoldAtomicAsync(Booking booking, TimeSpan? hourlyCheckInTime = null, int? hourlyDuration = null)
                => CreateBookingAsync(booking).ContinueWith(t => (true, t.Result));
            public Task<bool> UpdateBookingStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> CancelBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(true);
            public Task<int> RecordPaymentAsync(Payment payment) => Task.FromResult(1);
            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId) => Task.FromResult(new List<Payment>());
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
            public Task<List<int>> ExpireStaleHoldsWithIdsAsync() => Task.FromResult(ExpiredIdsToReturn.ToList());
        }

        private sealed class FakePartnerKycRepo : IPartnerKycRepository
        {
            public Dictionary<int, PartnerKycBankDetails> Records { get; } = new();
            public Task<PartnerKycBankDetails?> GetByUserIdAsync(int userId) => Task.FromResult(Records.TryGetValue(userId, out var r) ? r : null);
            public Task<bool> UpsertAsync(PartnerKycBankDetails record) { Records[record.UserId] = record; return Task.FromResult(true); }
            public Task<bool> MarkFirstPopupShownAsync(int userId) => Task.FromResult(true);
            public Task<bool> ReviewAsync(int partnerUserId, string newStatus, int adminUserId, string? reason, string? clientIp)
            {
                if (!Records.TryGetValue(partnerUserId, out var r)) return Task.FromResult(false);
                r.KycStatus = newStatus;
                r.RejectionReason = reason;
                r.ReviewedByUserId = adminUserId;
                r.ReviewedAt = DateTime.UtcNow;
                return Task.FromResult(true);
            }
        }

        private sealed class FakePartnerKycService : IPartnerKycService
        {
            private readonly FakePartnerKycRepo _repo;
            public FakePartnerKycService(FakePartnerKycRepo repo) => _repo = repo;
            public Task<PartnerKycStatusDto> GetStatusAsync(int userId) => Task.FromResult(new PartnerKycStatusDto { KycStatus = "Submitted", SubmittedAt = new DateTime(2026, 11, 28, 10, 0, 0, DateTimeKind.Utc) });
            public Task<(bool Success, Dictionary<string, string> FieldErrors, PartnerKycStatusDto Status)> SubmitAsync(int userId, PartnerKycSubmitViewModel model)
            {
                var submitted = new DateTime(2026, 11, 28, 10, 0, 0, DateTimeKind.Utc);
                _repo.Records[userId] = new PartnerKycBankDetails { UserId = userId, KycStatus = "Submitted", SubmittedAt = submitted };
                return Task.FromResult((true, new Dictionary<string, string>(), new PartnerKycStatusDto { IsComplete = true, KycStatus = "Submitted", SubmittedAt = submitted }));
            }
            public Task MarkFirstPopupShownAsync(int userId) => Task.CompletedTask;
        }

        private sealed class FakePhotoShootRepo : IPhotoShootRepository
        {
            private int _seq = 700;
            public List<PhotoShootRequest> Requests { get; } = new();
            public Task<int> CreateRequestAsync(PhotoShootRequest request)
            {
                request.RequestID = ++_seq;
                Requests.Add(request);
                return Task.FromResult(request.RequestID);
            }
            public Task<PhotoShootRequest?> GetByIdAsync(int requestId) => Task.FromResult(Requests.FirstOrDefault(r => r.RequestID == requestId));
            public Task<List<PhotoShootRequest>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(Requests.Where(r => r.OwnerID == ownerId).ToList());
            public Task<List<PhotoShootRequest>> GetByPropertyIdAsync(int propertyId) => Task.FromResult(Requests.Where(r => r.PropertyID == propertyId).ToList());
            public Task<List<PhotoShootRequest>> GetPendingRequestsAsync() => Task.FromResult(Requests.Where(r => r.Status == "Requested" || r.Status == "Scheduled").ToList());
            public Task<List<PhotoShootRequest>> GetAllRequestsAsync() => Task.FromResult(Requests.ToList());
            public Task<bool> HasOpenRequestAsync(int propertyId, string shootType) => Task.FromResult(Requests.Any(r => r.PropertyID == propertyId && r.ShootType == shootType && (r.Status == "Requested" || r.Status == "Scheduled")));
            public Task<bool> UpdateStatusAsync(int requestId, string status, int? assignedToUserId, string? notes) => Task.FromResult(true);
        }

        private class FakePropRepoProxy : DispatchProxy
        {
            public List<Property> Props { get; } = new();
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod!.Name == nameof(IPropertyRepository.GetByIdAsync) && args?.Length == 1)
                {
                    int id = (int)args[0]!;
                    return Task.FromResult(Props.FirstOrDefault(p => p.PropertyID == id));
                }
                if (targetMethod.Name == nameof(IPropertyRepository.GetAllPropertiesAsync))
                    return Task.FromResult(Props.ToList());
                if (targetMethod.Name == nameof(IPropertyRepository.GetPropertiesByOwnerIdAsync) && args?.Length == 1)
                    return Task.FromResult(Props.Where(p => p.OwnerID == (int)args[0]!).ToList());
                if (targetMethod.Name == nameof(IPropertyRepository.ApprovePropertyAsync) && args?.Length >= 3)
                {
                    int id = (int)args[0]!;
                    bool approved = (bool)args[1]!;
                    string? status = args[2] as string;
                    var p = Props.FirstOrDefault(x => x.PropertyID == id);
                    if (p == null) return Task.FromResult(false);
                    p.IsApproved = approved;
                    if (!string.IsNullOrEmpty(status)) p.ListingStatus = status;
                    return Task.FromResult(true);
                }
                if (targetMethod.ReturnType == typeof(Task<bool>)) return Task.FromResult(true);
                if (targetMethod.ReturnType.IsGenericType && targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var inner = targetMethod.ReturnType.GetGenericArguments()[0];
                    if (inner.IsGenericType && inner.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        var listInstance = Activator.CreateInstance(inner);
                        return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, new[] { listInstance });
                    }
                }
                return null;
            }
        }

        private class FakeSubRepoProxy : DispatchProxy
        {
            private int _seq = 900;
            public List<SubscriptionPlan> Plans { get; } = new();
            public List<OwnerSubscription> Subs { get; } = new();
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                switch (targetMethod!.Name)
                {
                    case nameof(ISubscriptionRepository.GetPlanByIdAsync):
                        return Task.FromResult(Plans.FirstOrDefault(p => p.PlanID == (int)args![0]!));
                    case nameof(ISubscriptionRepository.SubscribeOwnerAsync):
                    case nameof(ISubscriptionRepository.GrantSubscriptionAsync):
                        var sub = (OwnerSubscription)args![0]!;
                        sub.SubscriptionID = ++_seq;
                        Subs.Add(sub);
                        return Task.FromResult(sub.SubscriptionID);
                    case nameof(ISubscriptionRepository.GetSubscriptionByIdAsync):
                        return Task.FromResult(Subs.FirstOrDefault(s => s.SubscriptionID == (int)args![0]!));
                    case nameof(ISubscriptionRepository.UpdateSubscriptionPaymentStatusAsync):
                        var s = Subs.FirstOrDefault(x => x.SubscriptionID == (int)args![0]!);
                        if (s != null) s.PaymentStatus = (string)args![1]!;
                        return Task.FromResult(s != null);
                    case nameof(ISubscriptionRepository.GetActiveSubscriptionByOwnerIdAsync):
                        return Task.FromResult<OwnerSubscription?>(null);
                    case nameof(ISubscriptionRepository.GetSubscriptionHistoryByOwnerIdAsync):
                        return Task.FromResult(Subs.Where(x => x.OwnerID == (int)args![0]!).ToList());
                    default:
                        return targetMethod.ReturnType == typeof(Task<bool>) ? Task.FromResult(true) : null;
                }
            }
        }

        private sealed class FakeUserNotifRepo : IUserNotificationRepository
        {
            private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
            public Task<bool> InsertNotificationAsync(UserNotification notification) => Task.FromResult(true);
            public Task<List<UserNotification>> GetUserNotificationsAsync(int userId, int limit = 20) => Task.FromResult(new List<UserNotification>());
            public Task<int> GetUnreadCountAsync(int userId) => Task.FromResult(0);
            public Task<bool> MarkAsReadAsync(int notificationId, int userId) => Task.FromResult(true);
            public Task<int> MarkExpiryNotificationsReadAsync(int userId) => Task.FromResult(0);
            public Task<bool> TryRecordDispatchAsync(string notificationKey) => Task.FromResult(_keys.Add(notificationKey));
        }

        private sealed class FakePartnerLeadService : IPartnerLeadService
        {
            public Task<int> CreateLeadAsync(PartnerLeadCreateViewModel model) => Task.FromResult(1);
            public Task<List<PartnerLead>> GetAllLeadsAsync(string? status = null) => Task.FromResult(new List<PartnerLead>());
            public Task<PartnerLead> GetLeadByIdAsync(int leadId) => Task.FromResult(new PartnerLead());
            public Task<(bool success, string generatedPassword, string activationToken, int accountId, string roleDisplayName, string email, string? phone)> ApproveLeadAsync(int leadId, int staffUserId)
                => Task.FromResult((true, "", "", 1, "Owner", "owner@example.com", (string?)null));
            public Task<(bool success, int userId, string generatedPassword, string email, string? phone, string fullName)> ApproveOwnerRequestAsync(int requestId, int staffUserId, string? notes)
                => Task.FromResult((true, 1, "", "owner@example.com", (string?)null, "Owner"));
            public Task<bool> RejectLeadAsync(int leadId, int staffUserId) => Task.FromResult(true);
            public Task<bool> ActivateOwnerAccountAsync(ActivateOwnerViewModel model) => Task.FromResult(true);
        }

        private sealed class SimpleTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private sealed class RecordingLogger<T> : ILogger<T>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => Entries.Add((logLevel, formatter(state, exception)));
        }

        private static string FindRepoRoot()
        {
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 6; i++)
            {
                if (File.Exists(Path.Combine(dir, "BookMyFun.csproj"))) return dir;
                dir = Directory.GetParent(dir)?.FullName ?? dir;
            }
            return Directory.GetCurrentDirectory();
        }

        [Fact]
        public async Task All13TransactionalEvents_WriteTestModeFiles_Deduplicate_AndLeakZeroSecrets()
        {
            string repoRoot = FindRepoRoot();
            string tempEmailDir = Path.Combine(repoRoot, "logs", "emails", $"test_verify_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempEmailDir);

            const string sensitiveSmtpPassword = "SensitiveSmtpPassword_NeverLeak_987";
            const string sensitiveAadhaar = "999988887777";
            const string sensitivePan = "ABCDE1234F";
            const string sensitiveBankAcct = "12345678901234";

            try
            {
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Email:TestMode"] = "true",
                        ["Email:TestOutputDirectory"] = tempEmailDir,
                        ["PublicBaseUrl"] = "https://nayesta.example",
                        ["Smtp:Host"] = "mail.example.invalid",
                        ["Smtp:Port"] = "587",
                        ["Smtp:Username"] = "info@nayesta.com",
                        ["Smtp:Password"] = sensitiveSmtpPassword,
                        ["Smtp:EnableSsl"] = "true"
                    })
                    .Build();

                var outboxRepo = new InMemoryOutboxRepo();
                var userRepo = new InMemoryUserRepo();
                var propRepoProxy = DispatchProxy.Create<IPropertyRepository, FakePropRepoProxy>();
                var propList = ((FakePropRepoProxy)(object)propRepoProxy).Props;
                var bookingRepo = new FakeBookingRepo();
                var subRepoProxy = DispatchProxy.Create<ISubscriptionRepository, FakeSubRepoProxy>();
                var subState = (FakeSubRepoProxy)(object)subRepoProxy;
                var kycRepo = new FakePartnerKycRepo();
                var shootRepo = new FakePhotoShootRepo();
                var userNotifRepo = new FakeUserNotifRepo();
                var configRepo = new FakeConfigRepo();

                var commsLogger = new RecordingLogger<CommunicationService>();
                var emailQueue = new EmailQueueService(NullLogger<EmailQueueService>.Instance);
                var comms = new CommunicationService(configRepo, config, emailQueue, commsLogger, null, outboxRepo, new FakeHostEnv());

                var dispatcher = new NotificationDispatcherService(
                    outboxRepo, userNotifRepo, userRepo, bookingRepo, propRepoProxy,
                    new BookMyFun.Data.DBHelper(config), NullLogger<NotificationDispatcherService>.Instance);

                int staffId = await userRepo.CreateUserAsync(new User { FullName = "Staff Admin", Email = "staff.admin@example.com", Role = "Admin", IsActive = true, NotificationEmailEnabled = true });

                var services = new ServiceCollection();
                services.AddSingleton<INotificationOutboxRepository>(outboxRepo);
                services.AddSingleton<IUserRepository>(userRepo);
                services.AddSingleton<IPropertyRepository>(propRepoProxy);
                services.AddSingleton<IBookingRepository>(bookingRepo);
                services.AddSingleton<IEmailService>(comms);
                services.AddSingleton<IWhatsAppService>(comms);
                services.AddSingleton<IConfiguration>(config);
                var sp = services.BuildServiceProvider();

                var outboxWorker = new NotificationOutboxBackgroundService(sp, NullLogger<NotificationOutboxBackgroundService>.Instance);
                var holdExpiryWorker = new BookingHoldExpiryBackgroundService(sp, NullLogger<BookingHoldExpiryBackgroundService>.Instance);

                var accountCtrl = new AccountController(
                    userRepo, new FakeOtpRepo(), null!, new FakePartnerLeadService(),
                    comms, comms, bookingRepo, propRepoProxy, null!, null!,
                    NullLogger<AccountController>.Instance, configRepo, config);
                accountCtrl.TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider());
                accountCtrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

                // 1. Guest registration
                await accountCtrl.Register(new RegisterViewModel
                {
                    FullName = "Guest One",
                    Email = "guest.one@example.com",
                    Phone = "9876543210",
                    Password = "GuestPass!23",
                    OtpCode = "123456",
                    Role = "Client"
                });
                var guest = await userRepo.GetByEmailAsync("guest.one@example.com");
                Assert.NotNull(guest);

                // 2. Partner sign-up
                await accountCtrl.PartnerRegister(new PartnerRegisterViewModel
                {
                    FullName = "Partner Host",
                    Email = "partner.host@example.com",
                    Phone = "9876543211",
                    Password = "PartnerPass!23",
                    ConfirmPassword = "PartnerPass!23",
                    Role = "Owner"
                });
                var partner = await userRepo.GetByEmailAsync("partner.host@example.com");
                Assert.NotNull(partner);

                // 3. Password reset (single-use 48h link, hash-only in store)
                await accountCtrl.RequestPasswordResetLink("guest.one@example.com");
                Assert.Single(userRepo.Tokens);

                // Seed property
                var prop = new Property
                {
                    PropertyID = 401,
                    OwnerID = partner!.UserID,
                    Title = "Serene Farmhouse",
                    Location = "Hyderabad",
                    PricePerNight = 8500m,
                    MaxGuests = 10,
                    MinStayNights = 1,
                    MaxStayNights = 30,
                    IsApproved = true,
                    ListingStatus = "Draft"
                };
                propList.Add(prop);

                // 4. Booking created (Hold)
                var bookingCtrl = new BookingController(
                    propRepoProxy, bookingRepo, Proxy<IEventPackageRepository>(), new BookingPricingService(), Proxy<ICommissionService>(),
                    Proxy<IRefundEngineService>(), Proxy<IRefundRepository>(), Proxy<IPaymentGatewayService>(), comms, comms, userRepo,
                    Proxy<IIdVerificationService>(), Proxy<IMediaStorageService>(), configRepo, NullLogger<BookingController>.Instance,
                    null, null, null, null, dispatcher,
                    null, null, Options.Create(new BookingOptions { HoldMinutes = 15 }),
                    new AvailabilityService(bookingRepo));
                bookingCtrl.TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider());
                bookingCtrl.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, guest!.UserID.ToString()),
                            new Claim(ClaimTypes.Role, "Client")
                        }, "Test"))
                    }
                };
                await bookingCtrl.ConfirmBooking(new BookingCheckoutViewModel
                {
                    PropertyID = prop.PropertyID,
                    CheckInDate = DateTime.Today.AddDays(5),
                    CheckOutDate = DateTime.Today.AddDays(6),
                    BookingType = "FullDay",
                    GuestFullName = "Guest One",
                    GuestPhone = "9876543210",
                    IsClientAlreadyVerified = true
                });
                Assert.Single(bookingRepo.Bookings);
                int bookingId = bookingRepo.Bookings[0].BookingID;

                // 5. Booking expired
                bookingRepo.ExpiredIdsToReturn.Add(bookingId);
                await holdExpiryWorker.RunOnceAsync();

                // 6. Payment verified (outbox with dedupe key for guest + owner)
                await dispatcher.DispatchBookingConfirmedAsync(bookingId);
                await dispatcher.DispatchReceiptAvailableAsync(bookingId, $"REC-{bookingId}");
                await outboxWorker.ProcessPendingBatchAsync();

                // 7. Partner KYC submitted
                var kycService = new FakePartnerKycService(kycRepo);
                var partnerKycCtrl = new PartnerKycController(kycService, NullLogger<PartnerKycController>.Instance, comms, userRepo, config);
                partnerKycCtrl.TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider());
                partnerKycCtrl.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, partner.UserID.ToString()),
                            new Claim(ClaimTypes.Email, partner.Email),
                            new Claim(ClaimTypes.Name, partner.FullName),
                            new Claim(ClaimTypes.Role, "Owner")
                        }, "Test"))
                    }
                };
                await partnerKycCtrl.Index(new PartnerKycSubmitViewModel
                {
                    AccountHolderName = partner.FullName,
                    BankAccountNumber = sensitiveBankAcct,
                    IfscCode = "SBIN0001234"
                });

                // 8 & 9. Partner KYC verified & rejected
                var adminKycCtrl = new AdminPartnerKycController(kycRepo, Proxy<IMediaStorageService>(), NullLogger<AdminPartnerKycController>.Instance, comms, userRepo, config);
                adminKycCtrl.TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider());
                adminKycCtrl.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, staffId.ToString()),
                            new Claim(ClaimTypes.Role, "Admin")
                        }, "Test"))
                    }
                };
                await adminKycCtrl.Approve(partner.UserID);
                await adminKycCtrl.Reject(partner.UserID, "PAN image blurry");

                // 10. Property revoked (while in Draft with IsApproved=true)
                var adminCtrl = new AdminController(
                    propRepoProxy, userRepo, bookingRepo, subRepoProxy, configRepo, Proxy<IOwnerRequestRepository>(), Proxy<IContactRepository>(),
                    Proxy<ISiteSettingsService>(), Proxy<IFinancialRepository>(), new FakeWebHostEnv(), Proxy<IRefundEngineService>(),
                    new FakePartnerLeadService(), Proxy<IDamageClaimService>(), Proxy<IIdVerificationService>(), Proxy<IMediaStorageService>(),
                    Proxy<IEventPackageRepository>(), null, dispatcher, null, null, null, outboxRepo, null, comms, config);
                adminCtrl.TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider());
                adminCtrl.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, staffId.ToString()),
                            new Claim(ClaimTypes.Role, "Admin")
                        }, "Test"))
                    }
                };
                await adminCtrl.ApproveProperty(prop.PropertyID, approve: false, status: "Draft", reason: "Missing safety certificate");

                // 11. Shoot requested (owner POST -> saves row + owner email + staff notice)
                var ownerCtrl = new OwnerController(
                    propRepoProxy, userRepo, bookingRepo, subRepoProxy, Proxy<IPaymentGatewayService>(), Proxy<IDamageClaimService>(), configRepo,
                    NullLogger<OwnerController>.Instance, comms, comms, null, null, dispatcher, userNotifRepo,
                    kycService, shootRepo, config);
                ownerCtrl.TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider());
                ownerCtrl.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, partner.UserID.ToString()),
                            new Claim(ClaimTypes.Role, "Owner")
                        }, "Test"))
                    }
                };
                await ownerCtrl.RequestPhotoShoot(prop.PropertyID, "Both", "Morning lighting preferred");
                Assert.Single(shootRepo.Requests);
                await outboxWorker.ProcessPendingBatchAsync();

                // 12. Subscription purchased
                var plan = new SubscriptionPlan { PlanID = 1, PlanName = "Pro Host", DurationInDays = 90, MaxListings = 5, Price = 2999m, IsActive = true };
                subState.Plans.Add(plan);
                int subId = await subRepoProxy.SubscribeOwnerAsync(new OwnerSubscription
                {
                    OwnerID = partner.UserID,
                    PlanID = plan.PlanID,
                    PlanName = plan.PlanName,
                    MaxListings = plan.MaxListings,
                    MaxListingsSnapshot = plan.MaxListings,
                    DurationDaysSnapshot = plan.DurationInDays,
                    StartDate = DateTime.UtcNow.AddDays(-1),
                    EndDate = DateTime.UtcNow.AddDays(89),
                    PricePaid = plan.Price,
                    PaymentStatus = "Pending"
                });
                await ownerCtrl.VerifySubscriptionPayment(subId, "ord_1", "pay_1", "sig_1");

                // 13. Subscription granted by admin
                await adminCtrl.GrantSubscriptionPlan(new GrantSubscriptionPlanFormModel
                {
                    OwnerID = partner.UserID,
                    PlanID = plan.PlanID,
                    StartDate = DateTime.UtcNow,
                    GrantReason = "Launch partner promotion"
                });

                // Dedupe check: repeated DedupeKey produces no second email
                int countBeforeDuplicate = Directory.GetFiles(tempEmailDir, "*.html").Length;
                bool dupResult = await comms.SendTemplatedEmailAsync(
                    guest.UserID, bookingId, guest.Email, "BookingCreated", $"booking-created-{bookingId}",
                    "Duplicate check", "Duplicate check", "<p>Duplicate</p>");
                Assert.False(dupResult);
                Assert.Equal(countBeforeDuplicate, Directory.GetFiles(tempEmailDir, "*.html").Length);

                // Verify all 13 event types produced Sent outbox rows & email files
                var sentEventTypes = outboxRepo.Rows.Where(r => r.Channel == "Email" && r.Status == "Sent").Select(r => r.EventType).Distinct().ToList();
                Assert.Contains("GuestWelcome", sentEventTypes);
                Assert.Contains("PartnerWelcome", sentEventTypes);
                Assert.Contains("PasswordReset", sentEventTypes);
                Assert.Contains("BookingCreated", sentEventTypes);
                Assert.Contains("BookingExpired", sentEventTypes);
                Assert.Contains("BookingConfirmed", sentEventTypes);
                Assert.Contains("PaymentReceived", sentEventTypes);
                Assert.Contains("PartnerKycSubmitted", sentEventTypes);
                Assert.Contains("PartnerKycVerified", sentEventTypes);
                Assert.Contains("PartnerKycRejected", sentEventTypes);
                Assert.Contains("PropertyRevoked", sentEventTypes);
                Assert.Contains("PhotoShootRequested", sentEventTypes);
                Assert.Contains("SubscriptionPurchased", sentEventTypes);
                Assert.Contains("SubscriptionGranted", sentEventTypes);

                // Verify Shoot requested created 1 owner email + 1 staff notice
                var shootEmails = outboxRepo.Rows.Where(r => r.EventType == "PhotoShootRequested" && r.Channel == "Email" && r.Status == "Sent").ToList();
                Assert.Equal(2, shootEmails.Count);
                Assert.Contains(shootEmails, x => x.UserID == partner.UserID);
                Assert.Contains(shootEmails, x => x.UserID == staffId);

                // Verify zero passwords/Aadhaar/PAN/account numbers in any email file, log, or outbox row
                foreach (var file in Directory.GetFiles(tempEmailDir, "*.html"))
                {
                    string content = await File.ReadAllTextAsync(file);
                    Assert.DoesNotContain(sensitiveSmtpPassword, content);
                    Assert.DoesNotContain(sensitiveAadhaar, content);
                    Assert.DoesNotContain(sensitivePan, content);
                    Assert.DoesNotContain(sensitiveBankAcct, content);
                    Assert.DoesNotContain("GuestPass!23", content);
                    Assert.DoesNotContain("PartnerPass!23", content);
                    Assert.DoesNotContain("guest.one@example.com", content);
                    Assert.DoesNotContain("partner.host@example.com", content);
                }

                foreach (var log in commsLogger.Entries)
                {
                    Assert.DoesNotContain(sensitiveSmtpPassword, log.Message);
                    Assert.DoesNotContain(sensitiveAadhaar, log.Message);
                    Assert.DoesNotContain(sensitivePan, log.Message);
                    Assert.DoesNotContain(sensitiveBankAcct, log.Message);
                }

                foreach (var row in outboxRepo.Rows)
                {
                    Assert.DoesNotContain(sensitiveSmtpPassword, row.Body);
                    Assert.DoesNotContain(sensitiveAadhaar, row.Body);
                    Assert.DoesNotContain(sensitivePan, row.Body);
                    Assert.DoesNotContain(sensitiveBankAcct, row.Body);
                    Assert.DoesNotContain("?token=", row.Body);
                }
            }
            finally
            {
                if (Directory.Exists(tempEmailDir))
                {
                    Directory.Delete(tempEmailDir, recursive: true);
                }
            }
        }

        [Fact]
        public async Task FailedEmail_Retries3Times_MarksFailed_AndAdminCanRetry()
        {
            const string dummyPassword = "DummySmtpSecret_DoNotLog";
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:TestMode"] = "false",
                    ["Smtp:Host"] = "nonexistent.smtp.invalid",
                    ["Smtp:Port"] = "587",
                    ["Smtp:EnableSsl"] = "true",
                    ["Smtp:Username"] = "info@nayesta.com",
                    ["Smtp:Password"] = dummyPassword
                })
                .Build();

            var outboxRepo = new InMemoryOutboxRepo();
            var userRepo = new InMemoryUserRepo();
            int uid = await userRepo.CreateUserAsync(new User { FullName = "Retry User", Email = "retry.user@example.com", Role = "Client", IsActive = true, NotificationEmailEnabled = true });

            var comms = new CommunicationService(new FakeConfigRepo(), config, new EmailQueueService(NullLogger<EmailQueueService>.Instance), NullLogger<CommunicationService>.Instance, null, outboxRepo, new FakeHostEnv());

            bool firstAttempt = await comms.SendTemplatedEmailAsync(uid, uid, "retry.user@example.com", "GuestWelcome", $"retry-test-{uid}", "Welcome", "Welcome", "<p>Hello</p>");
            Assert.False(firstAttempt);
            var row = Assert.Single(outboxRepo.Rows);
            Assert.Equal("Retry", row.Status);
            Assert.Equal(1, row.RetryCount);
            Assert.False(string.IsNullOrWhiteSpace(row.LastError));
            Assert.DoesNotContain(dummyPassword, row.LastError);

            var services = new ServiceCollection();
            services.AddSingleton<INotificationOutboxRepository>(outboxRepo);
            services.AddSingleton<IUserRepository>(userRepo);
            services.AddSingleton<IEmailService>(comms);
            services.AddSingleton<IWhatsAppService>(comms);
            services.AddSingleton<IConfiguration>(config);
            var sp = services.BuildServiceProvider();
            var worker = new NotificationOutboxBackgroundService(sp, NullLogger<NotificationOutboxBackgroundService>.Instance);

            row.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await worker.ProcessPendingBatchAsync();
            Assert.Equal("Retry", row.Status);
            Assert.Equal(2, row.RetryCount);

            row.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await worker.ProcessPendingBatchAsync();
            Assert.Equal("Failed", row.Status);
            Assert.Equal(3, row.RetryCount);
            Assert.DoesNotContain(dummyPassword, row.LastError ?? string.Empty);

            string retryDir = Path.Combine(Path.GetTempPath(), $"bmf_retry_{Guid.NewGuid():N}");
            try
            {
                var testModeConfig = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Email:TestMode"] = "true",
                        ["Email:TestOutputDirectory"] = retryDir
                    })
                    .Build();
                var testComms = new CommunicationService(new FakeConfigRepo(), testModeConfig, new EmailQueueService(NullLogger<EmailQueueService>.Instance), NullLogger<CommunicationService>.Instance, null, outboxRepo, new FakeHostEnv());
                var adminCtrl = new AdminController(
                    Proxy<IPropertyRepository>(), userRepo, new FakeBookingRepo(), Proxy<ISubscriptionRepository>(), new FakeConfigRepo(),
                    Proxy<IOwnerRequestRepository>(), Proxy<IContactRepository>(), Proxy<ISiteSettingsService>(), Proxy<IFinancialRepository>(),
                    new FakeWebHostEnv(), Proxy<IRefundEngineService>(), new FakePartnerLeadService(), Proxy<IDamageClaimService>(),
                    Proxy<IIdVerificationService>(), Proxy<IMediaStorageService>(), Proxy<IEventPackageRepository>(),
                    null, null, null, null, null, outboxRepo, null, testComms, testModeConfig);
                adminCtrl.TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider());
                adminCtrl.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Admin") }, "Test"))
                    }
                };

                await adminCtrl.RetryEmailOutbox(row.OutboxID);
                Assert.Equal("Sent", row.Status);
            }
            finally
            {
                if (Directory.Exists(retryDir)) Directory.Delete(retryDir, recursive: true);
            }
        }

        [Fact]
        public async Task AdminEmailOutbox_RequiresAdminOnlyPolicy_DeniesPartnerAndGuestWith403()
        {
            var classAuthorize = typeof(AdminController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().FirstOrDefault();
            Assert.NotNull(classAuthorize);
            Assert.Equal("AdminOnly", classAuthorize!.Policy);
            Assert.Null(typeof(AdminController).GetMethod(nameof(AdminController.EmailOutbox))!.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Null(typeof(AdminController).GetMethod(nameof(AdminController.RetryEmailOutbox))!.GetCustomAttribute<AllowAnonymousAttribute>());

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddAuthorization(options =>
            {
                options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
            });
            var sp = services.BuildServiceProvider();
            var authService = sp.GetRequiredService<IAuthorizationService>();

            var partnerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "2"), new Claim(ClaimTypes.Role, "Owner") }, "Cookie"));
            var guestPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "3"), new Claim(ClaimTypes.Role, "Client") }, "Cookie"));
            var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Admin") }, "Cookie"));

            Assert.False((await authService.AuthorizeAsync(partnerPrincipal, null, "AdminOnly")).Succeeded);
            Assert.False((await authService.AuthorizeAsync(guestPrincipal, null, "AdminOnly")).Succeeded);
            Assert.True((await authService.AuthorizeAsync(adminPrincipal, null, "AdminOnly")).Succeeded);
        }
    }
}
