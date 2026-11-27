using System.Text;
using System.Text.RegularExpressions;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services.Exceptions;
using BookMyFun.Services.Security;

namespace BookMyFun.Services
{
    public interface IPartnerKycService
    {
        Task<PartnerKycStatusDto> GetStatusAsync(int userId);
        Task<(bool Success, Dictionary<string, string> FieldErrors, PartnerKycStatusDto Status)> SubmitAsync(int userId, PartnerKycSubmitViewModel model);
        Task MarkFirstPopupShownAsync(int userId);
    }

    public class PartnerKycService : IPartnerKycService
    {
        private static readonly Regex IfscRegex = new(@"^[A-Z]{4}0[A-Z0-9]{6}$", RegexOptions.Compiled);
        private static readonly Regex AccountNumberRegex = new(@"^\d{9,18}$", RegexOptions.Compiled);

        private readonly IPartnerKycRepository _kycRepo;
        private readonly IMediaStorageService _mediaStorage;
        private readonly IPrivateDocumentEncryptionService _encryptionService;
        private readonly ILogger<PartnerKycService> _logger;

        public PartnerKycService(
            IPartnerKycRepository kycRepo,
            IMediaStorageService mediaStorage,
            IPrivateDocumentEncryptionService encryptionService,
            ILogger<PartnerKycService> logger)
        {
            _kycRepo = kycRepo;
            _mediaStorage = mediaStorage;
            _encryptionService = encryptionService;
            _logger = logger;
        }

        public async Task<PartnerKycStatusDto> GetStatusAsync(int userId)
        {
            var record = await _kycRepo.GetByUserIdAsync(userId);
            return BuildStatusDto(record);
        }

        public async Task MarkFirstPopupShownAsync(int userId)
        {
            await _kycRepo.MarkFirstPopupShownAsync(userId);
        }

