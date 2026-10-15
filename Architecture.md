# BookMyFun — Platform System Architecture Specification

## 1. Architectural Overview & System Topology

BookMyFun is an enterprise-grade Farmhouse, Luxury Villa, and Resort rental platform built on **ASP.NET Core (.NET 10)**, **Microsoft SQL Server 2022**, and **Raw Parameterized ADO.NET** (`Microsoft.Data.SqlClient`). Designed for predictable high-throughput and zero ORM overhead, the system incorporates tenant isolation, multi-dashboard Role-Based Access Control (RBAC), bank-grade media processing, automated refund calculation, and multi-tier host monetization.

### 1.1 High-Level Architecture Diagram

```mermaid
flowchart TD
    subgraph Clients["Clients & Edge Network"]
        Browser["User Web Browser (Desktop / Mobile)"]
        CDN["Cloudflare Edge / Static Assets CDN"]
    end

    subgraph SecurityPipeline["HTTP Pipeline & Security Middlewares"]
        SecHeaders["SecurityHeadersMiddleware\n(CSP, HSTS, X-Frame-Options, X-Content-Type)"]
        RateLimit["RateLimitingMiddleware\n(In-Memory Window: 100 req/min per IP)"]
        CookieAuth["CookieAuthentication\n(HttpOnly, SameSite=Strict, SecurePolicy)"]
        ExFilter["GlobalExceptionFilter\n(JSON error payload / Error View correlation)"]
    end

    subgraph ControllerLayer["Controllers (MVC / REST API)"]
        HomeCtrl["HomeController\n(Catalog, Public Search, Lead Intake)"]
        AccountCtrl["AccountController\n(Auth, OTP, Host Activation)"]
        BookingCtrl["BookingAndPropertiesController\n(Slots, Checkout, Cancellation)"]
        ClientCtrl["ClientController\n(Bookings, Profile, KYC ID Upload)"]
        OwnerCtrl["OwnerController\n(Listings, Dual Media, Calendar, Claims)"]
        EmployeeCtrl["EmployeeController\n(Leads, ID Review, Claims, Triage)"]
        AdminCtrl["AdminController\n(Analytics, Ledgers, Config, Users)"]
        MediaCtrl["MediaController\n(Multipart Upload, SSRF Verification)"]
    end

    subgraph ServiceLayer["Domain Services & Business Engines"]
        MediaSvc["MediaStorageService\n(Magic-byte validation, UUID, Private Bucket)"]
        RefundSvc["RefundEngineService\n(Cancellation calculation, Gateway deductions)"]
        CommSvc["CommissionService\n(Property > Owner > Global Precedence)"]
        ClaimSvc["DamageClaimService\n(24h window, Itemized breakdown, Adjudication)"]
        LeadSvc["PartnerLeadService\n(Lead qualification, 48h HMAC activation token)"]
        IdSvc["IdVerificationService\n(Private KYC vault, 15-min expiring HMAC URLs)"]
        PricingSvc["BookingPricingService\n(Dynamic pricing, Slot multipliers, Security hold)"]
        PaymentSvc["RazorpayPaymentService\n(PCI-DSS tokenization, Signature verification)"]
        CommNotifier["CommunicationService\n(Email & WhatsApp notifications)"]
        SettingsSvc["SiteSettingsService\n(Platform settings & encrypted API keys)"]
    end

    subgraph RepoLayer["Repository Layer (Raw ADO.NET)"]
        UserRepo["UserRepository"]
        PropRepo["PropertyRepository"]
        BookRepo["BookingRepository"]
        RefundRepo["RefundRepository"]
        LeadRepo["PartnerLeadRepository"]
        ClaimRepo["DamageClaimRepository"]
        IdRepo["IdVerificationRepository"]
        SubRepo["SubscriptionRepository"]
        FinRepo["FinancialRepository"]
        OtpRepo["OtpRepository"]
        ConfigRepo["ConfigRepository"]
        ContactRepo["ContactRepository"]
        OwnerReqRepo["OwnerRequestRepository"]
    end

    subgraph DataLayer["Persistence & Storage"]
        DB["Microsoft SQL Server 2022\n(26 Normalized Tables, Atomic Transactions)"]
        PublicStorage["Public Storage (/wwwroot/uploads/media/)\n(Optimized web photos & videos)"]
        PrivateVault["Private Document Vault (/App_Data/PrivateDocuments/)\n(Restricted KYC Government IDs)"]
    end

    Browser --> CDN
    CDN --> SecHeaders
    SecHeaders --> RateLimit
    RateLimit --> CookieAuth
    CookieAuth --> ExFilter
    ExFilter --> ControllerLayer

    ControllerLayer --> ServiceLayer
    ServiceLayer --> RepoLayer
    RepoLayer --> DB

    MediaSvc --> PublicStorage
    IdSvc --> PrivateVault
```

