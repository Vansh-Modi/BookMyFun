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
        // Scoped per HTTP request: caches KYC status so multiple UI checks (banner, popup, publish gate) query & log once per request.
        private readonly Dictionary<int, PartnerKycStatusDto> _requestStatusCache = new();

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
            if (_requestStatusCache.TryGetValue(userId, out var cached))
            {
                return cached;
            }

            try
            {
                var record = await _kycRepo.GetByUserIdAsync(userId);
                var dto = BuildStatusDto(record);
                _requestStatusCache[userId] = dto;
                return dto;
            }
            catch (Exception ex)
            {
                string correlationId = NewCorrelationId();
                _logger.LogError(ex, "Partner KYC status check failed for UserId={UserId}, CorrelationId={CorrelationId}: {Message}",
                    userId, correlationId, ex.Message);

                var failedDto = new PartnerKycStatusDto
                {
                    IsComplete = false,
                    CheckFailed = true,
                    CorrelationId = correlationId,
                    KycStatus = "Incomplete",
                    ShouldShowFirstLoginPopup = false,
                    MissingItems = new List<string>
                    {
                        "Aadhaar (masked copy)",
                        "PAN Card",
                        "Bank Account Holder Name",
                        "Bank Account Number",
                        "IFSC Code"
                    }
                };
                _requestStatusCache[userId] = failedDto;
                return failedDto;
            }
        }

        public async Task MarkFirstPopupShownAsync(int userId)
        {
            try
            {
                await _kycRepo.MarkFirstPopupShownAsync(userId);
                _requestStatusCache.Remove(userId);
            }
            catch (Exception ex)
            {
                string correlationId = NewCorrelationId();
                _logger.LogError(ex, "Failed to record KYC popup dismissal for UserId={UserId}, CorrelationId={CorrelationId}: {Message}",
                    userId, correlationId, ex.Message);
            }
        }

        public async Task<(bool Success, Dictionary<string, string> FieldErrors, PartnerKycStatusDto Status)> SubmitAsync(int userId, PartnerKycSubmitViewModel model)
        {
            var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            PartnerKycBankDetails existing;
            try
            {
                existing = await _kycRepo.GetByUserIdAsync(userId) ?? new PartnerKycBankDetails { UserId = userId };
            }
            catch (Exception ex)
            {
                string corrId = NewCorrelationId();
                _logger.LogError(ex, "Failed to load Partner KYC record during submit for UserId={UserId}, CorrelationId={CorrelationId}: {Message}",
                    userId, corrId, ex.Message);
                errors[string.Empty] = $"We could not verify your payment details right now. Please try again later. Reference ID: {corrId}.";
                return (false, errors, new PartnerKycStatusDto
                {
                    IsComplete = false,
                    CheckFailed = true,
                    CorrelationId = corrId,
                    KycStatus = "Incomplete",
                    ShouldShowFirstLoginPopup = false
                });
            }

            var savedFromDb = existing;
            var pendingRecord = new PartnerKycBankDetails
            {
                KycId = savedFromDb.KycId,
                UserId = userId,
                AadhaarDocumentKey = savedFromDb.AadhaarDocumentKey,
                PanDocumentKey = savedFromDb.PanDocumentKey,
                BankProofDocumentKey = savedFromDb.BankProofDocumentKey,
                AccountHolderName = savedFromDb.AccountHolderName,
                BankAccountNumberEncrypted = savedFromDb.BankAccountNumberEncrypted,
                BankAccountLast4 = savedFromDb.BankAccountLast4,
                IfscCode = savedFromDb.IfscCode,
                KycStatus = savedFromDb.KycStatus,
                FirstPopupShownAt = savedFromDb.FirstPopupShownAt,
                SubmittedAt = savedFromDb.SubmittedAt
            };

            // 1. Validate required file presence & non-file fields before writing any files or DB rows
            if ((model.AadhaarFile == null || model.AadhaarFile.Length == 0) && string.IsNullOrWhiteSpace(pendingRecord.AadhaarDocumentKey))
            {
                errors[nameof(model.AadhaarFile)] = "Please upload a masked copy of your Aadhaar card (JPG, PNG, WEBP, or PDF, max 5 MB).";
            }

            if ((model.PanFile == null || model.PanFile.Length == 0) && string.IsNullOrWhiteSpace(pendingRecord.PanDocumentKey))
            {
                errors[nameof(model.PanFile)] = "Please upload your PAN card (JPG, PNG, WEBP, or PDF, max 5 MB).";
            }

            string holderName = (model.AccountHolderName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(holderName))
            {
                errors[nameof(model.AccountHolderName)] = "Account holder name is required.";
            }
            else
            {
                pendingRecord.AccountHolderName = holderName;
            }

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
                    errors[nameof(model.BankAccountNumber)] = "Encryption key is not configured on this server. Contact the administrator.";
                }
                else
                {
                    byte[] encrypted = _encryptionService.EncryptDocument(Encoding.UTF8.GetBytes(rawAcc));
                    pendingRecord.BankAccountNumberEncrypted = Convert.ToBase64String(encrypted);
                    pendingRecord.BankAccountLast4 = rawAcc[^4..];
                }
            }
            else if (string.IsNullOrWhiteSpace(pendingRecord.BankAccountNumberEncrypted))
            {
                errors[nameof(model.BankAccountNumber)] = "Bank account number is required.";
            }

            string ifsc = (model.IfscCode ?? string.Empty).Trim().ToUpperInvariant();
            if (!IfscRegex.IsMatch(ifsc))
            {
                errors[nameof(model.IfscCode)] = "Enter a valid 11-character IFSC code (e.g., SBIN0001234).";
            }
            else
            {
                pendingRecord.IfscCode = ifsc;
            }

            // 2. Validate & encrypt uploaded files (file type -> size 5 MB -> encryption availability), tracking new keys for rollback
            var newlySavedKeys = new List<string>();
            if (model.AadhaarFile != null && model.AadhaarFile.Length > 0)
            {
                try
                {
                    string key = await _mediaStorage.SavePrivateDocumentAsync(model.AadhaarFile, userId);
                    newlySavedKeys.Add(key);
                    pendingRecord.AadhaarDocumentKey = key;
                }
                catch (ValidationException ex)
                {
                    errors[nameof(model.AadhaarFile)] = ExtractValidationMessage(ex);
                }
                catch (Exception ex)
                {
                    string corrId = NewCorrelationId();
                    _logger.LogError(ex, "Aadhaar upload failed for UserId={UserId}, CorrelationId={CorrelationId}", userId, corrId);
                    errors[nameof(model.AadhaarFile)] = $"Could not upload Aadhaar file right now. Reference ID: {corrId}.";
                }
            }

            if (model.PanFile != null && model.PanFile.Length > 0)
            {
                try
                {
                    string key = await _mediaStorage.SavePrivateDocumentAsync(model.PanFile, userId);
                    newlySavedKeys.Add(key);
                    pendingRecord.PanDocumentKey = key;
                }
                catch (ValidationException ex)
                {
                    errors[nameof(model.PanFile)] = ExtractValidationMessage(ex);
                }
                catch (Exception ex)
                {
                    string corrId = NewCorrelationId();
                    _logger.LogError(ex, "PAN upload failed for UserId={UserId}, CorrelationId={CorrelationId}", userId, corrId);
                    errors[nameof(model.PanFile)] = $"Could not upload PAN file right now. Reference ID: {corrId}.";
                }
            }

            if (model.BankProofFile != null && model.BankProofFile.Length > 0)
            {
                try
                {
                    string key = await _mediaStorage.SavePrivateDocumentAsync(model.BankProofFile, userId);
                    newlySavedKeys.Add(key);
                    pendingRecord.BankProofDocumentKey = key;
                }
                catch (ValidationException ex)
                {
                    errors[nameof(model.BankProofFile)] = ExtractValidationMessage(ex);
                }
                catch (Exception ex)
                {
                    string corrId = NewCorrelationId();
                    _logger.LogError(ex, "Bank proof upload failed for UserId={UserId}, CorrelationId={CorrelationId}", userId, corrId);
                    errors[nameof(model.BankProofFile)] = $"Could not upload bank proof file right now. Reference ID: {corrId}.";
                }
            }

            if (errors.Count > 0)
            {
                CleanupSavedFiles(newlySavedKeys);
                string corrId = NewCorrelationId();
                _logger.LogWarning("Partner KYC submit rejected by validation for UserId={UserId}, CorrelationId={CorrelationId}, FailedFields={FailedFields}",
                    userId, corrId, string.Join(",", errors.Keys.Select(k => string.IsNullOrEmpty(k) ? "Form" : k)));
                // Return unmodified savedFromDb status so failed submit never pretends to have saved partial state
                var statusDto = BuildStatusDto(savedFromDb);
                statusDto.CorrelationId = corrId;
                return (false, errors, statusDto);
            }

            pendingRecord.KycStatus = "Submitted";
            pendingRecord.SubmittedAt = DateTime.UtcNow;

            try
            {
                await _kycRepo.UpsertAsync(pendingRecord);
                _requestStatusCache.Remove(userId);
                _logger.LogInformation("Partner KYC & bank details saved for UserId={UserId}. Status={Status}", userId, pendingRecord.KycStatus);
            }
            catch (Exception ex)
            {
                CleanupSavedFiles(newlySavedKeys);
                string corrId = NewCorrelationId();
                _logger.LogError(ex, "Failed to save Partner KYC & bank details for UserId={UserId}, CorrelationId={CorrelationId}", userId, corrId);
                errors[string.Empty] = $"We could not save your KYC & bank details right now. Please try again later. Reference ID: {corrId}.";
                return (false, errors, BuildStatusDto(savedFromDb));
            }

            return (true, errors, BuildStatusDto(pendingRecord));
        }

        private void CleanupSavedFiles(IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                try
                {
                    string physicalPath = _mediaStorage.GetPrivateDocumentPhysicalPath(key);
                    if (!string.IsNullOrWhiteSpace(physicalPath) && File.Exists(physicalPath))
                    {
                        File.Delete(physicalPath);
                    }
                }
                catch
                {
                    // Best-effort cleanup of newly created temp encrypted file on failed submit
                }
            }
        }

        private static string ExtractValidationMessage(ValidationException ex)
        {
            if (ex.ValidationErrors != null)
            {
                foreach (var arr in ex.ValidationErrors.Values)
                {
                    if (arr != null && arr.Length > 0 && !string.IsNullOrWhiteSpace(arr[0]))
                    {
                        return arr[0];
                    }
                }
            }
            return ex.Message;
        }

        private static string NewCorrelationId() => Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

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
