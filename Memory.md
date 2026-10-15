# BookMyFun — Context Retention & Architectural Decision Tracker (Memory.md)

## 1. Project Context & Environment
- **Platform:** BookMyFun Farmhouse, Villa & Resort Rental System.
- **Runtime:** .NET 10 (`net10.0`), C# 13, ASP.NET Core MVC.
- **Database:** Microsoft SQL Server 2022 via Raw Parameterized ADO.NET (`Microsoft.Data.SqlClient 7.1.1`).
- **UI Framework:** Bootstrap 5, Bootstrap Icons, custom `Plus Jakarta Sans` typography, and CSS custom properties for instant light/dark theming.
- **Reporting & Office Exports:** ClosedXML 0.104.2 (Excel `.xlsx`), CSV streaming, formatted printable PDF HTML.

---

## 2. Key Architectural Decisions (ADRs)

### ADR-01: Dual File and URL Media Architecture
- **Context:** Hosts capture content on smartphones (direct files) or have professional walkthroughs hosted on YouTube/Vimeo/Cloudinary.
- **Decision:** Support both storage models in a single `PropertyMedia` entity with `SourceType IN ('file', 'url')`. Direct uploads are inspected for magic bytes and saved to cloud storage/disk with random UUID filenames. External URLs are validated with an SSRF filter (HTTPS only, rejecting private and cloud metadata IPs).
- **Consequence:** Eliminates server storage bottlenecks for long 4K videos while guaranteeing security against malicious file uploads and internal network probes.

### ADR-02: Strict Separation of Guest Government IDs from Property Hosts
- **Context:** Indian hospitality regulations require guest identity verification before check-in. However, giving property owners access to raw Aadhaar/Passport copies violates data privacy best practices and increases liability for identity theft.
- **Decision:** Store guest identity documents in a private directory with 15-minute expiring signed URLs. Strictly restrict access to the guest (owner), Employee, and Admin. Hosts only receive a signed "Verified Guest" badge or masked identifier (`XXXX-XXXX-1234`).
- **Consequence:** Fully satisfies regulatory KYC requirements while shielding guests from unauthorized PII exposure.

### ADR-03: Immutable Snapshotting of Policies and Commission at Booking Creation
- **Context:** If a host or administrator updates a property's price, commission rate, or cancellation policy, past or active bookings must not be retroactively altered.
- **Decision:** Whenever a booking is created, snapshot the active cancellation parameters (`FreeCancellationWindowHoursSnapshot`, `CancellationCutoffHoursSnapshot`, `CancellationFeeTypeSnapshot`, `CancellationFeeValueSnapshot`) and commission calculations (`CommissionRateSnapshot`, `PlatformCommissionAmount`, `OwnerPayoutAmount`) directly onto the `Bookings` record.
- **Consequence:** Guarantees historical financial ledger integrity and prevents cancellation disputes.

### ADR-04: Replacement of Public Host Registration with Partner Lead Pipeline
- **Context:** Open self-registration allowed fraudulent or unvetted hosts to publish listings without review.
- **Decision:** Remove the Owner role option from public registration. Implement a public "Partner With Us / List Your Farm" lead intake form. Employees or Admins review submissions, conduct verification, and issue an encrypted, time-limited activation token via email.
- **Consequence:** Establishes a verified partner network and prevents unauthorized listings.

### ADR-05: Strict 24-Hour Damage Claim Adjudication Window
- **Context:** Hosts need a mechanism to claim security deposits for damages, but guests must not be subjected to delayed or unsubstantiated claims.
- **Decision:** Hosts must file damage claims within 24 hours of guest checkout with itemized costs and photographic/video evidence. Claims are adjudicated by an Employee or Admin before any security deposit funds are disbursed.
- **Consequence:** Provides fair, documented dispute resolution and maintains guest trust.

### ADR-06: Centralized Custom Error Hierarchy & Global Filtering
- **Context:** Unhandled exceptions can crash requests and leak sensitive database schemas or stack traces to clients.
- **Decision:** Implement a clean domain exception hierarchy (`AppException`, `ValidationException`, `ForbiddenException`, `NotFoundException`, `PaymentGatewayException`) and catch them via a Global Exception Filter. Return standardized JSON responses for API calls and friendly error views with correlation IDs for web views.
- **Consequence:** Eliminates information disclosure vulnerabilities (OWASP A05) and delivers resilient user experiences.

### ADR-07: Operational Queues Architecture for Support & Admin
- **Context:** Both Admins (Superusers) and Employees (Operations Staff) perform triage and review of refunds, onboarding leads, damage claims, and KYC verifications.
- **Decision:** Share UI Razor views (`Views/Admin/*.cshtml` and `Views/Employee/*.cshtml`) through view delegation and dynamic `@controllerName` resolution based on the user's active role.
- **Consequence:** Provides complete role separation while eliminating code and template duplication.

### ADR-08: ClosedXML & CSV Asynchronous Analytics Reporting
- **Context:** Administrators need financial reports (GMV, platform commission, pending host payouts, occupancy rates) across flexible presets (Today, 7D, MTD, YTD, Custom) exportable for executive review.
- **Decision:** Use ClosedXML for native Excel (`.xlsx`) generation with custom styling and header coloring, plus streaming UTF-8 CSV exports.
- **Consequence:** Generates fast, professional spreadsheet deliverables without external office suite dependencies.
