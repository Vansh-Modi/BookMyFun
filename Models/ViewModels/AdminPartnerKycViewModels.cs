using BookMyFun.Models.Entities;

namespace BookMyFun.Models.ViewModels
{
    public class AdminPartnerKycItemViewModel
    {
        public int KycId { get; set; }
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Role { get; set; } = string.Empty;
        public string KycStatus { get; set; } = "Incomplete";
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AdminPartnerKycListViewModel
    {
        public string SelectedStatus { get; set; } = "All";
        public List<AdminPartnerKycItemViewModel> Items { get; set; } = new();
        public int SubmittedCount { get; set; }
        public int VerifiedCount { get; set; }
        public int RejectedCount { get; set; }
        public int TotalCount { get; set; }
    }

    public class AdminPartnerKycDetailViewModel
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Role { get; set; } = string.Empty;
        public string KycStatus { get; set; } = "Incomplete";
        public string? RejectionReason { get; set; }
        public string? ReviewedByName { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }

        public bool HasAadhaar { get; set; }
        public bool HasPan { get; set; }
        public bool HasBankProof { get; set; }

        public string? AccountHolderName { get; set; }
        public string? IfscCode { get; set; }
        public string? MaskedBankAccount { get; set; }

        public List<PartnerKycAuditLog> AuditLogs { get; set; } = new();
    }
}
