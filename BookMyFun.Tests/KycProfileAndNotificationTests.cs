using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class KycProfileAndNotificationTests
    {
        private class MockIdVerificationRepository : IIdVerificationRepository
        {
            public ClientIdVerification? VerificationRecord { get; set; }
            public bool ReviewCalled { get; private set; }
            public string? ReviewedStatus { get; private set; }
            public string? ReviewedReason { get; private set; }

            public Task<int> InsertVerificationAsync(ClientIdVerification verification) => Task.FromResult(1);
            public Task<ClientIdVerification?> GetVerificationByIdAsync(int verificationId) => Task.FromResult(VerificationRecord);
            public Task<ClientIdVerification?> GetVerificationByClientIdAsync(int clientId) => Task.FromResult(VerificationRecord);
            public Task<List<ClientIdVerification>> GetAllVerificationsAsync(string? status = null) => Task.FromResult(new List<ClientIdVerification>());
            public Task<bool> ReviewVerificationAsync(int verificationId, string status, int verifiedByUserId, string? rejectionReason = null)
            {
                ReviewCalled = true;
                ReviewedStatus = status;
                ReviewedReason = rejectionReason;
                if (VerificationRecord != null)
                {
                    VerificationRecord.VerificationStatus = status;
                    VerificationRecord.RejectionReason = rejectionReason;
                }
                return Task.FromResult(true);
            }
        }

        private class MockUserRepository : IUserRepository
        {
            public User? UserRecord { get; set; }
            public Task<User?> GetByIdAsync(int userId) => Task.FromResult(UserRecord);
            public Task<User?> GetByEmailAsync(string email) => Task.FromResult(UserRecord);
            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult<User?>(null);
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(new List<User>());
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(new List<User>());
            public Task<int> CreateUserAsync(User user) => Task.FromResult(user.UserID);
            public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
        }

        private class MockEmailService : IEmailService
        {
            public bool RejectionNotificationCalled { get; private set; }
            public string? LastToEmail { get; private set; }
            public string? LastClientName { get; private set; }
            public string? LastReason { get; private set; }
            public int? LastClientId { get; private set; }

            public Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml) => Task.FromResult(true);
            public Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string purpose, string? recipientName = null) => Task.FromResult(true);
            public Task EnqueueEmailAsync(string toEmail, string subject, string bodyHtml, string messageType = "Notification") => Task.FromResult(true);
            public Task<bool> SendBookingReceiptEmailAsync(string toEmail, string clientName, BookingReceiptViewModel receipt, string receiptUrl) => Task.FromResult(true);
            public Task<bool> SendOwnerSettlementReceiptEmailAsync(string toEmail, string ownerName, BookingReceiptViewModel receipt, string settlementReceiptUrl) => Task.FromResult(true);

            public Task<bool> SendKycRejectionNotificationAsync(string toEmail, string clientName, string? rejectionReason, int clientId, string? documentType = null)
            {
                RejectionNotificationCalled = true;
                LastToEmail = toEmail;
                LastClientName = clientName;
                LastReason = rejectionReason;
                LastClientId = clientId;
                return Task.FromResult(true);
            }
        }

        private static string ResolveViewPath(params string[] segments)
        {
            var combined = new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(segments).ToArray();
            string path = Path.GetFullPath(Path.Combine(combined));
            if (!File.Exists(path))
            {
                var fallback = new[] { Directory.GetCurrentDirectory() }.Concat(segments).ToArray();
                path = Path.GetFullPath(Path.Combine(fallback));
            }
            return path;
        }

        [Fact]
        public void ProfileView_WhenKycIsPendingOrApproved_DoesNotRenderKycStatusBadgeOrLabels()
        {
            // Verify source code of Profile.cshtml enforces:
            // Do not show KYC status to the user while it is pending or approved
            string viewPath = ResolveViewPath("Views", "Account", "Profile.cshtml");
            Assert.True(File.Exists(viewPath), $"Profile.cshtml must exist at path: {viewPath}");

            string viewContent = File.ReadAllText(viewPath);

            // 1. Verify "Under Review" or "Verified" badges are no longer rendered unconditionally
            Assert.DoesNotContain("Model.KycStatus == \"Verified\"", viewContent);
            Assert.DoesNotContain("Model.KycStatus == \"PendingReview\"", viewContent);
            Assert.DoesNotContain("Optional at Checkout", viewContent);

            // 2. Verify that KYC status rendering strictly checks for "Rejected"
            Assert.Contains("Model.KycStatus == \"Rejected\"", viewContent);
            Assert.Contains("Model.KycRejectionReason", viewContent);
            Assert.Contains("Instructions to Resubmit", viewContent);
        }

        [Fact]
        public async Task IdVerificationService_WhenKycRejected_DispatchesEmailAndInAppNotification()
        {
            var mockRepo = new MockIdVerificationRepository
            {
                VerificationRecord = new ClientIdVerification
                {
                    VerificationID = 88,
                    ClientID = 42,
                    ClientName = "John Traveler",
                    ClientEmail = "traveler@example.com",
                    DocumentType = "Passport",
                    VerificationStatus = "PendingReview"
                }
            };

            var mockUserRepo = new MockUserRepository
            {
                UserRecord = new User
                {
                    UserID = 42,
                    FullName = "John Traveler",
                    Email = "traveler@example.com",
                    Role = "Client"
                }
            };

            var mockEmailService = new MockEmailService();
            var idService = new IdVerificationService(
                mockRepo,
                mockUserRepo,
                NullLogger<IdVerificationService>.Instance,
                mockEmailService);

            string rejectionReason = "Document image is blurred and expiry date is unreadable.";
            bool result = await idService.ReviewVerificationAsync(88, "Rejected", staffUserId: 1, rejectionReason: rejectionReason);

            Assert.True(result);
            Assert.True(mockRepo.ReviewCalled);
            Assert.Equal("Rejected", mockRepo.ReviewedStatus);
            Assert.Equal(rejectionReason, mockRepo.ReviewedReason);

            // Verify notification dispatch
            Assert.True(mockEmailService.RejectionNotificationCalled);
            Assert.Equal("traveler@example.com", mockEmailService.LastToEmail);
            Assert.Equal("John Traveler", mockEmailService.LastClientName);
            Assert.Equal(rejectionReason, mockEmailService.LastReason);
            Assert.Equal(42, mockEmailService.LastClientId);
        }

        [Fact]
        public async Task CommunicationService_SendKycRejectionNotificationAsync_EnqueuesBothEmailAndInAppNotification()
        {
            var queue = new EmailQueueService(NullLogger<EmailQueueService>.Instance);
            var inMemorySettings = new System.Collections.Generic.Dictionary<string, string?>
            {
                { "Smtp:Server", "smtp.test.com" }
            };
            IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
            var configRepo = new MockConfigRepository();

            var comms = new CommunicationService(
                configRepo,
                config,
                queue,
                NullLogger<CommunicationService>.Instance);

            string clientEmail = "amit.guest@example.com";
            string clientName = "Amit Guest";
            string reason = "ID document name does not match registered traveler profile.";
            int clientId = 101;

            bool sent = await comms.SendKycRejectionNotificationAsync(clientEmail, clientName, reason, clientId, "Aadhaar");
            Assert.True(sent);

            // 1. Verify in-app notification recorded in EmailQueueService store
            var inAppNotifications = queue.GetInAppNotifications(clientId);
            Assert.NotEmpty(inAppNotifications);
            var notif = inAppNotifications.First();
            Assert.Equal("Identity Verification (KYC) Rejected", notif.Title);
            Assert.Contains(reason, notif.Message);
            Assert.Equal("KycRejection", notif.NotificationType);
            Assert.Equal("/Account/Profile", notif.ActionUrl);

            // 2. Verify channel messages enqueued in background dispatcher queue
            // Message 1: InApp channel message
            var msg1 = await queue.DequeueAsync(CancellationToken.None);
            Assert.NotNull(msg1);
            Assert.Equal("InApp", msg1.Channel);
            Assert.Equal(clientEmail, msg1.ToEmail);
            Assert.Contains(reason, msg1.BodyHtml);

            // Message 2: Email channel message
            var msg2 = await queue.DequeueAsync(CancellationToken.None);
            Assert.NotNull(msg2);
            Assert.Equal("Email", msg2.Channel);
            Assert.Equal(clientEmail, msg2.ToEmail);
            Assert.Contains("Identity Verification Update", msg2.Subject);
            Assert.Contains("Status: Rejected", msg2.BodyHtml);
            Assert.Contains(reason, msg2.BodyHtml);
            Assert.Contains("How to Resubmit Your Verification", msg2.BodyHtml);
        }

        [Fact]
        public void AdminScreens_StillDisplayFullStatus_PendingVerifiedAndRejected()
        {
            // Verify requirement: "Admin screens still show the full status."
            string adminViewPath = ResolveViewPath("Views", "Admin", "IdVerifications.cshtml");
            Assert.True(File.Exists(adminViewPath), $"IdVerifications.cshtml must exist at path: {adminViewPath}");

            string adminContent = File.ReadAllText(adminViewPath);

            // Admin screen must retain all status options and badges
            Assert.Contains("\"PendingReview\"", adminContent);
            Assert.Contains("\"Verified\"", adminContent);
            Assert.Contains("\"Rejected\"", adminContent);
            Assert.Contains("@v.VerificationStatus", adminContent);
        }

        private class MockConfigRepository : IConfigRepository
        {
            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
            public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details) => Task.FromResult(true);
        }
    }
}
