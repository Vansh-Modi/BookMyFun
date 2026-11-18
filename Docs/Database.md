# BookMyFun Database Architecture and Data Dictionary

## 1. Overview
The database runs on Microsoft SQL Server. The application interacts with SQL Server strictly through raw parameterized ADO.NET via [`DBHelper.cs`](file:///d:/Projects/BookMyFun/Data/DBHelper.cs). There is no Entity Framework Core ORM layer.

Schema tables and baseline seeds are initialized idempotently on startup through [`DatabaseInitializer.cs`](file:///d:/Projects/BookMyFun/Data/DatabaseInitializer.cs) and [`DatabaseSeeder.cs`](file:///d:/Projects/BookMyFun/Data/DatabaseSeeder.cs).

---

## 2. /db Migration Scripts Execution Order

> [!WARNING]
> **Backup Required Before Execution**: Always take a full backup of the production database before executing any database migration script. Run scripts during a maintenance window or off-peak hours.

Execute the scripts located in the `/db` directory in the following exact order:

1. **`db/schema.sql`**
   - **Purpose**: Creates core baseline schema (tables, foreign keys, and baseline seed data) for all platform entities if they do not already exist.
   - **Safe to run**: Idempotent checks (`IF NOT EXISTS`) wrap table and constraint creations.
2. **`db/performance_indexes.sql`**
   - **Purpose**: Adds composite performance indexes for property search, booking range lookups, client dashboard filtering, and audit log lookups.
   - **Safe to run**: Uses `IF NOT EXISTS (SELECT * FROM sys.indexes ...)` guards.
3. **`db/update_booking_status_constraint.sql`**
   - **Purpose**: Updates the check constraint `CK_Bookings_Status` on `Bookings.Status` to support the `PaymentFailed` status alongside `Pending`, `Confirmed`, `Cancelled`, `Completed`, and `Refunded`.
   - **Safe to run**: Drops constraint only if present and re-adds with new values.
4. **`db/add_user_notifications.sql`**
   - **Purpose**: Creates the `UserNotifications` table and composite index `IX_UserNotifications_UserID_IsRead` for in-app bell alerts.
   - **Safe to run**: Uses `IF NOT EXISTS` table and index checks.
5. **`db/add_is_featured_to_properties.sql`**
   - **Purpose**: Adds the `IsFeatured` (`BIT NOT NULL DEFAULT 0`) column to the `Properties` table.
   - **Safe to run**: Checks `COL_LENGTH('dbo.Properties', 'IsFeatured') IS NULL`.
6. **`db/listing_subscription_rules.sql`**
   - **Purpose**: Adds listing lifecycle tracking columns to `Properties` (`CreatedByUserID`, `ApprovedAt`, `ApprovedByUserID`, `PublishedAt`, `UnpublishReason`, `MinStayNights`, `MaxStayNights`, `ListingStatus`, `AddressLine1`, `AddressLine2`, `PostalCode`, `Neighborhood`, `TrialEndsAt`, `SubscriptionExpiresAt`), creates `SubscriptionPlans`, `SubscriptionPlanPrices`, and `OwnerSubscriptions` tables, and seeds standard subscription plans (Free Trial, Starter, Growth, Premium).
   - **Safe to run**: Fully idempotent column, table, and seed checks.
7. **`db/link_missing_property_occasions.sql`**
   - **Purpose**: Links default occasion tags (`PropertyOccasions`) to legacy properties that lacked occasion associations.
   - **Safe to run**: Idempotent insert using `WHERE NOT EXISTS`.

---

## 3. Data Dictionary

The database consists of 36 base application tables (plus `UserNotifications` and system diagram table).

### 3.1 Users & Authentication

#### `Users`
Stores accounts for all 5 roles: `Admin`, `Employee`, `Owner`, `Client`, and `EventManager`.
- **Primary Key**: `UserID` (INT IDENTITY)
- **Key Columns**:
  - `FullName` (NVARCHAR(100), NOT NULL): Full name.
  - `Email` (NVARCHAR(100), NOT NULL): Normalized unique email.
  - `PasswordHash` (NVARCHAR(255), NOT NULL): PBKDF2/BCrypt salted hash.
  - `Phone` (NVARCHAR(20), NULL): Contact phone number.
  - `Role` (NVARCHAR(20), NOT NULL): One of `Admin`, `Employee`, `Owner`, `Client`, `EventManager`.
  - `IsActive` (BIT, NOT NULL, DEFAULT 1): Account enabled status.
  - `CustomCommissionRate` (DECIMAL(5,2), NULL): Commission override for hosts.
  - `SubscriptionTier` (NVARCHAR(30), NOT NULL, DEFAULT 'Free'): Legacy tier marker.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Creation timestamp.
  - `VerificationStatus` (NVARCHAR(30), NULL, DEFAULT 'Pending'): Partner verification state (`Pending`, `Verified`, `Rejected`, `Suspended`).
  - `VerifiedAt` (DATETIME2, NULL): Timestamp when partner was verified.
  - `VerifiedByUserID` (INT, NULL): Staff member who approved partner.
  - `RejectionReason` (NVARCHAR(500), NULL): Notes if partner was rejected.
  - `Bio` (NVARCHAR(1000), NULL): Partner description (sanitized for contact privacy).
  - `BusinessName` (NVARCHAR(150), NULL): Event manager studio or host brand name.
  - `City` (NVARCHAR(100), NULL): Operating city.
  - `State` (NVARCHAR(100), NULL): Operating state.
  - `ProfilePictureUrl` (NVARCHAR(500), NULL, DEFAULT '/images/default-avatar.svg'): Profile avatar.
  - `CancellationPolicy` (NVARCHAR(1000), NULL): Partner-specific cancellation terms.
  - `Rating` (DECIMAL(3,2), NULL, DEFAULT 5.00): Current aggregate review rating.
  - `EventsCompleted` (INT, NULL, DEFAULT 0): Number of executed events.
  - `NotificationWhatsAppEnabled` (BIT, NOT NULL, DEFAULT 1): WhatsApp opt-in flag.
  - `WhatsAppVerified` (BIT, NOT NULL, DEFAULT 0): Mobile phone WhatsApp validation status.
  - `WhatsAppPhone` (NVARCHAR(20), NULL): WhatsApp contact number.

#### `OtpVerifications`
Handles OTP generation and verification for registration, password reset, and mobile validation.
- **Primary Key**: `OtpID` (INT IDENTITY)
- **Key Columns**:
  - `Contact` (NVARCHAR(100), NOT NULL): Destination email or phone number.
  - `OtpCode` (NVARCHAR(10), NOT NULL): 6-digit numeric OTP code.
  - `Purpose` (NVARCHAR(50), NOT NULL): Purpose tag (`Registration`, `PasswordReset`, `PhoneVerification`).
  - `ExpiresAt` (DATETIME2, NOT NULL): Expiration datetime (typically 10 minutes).
  - `IsUsed` (BIT, NOT NULL, DEFAULT 0): Whether OTP was redeemed.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Timestamp generated.

#### `ClientIdVerifications`
Stores government ID uploads from guests for verification before check-in.
- **Primary Key**: `VerificationID` (INT IDENTITY)
- **Key Columns**:
  - `ClientID` (INT, NOT NULL, FK -> `Users.UserID`): Guest user account.
  - `DocumentType` (NVARCHAR(50), NOT NULL): `Aadhaar`, `Passport`, or `DrivingLicense`.
  - `DocumentStorageKey` (NVARCHAR(500), NOT NULL): Secure private storage relative file path.
  - `MaskedIdentifier` (NVARCHAR(20), NULL): Last digits of document (e.g. `XXXX-XXXX-1234`).
  - `VerificationStatus` (NVARCHAR(30), NOT NULL, DEFAULT 'PendingReview'): `PendingReview`, `Verified`, `Rejected`.
  - `RejectionReason` (NVARCHAR(255), NULL): Staff explanation if rejected.
  - `VerifiedByUserID` (INT, NULL, FK -> `Users.UserID`): Staff ID who verified document.
  - `VerifiedAt` (DATETIME2, NULL): Timestamp verified.
  - `UploadedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Upload timestamp.

---

### 3.2 Properties & Catalog

#### `Properties`
Core property listings (Villas, Farmhouses, Resorts).
- **Primary Key**: `PropertyID` (INT IDENTITY)
- **Key Columns**:
  - `OwnerID` (INT, NOT NULL, FK -> `Users.UserID`): Property host.
  - `PropertyTypeID` (INT, NOT NULL, FK -> `PropertyTypes.PropertyTypeID`): Property category.
  - `Title` (NVARCHAR(150), NOT NULL): Listing name.
  - `Description` (NVARCHAR(MAX), NOT NULL): Detailed description.
  - `Address` (NVARCHAR(255), NOT NULL): Street address.
  - `City` (NVARCHAR(100), NOT NULL): City.
  - `State` (NVARCHAR(100), NOT NULL): State.
  - `PricePerDay` (DECIMAL(18,2), NOT NULL): Full day daily booking rate.
  - `PricePerHour` (DECIMAL(18,2), NULL): Hourly booking rate.
  - `PricePerHalfDay` (DECIMAL(18,2), NULL): Half day booking rate.
  - `Capacity` (INT, NOT NULL): Maximum guest capacity.
  - `Bedrooms` (INT, NOT NULL, DEFAULT 1): Number of bedrooms.
  - `Bathrooms` (INT, NOT NULL, DEFAULT 1): Number of bathrooms.
  - `SecurityDeposit` (DECIMAL(18,2), NOT NULL, DEFAULT 0.00): Refundable damage deposit.
  - `IsApproved` (BIT, NOT NULL, DEFAULT 0): Staff moderation flag.
  - `IsActive` (BIT, NOT NULL, DEFAULT 1): Active/inactive state flag.
  - `ListingStatus` (NVARCHAR(30), NOT NULL, DEFAULT 'UnderReview'): `UnderReview`, `Approved`, `Live`, `Unpublished`, `Rejected`.
  - `CancellationPolicy` (NVARCHAR(50), NOT NULL, DEFAULT 'Flexible'): `Flexible`, `Moderate`, `Strict`.
  - `CheckInTime` (TIME, NULL, DEFAULT '12:00:00'): Standard check-in time.
  - `CheckOutTime` (TIME, NULL, DEFAULT '11:00:00'): Standard checkout time.
  - `MinStayNights` (INT, NOT NULL, DEFAULT 1): Minimum required booking duration.
  - `MaxStayNights` (INT, NOT NULL, DEFAULT 30): Maximum allowed booking duration.
  - `IsFeatured` (BIT, NOT NULL, DEFAULT 0): Home page featured badge.
  - `TrialEndsAt` (DATETIME2, NULL): 60-day host trial expiration timestamp.
  - `SubscriptionExpiresAt` (DATETIME2, NULL): Active paid subscription expiration timestamp.
  - `CreatedByUserID` (INT, NULL, FK -> `Users.UserID`): Creator ID.
  - `ApprovedByUserID` (INT, NULL, FK -> `Users.UserID`): Staff member who approved property.
  - `ApprovedAt` (DATETIME2, NULL): Approval timestamp.
  - `PublishedAt` (DATETIME2, NULL): Timestamp published live to search.
  - `UnpublishReason` (NVARCHAR(200), NULL): Reason if listing unpublished.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Creation timestamp.

#### `PropertyTypes`
Lookup table for property classifications.
- **Primary Key**: `PropertyTypeID` (INT IDENTITY)
- **Key Columns**:
  - `TypeName` (NVARCHAR(50), NOT NULL): Classification name (`Farmhouse`, `Villa`, `Resort`).
  - `Description` (NVARCHAR(255), NULL): Category summary.

#### `Occasions`
Lookup table for event and gathering types.
- **Primary Key**: `OccasionID` (INT IDENTITY)
- **Key Columns**:
  - `OccasionName` (NVARCHAR(50), NOT NULL): Name of occasion (`Birthday Party`, `Anniversary`, `Wedding / Reception`, `Corporate / Offsite`, `Custom Gathering`).
  - `Description` (NVARCHAR(255), NULL): Description.

#### `PropertyOccasions`
Many-to-many relationship linking properties to suitable occasions.
- **Primary Key**: Composite (`PropertyID`, `OccasionID`)
- **Key Columns**:
  - `PropertyID` (INT, NOT NULL, FK -> `Properties.PropertyID`)
  - `OccasionID` (INT, NOT NULL, FK -> `Occasions.OccasionID`)

#### `AmenitiesMaster`
Master catalog of available amenities.
- **Primary Key**: `AmenityID` (INT IDENTITY)
- **Key Columns**:
  - `AmenityName` (NVARCHAR(50), NOT NULL): Amenity name (e.g. `Swimming Pool`, `Wi-Fi`, `Power Backup`).
  - `IconClass` (NVARCHAR(50), NOT NULL): Bootstrap icon CSS class.
  - `Category` (NVARCHAR(50), NULL, DEFAULT 'General'): Grouping category.

#### `PropertyAmenitiesMapping`
Many-to-many relationship mapping properties to enabled amenities.
- **Primary Key**: Composite (`PropertyID`, `AmenityID`)
- **Key Columns**:
  - `PropertyID` (INT, NOT NULL, FK -> `Properties.PropertyID`)
  - `AmenityID` (INT, NOT NULL, FK -> `AmenitiesMaster.AmenityID`)

#### `PropertyMedia`
Stores optimized images and documents associated with listings.
- **Primary Key**: `MediaID` (INT IDENTITY)
- **Key Columns**:
  - `PropertyID` (INT, NOT NULL, FK -> `Properties.PropertyID`)
  - `MediaType` (NVARCHAR(20), NOT NULL, DEFAULT 'Photo'): `Photo`, `FloorPlan`, `Video`.
  - `StoragePath` (NVARCHAR(500), NOT NULL): Relative storage path.
  - `DisplayOrder` (INT, NOT NULL, DEFAULT 0): Visual sorting position.
  - `IsCoverPhoto` (BIT, NOT NULL, DEFAULT 0): Indicates primary search thumbnail.
  - `UploadedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Upload timestamp.

#### `PropertyImages`
Legacy image mapping table maintained for backwards compatibility with earlier migrations.
- **Primary Key**: `ImageID` (INT IDENTITY)
- **Key Columns**:
  - `PropertyID` (INT, NOT NULL, FK -> `Properties.PropertyID`)
  - `ImageUrl` (NVARCHAR(500), NOT NULL): Image asset URL.
  - `IsPrimary` (BIT, NOT NULL, DEFAULT 0): Primary image marker.

---

### 3.3 Event Managers & Décor

#### `EventPackages`
Décor packages created by verified event managers.
- **Primary Key**: `PackageID` (INT IDENTITY)
- **Key Columns**:
  - `EventManagerID` (INT, NOT NULL, FK -> `Users.UserID`): Package designer.
  - `OccasionID` (INT, NOT NULL, FK -> `Occasions.OccasionID`): Associated occasion category.
  - `PackageName` (NVARCHAR(100), NOT NULL): Package title.
  - `Description` (NVARCHAR(MAX), NOT NULL): Description (masked for privacy).
  - `Price` (DECIMAL(18,2), NOT NULL): Flat fee for décor.
  - `IncludedItems` (NVARCHAR(MAX), NULL): Package component items list.
  - `ImageUrl` (NVARCHAR(500), NULL): Showcase image path.
  - `IsActive` (BIT, NOT NULL, DEFAULT 1): Active for booking status.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Creation timestamp.

#### `PropertyEventCommissions`
Optional host-decorator commission agreements.
- **Primary Key**: Composite (`PropertyID`, `PackageID`)
- **Key Columns**:
  - `PropertyID` (INT, NOT NULL, FK -> `Properties.PropertyID`)
  - `PackageID` (INT, NOT NULL, FK -> `EventPackages.PackageID`)
  - `CommissionRate` (DECIMAL(5,2), NOT NULL, DEFAULT 10.00): Commission percentage.
  - `UpdatedAt` (DATETIME2, NULL): Timestamp updated.

#### `EventManagerPortfolios`
Portfolio showcase photos uploaded by event managers.
- **Primary Key**: `PortfolioID` (INT IDENTITY)
- **Key Columns**:
  - `UserID` (INT, NOT NULL, FK -> `Users.UserID`): Event manager owner.
  - `Title` (NVARCHAR(150), NOT NULL): Event setup title.
  - `Description` (NVARCHAR(500), NULL): Summary description.
  - `ImageUrl` (NVARCHAR(500), NOT NULL): File path in `/uploads/portfolio/`.
  - `IsApproved` (BIT, NOT NULL, DEFAULT 0): Staff moderation status.
  - `ApprovedByUserID` (INT, NULL, FK -> `Users.UserID`): Staff reviewer ID.
  - `ApprovedAt` (DATETIME2, NULL): Approval timestamp.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Creation timestamp.

#### `FlaggedContactAttempts`
Audit log of intercepted privacy violations where contact details were masked.
- **Primary Key**: `FlagID` (INT IDENTITY)
- **Key Columns**:
  - `UserID` (INT, NOT NULL, FK -> `Users.UserID`): User submitting text.
  - `ContentType` (NVARCHAR(50), NOT NULL): `Bio`, `CancellationPolicy`, `PackageDescription`.
  - `DetectedPatterns` (NVARCHAR(MAX), NOT NULL): Matching patterns detected (e.g. `Phone: 9876543210`).
  - `OriginalContentSnippet` (NVARCHAR(500), NOT NULL): Raw snippet excerpt.
  - `SanitizedContent` (NVARCHAR(MAX), NOT NULL): Cleaned output with `[Contact Details Masked]`.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Detection timestamp.

---

### 3.4 Subscriptions & Plans

#### `SubscriptionPlans`
Host listing tier catalog (`Free Trial`, `Starter`, `Growth`, `Premium`).
- **Primary Key**: `PlanID` (INT IDENTITY)
- **Key Columns**:
  - `PlanName` (NVARCHAR(100), NOT NULL): Plan name.
  - `Price` (DECIMAL(18,2), NOT NULL): Default price.
  - `DurationMonths` (INT, NOT NULL): Validity duration in months.
  - `MaxListings` (INT, NOT NULL): Permitted active listings limit.
  - `IsActive` (BIT, NOT NULL, DEFAULT 1): Active flag.

#### `SubscriptionPlanPrices`
Configurable pricing matrix per plan and billing cadence.
- **Primary Key**: `PriceID` (INT IDENTITY)
- **Key Columns**:
  - `PlanID` (INT, NOT NULL, FK -> `SubscriptionPlans.PlanID`): Target plan.
  - `BillingCycle` (NVARCHAR(30), NOT NULL): `Monthly`, `Quarterly`, `Yearly`.
  - `Price` (DECIMAL(18,2), NOT NULL): Price amount.
  - `DurationMonths` (INT, NOT NULL): Month multiplier.
  - `IsActive` (BIT, NOT NULL, DEFAULT 1): Active status.

#### `OwnerSubscriptions`
Records of active, scheduled, and expired host subscriptions.
- **Primary Key**: `SubscriptionID` (INT IDENTITY)
- **Key Columns**:
  - `OwnerID` (INT, NOT NULL, FK -> `Users.UserID`): Host account ID.
  - `PlanID` (INT, NOT NULL, FK -> `SubscriptionPlans.PlanID`): Purchased plan.
  - `StartDate` (DATETIME2, NOT NULL): Subscription activation date.
  - `EndDate` (DATETIME2, NOT NULL): Subscription expiration date.
  - `PaymentStatus` (NVARCHAR(30), NOT NULL, DEFAULT 'Pending'): `Trial`, `Active`, `Scheduled`, `Expired`, `Cancelled`, `Pending`.
  - `GatewayTransactionID` (NVARCHAR(100), NULL): Gateway payment transaction identifier.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Creation timestamp.

#### `PropertyListingPayments`
Historical audit logs of listing slot activation fees.
- **Primary Key**: `ListingPaymentID` (INT IDENTITY)
- **Key Columns**:
  - `PropertyID` (INT, NOT NULL, FK -> `Properties.PropertyID`)
  - `OwnerID` (INT, NOT NULL, FK -> `Users.UserID`)
  - `Amount` (DECIMAL(18,2), NOT NULL)
  - `GatewayTransactionID` (NVARCHAR(100), NULL)
  - `PaymentStatus` (NVARCHAR(30), NOT NULL, DEFAULT 'Success')
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE())

---

### 3.5 Bookings & Finance

#### `Bookings`
Primary transactional record linking guest, property, optional décor package, and stay dates.
- **Primary Key**: `BookingID` (INT IDENTITY)
- **Key Columns**:
  - `PropertyID` (INT, NOT NULL, FK -> `Properties.PropertyID`)
  - `ClientID` (INT, NOT NULL, FK -> `Users.UserID`): Guest user.
  - `OccasionID` (INT, NULL, FK -> `Occasions.OccasionID`): Chosen occasion.
  - `EventPackageID` (INT, NULL, FK -> `EventPackages.PackageID`): Selected décor package.
  - `DecoratorUserID` (INT, NULL, FK -> `Users.UserID`): Assigned decorator user ID.
  - `BookingType` (NVARCHAR(20), NOT NULL, DEFAULT 'FullDay'): `FullDay`, `HalfDay`, `Hourly`.
  - `HourlyDuration` (INT, NULL): Number of hours if `BookingType == 'Hourly'`.
  - `CheckInDate` (DATE, NOT NULL): Check-in calendar date.
  - `CheckOutDate` (DATE, NOT NULL): Check-out calendar date.
  - `CheckInTime` (TIME, NULL, DEFAULT '12:00:00'): Agreed check-in time.
  - `CheckOutTime` (TIME, NULL, DEFAULT '11:00:00'): Agreed checkout time.
  - `BaseStayPrice` (DECIMAL(18,2), NOT NULL, DEFAULT 0.00): Property rental fee before décor.
  - `EventPackagePrice` (DECIMAL(18,2), NOT NULL, DEFAULT 0.00): Flat décor fee.
  - `SecurityDeposit` (DECIMAL(18,2), NOT NULL, DEFAULT 0.00): Refundable deposit snapshot.
  - `PlatformFee` (DECIMAL(18,2), NOT NULL, DEFAULT 0.00): Client platform charge.
  - `TaxesAmount` (DECIMAL(18,2), NOT NULL, DEFAULT 0.00): Applicable GST / tax.
  - `TotalAmount` (DECIMAL(18,2), NOT NULL): Final payable total.
  - `Status` (NVARCHAR(20), NOT NULL, DEFAULT 'Pending'): `Pending`, `Confirmed`, `PaymentFailed`, `Cancelled`, `Completed`, `Refunded`.
  - `CancellationReason` (NVARCHAR(500), NULL): Reason provided upon cancellation.
  - `CancelledAt` (DATETIME2, NULL): Timestamp cancelled.
  - `CancelledByUserID` (INT, NULL, FK -> `Users.UserID`): User who triggered cancellation.
  - `HostEarnings` (DECIMAL(18,2), NULL, DEFAULT 0.00): Calculated host payout.
  - `DecoratorEarnings` (DECIMAL(18,2), NULL, DEFAULT 0.00): Calculated decorator payout.
  - `PlatformCommission` (DECIMAL(18,2), NULL, DEFAULT 0.00): Platform margin.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Booking creation timestamp.

#### `Payments`
Payment transactions processed through Razorpay.
- **Primary Key**: `PaymentID` (INT IDENTITY)
- **Key Columns**:
  - `BookingID` (INT, NOT NULL, FK -> `Bookings.BookingID`)
  - `Amount` (DECIMAL(18,2), NOT NULL): Transaction amount.
  - `PaymentStatus` (NVARCHAR(20), NOT NULL, DEFAULT 'Pending'): `Pending`, `Success`, `Failed`.
  - `PaymentMethod` (NVARCHAR(50), NOT NULL, DEFAULT 'Razorpay'): Payment instrument.
  - `GatewayTransactionID` (NVARCHAR(100), NULL): Razorpay payment ID (e.g. `RAZORPAY_pay_xxx`).
  - `PaymentDate` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Transaction timestamp.

#### `BookingRefunds`
Refund ledger entries for cancellations.
- **Primary Key**: `RefundID` (INT IDENTITY)
- **Key Columns**:
  - `BookingID` (INT, NOT NULL, FK -> `Bookings.BookingID`)
  - `GrossBookingAmount` (DECIMAL(18,2), NOT NULL): Total amount originally paid.
  - `CancellationFeeDeducted` (DECIMAL(18,2), NOT NULL): Policy fee retained.
  - `GatewayFeeDeducted` (DECIMAL(18,2), NOT NULL): Non-refundable gateway fee.
  - `NetRefundAmount` (DECIMAL(18,2), NOT NULL): Amount refunded to guest.
  - `RefundReason` (NVARCHAR(500), NULL): Cancellation rationale.
  - `Status` (NVARCHAR(30), NULL, DEFAULT 'Initiated'): `Initiated`, `Processed`, `Failed`.
  - `GatewayRefundID` (NVARCHAR(100), NULL): Razorpay refund transaction reference.
  - `ProcessedByUserID` (INT, NULL, FK -> `Users.UserID`): Staff who processed refund.
  - `ProcessedAt` (DATETIME2, NULL): Timestamp processed.
  - `RuleApplied` (INT, NULL): Policy rule scenario number (1 through 6).
  - `OwnerShareAmount` (DECIMAL(18,2), NULL, DEFAULT 0.00): Payout to owner from cancellation fee.
  - `CommissionAmount` (DECIMAL(18,2), NULL, DEFAULT 0.00): Platform fee from cancellation.
  - `ShortfallAmount` (DECIMAL(18,2), NULL, DEFAULT 0.00): Platform shortfall absorbed.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Timestamp initiated.

#### `FinancialLedger`
Internal double-entry journal for payouts, refunds, and earnings.
- **Primary Key**: `LedgerID` (INT IDENTITY)
- **Key Columns**:
  - `BookingID` (INT, NOT NULL, FK -> `Bookings.BookingID`)
  - `UserID` (INT, NOT NULL, FK -> `Users.UserID`): Beneficiary.
  - `EntryType` (NVARCHAR(30), NOT NULL): `Payout`, `Commission`, `Refund`, `DepositReturn`.
  - `Amount` (DECIMAL(18,2), NOT NULL): Transaction value.
  - `Description` (NVARCHAR(255), NULL): Context description.
  - `Status` (NVARCHAR(20), NOT NULL, DEFAULT 'Completed'): `Pending`, `Completed`, `Failed`.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Entry timestamp.

#### `DamageClaims`
Tracks host security deposit claims for property damage.
- **Primary Key**: `ClaimID` (INT IDENTITY)
- **Key Columns**:
  - `BookingID` (INT, NOT NULL, FK -> `Bookings.BookingID`)
  - `ClaimAmount` (DECIMAL(18,2), NOT NULL): Claimed damage cost.
  - `Description` (NVARCHAR(MAX), NOT NULL): Detailed incident explanation.
  - `EvidenceMediaUrls` (NVARCHAR(MAX), NULL): Comma-delimited photo URLs.
  - `Status` (NVARCHAR(30), NOT NULL, DEFAULT 'PendingReview'): `PendingReview`, `Approved`, `Rejected`, `Settled`.
  - `AdjudicatedByUserID` (INT, NULL, FK -> `Users.UserID`): Staff member adjudicating.
  - `AdjudicationNotes` (NVARCHAR(500), NULL): Staff comments.
  - `SettledAmount` (DECIMAL(18,2), NULL, DEFAULT 0.00): Approved payout.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Filing timestamp.

#### `CustomerReviews`
Client guest reviews for completed bookings.
- **Primary Key**: `ReviewID` (INT IDENTITY)
- **Key Columns**:
  - `BookingID` (INT, NOT NULL, FK -> `Bookings.BookingID`)
  - `ClientID` (INT, NOT NULL, FK -> `Users.UserID`)
  - `Rating` (INT, NOT NULL): Score from 1 to 5.
  - `Comment` (NVARCHAR(1000), NULL): Guest feedback text.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Creation timestamp.

---

### 3.6 Partner Leads & Inquiries

#### `PartnerLeads`
Prospective host and event manager onboarding leads.
- **Primary Key**: `LeadID` (INT IDENTITY)
- **Key Columns**:
  - `FullName` (NVARCHAR(100), NOT NULL): Applicant contact name.
  - `Email` (NVARCHAR(100), NOT NULL): Contact email address.
  - `Phone` (NVARCHAR(20), NOT NULL): Contact phone number.
  - `PartnerType` (NVARCHAR(50), NOT NULL): `Owner` or `EventManager`.
  - `PropertyName` (NVARCHAR(150), NULL): Farmhouse name or studio name.
  - `City` (NVARCHAR(100), NULL): Operating city.
  - `State` (NVARCHAR(100), NULL): Operating state.
  - `Status` (NVARCHAR(30), NOT NULL, DEFAULT 'Pending'): `Pending`, `Approved`, `Rejected`.
  - `CreatedAccountID` (INT, NULL, FK -> `Users.UserID`): Provisioned account ID upon approval.
  - `ApprovedByUserID` (INT, NULL, FK -> `Users.UserID`): Staff member approving lead.
  - `ApprovedAt` (DATETIME2, NULL): Timestamp approved.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Application timestamp.

#### `OwnerRequests`
Legacy partner onboarding requests.
- **Primary Key**: `RequestID` (INT IDENTITY)
- **Key Columns**:
  - `FullName` (NVARCHAR(100), NOT NULL)
  - `Email` (NVARCHAR(100), NOT NULL)
  - `Phone` (NVARCHAR(20), NOT NULL)
  - `Status` (NVARCHAR(20), NOT NULL, DEFAULT 'Pending')
  - `CreatedAccountID` (INT, NULL)
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE())

