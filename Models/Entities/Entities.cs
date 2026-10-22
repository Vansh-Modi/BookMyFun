namespace BookMyFun.Models.Entities
{
    public class User
    {
        public int UserID { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Role { get; set; } = "Client"; // 'Admin', 'Employee', 'Owner', 'Client', 'EventManager'
        public bool IsActive { get; set; } = true;
        public decimal? CustomCommissionRate { get; set; }
        public string SubscriptionTier { get; set; } = "Free"; // 'Free', 'Featured', 'Managed'
        public string? ServiceAreas { get; set; }
        public string AuthProvider { get; set; } = "Local"; // 'Local', 'Google', etc.
        public string? AuthProviderKey { get; set; }
        public string? ProfilePictureUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class SiteSetting
    {
        public int SettingID { get; set; }
        public string SettingKey { get; set; } = string.Empty;
        public string? SettingValue { get; set; }
        public bool IsEncrypted { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ApiConfiguration
    {
        public int ConfigID { get; set; }
        public string ServiceProvider { get; set; } = string.Empty; // 'SMTP', 'WhatsApp', 'Razorpay', 'Cashfree'
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
        public string? ExtraParams { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PropertyType
    {
        public int PropertyTypeID { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class Occasion
    {
        public int OccasionID { get; set; }
        public string OccasionName { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class SubscriptionPlan
    {
        public int PlanID { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int DurationInDays { get; set; }
        public int MaxListings { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class OwnerSubscription
    {
        public int SubscriptionID { get; set; }
        public int OwnerID { get; set; }
        public int PlanID { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string PaymentStatus { get; set; } = "Pending"; // 'Paid', 'Pending', 'Expired'
        public string? GatewayTransactionID { get; set; }

        // Navigation helpers
        public string? PlanName { get; set; }
        public string? OwnerName { get; set; }
    }

    public class Property
    {
        public int PropertyID { get; set; }
        public int OwnerID { get; set; }
        public int PropertyTypeID { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Location { get; set; } = string.Empty;
        public string? FormattedAddress { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public decimal PricePerNight { get; set; }
        public decimal SecurityDepositAmount { get; set; } = 0.00m;
        public decimal? CommissionRate { get; set; }
        public decimal? EventCommissionRate { get; set; }
        public int FreeCancellationWindowHours { get; set; } = 48;
        public int CancellationCutoffHours { get; set; } = 24;
        public string CancellationFeeType { get; set; } = "percentage"; // 'percentage', 'fixed'
        public decimal CancellationFeeValue { get; set; } = 10.00m;
        public int MaxGuests { get; set; }
        public string ListingStatus { get; set; } = "Draft"; // 'Draft', 'PendingPayment', 'Active', 'Expired'
        public bool IsApproved { get; set; }
        public bool IsFeatured { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Joined properties
        public string? PropertyTypeName { get; set; }
        public string? OwnerName { get; set; }
        public string? PrimaryImageUrl { get; set; }
        public List<int> OccasionIDs { get; set; } = new();
        public List<string> ImageUrls { get; set; } = new();
        public List<PropertyMedia> MediaList { get; set; } = new();
        public List<int> AmenityIDs { get; set; } = new();
        public List<AmenityMaster> Amenities { get; set; } = new();
    }

    public class PropertyMedia
    {
        public int MediaID { get; set; }
        public int PropertyID { get; set; }
        public string MediaType { get; set; } = "photo"; // 'photo', 'video'
        public string SourceType { get; set; } = "file"; // 'file', 'url'
        public string StorageUrl { get; set; } = string.Empty;
        public string? PublicId { get; set; }
        public bool IsPrimary { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AmenityMaster
    {
        public int AmenityID { get; set; }
        public string AmenityName { get; set; } = string.Empty;
        public string IconClass { get; set; } = "bi-check2-circle";
        public string Category { get; set; } = "General";
    }

    public class PropertyImage
    {
        public int ImageID { get; set; }
        public int PropertyID { get; set; }
        public string ImageURL { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
    }

    public class Booking
    {
        public int BookingID { get; set; }
        public int PropertyID { get; set; }
        public int ClientID { get; set; }
        public int? OccasionID { get; set; }
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal SecurityDepositAmount { get; set; } = 0.00m;
        public string SecurityDepositStatus { get; set; } = "Held"; // 'Held', 'Released', 'PartiallyClaimed', 'FullyClaimed'
        public decimal CommissionRateSnapshot { get; set; } = 10.00m;
        public decimal PlatformCommissionAmount { get; set; } = 0.00m;
        public decimal OwnerPayoutAmount { get; set; } = 0.00m;
        public int FreeCancellationWindowHoursSnapshot { get; set; } = 48;
        public int CancellationCutoffHoursSnapshot { get; set; } = 24;
        public string CancellationFeeTypeSnapshot { get; set; } = "percentage";
        public decimal CancellationFeeValueSnapshot { get; set; } = 10.00m;
        public string Status { get; set; } = "Pending"; // 'Pending', 'Confirmed', 'Cancelled', 'Completed'
        public string? IdempotencyKey { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Flexible booking addition
        public string BookingType { get; set; } = "FullDay"; // 'FullDay', 'HalfDay', 'Hourly'
        public string? SlotType { get; set; } // 'MorningSlot', 'EveningSlot', 'NightStay', 'CustomHours'
        public TimeSpan? SlotStartTime { get; set; }
        public TimeSpan? SlotEndTime { get; set; }

        // Event Decorator / 5th Dashboard Integration
        public bool IsEventBooking { get; set; }
        public int? EventPackageID { get; set; }
        public string? CustomEventNotes { get; set; }
        public DateTime? EventDate { get; set; }
        public int? DecoratorAssignedID { get; set; }
        public string? SetupStatus { get; set; } = "Pending"; // 'Pending', 'Assigned', 'InProgress', 'Completed', 'Inspected'
        public string? SetupProofMediaUrls { get; set; } // JSON array or comma separated media links
        public decimal EventPackagePriceSnapshot { get; set; } = 0.00m;
        public decimal EventCommissionRateSnapshot { get; set; } = 0.00m;
        public decimal EventPlatformCommissionAmount { get; set; } = 0.00m;
        public decimal EventManagerPayoutAmount { get; set; } = 0.00m;

        // Navigation helpers
        public string? PropertyTitle { get; set; }
        public string? ClientName { get; set; }
        public string? ClientEmail { get; set; }
        public string? ClientPhone { get; set; }
        public string? OccasionName { get; set; }
        public string? EventPackageName { get; set; }
        public string? DecoratorName { get; set; }
    }

    public class Payment
    {
        public int PaymentID { get; set; }
        public int BookingID { get; set; }
        public string? GatewayTransactionID { get; set; }
        public decimal Amount { get; set; }
        public string PaymentStatus { get; set; } = "Pending"; // 'Success', 'Failed'
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
    }

    public class CustomerReview
    {
        public int ReviewID { get; set; }
        public int BookingID { get; set; }
        public int ClientID { get; set; }
        public int PropertyID { get; set; }
        public int Rating { get; set; } // 1 - 5
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? ClientName { get; set; }
    }

    public class ContactUs
    {
        public int ContactID { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Subject { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = "New"; // 'New', 'Responded'
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Support Staff Accountability
        public int? RespondedByUserID { get; set; }
        public string? RespondedByUserName { get; set; }
        public string? RespondedByUserRole { get; set; }
        public DateTime? RespondedAt { get; set; }
        public string? ResponseNotes { get; set; }
    }

    public class AuditLog
    {
        public int AuditID { get; set; }
        public int PerformedByUserID { get; set; }
        public string Action { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public int? EntityID { get; set; }
        public string? Details { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class OwnerRequest
    {
        public int RequestID { get; set; }
        public int UserID { get; set; }
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public DateTime TrialDeadline { get; set; }
        public string Status { get; set; } = "PendingEvaluation"; // 'PendingEvaluation', 'Approved', 'Rejected'
        public string? AdminNotes { get; set; }
        public int? ReviewedByUserID { get; set; }
        public DateTime? ReviewedAt { get; set; }

        // Joined properties
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public int ListedPropertiesCount { get; set; }
    }

    public class FinancialEntry
    {
        public int LedgerID { get; set; }
        public string TransactionType { get; set; } = "Income"; // 'Income' or 'Expense'
        public string Category { get; set; } = "BookingCommission";
        public decimal Amount { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Details { get; set; }
        public DateTime TransactionDate { get; set; } = DateTime.Today;
        public int? LoggedByUserID { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class PartnerLead
    {
        public int LeadID { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string AcreageOrType { get; set; } = string.Empty;
        public string? Message { get; set; }
        public string Status { get; set; } = "New"; // 'New', 'Contacted', 'Approved', 'Rejected'
        public string? ActivationToken { get; set; }
        public DateTime? TokenExpiresAt { get; set; }
        public int? ReviewedByUserID { get; set; }
        public string PartnerType { get; set; } = "Owner"; // 'Owner' or 'EventManager'
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Joined properties
        public string? ReviewedByUserName { get; set; }
    }

    public class BookingRefund
    {
        public int RefundID { get; set; }
        public int BookingID { get; set; }
        public decimal GrossBookingAmount { get; set; }
        public decimal CancellationFeeDeducted { get; set; }
        public decimal GatewayFeeDeducted { get; set; }
        public decimal NetRefundAmount { get; set; }
        public string? RefundReason { get; set; }
        public string Status { get; set; } = "Initiated"; // 'Initiated', 'Processing', 'Completed', 'Failed', 'Overridden'
        public string? GatewayRefundID { get; set; }
        public int? ProcessedByUserID { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Joined properties
        public string? PropertyTitle { get; set; }
        public string? ClientName { get; set; }
        public string? ClientEmail { get; set; }
        public string? ProcessedByUserName { get; set; }
    }

    public class DamageClaim
    {
        public int ClaimID { get; set; }
        public int BookingID { get; set; }
        public int PropertyID { get; set; }
        public int OwnerID { get; set; }
        public decimal ClaimAmount { get; set; }
        public string ItemizedDescription { get; set; } = string.Empty;
        public string EvidenceMediaUrls { get; set; } = string.Empty; // JSON array or comma separated
        public string Status { get; set; } = "UnderReview"; // 'UnderReview', 'Approved', 'Rejected', 'Settled'
        public decimal? AdjudicatedAmount { get; set; }
        public int? AdjudicatedByUserID { get; set; }
        public string? AdjudicationNotes { get; set; }
        public DateTime? AdjudicatedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Joined properties
        public string? PropertyTitle { get; set; }
        public string? OwnerName { get; set; }
        public string? ClientName { get; set; }
        public decimal SecurityDepositAmount { get; set; }
        public string? AdjudicatedByUserName { get; set; }
    }

    public class ClientIdVerification
    {
        public int VerificationID { get; set; }
        public int ClientID { get; set; }
        public string DocumentType { get; set; } = "Aadhaar"; // 'Aadhaar', 'Passport', 'DrivingLicense'
        public string DocumentStorageKey { get; set; } = string.Empty;
        public string? MaskedIdentifier { get; set; }
        public string VerificationStatus { get; set; } = "PendingReview"; // 'PendingReview', 'Verified', 'Rejected'
        public string? RejectionReason { get; set; }
        public int? VerifiedByUserID { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Joined properties
        public string? ClientName { get; set; }
        public string? ClientEmail { get; set; }
        public string? VerifiedByUserName { get; set; }
    }

    public class EventPackage
    {
        public int PackageID { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "Birthday"; // 'Birthday', 'Anniversary', 'Wedding', 'Corporate', 'Custom'
        public decimal BasePrice { get; set; }
        public string? Description { get; set; }
        public string? MediaUrl { get; set; }
        public string? IncludedItems { get; set; } // JSON array or comma separated items
        public bool IsActive { get; set; } = true;
        public decimal? CommissionRate { get; set; }
        public int? CreatedByUserID { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Joined properties
        public string? CreatedByUserName { get; set; }
    }

    public class PropertyEventCommission
    {
        public int PropertyID { get; set; }
        public int PackageID { get; set; }
        public decimal CommissionRate { get; set; }
        public string? PackageName { get; set; }
        public decimal BasePrice { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
