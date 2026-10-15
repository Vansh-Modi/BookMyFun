# BookMyFun — Coding Standards, Security & Architecture Rules

## 1. Core Engineering Principles
1. **Report Ground Truth (Gravitas Invariant 1):** Never claim a bug is fixed, a feature implemented, or a test passed without verifiable execution output observed in the active session.
2. **Ground Before Mutating:** Inspect existing target files, callers, and dependencies before writing or editing.
3. **Zero Plaintext Credentials:** API keys, database connection strings, and webhook secrets must be accessed via `IConfiguration` or encrypted site settings. Never hardcode secrets.
4. **Parameterized Data Access:** String concatenation in SQL statements is strictly prohibited. Every query must use `SqlCommand.Parameters.AddWithValue` or `SqlParameter` with explicit types to eliminate SQL Injection.
5. **No Blind Retries:** If a command or build fails, identify the root cause before attempting an alternate implementation.

---

## 2. Security & Compliance Standards (OWASP & PCI-DSS)

### 2.1 File Upload & Media Ingestion Rules
- **Binary Header (Magic-Byte) Verification:**
  - Every uploaded file must be inspected by reading the first 16 bytes of the binary stream.
  - Allowed Magic Numbers:
    - JPEG: `FF D8 FF`
    - PNG: `89 50 4E 47 0D 0A 1A 0A`
    - WEBP: `52 49 46 46 ... 57 45 42 50`
    - MP4: `00 00 00 [18|20] 66 74 79 70`
    - PDF: `25 50 44 46`
  - Any file whose header does not match its claimed MIME type or is an executable binary (e.g. `4D 5A` for PE/EXE, or scripts) must be immediately rejected with an HTTP 400 `InvalidFileBinaryException`.
- **Filename Sanitization:**
  - Strip directory traversal sequences (`../`, `..\`).
  - Discard the client-supplied original filename for storage; substitute a cryptographically random UUID (`Guid.NewGuid().ToString("N") + extension`).
- **SSRF Prevention on External URLs:**
  - Only `https://` protocol schemes are allowed.
  - Resolve the hostname to an IP address before requesting headers.
  - Explicitly reject loopback addresses (`127.0.0.1`, `::1`), private networks (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16`), and AWS/GCP/Azure link-local metadata endpoints (`169.254.169.254`).

### 2.2 Payment & PCI-DSS Level 1 Compliance
- **Zero Card Storage:** Never accept, inspect, log, or persist Primary Account Numbers (PAN), CVV/CVC, or bank PINs on application servers.
- **Client-Side Tokenization:** All payment collection occurs via Razorpay / Stripe hosted checkout modals or Elements.
- **Webhook Verification:** Webhook endpoints must validate HMAC-SHA256 request signatures using the provider secret before parsing payloads.
- **Idempotency:** Payment initiation and refund processing must enforce an `IdempotencyKey` to prevent double-charging on network retries.

### 2.3 Authentication, Session & Tenant Isolation (IDOR Defense)
- **Cookie Security:** Auth cookies must be configured with `HttpOnly = true`, `SecurePolicy = CookieSecurePolicy.Always`, and `SameSite = SameSiteMode.Strict`.
- **Tenant Isolation on Scoped Routes:**
  - When an Owner queries or mutates a property (`/Owner/EditProperty/{id}`), the query must always include `AND OwnerID = @CurrentUserID`.
  - An Owner must never be able to access or mutate another Owner's listing or guest data by changing URL parameters.
- **Government ID Protection:**
  - Client government IDs are classified as Restricted PII.
  - Under no circumstance can an ID document be queried or served to an account with `Role == 'Owner'`.

---

## 3. Error Handling & System Resilience

### 3.1 Custom Error Hierarchy
All service-level failures must throw specialized domain exceptions rather than generic `Exception`:
```csharp
public abstract class AppException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }

    protected AppException(string message, int statusCode, string errorCode) : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}

public class ValidationException : AppException
{
    public ValidationException(string message) : base(message, 400, "ERR_VALIDATION_FAILED") { }
}

public class AuthenticationException : AppException
{
    public AuthenticationException(string message) : base(message, 401, "ERR_AUTH_REQUIRED") { }
}

public class ForbiddenException : AppException
{
    public ForbiddenException(string message) : base(message, 403, "ERR_FORBIDDEN") { }
}

public class NotFoundException : AppException
{
    public NotFoundException(string message) : base(message, 404, "ERR_RESOURCE_NOT_FOUND") { }
}

public class ConflictException : AppException
{
    public ConflictException(string message) : base(message, 409, "ERR_CONFLICT") { }
}

public class PaymentGatewayException : AppException
{
    public PaymentGatewayException(string message) : base(message, 502, "ERR_GATEWAY_FAILURE") { }
}
```

### 3.2 Global Exception Filter
- Intercepts all unhandled exceptions.
- For API / AJAX requests: Returns `{ "success": false, "error": { "code": "ERR_CODE", "message": "User-friendly description" } }`.
- For standard Razor views: Redirects to `/Home/Error` with a correlation ID.
- In production (`app.Environment.IsProduction()`), internal stack traces, SQL syntax details, and physical server paths must be suppressed.

### 3.3 Rate Limiting Policy
- Auth Routes (`/Account/Login`, `/Account/Register`): Max 5 requests / 15 minutes per IP.
- Payment / Booking Endpoints: Max 10 requests / minute per User ID.
- Media Upload Endpoints: Max 20 uploads / 15 minutes per User ID.
- Public Search / Browse: Max 100 requests / minute per IP.