#### `ContactUs`
Customer support contact messages.
- **Primary Key**: `ContactID` (INT IDENTITY)
- **Key Columns**:
  - `Name` (NVARCHAR(100), NOT NULL): Sender name.
  - `Email` (NVARCHAR(100), NOT NULL): Sender email.
  - `Phone` (NVARCHAR(20), NULL): Sender phone.
  - `Subject` (NVARCHAR(200), NULL): Subject line.
  - `Message` (NVARCHAR(MAX), NOT NULL): Inquiry text.
  - `Status` (NVARCHAR(20), NOT NULL, DEFAULT 'Pending'): `Pending`, `Resolved`, `Spam`.
  - `ResolvedByUserID` (INT, NULL, FK -> `Users.UserID`): Staff who resolved ticket.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Inquiry timestamp.

---

### 3.7 Legal, Compliance & System Operations

#### `LegalDocuments`
Versioned repository of legal documents (Terms of Service, Privacy Policy, Refund Policy).
- **Primary Key**: `DocumentID` (INT IDENTITY)
- **Key Columns**:
  - `DocumentType` (NVARCHAR(50), NOT NULL): `TermsOfService`, `PrivacyPolicy`, `RefundPolicy`.
  - `Version` (NVARCHAR(20), NOT NULL): Document semantic version (e.g. `1.0.0`).
  - `Title` (NVARCHAR(200), NOT NULL): Public title.
  - `ContentHtml` (NVARCHAR(MAX), NOT NULL): Versioned legal text in HTML.
  - `IsActive` (BIT, NOT NULL, DEFAULT 1): Active version marker.
  - `EffectiveDate` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Effective date.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Creation timestamp.

