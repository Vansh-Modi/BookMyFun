# BookMyFun — System Architecture Specification

## 1. Executive Summary & Architectural Overview
BookMyFun is an enterprise-grade Farm, Luxury Villa, and Resort rental platform built on ASP.NET Core MVC (.NET 10), SQL Server, and raw ADO.NET with parameterized queries for maximum throughput and predictable memory utilization. The system serves four distinct user roles through specialized dashboards:
1. **Client (Customer):** Browse, search with Google Maps, book flexible slots, upload verification IDs, manage cancellations with real-time refund estimation, and submit reviews.
2. **Owner (Farm Host):** Onboard via invite tokens, manage properties with dual-mode media (direct file uploads and external stream URLs), monitor subscription tier allowances, view booking calendars, and file post-checkout damage claims.
3. **Employee (Operations & Support):** Onboard partner leads, verify client IDs without exposing raw PII to owners, triage cancellation & refund overrides, adjudicate damage claims, and support guest requests.
4. **Admin (Superuser):** Complete platform sovereignty, financial ledger oversight, dynamic multi-dimensional analytics with CSV/Excel exports, user management, audit trails, and global configuration.

```
                              ┌──────────────────────────────────┐
                              │     Cloudflare / CDN Edge        │
                              └─────────────────┬────────────────┘
                                                │ HTTPS / TLS 1.3
                                                ▼
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                                 ASP.NET Core Application                               │
│                                                                                        │
│  ┌─────────────────────────┐  ┌─────────────────────────┐  ┌────────────────────────┐  │
│  │ Security & Rate Limiter │  │ Cookie Auth (HttpOnly)  │  │ Error Handling Filter  │  │
│  │ Middleware (Helmet/CSP) │  │ & Anti-Forgery CSRF     │  │ & Exception Wrapper    │  │
│  └────────────┬────────────┘  └────────────┬────────────┘  └───────────┬────────────┘  │
│               │                            │                           │               │
│               ▼                            ▼                           ▼               │
│  ┌──────────────────────────────────────────────────────────────────────────────────┐  │
│  │                         Controllers & Routing Engine                             │  │
│  │  ClientController  │  OwnerController  │  EmployeeController  │  AdminController │  │
│  │  BookingController │  MediaController  │  AccountController   │  LeadController  │  │
│  └─────────────────────────────────────────┬────────────────────────────────────────┘  │
│                                            │                                           │
│  ┌─────────────────────────────────────────▼────────────────────────────────────────┐  │
│  │                                  Service Layer                                   │  │
│  │  - MediaStorageService (Magic-byte, UUID, Private Buckets, SSRF Sanitization)    │  │
│  │  - RefundEngineService (Cancellation rules, Gateway fee deductions, Overrides)   │  │
│  │  - CommissionEngine (Property > Owner > Global precedence hierarchy)             │  │
│  │  - DamageClaimService (24h window, photographic adjudication, deposit hold/cut)  │  │
│  │  - PaymentGatewayService (PCI-DSS tokenized checkout, Webhook signature verifier)│  │
│  │  - CommunicationService (Encrypted invite tokens, WhatsApp/Email OTPs)           │  │
│  └─────────────────────────────────────────┬────────────────────────────────────────┘  │
│                                            │                                           │
│  ┌─────────────────────────────────────────▼────────────────────────────────────────┐  │
│  │                      Repository Layer (Parameterized ADO.NET)                    │  │
│  │  UserRepository │ PropertyRepository │ BookingRepository │ FinancialRepository   │  │
│  │  LeadRepository │ DamageClaimRepo    │ IdVerificationRepo│ SubscriptionRepo      │  │
│  └─────────────────────────────────────────┬────────────────────────────────────────┘  │
└────────────────────────────────────────────┼───────────────────────────────────────────┘
                                             │ Parameterized SQL Queries
                                             ▼
                              ┌──────────────────────────────────┐
                              │     Microsoft SQL Server 2022    │
                              │  - Relational Schemas & Indexes  │
                              │  - Atomic Financial Ledgers      │
                              └──────────────────────────────────┘
```

---

## 2. Multi-Dashboard Role-Based Access Control (RBAC) Matrix

