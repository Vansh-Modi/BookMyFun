using System.Text;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Exceptions;
using BookMyFun.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class PartnerKycServiceTests
    {
        private class MockPartnerKycRepo : IPartnerKycRepository
        {
            public Dictionary<int, PartnerKycBankDetails> Store = new();

            public Task<PartnerKycBankDetails?> GetByUserIdAsync(int userId)
            {
                Store.TryGetValue(userId, out var rec);
                return Task.FromResult(rec);
            }

            public Task<bool> UpsertAsync(PartnerKycBankDetails record)
            {
                Store[record.UserId] = record;
                return Task.FromResult(true);
            }

            public Task<bool> MarkFirstPopupShownAsync(int userId)
            {
                if (!Store.TryGetValue(userId, out var rec))
                {
                    rec = new PartnerKycBankDetails { UserId = userId, KycStatus = "Incomplete" };
                    Store[userId] = rec;
                }
                rec.FirstPopupShownAt ??= DateTime.UtcNow;
                return Task.FromResult(true);
            }
        }

        private class MockMediaStorageService : IMediaStorageService
        {
            public bool RejectNext { get; set; }
            public Task<(string publicUrl, string publicId)> SaveMediaFileAsync(IFormFile file, string mediaType) => Task.FromResult(("", ""));
            public Task<string> SavePrivateDocumentAsync(IFormFile file, int clientId)
            {
                if (RejectNext) throw new ValidationException("file", "Invalid file type or size.");
                return Task.FromResult($"doc_client_{clientId}_{Guid.NewGuid():N}.png");
            }
            public Task<bool> ValidateExternalUrlAsync(string url) => Task.FromResult(true);
            public string GenerateExpiringDocumentToken(string documentKey, int requestingUserId, string userRole, int expirationMinutes = 15) => "tok";
            public bool ValidateExpiringDocumentToken(string documentKey, string token, int requestingUserId, string userRole) => true;
            public string GetPrivateDocumentPhysicalPath(string documentKey) => documentKey;
        }

        private class MockEncryptionService : IPrivateDocumentEncryptionService
        {
            public bool IsEncryptionConfigured() => true;
            public byte[] EncryptDocument(byte[] plaintextBytes) => plaintextBytes.Reverse().ToArray();
            public byte[] DecryptDocument(byte[] fileBytes) => fileBytes.Reverse().ToArray();
            public bool IsEncrypted(byte[] fileBytes) => true;
        }

        private static IFormFile CreateFakeFormFile(string name, string fileName, byte[] content)
        {
            var ms = new MemoryStream(content);
            return new FormFile(ms, 0, content.Length, name, fileName);
        }

        [Fact]
        public async Task IncompleteKyc_ShowsFirstLoginPopup_AndListsMissingItems()
        {
            var repo = new MockPartnerKycRepo();
            var svc = new PartnerKycService(repo, new MockMediaStorageService(), new MockEncryptionService(), NullLogger<PartnerKycService>.Instance);

            var status = await svc.GetStatusAsync(42);
            Assert.False(status.IsComplete);
            Assert.True(status.ShouldShowFirstLoginPopup);
            Assert.Equal("Incomplete", status.KycStatus);
            Assert.Equal(5, status.MissingItems.Count);

            await svc.MarkFirstPopupShownAsync(42);
            var afterDismiss = await svc.GetStatusAsync(42);
            Assert.False(afterDismiss.ShouldShowFirstLoginPopup);
            Assert.False(afterDismiss.IsComplete);
        }

        [Fact]
        public async Task SubmitValidKycAndBank_EncryptsAccountNumber_MasksLast4_AndMarksSubmitted()
        {
            var repo = new MockPartnerKycRepo();
            var svc = new PartnerKycService(repo, new MockMediaStorageService(), new MockEncryptionService(), NullLogger<PartnerKycService>.Instance);

            var model = new PartnerKycSubmitViewModel
            {
                AadhaarFile = CreateFakeFormFile("AadhaarFile", "aadhaar.png", new byte[] { 1, 2, 3, 4 }),
                PanFile = CreateFakeFormFile("PanFile", "pan.png", new byte[] { 5, 6, 7, 8 }),
                AccountHolderName = "Ramesh Sharma",
                BankAccountNumber = "123456789012",
                IfscCode = "sbin0001234"
            };

            var (success, errors, status) = await svc.SubmitAsync(77, model);

            Assert.True(success);
            Assert.Empty(errors);
            Assert.True(status.IsComplete);
            Assert.Equal("Submitted", status.KycStatus);
            Assert.Equal("••••••••9012", status.MaskedBankAccount);
            Assert.Equal("SBIN0001234", status.IfscCode);

            var saved = await repo.GetByUserIdAsync(77);
            Assert.NotNull(saved);
            Assert.Equal("9012", saved!.BankAccountLast4);
            Assert.NotEqual("123456789012", saved.BankAccountNumberEncrypted);
        }

        [Fact]
        public async Task SubmitInvalidIfscAndAccountNumber_ReturnsFieldErrors()
        {
            var repo = new MockPartnerKycRepo();
            var svc = new PartnerKycService(repo, new MockMediaStorageService(), new MockEncryptionService(), NullLogger<PartnerKycService>.Instance);

            var model = new PartnerKycSubmitViewModel
            {
                AadhaarFile = CreateFakeFormFile("AadhaarFile", "aadhaar.png", new byte[] { 1, 2, 3 }),
                PanFile = CreateFakeFormFile("PanFile", "pan.png", new byte[] { 4, 5, 6 }),
                AccountHolderName = "Ramesh",
                BankAccountNumber = "123",
                IfscCode = "BADIFSC"
            };

            var (success, errors, _) = await svc.SubmitAsync(88, model);

            Assert.False(success);
            Assert.True(errors.ContainsKey(nameof(model.BankAccountNumber)));
            Assert.True(errors.ContainsKey(nameof(model.IfscCode)));
        }
    }
}
