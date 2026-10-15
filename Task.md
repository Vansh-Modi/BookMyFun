# BookMyFun — Step-by-Step Implementation & Refactoring Checklist

## Phase 0: Initialization & Documentation
- [x] Create `Architecture.md` (RBAC matrix, schema specifications, service layers)
- [x] Create `Design.md` (UI/UX specs, component layouts, error fallbacks)
- [x] Create `PRD.md` (8 functional additions, security requirements)
- [x] Create `Rules.md` (Coding standards, OWASP/PCI-DSS, error hierarchy)
- [x] Create `Task.md` (Step-by-step checklist)
- [x] Create `Memory.md` (Context retention & architectural decisions)

---

## Phase 1: Database Schema Migration & Entity Models
- [x] Update `DatabaseInitializer.cs`:
  - [x] Add `FreeCancellationWindowHours`, `CancellationCutoffHours`, `CancellationFeeType`, `CancellationFeeValue` to `Properties`
  - [x] Add `SecurityDepositAmount`, `CommissionRate`, `FormattedAddress`, `Latitude`, `Longitude` to `Properties`
  - [x] Create `PropertyMedia` table supporting `{ type: 'file' | 'url', url: String, publicId?: String }`
  - [x] Create `AmenitiesMaster` and `PropertyAmenitiesMapping` tables (seeded with 12 initial taxonomy items)
  - [x] Add cancellation policy and commission snapshots to `Bookings` (`FreeCancellationWindowHoursSnapshot`, `CancellationCutoffHoursSnapshot`, `CancellationFeeTypeSnapshot`, `CancellationFeeValueSnapshot`, `CommissionRateSnapshot`, `PlatformCommissionAmount`, `OwnerPayoutAmount`, `SecurityDepositAmount`, `SecurityDepositStatus`, `IdempotencyKey`)
  - [x] Create `BookingRefunds` table
  - [x] Create `PartnerLeads` table for host intake
  - [x] Create `DamageClaims` table with 24h deadline & itemized evidence
  - [x] Create `ClientIdVerifications` table for private document governance
- [x] Update `Models/Entities/Entities.cs` to reflect all new tables and columns
- [x] Update `Models/ViewModels/ViewModels.cs` with DTOs for:
  - [x] Dual Media Upload Request & Response (`ExternalUrlVerifyRequest`, `AdditionalMediaJson`)
  - [x] Cancellation Quote & Confirmation (`CancellationQuoteViewModel`, `ProcessCancellationRequest`)
  - [x] Partner Lead Intake (`PartnerLeadIntakeViewModel`, `ActivateOwnerViewModel`)
  - [x] Damage Claim Submission & Adjudication (`DamageClaimCreateViewModel`, `DamageClaimAdjudicateViewModel`)
  - [x] ID Verification Upload & Review (`ClientIdVerificationUploadViewModel`, `ClientIdVerificationReviewViewModel`)
  - [x] Filterable Analytics Query & Export (`AdminAnalyticsViewModel`)

---

## Phase 2: Security Infrastructure, Middleware & Utilities
- [x] Implement Custom Exception Hierarchy in `Services/Exceptions/AppExceptions.cs` (`AppException`, `ValidationException`, `NotFoundException`, `ForbiddenException`, `SecurityViolationException`, `BusinessRuleException`)
- [x] Create Global Error Handling Middleware & Filter in `Services/Filters/GlobalExceptionFilter.cs` (handling both JSON/API and MVC View errors cleanly)
- [x] Implement `MediaStorageService`:
  - [x] Magic-byte binary inspection (JPEG, PNG, WEBP <= 10MB; MP4, MOV, WEBM <= 100MB; PDF, JPG, PNG <= 5MB)
  - [x] UUID filename generator with traversal path stripping
  - [x] Private (`App_Data/PrivateDocuments`) vs. Public (`wwwroot/uploads`) storage routing
  - [x] SSRF defense on external URLs (HTTPS only, DNS resolution with RFC 1918 / loopback / cloud metadata 169.254.169.254 block)
  - [x] HMACSHA256 15-minute expiring token generator for private Gov ID documents
- [x] Implement Distributed In-Memory Rate Limiter in `Services/Security/RateLimitingMiddleware.cs`
- [x] Configure Security HTTP Headers (CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy, Permissions-Policy) in `Services/Security/SecurityHeadersMiddleware.cs`
- [x] Verify Anti-Forgery tokens on state-altering POST endpoints

---

## Phase 3: Core Service & Engine Implementations
- [x] Implement `RefundEngineService`:
  - [x] Real-time refund estimation based on check-in timestamp and policy snapshots
  - [x] Deduct operational gateway fees (2%) and cancellation penalties
  - [x] Process gateway refund with idempotency key
  - [x] Staff manual override workflow
- [x] Implement `BookingAndCommissionService`:
  - [x] Hierarchical resolution: `Property.CommissionRate` > `Owner.CustomCommissionRate` > `GlobalSetting (10%)`
  - [x] Immutable snapshot persistence onto `Booking` record
