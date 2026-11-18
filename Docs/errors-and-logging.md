# Errors, Status Code Handling, and Diagnostic Logging

## 1. Overview
BookMyFun provides centralized error management, structured HTTP status code responses, and correlation tracking across both standard browser navigation and API/AJAX endpoints.

Primary components:
- Middleware configuration: [`Program.cs`](file:///d:/Projects/BookMyFun/Program.cs)
- Error controller endpoint: [`HomeController.cs`](file:///d:/Projects/BookMyFun/Controllers/HomeController.cs)
- Error response service: [`CustomErrorHandlerService.cs`](file:///d:/Projects/BookMyFun/Services/Security/CustomErrorHandlerService.cs)
- Error view template: [`Views/Shared/Error.cshtml`](file:///d:/Projects/BookMyFun/Views/Shared/Error.cshtml)

---

## 2. Status Code Routing

Configured in [`Program.cs`](file:///d:/Projects/BookMyFun/Program.cs):
```csharp
app.UseExceptionHandler("/Home/Error");
app.UseStatusCodePagesWithReExecute("/Home/Error/{0}");
```

When an unhandled exception or HTTP error occurs, requests are re-executed to `/Home/Error/{statusCode}` without changing the browser URL.

---

## 3. Custom Error Pages

The [`CustomErrorHandlerService.cs`](file:///d:/Projects/BookMyFun/Services/Security/CustomErrorHandlerService.cs) dynamically differentiates between standard browser navigation and API/AJAX calls (detecting `X-Requested-With: XMLHttpRequest`, `Accept: application/json`, or `/api` routes).

### 3.1 404 Not Found (`OnNotFound`)
- **Trigger**: Non-existent route, deleted property, or invalid package ID.
- **Browser Presentation**:
  - Title: `Page Not Found (404)`
  - Message: *"The page or destination you are searching for might have been moved, renamed, or is temporarily unavailable."*
  - Provides navigation links back to the home page and search catalog.
- **API/AJAX JSON Response**:
  ```json
  {
    "success": false,
    "message": "The requested resource was not found.",
    "correlationId": "0HN123456:00000001"
  }
  ```

### 3.2 403 Forbidden / Access Restricted (`OnAccessDenied`)
- **Trigger**: Attempting to access an endpoint protected by a policy not granted to the user's role (e.g. Client navigating to `/Admin` or `/Owner`).
- **Browser Presentation**:
  - Title: `Access Restricted (403)`
  - Message: *"You do not have permission to view or manage this resource. If you believe this is in error, please verify that you are signed in with the correct account."*
- **API/AJAX JSON Response**:
  ```json
  {
    "success": false,
    "message": "Access restricted. You do not have permission to access this resource.",
    "correlationId": "0HN123456:00000002"
  }
  ```

### 3.3 401 Unauthorized (`OnLoginRequired`)
- **Trigger**: Accessing an `[Authorize]` endpoint while unauthenticated.
- **Browser Presentation**:
  - Automatically redirects to `/Account/Login?returnUrl={TargetUrl}`.
- **API/AJAX JSON Response**:
  ```json
  {
    "success": false,
    "message": "Authentication required. Please sign in to proceed.",
    "correlationId": "0HN123456:00000003",
    "loginUrl": "/Account/Login?returnUrl=%2FBooking%2FCheckout%3FpropertyId%3D12"
  }
  ```

### 3.4 500 Internal Server Error (`OnUnhandledException`)
- **Trigger**: Uncaught runtime exceptions or database connection failures.
- **Browser Presentation**:
  - Title: `Something Went Wrong (500)`
  - Message: *"We apologize for the inconvenience. An unexpected system error occurred on our end. Please quote the reference ID below to our support team."*
  - Shows the correlation ID. Internal exception stack traces and database query details are never rendered to clients.
- **API/AJAX JSON Response**:
  ```json
  {
    "success": false,
    "message": "An unexpected error occurred. Please try again later or quote reference {CorrelationId} to support.",
    "correlationId": "0HN123456:00000004"
  }
  ```

### 3.5 Other Status Codes (400, 502, 503)
- Rendered via generic fallback with matching HTTP status code, standard styling, and correlation ID.

---

## 4. Correlation ID Tracking

Every error response and log message is tied together using a unique correlation ID:

```csharp
string correlationId = Activity.Current?.Id ?? context.TraceIdentifier;
```

- **In Views**: Displayed as a reference badge:
  ```html
  <span class="badge bg-secondary font-monospace">Ref: 0HN123456:00000004</span>
  ```
- **In Logs**: Logged in log entries:
  ```text
  [0HN123456:00000004] Unhandled exception at POST /Booking/Checkout
  ```
- When a user reports an incident to customer support, staff use this correlation ID to look up exact stack traces in the server logs.

---

## 5. Where Logs Are Written

1. **Console & Debug Output**:
   - In local development (`ASPNETCORE_ENVIRONMENT=Development`), log events stream to stdout and the Visual Studio / terminal debug console.
2. **IIS / MonsterASP Stdout Logs**:
   - Configured in `web.config` under `<aspNetCore>`.
   - Written to the `.\logs\` directory on the host server.
3. **Database Audit Trail**:
   - Administrative actions and status updates are logged to the [`AuditLogs`](file:///d:/Projects/BookMyFun/Docs/database.md#auditlogs) table.
   - Intercepted contact details logged to [`FlaggedContactAttempts`](file:///d:/Projects/BookMyFun/Docs/database.md#flaggedcontactattempts).
   - Outbound notifications logged to [`NotificationDispatches`](file:///d:/Projects/BookMyFun/Docs/database.md#notificationdispatches).

---

## 6. How to Turn On stdout Logging on MonsterASP / IIS

By default, stdout logging is disabled in production to prevent disk exhaustion. To enable stdout logging when troubleshooting startup or 500.xx errors:

1. Open [`web.config`](file:///d:/Projects/BookMyFun/web.config) in the root deployment directory.
2. Locate the `<aspNetCore>` tag:
   ```xml
   <aspNetCore processPath="dotnet" arguments=".\BookMyFun.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess">
   ```
3. Change `stdoutLogEnabled="false"` to `stdoutLogEnabled="true"`:
   ```xml
   <aspNetCore processPath="dotnet" arguments=".\BookMyFun.dll" stdoutLogEnabled="true" stdoutLogFile=".\logs\stdout" hostingModel="inprocess">
   ```
4. Verify that a folder named `logs` exists in the site root directory. Create the `logs` folder if it does not exist.
5. Save `web.config` and reload the site in your browser.
6. Check `.\logs\` for log files matching the pattern:
   ```text
   .\logs\stdout_20261107120000_1234.log
   ```
7. **Important**: Once debugging is complete, set `stdoutLogEnabled="false"` again to prevent large log accumulation.
