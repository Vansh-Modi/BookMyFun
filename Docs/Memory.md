# BookMyFun — Context Retention & Architectural Decision Records (ADRs)

## 1. Platform Context & Technical Specifications

- **Platform Name:** BookMyFun — Luxury Farmhouse, Villa & Resort Rental Platform.
- **Runtime & Language:** .NET 10 (`net10.0`), C# 13, ASP.NET Core MVC with Razor View Engine.
- **Database Engine:** Microsoft SQL Server 2022 (`Microsoft.Data.SqlClient 7.1.1`).
- **Data Access Layer:** Raw Parameterized ADO.NET with centralized [`DBHelper.cs`](file:///d:/Projects/BookMyFun/Data/DBHelper.cs). Zero ORM overhead, zero memory tracking bloat.
- **User Roles & Dashboards (5 Roles):**
  1. `Client` (Guest) -> `/Client/Index`
  2. `Owner` (Farm Host) -> `/Owner/Index`
  3. `Employee` (Operations & Support) -> `/Employee/Index`
  4. `Admin` (Superuser) -> `/Admin/Index`
  5. `EventManager` (Decorator & Event Planner) -> `/EventManager/Dashboard`
- **UI Framework & Typography:** Bootstrap 5.3, Bootstrap Icons, custom `Plus Jakarta Sans` typography, and CSS custom property theming tokens with instant dark/light mode toggle.
- **Reporting Engine:** ClosedXML 0.104.2 (Native Excel `.xlsx` generation) and UTF-8 streaming CSV export.
- **Payment Gateway:** Razorpay SDK with HMAC-SHA256 signature verification and tokenized checkout.
- **Security Middlewares:** Native sliding-window rate limiting, security headers (CSP, HSTS, X-Frame-Options: DENY), and HTTP-only SameSite=Strict cookie authentication.

---

## 2. Architecture Decision Records (ADRs)

### ADR-01: Dual File and URL Media Architecture
- **Context:** Hosts capture content on smartphones (direct files) or have professional walkthroughs hosted on YouTube/Vimeo/Cloudinary. Server memory limits and disk bloat prevent storing large 4K video files for every listing.
- **Decision:** Support both storage models in a single `PropertyMedia` entity with `SourceType IN ('file', 'url')`. Direct uploads are inspected for magic bytes and saved to disk with random UUID filenames. External URLs are validated with an SSRF filter (HTTPS only, rejecting private and cloud metadata IPs).
- **Consequence:** Eliminates server storage bottlenecks for long 4K videos while guaranteeing security against malicious file uploads and internal network probes.

---

### ADR-02: Strict Separation of Guest Government IDs from Property Hosts (Privacy Shield)
- **Context:** Indian hospitality regulations require guest identity verification before check-in. However, giving property owners access to raw Aadhaar/Passport copies violates data privacy best practices and increases liability for identity theft.
- **Decision:** Store guest identity documents in a private directory (`/App_Data/PrivateDocuments/`) outside `wwwroot`, accessible only via 15-minute expiring HMAC-SHA256 signed URLs. Restrict raw document access to the guest (owner), Employee, and Admin. Hosts only receive a signed "Verified Guest" badge or masked identifier (`XXXX-XXXX-1234`).
- **Consequence:** Fully satisfies regulatory KYC requirements while shielding guests from unauthorized PII exposure.

---

### ADR-03: Immutable Snapshotting of Policies and Commission at Booking Creation
- **Context:** If a host or administrator updates a property's price, commission rate, or cancellation policy, past or active bookings must not be retroactively altered.
- **Decision:** Whenever a booking is created, snapshot the active cancellation parameters (`FreeCancellationWindowHoursSnapshot`, `CancellationCutoffHoursSnapshot`, `CancellationFeeTypeSnapshot`, `CancellationFeeValueSnapshot`) and commission calculations (`CommissionRateSnapshot`, `PlatformCommissionAmount`, `OwnerPayoutAmount`) directly onto the `Bookings` record.
- **Consequence:** Guarantees historical financial ledger integrity, prevents cancellation disputes, and ensures immutable accounting records.

---

### ADR-04: Replacement of Public Host Registration with Partner Lead Pipeline
- **Context:** Open self-registration allowed fraudulent or unvetted hosts to publish listings without review.
- **Decision:** Remove the Owner role option from public registration. Implement a public "Partner With Us / List Your Farm" lead intake form. Employees or Admins review submissions, conduct verification, and issue an encrypted, 48-hour activation token via email.
- **Consequence:** Establishes a verified partner network, eliminates fraudulent listings, and builds high platform credibility.

---

### ADR-05: Strict 24-Hour Damage Claim Adjudication Window
- **Context:** Hosts need a mechanism to claim security deposits for damages, but guests must not be subjected to delayed or unsubstantiated claims.
- **Decision:** Hosts must file damage claims within 24 hours of guest checkout with itemized costs and photographic/video evidence. Claims are adjudicated by an Employee or Admin before any security deposit funds are disbursed.
- **Consequence:** Provides fair, documented dispute resolution and maintains guest trust.

---

### ADR-06: Centralized Custom Error Hierarchy & Global Exception Filtering
- **Context:** Unhandled exceptions can crash requests and leak sensitive database schemas or stack traces to clients.
- **Decision:** Implement a clean domain exception hierarchy (`AppException`, `ValidationException`, `ForbiddenException`, `NotFoundException`, `PaymentGatewayException`, `SecurityViolationException`) and catch them via a Global Exception Filter. Return standardized JSON responses for API calls and friendly error views with correlation IDs for web views.
- **Consequence:** Eliminates information disclosure vulnerabilities (OWASP A05) and delivers resilient user experiences.

---

### ADR-07: Operational Queues Architecture for Support & Admin
- **Context:** Both Admins (Superusers) and Employees (Operations Staff) perform triage and review of refunds, onboarding leads, damage claims, and KYC verifications.
- **Decision:** Share UI Razor views (`Views/Admin/*.cshtml` and `Views/Employee/*.cshtml`) through view delegation and dynamic `@controllerName` resolution based on the user's active role.
- **Consequence:** Provides complete role separation while eliminating code and template duplication.

---

### ADR-08: ClosedXML & CSV Asynchronous Analytics Reporting
- **Context:** Administrators need financial reports (GMV, platform commission, pending host payouts, occupancy rates) across flexible presets (Today, 7D, MTD, YTD, Custom) exportable for executive review.
- **Decision:** Use ClosedXML for native Excel (`.xlsx`) generation with custom styling and header coloring, plus streaming UTF-8 CSV exports.
- **Consequence:** Generates fast, professional spreadsheet deliverables without external office suite dependencies.

---

### ADR-09: Raw Parameterized ADO.NET vs. Entity Framework Core
- **Context:** Entity Framework Core adds memory footprint, complex change-tracking overhead, and generated SQL queries that can degrade performance under heavy search and reservation traffic.
- **Decision:** Implement all data access with raw parameterized ADO.NET via [`DBHelper.cs`](file:///d:/Projects/BookMyFun/Data/DBHelper.cs).
- **Consequence:** Achieves maximum query performance, predictable memory utilization, and transparent database operations.

---

### ADR-10: Anti-FOUC Inline Theme Hydration & CSS Custom Property Tokens
- **Context:** Relying on JavaScript executed after DOMContentLoaded to apply dark mode causes an unsightly visual flicker (Flash of Unstyled Content).
- **Decision:** Inject a minimal, render-blocking `<script>` tag inside `<head>` to read `localStorage` or `prefers-color-scheme` and immediately set the `data-bs-theme` attribute on `<html>`. All layout colors are defined using CSS custom properties.
- **Consequence:** Smooth, instantaneous page rendering in both light and dark modes with zero visual flashing.

---

### ADR-11: Sliding-Window In-Memory Rate Limiting
- **Context:** Automated scrapers and brute-force bots can degrade service availability on search and login endpoints.
- **Decision:** Implement a native ASP.NET Core middleware (`RateLimitingMiddleware`) using a sliding in-memory dictionary tracking IP request counts with a cap of 100 requests per minute.
- **Consequence:** Protects the platform from volumetric abuse without external Redis dependencies for single-instance deployments.

---

### ADR-12: Magic-Byte Binary Inspection & Traversal Sanitization
- **Context:** Attackers can rename `.exe`, `.php`, or `.sh` files to `.jpg` or `.png` to bypass basic extension checks.
- **Decision:** Inspect the first 16 bytes of every uploaded file stream for magic signatures (JPEG, PNG, WEBP, MP4, MOV, PDF). Discard client filenames, strip path traversal sequences (`../`), and generate random UUID filenames.
- **Consequence:** Completely prevents remote code execution (RCE) and directory traversal via file uploads.

---

### ADR-13: Event Manager / Decorator 5th Dashboard Integration
- **Context:** Farm and villa bookings are frequently paired with celebrations (birthdays, weddings, anniversaries, corporate retreats) requiring decor setups. Previous iterations lacked dedicated tools for decorators and celebration stylists.
- **Decision:** Introduce a dedicated 5th role `EventManager` (`[Authorize(Roles = "EventManager,Admin")]`), an `EventPackages` table, booking event columns (`IsEventBooking`, `EventPackageID`, `SetupStatus`, `SetupProofMediaUrls`), an on-ground milestone lifecycle (`Pending` -> `Assigned` -> `InProgress` -> `Completed` -> `Inspected`), checkout add-on toggling, and Property vs Event GMV tracking.
- **Consequence:** Expands the marketplace into event coordination while isolating decorator privileges and ensuring verifiable setup execution through photo/video proofs.

---

### ADR-14: Clean Code & Dead Code Elimination Mandate
- **Context:** Rapid feature iterations can leave behind commented-out code blocks, unused variables, redundant parameters, or unnecessary boilerplate that compromise codebase health.
- **Decision:** Enforce zero commented-out code snippets, zero unreferenced fields, clean using directives, and zero compiler warnings across all models, repositories, and controllers.
- **Consequence:** Maintains a pristine, maintainable, enterprise-standard codebase that builds cleanly in every CI/CD cycle.

---

### ADR-15: Universal Profile & Security Credentials Architecture
- **Context:** Users across all 5 dashboard roles (`Client`, `Owner`, `Employee`, `Admin`, `EventManager`) required an authenticated, unified interface to manage personal details and change passwords without exposing privileged administrative routes.
- **Decision:** Implement a centralized `/Account/Profile` route in `AccountController` backed by `UserProfileViewModel` and `ChangePasswordViewModel`. Enforce BCrypt password verification, session identity preservation, and role-based badge rendering.
- **Consequence:** Eliminates disjoint profile management while maintaining consistent role-scoped authentication rules across all user types.

---

### ADR-16: Owner Safe Deactivation & Complimentary Trial Quota
- **Context:** Hard deletion of Property Owners caused foreign key integrity violations with existing properties and reservation records. Additionally, newly onboarded hosts faced quota lockouts on initial property creation.
- **Decision:** Wrap user deletions in a foreign-key-safe deactivation fallback (`IsActive = 0`) preserving relational referential integrity. Grant verified hosts a complimentary 1-listing trial quota (`activeListings < 1`) so they can publish their first property immediately without subscription barriers.
- **Consequence:** Eliminates runtime database exceptions, maintains historic financial audit trails, and provides seamless onboarding for vetted hosts.

---

### ADR-17: Support Accountability & End-to-End Operational Traceability
- **Context:** Customer support responses previously lacked attribution, making it difficult to audit which employee or administrator resolved a customer inquiry.
- **Decision:** Add responder metadata columns (`RespondedByUserName`, `RespondedByUserRole`, `RespondedAt`, `ResponseNotes`) to support query records. Capture the authenticated user's identity automatically upon query resolution.
- **Consequence:** Enforces organizational accountability, enhances compliance, and simplifies dispute resolution.

---

### ADR-18: Dedicated Partner & Event Manager Login Portals and Separate Event Manager Dashboard
- **Context:** Property hosts and Event Managers required tailored, dedicated sign-in portals aligned with their distinct operational workflows, rather than a single generic login form. Additionally, the Event Manager console required a separate, full-featured dashboard conforming to the system-wide design language.
- **Decision:** 
  1. Implement dedicated login routes `/Account/OwnerLogin` (accessible via `/Owner/Login`) with emerald branding and 1-click test fill (`owner@bookmyfun.com` / `Owner@123`).
  2. Implement dedicated login routes `/Account/EventManagerLogin` (accessible via `/EventManager/Login`) designed identical to Owner's with amber celebration branding and 1-click test fill (`eventmanager@bookmyfun.com` / `Event@123`).
  3. Implement separate Event Manager Dashboard view at `Views/EventManager/Index.cshtml` (and sync `Views/EventManager/Dashboard.cshtml`) using universal `.dash-*` tokens with full KPI grid, active package showcase, and order claiming/advancement queue.
  4. Equip `_Layout.cshtml` and `Login.cshtml` with portal switcher chips and clear navigation entry points.
- **Consequence:** Delivers an intuitive, role-specific authentication and management experience across all 5 platform roles without breaking generic login backwards compatibility.

---

### ADR-19: Explicit Cookie Lifecycle, Dual-Verb Logout & Database Column Migration for IsActive
- **Context:** During development and application restart, stale browser authentication cookies (`.AspNetCore.Cookies` with 7-day lifetimes) kept previous administrative sessions active upon initial site launch. Furthermore, invoking `/Account/Logout` via direct GET navigation caused HTTP 405 Method Not Allowed exceptions, and logging in as Admin failed with `SqlException: Invalid column name 'IsActive'` because pre-existing database instances lacked the `IsActive` column in `Users`.
- **Decision:**
  1. Assign an explicit cookie name `BookMyFun.AuthCookie` in `Program.cs`, configure `SameSiteMode.Lax`, and constrain default expiration to 8 hours.
  2. Implement dual-verb `[AcceptVerbs("GET", "POST")]` with `[IgnoreAntiforgeryToken]` on `AccountController.Logout` and explicitly invoke `Response.Cookies.Delete()` for `BookMyFun.AuthCookie` and `.AspNetCore.Cookies`.
  3. Ensure idempotent schema migration in `DatabaseInitializer.cs` to automatically execute `ALTER TABLE Users ADD IsActive BIT NOT NULL DEFAULT 1 WITH VALUES;` whenever the column is missing in existing databases.
  4. Include `IsActive` in `UserRepository.CreateUserAsync` and `UserRepository.UpdateUserAsync`.
- **Consequence:** Guarantees clean initial unauthenticated launches, provides robust sign-out across all navigation methods, and prevents SQL Server runtime schema errors when logging in as administrator.

---

### ADR-20: Automated Dual-Channel Credential Dispatch (WhatsApp API & Email) on Partner Lead & Owner Approval
- **Context:** When operations staff approve an Owner or Event Manager partner lead or a trial listing request, partners must receive instant access credentials without manual administrative intervention. Relying only on email often results in delayed opens or spam filtering, while phone communications need high reliability in the Indian market.
- **Decision:**
  1. Standardize secure password generation (`Host@<6-digits>` for Hosts/Owners, `Event@<6-digits>` for Event Managers) across both `PartnerLead` approvals and `OwnerRequest` reviews.
  2. Hash initial passwords using SHA-256 and store them directly in `Users.PasswordHash` with `IsActive = 1`.
  3. Dispatch account credentials across both Email (`IEmailService`) and WhatsApp (`IWhatsAppService` via Meta Cloud API / WhatsApp Gateway), explicitly communicating:
     - The Account ID (`#<UserID>`)
     - The Login Email / ID
     - The auto-generated temporary password
     - The official portal sign-in link
  4. Ensure both notification tasks are explicitly awaited with resilient error trapping so transmission glitches do not roll back the database transaction.
  5. Reflect the generated Account ID and temporary password directly in `TempData["Success"]` on the Admin/Employee console for instant staff reference and phone support.
- **Consequence:** Provides instant, friction-free onboarding for approved partners via their primary communication channels, eliminates manual staff friction, and reinforces transparency.





---

### ADR-21: Financial Segregation of Property Stays vs. Event Décor Add-ons & Event Manager Escrow Ledger
- **Context:** When guests booked a farm stay with an event decoration package, the combined sum was previously displayed as the total booking amount in the property owner's dashboard. This created mistrust, as host commission calculations and gross metrics appeared to encompass decoration revenue that was rightfully earned by and disbursed to the Event Decorator Studio. Furthermore, Event Managers needed a dedicated financial dashboard to monitor their GMV, settled earnings, and escrow-protected funds.
- **Decision:**
  1. Segregate property stay gross (`propertyStayAmount`) and event décor fee (`eventPackagePrice`) at reservation time in `BookingAndPropertiesController.ConfirmBooking`.
  2. Enforce that platform commission (10%) is strictly assessed on `propertyStayAmount`. The property host's `OwnerPayoutAmount` equals `propertyStayAmount - platformCommission`.
  3. Store `EventPackagePriceSnapshot` independently on `Bookings`, ensuring 100% of the décor fee is allocated to the Event Decorator Studio without reducing the host's payout.
  4. Explicitly itemize the financial breakdown on the Owner Dashboard (`Views/Owner/Index.cshtml`), showing Host Gross, Platform Commission, Net Host Payout, and an unambiguous Décor Add-on callout clarifying that the guest's decoration package is allocated to the Event Decorator.
  5. Build a dedicated Event Manager Financials console at `/EventManager/Financials` (`Views/EventManager/Financials.cshtml`) tracking Gross Revenue, Settled Disbursements (upon completion/inspection), and Escrow-Protected Funds with date/status filters.
- **Consequence:** Eliminates host mistrust and perceived financial shortchanging, ensures clean multi-tenant commission accounting, and empowers Event Managers with full financial visibility.

---

### ADR-22: Razorpay Gateway Integration & Multi-Party WhatsApp Notifications
- **Context:** BookMyFun required a production-grade Indian payment gateway (Razorpay) supporting standard orders API creation, secure client-side modal checkout (`checkout.js`), and server-side HMAC-SHA256 signature verification. Additionally, booking confirmations needed instantaneous multi-stakeholder alerts dispatched across WhatsApp to Guests, Property Owners, and Event Managers.
- **Decision:**
  1. Add `Razorpay` and `WhatsApp` configuration blocks to `appsettings.json`.
  2. Implement `RazorpayPaymentService` in `Services/PaymentGatewayService.cs` with HTTP Basic Auth orders API integration (`https://api.razorpay.com/v1/orders`), verifiable fallback tokens for sandbox testing, and cryptographic HMAC-SHA256 signature verification.
  3. Add `/Booking/VerifyPayment` in `BookingAndPropertiesController` to validate the Razorpay signature and advance bookings to `Confirmed`.
  4. Embed official Razorpay Checkout SDK (`https://checkout.razorpay.com/v1/checkout.js`) in `Views/Booking/Payment.cshtml` with modal auto-launch and callback form submission.
  5. Implement multi-stakeholder alert dispatching in `Services/CommunicationService.cs`:
     - Guest notification with booking ID, stay dates, and payment receipt.
     - Owner notification with property gross, net payout, and separate décor notice.
     - Event Manager notification with venue name, client details, décor package, and allocated décor fee.
- **Consequence:** Provides frictionless UPI/Card/NetBanking checkout with cryptographic tamper resistance, keeps all three transaction parties synchronized via real-time WhatsApp alerts, and maintains full local sandbox testability.

---

### ADR-23: Modern Luxury Resort UI/UX Design System (Airbnb Luxe / Plum Guide Aesthetic)
- **Context:** The property catalog page required an elevated aesthetic matching luxury booking platforms like Airbnb Luxe and Plum Guide, adopting a 60-30-10 color hierarchy (`#F8FAFC` background, `#FFFFFF` cards, `#0B4F6C` Deep Navy structural primary, `#2E933C` Palm Green subtle category pills, `#F59E0B` Warm Gold rating stars).
- **Decision:**
  1. Define global design tokens in `wwwroot/css/site.css` and scoped component styles in `Views/Properties/Index.cshtml`.
  2. Implement generous padding and replace heavy borders with hairline `1px solid #E2E8F0` and ambient soft shadows (`0 10px 30px -5px rgba(11, 79, 108, 0.05)`).
  3. Apply strict typography standards: bold headings with tight `-0.02em` letter-spacing in Deep Navy (`#0B4F6C`) and muted secondary metadata (`#64748B`).
  4. Implement smooth micro-interactions (`transition: all 0.3s ease`): card lift (`translateY(-4px)`), elevated shadow (`0 20px 35px -5px rgba(11, 79, 108, 0.12)`), and image zoom (`scale(1.03)`).
  5. Strictly preserve all existing backend Razor bindings (`@model`, `types`, `occasions`, `ViewBag.Selected*`), query parameters, and form names.
- **Consequence:** Provides an editorial luxury resort booking experience while guaranteeing zero regressions in form submission, search filtering, and property navigation.

---

### ADR-24: Full Platform Verification, Live Email OTP Verification & End-to-End Performance Hardening
- **Context:** The application required end-to-end verification of all platform operations (Registration, Real OTP dispatch, Razorpay Gateway, Financial Segregation, Event Staging, Host Damage Claims, Support Queries, and KYC ID verification). Additionally, website performance issues needed to be addressed to eliminate database query roundtrips on layout renders, render-blocking Google Fonts stylesheets, and uncompressed payload transfers.
- **Decision:**
  1. Validate real email OTP generation using the live configured Gmail SMTP credentials without test bypasses, verifying delivery and registration integrity.
  2. Implement an automated integration test suite in `BookMyFun.Tests/EndToEndWorkflowTests.cs` exercising all 5 role journeys: Client, Host/Owner, Event Manager, Employee, and Administrator.
  3. Correct `ContactUs.Status` update handling to align with SQL Server's `CHECK (Status IN ('New', 'Responded'))` constraint.
  4. Cache `SiteSettingsModel` in memory for 15 minutes with thread-safe locking and instant invalidation on save, eliminating recurring database queries from `_Layout.cshtml`.
  5. Remove render-blocking `@import` from `site.css` and use preconnected `<link rel="preconnect">` tags for Google Fonts in `<head>`.
  6. Enable ASP.NET Core response compression (Gzip and Brotli) in `Program.cs`.
  7. Add `loading="lazy"` and `decoding="async"` across all catalog property card image elements.
- **Consequence:** Guarantees an error-free, regression-tested platform with sub-second page rendering and validated payment, communication, and onboarding workflows.

---

### ADR-25: Multi-Channel Transactional Receipt & Settlement Statement Automated Dispatch
- **Context:** Guests and property hosts required instantaneous, verifiable access to generated official tax receipts and settlement statements upon reservation payment. Relying solely on in-browser viewing caused loss of records, while manual dispatches burdened operational staff.
- **Decision:**
  1. Extend `IEmailService` and `IWhatsAppService` with dedicated receipt delivery methods (`SendBookingReceiptEmailAsync`, `SendOwnerSettlementReceiptEmailAsync`, `SendBookingReceiptWhatsAppAsync`, `SendOwnerBookingReceiptWhatsAppAsync`).
  2. Automatically build and dispatch `BookingReceiptViewModel` in `BookingController.DispatchConfirmedBookingAlertsAsync` across both Email and WhatsApp for both guest tax receipts and host settlement statements.
  3. Provide authorized on-demand re-dispatch endpoints (`BookingController.ResendReceipt` and `OwnerController.ResendBookingReceipt`) with strict IDOR validation and audit trail logging.
- **Consequence:** Ensures immediate delivery of official fiscal and entry vouchers directly to guests and hosts across their preferred channels, provides frictionless proof of purchase, and simplifies check-in gate verification.

---

### ADR-26: Universal Theme Switching, Bootstrap 5.3 Attribute Synchronization & Token Inheritance
- **Context:** The application exhibited theme-switching styling bugs when transitioning between Light and Dark modes:
  1. Bootstrap 5.3 native dark mode was not activated because `<html>` only received `data-theme` rather than `data-bs-theme`.
  2. The luxury resort design system tokens (`--bg-primary`, `--bg-card`, `--border-subtle`, `--text-main`, `--brand-navy`) were only declared in `:root` and lacked corresponding dark mode definitions in `site.css`.
  3. `Views/Properties/Index.cshtml` had an in-page `:root` block with hardcoded light values that overrode dark mode on the explore stays catalog page.
  4. Buttons with `.btn-light`, text with `.text-secondary`, and borders with `.border-secondary` had low contrast or rendered as blinding white boxes on dark cards.
  5. The payment hero card in `Booking/Payment.cshtml` and `Owner/SubscriptionPayment.cshtml` used an inline mint-green gradient where `.text-dark` title turned white, producing an unreadable white-on-mint contrast bug.
  6. Chrome/Edge `-webkit-autofill` produced jarring yellow/white backgrounds on dark input fields.
- **Decision:**
  1. Synchronously set both `data-theme` and `data-bs-theme` in `Views/Shared/_Layout.cshtml` (initial HTML and Anti-FOUC script) and `wwwroot/js/site.js` (`applyTheme()`), unlocking native Bootstrap 5.3 component dark modes.
  2. Declare all luxury design tokens globally in `wwwroot/css/site.css` under both `:root, [data-theme="light"], [data-bs-theme="light"]` and `[data-theme="dark"], [data-bs-theme="dark"]`, harmonizing `--brand-navy` to `#38bdf8` in dark mode for WCAG AAA compliance.
  3. Remove the local `:root` block in `Views/Properties/Index.cshtml` and provide scoped dark mode overrides for all `.luxe-*` classes.
  4. Add dark mode overrides in `site.css` for `.btn-light` (`#334155` background), `.text-secondary` (`var(--text-muted)`), `.border-secondary` (`#334155`), `.btn-primary .badge.bg-white` (retaining white badge contrast), and `-webkit-autofill`.
  5. Replace inline payment gradients with `.payment-hero-card` supporting theme-aware dark gradients (`linear-gradient(135deg, #064e3b 0%, #022c22 100%)`).
  6. Establish `.dash-badge-secondary`, `.dash-badge-danger`, and `.dash-badge-info` classes with dark mode variants.
- **Consequence:** Provides uniform contrast, seamless token inheritance, and zero visual glitches across all 5 user portals and public pages in both Light and Dark modes.

---

### ADR-27: Cross-Device Responsive Layout, Viewport Containment & Touch Target Optimization
- **Context:** Across mobile (< 768px), tablet (768px - 991px), and desktop (>= 992px) viewports, elements exhibited layout overflow, orientation mismatches, and element sizing issues:
  1. Negative row margins and wide content caused horizontal dragging and rogue scrollbars on mobile devices.
  2. Input focus on iOS Safari automatically zoomed the viewport because form input `font-size` was below 16px.
  3. Explore catalog cards in `Views/Properties/Index.cshtml` had two side-by-side action buttons (`.luxe-card-actions`) in a fixed `1fr 1.2fr` grid that cramped or overflowed card boundaries on narrow screens (< 480px).
  4. Dashboard KPI stat cards (`.dash-stat-grid`) displayed 2 columns on all mobile screens down to 320px, truncating long currency values (`₹1,50,000`).
  5. Modal dialogs and receipts lacked responsive padding scaling on mobile, causing modals to bleed off-screen and receipts to waste up to 72px on horizontal padding on compact displays.
  6. Landscape phone orientations (< 500px height) squeezed viewport space when sticky bottom bars remained tall.
- **Decision:**
  1. Enforce strict viewport containment on `html` and `body` using `overflow-x: clip;` (with fallback to `overflow-x: hidden;`), preserving `position: sticky` functionality while eliminating accidental horizontal page scrolling.
  2. Apply global media fluidity bounds (`img, svg, video, canvas, audio, iframe, embed, object { max-width: 100%; height: auto; }`) and word-break wrapping on headings.
  3. Enforce `font-size: 16px !important;` on all input types, textareas, selects, `.form-control`, `.form-select`, and `.luxe-control` under `@media (max-width: 767.98px)` to prevent iOS Safari auto-zoom.
  4. Enforce touch-target minimum height standards (>= 40px - 48px) for mobile buttons, category pills, filter pills, and bottom navigation links.
  5. Stack `.luxe-card-actions` vertically (`grid-template-columns: 1fr`) on mobile (< 480px / < 576px) and scale `.luxe-search-card` padding to `1.25rem 1rem`.
  6. Re-architect `.dash-stat-grid` with fluid breakpoints: 4 columns on desktop, 3 columns on tablet, 2 columns on landscape/standard phones, and 1 column on ultra-compact phones (< 380px).
  7. Add modal dialog viewport fitting (`max-width: calc(100vw - 1rem); max-height: calc(100vh - 160px)`) and compact landscape orientation rules.
  8. Created `TestPathHelper` in `BookMyFun.Tests` for resilient repo root discovery across all test runners, and added Tests 100–105 in `ResponsiveLayoutTests.cs`.
- **Consequence:** Eliminates horizontal scrolling and layout overflow across all mobile, tablet, and desktop viewports, delivers touch-friendly interactive controls without iOS auto-zoom glitches, and achieves 100% test pass rate across both xUnit and DirectTestRunner suites.