- [x] Implement `DamageClaimService`:
  - [x] Enforce 24-hour post-checkout filing deadline
  - [x] Itemized cost breakdown validation with evidence URL parsing
  - [x] Security deposit release or deduction adjudication
- [x] Implement `PartnerLeadService`:
  - [x] Public lead intake submission
  - [x] Staff review and encrypted 48h token generation for activation
- [x] Implement `IdVerificationService`:
  - [x] Private bucket document ingestion
  - [x] 15-minute expiring signed URL generation for staff review
  - [x] Verified Guest badge issuance (strictly withholding raw PII from Owners)

---

## Phase 4: Repositories & Data Access Layer
- [x] Update `IPropertyRepository` & `PropertyRepository`:
  - [x] Support latitude, longitude, formatted address
  - [x] Dual media query and persistence (`PropertyMedia`)
  - [x] Cancellation policy and security deposit fields
  - [x] Amenities master and property mapping
- [x] Update `IBookingRepository` & `BookingRepository`:
  - [x] Write immutable commission and cancellation snapshots
  - [x] Idempotency key lookup
  - [x] Security deposit state tracking
- [x] Create `IPartnerLeadRepository` & `PartnerLeadRepository`
- [x] Create `IDamageClaimRepository` & `DamageClaimRepository`
- [x] Create `IIdVerificationRepository` & `IdVerificationRepository`
- [x] Create `IRefundRepository` & `RefundRepository`

---

## Phase 5: Controllers & API Endpoints
- [x] Create `MediaController` for secure multipart upload + URL verification
- [x] Update `AccountController`:
  - [x] Restricted public registration to Client only
  - [x] Added token-based Owner activation endpoints (`/Account/ActivateHost`)
- [x] Add public `/Home/Partner` lead-intake action in `HomeController`
- [x] Update `BookingController`:
  - [x] Cancellation quote endpoint (`/Booking/GetCancellationQuote`)
  - [x] Cancellation execution with refund breakdown (`/Booking/CancelBooking`)
- [x] Update `AdminController` & `EmployeeController`:
  - [x] Dedicated refund operations queue & manual override (`RefundsQueue`, `OverrideRefund`)
  - [x] Damage claims adjudication (`DamageClaims`, `AdjudicateDamageClaim`)
  - [x] Dynamic Analytics with multi-dimensional filters (`DynamicAnalytics`, `ExportAnalyticsCsv`, `ExportAnalyticsExcel`)
  - [x] Partner Leads pipeline (`PartnerLeads`, `ApprovePartnerLead`, `RejectPartnerLead`)
  - [x] Client ID Verifications queue (`IdVerifications`, `ReviewIdVerification`, `GetDocumentSignedUrl`)
  - [x] Owner subscription tier and custom commission updates
- [x] Update `OwnerController`:
  - [x] Dual media property creation and edit (`CreateProperty`, `EditProperty`)
  - [x] Subscription tier overview & upgrade flow (`Subscriptions`, `Subscribe`)
  - [x] Damage claim submission within 24h of guest checkout (`DamageClaims`, `CreateClaim`)

---

## Phase 6: Front-End Razor Views & Interactive Components
- [x] Dual File/URL Media Uploader Component with drag-and-drop & URL switcher (`Views/Owner/CreateProperty.cshtml`, `Views/Owner/EditProperty.cshtml`, `Views/Admin/CreateProperty.cshtml`, `Views/Admin/EditProperty.cshtml`)
- [x] Google Maps Location Picker with coordinates & location detection
- [x] Client Cancellation Modal with transparent refund quote breakdown (`Views/Client/Index.cshtml`)
- [x] Partner With Us public lead-intake form & landing page (`Views/Home/Partner.cshtml`, `Views/Account/ActivateHost.cshtml`)
- [x] Admin Filterable Dynamic Analytics Dashboard with ClosedXML Excel and CSV exports (`Views/Admin/DynamicAnalytics.cshtml`)
- [x] Owner Damage Claim filing view with photo/video proof inputs (`Views/Owner/CreateClaim.cshtml`, `Views/Owner/DamageClaims.cshtml`)
- [x] Client ID Verification upload UI with privacy notice (`Views/Client/Index.cshtml`, `Views/Admin/IdVerifications.cshtml`, `Views/Employee/IdVerifications.cshtml`)
- [x] Role-based navigation dropdowns in `Views/Shared/_Layout.cshtml`

---

## Phase 7: Verification, Testing & Build Validation
- [x] Run `dotnet build --nologo` to verify zero compilation errors (Build succeeded with 0 errors!)
- [x] Magic-byte inspection & validation implemented
- [x] SSRF defense implemented with private IP & cloud metadata blocks
- [x] Tenant isolation and role authorization verified across all controller routes
- [x] Cancellation policy calculation math implemented across all 3 policy time-windows
- [x] Lead-intake to host activation email workflow complete
