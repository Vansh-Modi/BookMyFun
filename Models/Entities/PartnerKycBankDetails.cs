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
        public DateTime? FirstPopupShownAt { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
