using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace BookMyFun.Models.ViewModels
{
    public class PartnerKycStatusDto
    {
        public bool IsComplete { get; set; }
        public bool IsVerified { get; set; }
        public bool CanPublish => !CheckFailed && IsVerified;
        public bool CheckFailed { get; set; }
        public string? CorrelationId { get; set; }
        public string KycStatus { get; set; } = "Incomplete";
        public string? RejectionReason { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public bool ShouldShowFirstLoginPopup { get; set; }
        public List<string> MissingItems { get; set; } = new();
        public string? MaskedBankAccount { get; set; }
        public bool HasAadhaar { get; set; }
        public bool HasPan { get; set; }
        public bool HasBankProof { get; set; }
        public string? AccountHolderName { get; set; }
        public string? IfscCode { get; set; }
        public DateTime? SubmittedAt { get; set; }
    }

    public class PartnerKycSubmitViewModel
    {
        public IFormFile? AadhaarFile { get; set; }
        public IFormFile? PanFile { get; set; }
        public IFormFile? BankProofFile { get; set; }

        [Required(ErrorMessage = "Account holder name is required.")]
        [StringLength(200, ErrorMessage = "Account holder name cannot exceed 200 characters.")]
        public string AccountHolderName { get; set; } = string.Empty;

        [StringLength(24, ErrorMessage = "Account number is too long.")]
        public string? BankAccountNumber { get; set; }

        [Required(ErrorMessage = "IFSC code is required.")]
        [RegularExpression(@"^[A-Za-z]{4}0[A-Za-z0-9]{6}$", ErrorMessage = "Enter a valid 11-character IFSC code (e.g., SBIN0001234).")]
        public string IfscCode { get; set; } = string.Empty;

        // Display state (populated on GET / re-render)
        public bool HasAadhaar { get; set; }
        public bool HasPan { get; set; }
        public bool HasBankProof { get; set; }
        public string? MaskedBankAccount { get; set; }
        public string KycStatus { get; set; } = "Incomplete";
        public string? RejectionReason { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public bool CheckFailed { get; set; }
        public string? CorrelationId { get; set; }
        public List<string> MissingItems { get; set; } = new();
        public DateTime? SubmittedAt { get; set; }
    }
}