| Resource / Action | Client | Owner | Employee | Admin | Security & Isolation Constraint |
|:---|:---:|:---:|:---:|:---:|:---|
| **Public Browse & Map Search** | Read | Read | Read | Read | Public access; rate limited to 100 req/min per IP |
| **Partner Lead Intake** | Create | - | View/Review | View/Review | Public submission; no account created until Employee/Admin approves |
| **Owner Invite & Activation** | - | Complete Setup | Initiate/Send | Initiate/Send | Encrypted 24h HMAC-SHA256 token delivered via email |
| **Property CRUD** | - | Scoped (Self) | Full | Full | Tenant isolation: Owner can only access properties matching their `OwnerID` |
| **Dual Media Upload (Files/URLs)** | - | Scoped (Self) | Full | Full | Magic-byte checked files + SSRF-sanitized HTTPS external URLs |
| **Booking Creation & Slot Hold** | Create | - | Create for guest | Full | Client ID bound from auth cookie; commission snapshot taken at creation |
| **Booking Cancellation & Refund** | Request/Self | - | Override/Triage | Override/Triage | Client can cancel based on window policy; Staff can execute operational override |
| **Raw Government ID Access** | Self (Owner) | **DENIED** | Read (Verification) | Read (Verification) | 15-minute expiring presigned links; stored in private bucket |
| **Guest Verification Badge Flag**| - | Flag Request | Verify & Badge | Verify & Badge | Owner receives only verification status badge, never raw ID or PII |
| **Security Deposit Hold & Claim**| Hold at checkout| File Claim (24h) | Adjudicate | Adjudicate | Claim requires itemized breakdown and photo/video evidence |
| **Owner Subscription & Monetization**| - | View/Upgrade | Manage | Full Override | Free/Starter, Featured/Spotlight, Managed Concierge Pack |
| **Financial Ledger & Analytics** | - | Scoped Payouts | Support Reports | Full Dynamic Analytics| Admin gets GMV, platform commission, occupancy, CSV/Excel export |
| **User & Account Governance** | - | - | Support view | Full (Suspend/Reset) | Audit trail logged for every state-altering action |

---

## 3. Database Schemas & Relational Model

The database schema utilizes relational constraints, composite indices, cascade rules, and check constraints to preserve structural integrity:

### 3.1 Table Schemas & Relational DDL

#### `Users`
- `UserID` (INT, IDENTITY(1,1), PK)
- `FullName` (NVARCHAR(100), NOT NULL)
- `Email` (NVARCHAR(100), UNIQUE, NOT NULL)
- `PasswordHash` (NVARCHAR(255), NOT NULL)
- `Phone` (NVARCHAR(20), NULL)
- `Role` (NVARCHAR(20), CHECK Role IN ('Admin', 'Employee', 'Owner', 'Client'))
- `IsActive` (BIT, DEFAULT 1)
- `CustomCommissionRate` (DECIMAL(5,2), NULL) — Overrides default platform rate for high-volume hosts
- `SubscriptionTier` (NVARCHAR(30), DEFAULT 'Free') — 'Free', 'Featured', 'Managed'
- `CreatedAt` (DATETIME2, DEFAULT GETDATE())

#### `PartnerLeads` (Replaces Public Owner Self-Serve Registration)
- `LeadID` (INT, IDENTITY(1,1), PK)
- `FullName` (NVARCHAR(100), NOT NULL)
- `Phone` (NVARCHAR(20), NOT NULL)
- `Email` (NVARCHAR(100), NOT NULL)
- `PropertyName` (NVARCHAR(150), NOT NULL)
- `Location` (NVARCHAR(255), NOT NULL)
- `AcreageOrType` (NVARCHAR(100), NOT NULL)
- `Message` (NVARCHAR(MAX), NULL)
- `Status` (NVARCHAR(30), DEFAULT 'New', CHECK Status IN ('New', 'Contacted', 'Approved', 'Rejected'))
- `ActivationToken` (NVARCHAR(255), NULL)
- `TokenExpiresAt` (DATETIME2, NULL)
- `ReviewedByUserID` (INT, FK -> Users(UserID), NULL)
- `CreatedAt` (DATETIME2, DEFAULT GETDATE())

