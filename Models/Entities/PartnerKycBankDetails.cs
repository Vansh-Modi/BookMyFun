namespace BookMyFun.Models.Entities
{
    public class PartnerKycBankDetails
    {
        public int KycId { get; set; }
        public int UserId { get; set; }
        public string? AadhaarDocumentKey { get; set; }
        public string? PanDocumentKey { get; set; }
        public string? BankProofDocumentKey { get; set; }
        public string? AccountHolderName { get; set; }
        public string? BankAccountNumberEncrypted { get; set; }
        public string? BankAccountLast4 { get; set; }
        public string? IfscCode { get; set; }
        public string KycStatus { get; set; } = "Incomplete";
        public string? RejectionReason { get; set; }
        public int? ReviewedByUserId { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime? FirstPopupShownAt { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class PartnerKycAuditLog
    {
        public int AuditId { get; set; }
        public int AdminUserId { get; set; }
        public string? AdminName { get; set; }
        public int PartnerUserId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? DocumentType { get; set; }
        public string? Reason { get; set; }
        public string? ClientIp { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
