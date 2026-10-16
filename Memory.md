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