        public async Task<(bool Success, Dictionary<string, string> FieldErrors, PartnerKycStatusDto Status)> SubmitAsync(int userId, PartnerKycSubmitViewModel model)
        {
            var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var existing = await _kycRepo.GetByUserIdAsync(userId) ?? new PartnerKycBankDetails { UserId = userId };

            // Validate and store Aadhaar (masked copy)
            if (model.AadhaarFile != null && model.AadhaarFile.Length > 0)
            {
                try
                {
                    existing.AadhaarDocumentKey = await _mediaStorage.SavePrivateDocumentAsync(model.AadhaarFile, userId);
                }
                catch (ValidationException ex)
                {
                    errors[nameof(model.AadhaarFile)] = ex.Message;
                }
            }
            else if (string.IsNullOrWhiteSpace(existing.AadhaarDocumentKey))
            {
                errors[nameof(model.AadhaarFile)] = "Please upload a masked copy of your Aadhaar card (JPG, PNG, WEBP, or PDF, max 5MB).";
            }

            // Validate and store PAN
            if (model.PanFile != null && model.PanFile.Length > 0)
            {
                try
                {
                    existing.PanDocumentKey = await _mediaStorage.SavePrivateDocumentAsync(model.PanFile, userId);
                }
                catch (ValidationException ex)
                {
                    errors[nameof(model.PanFile)] = ex.Message;
                }
            }
            else if (string.IsNullOrWhiteSpace(existing.PanDocumentKey))
            {
                errors[nameof(model.PanFile)] = "Please upload your PAN card (JPG, PNG, WEBP, or PDF, max 5MB).";
            }

            // Optional bank proof upload (cancelled cheque / passbook)
            if (model.BankProofFile != null && model.BankProofFile.Length > 0)
            {
                try
                {
                    existing.BankProofDocumentKey = await _mediaStorage.SavePrivateDocumentAsync(model.BankProofFile, userId);
                }
                catch (ValidationException ex)
                {
                    errors[nameof(model.BankProofFile)] = ex.Message;
                }
            }

            // Account Holder Name
            string holderName = (model.AccountHolderName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(holderName))
            {
                errors[nameof(model.AccountHolderName)] = "Account holder name is required.";
            }
            else
            {
                existing.AccountHolderName = holderName;
            }

            // Bank Account Number (encrypt with AES-256-GCM; store only last 4 digits in plaintext; never log)
            string rawAcc = (model.BankAccountNumber ?? string.Empty).Replace(" ", "").Trim();
            if (!string.IsNullOrEmpty(rawAcc))
            {
                if (!AccountNumberRegex.IsMatch(rawAcc))
                {
                    errors[nameof(model.BankAccountNumber)] = "Bank account number must be 9 to 18 digits.";
                }
                else if (!_encryptionService.IsEncryptionConfigured())
                {
                    _logger.LogError("Partner bank account encryption refused for UserId={UserId}: ID_ENCRYPTION_KEY is missing or invalid.", userId);
                    errors[nameof(model.BankAccountNumber)] = "Bank account encryption is currently unavailable on this server.";
                }
                else
                {
                    byte[] encrypted = _encryptionService.EncryptDocument(Encoding.UTF8.GetBytes(rawAcc));
                    existing.BankAccountNumberEncrypted = Convert.ToBase64String(encrypted);
                    existing.BankAccountLast4 = rawAcc[^4..];
                }
            }
            else if (string.IsNullOrWhiteSpace(existing.BankAccountNumberEncrypted))
            {
                errors[nameof(model.BankAccountNumber)] = "Bank account number is required.";
            }

            // IFSC Code
            string ifsc = (model.IfscCode ?? string.Empty).Trim().ToUpperInvariant();
            if (!IfscRegex.IsMatch(ifsc))
            {
                errors[nameof(model.IfscCode)] = "Enter a valid 11-character IFSC code (e.g., SBIN0001234).";
            }
            else
            {
                existing.IfscCode = ifsc;
            }

            if (errors.Count > 0)
            {
                return (false, errors, BuildStatusDto(existing));
            }

            var statusAfter = BuildStatusDto(existing);
            existing.KycStatus = statusAfter.IsComplete ? "Submitted" : "Incomplete";
            if (statusAfter.IsComplete && !existing.SubmittedAt.HasValue)
            {
                existing.SubmittedAt = DateTime.UtcNow;
            }

            await _kycRepo.UpsertAsync(existing);
            _logger.LogInformation("Partner KYC & bank details updated for UserId={UserId}. Status={Status}", userId, existing.KycStatus);

            return (true, errors, BuildStatusDto(existing));
        }

        public static PartnerKycStatusDto BuildStatusDto(PartnerKycBankDetails? record)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(record?.AadhaarDocumentKey))
                missing.Add("Aadhaar (masked copy)");
            if (string.IsNullOrWhiteSpace(record?.PanDocumentKey))
                missing.Add("PAN Card");
            if (string.IsNullOrWhiteSpace(record?.AccountHolderName))
                missing.Add("Bank Account Holder Name");
            if (string.IsNullOrWhiteSpace(record?.BankAccountNumberEncrypted))
                missing.Add("Bank Account Number");
            if (string.IsNullOrWhiteSpace(record?.IfscCode))
                missing.Add("IFSC Code");

            bool isComplete = missing.Count == 0;
            string? maskedAcc = !string.IsNullOrWhiteSpace(record?.BankAccountLast4)
                ? $"••••••••{record.BankAccountLast4}"
                : null;

            return new PartnerKycStatusDto
            {
                IsComplete = isComplete,
                KycStatus = isComplete ? (string.IsNullOrWhiteSpace(record?.KycStatus) ? "Submitted" : record.KycStatus) : "Incomplete",
                ShouldShowFirstLoginPopup = !isComplete && record?.FirstPopupShownAt == null,
                MissingItems = missing,
                MaskedBankAccount = maskedAcc,
                HasAadhaar = !string.IsNullOrWhiteSpace(record?.AadhaarDocumentKey),
                HasPan = !string.IsNullOrWhiteSpace(record?.PanDocumentKey),
                HasBankProof = !string.IsNullOrWhiteSpace(record?.BankProofDocumentKey),
                AccountHolderName = record?.AccountHolderName,
                IfscCode = record?.IfscCode,
                SubmittedAt = record?.SubmittedAt
            };
        }
    }
}
