using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class PartnerRegistrationAndActivationTests
    {
        private class MockPartnerLeadRepo : IPartnerLeadRepository
        {
            public Dictionary<int, PartnerLead> Leads = new();
            private int _idCounter = 1;

            public Task<int> InsertLeadAsync(PartnerLead lead)
            {
                lead.LeadID = _idCounter++;
                Leads[lead.LeadID] = lead;
                return Task.FromResult(lead.LeadID);
            }

            public Task<PartnerLead?> GetLeadByIdAsync(int leadId)
            {
                Leads.TryGetValue(leadId, out var lead);
                return Task.FromResult(lead);
            }

            public Task<PartnerLead?> GetLeadByTokenAsync(string token)
            {
                var lead = Leads.Values.FirstOrDefault(l => l.ActivationToken == token);
                return Task.FromResult(lead);
            }

            public Task<List<PartnerLead>> GetAllLeadsAsync(string? status = null)
            {
                var list = string.IsNullOrEmpty(status)
                    ? Leads.Values.ToList()
                    : Leads.Values.Where(l => l.Status == status).ToList();
                return Task.FromResult(list);
            }

            public Task<bool> UpdateLeadStatusAsync(int leadId, string status, int? reviewedByUserId, string? activationToken = null, DateTime? tokenExpiresAt = null)
            {
                if (Leads.TryGetValue(leadId, out var lead))
                {
                    lead.Status = status;
                    lead.ReviewedByUserID = reviewedByUserId;
                    if (activationToken != null) lead.ActivationToken = activationToken;
                    if (tokenExpiresAt != null) lead.TokenExpiresAt = tokenExpiresAt;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }

            public Task<bool> MarkTokenUsedAsync(int leadId)
            {
                if (Leads.TryGetValue(leadId, out var lead))
                {
                    lead.ActivationToken = null;
                    lead.TokenExpiresAt = null;
                    lead.Status = "Approved";
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
        }

        private class MockUserRepo : IUserRepository
        {
            public Dictionary<int, User> Users = new();
            private int _idCounter = 1;

            public Task<User?> GetByIdAsync(int userId)
            {
                Users.TryGetValue(userId, out var user);
                return Task.FromResult(user);
            }

            public Task<User?> GetByEmailAsync(string email)
            {
                var user = Users.Values.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(user);
            }

            public Task<User?> GetByPhoneAsync(string phone)
            {
                var user = Users.Values.FirstOrDefault(u => u.Phone == phone);
                return Task.FromResult(user);
            }

            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult<User?>(null);

            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(Users.Values.ToList());
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(Users.Values.Where(u => u.Role == role).ToList());

            public Task<int> CreateUserAsync(User user)
            {
                user.UserID = _idCounter++;
                Users[user.UserID] = user;
                return Task.FromResult(user.UserID);
            }

            public Task<bool> UpdateUserAsync(User user)
            {
                Users[user.UserID] = user;
                return Task.FromResult(true);
            }

            public Task<bool> ToggleUserStatusAsync(int userId, bool isActive) => Task.FromResult(true);
            public Task<bool> UpdatePasswordAsync(int userId, string passwordHash) => Task.FromResult(true);
            public Task<bool> UpdateProfilePictureAsync(int userId, string profilePictureUrl) => Task.FromResult(true);
            public Task<bool> UpdateCustomCommissionRateAsync(int userId, decimal? customRate) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);

            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash)
            {
                var u = Users.Values.FirstOrDefault(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase));
                if (u != null)
                {
                    u.PasswordHash = newPasswordHash;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }

            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? commissionRate) => Task.FromResult(true);
            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
        }

        private class MockOwnerReqRepo : IOwnerRequestRepository
        {
            public Task<int> CreateRequestAsync(int userId, int trialDays = 2) => Task.FromResult(1);
            public Task<OwnerRequest?> GetActiveRequestByUserIdAsync(int userId) => Task.FromResult<OwnerRequest?>(null);
            public Task<OwnerRequest?> GetRequestByIdAsync(int requestId) => Task.FromResult<OwnerRequest?>(null);
            public Task<List<OwnerRequest>> GetAllPendingRequestsAsync() => Task.FromResult(new List<OwnerRequest>());
            public Task<List<OwnerRequest>> GetAllRequestsAsync() => Task.FromResult(new List<OwnerRequest>());
            public Task<bool> ApproveOwnerRequestAsync(int requestId, int reviewerUserId, string? notes, string? newPasswordHash = null) => Task.FromResult(true);
            public Task<bool> RejectOwnerRequestAsync(int requestId, int reviewerUserId, string? notes) => Task.FromResult(true);
        }

        private class MockEmailService : IEmailService
        {
            public List<(string To, string Subject, string Body)> SentEmails = new();

            public Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml) => Task.FromResult(true);
            public Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string purpose, string? recipientName = null) => Task.FromResult(true);

            public Task EnqueueEmailAsync(string toEmail, string subject, string bodyHtml, string messageType = "Notification")
            {
                SentEmails.Add((toEmail, subject, bodyHtml));
                return Task.CompletedTask;
            }
        }

        private class MockWhatsAppService : IWhatsAppService
        {
            public List<(string Phone, string Message)> SentMessages = new();

            public Task<bool> SendWhatsAppMessageAsync(string phoneNumber, string message) => Task.FromResult(true);

            public Task EnqueueWhatsAppAsync(string phoneNumber, string message, string messageType = "Notification")
            {
                SentMessages.Add((phoneNumber, message));
                return Task.CompletedTask;
            }

            public Task<bool> SendBookingConfirmationAsync(string phoneNumber, string clientName, string propertyTitle, string bookingId, decimal amount) => Task.FromResult(true);
            public Task<bool> SendOwnerBookingAlertAsync(string phoneNumber, string ownerName, string propertyTitle, string bookingId, decimal hostGross, decimal hostPayout, string guestName, DateTime checkIn, DateTime checkOut, bool hasDecoration, decimal decorationAmount) => Task.FromResult(true);
            public Task<bool> SendEventManagerSetupAlertAsync(string phoneNumber, string eventManagerName, string propertyTitle, string bookingId, string packageName, decimal packageAmount, DateTime eventDate, string clientName) => Task.FromResult(true);
        }

        [Fact]
        public async Task ApprovePartnerLead_Generates48HourSingleUseToken_AndDoesNotSendPermanentPassword()
        {
            // Arrange
            var leadRepo = new MockPartnerLeadRepo();
            var userRepo = new MockUserRepo();
            var ownerReqRepo = new MockOwnerReqRepo();
            var emailService = new MockEmailService();
            var whatsAppService = new MockWhatsAppService();

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AppUrl", "https://bookmyfun.in" }
            }).Build();

            var service = new PartnerLeadService(
                leadRepo, userRepo, ownerReqRepo, emailService, whatsAppService,
                NullLogger<PartnerLeadService>.Instance, config);

            var leadId = await leadRepo.InsertLeadAsync(new PartnerLead
            {
                FullName = "Ramesh Farmhouse Host",
                Email = "ramesh@example.com",
                Phone = "9876543210",
                PropertyName = "Green Acres Retreat",
                PartnerType = "Owner",
                Status = "New"
            });

            // Act: Staff approves the lead
            var result = await service.ApproveLeadAsync(leadId, staffUserId: 1);

            // Assert
            Assert.True(result.success);
            Assert.NotEmpty(result.activationToken);
            Assert.Equal("Farmhouse & Villa Host", result.roleDisplayName);

            // Crucial Security Rule: Generated plaintext password must NOT be exposed or transmitted
            Assert.Empty(result.generatedPassword);

            // Verify database state: token set, expiration set ~48 hours
            var approvedLead = await leadRepo.GetLeadByIdAsync(leadId);
            Assert.NotNull(approvedLead);
            Assert.Equal("Approved", approvedLead.Status);
            Assert.NotNull(approvedLead.ActivationToken);
            Assert.NotNull(approvedLead.TokenExpiresAt);
            Assert.True(approvedLead.TokenExpiresAt > DateTime.UtcNow.AddHours(47));
            Assert.True(approvedLead.TokenExpiresAt <= DateTime.UtcNow.AddHours(49));

            // Verify Email notification sent
            Assert.Single(emailService.SentEmails);
            var email = emailService.SentEmails[0];
            Assert.Equal("ramesh@example.com", email.To);
            Assert.Contains("Set Your Password &amp; Activate Account", email.Body);
            Assert.Contains(approvedLead.ActivationToken, email.Body);
            Assert.Contains("48 hours", email.Body);

            // Verify WhatsApp notification sent
            Assert.Single(whatsAppService.SentMessages);
            var wa = whatsAppService.SentMessages[0];
            Assert.Equal("9876543210", wa.Phone);
            Assert.Contains("valid for 48 hours", wa.Message);
            Assert.Contains(approvedLead.ActivationToken, wa.Message);
            Assert.DoesNotContain("Password: Host@", wa.Message);
        }

        [Fact]
        public async Task ActivatePartnerAccount_SetsPasswordAndCanOnlyBeUsedOnce()
        {
            // Arrange
            var leadRepo = new MockPartnerLeadRepo();
            var userRepo = new MockUserRepo();
            var ownerReqRepo = new MockOwnerReqRepo();
            var emailService = new MockEmailService();
            var whatsAppService = new MockWhatsAppService();

            var service = new PartnerLeadService(
                leadRepo, userRepo, ownerReqRepo, emailService, whatsAppService,
                NullLogger<PartnerLeadService>.Instance);

            var leadId = await leadRepo.InsertLeadAsync(new PartnerLead
            {
                FullName = "Meera Event Stylist",
                Email = "meera@decorstudio.com",
                Phone = "9123456780",
                PropertyName = "Royal Decors",
                PartnerType = "EventManager",
                Status = "New"
            });

            var approval = await service.ApproveLeadAsync(leadId, staffUserId: 1);
            string token = approval.activationToken;

            var activateModel = new ActivateOwnerViewModel
            {
                Token = token,
                Email = "meera@decorstudio.com",
                Password = "MyStrongSecurePassword123!",
                ConfirmPassword = "MyStrongSecurePassword123!"
            };

            // Act 1: First activation succeeds
            bool activated = await service.ActivateOwnerAccountAsync(activateModel);
            Assert.True(activated);

            // Verify user created with correct EventManager role
            var user = await userRepo.GetByEmailAsync("meera@decorstudio.com");
            Assert.NotNull(user);
            Assert.Equal("EventManager", user.Role);
            Assert.True(user.IsActive);

            // Verify token is invalidated in database (single-use)
            var leadAfterActivation = await leadRepo.GetLeadByIdAsync(leadId);
            Assert.Null(leadAfterActivation!.ActivationToken);
            Assert.Null(leadAfterActivation.TokenExpiresAt);

            // Act 2 & Assert: Second activation attempt with the same token MUST throw ValidationException
            await Assert.ThrowsAsync<ValidationException>(async () =>
            {
                await service.ActivateOwnerAccountAsync(activateModel);
            });
        }

        [Fact]
        public async Task ActivatePartnerAccount_WhenExpiredAfter48Hours_ThrowsValidationException()
        {
            // Arrange
            var leadRepo = new MockPartnerLeadRepo();
            var userRepo = new MockUserRepo();
            var ownerReqRepo = new MockOwnerReqRepo();
            var emailService = new MockEmailService();
            var whatsAppService = new MockWhatsAppService();

            var service = new PartnerLeadService(
                leadRepo, userRepo, ownerReqRepo, emailService, whatsAppService,
                NullLogger<PartnerLeadService>.Instance);

            string expiredToken = "expired_token_12345";
            var leadId = await leadRepo.InsertLeadAsync(new PartnerLead
            {
                FullName = "Late Host",
                Email = "latehost@example.com",
                Phone = "9988776655",
                PropertyName = "Delayed Villa",
                PartnerType = "Owner",
                Status = "Approved",
                ActivationToken = expiredToken,
                TokenExpiresAt = DateTime.UtcNow.AddHours(-1) // Expired 1 hour ago
            });

            var activateModel = new ActivateOwnerViewModel
            {
                Token = expiredToken,
                Email = "latehost@example.com",
                Password = "NewPassword123!",
                ConfirmPassword = "NewPassword123!"
            };

            // Act & Assert: Expired token must be rejected
            var ex = await Assert.ThrowsAsync<ValidationException>(async () =>
            {
                await service.ActivateOwnerAccountAsync(activateModel);
            });
            Assert.True(ex.ValidationErrors != null && ex.ValidationErrors.ContainsKey("Token") && ex.ValidationErrors["Token"].Any(e => e.Contains("expired", StringComparison.OrdinalIgnoreCase)));
        }
    }
}
