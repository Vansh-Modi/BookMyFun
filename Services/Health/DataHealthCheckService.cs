using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services.Health
{
    public class DataHealthCheckService : IDataHealthCheckService
    {
        private readonly IPropertyRepository _propRepo;
        private readonly IUserRepository _userRepo;
        private readonly IEventPackageRepository _eventPackageRepo;
        private readonly ISubscriptionRepository _subRepo;
        private readonly INotificationOutboxRepository _outboxRepo;
        private readonly IWebHostEnvironment? _env;
        private readonly ILogger<DataHealthCheckService>? _logger;

        public DataHealthCheckService(
            IPropertyRepository propRepo,
            IUserRepository userRepo,
            IEventPackageRepository eventPackageRepo,
            ISubscriptionRepository subRepo,
            INotificationOutboxRepository outboxRepo,
            IWebHostEnvironment? env = null,
            ILogger<DataHealthCheckService>? logger = null)
        {
            _propRepo = propRepo;
            _userRepo = userRepo;
            _eventPackageRepo = eventPackageRepo;
            _subRepo = subRepo;
            _outboxRepo = outboxRepo;
            _env = env;
            _logger = logger;
        }

        public async Task<DataHealthCheckViewModel> RunDiagnosticsAsync()
        {
            var findings = new List<HealthCheckFindingViewModel>();

            var allProperties = await _propRepo.GetAllPropertiesAsync();
            var allUsers = await _userRepo.GetAllUsersAsync();
            var allManagers = allUsers.Where(u => string.Equals(u.Role, "EventManager", StringComparison.OrdinalIgnoreCase)).ToList();
            var allPackages = await _eventPackageRepo.GetAllPackagesAsync();
            var allPortfolios = await _eventPackageRepo.GetAllPortfoliosAsync();

            // 1. PropertyMedia rows pointing to missing files or placeholders
            foreach (var prop in allProperties)
            {
                try
                {
                    var mediaItems = await _propRepo.GetPropertyMediaAsync(prop.PropertyID);
                    foreach (var m in mediaItems)
                    {
                        string url = m.StorageUrl ?? string.Empty;
                        bool isPlaceholder = url.Contains("test.jpg", StringComparison.OrdinalIgnoreCase) ||
                                             url.Contains("placeholder", StringComparison.OrdinalIgnoreCase);

                        bool fileMissing = false;
                        if (!isPlaceholder && url.StartsWith("/") && _env != null)
                        {
                            string rel = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                            string fullPath = Path.Combine(_env.WebRootPath, rel);
                            if (!File.Exists(fullPath))
                            {
                                fileMissing = true;
                            }
                        }

                        if (isPlaceholder || fileMissing)
                        {
                            findings.Add(new HealthCheckFindingViewModel
                            {
                                Category = "MissingMedia",
                                CategoryLabel = "Missing / Placeholder Media",
                                EntityID = prop.PropertyID,
                                Name = $"{prop.Title} (Media #{m.MediaID})",
                                Issue = isPlaceholder
                                    ? $"PropertyMedia #{m.MediaID} references a placeholder image: '{url}'"
                                    : $"PropertyMedia #{m.MediaID} file not found on disk at '{url}'",
                                FixUrl = $"/Owner/EditProperty/{prop.PropertyID}",
                                Severity = "Warning"
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error checking media for property #{PropertyId}", prop.PropertyID);
                }
            }

            // 2. Verified event managers with no active package or no approved portfolio item
            var approvedPortfolioUserIds = allPortfolios.Where(p => p.IsApproved).Select(p => p.UserID).ToHashSet();
            var activePackageUserIds = allPackages.Where(p => p.IsActive).Select(p => p.CreatedByUserID ?? 0).ToHashSet();

            foreach (var mgr in allManagers.Where(m => string.Equals(m.VerificationStatus, "Verified", StringComparison.OrdinalIgnoreCase) && m.IsActive))
            {
                bool hasActivePkg = activePackageUserIds.Contains(mgr.UserID);
                bool hasApprovedPort = approvedPortfolioUserIds.Contains(mgr.UserID);

                if (!hasActivePkg)
                {
                    findings.Add(new HealthCheckFindingViewModel
                    {
                        Category = "IncompleteEventManager",
                        CategoryLabel = "Verified Manager Without Active Package",
                        EntityID = mgr.UserID,
                        Name = mgr.FullName,
                        Issue = $"Verified event manager #{mgr.UserID} ({mgr.FullName}) has zero active décor packages published.",
                        FixUrl = $"/Admin/EventManagerDetail/{mgr.UserID}",
                        Severity = "Warning"
                    });
                }

                if (!hasApprovedPort)
                {
                    findings.Add(new HealthCheckFindingViewModel
                    {
                        Category = "IncompleteEventManager",
                        CategoryLabel = "Verified Manager Without Approved Portfolio",
                        EntityID = mgr.UserID,
                        Name = mgr.FullName,
                        Issue = $"Verified event manager #{mgr.UserID} ({mgr.FullName}) has zero approved showcase portfolio items (cannot appear in Stay & Celebrate checkout).",
                        FixUrl = $"/Admin/EventManagerDetail/{mgr.UserID}",
                        Severity = "Error"
                    });
                }
            }

            // 3. Event managers with un-normalised service areas
            foreach (var mgr in allManagers)
            {
                if (!string.IsNullOrWhiteSpace(mgr.ServiceAreas))
                {
                    var lines = mgr.ServiceAreas.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                        {
                            findings.Add(new HealthCheckFindingViewModel
                            {
                                Category = "UnnormalizedServiceArea",
                                CategoryLabel = "Un-normalised Service Area",
                                EntityID = mgr.UserID,
                                Name = mgr.FullName,
                                Issue = $"Manager #{mgr.UserID} has un-normalised service area '{line}' that does not conform to 'City, State' format.",
                                FixUrl = $"/Admin/EventManagerDetail/{mgr.UserID}",
                                Severity = "Warning"
                            });
                        }
                    }
                }
            }

            // 4. Live properties with no eligible décor in their area
            // Eligible manager rule: Verified, IsActive, has active package, has approved portfolio, serves area
            var fullyEligibleManagers = allManagers
                .Where(m => string.Equals(m.VerificationStatus, "Verified", StringComparison.OrdinalIgnoreCase) &&
                            m.IsActive &&
                            activePackageUserIds.Contains(m.UserID) &&
                            approvedPortfolioUserIds.Contains(m.UserID) &&
                            !string.IsNullOrWhiteSpace(m.ServiceAreas))
                .ToList();

            var liveProperties = allProperties.Where(p => p.IsApproved &&
                                                          !string.Equals(p.ListingStatus, "Rejected", StringComparison.OrdinalIgnoreCase) &&
                                                          !string.Equals(p.ListingStatus, "Deactivated", StringComparison.OrdinalIgnoreCase) &&
                                                          !string.Equals(p.ListingStatus, "Suspended", StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var prop in liveProperties)
            {
                string city = prop.City?.Trim() ?? string.Empty;
                string state = prop.State?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(prop.Location))
                {
                    var parts = prop.Location.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length > 0) city = parts[0].Trim();
                    if (parts.Length > 1 && string.IsNullOrWhiteSpace(state)) state = parts[1].Trim();
                }

                bool hasMatchingCity = !string.IsNullOrEmpty(city) && fullyEligibleManagers.Any(m =>
                    m.ServiceAreas != null && m.ServiceAreas.Split(';').Any(sa => sa.Trim().StartsWith(city + ",", StringComparison.OrdinalIgnoreCase)));

                bool hasMatchingState = !hasMatchingCity && !string.IsNullOrEmpty(state) && fullyEligibleManagers.Any(m =>
                    m.ServiceAreas != null && m.ServiceAreas.Split(';').Any(sa => sa.Trim().EndsWith(", " + state, StringComparison.OrdinalIgnoreCase) || sa.Trim().EndsWith("," + state, StringComparison.OrdinalIgnoreCase)));

                if (!hasMatchingCity && !hasMatchingState)
                {
                    // Check if there is a manager in this area who is incomplete (e.g., missing portfolio like Local Property 6)
                    var incompleteInArea = allManagers.FirstOrDefault(m =>
                        m.ServiceAreas != null &&
                        !string.IsNullOrEmpty(city) &&
                        m.ServiceAreas.Split(';').Any(sa => sa.Trim().StartsWith(city + ",", StringComparison.OrdinalIgnoreCase)));

                    string issue = incompleteInArea != null
                        ? $"Live property #{prop.PropertyID} ('{prop.Title}' in {prop.Location}) has no eligible Stay & Celebrate coverage: manager '{incompleteInArea.FullName}' in this area is missing an approved portfolio."
                        : $"Live property #{prop.PropertyID} ('{prop.Title}' in {prop.Location}) has no verified event manager covering this service area.";

                    findings.Add(new HealthCheckFindingViewModel
                    {
                        Category = "NoDecorCoverage",
                        CategoryLabel = "No Eligible Décor Coverage",
                        EntityID = prop.PropertyID,
                        Name = prop.Title,
                        Issue = issue,
                        FixUrl = incompleteInArea != null ? $"/Admin/EventManagerDetail/{incompleteInArea.UserID}" : "/Admin/EventManagerVerifications",
                        Severity = "Warning"
                    });
                }
            }

            // 5. Live properties whose owner's subscription has expired
            foreach (var prop in liveProperties)
            {
                try
                {
                    var activeSub = await _subRepo.GetActiveSubscriptionByOwnerIdAsync(prop.OwnerID);
                    if (activeSub == null || activeSub.EndDate < DateTime.UtcNow)
                    {
                        string expDate = activeSub != null ? activeSub.EndDate.ToString("dd MMM yyyy") : "No active subscription";
                        findings.Add(new HealthCheckFindingViewModel
                        {
                            Category = "ExpiredSubscription",
                            CategoryLabel = "Expired Owner Subscription",
                            EntityID = prop.PropertyID,
                            Name = prop.Title,
                            Issue = $"Live property #{prop.PropertyID} is published, but Owner #{prop.OwnerID}'s subscription status is: {expDate}.",
                            FixUrl = $"/Admin/ManageOwners",
                            Severity = "Error"
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error checking owner subscription for property #{PropertyId}", prop.PropertyID);
                }
            }

            // 6. Notifications in the outbox that failed after 3 retries
            try
            {
                var failedBatch = await _outboxRepo.GetPendingBatchAsync(100);
                // Note: GetPendingBatchAsync gets Pending and Retry items. For failed items, query failed:
                // If any items have RetryCount >= 3 or Status == 'Failed'
                foreach (var item in failedBatch.Where(x => x.RetryCount >= 3 || string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase)))
                {
                    findings.Add(new HealthCheckFindingViewModel
                    {
                        Category = "FailedOutbox",
                        CategoryLabel = "Failed Notification Outbox Item",
                        EntityID = (int)item.OutboxID,
                        Name = $"{item.EventType} to User #{item.UserID} ({item.Channel})",
                        Issue = $"Notification #{item.OutboxID} ({item.EventType}) failed after {item.RetryCount} retries. Error: {item.LastError ?? "Exhausted retry attempts."}",
                        FixUrl = "/Admin/Index",
                        Severity = "Error"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error checking failed outbox notifications.");
            }

            // Counts by category
            var countsByCategory = findings
                .GroupBy(f => f.Category)
                .ToDictionary(g => g.Key, g => g.Count());

            return new DataHealthCheckViewModel
            {
                Findings = findings,
                TotalIssuesCount = findings.Count,
                CheckedAt = DateTime.UtcNow,
                CountsByCategory = countsByCategory
            };
        }
    }
}
