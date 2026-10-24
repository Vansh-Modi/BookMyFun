using System.ComponentModel.DataAnnotations;
using BookMyFun.Models.Entities;

namespace BookMyFun.Models.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Please enter your email address")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address")]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your password"), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; } = true;

        public string? ReturnUrl { get; set; }
    }

    public class ForgotPasswordViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Verification OTP is required")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP must be 6 digits")]
        public string OtpCode { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
        public string NewPassword { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Compare("NewPassword", ErrorMessage = "Passwords do not match")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class RegisterViewModel
    {
        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), MinLength(6)]
        public string Password { get; set; } = string.Empty;

        [Phone]
        public string? Phone { get; set; }

        [Required]
        public string Role { get; set; } = "Client"; // 'Owner' or 'Client'

        [Required(ErrorMessage = "OTP is required to complete verification")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP must be 6 digits")]
        public string OtpCode { get; set; } = string.Empty;
    }

    public class SendOtpRequestModel
    {
        [Required]
        public string Contact { get; set; } = string.Empty;
        public string Purpose { get; set; } = "Registration";
    }

    public class PropertyEditViewModel
    {
        public int PropertyID { get; set; }
        public int OwnerID { get; set; }

        [Required, MaxLength(150)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required, MaxLength(255)]
        public string Location { get; set; } = string.Empty;

        [Required, Range(1, 1000000)]
        public decimal PricePerNight { get; set; }

        [Range(0, 500000)]
        public decimal SecurityDepositAmount { get; set; } = 0.00m;

        [Range(0, 100)]
        public decimal? CommissionRate { get; set; }

        [Range(0, 100)]
        public decimal? EventCommissionRate { get; set; }

        public int FreeCancellationWindowHours { get; set; } = 48;
        public int CancellationCutoffHours { get; set; } = 24;
        public string CancellationFeeType { get; set; } = "percentage";
        public decimal CancellationFeeValue { get; set; } = 10.00m;

        public string? FormattedAddress { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        [MaxLength(1000)]
        [Display(Name = "Direct Map / Location URL")]
        public string? MapUrl { get; set; }
        public bool IsFeatured { get; set; }

        [Required, Range(1, 1000)]
        public int MaxGuests { get; set; }

        [Required]
        [Display(Name = "Property Category / Type")]
        public int PropertyTypeID { get; set; }

        [MaxLength(100)]
        [Display(Name = "Custom Category")]
        public string? CustomCategory { get; set; }

        public string ListingStatus { get; set; } = "Draft";
        public bool IsApproved { get; set; }

        public List<int> SelectedOccasionIDs { get; set; } = new();
        public List<int> SelectedAmenityIDs { get; set; } = new();
        public List<string> CustomAmenities { get; set; } = new();
        public List<string> CustomOccasions { get; set; } = new();
        public string? ImageUrlInput { get; set; }
        public string? AdditionalMediaJson { get; set; }

        // Lookups
        public List<PropertyType> AvailableTypes { get; set; } = new();
        public List<Occasion> AvailableOccasions { get; set; } = new();
        public List<AmenityMaster> AvailableAmenities { get; set; } = new();
        public List<PropertyMedia> ExistingMedia { get; set; } = new();
        public List<User> AvailableOwners { get; set; } = new();
        public List<PropertyEventCommission> PropertyEventCommissions { get; set; } = new();
        public List<EventPackage> AvailableEventPackages { get; set; } = new();
    }

    public class UpdatePropertyLocationUrlViewModel
    {
        public int PropertyID { get; set; }
        public string PropertyTitle { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;

        [MaxLength(1000)]
        [Display(Name = "Direct Map / Location URL")]
        public string? MapUrl { get; set; }

        public string? ReturnUrl { get; set; }
    }

    public class BookingCheckoutViewModel
    {
        public int PropertyID { get; set; }
        public string PropertyTitle { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public decimal PricePerNight { get; set; }
        public string? ImageUrl { get; set; }

        [Required]
        public string BookingType { get; set; } = "FullDay"; // 'FullDay', 'HalfDay', 'Hourly'

        public string? SlotType { get; set; } // 'MorningSlot', 'EveningSlot', 'NightStay'
        public int? HourlyDuration { get; set; } // e.g. 4 hours, 6 hours

        [Required, DataType(DataType.Date)]
        public DateTime CheckInDate { get; set; } = DateTime.Today;

        [Required, DataType(DataType.Date)]
        public DateTime CheckOutDate { get; set; } = DateTime.Today.AddDays(1);

        public TimeSpan CheckInTime { get; set; } = new TimeSpan(11, 0, 0); // Default 11:00 AM
        public TimeSpan CheckOutTime { get; set; } = new TimeSpan(10, 0, 0); // Default 10:00 AM next day

        public int? OccasionID { get; set; }
        public decimal CalculatedAmount { get; set; }

        // Event Décor Add-on Options
        public bool IsEventBooking { get; set; }
        public int? EventPackageID { get; set; }
        public string? CustomEventNotes { get; set; }
        public bool IsEventServiceAvailable { get; set; }

        // Lookups
        public List<Occasion> AvailableOccasions { get; set; } = new();
        public List<EventPackage> AvailableEventPackages { get; set; } = new();

        // KYC Identity Proof at Booking Time
        public bool IsClientAlreadyVerified { get; set; }
        public string? DocumentType { get; set; } = "Aadhaar"; // 'Aadhaar', 'Passport', 'DrivingLicense', 'VoterID'
        public string? IdDocumentNumber { get; set; }
        public IFormFile? IdDocumentFile { get; set; }
    }

    public class PaymentPageViewModel
    {
        public int BookingID { get; set; }
        public string PropertyTitle { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "INR";
        public string OrderId { get; set; } = string.Empty;
        public string KeyId { get; set; } = string.Empty;
        public bool IsSimulated { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public string ClientEmail { get; set; } = string.Empty;
        public string? ClientPhone { get; set; }
        public string BookingType { get; set; } = "FullDay";
        public string? SlotType { get; set; }
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public TimeSpan? CheckInTime { get; set; }
        public TimeSpan? CheckOutTime { get; set; }
    }

    public class SubscriptionPaymentViewModel
    {
        public int SubscriptionID { get; set; }
        public int PlanID { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public int DurationInDays { get; set; }
        public int MaxListings { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "INR";
        public string OrderId { get; set; } = string.Empty;
        public string KeyId { get; set; } = string.Empty;
        public bool IsSimulated { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public string? OwnerPhone { get; set; }
    }

    public class AdminDashboardViewModel
    {
        public int TotalProperties { get; set; }
        public int PendingApprovals { get; set; }
        public int TotalOwners { get; set; }
        public int TotalClients { get; set; }
        public int TotalEmployees { get; set; }
        public int TotalBookings { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal PlatformEarnings { get; set; }

        public List<Property> RecentProperties { get; set; } = new();
        public List<Booking> RecentBookings { get; set; } = new();
        public List<AuditLog> RecentAuditLogs { get; set; } = new();
    }

    public class OwnerDashboardViewModel
    {
        public User Owner { get; set; } = new();
        public OwnerSubscription? ActiveSubscription { get; set; }
        public int ActiveListingsCount { get; set; }
        public int MaxListingsAllowed { get; set; }
        public decimal TotalEarnings { get; set; }

        public List<Property> Properties { get; set; } = new();
        public List<Booking> Bookings { get; set; } = new();
        public List<SubscriptionPlan> AvailablePlans { get; set; } = new();
        public List<PropertyType> AvailableCategories { get; set; } = new();
    }

    public class ClientDashboardViewModel
    {
        public User Client { get; set; } = new();
        public List<Booking> Bookings { get; set; } = new();
    }

    public class PeriodFinancialStats
    {
        public string Label { get; set; } = string.Empty;
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal TotalIncome { get; set; }
        public decimal TotalExpense { get; set; }
        public decimal NetProfit => TotalIncome - TotalExpense;
        public int BookingCount { get; set; }
        public decimal BookingRevenue { get; set; }
        public decimal CommissionEarned { get; set; }
    }

    public class ReportsViewModel
    {
        // --- Overall KPIs ---
        public int TotalUsers { get; set; }
        public int TotalOwners { get; set; }
        public int TotalClients { get; set; }
        public int TotalProperties { get; set; }
        public int ActiveProperties { get; set; }
        public int TotalBookings { get; set; }
        public int ConfirmedBookings { get; set; }

        public decimal GrossBookingRevenue { get; set; }
        public decimal PlatformCommissionEarned { get; set; }
        public decimal TotalRecordedExpenses { get; set; }
        public decimal NetProfit { get; set; }

        // --- Time Filter ---
        public string SelectedPeriod { get; set; } = "6months";

        // --- Period Breakdown Data ---
        public List<PeriodFinancialStats> PeriodBreakdown { get; set; } = new();

        // --- Statistical Highlights ---
        public PeriodFinancialStats? HighestIncomePeriod { get; set; }
        public PeriodFinancialStats? LowestIncomePeriod { get; set; }
        public PeriodFinancialStats? HighestExpensePeriod { get; set; }
        public PeriodFinancialStats? LowestExpensePeriod { get; set; }
        public PeriodFinancialStats? HighestProfitPeriod { get; set; }
        public PeriodFinancialStats? LowestProfitPeriod { get; set; }
        public PeriodFinancialStats? HighestBookingPeriod { get; set; }
        public decimal AvgIncome { get; set; }
        public decimal AvgExpense { get; set; }
        public decimal AvgNetProfit { get; set; }
        public decimal AvgBookings { get; set; }

        // --- Ledger ---
        public List<FinancialEntry> RecentLedgerEntries { get; set; } = new();

        // --- Chart Data (period-aware) ---
        public List<string> Months { get; set; } = new();
        public List<decimal> MonthlyIncomes { get; set; } = new();
        public List<decimal> MonthlyExpenses { get; set; } = new();
        public List<int> MonthlyBookings { get; set; } = new();
    }

    public class PartnerLeadCreateViewModel
    {
        public string PartnerType { get; set; } = "Owner"; // "Owner" or "EventManager"

        [Required(ErrorMessage = "Full Name is required"), MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone number is required"), MinLength(8, ErrorMessage = "Please enter a valid phone number")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email address is required"), EmailAddress(ErrorMessage = "Please enter a valid email address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Property or Studio name is required"), MaxLength(150)]
        public string PropertyName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Location or Service Region is required"), MaxLength(255)]
        public string Location { get; set; } = string.Empty;

        [Required(ErrorMessage = "Acreage or Specialization is required"), MaxLength(100)]
        public string AcreageOrType { get; set; } = string.Empty;

        public string? Message { get; set; }
    }

    public class PartnerLeadReviewViewModel
    {
        public int LeadID { get; set; }
        public string Action { get; set; } = "Approve"; // 'Approve', 'Reject'
    }

    public class ActivateOwnerViewModel
    {
        [Required]
        public string Token { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
        public string Password { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Compare("Password", ErrorMessage = "Passwords do not match")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class CancellationQuoteViewModel
    {
        public int BookingID { get; set; }
        public string PropertyTitle { get; set; } = string.Empty;
        public DateTime CheckInDate { get; set; }
        public decimal TotalAmount { get; set; }
        public double HoursUntilCheckIn { get; set; }
        public int FreeCancellationWindowHours { get; set; }
        public int CancellationCutoffHours { get; set; }
        public string CancellationFeeType { get; set; } = "percentage";
        public decimal CancellationFeeValue { get; set; }
        public decimal GatewayFeeDeducted { get; set; }
        public decimal CancellationFeeDeducted { get; set; }
        public decimal NetRefundAmount { get; set; }
        public bool IsEligibleForRefund { get; set; }
        public string PolicyTier { get; set; } = string.Empty; // 'FullRefund', 'PartialRefund', 'NonRefundable'
        public string PolicyExplanation { get; set; } = string.Empty;
        public int RuleApplied { get; set; } = 1; // Scenario 1 to 6
        public decimal CustomerRefundAmount { get; set; }
        public decimal OwnerShareAmount { get; set; }
        public decimal CommissionAmount { get; set; }
        public decimal AmountPaid { get; set; }
        public string RefundTimeline { get; set; } = "5 to 7 business days";
    }

    public class RefundAdminReviewViewModel
    {
        [Required]
        public int BookingID { get; set; }

        [Required, Range(5, 6, ErrorMessage = "Rule scenario must be 5 (Listing Mismatch) or 6 (Force Majeure)")]
        public int RuleScenario { get; set; } // 5 or 6

        [Required]
        public string Action { get; set; } = "Approve"; // 'Approve', 'Reject'

        [Range(0, 1000000, ErrorMessage = "Refund amount must be zero or positive")]
        public decimal RefundAmount { get; set; }

        [Required(ErrorMessage = "A specific reason is required for administrative refund adjudications"), MinLength(5)]
        public string Reason { get; set; } = string.Empty;

        public bool IssueAsCredit { get; set; } = false; // For row 6 credit option
    }

    public class ProcessCancellationRequest
    {
        [Required]
        public int BookingID { get; set; }
        public string? Reason { get; set; }
        public bool ForceOverride { get; set; } = false; // Staff only
    }

    public class DamageClaimCreateViewModel
    {
        [Required]
        public int BookingID { get; set; }

        public int PropertyID { get; set; }

        [Required, Range(1, 1000000, ErrorMessage = "Claim amount must be greater than zero")]
        public decimal ClaimAmount { get; set; }

        [Required, MinLength(10, ErrorMessage = "Please provide an itemized description of damages")]
        public string ItemizedDescription { get; set; } = string.Empty;

        public string? EvidenceUrlsInput { get; set; }
        public List<IFormFile>? EvidenceFiles { get; set; }
        public List<string> EvidenceUrls { get; set; } = new();
    }

    public class DamageClaimAdjudicateViewModel
    {
        [Required]
        public int ClaimID { get; set; }

        [Required]
        public string Action { get; set; } = "Approve"; // 'Approve', 'PartiallyApprove', 'Reject'

        public decimal? ApprovedAmount { get; set; }

        public string? AdjudicationNotes { get; set; }
    }

    public class ClientIdVerificationUploadViewModel
    {
        [Required]
        public string DocumentType { get; set; } = "Aadhaar"; // 'Aadhaar', 'Passport', 'DrivingLicense'

        public string? MaskedIdentifier { get; set; }

        public string? DocumentUrl { get; set; } // External verified URL or direct uploaded path
    }

    public class ClientIdVerificationReviewViewModel
    {
        [Required]
        public int VerificationID { get; set; }

        [Required]
        public string Action { get; set; } = "Verify"; // 'Verify', 'Reject'

        public string? RejectionReason { get; set; }
    }

    public class MediaUploadResponse
    {
        public bool Success { get; set; }
        public string? MediaUrl { get; set; }
        public string? PublicId { get; set; }
        public string? MediaType { get; set; }
        public string? SourceType { get; set; }
        public string? Message { get; set; }
    }

    public class AdminAnalyticsViewModel
    {
        public decimal GMV { get; set; }
        public decimal PropertyGMV { get; set; }
        public decimal EventGMV { get; set; }
        public decimal NetPlatformCommission { get; set; }
        public decimal PendingPayouts { get; set; }
        public int ActiveProperties { get; set; }
        public int TotalEventBookings { get; set; }
        public double OccupancyRate { get; set; }

        public string DateRangePreset { get; set; } = "7D"; // Today, 7D, MTD, YTD, Custom
        public DateTime? CustomStartDate { get; set; }
        public DateTime? CustomEndDate { get; set; }

        public int? FilterPropertyID { get; set; }
        public int? FilterOwnerID { get; set; }
        public string? FilterStatus { get; set; }

        public List<PeriodFinancialStats> TrendData { get; set; } = new();
    }

    public class EventPackageCreateViewModel
    {
        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Category { get; set; } = "Birthday"; // 'Birthday', 'Anniversary', 'Wedding', 'Corporate', 'Custom'

        [Required, Range(1, 1000000)]
        public decimal BasePrice { get; set; }

        public string? Description { get; set; }

        [Display(Name = "Cover Image / Media URL")]
        public string? MediaUrl { get; set; }

        [Display(Name = "Upload Cover Image File")]
        public IFormFile? ImageFile { get; set; }

        public string? IncludedItems { get; set; }

        [Range(0, 100)]
        public decimal? CommissionRate { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class EventPackageEditViewModel : EventPackageCreateViewModel
    {
        [Required]
        public int PackageID { get; set; }
    }

    public class DecoratorSetupAssignmentViewModel
    {
        [Required]
        public int BookingID { get; set; }

        [Required]
        public int DecoratorAssignedID { get; set; }
    }

    public class SetupStatusUpdateViewModel
    {
        [Required]
        public int BookingID { get; set; }

        [Required]
        public string SetupStatus { get; set; } = "InProgress"; // 'Assigned', 'InProgress', 'Completed', 'Inspected'

        [Display(Name = "Photo / Video Proof URLs")]
        public string? SetupProofMediaUrls { get; set; } // comma separated or JSON array of image/video URLs

        [Display(Name = "Upload Proof Photos")]
        public List<IFormFile>? ProofFiles { get; set; }

        public string? Notes { get; set; }
    }

    public class UserProfileViewModel
    {
        public int UserID { get; set; }

        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Phone]
        public string? Phone { get; set; }

        public string Role { get; set; } = "Client";
        public string? SubscriptionTier { get; set; }
        public decimal? CustomCommissionRate { get; set; }
        public DateTime CreatedAt { get; set; }

        // Associated metrics
        public int TotalBookingsCount { get; set; }
        public int TotalPropertiesCount { get; set; }
        public string? KycStatus { get; set; }
        public string? KycRejectionReason { get; set; }
        public string? KycDocumentType { get; set; }
        public DateTime? KycVerifiedAt { get; set; }
    }

    public class ChangePasswordViewModel
    {
        [Required, DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, MinLength(6), DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required, Compare("NewPassword", ErrorMessage = "New password and confirmation password do not match."), DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class EventManagerFinancialsViewModel
    {
        public List<Booking> EventBookings { get; set; } = new();
        public decimal TotalGrossRevenue { get; set; }
        public decimal TotalPlatformCommission { get; set; }
        public decimal TotalNetEarnings { get; set; }
        public decimal TotalSettledEarnings { get; set; }
        public decimal TotalPendingEscrowEarnings { get; set; }
        public int TotalOrdersCount { get; set; }
        public int CompletedOrdersCount { get; set; }
        public int ActiveJobsCount { get; set; }
        public string? SelectedStatus { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class EventManagerServiceAreasViewModel
    {
        public List<string> AvailableLocations { get; set; } = new();
        public List<string> SelectedAreas { get; set; } = new();
        public string? RawServiceAreas { get; set; }
    }

    public class UpdateEventCommissionRequest
    {
        public int PackageId { get; set; }
        public decimal? CommissionRate { get; set; }
    }

    public class UpdateBookingEventCommissionRequest
    {
        public int BookingId { get; set; }
        public decimal CommissionRate { get; set; }
    }

    public class UpdateEventManagerCommissionRequest
    {
        public int UserId { get; set; }
        public decimal? CommissionRate { get; set; }
    }

    public class UpdateGlobalEventCommissionRequest
    {
        public decimal CommissionRate { get; set; }
    }

    public class EventCommissionsManagementViewModel
    {
        public decimal GlobalStandingEventCommission { get; set; } = 10.0m;
        public decimal GlobalHostCommission { get; set; } = 10.0m;
        public List<User> EventManagers { get; set; } = new();
        public List<EventPackage> EventPackages { get; set; } = new();
    }

    public class CreateEmployeeViewModel
    {
        [Required(ErrorMessage = "Full Name is required.")]
        [StringLength(100, ErrorMessage = "Full Name cannot exceed 100 characters.")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email Address is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address format.")]
        [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters.")]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Invalid phone number format.")]
        [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters.")]
        [Display(Name = "Phone Number")]
        public string? Phone { get; set; }

        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
        [Display(Name = "Initial Password (Optional)")]
        public string? Password { get; set; }

        [Display(Name = "Active Account")]
        public bool IsActive { get; set; } = true;
    }

    public class EditEmployeeViewModel
    {
        public int UserID { get; set; }

        [Required(ErrorMessage = "Full Name is required.")]
        [StringLength(100, ErrorMessage = "Full Name cannot exceed 100 characters.")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email Address is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address format.")]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Invalid phone number format.")]
        [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters.")]
        [Display(Name = "Phone Number")]
        public string? Phone { get; set; }

        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "New password must be at least 6 characters.")]
        [Display(Name = "Reset Password (Optional)")]
        public string? NewPassword { get; set; }

        [Display(Name = "Active Account")]
        public bool IsActive { get; set; } = true;
    }

    public class ExternalLoginPromptViewModel
    {
        public string Provider { get; set; } = "Google";
        public string? ReturnUrl { get; set; }

        [Required(ErrorMessage = "Google Email is required.")]
        [EmailAddress(ErrorMessage = "Please provide a valid Google email.")]
        [Display(Name = "Google Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Full Name is required.")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        public string? ActiveProjectEmail { get; set; } = "showsgarage@gmail.com";
        public string? ConfiguredClientId { get; set; }
        public bool IsConfiguredInDatabase { get; set; }
    }

    public class BookingReceiptViewModel
    {
        public Booking Booking { get; set; } = new();
        public Property? Property { get; set; }
        public User? Client { get; set; }
        public User? Host { get; set; }
        public Payment? PrimaryPayment { get; set; }
        public List<Payment> PaymentHistory { get; set; } = new();

        public string ReceiptNumber => $"BMF-REC-{Booking.BookingID:D6}";
        public string HostReceiptNumber => $"BMF-HST-{Booking.BookingID:D6}";
        public string TaxInvoiceNumber => $"INV-{Booking.CreatedAt:yyyyMM}-{Booking.BookingID:D5}";
        public DateTime IssuedAt => Booking.CreatedAt;

        public decimal PropertyStayGross => Math.Max(0, Booking.TotalAmount - Booking.EventPackagePriceSnapshot);
        public decimal CommissionRate => Booking.CommissionRateSnapshot > 0 ? Booking.CommissionRateSnapshot : 10.0m;
        public decimal PlatformCommission => (Booking.PlatformCommissionAmount > 0 && Booking.PlatformCommissionAmount <= PropertyStayGross)
            ? Booking.PlatformCommissionAmount
            : Math.Round(PropertyStayGross * (CommissionRate / 100m), 2);
        public decimal HostNetPayout => (Booking.OwnerPayoutAmount > 0 && Booking.OwnerPayoutAmount <= PropertyStayGross)
            ? Booking.OwnerPayoutAmount
            : Math.Max(0, PropertyStayGross - PlatformCommission);

        public decimal EventPackagePrice => Booking.EventPackagePriceSnapshot;
        public decimal SecurityDeposit => Booking.SecurityDepositAmount;
        public decimal TotalPaid => Booking.TotalAmount;

        public bool IsOwnerView { get; set; } = false;
        public bool IsAdminOrStaffView { get; set; } = false;
    }
}