#### `PolicyAcceptances`
Audit trail of consent for mandatory legal policies across registration, leads, and inquiries.
- **Primary Key**: `AcceptanceID` (INT IDENTITY)
- **Key Columns**:
  - `UserID` (INT, NULL, FK -> `Users.UserID`): User account if registered.
  - `LeadID` (INT, NULL, FK -> `PartnerLeads.LeadID`): Partner lead ID if guest applicant.
  - `DocumentType` (NVARCHAR(50), NOT NULL): Policy type accepted.
  - `DocumentVersion` (NVARCHAR(20), NOT NULL): Version accepted.
  - `IpAddress` (NVARCHAR(50), NOT NULL): Client IP address.
  - `PageUrl` (NVARCHAR(255), NOT NULL): Originating submission URL.
  - `AcceptedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Consent timestamp.

#### `NotificationDispatches`
Idempotent deduplication ledger for outbound notifications.
- **Primary Key**: `NotificationKey` (NVARCHAR(255))
- **Key Columns**:
  - `NotificationKey` (NVARCHAR(255), NOT NULL): Composite dedupe key in the format `{eventType}:{entityId}:{recipientUserId}:{channel}`.
  - `DispatchedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Timestamp recorded.

#### `UserNotifications`
In-app notification bell inbox for logged-in users.
- **Primary Key**: `NotificationID` (INT IDENTITY)
- **Key Columns**:
  - `UserID` (INT, NOT NULL, FK -> `Users.UserID`): Target user.
  - `Title` (NVARCHAR(200), NOT NULL): Notification title.
  - `Body` (NVARCHAR(1000), NOT NULL): Alert content.
  - `LinkUrl` (NVARCHAR(500), NULL): Optional redirect URL.
  - `IsRead` (BIT, NOT NULL, DEFAULT 0): Read flag.
  - `CreatedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Timestamp received.

#### `AuditLogs`
Security audit trail for administrative modifications and data mutations.
- **Primary Key**: `AuditID` (INT IDENTITY)
- **Key Columns**:
  - `PerformedByUserID` (INT, NOT NULL, FK -> `Users.UserID`): User executing action.
  - `Action` (NVARCHAR(100), NOT NULL): Action identifier (e.g. `ApproveProperty`, `RejectLead`).
  - `EntityType` (NVARCHAR(50), NOT NULL): Target entity type (e.g. `Property`, `PartnerLead`).
  - `EntityID` (INT, NULL): Target primary key value.
  - `Details` (NVARCHAR(MAX), NULL): Event payload description.
  - `Timestamp` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Execution timestamp.

#### `SiteSettings`
Dynamic runtime settings stored in the database.
- **Primary Key**: `SettingID` (INT IDENTITY)
- **Key Columns**:
  - `SettingKey` (NVARCHAR(100), NOT NULL, UNIQUE): Configuration identifier.
  - `SettingValue` (NVARCHAR(MAX), NULL): Current value.
  - `Category` (NVARCHAR(50), NULL, DEFAULT 'General'): Setting group.
  - `UpdatedAt` (DATETIME2, NULL, DEFAULT GETDATE()): Last updated timestamp.

#### `ApiConfigurations`
Encrypted/obscured third-party API provider configuration settings.
- **Primary Key**: `ConfigID` (INT IDENTITY)
- **Key Columns**:
  - `ServiceProvider` (NVARCHAR(50), NOT NULL): Service name (e.g. `Razorpay`, `WhatsApp`, `SendGrid`).
  - `ApiKey` (NVARCHAR(255), NOT NULL): Provider public key.
  - `ApiSecret` (NVARCHAR(255), NOT NULL): Provider secret token.
  - `ExtraParams` (NVARCHAR(MAX), NULL): Supplementary JSON configuration.
  - `IsActive` (BIT, NOT NULL, DEFAULT 1): Enabled status.

#### `WebhookEvents`
Idempotency table storing processed external webhook events.
- **Primary Key**: `EventID` (INT IDENTITY)
- **Key Columns**:
  - `Provider` (NVARCHAR(50), NOT NULL): Provider tag (e.g. `Razorpay`).
  - `ExternalEventID` (NVARCHAR(100), NOT NULL): Provider event ID.
  - `Payload` (NVARCHAR(MAX), NULL): Raw JSON webhook payload.
  - `ProcessedAt` (DATETIME2, NOT NULL, DEFAULT GETDATE()): Receipt timestamp.
