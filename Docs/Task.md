# BookMyFun — Master Platform Implementation & Verification Checklist

## Phase 0: Master System Documentation & Architectural Specification
- [x] Create comprehensive [`Docs/PRD.md`](file:///d:/Projects/BookMyFun/Docs/PRD.md) and mirror to [`PRD.md`](file:///d:/Projects/BookMyFun/PRD.md) covering all 15 platform modules and 5 user personas.
- [x] Create comprehensive [`Docs/Architecture.md`](file:///d:/Projects/BookMyFun/Docs/Architecture.md) and mirror to [`Architecture.md`](file:///d:/Projects/BookMyFun/Architecture.md) with system topology, 5-role RBAC matrix, service layer breakdown, and 6 Mermaid sequence diagrams.
- [x] Create comprehensive [`Docs/Database.md`](file:///d:/Projects/BookMyFun/Docs/Database.md) and mirror to [`Database.md`](file:///d:/Projects/BookMyFun/Database.md) with ER diagram, complete data dictionary for all 27 tables, and index strategy.
- [x] Create comprehensive [`Docs/Design.md`](file:///d:/Projects/BookMyFun/Docs/Design.md) and mirror to [`Design.md`](file:///d:/Projects/BookMyFun/Design.md) with design system tokens, anti-FOUC hydration script, and 7 interactive component wireframes.
- [x] Create comprehensive [`Docs/API.md`](file:///d:/Projects/BookMyFun/Docs/API.md) and mirror to [`API.md`](file:///d:/Projects/BookMyFun/API.md) with full catalog across all 9 controllers, HTTP verbs, payload parameters, and status responses.
- [x] Create comprehensive [`Docs/Rules.md`](file:///d:/Projects/BookMyFun/Docs/Rules.md) and mirror to [`Rules.md`](file:///d:/Projects/BookMyFun/Rules.md) with clean code mandates, OWASP Top 10 mitigation matrix, PCI-DSS rules, magic-byte tables, SSRF defense, and custom exception hierarchy.
- [x] Create comprehensive [`Docs/Task.md`](file:///d:/Projects/BookMyFun/Docs/Task.md) and mirror to [`Task.md`](file:///d:/Projects/BookMyFun/Task.md).
- [x] Create comprehensive [`Docs/Memory.md`](file:///d:/Projects/BookMyFun/Docs/Memory.md) and mirror to [`Memory.md`](file:///d:/Projects/BookMyFun/Memory.md).

---

## Phase 1: Database Persistence Layer & Schema Evolution
- [x] Implement [`Data/DBHelper.cs`](file:///d:/Projects/BookMyFun/Data/DBHelper.cs) with raw parameterized ADO.NET query execution and connection pooling.
- [x] Implement [`Data/DatabaseInitializer.cs`](file:///d:/Projects/BookMyFun/Data/DatabaseInitializer.cs) for idempotent startup migrations covering all 27 tables:
  - [x] Core Authentication: `Users`, `OtpVerifications`, `ClientIdVerifications`
  - [x] Property Catalog: `Properties`, `PropertyTypes`, `Occasions`, `AmenitiesMaster`, `PropertyAmenitiesMapping`, `PropertyOccasions`, `PropertyMedia`, `PropertyImages`
  - [x] Booking & Billing: `Bookings`, `Payments`, `BookingRefunds`, `DamageClaims`, `FinancialLedger`
  - [x] Host Subscriptions & Onboarding: `SubscriptionPlans`, `OwnerSubscriptions`, `PropertyListingPayments`, `PartnerLeads`, `OwnerRequests`
  - [x] Support, Auditing & Config: `CustomerReviews`, `ContactUs`, `AuditLogs`, `SiteSettings`, `ApiConfigurations`
  - [x] Event Décor Module: `EventPackages`
- [x] Add immutable policy and commission snapshots to `Bookings` table.
- [x] Add event décor tracking columns to `Bookings`: `IsEventBooking`, `EventPackageID`, `CustomEventNotes`, `EventDate`, `DecoratorAssignedID`, `SetupStatus`, `SetupProofMediaUrls`, `EventPackagePriceSnapshot`.
- [x] Seed initial 12 categorized amenities with Bootstrap Icons in `AmenitiesMaster`.
- [x] Seed 4 initial celebration packages and test personas (including `eventmanager@bookmyfun.com`) in `DatabaseSeeder.cs`.

---

## Phase 2: Security Infrastructure, Middleware & Exception Handling
- [x] Define typed domain exception hierarchy in [`Services/Exceptions/AppExceptions.cs`](file:///d:/Projects/BookMyFun/Services/Exceptions/AppExceptions.cs) (`AppException`, `ValidationException`, `ForbiddenException`, `NotFoundException`, `SecurityViolationException`, `ConflictException`, `BusinessRuleException`, `PaymentGatewayException`).
- [x] Implement [`Services/Filters/GlobalExceptionFilter.cs`](file:///d:/Projects/BookMyFun/Services/Filters/GlobalExceptionFilter.cs) for dual JSON API / Razor view error handling.
- [x] Implement [`Services/Security/SecurityHeadersMiddleware.cs`](file:///d:/Projects/BookMyFun/Services/Security/SecurityHeadersMiddleware.cs) with CSP, HSTS, X-Frame-Options: DENY, X-Content-Type-Options: nosniff.
- [x] Implement [`Services/Security/RateLimitingMiddleware.cs`](file:///d:/Projects/BookMyFun/Services/Security/RateLimitingMiddleware.cs) enforcing 100 requests per minute per IP.
- [x] Implement [`Services/MediaStorageService.cs`](file:///d:/Projects/BookMyFun/Services/MediaStorageService.cs):
  - [x] Magic-byte binary verification for JPEG, PNG, WEBP, MP4, MOV, and PDF.
  - [x] UUID filename generation and path traversal sanitization.
  - [x] Storage segregation: Public `/wwwroot/uploads/media/` vs. Private `/App_Data/PrivateDocuments/`.
  - [x] SSRF defense validating HTTPS scheme and blocking loopback, RFC 1918 private IPs, and cloud metadata (`169.254.169.254`).
  - [x] HMAC-SHA256 15-minute expiring token generator for private KYC documents.
- [x] Configure Cookie Authentication with `HttpOnly = true`, `SameSite = SameSiteMode.Strict`, and sliding expiration.

---

## Phase 3: Core Domain Services & Business Engines
- [x] Implement [`Services/RefundEngineService.cs`](file:///d:/Projects/BookMyFun/Services/RefundEngineService.cs):
  - [x] Real-time refund calculation based on check-in timestamp and policy snapshots.
  - [x] Automatic deduction of 2% payment gateway fee and cancellation penalties.
  - [x] Razorpay refund API dispatch with idempotency keys.
  - [x] Operational staff manual override workflow.
- [x] Implement [`Services/BookingAndCommissionService.cs`](file:///d:/Projects/BookMyFun/Services/BookingAndCommissionService.cs):
  - [x] Three-tier commission resolution (`Property.CommissionRate ?? Owner.CustomCommissionRate ?? GlobalSettings.10%`).
  - [x] Multi-duration pricing calculations (Full Day, Morning/Evening slots, Multi-day).
  - [x] Immutable snapshotting of pricing and commission onto `Bookings` record.
- [x] Implement [`Services/DamageClaimService.cs`](file:///d:/Projects/BookMyFun/Services/DamageClaimService.cs):
  - [x] Strict 24-hour post-checkout filing deadline verification.
  - [x] Itemized cost breakdown validation and photographic evidence linking.
  - [x] Adjudication logic to transfer claimed damages to host and release remaining deposit to guest.
- [x] Implement [`Services/PartnerLeadService.cs`](file:///d:/Projects/BookMyFun/Services/PartnerLeadService.cs):
  - [x] Public intake form processing.
  - [x] Staff qualification and triage.
  - [x] 48-hour encrypted HMAC-SHA256 activation token generation and email dispatch.
- [x] Implement [`Services/IdVerificationService.cs`](file:///d:/Projects/BookMyFun/Services/IdVerificationService.cs):
  - [x] Government ID intake and metadata recording.
  - [x] Expiring URL generation for staff review.
  - [x] Verified Guest badge assignment with absolute denial of raw ID access to hosts.
- [x] Implement [`Services/PaymentGatewayService.cs`](file:///d:/Projects/BookMyFun/Services/PaymentGatewayService.cs) for Razorpay order generation and webhook signature verification.
- [x] Implement [`Services/CommunicationService.cs`](file:///d:/Projects/BookMyFun/Services/CommunicationService.cs) for transactional emails and OTP dispatches.
- [x] Implement [`Services/SiteSettingsService.cs`](file:///d:/Projects/BookMyFun/Services/SiteSettingsService.cs) for dynamic platform settings and encrypted secrets.

---

## Phase 4: Repositories & Data Access Layer
- [x] [`Repositories/UserRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/UserRepository.cs) — Credential lookup, role management, custom commission rates.
- [x] [`Repositories/PropertyRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/PropertyRepository.cs) — Property CRUD, dual-mode media persistence, amenities mapping, tenant scoping.
- [x] [`Repositories/BookingRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/BookingRepository.cs) — Availability checks, reservation creation with snapshots, event joins, status updates.
- [x] [`Repositories/EventPackageRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/EventPackageRepository.cs) — Décor package CRUD, assigned setups retrieval, milestone progression, decorator lookups.
- [x] [`Repositories/RefundRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/RefundRepository.cs) — Refund ledger tracking and status management.
- [x] [`Repositories/PartnerLeadRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/PartnerLeadRepository.cs) — Lead pipeline persistence and activation token lookup.
- [x] [`Repositories/DamageClaimRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/DamageClaimRepository.cs) — Damage claim persistence and adjudication status.
- [x] [`Repositories/IdVerificationRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/IdVerificationRepository.cs) — Private document key and verification state tracking.
- [x] [`Repositories/SubscriptionRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/SubscriptionRepository.cs) — Host subscription plans and validity checking.
- [x] [`Repositories/FinancialRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/FinancialRepository.cs) — Double-entry financial ledger operations.
- [x] [`Repositories/OtpRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/OtpRepository.cs) — OTP issuance, expiration, and consumption.
- [x] [`Repositories/ConfigRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/ConfigRepository.cs) — API credentials and site parameters.
- [x] [`Repositories/ContactRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/ContactRepository.cs) — Customer support message triage.
- [x] [`Repositories/OwnerRequestRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/OwnerRequestRepository.cs) — Trial period listing requests.

---

## Phase 5: Controllers & Endpoints (9 Controllers)
- [x] [`Controllers/AccountController.cs`](file:///d:/Projects/BookMyFun/Controllers/AccountController.cs) — Login, client registration, host activation via token, role redirections (including `EventManager` -> `/EventManager/Dashboard`).
- [x] [`Controllers/HomeController.cs`](file:///d:/Projects/BookMyFun/Controllers/HomeController.cs) — Public catalog, search, details, "List Your Farm" intake, contact.
- [x] [`Controllers/BookingAndPropertiesController.cs`](file:///d:/Projects/BookMyFun/Controllers/BookingAndPropertiesController.cs) — Reservation checkout with event package add-on, dynamic pricing, payment verification, cancellation quote & confirmation.
- [x] [`Controllers/EventManagerController.cs`](file:///d:/Projects/BookMyFun/Controllers/EventManagerController.cs) — Dashboard KPIs, package CRUD, active toggling, assigned setup tracking, and milestone advancement with photo/video proofs.
- [x] [`Controllers/ClientController.cs`](file:///d:/Projects/BookMyFun/Controllers/ClientController.cs) — Guest dashboard, booking history, KYC ID upload, review submission.
- [x] [`Controllers/OwnerController.cs`](file:///d:/Projects/BookMyFun/Controllers/OwnerController.cs) — Host dashboard, listing CRUD with dual media, calendar, 24h damage claims, subscriptions.
- [x] [`Controllers/EmployeeController.cs`](file:///d:/Projects/BookMyFun/Controllers/EmployeeController.cs) — Onboarding leads pipeline, guest ID verification, refunds queue, damage claims adjudication.
- [x] [`Controllers/AdminController.cs`](file:///d:/Projects/BookMyFun/Controllers/AdminController.cs) — Master analytics (Property GMV vs Event GMV), ClosedXML Excel export, CSV export, financial ledger, user governance, audit trail.
- [x] [`Controllers/MediaController.cs`](file:///d:/Projects/BookMyFun/Controllers/MediaController.cs) — Multipart upload, SSRF URL verification, expiring private KYC retrieval.

---

## Phase 6: User Interface, Razor Views & Theming
- [x] Configure Light & Dark mode tokens in [`wwwroot/css/site.css`](file:///d:/Projects/BookMyFun/wwwroot/css/site.css) with anti-FOUC inline hydration script in [`Views/Shared/_Layout.cshtml`](file:///d:/Projects/BookMyFun/Views/Shared/_Layout.cshtml).
- [x] Create responsive public landing page with search, featured farms, and social proof in [`Views/Home/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Home/Index.cshtml).
- [x] Implement public "Partner With Us / List Your Farm" lead intake in [`Views/Home/Partner.cshtml`](file:///d:/Projects/BookMyFun/Views/Home/Partner.cshtml).
- [x] Implement Host Activation interface in [`Views/Account/ActivateHost.cshtml`](file:///d:/Projects/BookMyFun/Views/Account/ActivateHost.cshtml).
- [x] Implement Dual-Mode Media Uploader UI in [`Views/Owner/AddProperty.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/AddProperty.cshtml) and [`Views/Owner/EditProperty.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/EditProperty.cshtml).
- [x] Implement Event Décor selection switch and live pricing update in [`Views/Booking/Checkout.cshtml`](file:///d:/Projects/BookMyFun/Views/Booking/Checkout.cshtml).
- [x] Implement Cancellation & Refund Transparency Modal in [`Views/Client/MyBookings.cshtml`](file:///d:/Projects/BookMyFun/Views/Client/MyBookings.cshtml).
- [x] Implement 24-Hour Damage Claim submission portal in [`Views/Owner/FileDamageClaim.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/FileDamageClaim.cshtml).
- [x] Implement Event Manager portal views:
  - [x] [`Views/EventManager/Dashboard.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/Dashboard.cshtml)
  - [x] [`Views/EventManager/Packages.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/Packages.cshtml)
  - [x] [`Views/EventManager/CreatePackage.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/CreatePackage.cshtml)
  - [x] [`Views/EventManager/EditPackage.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/EditPackage.cshtml)
  - [x] [`Views/EventManager/AssignedSetups.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/AssignedSetups.cshtml)
- [x] Implement shared operational queues for Admin and Employee:
  - [x] [`Views/Admin/OnboardingLeads.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/OnboardingLeads.cshtml)
  - [x] [`Views/Admin/IdVerifications.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/IdVerifications.cshtml)
  - [x] [`Views/Admin/RefundsQueue.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/RefundsQueue.cshtml)
  - [x] [`Views/Admin/DamageClaimsQueue.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/DamageClaimsQueue.cshtml)
- [x] Implement Dynamic Analytics Dashboard with Property GMV vs Event GMV and ClosedXML Excel / CSV exports in [`Views/Admin/DynamicAnalytics.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/DynamicAnalytics.cshtml).

---

## Phase 7: Clean Code & Codebase Hygiene Audit
- [x] Purge all historical commented-out code blocks in [`Data/DatabaseInitializer.cs`](file:///d:/Projects/BookMyFun/Data/DatabaseInitializer.cs) and [`Data/DatabaseSeeder.cs`](file:///d:/Projects/BookMyFun/Data/DatabaseSeeder.cs).
- [x] Eliminate unreferenced variables and unused namespace directives.
- [x] Fix compiler warnings (`CS1061`, `CS8601`) to achieve clean build output.
- [x] Verify DRY compliance across ADO.NET reader helpers and models.

---

## Phase 8: Verification & Build Quality Assurance
- [x] Compile solution using `dotnet build --nologo` — Verify 0 project errors and 0 project warnings.
- [x] Validate all 15 functional modules against actual running code.
- [x] Verify tenant isolation and RBAC constraints across all 5 dashboards.
- [x] Ensure all documentation cross-links resolve to existing files.

---

## Phase 9: Systematic Resolution of 13 Multi-Dashboard User Issues
- [x] **Issue 1 (Event Manager Dashboard & Order Flow):** Built complete décor management and order claiming system in [`Controllers/EventManagerController.cs`](file:///d:/Projects/BookMyFun/Controllers/EventManagerController.cs) (`AcceptOrder`, `ApproveOrder`, `AssignDecorator`, `UpdateSetupStatus`) accessible by `EventManager`, `Employee`, and `Admin`. Added filter tabs and assignment modals in [`Views/EventManager/Dashboard.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/Dashboard.cshtml).
- [x] **Issue 2 (Payment/Refund Transparency & Universal Damage Reporting):**
  - Added live transaction & refund audit endpoint `/Booking/GetPaymentAndRefundDetails` in [`Controllers/BookingAndPropertiesController.cs`](file:///d:/Projects/BookMyFun/Controllers/BookingAndPropertiesController.cs) and interactive `#paymentDetailsModal` in [`Views/Client/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Client/Index.cshtml).
  - Added "File Damage Claim" button and modal `#newClaimModal` in [`Views/Owner/DamageClaims.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/DamageClaims.cshtml).
  - Added "Log Damage Incident" button and modal `#logDamageClaimModal` for staff in [`Views/Admin/DamageClaims.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/DamageClaims.cshtml) and [`Views/Employee/DamageClaims.cshtml`](file:///d:/Projects/BookMyFun/Views/Employee/DamageClaims.cshtml).
- [x] **Issue 3 (Dynamic ID Verification Status):** Fixed string matching bug in [`Services/IdVerificationService.cs`](file:///d:/Projects/BookMyFun/Services/IdVerificationService.cs) so statuses transition dynamically between `"Verified"` and `"Rejected"`.
- [x] **Issue 4 (Government ID Proof Only at Booking Checkout):** Added KYC document upload inputs inside [`Views/Booking/Checkout.cshtml`](file:///d:/Projects/BookMyFun/Views/Booking/Checkout.cshtml), securely bound to private storage in `ConfirmBooking`, and clarified non-blocking guest account policies in [`Views/Client/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Client/Index.cshtml).
- [x] **Issue 5 (Status Filters across Dashboards):**
  - Added property status filters (`All`, `Approved & Live`, `Waiting Review`, `Rejected`) in [`Views/Admin/ManageProperties.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/ManageProperties.cshtml) (and Employee) and [`Views/Owner/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/Index.cshtml).
  - Added booking status filters (`All`, `Confirmed`, `Cancelled`, `Refunded`, `Damage Claimed`) in [`Views/Owner/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/Index.cshtml).
- [x] **Issue 6 (Owner Dashboard Buttons & Listing Quota):** Fixed quota lockout in [`Repositories/SubscriptionRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/SubscriptionRepository.cs) and audited all buttons in [`Views/Owner/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/Index.cshtml) ("List New Farm", "Damage Claims", "Subscriptions", "Edit", "Report Damage").
- [x] **Issue 7 (Delete Owner Button):** Secured `<form asp-controller="@controllerName" asp-action="DeleteOwner">` in [`Views/Admin/ManageOwners.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/ManageOwners.cshtml) with `@Html.AntiForgeryToken()` and implemented safe soft-deactivation in [`Repositories/UserRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/UserRepository.cs) when foreign key constraints exist.
- [x] **Issue 8 (Support Accountability):** Attached staff responder information (`RespondedByUserName`, `RespondedByUserRole`, `RespondedAt`, `ResponseNotes`) across [`Repositories/ContactRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/ContactRepository.cs), controllers, and [`Views/Admin/SupportQueries.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/SupportQueries.cshtml).
- [x] **Issue 9 (ID Verification Document Streaming in Employee Dashboard):** Fixed document streaming in [`Controllers/MediaController.cs`](file:///d:/Projects/BookMyFun/Controllers/MediaController.cs) and embedded an interactive modal preview in [`Views/Admin/IdVerifications.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/IdVerifications.cshtml).
- [x] **Issue 10 (Owner Approval Password Generation & Dispatch):** Auto-generated `Host@<6-digits>` password in [`Services/PartnerLeadService.cs`](file:///d:/Projects/BookMyFun/Services/PartnerLeadService.cs), activated user, dispatched credentials via Email + WhatsApp, and rendered credentials in `TempData["Success"]`.
- [x] **Issue 11 (Owner Booking Financial Breakdown):** Added transparent itemized financial breakdown on each booking card in [`Views/Owner/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/Index.cshtml) (Gross Amount, 10% Platform Fee, Host Net Earning, Deposit status, and cancellation payouts).
- [x] **Issue 12 (Universal Profile Page for All Logins):** Implemented `Profile` (GET & POST) and `ChangePassword` (POST) in [`Controllers/AccountController.cs`](file:///d:/Projects/BookMyFun/Controllers/AccountController.cs) and created universal [`Views/Account/Profile.cshtml`](file:///d:/Projects/BookMyFun/Views/Account/Profile.cshtml) supporting all 5 roles.
- [x] **Issue 13 (User Alignment):** Strict rule maintained to not introduce unrequested extra features without prior user notification.
- [x] **Issue 14 (Dedicated Event Manager Login & Separate Dashboard):**
  - Created dedicated Host Partner Sign In portal in [`Views/Account/OwnerLogin.cshtml`](file:///d:/Projects/BookMyFun/Views/Account/OwnerLogin.cshtml) (`/Account/OwnerLogin` and `/Owner/Login`) with 1-click demo auto-fill (`owner@bookmyfun.com` / `Owner@123`).
  - Created dedicated Event Manager Sign In portal in [`Views/Account/EventManagerLogin.cshtml`](file:///d:/Projects/BookMyFun/Views/Account/EventManagerLogin.cshtml) (`/Account/EventManagerLogin` and `/EventManager/Login`) designed identical to Owner's with 1-click demo auto-fill (`eventmanager@bookmyfun.com` / `Event@123`).
  - Built dedicated separate Event Manager Dashboard in [`Views/EventManager/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/Index.cshtml) and [`Views/EventManager/Dashboard.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/Dashboard.cshtml) conforming to the standardized design system (`dash-page-header`, `dash-stat-grid`, `dash-stat-card`, order lifecycle tabs, package management, photo proofs modal).
  - Enhanced [`Views/Account/Login.cshtml`](file:///d:/Projects/BookMyFun/Views/Account/Login.cshtml) and [`Views/Shared/_Layout.cshtml`](file:///d:/Projects/BookMyFun/Views/Shared/_Layout.cshtml) with portal switcher pills and navigation links across all roles.


