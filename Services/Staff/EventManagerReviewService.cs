using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services.Checkout;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services.Staff
{
    public class EventManagerReviewService : IEventManagerReviewService
    {
        private readonly IUserRepository _userRepo;
        private readonly IEventPackageRepository _eventPackageRepo;
        private readonly IConfigRepository _configRepo;
        private readonly IIdVerificationService _idVerificationService;
        private readonly IMediaStorageService _mediaStorage;
        private readonly ILogger<EventManagerReviewService>? _logger;

        public EventManagerReviewService(
            IUserRepository userRepo,
            IEventPackageRepository eventPackageRepo,
            IConfigRepository configRepo,
            IIdVerificationService idVerificationService,
            IMediaStorageService mediaStorage,
            ILogger<EventManagerReviewService>? logger = null)
        {
            _userRepo = userRepo;
            _eventPackageRepo = eventPackageRepo;
            _configRepo = configRepo;
            _idVerificationService = idVerificationService;
            _mediaStorage = mediaStorage;
            _logger = logger;
        }

        public async Task<int> GetPendingCountAsync()
        {
            var managers = await _userRepo.GetUsersByRoleAsync("EventManager");
            return managers.Count(m => string.Equals(m.VerificationStatus, "Pending", StringComparison.OrdinalIgnoreCase) ||
                                       string.IsNullOrEmpty(m.VerificationStatus));
        }

        public async Task<EventManagerListViewModel> GetManagerListAsync(string tab = "Pending")
        {
            var allManagers = await _userRepo.GetUsersByRoleAsync("EventManager");
            var allPackages = await _eventPackageRepo.GetAllPackagesAsync();
            var allPortfolios = await _eventPackageRepo.GetAllPortfoliosAsync();

            int pendingCount = 0;
            int verifiedCount = 0;
            int rejectedCount = 0;
            int suspendedCount = 0;

            var items = new List<EventManagerListItemViewModel>();

            foreach (var m in allManagers)
            {
                int activePackages = allPackages.Count(p => p.CreatedByUserID == m.UserID && p.IsActive);
                int approvedPortfolios = allPortfolios.Count(p => p.UserID == m.UserID && p.IsApproved);

                bool isSuspended = string.Equals(m.VerificationStatus, "Suspended", StringComparison.OrdinalIgnoreCase) ||
                                   (!m.IsActive && !string.Equals(m.VerificationStatus, "Rejected", StringComparison.OrdinalIgnoreCase));
                bool isPending = string.Equals(m.VerificationStatus, "Pending", StringComparison.OrdinalIgnoreCase) ||
                                 string.IsNullOrEmpty(m.VerificationStatus);
                bool isVerified = string.Equals(m.VerificationStatus, "Verified", StringComparison.OrdinalIgnoreCase) && m.IsActive;
                bool isRejected = string.Equals(m.VerificationStatus, "Rejected", StringComparison.OrdinalIgnoreCase);

                if (isSuspended) suspendedCount++;
                else if (isPending) pendingCount++;
                else if (isVerified) verifiedCount++;
                else if (isRejected) rejectedCount++;

                items.Add(new EventManagerListItemViewModel
                {
                    UserID = m.UserID,
                    FullName = m.FullName,
                    Email = m.Email,
                    Phone = m.Phone,
                    ServiceAreas = m.ServiceAreas,
                    VerificationStatus = isSuspended ? "Suspended" : (m.VerificationStatus ?? "Pending"),
                    IsActive = m.IsActive,
                    ActivePackageCount = activePackages,
                    ApprovedPortfolioCount = approvedPortfolios,
                    SubmittedDate = m.CreatedAt,
                    ProfilePictureUrl = m.ProfilePictureUrl
                });
            }

            string targetTab = tab?.Trim() ?? "Pending";
            List<EventManagerListItemViewModel> filteredItems;

            if (string.Equals(targetTab, "Verified", StringComparison.OrdinalIgnoreCase))
            {
                filteredItems = items.Where(i => i.VerificationStatus == "Verified" && i.IsActive).ToList();
            }
            else if (string.Equals(targetTab, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                filteredItems = items.Where(i => i.VerificationStatus == "Rejected").ToList();
            }
            else if (string.Equals(targetTab, "Suspended", StringComparison.OrdinalIgnoreCase))
            {
                filteredItems = items.Where(i => i.VerificationStatus == "Suspended" || !i.IsActive).ToList();
            }
            else // Default to Pending
            {
                targetTab = "Pending";
                filteredItems = items.Where(i => (i.VerificationStatus == "Pending" || string.IsNullOrEmpty(i.VerificationStatus)) && i.IsActive).ToList();
            }

            return new EventManagerListViewModel
            {
                ActiveTab = targetTab,
                PendingCount = pendingCount,
                VerifiedCount = verifiedCount,
                RejectedCount = rejectedCount,
                SuspendedCount = suspendedCount,
                Managers = filteredItems.OrderByDescending(i => i.SubmittedDate).ToList()
            };
        }

        public async Task<EventManagerDetailViewModel?> GetManagerDetailAsync(int userId)
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || !string.Equals(user.Role, "EventManager", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var packages = await _eventPackageRepo.GetPackagesByUserIdAsync(userId);
            var portfolios = await _eventPackageRepo.GetPortfoliosByUserIdAsync(userId, onlyApproved: false);
            var verification = await _idVerificationService.GetClientVerificationAsync(userId, userId, "Staff");

            bool hasDocs = verification != null && !string.IsNullOrWhiteSpace(verification.DocumentStorageKey);
            bool hasActivePkg = packages != null && packages.Any(p => p.IsActive);
            bool hasApprovedPort = portfolios != null && portfolios.Any(p => p.IsApproved);
            bool hasServiceAreas = !string.IsNullOrWhiteSpace(user.ServiceAreas);

            var warnings = new List<string>();
            if (!hasDocs) warnings.Add("Checklist Warning: No identity or verification documents have been uploaded.");
            if (!hasActivePkg) warnings.Add("Checklist Warning: No active décor packages published.");
            if (!hasApprovedPort) warnings.Add("Checklist Warning: No approved showcase portfolio items (approval required for checkout visibility).");
            if (!hasServiceAreas) warnings.Add("Checklist Warning: No service areas configured (manager cannot match guest checkout locations).");

            return new EventManagerDetailViewModel
            {
                Manager = user,
                Packages = packages ?? new List<EventPackage>(),
                Portfolios = portfolios ?? new List<EventManagerPortfolio>(),
                VerificationDoc = verification,
                HasDocumentsUploaded = hasDocs,
                HasActivePackage = hasActivePkg,
                HasApprovedPortfolio = hasApprovedPort,
                HasServiceAreas = hasServiceAreas,
                Warnings = warnings
            };
        }

        public async Task<(bool Success, string? Error)> VerifyAsync(int userId, int performedById)
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || !string.Equals(user.Role, "EventManager", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Event manager not found.");
            }

            user.VerificationStatus = "Verified";
            user.IsActive = true;
            await _userRepo.UpdateUserAsync(user);

            await _configRepo.LogAuditAsync(performedById, "ApproveEventManagerVerification", "User", userId,
                $"Approved event manager #{userId} ({user.FullName}) verification status to Verified.");

            StayCelebrationEligibilityService.InvalidateAllCache();
            return (true, null);
        }

        public async Task<(bool Success, string? Error)> RejectAsync(int userId, string reason, int performedById)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return (false, "A reason is required to reject an event manager.");
            }

            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || !string.Equals(user.Role, "EventManager", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Event manager not found.");
            }

            user.VerificationStatus = "Rejected";
            await _userRepo.UpdateUserAsync(user);

            await _configRepo.LogAuditAsync(performedById, "RejectEventManagerVerification", "User", userId,
                $"Rejected event manager #{userId} ({user.FullName}) verification. Reason: {reason.Trim()}");

            StayCelebrationEligibilityService.InvalidateAllCache();
            return (true, null);
        }

        public async Task<(bool Success, string? Error)> ReturnToPendingAsync(int userId, int performedById)
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || !string.Equals(user.Role, "EventManager", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Event manager not found.");
            }

            user.VerificationStatus = "Pending";
            await _userRepo.UpdateUserAsync(user);

            await _configRepo.LogAuditAsync(performedById, "ReturnToPendingEventManager", "User", userId,
                $"Returned event manager #{userId} ({user.FullName}) to Pending status.");

            StayCelebrationEligibilityService.InvalidateAllCache();
            return (true, null);
        }

        public async Task<(bool Success, string? Error)> SuspendAsync(int userId, string? reason, int performedById)
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || !string.Equals(user.Role, "EventManager", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Event manager not found.");
            }

            user.VerificationStatus = "Suspended";
            user.IsActive = false;
            await _userRepo.UpdateUserAsync(user);

            await _configRepo.LogAuditAsync(performedById, "SuspendEventManager", "User", userId,
                $"Suspended event manager #{userId} ({user.FullName}). Reason: {reason ?? "Administrative suspension"}");

            StayCelebrationEligibilityService.InvalidateAllCache();
            return (true, null);
        }

        public async Task<(bool Success, string? Error)> ReactivateAsync(int userId, int performedById)
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || !string.Equals(user.Role, "EventManager", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Event manager not found.");
            }

            user.VerificationStatus = "Verified";
            user.IsActive = true;
            await _userRepo.UpdateUserAsync(user);

            await _configRepo.LogAuditAsync(performedById, "ReactivateEventManager", "User", userId,
                $"Reactivated event manager #{userId} ({user.FullName}).");

            StayCelebrationEligibilityService.InvalidateAllCache();
            return (true, null);
        }

        public async Task<(bool Success, string NormalizedAreas, List<string> Unnormalized, string? Error)> UpdateServiceAreasAsync(
            int userId, string rawServiceAreas, int performedById)
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || !string.Equals(user.Role, "EventManager", StringComparison.OrdinalIgnoreCase))
            {
                return (false, string.Empty, new List<string>(), "Event manager not found.");
            }

            var lines = (rawServiceAreas ?? string.Empty)
                .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var normalizedList = new List<string>();
            var unnormalizedList = new List<string>();

            foreach (var line in lines)
            {
                var parts = line.Split(',', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    string city = Capitalize(parts[0]);
                    string state = Capitalize(parts[1]);
                    string norm = $"{city}, {state}";
                    if (!normalizedList.Contains(norm))
                    {
                        normalizedList.Add(norm);
                    }
                }
                else
                {
                    unnormalizedList.Add(line);
                }
            }

            string combined = string.Join("; ", normalizedList);
            user.ServiceAreas = combined;
            await _userRepo.UpdateUserAsync(user);

            await _configRepo.LogAuditAsync(performedById, "EditServiceAreas", "User", userId,
                $"Updated service areas for event manager #{userId} ({user.FullName}) to: '{combined}'. Unnormalized: [{string.Join(", ", unnormalizedList)}]");

            StayCelebrationEligibilityService.InvalidateAllCache();
            return (true, combined, unnormalizedList, null);
        }

        public async Task<(bool Success, string? Error)> ApprovePortfolioAsync(int portfolioId, int performedById)
        {
            await _eventPackageRepo.ApprovePortfolioItemAsync(portfolioId, true);
            await _configRepo.LogAuditAsync(performedById, "ApprovePortfolioItem", "EventManagerPortfolio", portfolioId,
                $"Approved showcase portfolio item #{portfolioId} for public display.");

            StayCelebrationEligibilityService.InvalidateAllCache();
            return (true, null);
        }

        public async Task<(bool Success, string? Error)> RejectPortfolioAsync(int portfolioId, string reason, int performedById)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return (false, "A reason is required to reject a portfolio item.");
            }

            await _eventPackageRepo.ApprovePortfolioItemAsync(portfolioId, false);
            await _configRepo.LogAuditAsync(performedById, "RejectPortfolioItem", "EventManagerPortfolio", portfolioId,
                $"Rejected showcase portfolio item #{portfolioId}. Reason: {reason.Trim()}");

            StayCelebrationEligibilityService.InvalidateAllCache();
            return (true, null);
        }

        public async Task<(Stream? Stream, string ContentType, string? FileName)> GetDocumentStreamAsync(int userId, string docType)
        {
            var verification = await _idVerificationService.GetClientVerificationAsync(userId, userId, "Staff");
            if (verification == null || string.IsNullOrWhiteSpace(verification.DocumentStorageKey))
            {
                return (null, string.Empty, null);
            }

            string physicalPath = _mediaStorage.GetPrivateDocumentPhysicalPath(verification.DocumentStorageKey);
            if (!File.Exists(physicalPath))
            {
                return (null, string.Empty, null);
            }

            string ext = Path.GetExtension(physicalPath).ToLowerInvariant();
            string contentType = ext switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };

            var stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            string fileName = $"ManagerDoc_{userId}_{verification.DocumentType}{ext}";
            return (stream, contentType, fileName);
        }

        private static string Capitalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1).ToLowerInvariant() : "")));
        }
    }
}
