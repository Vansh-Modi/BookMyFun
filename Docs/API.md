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

## 2. API Catalog by Controller

### 2.1 `AccountController`
Handles identity lifecycle, authentication, and secure tokenized host activation.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---:|:---:|:---|:---:|:---|
| `/Account/Login` | `GET` | Anonymous | `returnUrl` (string, optional) | HTML View | Renders user login interface. |
| `/Account/Login` | `POST` | Anonymous | `Email`, `Password`, `RememberMe`, `ReturnUrl` | HTML / Redirect | Validates credentials and issues auth cookie. |
| `/Account/Register` | `GET` | Anonymous | - | HTML View | Renders client (customer) registration form. |
| `/Account/Register` | `POST` | Anonymous | `FullName`, `Email`, `Password`, `Phone` | HTML / Redirect | Registers client account (Host self-registration blocked). |
| `/Account/Logout` | `GET` | Authenticated | - | Redirect | Clears auth cookie and redirects to `/Home/Index`. |
| `/Account/ActivateHost` | `GET` | Anonymous | `token` (HMAC token, required) | HTML View | Validates 48h activation token and renders host setup. |
| `/Account/ActivateHost` | `POST` | Anonymous | `Token`, `Password`, `Phone`, `PropertyName` | HTML / Redirect | Finalizes host account creation and grants `Owner` role. |
| `/Account/AccessDenied` | `GET` | Anonymous | - | HTML View | Friendly 403 Forbidden error view. |

---

### 2.2 `HomeController`
Public catalog exploration, search, lead intake, and support contact.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---|:---:|:---|:---:|:---|
| `/` or `/Home/Index` | `GET` | Anonymous | - | HTML View | Homepage with hero search, featured farms, and reviews. |
| `/Home/Explore` | `GET` | Anonymous | `location`, `typeId`, `occasionId`, `guests`, `minPrice`, `maxPrice` | HTML View | Search and filter listings with map pin view. |
| `/Home/PropertyDetails/{id}`| `GET` | Anonymous | `id` (int, required) | HTML View | Listing details, gallery, amenities, and reviews. |
| `/Home/Partner` | `GET` | Anonymous | - | HTML View | Public "List Your Farm / Partner With Us" lead intake. |
| `/Home/Partner` | `POST` | Anonymous | `FullName`, `Phone`, `Email`, `PropertyName`, `Location`, `AcreageOrType`, `Message` | JSON / HTML | Submits partner lead into employee onboarding pipeline. |
| `/Home/Contact` | `GET` | Anonymous | - | HTML View | Customer support inquiry form. |
| `/Home/Contact` | `POST` | Anonymous | `Name`, `Email`, `Phone`, `Subject`, `Message` | HTML / Redirect | Submits inquiry into `ContactUs` repository. |
| `/Home/Error` | `GET` | Anonymous | - | HTML View | Centralized error fallback page with correlation ID. |

---

### 2.3 `BookingAndPropertiesController`
Reservation creation, pricing calculations, payment verification, and cancellations.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---|:---:|:---|:---:|:---|
| `/Booking/Checkout` | `GET` | Client | `propertyId`, `checkIn`, `checkOut`, `bookingType`, `slotType` | HTML View | Renders reservation checkout summary with breakdown. |
| `/Booking/InitiateBooking` | `POST` | Client | `PropertyID`, `CheckInDate`, `CheckOutDate`, `BookingType`, `SlotType` | JSON | Creates order in Razorpay and returns payment order ID. |
| `/Booking/ConfirmPayment` | `POST` | Client | `RazorpayPaymentID`, `RazorpayOrderID`, `RazorpaySignature`, `BookingData` | JSON | Validates signature, snapshots commission & policy, saves booking. |
| `/Booking/GetCancellationQuote` | `GET` | Client / Staff | `bookingId` (int, required) | JSON | Calculates real-time policy breakdown and net refund. |
| `/Booking/CancelBooking` | `POST` | Client / Staff | `BookingID`, `Reason` | JSON | Processes cancellation, issues gateway refund, records refund log. |
| `/Booking/Details/{id}` | `GET` | Authenticated | `id` (int, required) | HTML View | Displays confirmed booking receipt and policy details. |

---

### 2.4 `ClientController`
Guest account management, reservation history, and government KYC upload.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---|:---:|:---|:---:|:---|
| `/Client/Dashboard` | `GET` | Client | - | HTML View | Active reservations, past stays, and KYC badge status. |
| `/Client/MyBookings` | `GET` | Client | - | HTML View | Comprehensive reservation list with cancellation triggers. |
| `/Client/Profile` | `GET` | Client | - | HTML View | Personal profile details and notification preferences. |
| `/Client/UploadIdVerification` | `POST` | Client | `DocumentType`, `File` (multipart) | JSON | Uploads government ID (PDF/JPG/PNG) into private KYC vault. |
| `/Client/SubmitReview` | `POST` | Client | `BookingID`, `PropertyID`, `Rating` (1-5), `Comment` | JSON | Posts guest review and star rating post-stay. |

---

