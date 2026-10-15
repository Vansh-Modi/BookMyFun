# BookMyFun — Controller & API Reference Catalog

## 1. Overview & Protocol Standards

The BookMyFun platform utilizes an integrated hybrid architecture combining server-rendered Razor MVC views with asynchronous RESTful JSON endpoints. 

### Protocol Standards:
1. **Response Envelope:** All AJAX/API endpoints return a standardized JSON envelope:
   ```json
   {
     "success": true,
     "data": { ... },
     "message": "Operation completed successfully"
   }
   ```
   Or upon failure:
   ```json
   {
     "success": false,
     "error": {
       "code": "ERR_VALIDATION_FAILED",
       "message": "The selected file is not a supported image format."
     }
   }
   ```
2. **Anti-Forgery Protection:** All state-altering `POST` requests require a valid ASP.NET Core Anti-Forgery token passed via form data (`__RequestVerificationToken`) or the `RequestVerificationToken` HTTP header.
3. **Session & Authentication:** Handled via secure, HTTP-only, SameSite=Strict authentication cookies.
4. **Tenant Isolation:** Enforced at the controller and repository layer. Users attempting to access resources belonging to other tenants receive an HTTP 403 Forbidden.

---

## 2. API Catalog by Controller (9 Controllers)

### 2.1 `AccountController`
Handles identity lifecycle, authentication, and secure tokenized host activation.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---:|:---:|:---|:---:|:---|
| `/Account/Login` | `GET` | Anonymous | `returnUrl` (string, optional) | HTML View | Renders user login interface. |
| `/Account/Login` | `POST` | Anonymous | `Email`, `Password`, `RememberMe`, `ReturnUrl` | HTML / Redirect | Validates credentials and issues auth cookie with role redirection. |
| `/Account/Register` | `GET` | Anonymous | - | HTML View | Renders client (customer) registration form. |
| `/Account/Register` | `POST` | Anonymous | `FullName`, `Email`, `Password`, `Phone` | HTML / Redirect | Registers client account (Host self-registration blocked). |
| `/Account/Logout` | `GET` | Authenticated | - | Redirect | Clears auth cookie and redirects to `/Home/Index`. |
| `/Account/ActivateHost` | `GET` | Anonymous | `token` (HMAC token, required) | HTML View | Validates 48h activation token and renders host setup. |
| `/Account/ActivateHost` | `POST` | Anonymous | `Token`, `Password`, `Phone`, `PropertyName` | HTML / Redirect | Finalizes host account creation and grants `Owner` role. |
| `/Account/AccessDenied` | `GET` | Anonymous | - | HTML View | Friendly 403 Forbidden error view. |

---

### 2.2 `HomeController`
Public catalog discovery, location search, lead intake, and customer support.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---:|:---:|:---|:---:|:---|
| `/Home/Index` | `GET` | Anonymous | - | HTML View | Landing page with featured stays, categories, search. |
| `/Home/Partner` | `GET` | Anonymous | - | HTML View | Public "List Your Farm / Partner With Us" lead intake form. |
| `/Home/Partner` | `POST` | Anonymous | `FullName`, `Email`, `Phone`, `PropertyName`, `Location`, `AcreageOrType`, `Message` | HTML / Redirect | Ingests prospective host lead for staff triage. |
| `/Home/Contact` | `GET` | Anonymous | - | HTML View | Public support and inquiry contact form. |
| `/Home/Contact` | `POST` | Anonymous | `FullName`, `Email`, `Phone`, `Subject`, `Message` | HTML / Redirect | Creates customer support inquiry ticket. |

---

### 2.3 `PropertiesController` & `BookingController` (`BookingAndPropertiesController.cs`)
Handles property search, detailed previews, flexible reservation checkout with event décor add-ons, payments, and cancellations.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---:|:---:|:---|:---:|:---|
| `/Properties/Index` | `GET` | Anonymous | `location`, `typeId`, `occasionId`, `minPrice`, `maxPrice`, `guests` | HTML View | Multi-criteria property search and filter results. |
| `/Properties/Details/{id}` | `GET` | Anonymous | `id` (int, required) | HTML View | Property details, high-res gallery, amenities, reviews. |
| `/Booking/Checkout` | `GET` | Authenticated | `propertyId`, `bookingType`, `slotType`, `daysOrHours` | HTML View | Checkout form with duration options and event décor add-on. |
| `/Booking/ConfirmBooking` | `POST` | Authenticated | `BookingCheckoutViewModel` (includes `IsEventBooking`, `EventPackageID`, `CustomEventNotes`) | HTML / Redirect | Validates availability, calculates total with décor add-on, snapshots policy, redirects to payment. |
| `/Booking/Payment/{id}` | `GET` | Authenticated | `id` (int, booking ID) | HTML View | Dedicated PCI-DSS payment page with order token. |
| `/Booking/ProcessPayment` | `POST` | Authenticated | `bookingId`, `orderId`, `paymentMethod` | HTML / Redirect | Records payment success, confirms booking, dispatches receipts. |
| `/Booking/GetRefundEstimate`| `GET` | Authenticated | `bookingId` (int, required) | JSON | Returns real-time refund quote with fee breakdown. |
| `/Booking/CancelBooking` | `POST` | Authenticated | `BookingID`, `Reason` | JSON / Redirect | Policy-enforced cancellation and automated gateway refund. |