#### `Properties`
- `PropertyID` (INT, IDENTITY(1,1), PK)
- `OwnerID` (INT, FK -> Users(UserID), NOT NULL)
- `PropertyTypeID` (INT, FK -> PropertyTypes(PropertyTypeID), NOT NULL)
- `Title` (NVARCHAR(150), NOT NULL)
- `Description` (NVARCHAR(MAX), NULL)
- `Location` (NVARCHAR(255), NOT NULL)
- `FormattedAddress` (NVARCHAR(500), NULL)
- `Latitude` (DECIMAL(9,6), NULL)
- `Longitude` (DECIMAL(9,6), NULL)
- `PricePerNight` (DECIMAL(18,2), NOT NULL)
- `SecurityDepositAmount` (DECIMAL(18,2), DEFAULT 0.00)
- `CommissionRate` (DECIMAL(5,2), NULL) — Overrides Owner and Platform default
- `FreeCancellationWindowHours` (INT, DEFAULT 48)
- `CancellationCutoffHours` (INT, DEFAULT 24)
- `CancellationFeeType` (NVARCHAR(20), DEFAULT 'percentage', CHECK CancellationFeeType IN ('percentage', 'fixed'))
- `CancellationFeeValue` (DECIMAL(18,2), DEFAULT 10.00)
- `MaxGuests` (INT, NOT NULL)
- `ListingStatus` (NVARCHAR(20), DEFAULT 'Draft', CHECK ListingStatus IN ('Draft', 'PendingPayment', 'Active', 'Suspended'))
- `IsFeatured` (BIT, DEFAULT 0)
- `IsApproved` (BIT, DEFAULT 0)
- `CreatedAt` (DATETIME2, DEFAULT GETDATE())

#### `PropertyMedia` (Dual File/URL Media Architecture)
- `MediaID` (INT, IDENTITY(1,1), PK)
- `PropertyID` (INT, FK -> Properties(PropertyID) ON DELETE CASCADE, NOT NULL)
- `MediaType` (NVARCHAR(10), CHECK MediaType IN ('photo', 'video'))
- `SourceType` (NVARCHAR(10), CHECK SourceType IN ('file', 'url'))
- `StorageUrl` (NVARCHAR(MAX), NOT NULL)
- `PublicId` (NVARCHAR(255), NULL) — Storage key or provider ID
- `IsPrimary` (BIT, DEFAULT 0)
- `SortOrder` (INT, DEFAULT 0)
- `CreatedAt` (DATETIME2, DEFAULT GETDATE())

#### `PropertyAmenities` & `AmenitiesMaster`
- `AmenityID` (INT, IDENTITY(1,1), PK)
- `AmenityName` (NVARCHAR(50), NOT NULL, UNIQUE)
- `IconClass` (NVARCHAR(50), NOT NULL)
- `Category` (NVARCHAR(50), DEFAULT 'General')
- Junction Table: `PropertyAmenitiesMapping(PropertyID, AmenityID)`

#### `Bookings`
- `BookingID` (INT, IDENTITY(1,1), PK)
- `PropertyID` (INT, FK -> Properties(PropertyID), NOT NULL)
- `ClientID` (INT, FK -> Users(UserID), NOT NULL)
- `OccasionID` (INT, FK -> Occasions(OccasionID), NULL)
- `CheckInDate` (DATE, NOT NULL)
- `CheckOutDate` (DATE, NOT NULL)
- `TotalAmount` (DECIMAL(18,2), NOT NULL)
- `SecurityDepositAmount` (DECIMAL(18,2), NOT NULL, DEFAULT 0.00)
- `SecurityDepositStatus` (NVARCHAR(30), DEFAULT 'Held', CHECK SecurityDepositStatus IN ('Held', 'Released', 'PartiallyClaimed', 'FullyClaimed'))
- `CommissionRateSnapshot` (DECIMAL(5,2), NOT NULL)
- `PlatformCommissionAmount` (DECIMAL(18,2), NOT NULL)
- `OwnerPayoutAmount` (DECIMAL(18,2), NOT NULL)
- `FreeCancellationWindowHoursSnapshot` (INT, NOT NULL)
- `CancellationCutoffHoursSnapshot` (INT, NOT NULL)
- `CancellationFeeTypeSnapshot` (NVARCHAR(20), NOT NULL)
- `CancellationFeeValueSnapshot` (DECIMAL(18,2), NOT NULL)
- `Status` (NVARCHAR(20), DEFAULT 'Pending', CHECK Status IN ('Pending', 'Confirmed', 'Cancelled', 'Completed'))
- `IdempotencyKey` (NVARCHAR(100), UNIQUE, NULL)
- `CreatedAt` (DATETIME2, DEFAULT GETDATE())