---

## 2. Multi-Dashboard Role-Based Access Control (RBAC) Matrix

BookMyFun enforces strict tenant isolation and role separation across four distinct dashboards:

| Operational Capability | Client | Owner (Host) | Employee | Admin | Security & Isolation Enforcement |
|:---|:---:|:---:|:---:|:---:|:---|
| **Browse Catalog & Geo-Search** | Read | Read | Read | Read | Public access; rate limited to 100 req/min |
| **Partner Lead Submission** | Create | - | Review | Review | Public form; no user created until approved |
| **Host Activation via Token** | - | Complete Setup | Dispatch | Dispatch | 48h encrypted HMAC-SHA256 token |
| **Property CRUD Management** | - | Scoped (Self) | Full | Full | Tenant isolation: `WHERE OwnerID = @CurrentUserID` |
| **Dual Media Upload (Files/URLs)**| - | Scoped (Self) | Full | Full | Magic-byte checked files + SSRF-filtered URLs |
| **Booking Creation & Slot Hold** | Create | - | Staff Booking | Full | Commission and cancellation snapshots stored |
| **Booking Cancellation & Refund** | Self Request | - | Override Queue | Override Queue | Real-time policy breakdown modal before submit |
| **Raw Government ID Access** | Self (Owner) | **DENIED** | 15-min Expiring | 15-min Expiring | Stored in `/App_Data/PrivateDocuments/` |
| **Guest Verification Badge** | - | Flag Guest | Review & Grant | Review & Grant | Host sees only "Verified Guest" badge, zero PII |
| **Security Deposit Hold & Claim** | Hold at Pay | File Claim (24h) | Adjudicate | Adjudicate | 24h deadline post-checkout with photo evidence |
| **Subscription Tiers & Monetization**| - | View/Upgrade | Manage | Full Override | Free, Featured, Managed Concierge Pack |
| **Financial Ledgers & Analytics** | - | Scoped Payouts | Support Stats | Full Dashboard | Date presets, ClosedXML Excel, streaming CSV |
| **User & Account Management** | Self Profile | Self Profile | Support View | Full (Suspend/Reset)| Cryptographic audit logs on all state mutations |
| **Platform Config & API Keys** | - | - | - | Full | AES/HMAC encrypted settings in database |

---

## 3. Layered Architectural Decomposition

### 3.1 Presentation & Routing Layer
- **Technology:** ASP.NET Core MVC with Razor View Engine.
- **Unified Views for Operations:** Admin and Employee dashboards share operational views (`Views/Admin/RefundsQueue.cshtml`, `Views/Admin/OnboardingLeads.cshtml`, `Views/Admin/DamageClaimsQueue.cshtml`, `Views/Admin/IdVerificationsQueue.cshtml`). The active controller context (`@controllerName`) dynamically switches form submission endpoints to prevent permission elevation.
- **Asynchronous AJAX Endpoints:** Lightweight RESTful actions return standardized JSON payloads (`{ success: true, data: ... }` or `{ success: false, error: ... }`).

### 3.2 Security & Middleware Pipeline
1. **SecurityHeadersMiddleware:**
   - Injects `Content-Security-Policy`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, and `Permissions-Policy`.
2. **RateLimitingMiddleware:**
   - Sliding-window in-memory IP rate limiter enforcing 100 requests per minute per IP, protecting search and login endpoints from volumetric abuse.
3. **CookieAuthenticationMiddleware:**
   - Enforces `HttpOnly: true`, `SameSite: Strict`, and `SecurePolicy: SameAsRequest` with a 7-day sliding expiration window.
4. **GlobalExceptionFilter:**
   - Catches unhandled exceptions, logs detailed telemetry with correlation IDs, and renders clean user error pages or JSON error bodies without leaking database structures.

### 3.3 Domain Services Layer

