# Roles, Authorization, and Access Control

## 1. Overview
BookMyFun defines five distinct user roles:
- `Client` (Guest traveler / event organizer)
- `Owner` (Farmhouse / Villa host)
- `EventManager` (Décor studio / event manager)
- `Employee` (Operations staff and moderation team)
- `Admin` (Platform system administrator)

Authentication is handled via ASP.NET Core Cookie Authentication with role claims (`ClaimTypes.Role`). Access control is enforced using ASP.NET Core authorization policies configured in [`Program.cs`](file:///d:/Projects/BookMyFun/Program.cs).

---

## 2. Authorization Policies

Configured in [`Program.cs`](file:///d:/Projects/BookMyFun/Program.cs):

| Policy Name | Permitted Roles | Protected Areas / Controllers |
|:---|:---|:---|
| `AdminOnly` | `Admin` | System settings, staff management, financial ledger, security audit logs |
| `RequireAdmin` | `Admin` | [`AdminController.cs`](file:///d:/Projects/BookMyFun/Controllers/AdminController.cs) |
| `RequireEmployee` | `Employee`, `Admin` | [`EmployeeController.cs`](file:///d:/Projects/BookMyFun/Controllers/EmployeeController.cs) |
| `RequireOwner` | `Owner`, `Admin` | [`OwnerController.cs`](file:///d:/Projects/BookMyFun/Controllers/OwnerController.cs) |
| `RequireEventManager` | `EventManager`, `Admin`, `Employee` | [`EventManagerController.cs`](file:///d:/Projects/BookMyFun/Controllers/EventManagerController.cs) |
| `RequireClient` | `Client`, `Admin` | [`ClientController.cs`](file:///d:/Projects/BookMyFun/Controllers/ClientController.cs) |

Public endpoints (`[AllowAnonymous]`):
- [`HomeController.cs`](file:///d:/Projects/BookMyFun/Controllers/HomeController.cs): Index, About, Contact, Partner application, Legal policy views.
- [`PropertiesController.cs`](file:///d:/Projects/BookMyFun/Controllers/PropertiesController.cs): Search catalog, property details.
- [`AccountController.cs`](file:///d:/Projects/BookMyFun/Controllers/AccountController.cs): Login, Register, ForgotPassword, ResetPassword, OTP verification.
- [`BookingController.cs`](file:///d:/Projects/BookMyFun/Controllers/BookingController.cs): `CelebrationEligibility` endpoint (public availability check). Checkout submission requires authentication; unauthenticated guests are redirected to `/Account/Login?returnUrl=...`.

---

## 3. Role Capabilities Matrix

### 3.1 Client (Guest)
- **Search & Explore**: Filters properties by city, dates, occasion, guest capacity, and pricing.
- **Booking**: Reserves full-day stays, half-day visits, or hourly slots.
- **Stay & Celebrate**: Can select add-on décor packages when booking more than 48 hours in advance in eligible service areas.
- **KYC Submission**: Uploads government identification (Aadhaar, Passport, Driving License) during checkout or in profile.
- **Payment & Receipts**: Completes Razorpay transactions and views printable GST receipts.
- **Cancellations**: Cancels reservations in `/Client/Index` subject to tiered platform refund rules.
- **Restricted From**: Accessing owner dashboards, managing listings, editing décor packages, adjudicating reviews, or viewing platform financial ledgers.

### 3.2 Owner (Host)
- **Listing Management**: Creates and edits properties (`/Owner/CreateProperty`, `/Owner/EditProperty`).
- **Calendar & Availability**: Views bookings and manages blacked-out calendar dates.
- **Subscriptions & Trials**: Receives a 60-day free trial on initial approval. Purchases Starter, Growth, or Premium listing subscriptions (`/Owner/Subscriptions`).
- **Deposit Claims**: Files security damage claims against guest bookings (`/Owner/DamageClaims`).
- **Notifications**: Configures email and WhatsApp alert preferences (`/Owner/NotificationSettings`).
- **Restricted From**: Approving their own properties, reviewing other hosts, editing system pricing, or modifying platform commission rates.

### 3.3 EventManager (Décor Partner)
- **Service Areas**: Manages serviceable cities and states (`/EventManager/ServiceAreas`).
- **Package Catalog**: Creates and updates décor packages across 5 categories (`/EventManager/Packages`).
- **Assigned Setups**: Views confirmed event setups and schedule times (`/EventManager/AssignedSetups`).
- **Portfolio Showcase**: Uploads photos of completed events (`/EventManager/UploadPortfolio`) for staff approval.
- **Profile Management**: Edits business details with automated contact privacy scanning (`/EventManager/Profile`).
- **Financials**: Views earnings and payout records (`/EventManager/Financials`).
- **Restricted From**: Displaying direct personal phone numbers, emails, social media handles, or external payment links.

### 3.4 Employee (Operations Staff)
- **Property Approvals**: Reviews newly submitted host properties (`/Employee/ManageProperties`). Approves or rejects with feedback.
- **KYC Queue**: Verifies guest identity documents (`/Employee/IdVerifications`).
- **Partner Onboarding**: Reviews prospective host and event manager applications (`/Employee/PartnerApprovals`).
- **Support Desk**: Resolves incoming contact tickets (`/Employee/SupportQueries`).
- **Disputes & Refunds**: Inspects refund requests (`/Employee/RefundRequests`) and damage claims (`/Employee/DamageClaims`).
- **Restricted From**: Managing staff accounts, creating or deleting admin users, editing subscription pricing, or modifying raw database configurations.

### 3.5 Admin (System Administrator)
- **Full Platform Oversight**: Access to all moderation and operation queues.
- **User & Staff Management**: Creates, activates, and deactivates employee accounts (`/Admin/ManageEmployees`) and host accounts (`/Admin/ManageOwners`).
- **Plan & Price Configuration**: Manages subscription plans and billing cycle prices (`/Admin/ManagePlanPrices`).
- **Privacy & Safety Audit**: Inspects intercepted contact details (`/Admin/FlaggedContacts`).
- **Financial Controls**: Processes booking refunds (`/Admin/RefundsQueue`) and views platform analytics (`/Admin/DynamicAnalytics`).
- **Platform Configuration**: Edits dynamic platform settings (`/Admin/PlatformSettings`).

---

## 4. Dashboard Visibility and Navigation Isolation

Navigation links are generated centrally by [`NavigationMenuDefinition.cs`](file:///d:/Projects/BookMyFun/Services/Navigation/NavigationMenuService.cs). The application enforces strict role isolation: each role sees only its own dashboard and links.

### 4.1 Header Dropdown
Rendered in `Views/Shared/_Layout.cshtml` via `NavigationMenuDefinition.GetHeaderDropdownItems(role)`:
- **Admin**: "Admin Dashboard", "Manage Bookings", "Manage Properties", "My Profile", "Log Out".
- **Employee**: "Employee Dashboard", "Property Approvals", "Guest KYC Queue", "My Profile", "Log Out".
- **Owner**: "Host Dashboard", "List New Farm", "Deposit Claims", "Subscription Plans", "My Profile", "Log Out".
- **EventManager**: "Event / Decor Dashboard", "Décor Packages", "Assigned Setups", "My Profile", "Log Out".
- **Client**: "My Bookings", "Receipts", "My Profile", "Log Out".
- **Unauthenticated**: "Sign In", "Register".

### 4.2 Sidebar Drawer
Rendered via `NavigationMenuDefinition.GetSidebarDrawerItems(role, isAuthenticated)`:
- **Unauthenticated Guest**: Shows public exploration routes ("Explore Stays", "Villas & Estates", "Farmhouses & Grounds", "Resorts & Venues", "Support & Help", "Sign In").
- **Authenticated Role**: Shows only that role's categorized menu headings and actions. Cross-role links are excluded.

### 4.3 Default Dashboard Routing
When navigating to the generic dashboard entrypoint, `NavigationMenuDefinition.GetDashboardRouteForRole(role)` maps:
- `Admin` -> `/Admin/Index`
- `Employee` -> `/Employee/Index`
- `Owner` -> `/Owner/Index`
- `EventManager` -> `/EventManager/Index`
- `Client` -> `/Client/Index`
- Anonymous -> `/Account/Login`
