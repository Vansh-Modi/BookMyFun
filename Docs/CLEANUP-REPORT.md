# BookMyFun — Cleanup & Inventory Report (Phase 1)

This report details the architectural inventory, unused code, duplicate logic, unwanted files, protected assets, and identified secrets/hard-coded values across the BookMyFun ASP.NET Core solution. No deletions have been executed in this phase.

---

## 1. Solution Structure Overview

The solution consists of:
- **Web Project (`BookMyFun.csproj`)**: ASP.NET Core (.NET 10.0 target) MVC application with Razor views, raw ADO.NET data access via `Microsoft.Data.SqlClient`, ClosedXML, Google OAuth, and System.Drawing.Common.
- **Test Project (`BookMyFun.Tests/BookMyFun.Tests.csproj`)**: xUnit test project containing 183 unit and integration test cases covering authentication, access control, availability calculations, calendar ranges, commission tiers, legal documents, and UI workflows.
- **Entry Point (`Program.cs`)**: Configures DI services, HTTP pipeline, authentication cookies, antiforgery, static file providers, and database initialization.
- **Controllers (`Controllers/`)**: 12 controller classes (`AccountController`, `AdminController`, `BookingAndPropertiesController` containing `PropertiesController` and `BookingController`, `ClientController`, `EmployeeController`, `EventManagerController`, `HomeController`, `MediaController`, `NotificationController`, `OwnerController`, `RazorpayWebhookController`, `SettingsController`).
- **Services (`Services/`)**: 17 services covering pricing, commissions, payment gateways, media storage, image optimization, notifications, refunds, damage claims, identity verification, legal documents, availability, and privacy scanning.
- **Repositories (`Repositories/`)**: 18 repository classes and interfaces executing raw ADO.NET queries against SQL Server.
- **Data Access (`Data/`)**: `DBHelper.cs` (ADO.NET connection manager), `DatabaseInitializer.cs` (idempotent schema updates), and `DatabaseSeeder.cs` (initial demo/reference data).
- **Models (`Models/`)**: Entities defined in `Models/Entities/Entities.cs`, ViewModels defined in `Models/ViewModels/ViewModels.cs`, and `Models/ErrorViewModel.cs`.
- **Views (`Views/`)**: 87 Razor `.cshtml` view templates organized by area/role (`Account`, `Admin`, `Booking`, `Client`, `Employee`, `EventManager`, `Home`, `Owner`, `Properties`, `Shared`).
- **Static Assets (`wwwroot/`)**: Bootstrap 5, jQuery, custom CSS (`site.css`), theme manager (`site.js`), client-side WebP image optimizer (`imageOptimizer.js`), placeholder assets, and uploaded media directories (`uploads/photos`, `uploads/videos`).
- **Database Scripts (`/db` & root)**: Canonical schema in `db/schema.sql`, performance indexes in `Database_AddPerformanceIndexes.sql`, and full setup in `Database_Setup_MonsterASP.sql`.

---

## 2. Unused Code

### a. Unused Classes & ViewModels
| Item | Location | Check Method | Findings |
| :--- | :--- | :--- | :--- |
| `UpdatePropertyLocationUrlViewModel` | `Models/ViewModels/ViewModels.cs:165` | Workspace grep across `Controllers/`, `Views/`, `Services/`, `Repositories/`, `BookMyFun.Tests/` | 0 references found anywhere. Model was replaced by inline location URL binding. Safe to remove. |
| `MapLinkChangeRequestViewModel` | `Models/ViewModels/ViewModels.cs:892` | Workspace grep across `Controllers/`, `Views/`, `Services/`, `Repositories/`, `BookMyFun.Tests/` | 0 references found anywhere. Replaced by direct owner request handling. Safe to remove. |

### b. Unused Views & Partial Views
| Item | Location | Check Method | Findings |
| :--- | :--- | :--- | :--- |
| `_AdminSidebar.cshtml` | `Views/Admin/_AdminSidebar.cshtml` | Grepped across all `.cshtml` and `.cs` files | 0 references. Superseded by the unified `Views/Shared/_DashboardSidebarNav.cshtml` partial. Safe to remove in Phase 2. |
| `Index.cshtml` (EventManager) | `Views/EventManager/Index.cshtml` | Compared against `Views/EventManager/Dashboard.cshtml` | 356-line duplicate of `Dashboard.cshtml`. `EventManagerController` defines both `Index()` and `Dashboard()`. Redirecting `Index()` to `Dashboard()` eliminates this redundant view. |

