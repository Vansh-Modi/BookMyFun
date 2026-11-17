using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services.Staff
{
    public class StaffQueueService : IStaffQueueService
    {
        private readonly IUserRepository _userRepo;
        private readonly IEventPackageRepository _eventPackageRepo;
        private readonly IPropertyRepository _propRepo;
        private readonly IIdVerificationService _idVerificationService;
        private readonly IPartnerLeadService _partnerLeadService;
        private readonly IRefundEngineService _refundEngine;
        private readonly IDamageClaimService _damageClaimService;
        private readonly ILogger<StaffQueueService>? _logger;

        public StaffQueueService(
            IUserRepository userRepo,
            IEventPackageRepository eventPackageRepo,
            IPropertyRepository propRepo,
            IIdVerificationService idVerificationService,
            IPartnerLeadService partnerLeadService,
            IRefundEngineService refundEngine,
            IDamageClaimService damageClaimService,
            ILogger<StaffQueueService>? logger = null)
        {
            _userRepo = userRepo;
            _eventPackageRepo = eventPackageRepo;
            _propRepo = propRepo;
            _idVerificationService = idVerificationService;
            _partnerLeadService = partnerLeadService;
            _refundEngine = refundEngine;
            _damageClaimService = damageClaimService;
            _logger = logger;
        }

        public async Task<int> GetTotalPendingCountAsync()
        {
            var queue = await GetQueueAsync(null, 1, 1);
            return queue.TotalPendingCount;
        }

        public async Task<StaffQueueViewModel> GetQueueAsync(string? filter = null, int page = 1, int pageSize = 25)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 25);

            var allItems = new List<StaffQueueItemViewModel>();

            // 1. Event Manager Verifications Pending
            try
            {
                var managers = await _userRepo.GetUsersByRoleAsync("EventManager");
                foreach (var m in managers.Where(u => string.Equals(u.VerificationStatus, "Pending", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(u.VerificationStatus)))
                {
                    allItems.Add(new StaffQueueItemViewModel
                    {
                        TaskType = "EventManager",
                        TaskTypeLabel = "Event Manager Verification",
                        EntityID = m.UserID,
                        Title = m.FullName,
                        Subtitle = $"Areas: {(string.IsNullOrWhiteSpace(m.ServiceAreas) ? "None configured" : m.ServiceAreas)} · {m.Email}",
                        SubmittedAt = m.CreatedAt,
                        ActionUrl = $"/Admin/EventManagerDetail/{m.UserID}",
                        BadgeClass = "bg-primary"
                    });
                }
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Error querying pending event managers for staff queue."); }

            // 2. Portfolio Items Pending Approval
            try
            {
                var portfolios = await _eventPackageRepo.GetAllPortfoliosAsync();
                foreach (var p in portfolios.Where(x => !x.IsApproved))
                {
                    allItems.Add(new StaffQueueItemViewModel
                    {
                        TaskType = "Portfolio",
                        TaskTypeLabel = "Portfolio Moderation",
                        EntityID = p.PortfolioID,
                        Title = p.Title,
                        Subtitle = $"Manager #{p.UserID} · {(string.IsNullOrWhiteSpace(p.Description) ? "No caption" : p.Description)}",
                        SubmittedAt = p.CreatedAt,
                        ActionUrl = $"/Admin/EventManagerDetail/{p.UserID}",
                        BadgeClass = "bg-info text-dark"
                    });
                }
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Error querying pending portfolios for staff queue."); }

            // 3. Properties Pending Approval
            try
            {
                var properties = await _propRepo.GetAllPropertiesAsync();
                foreach (var p in properties.Where(x => !x.IsApproved && x.ListingStatus != "Rejected" && x.ListingStatus != "Deactivated"))
                {
                    allItems.Add(new StaffQueueItemViewModel
                    {
                        TaskType = "Property",
                        TaskTypeLabel = "Property Approval",
                        EntityID = p.PropertyID,
                        Title = p.Title,
                        Subtitle = $"Location: {p.Location} · Host #{p.OwnerID} · ₹{p.PricePerNight:N0}/night",
                        SubmittedAt = p.CreatedAt,
                        ActionUrl = $"/Admin/ManageProperties",
                        BadgeClass = "bg-warning text-dark"
                    });
                }
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Error querying pending properties for staff queue."); }

            // 4. Client ID Verifications Pending
            try
            {
                var idVerifications = await _idVerificationService.GetAllVerificationsAsync("PendingReview");
                foreach (var k in idVerifications)
                {
                    allItems.Add(new StaffQueueItemViewModel
                    {
                        TaskType = "ClientId",
                        TaskTypeLabel = "Guest KYC Review",
                        EntityID = k.VerificationID,
                        Title = $"Guest #{k.ClientID}: {k.DocumentType}",
                        Subtitle = $"Submitted: {k.CreatedAt:dd MMM yyyy, HH:mm} · {k.MaskedIdentifier ?? "Document attached"}",
                        SubmittedAt = k.CreatedAt,
                        ActionUrl = $"/Employee/IdVerifications",
                        BadgeClass = "bg-secondary text-white"
                    });
                }
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Error querying pending KYC for staff queue."); }

            // 5. Partner Leads in New Status
            try
            {
                var leads = await _partnerLeadService.GetAllLeadsAsync("New");
                foreach (var l in leads)
                {
                    allItems.Add(new StaffQueueItemViewModel
                    {
                        TaskType = "PartnerLead",
                        TaskTypeLabel = "Partner Lead",
                        EntityID = l.LeadID,
                        Title = $"{l.FullName} ({l.PartnerType})",
                        Subtitle = $"{l.Location} · {l.Email} · {l.Phone}",
                        SubmittedAt = l.CreatedAt,
                        ActionUrl = $"/Admin/PartnerLeads",
                        BadgeClass = "bg-success"
                    });
                }
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Error querying pending leads for staff queue."); }

            // 6. Refunds in Initiated or Processing status
            try
            {
                var refunds = await _refundEngine.GetRefundsQueueAsync();
                foreach (var r in refunds.Where(x => string.Equals(x.Status, "Pending", StringComparison.OrdinalIgnoreCase) ||
                                                    string.Equals(x.Status, "Initiated", StringComparison.OrdinalIgnoreCase) ||
                                                    string.Equals(x.Status, "Processing", StringComparison.OrdinalIgnoreCase)))
                {
                    allItems.Add(new StaffQueueItemViewModel
                    {
                        TaskType = "Refund",
                        TaskTypeLabel = "Refund Processing",
                        EntityID = r.RefundID,
                        Title = $"Refund #{r.RefundID} for Booking #{r.BookingID}",
                        Subtitle = $"Amount: ₹{r.NetRefundAmount:N2} · Status: {r.Status} · Reason: {r.RefundReason}",
                        SubmittedAt = r.CreatedAt,
                        ActionUrl = $"/Admin/RefundsQueue",
                        BadgeClass = "bg-danger"
                    });
                }
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Error querying pending refunds for staff queue."); }

            // 7. Open Damage Claims with status Raised or UnderReview
            try
            {
                var claims = await _damageClaimService.GetAllClaimsAsync();
                foreach (var c in claims.Where(x => string.Equals(x.Status, "Raised", StringComparison.OrdinalIgnoreCase) ||
                                                   string.Equals(x.Status, "UnderReview", StringComparison.OrdinalIgnoreCase)))
                {
                    allItems.Add(new StaffQueueItemViewModel
                    {
                        TaskType = "DamageClaim",
                        TaskTypeLabel = "Damage Claim",
                        EntityID = c.ClaimID,
                        Title = $"Claim #{c.ClaimID} (Booking #{c.BookingID})",
                        Subtitle = $"Claimed: ₹{c.ClaimAmount:N2} · Status: {c.Status} · {c.ItemizedDescription}",
                        SubmittedAt = c.CreatedAt,
                        ActionUrl = $"/Admin/DamageClaims",
                        BadgeClass = "bg-dark text-white"
                    });
                }
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Error querying open damage claims for staff queue."); }

            // Compute counts by type
            var countsByType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["EventManager"] = allItems.Count(x => x.TaskType == "EventManager"),
                ["Portfolio"] = allItems.Count(x => x.TaskType == "Portfolio"),
                ["Property"] = allItems.Count(x => x.TaskType == "Property"),
                ["ClientId"] = allItems.Count(x => x.TaskType == "ClientId"),
                ["PartnerLead"] = allItems.Count(x => x.TaskType == "PartnerLead"),
                ["Refund"] = allItems.Count(x => x.TaskType == "Refund"),
                ["DamageClaim"] = allItems.Count(x => x.TaskType == "DamageClaim")
            };

            int totalPending = allItems.Count;

            // Filter
            string activeFilter = string.IsNullOrWhiteSpace(filter) ? "All" : filter.Trim();
            var filteredItems = string.Equals(activeFilter, "All", StringComparison.OrdinalIgnoreCase)
                ? allItems
                : allItems.Where(x => string.Equals(x.TaskType, activeFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            // Sort: oldest first
            var sortedItems = filteredItems.OrderBy(x => x.SubmittedAt).ToList();

            // Paginate at 25 per page
            int totalFiltered = sortedItems.Count;
            int totalPages = Math.Max(1, (int)Math.Ceiling((double)totalFiltered / pageSize));
            var pagedItems = sortedItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return new StaffQueueViewModel
            {
                Items = pagedItems,
                SelectedFilter = activeFilter,
                TotalPendingCount = totalPending,
                CountsByType = countsByType,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                TotalItems = totalFiltered
            };
        }
    }
}