### 2.5 `OwnerController`
Farm host dashboard, listing CRUD, dual-mode media management, and 24h damage claims.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---|:---:|:---|:---|:---|
| `/Owner/Dashboard` | `GET` | Owner | - | HTML View | Host KPI metrics (revenue, occupancy, active listings). |
| `/Owner/Properties` | `GET` | Owner | - | HTML View | Host property portfolio with listing status toggles. |
| `/Owner/AddProperty` | `GET` | Owner | - | HTML View | Wizard for creating new farm/villa listing. |
| `/Owner/AddProperty` | `POST` | Owner | `Title`, `Description`, `Location`, `Latitude`, `Longitude`, `PricePerNight`, `MaxGuests`, `Amenities[]`, `Media[]` | HTML / Redirect | Validates limits, creates property, attaches media. |
| `/Owner/EditProperty/{id}` | `GET` | Owner | `id` (int, required) | HTML View | Edit property details (Enforces tenant isolation). |
| `/Owner/EditProperty/{id}` | `POST` | Owner | `PropertyID`, Updated Fields | HTML / Redirect | Updates property configuration and media items. |
| `/Owner/DeleteProperty/{id}` | `POST` | Owner | `id` (int, required) | JSON | Deletes listing (Tenant scoped). |
| `/Owner/Bookings` | `GET` | Owner | - | HTML View | Host reservation calendar and guest arrival list. |
| `/Owner/FileDamageClaim` | `GET` | Owner | `bookingId` (int, required) | HTML View | Form to file damage claim (within 24h of checkout). |
| `/Owner/FileDamageClaim` | `POST` | Owner | `BookingID`, `ClaimAmount`, `ItemizedDescription`, `EvidenceMediaUrls[]` | JSON | Submits claim against client security deposit. |
| `/Owner/Subscriptions` | `GET` | Owner | - | HTML View | Current subscription tier, listing allowance, upgrade CTA. |
| `/Owner/UpgradeSubscription` | `POST` | Owner | `PlanID` (int, required) | JSON | Initiates subscription upgrade payment flow. |

---

### 2.6 `EmployeeController`
Operational workflows for lead triage, identity verification, refunds, and damage claims.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---|:---:|:---|:---|:---|
| `/Employee/Dashboard` | `GET` | Employee | - | HTML View | Operational KPI summary (pending leads, unverified IDs). |
| `/Employee/OnboardingLeads` | `GET` | Employee | - | HTML View | Triage pipeline for host intake applications. |
| `/Employee/ApproveLead` | `POST` | Employee | `LeadID` (int, required) | JSON | Approves lead and emails 48h activation token. |
| `/Employee/RejectLead` | `POST` | Employee | `LeadID`, `Reason` | JSON | Rejects lead and records staff note. |
| `/Employee/IdVerifications` | `GET` | Employee | - | HTML View | KYC verification queue for guest government IDs. |
| `/Employee/VerifyId` | `POST` | Employee | `VerificationID` | JSON | Approves guest ID and grants "Verified Guest" badge. |
| `/Employee/RejectId` | `POST` | Employee | `VerificationID`, `Reason` | JSON | Flags document rejection and prompts guest re-upload. |
| `/Employee/RefundsQueue` | `GET` | Employee | - | HTML View | Operational refund requests queue and override review. |
| `/Employee/OverrideRefund` | `POST` | Employee | `RefundID` or `BookingID`, `OverrideAmount`, `Notes` | JSON | Manually adjusts refund amount and dispatches payment. |
| `/Employee/DamageClaimsQueue`| `GET` | Employee | - | HTML View | Host damage claims queue awaiting adjudication. |
| `/Employee/AdjudicateClaim` | `POST` | Employee | `ClaimID`, `ApprovedAmount`, `Notes` | JSON | Adjudicates claim, settles deposit, and releases remainder. |

---

### 2.7 `AdminController`
Superuser platform administration, financial ledgers, dynamic analytics, and user governance.

| Endpoint | Verb | Authorization | Request Body / Query Params | Response Type | Description |
|:---|:---|:---:|:---|:---|:---|
| `/Admin/Dashboard` | `GET` | Admin | - | HTML View | Master platform dashboard with high-level KPI tiles. |
| `/Admin/Analytics` | `GET` | Admin | `range` (Today, 7D, MTD, YTD, Custom), `from`, `to` | HTML / JSON | Multi-dimensional financial analytics with charts. |
| `/Admin/ExportAnalyticsExcel`| `GET` | Admin | `range`, `from`, `to` | File (`.xlsx`) | Generates ClosedXML spreadsheet report. |
| `/Admin/ExportAnalyticsCsv` | `GET` | Admin | `range`, `from`, `to` | File (`.csv`) | Streams UTF-8 comma-separated financial data. |
| `/Admin/FinancialLedger` | `GET` | Admin | - | HTML View | Platform double-entry ledger (Income & Expenses). |
| `/Admin/AddLedgerEntry` | `POST` | Admin | `TransactionType`, `Category`, `Amount`, `Title`, `Details`, `TransactionDate` | JSON | Posts new operational expense/income line item. |
| `/Admin/Users` | `GET` | Admin | `roleFilter`, `search` | HTML View | User management grid across all 4 roles. |
| `/Admin/ToggleUserStatus` | `POST` | Admin | `UserID`, `IsActive` | JSON | Suspends or reactivates user account. |
| `/Admin/UpdateCommissionRate`| `POST` | Admin | `UserID`, `CommissionRate` | JSON | Sets custom commission rate override for host. |
| `/Admin/AuditLogs` | `GET` | Admin | `page`, `entityType` | HTML View | Immutable system-wide audit trail with JSON inspection. |
| `/Admin/SiteSettings` | `GET` | Admin | - | HTML View | Global platform parameters and commission defaults. |
| `/Admin/UpdateSiteSettings` | `POST` | Admin | Key-Value settings dictionary | HTML / Redirect | Updates platform settings and commission rates. |

---

### 2.8 `MediaController`
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