#### `BookingRefunds`
- `RefundID` (INT, IDENTITY(1,1), PK)
- `BookingID` (INT, FK -> Bookings(BookingID), NOT NULL)
- `GrossBookingAmount` (DECIMAL(18,2), NOT NULL)
- `CancellationFeeDeducted` (DECIMAL(18,2), NOT NULL)
- `GatewayFeeDeducted` (DECIMAL(18,2), NOT NULL)
- `NetRefundAmount` (DECIMAL(18,2), NOT NULL)
- `RefundReason` (NVARCHAR(500), NULL)
- `Status` (NVARCHAR(30), DEFAULT 'Initiated', CHECK Status IN ('Initiated', 'Processing', 'Completed', 'Failed', 'Overridden'))
- `GatewayRefundID` (NVARCHAR(100), NULL)
- `ProcessedByUserID` (INT, FK -> Users(UserID), NULL)
- `ProcessedAt` (DATETIME2, NULL)
- `CreatedAt` (DATETIME2, DEFAULT GETDATE())

#### `DamageClaims`
- `ClaimID` (INT, IDENTITY(1,1), PK)
- `BookingID` (INT, FK -> Bookings(BookingID), NOT NULL)
- `PropertyID` (INT, FK -> Properties(PropertyID), NOT NULL)
- `OwnerID` (INT, FK -> Users(UserID), NOT NULL)
- `ClaimAmount` (DECIMAL(18,2), NOT NULL)
- `ItemizedDescription` (NVARCHAR(MAX), NOT NULL)
- `EvidenceMediaUrls` (NVARCHAR(MAX), NOT NULL) -- JSON array of file/URL evidence
- `Status` (NVARCHAR(30), DEFAULT 'UnderReview', CHECK Status IN ('UnderReview', 'Approved', 'Rejected', 'Settled'))
- `AdjudicatedAmount` (DECIMAL(18,2), NULL)
- `AdjudicatedByUserID` (INT, FK -> Users(UserID), NULL)
- `AdjudicationNotes` (NVARCHAR(MAX), NULL)
- `AdjudicatedAt` (DATETIME2, NULL)
- `CreatedAt` (DATETIME2, DEFAULT GETDATE())

#### `ClientIdVerifications`
- `VerificationID` (INT, IDENTITY(1,1), PK)
- `ClientID` (INT, FK -> Users(UserID), NOT NULL)
- `DocumentType` (NVARCHAR(50), NOT NULL) -- 'Aadhaar', 'Passport', 'DrivingLicense'
- `DocumentStorageKey` (NVARCHAR(500), NOT NULL) -- Private bucket storage path
- `MaskedIdentifier` (NVARCHAR(20), NULL) -- e.g., 'XXXX-XXXX-1234'
- `VerificationStatus` (NVARCHAR(30), DEFAULT 'PendingReview', CHECK VerificationStatus IN ('PendingReview', 'Verified', 'Rejected'))
- `RejectionReason` (NVARCHAR(255), NULL)
- `VerifiedByUserID` (INT, FK -> Users(UserID), NULL)
- `VerifiedAt` (DATETIME2, NULL)
- `CreatedAt` (DATETIME2, DEFAULT GETDATE())

---

## 4. Service Layers & Data Flow

1. **`MediaStorageService`**: Validates file magic bytes (JPEG `FF D8 FF`, PNG `89 50 4E 47`, WEBP `52 49 46 46`, MP4 `00 00 00 ... ftyp`), computes cryptographically random UUID names, uploads to private or public storage, and executes SSRF checks on submitted external URLs (verifying HTTPS, DNS resolution, and denying `127.0.0.1`, `10.0.0.0/8`, `169.254.169.254`, etc.).
2. **`RefundEngineService`**: Calculates dynamic refund amounts based on `CheckInDate`, `freeCancellationWindowHours`, `cancellationCutoffHours`, and gateway processing fees. Triggers payment gateway refund APIs with idempotency keys.
3. **`CommissionEngine`**: Resolves property-level commission, falling back to owner-level custom rate, falling back to global platform setting. Locks and stores immutable snapshots on booking rows.
4. **`DamageClaimService`**: Enforces a strict 24-hour filing deadline post check-out. Holds security deposit pre-auth and coordinates adjudication payouts between Host and Client.
5. **`PaymentGatewayService`**: Orchestrates client-side tokenized payments, handles Razorpay/Stripe webhooks with cryptographic HMAC signature verification, and guarantees zero storage of card numbers or sensitive credentials.