---

### 2.4 `EventManagerController` (5th Dashboard: Event Manager / Decorator)
Décor package catalog curation, on-ground setup tracking, milestone progression, and photo/video proof verification.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---:|:---:|:---|:---:|:---|
| `/EventManager/Dashboard` | `GET` | EventManager, Admin | - | HTML View | KPI summary cards (GMV, active packages, assigned setups). |
| `/EventManager/Packages` | `GET` | EventManager, Admin | - | HTML View | Grid of active and inactive celebration packages. |
| `/EventManager/CreatePackage` | `GET` | EventManager, Admin | - | HTML View | Package creation form. |
| `/EventManager/CreatePackage` | `POST` | EventManager, Admin | `EventPackageCreateViewModel` (Name, Category, BasePrice, MediaUrl, IncludedItems) | HTML / Redirect | Creates new event décor package. |
| `/EventManager/EditPackage/{id}` | `GET` | EventManager, Admin | `id` (int, required) | HTML View | Package modification form. |
| `/EventManager/EditPackage/{id}` | `POST` | EventManager, Admin | `EventPackageEditViewModel` | HTML / Redirect | Updates package pricing, media, and description. |
| `/EventManager/TogglePackageStatus/{id}` | `POST` | EventManager, Admin | `id` (int, required) | JSON / Redirect | Toggles package between active and inactive. |
| `/EventManager/AssignedSetups` | `GET` | EventManager, Admin | - | HTML View | Tracks setups assigned to decorator with status pills. |
| `/EventManager/UpdateSetupStatus` | `POST` | EventManager, Admin | `BookingID`, `SetupStatus`, `SetupProofMediaUrls` | JSON / Redirect | Advances milestone (`Pending`, `Assigned`, `InProgress`, `Completed`, `Inspected`) with proofs. |
| `/EventManager/AssignDecorator` | `POST` | Admin | `BookingID`, `DecoratorUserID` | JSON / Redirect | Reassigns reservation to specific decorator crew. |

---

### 2.5 `ClientController`
Guest profile governance, booking history, KYC government ID submission, and reviews.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---:|:---:|:---|:---|:---|
| `/Client/Index` | `GET` | Client | - | HTML View | Client dashboard overview. |
| `/Client/MyBookings` | `GET` | Client | - | HTML View | Reservation history with cancellation modals. |
| `/Client/Profile` | `GET` | Client | - | HTML View | Personal information and KYC status. |
| `/Client/UploadId` | `POST` | Client | `DocumentType`, `File` (multipart) | JSON / Redirect | Uploads government ID to private KYC vault. |
| `/Client/SubmitReview` | `POST` | Client | `BookingID`, `Rating`, `Comment` | JSON / Redirect | Submits verified stay review ($1$ to $5$ stars). |

---

### 2.6 `OwnerController`
Farm host dashboard, listing CRUD with dual-mode media uploader, availability calendar, and 24h damage claims.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---:|:---:|:---|:---|:---|
| `/Owner/Index` | `GET` | Owner | - | HTML View | Host dashboard with earnings, bookings, and listings. |
| `/Owner/Properties` | `GET` | Owner | - | HTML View | Host portfolio (Scoped: `WHERE OwnerID = @CurrentUserID`). |
| `/Owner/AddProperty` | `GET` | Owner | - | HTML View | New listing form with dual media uploader. |
| `/Owner/AddProperty` | `POST` | Owner | `PropertyCreateViewModel` | HTML / Redirect | Validates and creates property draft with GPS & media. |
| `/Owner/EditProperty/{id}` | `GET` | Owner | `id` (int, required) | HTML View | Listing edit form (Tenant scoped). |
| `/Owner/EditProperty/{id}` | `POST` | Owner | `PropertyEditViewModel` | HTML / Redirect | Updates property details, policies, and media. |
| `/Owner/FileDamageClaim` | `GET` | Owner | `bookingId` (int, required) | HTML View | 24-hour damage claim filing interface. |
| `/Owner/SubmitDamageClaim` | `POST` | Owner | `DamageClaimViewModel` | JSON / Redirect | Enforces 24h cutoff; attaches itemized losses & photos. |

---