### c. View Components
- **Search Method**: Searched all `.cs` files for `: ViewComponent` and `ViewComponent`.
- **Result**: No ViewComponents exist in the codebase.

### d. Unused Methods & Redundant Routes
| Method | Location | Check Method | Findings |
| :--- | :--- | :--- | :--- |
| `Login(string? returnUrl)` | `Controllers/EventManagerController.cs:46` | Route grep in views and scripts | Redundant one-line redirect to `/Account/Login`. Route should be handled uniformly via cookie authentication challenge. |

### e. Unused CSS & JS Selectors
| Selector | Location | Check Method | Findings |
| :--- | :--- | :--- | :--- |
| `.admin-sidebar`, `.admin-nav` | `wwwroot/css/site.css` | Grep across active views | Associated solely with the obsolete `_AdminSidebar.cshtml`. |

---

## 3. Duplicate Code & Proposed Shared Implementations

| Area | Occurrences | Proposed Shared Implementation |
| :--- | :--- | :--- |
| **Password Hashing & Verification** | `AccountController.HashPassword` (raw unsalted SHA256) and `PartnerLeadService.HashPassword` (raw unsalted SHA256). | Extract into `IPasswordHasher` / `PasswordService` using ASP.NET Core Identity's PBKDF2 implementation (`PasswordHasher<User>`). Provide an automatic upgrade path upon successful login for accounts using legacy SHA-256 or plaintext passwords. |
| **User ID & Role Claim Extraction** | `GetCurrentUserId()` duplicated across 7 controllers (`AccountController`, `AdminController`, `EmployeeController`, `OwnerController`, `EventManagerController`, `NotificationController`, `MediaController`). | Create `ClaimsPrincipalExtensions.cs` with `GetUserId(this ClaimsPrincipal user)` and `GetUserRole(this ClaimsPrincipal user)` returning safe integer IDs and role strings with error handling. |
| **Employee vs Admin Duplication** | `EmployeeController.cs` (831 lines) reproduces nearly identical actions of `AdminController.cs` (`ManageProperties`, `ManageOwners`, `ManageBookings`, `OwnerRequests`, `PartnerLeads`, `SupportQueries`, `DamageClaims`, `RefundsQueue`, `IdVerifications`, `EventCommissions`). 14 views under `Views/Employee/` duplicate `Views/Admin/`. | Move common management business logic to services, and unify views where possible by passing role context into shared views/partials. |
| **SQL Pagination Offset Syntax** | `ConfigRepository.cs` line 203-213 interpolates integer offsets directly into SQL (`OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY`). | Parameterize with `@Offset` and `@PageSize` `SqlParameter` objects, matching the secure pattern in `PropertyRepository.cs`. |
| **Relative Time Helper** | `NotificationController.cs` defines a private `GetTimeAgo(DateTime utcDate)` method. | Extract to `DateTimeExtensions.ToTimeAgo(this DateTime utcDate)` under `Services/Common/`. |
| **File Upload Validation & Path Security** | Image/video extension checks, size bounds, and filename generation are duplicated in `MediaController`, `MediaStorageService`, `EventManagerController`, and `AdminController`. | Unify validation inside `MediaStorageService` (allowed extensions, magic bytes/signature verification, 2MB photo / 100MB video caps, GUID renaming). |
| **Contact Info Masking** | Contact filtering across bio, descriptions, and package details. | Route all contact privacy scanning through `IContactPrivacyScanner`. |

---

## 4. Unwanted Files

