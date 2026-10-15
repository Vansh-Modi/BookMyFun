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
- [ ] Update `DatabaseInitializer.cs`:
  - [ ] Add `FreeCancellationWindowHours`, `CancellationCutoffHours`, `CancellationFeeType`, `CancellationFeeValue` to `Properties`
  - [ ] Add `SecurityDepositAmount`, `CommissionRate`, `FormattedAddress`, `Latitude`, `Longitude` to `Properties`
  - [ ] Create `PropertyMedia` table supporting `{ type: 'file' | 'url', url: String, publicId?: String }`
  - [ ] Create `AmenitiesMaster` and `PropertyAmenitiesMapping` tables
  - [ ] Add cancellation policy and commission snapshots to `Bookings` (`FreeCancellationWindowHoursSnapshot`, `CancellationCutoffHoursSnapshot`, `CancellationFeeTypeSnapshot`, `CancellationFeeValueSnapshot`, `CommissionRateSnapshot`, `PlatformCommissionAmount`, `OwnerPayoutAmount`, `SecurityDepositAmount`, `SecurityDepositStatus`, `IdempotencyKey`)
  - [ ] Create `BookingRefunds` table
  - [ ] Create `PartnerLeads` table for host intake
  - [ ] Create `DamageClaims` table with 24h deadline & itemized evidence
  - [ ] Create `ClientIdVerifications` table for private document governance
- [ ] Update `Models/Entities/Entities.cs` to reflect all new tables and columns
- [ ] Update `Models/ViewModels/ViewModels.cs` with DTOs for:
  - [ ] Dual Media Upload Request & Response
  - [ ] Cancellation Quote & Confirmation
  - [ ] Partner Lead Intake
  - [ ] Damage Claim Submission & Adjudication
  - [ ] ID Verification Upload & Review
  - [ ] Filterable Analytics Query & Export

---

## Phase 2: Security Infrastructure, Middleware & Utilities
- [ ] Implement Custom Exception Hierarchy in `Services/Exceptions/AppExceptions.cs`
- [ ] Create Global Error Handling Middleware & Filter in `Services/Filters/GlobalExceptionFilter.cs`
- [ ] Implement `MediaStorageService`:
  - [ ] Magic-byte header validator (JPEG, PNG, WEBP, MP4, PDF)
  - [ ] UUID filename generator with directory-traversal sanitizer
  - [ ] Private vs. Public storage routing
  - [ ] SSRF defense on external URLs (HTTPS only, link-local / private IP ban)
- [ ] Implement Distributed In-Memory Rate Limiter in `Services/Security/RateLimitingMiddleware.cs`
- [ ] Configure Security HTTP Headers (Helmet equivalents: CSP, X-Frame-Options, X-Content-Type-Options) in `Program.cs`
- [ ] Verify Anti-Forgery tokens on all state-altering POST/PUT endpoints

---

## Phase 3: Core Service & Engine Implementations
- [ ] Implement `RefundEngineService`:
  - [ ] Compute real-time refund estimation based on check-in timestamp and policy snapshots
  - [ ] Deduct operational gateway fees and cancellation penalties
  - [ ] Process gateway refund with idempotency key
  - [ ] Staff manual override workflow
- [ ] Implement `CommissionEngineService`:
  - [ ] Hierarchical resolution: `Property.CommissionRate` > `Owner.CustomCommissionRate` > `GlobalSetting`
  - [ ] Immutable snapshot persistence onto `Booking` record
- [ ] Implement `DamageClaimService`:
  - [ ] Enforce 24-hour post-checkout filing deadline
  - [ ] Itemized cost breakdown validation with evidence URL parsing
  - [ ] Security deposit release or deduction adjudication
- [ ] Implement `PartnerLeadService`:
  - [ ] Public lead intake submission
  - [ ] Staff review and encrypted token generation for activation
- [ ] Implement `IdVerificationService`:
  - [ ] Private bucket document ingestion
  - [ ] 15-minute expiring signed URL generation for staff review
  - [ ] Verified Guest badge issuance (strictly withholding raw PII from Owners)

---

## Phase 4: Repositories & Data Access Layer
- [ ] Update `IPropertyRepository` & `PropertyRepository`:
  - [ ] Support latitude, longitude, formatted address
  - [ ] Dual media query and persistence
  - [ ] Cancellation policy and security deposit fields
- [ ] Update `IBookingRepository` & `BookingRepository`:
  - [ ] Write immutable commission and cancellation snapshots
  - [ ] Idempotency key lookup
  - [ ] Security deposit state tracking
- [ ] Create `ILeadRepository` & `LeadRepository`
- [ ] Create `IDamageClaimRepository` & `DamageClaimRepository`
- [ ] Create `IIdVerificationRepository` & `IdVerificationRepository`
- [ ] Create `IRefundRepository` & `RefundRepository`

---

## Phase 5: Controllers & API Endpoints
- [ ] Create `MediaController` for secure multipart upload + URL verification
- [ ] Update `AccountController`:
  - [ ] Remove public Owner registration
  - [ ] Add token-based Owner activation endpoint (`/Account/ActivateOwner?token=...`)
- [ ] Create `PartnerLeadController` for public `/Home/Partner` and Admin/Employee lead review
- [ ] Update `BookingController`:
  - [ ] Cancellation quote endpoint (AJAX)
  - [ ] Cancellation execution with refund breakdown
  - [ ] ID verification upload during checkout
- [ ] Update `AdminController` & `EmployeeController`:
  - [ ] Refund operations queue & manual override
  - [ ] Damage claims adjudication
  - [ ] Dynamic Analytics with multi-dimensional filters
  - [ ] User management with role tabs
  - [ ] Lead pipeline
- [ ] Update `OwnerController`:
  - [ ] Dual media property setup
  - [ ] Subscription tier overview & upgrade flow
  - [ ] Damage claim submission within 24h of guest checkout

---

## Phase 6: Front-End Razor Views & Interactive Components
- [ ] Dual File/URL Media Uploader Component with drag-and-drop & URL switcher
- [ ] Google Maps Location Picker with Places autocomplete and pin drag
- [ ] Client Cancellation Modal with transparent refund quote breakdown
- [ ] Partner With Us public lead-intake form & landing page
- [ ] Admin Filterable Dynamic Analytics Dashboard
- [ ] Owner Damage Claim filing view with photo/video proof inputs
- [ ] Client ID Verification upload UI with privacy notice

---

## Phase 7: Verification, Testing & Build Validation
- [ ] Run `dotnet build --nologo` to verify zero compilation errors
- [ ] Verify magic-byte validation with sample file tests
- [ ] Verify SSRF filter rejecting loopback/private IPs
- [ ] Verify tenant isolation (Owner A cannot access Owner B's listing)
- [ ] Verify cancellation calculation math across all 3 policy time-windows
- [ ] Verify lead-intake to host activation email workflow
