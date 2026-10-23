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
- [x] **Issue 15 (Admin Startup Session Cleanup & Admin Login Schema Fix):**
  - Configured named cookie `BookMyFun.AuthCookie` in [`Program.cs`](file:///d:/Projects/BookMyFun/Program.cs) with `SameSiteMode.Lax` and 8-hour lifetime, immediately invalidating stale persistent `.AspNetCore.Cookies` from previous debugging runs so the application starts unauthenticated.
  - Enabled dual-verb `[AcceptVerbs("GET", "POST")]` with `[IgnoreAntiforgeryToken]` on [`Controllers/AccountController.cs`](file:///d:/Projects/BookMyFun/Controllers/AccountController.cs) `Logout()` with explicit cookie deletion to prevent HTTP 405 errors on link/GET navigation.
  - Added idempotent schema migration for missing `IsActive` column on `Users` in [`Data/DatabaseInitializer.cs`](file:///d:/Projects/BookMyFun/Data/DatabaseInitializer.cs) and updated [`Repositories/UserRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/UserRepository.cs) (`CreateUserAsync`, `UpdateUserAsync`) to eliminate `SqlException: Invalid column name 'IsActive'` when logging into the Admin panel.
  - Added automated xUnit regression tests in [`BookMyFun.Tests/UnitTest1.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/UnitTest1.cs) verifying admin credentials and administrative dashboard query execution.
- [x] **Issue 16 (Owner & Event Manager Approval Password Auto-Generation & Dual WhatsApp/Email Dispatch):**
  - Integrated Meta Cloud API / custom WhatsApp gateway payload format into [`Services/CommunicationService.cs`](file:///d:/Projects/BookMyFun/Services/CommunicationService.cs) (`SendWhatsAppMessageAsync`) with phone number normalization (stripping spaces, symbols, and prefixing country code `91` for 10-digit mobile numbers) and live configuration lookup from `ApiConfigurations`.
  - Added `GetRequestByIdAsync(int requestId)` and enhanced `ApproveOwnerRequestAsync(..., string? newPasswordHash = null)` in [`Repositories/OwnerRequestRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/OwnerRequestRepository.cs) to atomically set hashed initial passwords on `Users.PasswordHash`.
  - Implemented `ApproveOwnerRequestAsync` in [`Services/PartnerLeadService.cs`](file:///d:/Projects/BookMyFun/Services/PartnerLeadService.cs), generating secure `Host@<6-digits>` passwords, hashing them, and awaiting dual-channel notifications via both Email and WhatsApp API containing the Account ID (`UserID`), Login Email/ID, and auto-generated Password.
  - Updated `ApproveLeadAsync` in [`Services/PartnerLeadService.cs`](file:///d:/Projects/BookMyFun/Services/PartnerLeadService.cs) to await Email and WhatsApp dispatches, returning full partner metadata and credentials.
  - Updated `ApprovePartnerLead` and `ApproveOwnerRequest` in [`Controllers/AdminController.cs`](file:///d:/Projects/BookMyFun/Controllers/AdminController.cs) and [`Controllers/EmployeeController.cs`](file:///d:/Projects/BookMyFun/Controllers/EmployeeController.cs) to unpack credentials, log staff accountability, and display the Account ID and temporary password in `TempData["Success"]`.
  - Added comprehensive automated xUnit tests in [`BookMyFun.Tests/UnitTest1.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/UnitTest1.cs) verifying lead and owner request approval, credential generation, and authentication verification.





- [x] **Issue 17 (Razorpay Payment Gateway, WhatsApp Multi-Stakeholder Alerts, Host/Decorator Financial Segregation & Dedicated Event Manager Financials Page):**
  - Configured `Razorpay` (`KeyId`, `KeySecret`) and `WhatsApp` (`ApiKey`, `PhoneNumberId`, `Endpoint`) credentials in [`appsettings.json`](file:///d:/Projects/BookMyFun/appsettings.json).
  - Enhanced [`Services/PaymentGatewayService.cs`](file:///d:/Projects/BookMyFun/Services/PaymentGatewayService.cs) (`RazorpayPaymentService`) with live HTTP Basic Auth API call to `https://api.razorpay.com/v1/orders`, verifiable sandbox tokens (`order_<16 chars>`), and HMAC-SHA256 signature verification.
  - Implemented `/Booking/VerifyPayment` endpoint in [`Controllers/BookingAndPropertiesController.cs`](file:///d:/Projects/BookMyFun/Controllers/BookingAndPropertiesController.cs) validating Razorpay payment signature and transitioning bookings to `Confirmed`.
  - Integrated official Razorpay Checkout SDK (`https://checkout.razorpay.com/v1/checkout.js`) in [`Views/Booking/Payment.cshtml`](file:///d:/Projects/BookMyFun/Views/Booking/Payment.cshtml) with instant popup modal and signature form dispatch.
  - Implemented multi-stakeholder alert dispatching in [`Services/CommunicationService.cs`](file:///d:/Projects/BookMyFun/Services/CommunicationService.cs) with `SendOwnerBookingAlertAsync` (alerting hosts with stay dates, property gross, and clear décor add-on notice) and `SendEventManagerSetupAlertAsync` (alerting event decorators with venue, package name, allocated décor fee, and event date).
  - Segregated property stay pricing from event décor package fees in `ConfirmBooking` ([`Controllers/BookingAndPropertiesController.cs`](file:///d:/Projects/BookMyFun/Controllers/BookingAndPropertiesController.cs)): host commission is strictly calculated on `propertyStayAmount`, `OwnerPayoutAmount` reflects property earnings without décor distortions, and `EventPackagePriceSnapshot` is cleanly preserved for the decorator.
  - Updated [`Views/Owner/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/Index.cshtml) with itemized financial cards (Property Stay Gross, Platform Commission, Host Net Payout) and a prominent Décor Add-on callout explaining that the client's décor fee is allocated 100% to the Event Decorator Studio and not deducted from the host's revenue.
  - Created dedicated Event Manager Financials page at `/EventManager/Financials` backed by `EventManagerFinancialsViewModel` in [`Controllers/EventManagerController.cs`](file:///d:/Projects/BookMyFun/Controllers/EventManagerController.cs) and [`Views/EventManager/Financials.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/Financials.cshtml) with GMV KPI cards, Settled vs Escrow earnings metrics, status/date filters, itemized ledger table, and escrow protection guarantee banner.
  - Added 3 automated xUnit regression tests in [`BookMyFun.Tests/UnitTest1.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/UnitTest1.cs) verifying Razorpay order creation/signature validation, strict host/decorator financial segregation, and WhatsApp multi-party alert dispatches. All 7 tests passing.

- [x] **Issue 18 (Luxury Resort UI/UX Redesign — Airbnb Luxe / Plum Guide Style):**
  - Integrated the exact luxury design system tokens into [`wwwroot/css/site.css`](file:///d:/Projects/BookMyFun/wwwroot/css/site.css) and [`Views/Properties/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Properties/Index.cshtml) (`--bg-primary: #F8FAFC`, `--bg-card: #FFFFFF`, `--brand-navy: #0B4F6C`, `--brand-sky: #01BAEF`, `--text-main: #0F172A`, `--text-muted: #64748B`, `--border-subtle: #E2E8F0`, `--brand-green: #2E933C`, `--brand-gold: #F59E0B`).
  - Restructured [`Views/Properties/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Properties/Index.cshtml) with a luxury hero header, handpicked badge, and live property counter.
  - Redesigned search card with generous whitespace, subtle `1px solid #E2E8F0` border, and soft ambient shadow (`0 10px 30px -5px rgba(11, 79, 108, 0.05)`).
  - Implemented horizontal quick-navigation category pills using Palm Green (`#2E933C`) for active indicators.
  - Upgraded property cards to 16px rounded corners (`overflow: hidden`) with micro-interactions: card hover lift (`translateY(-4px)`), elevated ambient shadow (`0 20px 35px -5px rgba(11, 79, 108, 0.12)`), and smooth image zoom (`scale(1.03)`).
  - Applied typography hierarchy: bold headings with tight letter-spacing (`-0.02em`) in Deep Navy (`#0B4F6C`), muted secondary metadata in `#64748B`, and rating star badges in Warm Gold (`#F59E0B`).
  - Preserved 100% of existing backend data bindings, variable names, routing parameters, and form attributes without breaking changes.

- [x] **Issue 19 (End-to-End System Testing, Real Email OTP Verification & Full Performance Optimization):**
  - Tested and validated real email OTP generation and dispatch using live Gmail SMTP credentials (`showsgarage@gmail.com`). Verified that dynamic 6-digit random OTPs are stored in `OtpVerifications`, dispatched via SMTP, and validated during registration without mock or bypass codes.
  - Implemented end-to-end integration tests in [`BookMyFun.Tests/EndToEndWorkflowTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/EndToEndWorkflowTests.cs) covering all major platform modules:
    1. Flexible booking checkout with event decor package.
    2. Razorpay order generation and HMAC-SHA256 signature verification.
    3. Multi-stakeholder real-time alerts (Guest, Host, Decorator).
    4. Cancellation quote calculation and refund execution.
    5. Event Manager decor catalog curation, order advancement, and financial metrics.
    6. Host damage claim submission and staff adjudication against security deposits.
    7. Customer support inquiry handling with staff accountability tracking.
    8. Client KYC ID document submission and administrative review.
  - Resolved `ContactUs` database check constraint violation (`CK__ContactUs__Statu__5FD33367`), standardizing valid status transitions (`New`, `Responded`).
  - Added thread-safe in-memory caching to [`Services/SiteSettingsService.cs`](file:///d:/Projects/BookMyFun/Services/SiteSettingsService.cs) with 15-minute TTL, eliminating repeated database roundtrips on every page render.
  - Replaced render-blocking `@import` in [`wwwroot/css/site.css`](file:///d:/Projects/BookMyFun/wwwroot/css/site.css) with preconnected `<link rel="preconnect">` and `<link rel="stylesheet">` tags in [`Views/Shared/_Layout.cshtml`](file:///d:/Projects/BookMyFun/Views/Shared/_Layout.cshtml).
  - Configured response compression (`AddResponseCompression`, `UseResponseCompression`) for Gzip and Brotli payload delivery in [`Program.cs`](file:///d:/Projects/BookMyFun/Program.cs).
  - Added `loading="lazy"` and `decoding="async"` across property card catalog and landing page image tags.
  - Verified 100% test pass rate across **13 comprehensive unit and integration tests** with **0 build warnings and 0 errors**.
- [x] **Task 1 (Storage Optimization):** Implemented dual client-side and server-side image & asset optimization pipeline. Created [`Services/ImageOptimizationService.cs`](file:///d:/Projects/BookMyFun/Services/ImageOptimizationService.cs) with stream-level bicubic downscaling and quality compression, updated [`Services/MediaStorageService.cs`](file:///d:/Projects/BookMyFun/Services/MediaStorageService.cs) with magic-byte validation for modern formats (WebP and AVIF), created client-side [`wwwroot/js/imageOptimizer.js`](file:///d:/Projects/BookMyFun/wwwroot/js/imageOptimizer.js) hooked into all upload forms, and verified with 7 automated unit tests. All tests passing with 0 build warnings and 0 errors.
- [x] **Task 14 (OTP Email Dispatch Bug):** Debugged and fixed email dispatch service responsible for verification/login OTPs. Created [`Services/EmailQueueService.cs`](file:///d:/Projects/BookMyFun/Services/EmailQueueService.cs) with background channel trigger queue and hosted service dispatcher eliminating scoped `ObjectDisposedException` on asynchronous dispatches. Enhanced [`Services/CommunicationService.cs`](file:///d:/Projects/BookMyFun/Services/CommunicationService.cs) with credential normalization (stripping internal spaces from Gmail app passwords), recipient address sanitization, timeout safeguards, and purpose-tailored responsive HTML templates. Updated [`Controllers/AccountController.cs`](file:///d:/Projects/BookMyFun/Controllers/AccountController.cs) to verify email dispatch outcome and return explicit failure responses on delivery errors. Verified with 5 automated unit tests (12 tests total across suite). All tests passing with 0 build warnings and 0 errors.
- [x] **Task 13 (Login UI Refinement):** Cleaned up authentication UI and standardized on email-only login. Removed "User ID" and "Email or User ID" labels and placeholders across [`Views/Account/Login.cshtml`](file:///d:/Projects/BookMyFun/Views/Account/Login.cshtml), converted input to `type="email"`, added `autocomplete="email"`, and updated icon to `bi-envelope`. Updated [`Models/ViewModels/ViewModels.cs`](file:///d:/Projects/BookMyFun/Models/ViewModels/ViewModels.cs) with `[EmailAddress]` validation and updated `[Display(Name = "Email Address")]` and `[Required]`. Sanitized controller error messages in [`Controllers/AccountController.cs`](file:///d:/Projects/BookMyFun/Controllers/AccountController.cs) and cleaned up OAuth button in [`Views/Account/ExternalLoginPrompt.cshtml`](file:///d:/Projects/BookMyFun/Views/Account/ExternalLoginPrompt.cshtml). Created [`BookMyFun.Tests/LoginUiRefinementTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/LoginUiRefinementTests.cs) and integrated into master test suite with 5 automated tests (17 total tests). All passing with 0 compiler warnings and 0 errors.
- [x] **Task 2 (Event Manager Image Upload):** Updated event manager portfolio/work showcase so images can be directly uploaded via file input in addition to providing external URLs. Enhanced [`Models/ViewModels/ViewModels.cs`](file:///d:/Projects/BookMyFun/Models/ViewModels/ViewModels.cs) (`EventPackageCreateViewModel`, `EventPackageEditViewModel` with `ImageFile`, and `SetupStatusUpdateViewModel` with `ProofFiles`). Updated [`Controllers/EventManagerController.cs`](file:///d:/Projects/BookMyFun/Controllers/EventManagerController.cs) (`CreatePackage`, `EditPackage`, `UpdateSetupStatus`) to stream direct uploads through `IMediaStorageService` with magic-byte validation and bicubic optimization while maintaining external URL support. Authorized `EventManager` role in [`Controllers/MediaController.cs`](file:///d:/Projects/BookMyFun/Controllers/MediaController.cs). Upgraded [`Views/EventManager/CreatePackage.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/CreatePackage.cshtml), [`Views/EventManager/EditPackage.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/EditPackage.cshtml), [`Views/EventManager/AssignedSetups.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/AssignedSetups.cshtml), [`Views/EventManager/Dashboard.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/Dashboard.cshtml), and [`Views/EventManager/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/Index.cshtml) with `multipart/form-data`, file inputs, and live image previews. Created [`BookMyFun.Tests/EventManagerImageUploadTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/EventManagerImageUploadTests.cs) with 6 comprehensive unit tests (23 total tests). All passing with 0 compiler warnings and 0 errors.
- [x] **Task 15 (Google OAuth Config):** Updated the Google OAuth flow to use the currently active project email (`showsgarage@gmail.com`) and project credentials for authentication. Configured `Authentication:Google:ProjectEmail` in [`appsettings.json`](file:///d:/Projects/BookMyFun/appsettings.json) and enabled dynamic database lookup via `ApiConfigurations`. Upgraded `ApiConfigurations` check constraint in [`Data/DatabaseInitializer.cs`](file:///d:/Projects/BookMyFun/Data/DatabaseInitializer.cs) and [`Database_Setup_MonsterASP.sql`](file:///d:/Projects/BookMyFun/Database_Setup_MonsterASP.sql) to support `'Google'`, seeding active project credentials with `showsgarage@gmail.com`. Added Google OAuth administration tab in [`Views/Admin/Settings.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/Settings.cshtml) for managing Client ID, Client Secret, and project email parameters. Updated [`Controllers/AccountController.cs`](file:///d:/Projects/BookMyFun/Controllers/AccountController.cs) (`ExternalLogin` and `DirectGoogleLogin`) to resolve project credentials from `IConfigRepository` and `IConfiguration`, populate [`Models/ViewModels/ViewModels.cs`](file:///d:/Projects/BookMyFun/Models/ViewModels/ViewModels.cs) (`ExternalLoginPromptViewModel`), and automatically authenticate and link users via Google claims. Overhauled [`Views/Account/ExternalLoginPrompt.cshtml`](file:///d:/Projects/BookMyFun/Views/Account/ExternalLoginPrompt.cshtml) to eliminate legacy demo placeholders (`demo.google@bookmyfun.in`), add an active project account badge, provide 1-click project account auto-fill (`showsgarage@gmail.com`), and document Google Cloud Console setup. Created [`BookMyFun.Tests/GoogleOAuthConfigTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/GoogleOAuthConfigTests.cs) with 5 automated unit tests integrated into master test suite (28 total tests). All passing with 0 compiler warnings and 0 errors.
- [x] **Task 8 (Property Lifecycle Controls):** Implemented granular property lifecycle controls across all user roles:
  - **Admin & Employee:** Full permissions to deactivate or permanently delete properties across the platform. Upgraded `Properties.ListingStatus` check constraint in [`Data/DatabaseInitializer.cs`](file:///d:/Projects/BookMyFun/Data/DatabaseInitializer.cs) and [`Database_Setup_MonsterASP.sql`](file:///d:/Projects/BookMyFun/Database_Setup_MonsterASP.sql) to `CK_Properties_ListingStatus_V2` supporting `'Deactivated'` and `'Rejected'`. Enhanced [`Repositories/PropertyRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/PropertyRepository.cs) with `PermanentlyDeletePropertyAsync` (atomic cascading deletion of child payments, refunds, damage claims, bookings, reviews, and media) and `DeactivatePropertyAsync` (toggling `ListingStatus` between `'Deactivated'` and `'Active'`). Added `DeactivateProperty`, `DeleteProperty`, and `ApproveProperty` endpoints in [`Controllers/AdminController.cs`](file:///d:/Projects/BookMyFun/Controllers/AdminController.cs) and [`Controllers/EmployeeController.cs`](file:///d:/Projects/BookMyFun/Controllers/EmployeeController.cs) with full audit trail logging. Updated [`Views/Admin/ManageProperties.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/ManageProperties.cshtml) (and shared employee view), [`Views/Admin/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/Index.cshtml), and [`Views/Employee/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Employee/Index.cshtml) with Deactivate/Activate toggle buttons, irreversible permanent delete forms with CSRF protection, and real-time status filter badges.
  - **Owner / Host:** Permission to deactivate/unpublish only. Explicitly restricted and hid property deletion functionality from owner accounts. Added `DeactivateProperty` (with tenant IDOR protection and audit logging) and explicitly restricted `DeleteProperty` (throwing `ForbiddenException`) in [`Controllers/OwnerController.cs`](file:///d:/Projects/BookMyFun/Controllers/OwnerController.cs). Updated [`Views/Owner/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/Index.cshtml) with quick Unpublish/Publish action buttons, "Unpublished" status filter pills, and updated [`Views/Owner/EditProperty.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/EditProperty.cshtml) with a Listing Controls banner while strictly omitting any delete functionality. Verified with 11 automated unit/integration tests in [`BookMyFun.Tests/PropertyPermissionsTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/PropertyPermissionsTests.cs) (39 total tests across master suite). All passing with 0 compiler warnings and 0 errors.
- [x] **Task 3 (Serviceable Areas UI):** Redesigned the event manager dashboard interface for managing serviceable areas with interactive multi-select chips, active coverage tag cloud with intuitive add/remove actions, live location search filtering, custom area tag input, and regional map boundary zone presets:
  - **Design System & CSS Components:** Created `.area-chip`, `.selected-area-tag`, and `.zone-preset-pill` components in [`wwwroot/css/site.css`](file:///d:/Projects/BookMyFun/wwwroot/css/site.css) with fluid transitions, active state highlighting, remove buttons, and complete Dark/Light mode theme support.
  - **Dedicated Service Areas Management View:** Completely redesigned [`Views/EventManager/ServiceAreas.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/ServiceAreas.cshtml), replacing the legacy multiple select dropdown with an interactive multi-select chips grid (`#catalogChipsGrid`), active coverage tag cloud (`#selectedChipsCloud`), custom area tag entry field (`#customAreaInput`), regional map boundary presets (`#boundaryZonePresets` for Western Ghats, Coastal Konkan, Pune Metro & PCMC, and Nashik Vineyards), real-time search filtering (`#catalogSearchInput`), and synchronized hidden form inputs (`#hiddenInputsContainer`).
  - **Dashboard Quick-Manage Widgets:** Redesigned the quick service area card in [`Views/EventManager/Dashboard.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/Dashboard.cshtml) and [`Views/EventManager/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/Index.cshtml) with interactive chips, active coverage tag cloud with delete badges, live location search, and custom tag input.
  - **Backend Controller & Safe Routing:** Verified and hardened `UpdateServiceAreas` in [`Controllers/EventManagerController.cs`](file:///d:/Projects/BookMyFun/Controllers/EventManagerController.cs) with semicolon-delimited persistence, comprehensive audit logging (`UpdateServiceAreas`), and safe returnUrl redirection logic.
  - **Automated Regression Suite:** Created [`BookMyFun.Tests/ServiceableAreasTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/ServiceableAreasTests.cs) with 5 comprehensive automated tests (Tests 40–44) covering repository persistence, null/empty clearing, distinct location viewmodel mapping, and full DOM/HTML structure assertions. Integrated into [`BookMyFun.Tests/DirectTestRunner.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/DirectTestRunner.cs). All 44 master tests passing with 100% success, 0 build warnings, and 0 compiler errors.
- [x] **Task 10 (Owner Location Links):** Allowed property owners to submit and update direct Google Maps and location URLs for their listings:
  - **Database Persistence & Migrations:** Added `MapUrl NVARCHAR(1000) NULL` column to `Properties` table in [`Data/DatabaseInitializer.cs`](file:///d:/Projects/BookMyFun/Data/DatabaseInitializer.cs) and [`Database_Setup_MonsterASP.sql`](file:///d:/Projects/BookMyFun/Database_Setup_MonsterASP.sql) with an idempotent migration check.
  - **Entities & ViewModels:** Added `MapUrl` property to `Property` entity in [`Models/Entities/Entities.cs`](file:///d:/Projects/BookMyFun/Models/Entities/Entities.cs) and `PropertyEditViewModel` in [`Models/ViewModels/ViewModels.cs`](file:///d:/Projects/BookMyFun/Models/ViewModels/ViewModels.cs). Added `UpdatePropertyLocationUrlViewModel` for lightweight location link updates.
  - **Repository Layer:** Extended [`Repositories/PropertyRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/PropertyRepository.cs) (`IPropertyRepository`) with `UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl)`, updated `CreatePropertyAsync` to insert `MapUrl`, updated `UpdatePropertyAsync` to persist `MapUrl`, and updated `MapProperty` reader logic.
  - **Controller Actions:** Added `UpdateLocationUrl` POST action in [`Controllers/OwnerController.cs`](file:///d:/Projects/BookMyFun/Controllers/OwnerController.cs) with tenant IDOR verification (`prop.OwnerID == ownerId`), HTTP/HTTPS scheme validation, audit trail logging via `_configRepo.LogAuditAsync`, and safe returnUrl routing. Updated `CreateProperty` and `EditProperty` (GET/POST) in `OwnerController` and `AdminController` to seamlessly bind `MapUrl`.
  - **User Interface & Views:**
    - [`Views/Owner/CreateProperty.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/CreateProperty.cshtml) & [`Views/Owner/EditProperty.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/EditProperty.cshtml): Added Direct Google Maps / Location URL input field with a live "Test Link" popup button.
    - [`Views/Owner/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/Index.cshtml): Added direct Map Link indicator badge, quick "Add/Update Map Link" action button, and dedicated interactive modal `#modalUpdateMap_@p.PropertyID` with CSRF protection, current URL preview, and test link button.
    - [`Views/Properties/Details.cshtml`](file:///d:/Projects/BookMyFun/Views/Properties/Details.cshtml): Upgraded guest driving directions card to render a direct verified link to `@Model.MapUrl` when available.
    - [`Views/Admin/CreateProperty.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/CreateProperty.cshtml) & [`Views/Admin/EditProperty.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/EditProperty.cshtml): Added `MapUrl` inputs for complete admin synchronization.
  - **Automated Regression Suite:** Created [`BookMyFun.Tests/OwnerLocationLinksTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/OwnerLocationLinksTests.cs) with 6 comprehensive unit and integration tests (Tests 45–50) covering property creation persistence, edit preservation, quick update audit logging, invalid scheme rejection, tenant IDOR enforcement, and view markup assertions. Integrated into [`BookMyFun.Tests/DirectTestRunner.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/DirectTestRunner.cs). All 50 master tests passing with 100% success, 0 build warnings, and 0 compiler errors.
- [x] **Task 9 (Owner Category Assignment):** Added functionality allowing owners to assign categories to their properties/listings:
  - **Data & Repository Layer:** Added `GetOrCreatePropertyTypeByNameAsync(string name)` and `UpdatePropertyCategoryAsync(int propertyId, int ownerId, int propertyTypeId)` to `IPropertyRepository` and implemented in [`Repositories/PropertyRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/PropertyRepository.cs).
  - **Models & ViewModels:** Added `CustomCategory` property to `PropertyEditViewModel` and `AvailableCategories` lookup list to `OwnerDashboardViewModel` in [`Models/ViewModels/ViewModels.cs`](file:///d:/Projects/BookMyFun/Models/ViewModels/ViewModels.cs).
  - **Controller Actions:**
    - `OwnerController.Index()` populates `AvailableCategories` for quick category management on the host dashboard.
    - `OwnerController.CreateProperty` and `EditProperty` (POST) resolve and assign existing or custom categories via `GetOrCreatePropertyTypeByNameAsync`.
    - Added `AssignCategory` POST action in [`Controllers/OwnerController.cs`](file:///d:/Projects/BookMyFun/Controllers/OwnerController.cs) with tenant IDOR verification (`prop.OwnerID == ownerId`), automatic category creation, category assignment update, audit trail logging via `_configRepo.LogAuditAsync`, and safe returnUrl redirection.
  - **User Interface & Views:**
    - [`Views/Owner/CreateProperty.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/CreateProperty.cshtml) & [`Views/Owner/EditProperty.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/EditProperty.cshtml): Enhanced Category / Property Type selector with standard category dropdown and inline custom category tag input (`CustomCategory`).
    - [`Views/Owner/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/Index.cshtml): Added category badge indicator on each listing card, quick "Assign Category" action button, and interactive modal `#modalAssignCategory_@p.PropertyID` allowing hosts to choose an existing category or enter a custom category with CSRF protection.
  - **Automated Regression Suite:** Created [`BookMyFun.Tests/OwnerCategoryAssignmentTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/OwnerCategoryAssignmentTests.cs) with 6 comprehensive unit and integration tests (Tests 51–56) covering custom category creation during property creation and editing, 1-click modal category assignment, custom category registration and assignment, tenant IDOR isolation, and view markup verification. Integrated into [`BookMyFun.Tests/DirectTestRunner.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/DirectTestRunner.cs). All 56 master tests passing with 100% success, 0 build warnings, and 0 compiler errors.
- [x] **Task 5 (Commission Architecture):** Established a default global commission rate for all event managers and implemented an administrative override interface strictly accessible to Admin and Employee roles:
  - **Database & Persistence Layer:** Added idempotent seed migration for `EventPlatformCommissionPercent` (defaulting to 10.0%) in [`Data/DatabaseInitializer.cs`](file:///d:/Projects/BookMyFun/Data/DatabaseInitializer.cs) and [`Database_Setup_MonsterASP.sql`](file:///d:/Projects/BookMyFun/Database_Setup_MonsterASP.sql). Added `UpdateUserCommissionRateAsync` to `IUserRepository` and [`Repositories/UserRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/UserRepository.cs) to persist manager custom rates to `Users.CustomCommissionRate`.
  - **Models & ViewModels:** Added `UpdateEventManagerCommissionRequest`, `UpdateGlobalEventCommissionRequest`, and `EventCommissionsManagementViewModel` in [`Models/ViewModels/ViewModels.cs`](file:///d:/Projects/BookMyFun/Models/ViewModels/ViewModels.cs).
  - **Controllers & RBAC:**
    - Added `EventCommissions` (GET), `UpdateGlobalEventCommission` (POST), and `UpdateEventManagerCommission` (POST) to [`Controllers/AdminController.cs`](file:///d:/Projects/BookMyFun/Controllers/AdminController.cs) and [`Controllers/EmployeeController.cs`](file:///d:/Projects/BookMyFun/Controllers/EmployeeController.cs).
    - Added `UpdateEventCommission` and `UpdateBookingEventCommission` to [`Controllers/EmployeeController.cs`](file:///d:/Projects/BookMyFun/Controllers/EmployeeController.cs), matching `AdminController` with role checks, parameter validation, and audit trail logging via `_configRepo.LogAuditAsync`.
    - Strictly restricted to `Admin` and `Employee` roles (`[Authorize(Roles = "Admin")]` and `[Authorize(Roles = "Employee,Admin")]`).
  - **User Interface & Views:**
    - Created dedicated administrative management view [`Views/Admin/EventCommissions.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/EventCommissions.cshtml) and wrapper [`Views/Employee/EventCommissions.cshtml`](file:///d:/Projects/BookMyFun/Views/Employee/EventCommissions.cshtml) with global standing rate input, 5-tier hierarchy explainer, event managers custom overrides table with instant save/reset, and catalog event packages overrides table.
    - Updated [`Views/Admin/Settings.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/Settings.cshtml) with dedicated shortcut to the Commission Console.
    - Updated [`Views/Admin/ManageBookings.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/ManageBookings.cshtml) to allow Operations Employees (`User.IsInRole("Employee")`) to adjust event commissions on individual bookings.
    - Added navigation links under both Admin Console and Operations Staff dropdown menus in [`Views/Shared/_Layout.cshtml`](file:///d:/Projects/BookMyFun/Views/Shared/_Layout.cshtml).
  - **Automated Regression Suite:** Created [`BookMyFun.Tests/CommissionArchitectureTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/CommissionArchitectureTests.cs) with 7 comprehensive unit and integration tests (Tests 57–63) covering default global standing rate calculation, global rate updates and audits, per-event manager custom rate updates and calculation, manager rate resets, per-event package and booking overrides, RBAC restrictions strictly to Admin and Employee, and full view markup assertions. Integrated into [`BookMyFun.Tests/DirectTestRunner.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/DirectTestRunner.cs). All 63 master tests passing with 100% success, 0 build warnings, and 0 compiler errors.
- [x] **Task 11 (Employee Property Approval Bug):** Fixed the property verification/approval workflow in the employee dashboard and management console so status updates correctly persist and trigger state changes:
  - **Data & Repository Layer:** Enhanced `ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null)` in `IPropertyRepository` and [`Repositories/PropertyRepository.cs`](file:///d:/Projects/BookMyFun/Repositories/PropertyRepository.cs). When approved (`isApproved = true`), sets `IsApproved = 1` and `ListingStatus = 'Active'`; when rejected or revoked (`isApproved = false`), dynamically assigns target status (`'Rejected'` or `'Draft'`). Returns `false` if property does not exist.
  - **Controllers & Auditing:**
    - Upgraded `ApproveProperty(int id, bool approve, string? status = null, string? returnUrl = null)` in [`Controllers/EmployeeController.cs`](file:///d:/Projects/BookMyFun/Controllers/EmployeeController.cs) and [`Controllers/AdminController.cs`](file:///d:/Projects/BookMyFun/Controllers/AdminController.cs) with explicit `prop == null` check returning `NotFound()`, role-based audit trail logging (`EmployeeApproveProperty`, `EmployeeRejectProperty`, `EmployeeRevokeProperty`, `AdminApproveProperty`, `AdminRejectProperty`), user feedback TempData notifications, and seamless local `returnUrl` redirection.
    - Updated `DeactivateProperty` and `DeleteProperty` in both controllers to support `returnUrl`, preventing abrupt page displacement during operational triage.
    - Added `ViewBag.PendingPropertyApprovals` counter to `EmployeeController.Index()`.
  - **User Interface & Views:**
    - [`Views/Employee/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Employee/Index.cshtml): Added dedicated "Pending Property Approvals" stat card in the dashboard grid, quick status filter pills (`All`, `Pending Review`, `Approved & Live`, `Rejected`, `Deactivated`), and action buttons for "Approve", "Reject", and "Revoke" with CSRF tokens and safe returnUrl routing.
    - [`Views/Admin/ManageProperties.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/ManageProperties.cshtml): Added explicit "Reject" button for pending listings alongside "Approve" and "Revoke" buttons, with returnUrl context preservation.
    - [`Views/Admin/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/Index.cshtml): Enhanced recent property inventory with Approve, Reject, and Revoke buttons for rapid administrative verification.
  - **Automated Regression Suite:** Created [`BookMyFun.Tests/EmployeePropertyApprovalTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/EmployeePropertyApprovalTests.cs) with 6 comprehensive unit and integration tests (Tests 64–69) covering approval transition to Active, rejection transition to Rejected, revocation transition to Draft, non-existent property NotFoundResult, returnUrl redirection, and full DOM/HTML structure assertions. Integrated into [`BookMyFun.Tests/DirectTestRunner.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/DirectTestRunner.cs). All 69 master tests passing with 100% success, 0 build warnings, and 0 compiler errors.
- [x] **Task 4 (High-Priority Decoration Upsell UX):** Reorganized the event manager dashboard hierarchy to elevate the decoration offerings section out of the footer/subordinate area into a prominent, high-converting focal point:
  - **Hierarchy Reorganization:**
    - Repositioned the layout hierarchy in both [`Views/EventManager/Dashboard.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/Dashboard.cshtml) and [`Views/EventManager/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/EventManager/Index.cshtml): (1) Header & KPI Metrics Grid -> (2) Featured Decoration Offerings & Upsell Showcase (`#decorationOfferingsShowcase`) -> (3) Active Décor Orders & Setups Queue (`#eventOrdersTable`) -> (4) Operational Service Areas Configuration (`#dashServiceAreasForm`).
    - Elevated decoration offerings directly beneath the KPI overview, eliminating the subordinate positioning below the orders table and secondary service areas card.
  - **Design System & Conversion Badges:**
    - Added dedicated CSS classes in [`wwwroot/css/site.css`](file:///d:/Projects/BookMyFun/wwwroot/css/site.css): `.offering-focal-card`, `.offering-img-wrapper`, `.offering-badge-live`, `.offering-badge-inactive`, `.offering-payout-chip`, and `.offering-filter-btn` supporting both Light and Dark themes.
    - Integrated conversion elements: dynamic "Live in Checkout" green glow badge with pulse dot for active offerings, "Hidden / Draft" warning badge for inactive offerings, interactive client-side filter pills (`All Offerings`, `Live in Checkout`, `Hidden / Draft`), net decorator estimated payout chips (`Est: ₹...` accounting for platform commission), and 1-click active/inactive toggle forms with CSRF protection.
  - **Controller Actions & ReturnUrl Context:**
    - Enhanced `TogglePackageStatus` in [`Controllers/EventManagerController.cs`](file:///d:/Projects/BookMyFun/Controllers/EventManagerController.cs) to accept a `returnUrl` parameter, redirecting seamlessly without losing filter/scroll context on dashboards.
    - Hardened `DeletePackage` in [`Controllers/EventManagerController.cs`](file:///d:/Projects/BookMyFun/Controllers/EventManagerController.cs) with safe local `returnUrl` redirection.
  - **Automated Regression Suite:** Created [`BookMyFun.Tests/DecorationUpsellUXTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/DecorationUpsellUXTests.cs) with 6 comprehensive unit and integration tests (Tests 70–75) verifying active/inactive state toggling with audit logging, safe returnUrl redirection, non-existent package handling, hierarchical section ordering assertions (`#decorationOfferingsShowcase` preceding `#eventOrdersTable` preceding `#dashServiceAreasForm`), and DOM verification for conversion badges, filter pills, and 1-click toggles. Integrated into [`BookMyFun.Tests/DirectTestRunner.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/DirectTestRunner.cs). All 75 master tests passing with 100% success, 0 build warnings, and 0 compiler errors.
- [x] **Task 6 (Receipt Engine & Design):** Built and styled high-quality, professional booking confirmation receipts for both users and property owners:
  - **Models & ViewModels:**
    - Created [`BookingReceiptViewModel`](file:///d:/Projects/BookMyFun/Models/ViewModels/ViewModels.cs) with calculated monetary properties (`PropertyStayGross`, `CommissionRate`, `PlatformCommission`, `HostNetPayout`, `EventPackagePrice`, `SecurityDeposit`, `TotalPaid`), standardized document identifiers (`ReceiptNumber` -> `BMF-REC-XXXXXX`, `HostReceiptNumber` -> `BMF-HST-XXXXXX`, `TaxInvoiceNumber` -> `INV-YYYYMM-XXXXX`), and role contextual view flags (`IsOwnerView`, `IsAdminOrStaffView`).
  - **Design System & Print Engine (`wwwroot/css/site.css`):**
    - Added luxury receipt tokens and styles: `.receipt-container`, `.receipt-card`, `.receipt-header`, `.receipt-header-host`, `.receipt-brand-title`, `.receipt-badge-verified`, `.receipt-info-card`, `.receipt-table`, `.receipt-total-row`, and `.receipt-action-bar`.
    - Implemented print stylesheet (`@media print`) rendering clean, high-contrast, clutter-free A4 printable invoices with automatic suppression of navigation bars, footers, buttons, and shadows.
  - **Controllers & RBAC:**
    - Upgraded `Confirmation(int id)` and added `Receipt(int id)` in [`Controllers/BookingAndPropertiesController.cs`](file:///d:/Projects/BookMyFun/Controllers/BookingAndPropertiesController.cs) with tenant IDOR protection for guests (`booking.ClientID == currentUserId`), guest and stay details hydration, primary payment retrieval, and role-based staff access.
    - Added `BookingReceipt(int id)` in [`Controllers/OwnerController.cs`](file:///d:/Projects/BookMyFun/Controllers/OwnerController.cs) (`[Authorize(Roles = "Owner,Admin,Employee")]`) with host ownership verification (`property.OwnerID == currentUserId`) and transparent host earnings and commission settlement ledger.
  - **User Interface & Views:**
    - Created [`Views/Booking/Receipt.cshtml`](file:///d:/Projects/BookMyFun/Views/Booking/Receipt.cshtml): Luxury guest booking voucher & tax receipt with verified status seal, property destination card with direct Google Maps navigation link (`MapUrl`), primary traveler details, stay schedule, itemized billing statement, payment transaction reference, check-in instructions, and 1-click "Print / Save PDF" button.
    - Upgraded [`Views/Booking/Confirmation.cshtml`](file:///d:/Projects/BookMyFun/Views/Booking/Confirmation.cshtml): Post-payment entry pass voucher with instant alert confirmation banner, schedule overview, itemized financial summary, and direct links to full tax receipt and driving directions.
    - Created [`Views/Owner/BookingReceipt.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/BookingReceipt.cshtml): Official host settlement receipt with Emerald host partner header, gross stay revenue, platform commission deduction, net payout calculation, escrow security deposit protection, and separate celebration décor fee disclosure (ADR-21).
    - Updated [`Views/Client/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Client/Index.cshtml) with 1-click "Receipt" button on confirmed bookings.
    - Updated [`Views/Owner/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/Index.cshtml) with 1-click "Host Receipt" button on confirmed bookings.
    - Updated [`Views/Admin/ManageBookings.cshtml`](file:///d:/Projects/BookMyFun/Views/Admin/ManageBookings.cshtml) with guest and host receipt action buttons.
  - **Automated Regression Suite:** Created [`BookMyFun.Tests/ReceiptEngineTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/ReceiptEngineTests.cs) with 6 comprehensive unit and integration tests (Tests 76–81) covering financial calculations, guest receipt viewmodel hydration, client IDOR enforcement, host settlement viewmodel hydration, owner IDOR enforcement, and view/CSS print markup assertions. Integrated into [`BookMyFun.Tests/DirectTestRunner.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/DirectTestRunner.cs). All 81 master tests passing with 100% success, 0 build warnings, and 0 compiler errors.
- [x] **Task 7 (Dedicated Confirmation Page):** Built a dedicated post-booking confirmation page displaying key booking metadata, next steps roadmap, and direct access to download receipts:
  - **Design System & Visual Tokens (`wwwroot/css/site.css`):**
    - Added luxury confirmation page tokens and styles: `.confirm-hero-banner`, `.confirm-pulse-icon`, `.next-steps-grid`, `.next-step-card`, `.next-step-badge`, `.next-step-number`, `.receipt-download-hub`, and `.btn-confirm-action` supporting both Light and Dark themes.
  - **Dedicated Confirmation View (`Views/Booking/Confirmation.cshtml`):**
    - **Celebration Hero Header:** Glowing checkmark icon, reservation confirmation badge, reservation reference code with 1-click clipboard copy button, and instant multi-party alert dispatch banner (confirming Email & WhatsApp delivery to Guest, Host, and Decorator).
    - **Key Booking Metadata Grid:** 3 structured cards detailing Destination & Google Maps direct navigation link (`MapUrl`), Stay Schedule (Check-in/out windows, duration, guest party capacity), and Host Partner Contact Information.
    - **Itemized Financial Snapshot:** Clean itemization showing Razorpay Transaction ID, property stay gross, celebration décor package add-on, escrow security deposit, and total paid.
    - **Actionable 4-Step "Next Steps" Roadmap (`.next-steps-grid`):**
      1. *Confirmation Dispatched* (Instant, Completed) — Electronic pass voucher and payment acknowledgment generated.
      2. *Carry Government Photo IDs* (Action Required) — Reminder for all arriving guests to present valid photo IDs at check-in.
      3. *Map Driving Route* (Recommended) — Verified Google Maps navigation direct route.
      4. *Check-in & Gate Pass Handover* (On Stay Date) — Digital gate pass presentation and keys handover by host.
    - **Dedicated Receipt Download Hub (`#receiptDownloadCard`, `.receipt-download-hub`):**
      - 1-click "Download Official Tax Receipt" linking directly to `/Booking/Receipt/{id}`.
      - 1-click "Print Gate Pass Voucher" invoking high-contrast print mode (`window.print()`).
      - 1-click "Share Reservation Details" copying formatted booking summary to clipboard with toast notification.
  - **Controller Logic & Tenant IDOR Security (`Controllers/BookingAndPropertiesController.cs`):**
    - Verified `Confirmation(int id)` securely hydrates `BookingReceiptViewModel` with complete property, guest, stay, decor, and payment records.
    - Enforced strict tenant IDOR protection: guests can only access their own reservations (`booking.ClientID == currentUserId`), while host partners (`property.OwnerID == currentUserId`), Admin, and Operations Employee roles have verified access.
  - **Automated Regression Suite (`BookMyFun.Tests/DedicatedConfirmationPageTests.cs`):**
    - Created 6 comprehensive unit and integration tests (Tests 82–87):
      - Test 82: `Confirmation` GET returns populated `BookingReceiptViewModel` with property, stay schedule, and receipt links.
      - Test 83: Tenant IDOR security enforcement — client attempting to view another guest's confirmation receives `ForbiddenResult`.
      - Test 84: Admin and Employee authorization — administrative staff can inspect any confirmed booking.
      - Test 85: View markup verification — asserts presence of 4-step roadmap (`.next-steps-grid`, `.next-step-card`, `Step 1: Confirmation Dispatched`, `Step 2: Carry Government Photo IDs`, `Step 3: Map Driving Route`, `Step 4: Check-in & Gate Pass Handover`).
      - Test 86: Receipt download hub markup verification — asserts presence of `#receiptDownloadCard`, `.receipt-download-hub`, `Download Official Tax Receipt`, `/Booking/Receipt/`, `window.print()`, and `copyReservationSummary()`.
      - Test 87: CSS stylesheet verification — asserts presence of all confirmation and roadmap CSS classes in `wwwroot/css/site.css`.
    - Integrated into master test runner [`BookMyFun.Tests/DirectTestRunner.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/DirectTestRunner.cs). All 87 master tests passing with 100% success, 0 build warnings, and 0 compiler errors.
- [x] **Task 12 (Receipt Dispatch Integration):** Hooked generated receipts into transactional delivery channels, automatically sending copies via email and WhatsApp upon booking confirmation:
  - **Transactional Communication Services ([`Services/CommunicationService.cs`](file:///d:/Projects/BookMyFun/Services/CommunicationService.cs)):**
    - Extended `IEmailService` with `SendBookingReceiptEmailAsync` (luxury HTML email voucher containing Receipt Number `BMF-REC-XXXXXX`, Tax Invoice `INV-...`, property venue with Google Maps driving directions link, itemized stay/decor/deposit breakdown, digital gate pass instructions, and 1-click Download Tax Receipt button) and `SendOwnerSettlementReceiptEmailAsync` (official host settlement statement with Host Receipt Number `BMF-HST-XXXXXX`, property gross, platform commission deduction, net host payout, escrow security deposit protection, and celebration décor disclosure notice).
    - Extended `IWhatsAppService` with `SendBookingReceiptWhatsAppAsync` (WhatsApp booking confirmation message with client name, property title, receipt number, stay dates, amount, and direct receipt download URL) and `SendOwnerBookingReceiptWhatsAppAsync` (WhatsApp host notification with settlement reference, property stay gross, net host payout, guest details, and direct host settlement receipt URL).
    - Backed by asynchronous background queue `IEmailQueueService` preventing request latency.
  - **Automated Booking Confirmation Hook ([`Controllers/BookingAndPropertiesController.cs`](file:///d:/Projects/BookMyFun/Controllers/BookingAndPropertiesController.cs)):**
    - Enhanced `DispatchConfirmedBookingAlertsAsync` to automatically build `BookingReceiptViewModel` and dispatch official tax receipts and settlement statements across both Email and WhatsApp channels for all confirmed payments (both Razorpay SDK verification and direct checkout).
    - Added `ResendReceipt(int bookingId, string? returnUrl = null)` endpoint with strict tenant IDOR protection (`booking.ClientID == currentUserId` or Admin/Employee), audit logging via `_configRepo.LogAuditAsync`, and 1-click re-dispatch to registered email and WhatsApp.
  - **Host Settlement Receipt Dispatch ([`Controllers/OwnerController.cs`](file:///d:/Projects/BookMyFun/Controllers/OwnerController.cs)):**
    - Added `ResendBookingReceipt(int bookingId, string? returnUrl = null)` with property ownership verification (`property.OwnerID == currentUserId` or Admin/Employee) and audit logging to re-dispatch host settlement statements on demand.
  - **User Interface & Action Controls:**
    - [`Views/Booking/Confirmation.cshtml`](file:///d:/Projects/BookMyFun/Views/Booking/Confirmation.cshtml): Enhanced alert banner with explicit receipt confirmation notice and added 1-click "Email & WhatsApp Receipt" dispatch button in `#receiptDownloadCard`.
    - [`Views/Booking/Receipt.cshtml`](file:///d:/Projects/BookMyFun/Views/Booking/Receipt.cshtml): Added "Send to Email & WhatsApp" action button with CSRF token and returnUrl routing.
    - [`Views/Owner/BookingReceipt.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/BookingReceipt.cshtml): Added "Send to Email & WhatsApp" settlement statement button in the action bar.
  - **Automated Regression Suite ([`BookMyFun.Tests/ReceiptDispatchIntegrationTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/ReceiptDispatchIntegrationTests.cs)):**
    - Created 6 comprehensive unit and integration tests (Tests 88–93):
      - Test 88: `CommunicationService.SendBookingReceiptEmailAsync` generates responsive HTML containing receipt numbers, itemized billing, and direct receipt links.
      - Test 89: `CommunicationService.SendBookingReceiptWhatsAppAsync` dispatches complete receipt details and direct URLs.
      - Test 90: `CommunicationService.SendOwnerSettlementReceiptEmailAsync` generates host statement with net payout, commission fee, and settlement receipt link.
      - Test 91: `CommunicationService.SendOwnerBookingReceiptWhatsAppAsync` dispatches settlement summary and URL via WhatsApp.
      - Test 92: `BookingController.ResendReceipt` enforces tenant IDOR security (forbids unauthorized guests, succeeds for owner guest, logs audit).
      - Test 93: Views and confirmation templates declare transactional delivery controls (`btnResendReceipt`, `btnDispatchReceiptChannels`, `btnReceiptResendDelivery`, `btnHostReceiptResendDelivery`).
    - Integrated into master test runner [`BookMyFun.Tests/DirectTestRunner.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/DirectTestRunner.cs). All 93 master tests passing with 100% success, 0 build warnings, and 0 compiler errors.
- [x] **Task 16 (Theme Glitch Resolution):** Resolved theme-switching styling bugs across the application, ensuring uniform contrast, borders, and token inheritance across Dark and Light modes:
  - **Attribute Synchronization & Anti-FOUC ([`Views/Shared/_Layout.cshtml`](file:///d:/Projects/BookMyFun/Views/Shared/_Layout.cshtml) & [`wwwroot/js/site.js`](file:///d:/Projects/BookMyFun/wwwroot/js/site.js)):**
    - Updated initial `<html>` tag to declare both `data-theme="light"` and `data-bs-theme="light"`.
    - Enhanced the Anti-FOUC inline head script to synchronously set both `data-theme` and `data-bs-theme` before initial paint, eliminating flashing across webviews and fast reloads.
    - Updated `site.js` `applyTheme()` to update both `data-theme` and `data-bs-theme` attributes on `document.documentElement`, unlocking native Bootstrap 5.3 dark-mode styling across dropdowns, modals, forms, and tables.
  - **Global Luxury Design System Tokens ([`wwwroot/css/site.css`](file:///d:/Projects/BookMyFun/wwwroot/css/site.css)):**
    - Established unified tokens under `:root, [data-theme="light"], [data-bs-theme="light"]`: `--bg-primary: #f8fafc`, `--bg-card: #ffffff`, `--bg-glass: rgba(255, 255, 255, 0.85)`, `--brand-navy: #0b4f6c`, `--brand-navy-dark: #083c53`, `--brand-sky: #01baef`, `--text-main: #0f172a`, `--text-muted: #64748b`, `--border-subtle: #e2e8f0`.
    - Established unified dark mode tokens under `[data-theme="dark"], [data-bs-theme="dark"]`: `--bg-primary: #0f172a`, `--bg-card: #1e293b`, `--bg-glass: rgba(15, 23, 42, 0.88)`, `--brand-navy: #38bdf8`, `--brand-navy-dark: #0ea5e9`, `--brand-sky: #38bdf8`, `--text-main: #f1f5f9`, `--text-muted: #94a3b8`, `--border-subtle: #334155`.
    - Harmonized Bootstrap 5 link colors in dark mode: `--bs-link-color: #60a5fa` and `--bs-link-hover-color: #93c5fd`.
  - **Catalog & Views Token Scoping ([`Views/Properties/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Properties/Index.cshtml)):**
    - Removed hardcoded `:root` block that previously locked in light-mode colors and broke dark mode on the explore catalog page.
    - Added comprehensive dark mode overrides for `.luxe-*` classes: `.luxe-control`, `.luxe-control:focus`, `.luxe-category-pill`, `.luxe-category-pill:hover`, `.luxe-category-pill.active`, `.luxe-card-rating`, `.luxe-img-container`, `.luxe-card-heading`, `.luxe-price-figure`, `.luxe-btn-secondary`, and `.luxe-empty-icon`.
  - **Bootstrap Utility, Contrast & Inset Surface Polish ([`wwwroot/css/site.css`](file:///d:/Projects/BookMyFun/wwwroot/css/site.css)):**
    - Added `.btn-light` dark mode styling with `#334155` background, `#475569` border, and `#f1f5f9` text, eliminating blinding white buttons on dark dashboards.
    - Added `.text-secondary` and `.border-secondary` dark mode contrast rules mapping to `var(--text-muted)` and `#334155`.
    - Added `.btn-primary .badge.bg-white` dark mode override preserving clean white badge contrast inside primary action buttons.
    - Added `.table-bordered` and `.breadcrumb-item` dark mode rules with `#334155` borders and `#94a3b8` text.
    - Fixed sub-surface depth hierarchy: `.bg-light` in dark mode resolves to `#0f172a` sunken well with `#334155` border inside `#1e293b` cards.
    - Added `-webkit-autofill` dark mode rules preventing Chrome/Edge browser autofill yellow/white glare.
    - Added `.dash-badge-secondary`, `.dash-badge-danger`, and `.dash-badge-info` classes with dark mode variants.
  - **Payment Hero Card Gradient Refactoring ([`Views/Booking/Payment.cshtml`](file:///d:/Projects/BookMyFun/Views/Booking/Payment.cshtml) & [`Views/Owner/SubscriptionPayment.cshtml`](file:///d:/Projects/BookMyFun/Views/Owner/SubscriptionPayment.cshtml)):**
    - Replaced hardcoded inline mint green gradient with reusable `.payment-hero-card` class.
    - Added dark mode gradient (`linear-gradient(135deg, #064e3b 0%, #022c22 100%)`) with emerald border and `#f1f5f9` text, eliminating white-on-mint unreadable contrast.
  - **Automated Regression Suite ([`BookMyFun.Tests/ThemeGlitchResolutionTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/ThemeGlitchResolutionTests.cs)):**
    - Created 6 comprehensive unit & regression tests (Tests 94–99) integrated into [`BookMyFun.Tests/DirectTestRunner.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/DirectTestRunner.cs).
    - All 99 master tests passing with 100% success, 0 build warnings, and 0 compiler errors.
- [x] **Task 17 (Responsive Layout & Sizing):** Fixed layout overflow, orientation mismatches, and element sizing issues across mobile, tablet, and desktop breakpoints:
  - **Document & Viewport Containment ([`wwwroot/css/site.css`](file:///d:/Projects/BookMyFun/wwwroot/css/site.css)):**
    - Configured `html` and `body` with `overflow-x: clip;` (with `@supports not (overflow-x: clip)` fallback to `overflow-x: hidden;`), preventing unintentional horizontal viewport scrollbars caused by negative row gutters without breaking sticky positioning.
    - Set global box-sizing inheritance (`box-sizing: border-box;`) and `-webkit-text-size-adjust: 100%`.
    - Applied global media containment rule (`img, svg, video, canvas, audio, iframe, embed, object { max-width: 100%; height: auto; }`) ensuring third-party or user-uploaded media cannot break parent grid columns.
    - Enforced word-break and overflow-wrap defense (`overflow-wrap: break-word; word-break: break-word;`) across `.text-break-word`, `.receipt-brand-title`, `.dash-title`, `.hero-title`, and `.luxe-title`.
  - **iOS Safari Auto-Zoom Prevention & Touch Target Standard ([`wwwroot/css/site.css`](file:///d:/Projects/BookMyFun/wwwroot/css/site.css)):**
    - Standardized `font-size: 16px !important;` under `@media (max-width: 767.98px)` for all input types (`text`, `number`, `email`, `tel`, `password`, `date`, `datetime-local`, `search`, `url`), `.form-control`, `.form-select`, and `.luxe-control`, eliminating automatic viewport zoom upon focus on iOS WebKit devices.
    - Enforced minimum touch target heights (>= 40px - 48px) on `.btn`, `.dash-btn`, `.dash-filter-pill`, `.luxe-category-pill`, `.zone-preset-pill`, and `.mobile-nav-link`.
  - **Catalog & Explore Page Fluidity ([`Views/Properties/Index.cshtml`](file:///d:/Projects/BookMyFun/Views/Properties/Index.cshtml) & [`wwwroot/css/site.css`](file:///d:/Projects/BookMyFun/wwwroot/css/site.css)):**
    - Stacked `.luxe-card-actions` action buttons (`grid-template-columns: 1fr`) on mobile viewports (< 480px / < 576px), eliminating awkward button text wrapping and horizontal card clipping.
    - Scaled `.luxe-search-card` padding down to `1.25rem 1rem` on mobile and tablet screens, preserving interior content area.
    - Scaled `.luxe-title` with dynamic fluid typography (`font-size: clamp(1.5rem, 5vw, 1.85rem)`).
    - Enabled smooth horizontal momentum scrolling (`-webkit-overflow-scrolling: touch; scrollbar-width: none;`) for `.luxe-category-bar`.
  - **Dashboard Hierarchy & Responsive Stat Grid ([`wwwroot/css/site.css`](file:///d:/Projects/BookMyFun/wwwroot/css/site.css)):**
    - Re-architected `.dash-stat-grid` across breakpoints: 4 columns on desktop (`>= 992px`), 3 columns on tablets (`768px - 991px`), 2 columns on small tablets/landscape phones (`576px - 767px`) and compact phones (`< 576px`), and graceful 1-column collapse on ultra-narrow phones (`< 380px`), eliminating KPI currency figure truncation.
    - Enforced horizontal scroller mechanics on `.dash-actions` and `.dash-filter-pills` with `flex-shrink: 0`.
    - Enhanced table containment in `.table-responsive` with smooth touch scrolling and `white-space: nowrap` cell wrapping.
  - **Receipt & Confirmation Sizing & Mobile Modals ([`wwwroot/css/site.css`](file:///d:/Projects/BookMyFun/wwwroot/css/site.css)):**
    - Scaled `.receipt-container`, `.receipt-header`, and `.receipt-body` padding down to `1.25rem 1rem` on mobile.
    - Configured `.receipt-action-bar` to stack buttons full-width vertically on narrow screens.
    - Enabled block-level touch scrolling for `.receipt-table` and scaled `.confirm-hero-banner` padding.
    - Added modal dialog viewport fitting (`.modal-dialog { margin: 0.5rem !important; max-width: calc(100vw - 1rem) !important; }`) and scrollable body bounds (`max-height: calc(100vh - 160px)`).
    - Handled landscape orientation (`@media (max-height: 500px) and (orientation: landscape)`) with compact 48px bottom bar and scaled hero containers.
  - **Automated Regression Suite ([`BookMyFun.Tests/ResponsiveLayoutTests.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/ResponsiveLayoutTests.cs)):**
    - Created 6 comprehensive unit & regression tests (Tests 100–105) covering viewport containment, iOS auto-zoom prevention, tap targets & table containment, luxe catalog stacking, dashboard stat grid collapse, and receipt/modal viewport fitting.
    - Added [`BookMyFun.Tests/TestPathHelper.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/TestPathHelper.cs) ensuring 100% path resolution across both `dotnet test` and standalone `DirectTestRunner.exe`.
    - Integrated into master test runner [`BookMyFun.Tests/DirectTestRunner.cs`](file:///d:/Projects/BookMyFun/BookMyFun.Tests/DirectTestRunner.cs). All 105 master tests passing with 100% success, 0 build warnings, and 0 compiler errors.
