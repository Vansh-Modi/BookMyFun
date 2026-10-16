# BookMyFun — Premier Farmhouse, Luxury Villa & Resort Rental Platform

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![SQL Server](https://img.shields.io/badge/SQL%20Server-2022-CC292B?logo=microsoftsqlserver&logoColor=white)](https://www.microsoft.com/sql-server)
[![Bootstrap 5](https://img.shields.io/badge/Bootstrap-5.3-7952B3?logo=bootstrap&logoColor=white)](https://getbootstrap.com/)
[![ClosedXML](https://img.shields.io/badge/ClosedXML-0.104.2-059669?logo=microsoftexcel&logoColor=white)](https://github.com/ClosedXML/ClosedXML)
[![Security: OWASP Hardened](https://img.shields.io/badge/Security-OWASP%20Hardened-blue)](https://owasp.org/)
[![License: Proprietary](https://img.shields.io/badge/License-Proprietary-red)](#)

---

## 1. Project Overview

**BookMyFun** is an enterprise-grade multi-role farm, luxury villa, and private resort rental system designed for weekend getaways, family reunions, corporate retreats, pool parties, and weddings. Built on high-performance ASP.NET Core (.NET 10) and SQL Server utilizing raw parameterized ADO.NET, the platform combines intuitive client booking workflows with back-office operations, host protection, and real-time executive analytics.

The system features four dedicated role dashboards:
1. **Client (Guest / Customer):** Discovery, multi-criteria geo-search, flexible booking slots (Full Day, Morning/Evening Half-Day, Hourly), secure checkout via Razorpay, Government ID verification (KYC), transparent cancellation quotes with automated refund calculation, and verified guest reviews.
2. **Owner (Farm Host / Partner):** Vetted partner onboarding, dual-mode media management (direct file upload with magic-byte verification + external video/image URL embedding), 5-photo minimum enforcement, amenities taxonomy, interactive Google Maps pin selector, subscription tiers with listing quotas, and a 24-hour post-checkout security deposit damage claim module.
3. **Employee (Operations & Customer Support):** Partner lead intake pipeline, client KYC verification with privacy-shielded vault (hosts never view raw guest IDs), cancellation refund operations queue with staff overrides, damage claim adjudication, and listing moderation.
4. **Admin (Superuser):** Complete platform control, real-time dynamic financial analytics (GMV, platform commission, pending host payouts, occupancy rates), ClosedXML Excel (`.xlsx`) and CSV asynchronous report exports, role management, audit logging, and global white-label settings.

---

## 2. Key Platform Features

```
                               ┌───────────────────────────────────────────────┐
                               │             BookMyFun Ecosystem               │
                               └───────┬───────────────────────────────┬───────┘
                                       │                               │
                ┌──────────────────────▼──────────┐ ┌──────────────────▼──────────────────────┐
                │        Client & Public Stays    │ │        Host & Operations Engine         │
                ├─────────────────────────────────┤ ├─────────────────────────────────────────┤
                │ • Location & Occasion Search    │ │ • Lead Intake & Encrypted Host Invites  │
                │ • Full-Day, Half-Day & Hourly   │ │ • Dual-Mode Media (Upload + URLs)       │
                │ • PCI-DSS Razorpay Checkout     │ │ • Dynamic 3-Tier Commission Engine      │
                │ • Transparent Refund Engine     │ │ • 24h Post-Checkout Damage Claims       │
                │ • KYC Vault with Expiring URLs  │ │ • Host Subscription Quotas (Starter/VIP)│
                │ • Instant Dark/Light Mode Switch│ │ • ClosedXML Excel & CSV Analytics Engine│
                └─────────────────────────────────┘ └─────────────────────────────────────────┘
```

### 1. Flexible Booking Engine & Hourly Slots
- **Full-Day Stays:** Standard overnight reservations (e.g., 11:00 AM check-in to 10:00 AM check-out).
- **Half-Day Slots:** Configurable daytime (`MorningSlot`, 09:00 AM - 05:00 PM) or evening party slots (`EveningSlot`, 06:00 PM - 08:00 AM next day).
- **Hourly Stays:** Short-duration reservations (e.g. 4h, 6h, 8h) with automated pro-rated pricing.

### 2. Transparent Cancellation & Refund System
- Property-level policy configuration: `FreeCancellationWindowHours`, `CancellationCutoffHours`, `CancellationFeeType`, and `CancellationFeeValue`.
- Immutable snapshot saved to `Bookings` at creation time.
- **Refund Engine Tiers:**
  - *Before Free Cancellation Window:* 100% refund minus 2% gateway fee.
  - *Between Window and Cutoff:* Gross booking amount minus cancellation fee (percentage or fixed) and gateway fee.
  - *Inside Cutoff Window:* Non-refundable ($0$ refund).
- Client-side confirmation modal with real-time fee breakdown.
- Staff triage queue with manual override and gateway refund tracking.

### 3. Dual-Mode Media & Assets Manager
- **Direct File Upload (`/api/media/upload-file`):** Server-side magic-byte header inspection (JPG, PNG, WEBP $\le$10MB; MP4, MOV, WEBM $\le$100MB). Files sanitized with cryptographically random UUIDs.
- **External URL / Embed (`/api/media/verify-url`):** Direct image links or video streaming links (YouTube, Vimeo, Cloudinary) with SSRF defense (blocking internal RFC 1918, loopback, and cloud metadata `169.254.169.254`).
- **5-Photo Minimum:** Enforced listing quality rule with live counter badge.
- **Video Tour Toggle:** Toggle between direct video upload and YouTube/Vimeo embed.

### 4. Partner Intake & Encrypted Host Onboarding
- Public self-serve registration for property owners is completely disabled.
- Public "Partner With Us / List Your Farm" lead intake form (`/Home/Partner`).
- Admin/Employee review queue with **"Approve & Invite Owner"** action that creates an inactive account and delivers a time-limited 48-hour cryptographic token for activation (`/Account/ActivateHost`).

### 5. ID Verification & Privacy Governance (KYC)
- Guests upload Government ID (Aadhaar, Passport, Driving License) into a private vault (`App_Data/PrivateDocuments`).
- Raw documents are strictly shielded from Property Hosts (hosts only view a "Verified Guest" badge or masked last-4 digits).
- Staff view documents using 15-minute expiring signed tokens (`HMACSHA256`).

### 6. Security Deposit Hold & 24h Damage Claim Architecture
- Refundable security deposit collected at checkout via gateway hold.
- Hosts can file a damage claim within 24 hours of guest checkout with mandatory itemized losses and photographic/video proof.
- Operations staff adjudicate the claim: approved deductible is transferred to the host, and the remaining balance is released back to the guest.

### 7. Dynamic Commission Hierarchy
- Commission resolution hierarchy:
  $$\text{Effective Commission} = \text{Property.CommissionRate} \;\;?\?\;\; \text{Owner.CustomCommissionRate} \;\;?\?\;\; \text{GlobalDefault (10\%)}$$
- Commission snapshot permanently persisted onto each booking record.

### 8. Dynamic Analytics & Financial Reporting Engine
- Real-time KPIs: Gross Merchandise Value (GMV), Net Platform Commission, Pending Host Payouts, Active Properties, and Occupancy Rate.
- Multi-dimensional filtering: Today, 7D, MTD, YTD, and Custom Date Picker + Property, Owner, and Booking Status filters.
- **Office Exports:**
  - **Excel (.xlsx):** Formatted workbook via ClosedXML with emerald-branded headers and column auto-sizing.
  - **CSV Export:** Standardized UTF-8 CSV stream for external BI pipelines.

### 9. Dark Mode & Light Mode Theming
- Instant theme toggle in top navigation.
- Anti-FOUC (Flash of Unstyled Content) pre-paint script restoring user preference from `localStorage`.
- Comprehensive CSS custom variables for seamless contrast across all dashboards.

---

## 3. Technology Stack

| Layer | Technologies |
| :--- | :--- |
| **Framework & Runtime** | ASP.NET Core 10.0 (`net10.0`), C# 13 |
| **Database & ORM** | Microsoft SQL Server 2022 via Raw Parameterized ADO.NET (`Microsoft.Data.SqlClient 7.1.1`) |
| **Front-End UI** | Razor Views, Bootstrap 5.3, Bootstrap Icons 1.11, Custom Responsive CSS |
| **Spreadsheet Engine** | ClosedXML 0.104.2 (Native Excel `.xlsx` generation without MS Office dependencies) |
| **Payment Gateway** | Razorpay SDK (Order creation, Webhook HMAC-SHA256 signature verification) |
| **Security & Middleware** | Custom OWASP Security Headers (CSP, HSTS, X-Frame-Options), Rate Limiting Middleware, Global Exception Filter |

---

## 4. Getting Started & Installation

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Microsoft SQL Server 2019/2022 (Express, Developer, or Enterprise) or Azure SQL Database
- Node.js & npm (optional, if customizing frontend assets)

### Setup Instructions

1. **Clone the repository:**
   ```bash
   git clone https://github.com/your-org/BookMyFun.git
   cd BookMyFun
   ```

2. **Configure Database Connection:**
   Update `appsettings.json` with your SQL Server connection string:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=localhost;Database=BookMyFunDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"
     }
   }
   ```

3. **Restore Dependencies & Build:**
   ```bash
   dotnet restore
   dotnet build --nologo
   ```

4. **Run the Application:**
   ```bash
   dotnet run
   ```
   *Note: On initial startup, `DatabaseInitializer.cs` automatically creates all required database tables, indexes, and initial master data (amenities, default roles, system settings).*

5. **Navigate to the Application:**
   Open your browser and visit `https://localhost:7194` or `http://localhost:5242`.

---

## 5. Default Role Accounts & Testing Personas

The database initializer provisions initial test accounts:

| Role | Email | Password | Primary Capabilities |
| :--- | :--- | :--- | :--- |
| **Admin** | `admin@bookmyfun.com` | `Admin@123` | Dynamic analytics, financial exports, refund overrides, KYC approvals, site branding |
| **Employee** | `employee@bookmyfun.com` | `Employee@123` | Partner leads pipeline, guest KYC verification, damage claims adjudication, support desk |
| **Owner (Host)** | `owner@bookmyfun.com` | `Owner@123` | Listing creation with dual upload, calendar, damage claims within 24h, subscription plans |
| **Client (Guest)** | `client@bookmyfun.com` | `Client@123` | Property booking, KYC upload, cancellation with real-time refund estimation, reviews |

---

## 6. Project Directory Structure

```
BookMyFun/
├── Controllers/                      # MVC and Web API Controllers
│   ├── AccountController.cs          # Authentication, host activation, login/register
│   ├── AdminController.cs            # Superuser console, analytics, queues, exports
│   ├── BookingAndPropertiesController.cs # Properties discovery, flexible checkout, cancellation quotes
│   ├── ClientController.cs           # Guest dashboard, booking history, KYC management
│   ├── EmployeeController.cs         # Operations queues (leads, refunds, claims, KYC)
│   ├── HomeController.cs             # Landing page, search, contact support, partner intake
│   ├── MediaController.cs            # File upload (magic bytes) & external URL verification (SSRF)
│   └── OwnerController.cs            # Host property CRUD, damage claims, subscriptions
├── Data/
│   ├── DatabaseInitializer.cs        # Idempotent DDL migration engine & table seeding
│   └── DBHelper.cs                   # Parameterized ADO.NET query execution helper
├── Docs/                             # Complete System Markdown Documentation
│   ├── Architecture.md               # System topology, RBAC matrix, request lifecycle
│   ├── PRD.md                        # Complete 14-module Product Requirements Document
│   ├── Design.md                     # UI/UX design tokens, dark mode, component library
│   ├── Rules.md                      # Engineering standards, OWASP, PCI-DSS, magic bytes
│   ├── Database.md                   # Complete Data Dictionary, DDL schemas, indexes
│   ├── API.md                        # Full REST and MVC endpoint reference
│   ├── Task.md                       # Master feature checklist & development roadmap
│   └── Memory.md                     # Architectural Decision Records (ADR-01 to ADR-08)
├── Models/
│   ├── Entities/                     # Relational database models (User, Property, Booking, etc.)
│   └── ViewModels/                   # UI ViewModels, DTOs, and filter parameters
├── Repositories/                     # Raw ADO.NET Data Access Repositories
│   ├── BookingRepository.cs
│   ├── PropertyRepository.cs
│   ├── UserRepository.cs
│   ├── PartnerLeadRepository.cs
│   ├── RefundRepository.cs
│   ├── DamageClaimRepository.cs
│   └── IdVerificationRepository.cs
├── Services/                         # Domain Logic & Business Engines
│   ├── BookingAndCommissionService.cs
│   ├── RefundEngineService.cs
│   ├── DamageClaimService.cs
│   ├── IdVerificationService.cs
│   ├── MediaStorageService.cs
│   ├── PartnerLeadService.cs
│   ├── Exceptions/AppExceptions.cs   # Centralized domain exception hierarchy
│   ├── Filters/GlobalExceptionFilter.cs # Unified error handling filter
│   └── Security/                     # Security headers & rate limiting middlewares
├── Views/                            # Razor Views organized by Role & Module
│   ├── Admin/                        # Dynamic analytics, refunds, claims, leads, KYC
│   ├── Client/                       # Bookings, KYC upload, cancellation quote modal
│   ├── Employee/                     # Operations staff queues (delegated partials)
│   ├── Home/                         # Landing page, partner lead intake, contact us
│   ├── Owner/                        # Property creation (dual upload), damage claims
│   ├── Properties/                   # Stay discovery, details, Google Maps, amenities
│   └── Shared/                       # _Layout with role dropdowns & dark mode toggle
└── wwwroot/                          # Static assets (CSS, JS, images)
```

---

## 7. Security & Compliance Highlights

- **Magic-Byte Binary Validation:** Direct file uploads inspect binary headers (e.g., `FF D8 FF` for JPEG, `89 50 4E 47` for PNG, `00 00 00` for MP4) before writing to disk, preventing malicious executable disguise.
- **SSRF Defense:** External media URLs are checked for `https://` protocol and their resolved IP addresses are verified against private RFC 1918 ranges, loopback addresses, and cloud metadata services.
- **PCI-DSS Principles:** Payment card details are never transmitted through or stored in application databases; tokenized payment processing is handled exclusively via secure gateway integrations.
- **Confidential KYC Vault:** Guest Government IDs are stored outside public web roots and accessed only via short-lived (15-minute) signed tokens. Hosts are strictly forbidden from viewing raw guest documents.
- **OWASP HTTP Headers:** Strict Content Security Policy (CSP), `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, and `Referrer-Policy: strict-origin-when-cross-origin`.

---

## 8. License & Support

BookMyFun is a proprietary platform. For commercial licensing, customization, or technical support:
- **Helpline:** +91 98765 43210
- **Email:** support@bookmyfun.com
- **Partner Inquiries:** partner@bookmyfun.com