| File / Folder | Size / Type | Reason for Removal / Ignorance |
| :--- | :--- | :--- |
| `BookMyFun-MonsterASP-Deploy.zip` | 12.3 MB Zip | Stale deployment artifact from Oct 23, 2026. |
| `BookMyFun-Publish.zip` | 14.3 MB Zip | Stale publish artifact from Nov 1, 2026. |
| `BookMyFun_MonsterASP_Upload.zip` | 14.3 MB Zip | Stale publish artifact from Nov 1, 2026. |
| `publish.zip` | 13.3 MB Zip | Stale publish artifact from Nov 1, 2026. |
| `publish/` (entire directory) | Directory (~60 MB) | Compiled binaries, assemblies, and static web assets from old publish builds. Must not be tracked in source control. |
| `scripts/schema.sql` | 54.6 KB SQL | 100% byte-for-byte duplicate of `db/schema.sql`. `/db` is the canonical schema directory. |
| `Views/Admin/_AdminSidebar.cshtml` | 167 lines Razor | Obsolete sidebar view, superseded by unified `_DashboardSidebarNav.cshtml`. |
| `Views/EventManager/Index.cshtml` | 356 lines Razor | Duplicate of `Views/EventManager/Dashboard.cshtml`. |
| `logs/` | Directory | Runtime log output directory. Should be dynamically created at startup and ignored by git. |

---

## 5. Protected Files (Must NOT Be Deleted)

The following files are strictly required by routes, views, DI registrations, database initialization, or build configurations and must be preserved:
1. **Controllers**: `AccountController.cs`, `AdminController.cs`, `BookingAndPropertiesController.cs`, `ClientController.cs`, `EmployeeController.cs`, `EventManagerController.cs`, `HomeController.cs`, `MediaController.cs`, `NotificationController.cs`, `OwnerController.cs`, `RazorpayWebhookController.cs`, `SettingsController.cs`.
2. **Services**: All classes and interfaces under `Services/`.
3. **Repositories**: All classes and interfaces under `Repositories/`.
4. **Data Access**: `Data/DBHelper.cs`, `Data/DatabaseInitializer.cs`, `Data/DatabaseSeeder.cs`.
5. **Database Scripts**: `db/schema.sql`, `Database_Setup_MonsterASP.sql`, `Database_AddPerformanceIndexes.sql`.
6. **Active Views**: All `.cshtml` views under `Views/` (except the 2 confirmed redundant files in Section 4).
7. **Static Assets**: `wwwroot/css/site.css`, `wwwroot/js/site.js`, `wwwroot/js/imageOptimizer.js`, `wwwroot/favicon.ico`, library assets (`wwwroot/lib/`), and placeholder images.
8. **Configuration & Project**: `appsettings.json`, `appsettings.Development.json`, `web.config`, `Program.cs`, `BookMyFun.csproj`, `BookMyFun.slnx`.
9. **Test Project**: All test suites under `BookMyFun.Tests/`.

---

## 6. Hard-coded Values & Secrets Found

| File | Line | Key / Variable | Finding & Action Taken |
| :--- | :--- | :--- | :--- |
| `appsettings.json` | 10 | `ConnectionStrings:DefaultConnection` | Contained local SQL Express connection string. Replaced with empty string `""`. |
| `appsettings.json` | 16 | `Authentication:Google:ProjectEmail` | Contained hardcoded Gmail address. Replaced with empty string `""`. |
| `appsettings.json` | 20 | `Razorpay:KeyId` | Contained hardcoded demo key. Replaced with empty string `""`. |
| `appsettings.json` | 21 | `Razorpay:KeySecret` | Contained hardcoded demo secret. Replaced with empty string `""`. |
| `appsettings.json` | 31 | `Smtp:Username` | Contained hardcoded Gmail address. Replaced with empty string `""`. |
| `appsettings.json` | 32 | `Smtp:Password` | **REAL SECRET DETECTED**: Contained live 16-character Gmail App Password. Replaced with empty string `""`. Secret was not logged or printed. |
| `appsettings.json` | 33 | `Smtp:From` | Contained hardcoded Gmail address. Replaced with empty string `""`. |
| `appsettings.Development.json` | 9 | `ConnectionStrings:DefaultConnection` | Contained local SQL Express connection string. Replaced with empty string `""`. |
| `Data/DatabaseSeeder.cs` | 12-16 | Demo hashes | Hardcoded development passwords (`Admin@123`, `Emp@123`, etc.) used strictly for seeding initial local demo accounts. |
| `Database_Setup_MonsterASP.sql` | 499-523 | Demo hashes | Base64 SHA256 hashes corresponding to initial demo seed users. |
| `Services/PaymentGatewayService.cs` | 114 | Dummy test keys | Fallback check string comparison for `"YOUR_KEY"` and `"TEST_KEY"`. |
| `Services/SiteSettingsService.cs` | 98-105 | `DefaultFallbackStays` | In-memory fallback list for popular stays if database query is empty. |

---

## 7. Phase 1 Verification