| Service Name | Primary Responsibilities |
|:---|:---|
| [`MediaStorageService`](file:///d:/Projects/BookMyFun/Services/MediaStorageService.cs) | Magic-byte file validation, UUID filename assignment, private vault storage, SSRF URL defense with DNS validation. |
| [`RefundEngineService`](file:///d:/Projects/BookMyFun/Services/RefundEngineService.cs) | Real-time refund estimation, policy snapshot evaluation, 2% gateway deduction, and automated/manual refund processing. |
| [`CommissionService`](file:///d:/Projects/BookMyFun/Services/BookingAndCommissionService.cs) | Three-tier commission resolution (`Property.CommissionRate ?? Owner.CustomCommissionRate ?? GlobalSettings.10%`) and ledger balance calculation. |
| [`DamageClaimService`](file:///d:/Projects/BookMyFun/Services/DamageClaimService.cs) | Enforces 24-hour post-checkout filing deadline, itemized damage costs, evidence media links, and deposit release/deduction adjudication. |
| [`PartnerLeadService`](file:///d:/Projects/BookMyFun/Services/PartnerLeadService.cs) | Ingests host partnership inquiries, facilitates staff triage, generates encrypted 48-hour activation tokens, and activates owner accounts. |
| [`IdVerificationService`](file:///d:/Projects/BookMyFun/Services/IdVerificationService.cs) | Secure document intake (Aadhaar, Passport, DL), 15-minute expiring HMAC-SHA256 URL generation, and guest verification badge issuance. |
| [`BookingPricingService`](file:///d:/Projects/BookMyFun/Services/BookingAndCommissionService.cs) | Calculates multi-duration rates (Full Day, Morning/Evening slots, Multi-day), security deposit requirements, and tax totals. |
| [`RazorpayPaymentService`](file:///d:/Projects/BookMyFun/Services/PaymentGatewayService.cs) | Tokenized checkout creation, HMAC-SHA256 webhook signature verification, payment status confirmation, and refund API dispatch. |
| [`CommunicationService`](file:///d:/Projects/BookMyFun/Services/CommunicationService.cs) | Dispatches transactional booking confirmations, OTP verification codes, and host activation emails. |
| [`SiteSettingsService`](file:///d:/Projects/BookMyFun/Services/SiteSettingsService.cs) | Manages global platform settings, commission percentages, support contact details, and encrypted third-party credentials. |

### 3.4 Data Access Layer (Raw ADO.NET)
- **Elimination of ORM Latency:** Queries execute directly through `Microsoft.Data.SqlClient` with explicit parameterized queries, eliminating EF Core change tracker overhead.
- **Helper Architecture (`DBHelper`):** Centralizes connection management, parameterized command creation, null value normalization (`DBNull.Value`), and scalar/reader/non-query execution.
- **Idempotent Evolution (`DatabaseInitializer`):** Automatically initializes schemas, creates missing tables, and applies schema additions on application boot without external migration scripts.

---

## 4. Sequence Diagrams of Core Workflows

### 4.1 Booking, Pricing Snapshot & Payment Workflow

```mermaid
sequenceDiagram
    autonumber
    actor Client
    participant Web as BookingController
    participant Pricing as BookingPricingService
    participant Comm as CommissionService
    participant Gateway as RazorpayPaymentService
    participant Repo as BookingRepository
    participant DB as SQL Server 2022

    Client->>Web: Select Property, Dates, Duration (Full Day / Slot)
    Web->>Pricing: CalculateTotal(propertyId, dates, slotType)
    Pricing-->>Web: BaseAmount, SecurityDeposit, Taxes, GrandTotal
    Web->>Comm: ResolveCommissionRate(propertyId, ownerId)
    Comm-->>Web: EffectiveRate (e.g. 12%), PlatformCommission, HostPayout
    Web->>Gateway: CreateOrder(GrandTotal, Currency="INR")
    Gateway-->>Web: OrderId, KeyId
    Web-->>Client: Render Checkout Modal (Razorpay SDK)

    Client->>Gateway: Complete Payment (Card/UPI/NetBanking)
    Gateway-->>Client: RazorpayPaymentId, Signature
    Client->>Web: POST /Booking/ConfirmPayment(BookingData, PaymentId, Signature)
    Web->>Gateway: VerifySignature(OrderId, PaymentId, Signature)
    alt Signature Valid
        Web->>Repo: CreateBookingWithSnapshots(booking, payment)
        Repo->>DB: INSERT Bookings (With Policy & Commission Snapshots)
        Repo->>DB: INSERT Payments (Status='Success')
        DB-->>Repo: BookingID
        Web-->>Client: 200 OK (Booking Confirmed, Send Email/WhatsApp)
    else Signature Invalid
        Web-->>Client: 400 Bad Request (Payment Tamper Detected)
    end
```

### 4.2 Cancellation & Automated Refund Workflow

```mermaid
sequenceDiagram
    autonumber
    actor Client
    participant Web as BookingController
    participant RefundSvc as RefundEngineService
    participant Repo as BookingRepository
    participant Gateway as RazorpayPaymentService
    participant DB as SQL Server 2022

    Client->>Web: Request Cancellation Quote for Booking #ID
    Web->>RefundSvc: CalculateRefundEstimate(BookingID)
    RefundSvc->>Repo: GetBookingWithSnapshots(BookingID)
    Repo-->>RefundSvc: Booking Details (Snapshots: FreeWindow=48h, Cutoff=24h, Fee=10%)
    RefundSvc->>RefundSvc: Calculate Elapsed Hours to Check-In
    alt > 48 Hours before Check-in
        RefundSvc-->>Web: Full Refund minus 2% Gateway Fee
    else Between 48h and 24h
        RefundSvc-->>Web: Refund minus 10% Fee and 2% Gateway Fee
    else < 24 Hours before Check-in
        RefundSvc-->>Web: Non-refundable (0.00 Refund)
    end
    Web-->>Client: Display Transparent Refund Breakdown Modal

    Client->>Web: POST /Booking/CancelBooking(BookingID, Reason)
    Web->>RefundSvc: ProcessCancellationAsync(BookingID, Reason, UserId)
    RefundSvc->>Gateway: IssueRefund(PaymentId, NetRefundAmount, IdempotencyKey)
    Gateway-->>RefundSvc: RefundId, Status='Completed'
    RefundSvc->>DB: INSERT BookingRefunds, UPDATE Bookings (Status='Cancelled')
    RefundSvc-->>Web: Refund Success Response
    Web-->>Client: Display Confirmation & Email Receipt
```

### 4.3 Partner Lead Onboarding & Host Activation

```mermaid
sequenceDiagram
    autonumber
    actor HostCandidate as Prospective Host
    participant Home as HomeController
    participant LeadSvc as PartnerLeadService
    participant LeadRepo as PartnerLeadRepository
    actor Staff as Employee / Admin
    participant Email as CommunicationService
    participant Account as AccountController
    participant DB as SQL Server 2022

    HostCandidate->>Home: Submit "List Your Farm" Form (Name, Phone, Email, Acreage)
    Home->>LeadSvc: SubmitLeadAsync(leadDto)
    LeadSvc->>LeadRepo: InsertLead(lead)
    LeadRepo->>DB: INSERT PartnerLeads (Status='New')
    LeadRepo-->>LeadSvc: LeadID
    LeadSvc-->>Home: Confirmation Acknowledged

    Staff->>Home: View /Employee/OnboardingLeads
    Staff->>LeadSvc: ApproveAndInviteLead(LeadID, StaffUserID)
    LeadSvc->>LeadSvc: Generate 48h HMAC-SHA256 Token
    LeadSvc->>LeadRepo: UpdateLeadStatus(LeadID, 'Approved', Token, ExpiresAt)
    LeadSvc->>Email: SendInviteEmail(Email, ActivationLinkWithToken)

    HostCandidate->>Account: Click /Account/ActivateHost?token=XYZ
    Account->>Account: Verify Token Signature & Expiration
    Account-->>HostCandidate: Render Host Account Creation Form
    HostCandidate->>Account: POST Password, Phone, Farm Details
    Account->>DB: INSERT Users (Role='Owner', IsActive=1)
    Account-->>HostCandidate: Account Active -> Redirect to /Owner/Dashboard
```

### 4.4 Client Government ID Verification & Privacy Shield

```mermaid
sequenceDiagram
    autonumber
    actor Client
    participant ClientCtrl as ClientController
    participant IdSvc as IdVerificationService
    participant MediaSvc as MediaStorageService
    actor Host as Property Owner
    actor Staff as Employee / Admin
    participant DB as SQL Server 2022

    Client->>ClientCtrl: Upload Government ID (Aadhaar/Passport/DL)
    ClientCtrl->>MediaSvc: StorePrivateDocumentAsync(File)
    MediaSvc->>MediaSvc: Magic-byte Check (PDF, JPG, PNG <= 5MB)
    MediaSvc->>MediaSvc: Write to /App_Data/PrivateDocuments/{GUID}.ext
    MediaSvc-->>ClientCtrl: DocumentStorageKey
    ClientCtrl->>IdSvc: RegisterVerificationAsync(ClientID, DocType, StorageKey)
    IdSvc->>DB: INSERT ClientIdVerifications (Status='PendingReview')
    ClientCtrl-->>Client: Document Uploaded Successfully

    Host->>ClientCtrl: Query Client Details for Booking
    ClientCtrl-->>Host: Guest Name, Contact, "Verification Pending" (ZERO RAW ID ACCESS)

    Staff->>ClientCtrl: Open /Employee/IdVerifications
    ClientCtrl->>IdSvc: GenerateSignedDocumentUrl(VerificationID, 15 Minutes)
    IdSvc-->>ClientCtrl: Temporary Signed URL (/Media/PrivateDoc?token=HMAC)
    Staff->>ClientCtrl: Inspect ID Document in Browser
    Staff->>IdSvc: ApproveVerification(VerificationID, StaffUserID)
    IdSvc->>DB: UPDATE ClientIdVerifications (Status='Verified')
    
    Host->>ClientCtrl: Re-query Booking
    ClientCtrl-->>Host: "Verified Guest" Green Badge Displayed
```

### 4.5 Security Deposit Hold & 24-Hour Damage Claim Adjudication

```mermaid
sequenceDiagram
    autonumber
    actor Host
    actor Client
    participant OwnerCtrl as OwnerController
    participant ClaimSvc as DamageClaimService
    actor Staff as Employee / Admin
    participant Gateway as RazorpayPaymentService
    participant DB as SQL Server 2022

    Note over Client,Host: Guest Checks Out at 11:00 AM on Day X
    alt Within 24 Hours of Checkout
        Host->>OwnerCtrl: File Damage Claim (Booking #ID, Amount=₹5000, Breakdown, Evidence URLs)
        OwnerCtrl->>ClaimSvc: SubmitClaimAsync(claimDto, HostUserID)
        ClaimSvc->>ClaimSvc: Validate Booking Checkout < 24 Hours
        ClaimSvc->>DB: INSERT DamageClaims (Status='UnderReview')
        ClaimSvc-->>OwnerCtrl: Claim Registered
    else After 24 Hours
        Host->>OwnerCtrl: File Damage Claim
        OwnerCtrl-->>Host: REJECTED: Claim window expired (24h cutoff exceeded)
    end

    Staff->>OwnerCtrl: Open /Admin/DamageClaimsQueue
    Staff->>ClaimSvc: AdjudicateClaim(ClaimID, ApprovedAmount=₹3500, Notes, StaffUserID)
    ClaimSvc->>Gateway: CapturePreAuthDeposit(BookingID, Amount=₹3500)
    Gateway-->>ClaimSvc: Transferred ₹3500 to Host
    ClaimSvc->>Gateway: ReleaseDepositBalance(BookingID, Balance=₹1500)
    Gateway-->>ClaimSvc: Refunded ₹1500 to Guest
    ClaimSvc->>DB: UPDATE DamageClaims (Status='Settled', AdjudicatedAmount=3500)
    ClaimSvc->>DB: UPDATE Bookings (SecurityDepositStatus='Settled')
    ClaimSvc-->>Staff: Adjudication Complete
```

---

## 5. Security & Isolation Architecture

### 5.1 Object-Level Access Control (IDOR Defense)
- **Multi-Tenant Scoping:** Every query executed within `OwnerController` and `PropertyRepository` includes the owner identifier:
  ```sql
  SELECT * FROM Properties WHERE PropertyID = @PropertyID AND OwnerID = @CurrentUserID;
  ```
- **Staff Sovereign Access:** `Admin` and `Employee` roles bypass owner tenant scoping for administrative oversight, with all actions recorded in `AuditLogs`.

### 5.2 Server-Side Request Forgery (SSRF) Defense
- When external media URLs (YouTube, Vimeo, Cloudinary, AWS S3) are submitted:
  1. The URI scheme is strictly verified to be `https://`.
  2. The hostname is resolved to an IPv4/IPv6 address via DNS.
  3. The resolved address is checked against private, loopback, and cloud metadata ranges (`127.0.0.1`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.169.254`).
  4. If any internal or link-local address is matched, the request is immediately aborted with a `SecurityViolationException`.

### 5.3 Cryptographic Audit Trail
- Every administrative mutation, cancellation override, damage claim adjudication, and owner status change writes to `AuditLogs` containing:
  - `PerformedByUserID`
  - `Action`
  - `EntityType`
  - `EntityID`
  - `Details` (JSON payload)
  - `Timestamp`

---

## 6. Scalability & Deployment Architecture

- **Stateless Application Tier:** ASP.NET Core application nodes are stateless; sessions and authentication rely on signed HTTP-only cookies and SQL persistence, allowing horizontal scaling behind AWS ALB / Azure Application Gateway / Nginx.
- **Static Assets Offloading:** Media uploads are structured to easily transition from local disk (`wwwroot/uploads`) to S3/Cloudflare R2 buckets by changing `IMediaStorageService` configuration without touching business controllers.
- **Database Partitioning Readiness:** The 26 normalized SQL tables maintain foreign key boundaries enabling future table partitioning on `Bookings`, `FinancialLedger`, and `AuditLogs` by fiscal year.
