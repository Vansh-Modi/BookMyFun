# Known Issues and Limitations

## 1. Overview
This document records known bugs, code inconsistencies, compiler warnings, and architectural limitations identified in BookMyFun as of November 2026. Per project guidelines, these items are documented here for tracking rather than patched during documentation cycles.

---

## 2. Issues and Limitations Catalog

### 2.1 ClosedXML Possible Null Reference Assignment Warnings (CS8601)
- **Files**:
  - [`Controllers/AdminController.cs`](file:///d:/Projects/BookMyFun/Controllers/AdminController.cs#L618) (line 618, line 751)
  - [`Controllers/EmployeeController.cs`](file:///d:/Projects/BookMyFun/Controllers/EmployeeController.cs#L215) (line 215, line 346)
- **Status**: **Verified** (confirmed via `dotnet build BookMyFun.csproj`).
- **Description**: When generating Excel export reports, cell value assignments from nullable string properties produce 4 compiler warnings (`CS8601: Possible null reference assignment`).
- **Remediation**: Apply null-coalescing fallback operators (e.g. `?? string.Empty`) on data extraction rows before cell assignment.

---

### 2.2 Template Literal String in `Occasions` Table Row 5
- **File / Component**: SQL Server database table `dbo.Occasions` (row `OccasionID = 5`).
- **Status**: **Verified** (confirmed via SQL query on database `BookMyFun`).
- **Description**: The 5th occasion record has its `OccasionName` set to the literal unescaped JavaScript template expression:
  ```text
  ${escapeHtml(name)}
  ```
  This was introduced during an automated database script execution where a template string was not evaluated prior to insertion.
- **Remediation**: Execute an idempotent update script to correct the name to `Custom Gathering`:
  ```sql
  UPDATE dbo.Occasions 
  SET OccasionName = 'Custom Gathering' 
  WHERE OccasionID = 5 AND OccasionName LIKE '%escapeHtml%';
  ```

---

### 2.3 Default Checkout Check-in Date Hides "Stay & Celebrate" on Initial Load
- **Files**:
  - [`Controllers/BookingController.cs`](file:///d:/Projects/BookMyFun/Controllers/BookingController.cs#L201-L248) (lines 201-248)
  - [`Views/Booking/Checkout.cshtml`](file:///d:/Projects/BookMyFun/Views/Booking/Checkout.cshtml)
- **Status**: **Verified** (confirmed in controller logic and browser testing).
- **Description**: When a guest opens the checkout page directly without specifying query parameters, check-in defaults to `DateTime.Today`. Because today's date is under 48 hours from current time, the 48-hour lead time check fails on first load, causing the "Stay & Celebrate" option to be hidden until the user clicks and selects a date at least 2 days into the future.
- **Remediation**: Set default checkout check-in date to `DateTime.Today.AddDays(3)` when no date query parameter is provided.

---

### 2.4 `OwnerSubscriptions` Table Column Name Discrepancy in Historical Queries
- **File / Component**: Database table `dbo.OwnerSubscriptions` and older query snippets.
- **Status**: **Verified** (confirmed via schema dump and SQL exception test).
- **Description**: The column in `OwnerSubscriptions` is named `PaymentStatus`. Historical scripts and legacy documentation occasionally reference `Status`, resulting in SQL exception:
  ```text
  Msg 207: Invalid column name 'Status'.
  ```
- **Remediation**: Ensure all custom SQL queries strictly select `PaymentStatus`.

---

### 2.5 In-Memory Fallback Secret in `MediaStorageService`
- **File**: [`Services/MediaStorageService.cs`](file:///d:/Projects/BookMyFun/Services/MediaStorageService.cs#L14) (line 14).
- **Status**: **Verified** (confirmed in code inspection).
- **Description**: If the configuration key `Security:TokenSecret` is missing in `IConfiguration`, the service falls back to a hardcoded string `__bmf_default_secret_fallback_key__` to prevent startup crash.
- **Remediation**: Add a startup validation check in `Program.cs` that throws an exception in production if `Security:TokenSecret` is not supplied via environment variable.

---

### 2.6 In-Memory Rate Limiting Not Distributed Across Server Instances
- **File**: [`Services/Security/RateLimitingMiddleware.cs`](file:///d:/Projects/BookMyFun/Services/Security/RateLimitingMiddleware.cs).
- **Status**: **Verified** (architectural limitation).
- **Description**: Rate limiting tracks client requests using an in-memory `ConcurrentDictionary`. In multi-instance or load-balanced environments, client rate counts are tracked independently per server process rather than shared centrally.
- **Remediation**: For multi-server clusters, integrate a distributed cache (such as Redis) or enforce rate limits at the reverse proxy / web application firewall (WAF) layer.