### 2.7 `EmployeeController`
Operational triage queues for staff handling partner leads, KYC verifications, refund overrides, and claims.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---:|:---:|:---|:---|:---|
| `/Employee/Index` | `GET` | Employee, Admin | - | HTML View | Operations workbench with pending task counters. |
| `/Employee/OnboardingLeads` | `GET` | Employee, Admin | `status` (string, optional) | HTML View | Host partnership inquiry triage pipeline. |
| `/Employee/ApproveLead` | `POST` | Employee, Admin | `leadId` (int, required) | JSON | Issues 48h encrypted HMAC activation token via email. |
| `/Employee/IdVerifications` | `GET` | Employee, Admin | `status` (string, optional) | HTML View | Confidential KYC document review queue. |
| `/Employee/ApproveId` | `POST` | Employee, Admin | `verificationId` (int, required) | JSON | Grants "Verified Guest" badge to client. |
| `/Employee/RefundsQueue` | `GET` | Employee, Admin | `status` (string, optional) | HTML View | Refund operations queue with manual override triggers. |
| `/Employee/DamageClaimsQueue` | `GET` | Employee, Admin | `status` (string, optional) | HTML View | 24-hour host damage claims adjudication queue. |

---

### 2.8 `AdminController`
Executive command center, Property GMV vs Event GMV analytics, ClosedXML Excel/CSV exports, financial ledgers, user governance, and audit trails.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---:|:---:|:---|:---|:---|
| `/Admin/Index` | `GET` | Admin | - | HTML View | Master platform dashboard with GMV KPIs. |
| `/Admin/DynamicAnalytics` | `GET` | Admin | `range`, `from`, `to` | HTML View | Multi-dimensional financial analytics (Property GMV vs Event GMV). |
| `/Admin/ExportAnalyticsExcel` | `GET` | Admin | `range`, `from`, `to` | File (`.xlsx`) | Generates styled ClosedXML spreadsheet with emerald headers. |
| `/Admin/ExportAnalyticsCsv` | `GET` | Admin | `range`, `from`, `to` | File (`.csv`) | Streams UTF-8 comma-separated financial data. |
| `/Admin/FinancialLedger` | `GET` | Admin | - | HTML View | Platform double-entry ledger (Income & Expenses). |
| `/Admin/Users` | `GET` | Admin | `roleFilter`, `search` | HTML View | User management grid across all 5 roles. |
| `/Admin/ToggleUserStatus` | `POST` | Admin | `UserID`, `IsActive` | JSON | Suspends or reactivates user account. |
| `/Admin/UpdateCommissionRate`| `POST` | Admin | `UserID`, `CommissionRate` | JSON | Sets custom commission rate override for host. |
| `/Admin/AuditLogs` | `GET` | Admin | `page`, `entityType` | HTML View | Immutable system-wide audit trail with JSON inspection. |
| `/Admin/SiteSettings` | `GET` | Admin | - | HTML View | Global platform parameters and commission defaults. |

---

### 2.9 `MediaController`
Dual-mode media ingestion, magic-byte inspection, SSRF URL validation, and private KYC retrieval.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---|:---:|:---|:---|:---|
| `/Media/UploadFile` | `POST` | Authenticated | `File` (multipart/form-data), `MediaType` ('photo'\|'video') | JSON | Magic-byte checks file, saves with UUID name, returns URL. |
| `/Media/VerifyExternalUrl` | `POST` | Authenticated | `Url` (string, required) | JSON | SSRF-checks HTTPS URL, validates DNS, returns embed preview. |
| `/Media/PrivateDoc` | `GET` | Employee / Admin / Owner of Doc | `token` (HMAC-SHA256 expiring token) | File Stream | Streams private KYC document (Valid for 15 minutes). |
| `/Media/DeleteMedia` | `POST` | Owner / Admin | `MediaID` (int, required) | JSON | Deletes media record and disk file (Tenant scoped). |

---

## 3. Error Codes & HTTP Status Responses

| HTTP Status | Error Code | Description |
|:---:|:---|:---|
| `400` | `ERR_VALIDATION_FAILED` | Payload validation failure, invalid file size, or magic-byte mismatch. |
| `400` | `ERR_SSRF_DETECTED` | External URL resolved to a loopback, private RFC 1918, or cloud metadata address. |
| `401` | `ERR_AUTH_REQUIRED` | Unauthenticated request on a protected endpoint. |
| `403` | `ERR_FORBIDDEN` | Role mismatch or tenant isolation violation (IDOR defense). |
| `404` | `ERR_NOT_FOUND` | Target property, booking, or user record does not exist. |
| `409` | `ERR_CONFLICT` | Resource state conflict (e.g., property already booked for requested dates). |
| `410` | `ERR_TOKEN_EXPIRED` | 48h host activation token or 15-minute KYC document token has expired. |
| `429` | `ERR_RATE_LIMIT_EXCEEDED`| In-memory sliding window limit exceeded (> 100 requests / minute). |
| `502` | `ERR_GATEWAY_FAILURE` | Payment gateway API (Razorpay) returned an unrecoverable failure. |
| `500` | `ERR_INTERNAL_SERVER` | Unhandled application exception; detailed telemetry logged with correlation ID. |
