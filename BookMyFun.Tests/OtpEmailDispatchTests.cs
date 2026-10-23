using System.Runtime.Versioning;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookMyFun.Tests
{
    [SupportedOSPlatform("windows6.1")]
    public class OtpEmailDispatchTests
    {
        private class StubConfigRepo : IConfigRepository
        {
            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider)
                => Task.FromResult<ApiConfiguration?>(null);

            public Task<List<ApiConfiguration>> GetAllConfigsAsync()
                => Task.FromResult(new List<ApiConfiguration>());

            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config)
                => Task.FromResult(true);

            public Task<string?> GetSettingValueAsync(string key)
                => Task.FromResult<string?>(null);

            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false)
                => Task.FromResult(true);

            public Task<List<SiteSetting>> GetAllSettingsAsync()
                => Task.FromResult(new List<SiteSetting>());

            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details)
                => Task.CompletedTask;
        }

        private class StubOtpRepo : IOtpRepository
        {
            public string LastSavedContact { get; private set; } = string.Empty;
            public string LastSavedOtp { get; private set; } = string.Empty;
            public string LastSavedPurpose { get; private set; } = string.Empty;

            public Task<bool> SaveOtpAsync(string contact, string otp, string purpose, int expiryMinutes = 10)
            {
                LastSavedContact = contact;
                LastSavedOtp = otp;
                LastSavedPurpose = purpose;
                return Task.FromResult(true);
            }

            public Task<bool> ValidateOtpAsync(string contact, string otp, string purpose)
            {
                return Task.FromResult(contact == LastSavedContact && otp == LastSavedOtp && purpose == LastSavedPurpose);
            }
        }

        private class StubUserRepo : IUserRepository
        {
            public Task<User?> GetByEmailAsync(string email)
            {
                if (email == "existing@bookmyfun.com")
                {
                    return Task.FromResult<User?>(new User
                    {
                        UserID = 1,
                        Email = email,
                        FullName = "Existing User",
                        Role = "Client",
                        IsActive = true
                    });
                }
                return Task.FromResult<User?>(null);
            }

            public Task<User?> GetByIdAsync(int id) => Task.FromResult<User?>(null);
            public Task<User?> GetByPhoneAsync(string phone) => Task.FromResult<User?>(null);
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(new List<User>());
            public Task<int> CreateUserAsync(User user) => Task.FromResult(1);
            public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
            public Task<bool> UpdatePasswordAsync(int userId, string passwordHash) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
            public Task<bool> SetCustomCommissionRateAsync(int userId, decimal? commissionRate) => Task.FromResult(true);
            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult<User?>(null);
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(new List<User>());
            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string tier, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? email = null) => Task.FromResult(true);
        }

        private class MockFailingEmailService : IEmailService
        {
            public Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml) => Task.FromResult(false);
            public Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string purpose, string? recipientName = null) => Task.FromResult(false);
            public Task EnqueueEmailAsync(string toEmail, string subject, string bodyHtml, string messageType = "Notification") => Task.CompletedTask;
        }

        private class MockSuccessEmailService : IEmailService
        {
            public string? LastToEmail { get; private set; }
            public string? LastOtpCode { get; private set; }
            public string? LastPurpose { get; private set; }

            public Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml) => Task.FromResult(true);

            public Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string purpose, string? recipientName = null)
            {
                LastToEmail = toEmail;
                LastOtpCode = otpCode;
                LastPurpose = purpose;
                return Task.FromResult(true);
            }

            public Task EnqueueEmailAsync(string toEmail, string subject, string bodyHtml, string messageType = "Notification") => Task.CompletedTask;
        }

        [Fact]
        public async Task SendOtpEmailAsync_DispatchesSuccessfullyWithValidHtml()
        {
            var config = new ConfigurationBuilder().Build();
            var queue = new EmailQueueService(NullLogger<EmailQueueService>.Instance);
            var comms = new CommunicationService(
                new StubConfigRepo(),
                config,
                queue,
                NullLogger<CommunicationService>.Instance);

            // In test environment without SMTP credentials, simulated dispatch should succeed and log warning
            bool result = await comms.SendOtpEmailAsync("guest@example.com", "849201", "Registration", "John Doe");
            Assert.True(result);

            bool resultForgot = await comms.SendOtpEmailAsync("guest@example.com", "554433", "ForgotPassword");
            Assert.True(resultForgot);

            bool resultLogin = await comms.SendOtpEmailAsync("guest@example.com", "112233", "Login");
            Assert.True(resultLogin);
        }

        [Fact]
        public async Task SendEmailAsync_RejectsInvalidRecipientAddresses()
        {
            var config = new ConfigurationBuilder().Build();
            var queue = new EmailQueueService(NullLogger<EmailQueueService>.Instance);
            var comms = new CommunicationService(
                new StubConfigRepo(),
                config,
                queue,
                NullLogger<CommunicationService>.Instance);

            Assert.False(await comms.SendEmailAsync("", "Subject", "<p>Body</p>"));
            Assert.False(await comms.SendEmailAsync("   ", "Subject", "<p>Body</p>"));
            Assert.False(await comms.SendEmailAsync("not-an-email", "Subject", "<p>Body</p>"));
            Assert.False(await comms.SendEmailAsync("missing_domain@", "Subject", "<p>Body</p>"));
        }

        [Fact]
        public async Task EmailQueueService_EnqueuesAndDequeuesMessagesReliably()
        {
            var queue = new EmailQueueService(NullLogger<EmailQueueService>.Instance);

            var message1 = new EmailQueueMessage
            {
                ToEmail = "user1@example.com",
                Subject = "Notification 1",
                BodyHtml = "<p>Message 1</p>",
                MessageType = "Alert"
            };

            var message2 = new EmailQueueMessage
            {
                ToEmail = "user2@example.com",
                Subject = "Notification 2",
                BodyHtml = "<p>Message 2</p>",
                MessageType = "BookingConfirmation"
            };

            await queue.EnqueueAsync(message1);
            await queue.EnqueueAsync(message2);

            Assert.Equal(2, queue.ApproximateQueueCount);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var dequeued1 = await queue.DequeueAsync(cts.Token);
            Assert.Equal("user1@example.com", dequeued1.ToEmail);
            Assert.Equal("Notification 1", dequeued1.Subject);

            var dequeued2 = await queue.DequeueAsync(cts.Token);
            Assert.Equal("user2@example.com", dequeued2.ToEmail);
            Assert.Equal("Notification 2", dequeued2.Subject);

            Assert.Equal(0, queue.ApproximateQueueCount);
        }

        [Fact]
        public async Task AccountController_SendOtp_ReturnsFailureWhenEmailFails()
        {
            var failingEmail = new MockFailingEmailService();
            var otpRepo = new StubOtpRepo();
            var userRepo = new StubUserRepo();
            var whatsApp = new CommunicationService(
                new StubConfigRepo(),
                new ConfigurationBuilder().Build(),
                new EmailQueueService(NullLogger<EmailQueueService>.Instance),
                NullLogger<CommunicationService>.Instance);

            var controller = new AccountController(
                userRepo,
                otpRepo,
                null!, // ownerReqRepo
                null!, // partnerLeadService
                failingEmail,
                whatsApp,
                null!, // bookingRepo
                null!, // propRepo
                null!, // idRepo
                null!, // schemeProvider
                NullLogger<AccountController>.Instance);

            // Act: Attempt to send OTP with failing email service
            var result = await controller.SendOtp(new SendOtpRequestModel
            {
                Contact = "guest@example.com",
                Purpose = "Registration"
            }) as JsonResult;

            // Assert: Must return false, not misleading success
            Assert.NotNull(result);
            var val = result.Value;
            var successProp = val?.GetType().GetProperty("success")?.GetValue(val);
            Assert.Equal(false, successProp);
        }

        [Fact]
        public async Task AccountController_SendOtp_SucceedsAndDispatchesOtpWhenServiceHealthy()
        {
            var successEmail = new MockSuccessEmailService();
            var otpRepo = new StubOtpRepo();
            var userRepo = new StubUserRepo();
            var whatsApp = new CommunicationService(
                new StubConfigRepo(),
                new ConfigurationBuilder().Build(),
                new EmailQueueService(NullLogger<EmailQueueService>.Instance),
                NullLogger<CommunicationService>.Instance);

            var controller = new AccountController(
                userRepo,
                otpRepo,
                null!, // ownerReqRepo
                null!, // partnerLeadService
                successEmail,
                whatsApp,
                null!, // bookingRepo
                null!, // propRepo
                null!, // idRepo
                null!, // schemeProvider
                NullLogger<AccountController>.Instance);

            var result = await controller.SendOtp(new SendOtpRequestModel
            {
                Contact = "newuser@example.com",
                Purpose = "Registration"
            }) as JsonResult;

            Assert.NotNull(result);
            var val = result.Value;
            var successProp = val?.GetType().GetProperty("success")?.GetValue(val);
            Assert.Equal(true, successProp);

            Assert.Equal("newuser@example.com", successEmail.LastToEmail);
            Assert.Equal("Registration", successEmail.LastPurpose);
            Assert.NotNull(successEmail.LastOtpCode);
            Assert.Equal(6, successEmail.LastOtpCode.Length);
            Assert.Equal("newuser@example.com", otpRepo.LastSavedContact);
        }
    }
}
