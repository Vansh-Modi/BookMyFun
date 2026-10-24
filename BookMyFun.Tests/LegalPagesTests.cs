using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace BookMyFun.Tests
{
    public class MockUserRepo : IUserRepository
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
        public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
        public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
        public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? commissionRate) => Task.FromResult(true);
        public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
    }

    public class MockOtpRepo : IOtpRepository
    {
        private readonly Dictionary<string, string> _otps = new();

        public Task<bool> SaveOtpAsync(string contact, string otp, string purpose, int expirySeconds)
        {
            _otps[$"{contact}_{purpose}"] = otp;
            return Task.FromResult(true);
        }

        public Task<bool> ValidateOtpAsync(string contact, string otp, string purpose)
        {
            if (_otps.TryGetValue($"{contact}_{purpose}", out var stored))
            {
                return Task.FromResult(stored == otp);
            }
            return Task.FromResult(false);
        }
    }

    public class InMemoryLegalDocumentRepository : ILegalDocumentRepository
    {
        public List<LegalDocument> Documents { get; } = new();
        public List<PolicyAcceptance> Acceptances { get; } = new();

        public Task<LegalDocument?> GetCurrentDocumentAsync(string documentType)
        {
            var doc = Documents
                .Where(d => d.DocumentType.Equals(documentType, StringComparison.OrdinalIgnoreCase) && d.IsCurrent)
                .OrderByDescending(d => d.DocumentID)
                .FirstOrDefault();
            return Task.FromResult(doc);
        }

        public Task<List<LegalDocument>> GetDocumentHistoryAsync(string documentType)
        {
            var list = Documents
                .Where(d => d.DocumentType.Equals(documentType, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.CreatedAt)
                .ToList();
            return Task.FromResult(list);
        }

        public Task<LegalDocument?> GetDocumentByIdAsync(int documentId)
        {
            var doc = Documents.FirstOrDefault(d => d.DocumentID == documentId);
            return Task.FromResult(doc);
        }

        public Task<LegalDocument> CreateNewVersionAsync(
            string documentType,
            string title,
            string body,
            string version,
            DateTime effectiveDate,
            bool isDraft,
            int? createdByUserId)
        {
            // Set all existing versions of this document type to IsCurrent = false
            foreach (var existing in Documents.Where(d => d.DocumentType.Equals(documentType, StringComparison.OrdinalIgnoreCase)))
            {
                existing.IsCurrent = false;
            }

            var newDoc = new LegalDocument
            {
                DocumentID = Documents.Count + 1,
                DocumentType = documentType,
                Title = title,
                Body = body,
                Version = version,
                EffectiveDate = effectiveDate,
                IsDraft = isDraft,
                IsCurrent = true,
                CreatedByUserID = createdByUserId,
                CreatedAt = DateTime.UtcNow
            };

            Documents.Add(newDoc);
            return Task.FromResult(newDoc);
        }

        public Task RecordAcceptanceAsync(PolicyAcceptance acceptance)
        {
            acceptance.AcceptanceID = Acceptances.Count + 1;
            Acceptances.Add(acceptance);
            return Task.CompletedTask;
        }

        public Task<List<PolicyAcceptance>> GetAcceptancesByUserAsync(int userId)
        {
            var list = Acceptances.Where(a => a.UserID == userId).ToList();
            return Task.FromResult(list);
        }

        public Task<List<PolicyAcceptance>> GetAcceptancesByLeadAsync(int leadId)
        {
            var list = Acceptances.Where(a => a.LeadID == leadId).ToList();
            return Task.FromResult(list);
        }
    }

    public class LegalPagesTests
    {
        [Fact]
        public async Task VersionIncrementOnSave_IncrementsVersionAutomatically()
        {
            var repo = new InMemoryLegalDocumentRepository();
            var service = new LegalDocumentService(repo);

            // First version: initial save produces 1.0
            var v1 = await service.SaveNewVersionAsync(
                "PrivacyPolicy",
                "Privacy Policy v1",
                "<p>Initial privacy policy body</p>",
                version: null,
                effectiveDate: DateTime.UtcNow,
                isDraft: false,
                createdByUserId: 1);

            Assert.Equal("1.0", v1.Version);
            Assert.True(v1.IsCurrent);

            // Second version: saving new version auto-increments to 1.1
            var v2 = await service.SaveNewVersionAsync(
                "PrivacyPolicy",
                "Privacy Policy v2",
                "<p>Updated privacy policy body</p>",
                version: null,
                effectiveDate: DateTime.UtcNow,
                isDraft: false,
                createdByUserId: 1);

            Assert.Equal("1.1", v2.Version);
            Assert.True(v2.IsCurrent);

            // Third version: explicit version override
            var v3 = await service.SaveNewVersionAsync(
                "PrivacyPolicy",
                "Privacy Policy v2.0 Major",
                "<p>Major revision</p>",
                version: "2.0",
                effectiveDate: DateTime.UtcNow,
                isDraft: false,
                createdByUserId: 1);

            Assert.Equal("2.0", v3.Version);
            Assert.True(v3.IsCurrent);
        }

        [Fact]
        public async Task OldVersions_AreNotEditable_PreservedInHistory()
        {
            var repo = new InMemoryLegalDocumentRepository();
            var service = new LegalDocumentService(repo);

            var v1 = await service.SaveNewVersionAsync(
                "TermsOfService",
                "Terms of Service v1",
                "<p>Initial terms</p>",
                "1.0",
                DateTime.UtcNow.AddDays(-30),
                false,
                1);

            var v2 = await service.SaveNewVersionAsync(
                "TermsOfService",
                "Terms of Service v2",
                "<p>Revised terms</p>",
                "1.1",
                DateTime.UtcNow,
                false,
                1);

            // Verify v1 is now not current
            var fetchedV1 = await service.GetDocumentByIdAsync(v1.DocumentID);
            Assert.NotNull(fetchedV1);
            Assert.False(fetchedV1!.IsCurrent);
            Assert.Equal("<p>Initial terms</p>", fetchedV1.Body); // Immutable body preserved

            // Verify history has both versions in descending order
            var history = await service.GetDocumentHistoryAsync("TermsOfService");
            Assert.Equal(2, history.Count);
            Assert.Equal("1.1", history[0].Version);
            Assert.True(history[0].IsCurrent);
            Assert.Equal("1.0", history[1].Version);
            Assert.False(history[1].IsCurrent);
        }

        [Fact]
        public async Task AcceptanceRecord_WrittenWithCorrectVersionAndMetadata()
        {
            var repo = new InMemoryLegalDocumentRepository();
            var service = new LegalDocumentService(repo);

            await service.SaveNewVersionAsync(
                "PrivacyPolicy",
                "Privacy Policy",
                "<p>Valid privacy body</p>",
                "1.5",
                DateTime.UtcNow,
                false,
                1);

            await service.SaveNewVersionAsync(
                "TermsOfService",
                "Terms of Service",
                "<p>Valid terms body</p>",
                "2.2",
                DateTime.UtcNow,
                false,
                1);

            await service.SaveNewVersionAsync(
                "RefundPolicy",
                "Refund & Cancellation Policy",
                "<p>Valid refund body</p>",
                "1.0",
                DateTime.UtcNow,
                false,
                1);

            int userId = 42;
            string clientIp = "192.168.1.100";
            string checkoutUrl = "/Booking/Checkout?propertyId=5";

            // Record all standard consents
            await service.RecordAllStandardConsentsAsync(userId, null, clientIp, checkoutUrl);

            var userAcceptances = await repo.GetAcceptancesByUserAsync(userId);
            Assert.Equal(3, userAcceptances.Count);

            var privacyRec = userAcceptances.FirstOrDefault(a => a.DocumentType == "PrivacyPolicy");
            Assert.NotNull(privacyRec);
            Assert.Equal("1.5", privacyRec!.Version);
            Assert.Equal(clientIp, privacyRec.IpAddress);
            Assert.Equal(checkoutUrl, privacyRec.PageUrl);

            var termsRec = userAcceptances.FirstOrDefault(a => a.DocumentType == "TermsOfService");
            Assert.NotNull(termsRec);
            Assert.Equal("2.2", termsRec!.Version);

            var refundRec = userAcceptances.FirstOrDefault(a => a.DocumentType == "RefundPolicy");
            Assert.NotNull(refundRec);
            Assert.Equal("1.0", refundRec!.Version);
        }

        [Fact]
        public async Task EmptyDocumentBehavior_BlocksConsent_AndThrowsFriendlyException()
        {
            var repo = new InMemoryLegalDocumentRepository();
            var service = new LegalDocumentService(repo);

            // Document exists but has empty body
            await service.SaveNewVersionAsync(
                "PrivacyPolicy",
                "Empty Privacy",
                "",
                "1.0",
                DateTime.UtcNow,
                true,
                1);

            bool hasContent = await service.HasContentAsync("PrivacyPolicy");
            Assert.False(hasContent);

            bool allPublished = await service.AreAllStandardPoliciesPublishedAsync();
            Assert.False(allPublished);

            // Attempting to record consent on empty document throws exception
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await service.RecordConsentAsync(1, null, "PrivacyPolicy", "127.0.0.1", "/register");
            });

            Assert.Contains("Policy not yet published", ex.Message);
        }

        [Fact]
        public async Task MissingDocument_AreAllStandardPoliciesPublished_ReturnsFalse()
        {
            var repo = new InMemoryLegalDocumentRepository();
            var service = new LegalDocumentService(repo);

            // Only 1 of 3 policies is published
            await service.SaveNewVersionAsync(
                "TermsOfService",
                "Terms",
                "<p>Some terms text</p>",
                "1.0",
                DateTime.UtcNow,
                false,
                1);

            bool allPublished = await service.AreAllStandardPoliciesPublishedAsync();
            Assert.False(allPublished);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await service.RecordAllStandardConsentsAsync(1, null, "127.0.0.1", "/checkout");
            });

            Assert.Contains("Policy not yet published", ex.Message);
        }

        [Fact]
        public async Task SubmissionBlocked_WhenConsentCheckboxNotTicked()
        {
            var userRepo = new MockUserRepo();
            var otpRepo = new MockOtpRepo();
            await otpRepo.SaveOtpAsync("guest@example.com", "123456", "Registration", 150);

            var legalRepo = new InMemoryLegalDocumentRepository();
            var legalService = new LegalDocumentService(legalRepo);

            var controller = new AccountController(
                userRepo, otpRepo, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, legalService);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "application/x-www-form-urlencoded";
            // No PolicyConsent in Form
            httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                { "FullName", "Test Guest" }
            });
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var model = new RegisterViewModel
            {
                FullName = "Test Guest",
                Email = "guest@example.com",
                Password = "Password123!",
                OtpCode = "123456"
            };

            var result = await controller.Register(model);

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.Contains(controller.ModelState.Values, v => v.Errors.Any(e => e.ErrorMessage.Contains("agree to the Terms of Service")));
            Assert.Empty(userRepo.Users); // No user created
        }
    }
}