- **Build Status**: Executed `dotnet build` — succeeded with **0 Errors** and **0 Warnings**.
- **Test Suite**: Executed `dotnet test` — **183 Passed, 0 Failed**.
- **Secrets Cleared**: All secret fields in tracked configuration files are now empty strings `""`.
- **Deletions Executed**: 0 files deleted (adhering strictly to Phase 1 rules).


---

## 7. Execution & Resolution Summary (Phases 2 - 10)

| Phase | Category | Action Taken | Current Status |
| :--- | :--- | :--- | :--- |
| **Phase 2** | Redundant Controllers | Split `BookingAndPropertiesController.cs` into standalone `Controllers/PropertiesController.cs` and `Controllers/BookingController.cs`. Removed composite file. | **Complete & Verified** |
| **Phase 2** | Duplicate Services | Split `BookingAndCommissionService.cs` into `Services/BookingPricingService.cs` and `Services/CommissionService.cs`. Removed composite file. | **Complete & Verified** |
| **Phase 2** | Obsolete Views | Deleted `Views/Admin/_AdminSidebar.cshtml` and `Views/EventManager/Index.cshtml` in favor of unified navigation. | **Complete & Verified** |
| **Phase 2** | Security / Passwords | Added `PasswordHasherService.cs` implementing OWASP PBKDF2 HMAC-SHA256 (100,000 iterations) with seamless SHA-256 legacy migration. | **Complete & Verified** |
| **Phase 2** | Obsolete Archives | Removed root backup archives (`publish.zip`, `BookMyFun-MonsterASP-Deploy.zip`). | **Complete & Verified** |
| **Phase 3** | Configuration Safety | Introduced strongly-typed `ApplicationOptions.cs` with validation. Converted `Program.cs` to fail-fast on missing `DefaultConnection` without logging secrets. Added `/health` endpoint. | **Complete & Verified** |
| **Phase 3** | Environment & Web.Config | Set `ASPNETCORE_ENVIRONMENT` to `Production` in `web.config` with `stdoutLogEnabled="false"`. Created runtime directories (`logs/`, `wwwroot/uploads/`, `App_Data/DataProtectionKeys/`). | **Complete & Verified** |
| **Phase 4** | Authentication & Cookies | Configured secure cookie policy (`HttpOnly`, `SameSite=Lax`, `Secure` in Production, 8h sliding, 24h absolute cap). Added file-system Data Protection key persistence. | **Complete & Verified** |
| **Phase 4** | Security Middleware | Integrated `LoginLockoutService` (5 failed attempts per 15 min), OTP rate limiting (3 requests per 10 min), parameterized SQL across repositories, `HtmlSanitizerService`, Brotli/Gzip response compression, and security headers. | **Complete & Verified** |
| **Phase 5** | Error Handling | Implemented `CustomErrorHandlerService` and redesigned `Views/Shared/Error.cshtml` with branded 404, 403, and 500 error cards. Handled JSON errors for AJAX. Added retry link for `PaymentFailed`. | **Complete & Verified** |
| **Phase 6** | Checkout Experience | Implemented `CheckoutEventManagerService` with occasion-first selection flow. Hid décor add-ons until occasion is chosen. Enforced server-side validation and price snapshots. | **Complete & Verified** |
| **Phase 7** | Public Profile View | Built `Views/EventManager/PublicProfile.cshtml` and `/EventManagers/Profile/{id}` route. Enforced 404 for unverified/inactive partners, approved-only portfolio items, and zero contact leakage. | **Complete & Verified** |
| **Phase 8** | Self-Service Profile | Enforced `EventManagerProfileInput` DTO restricting model binding to allowed fields. Built `Views/EventManager/Profile.cshtml` and `Views/EventManager/Inbox.cshtml`. Watermarked portfolio uploads defaulting to `IsApproved = 0`. Private storage for KYC documents. | **Complete & Verified** |
| **Phase 9** | Admin Moderation | Built portfolio approval/rejection moderation, partner verification adjudication, flagged contact resolution, and one-click account suspension for contact leakage violations with audit logging. | **Complete & Verified** |
| **Phase 10** | Verification & Handover | Codebase audited for forbidden patterns, all 234 unit/integration tests passing, 0 compiler warnings, 0 secrets in tracked files, git state uncommitted. | **Complete & Verified** |
