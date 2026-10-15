# BookMyFun — Engineering Standards, Security Rules & Compliance Guidelines

## 1. Core Engineering & Clean Code Principles

Every engineer, autonomous agent, and contributor working on BookMyFun must strictly abide by the following fundamental engineering rules:

1. **Report Ground Truth (Invariant 1):** Never claim a bug is resolved, a feature implemented, or an API working without verifiable execution output observed in the active session (e.g. `dotnet build --nologo` exiting with code 0).
2. **Ground Before Mutating:** Always inspect target files, caller signatures, and related dependencies before applying edits or refactors.
3. **Clean Code & Zero Dead Code Mandate:**
   - **No Commented-Out Code:** Absolutely no blocks of commented-out logic, historical code relics, or debugging print statements committed to the repository.
   - **No Dead / Redundant Variables:** All variables, parameters, and private fields must serve an active, verified purpose.
   - **No Unused Namespaces:** Keep `using` directives clean and relevant.
   - **Strict DRY (Don't Repeat Yourself):** Reusable business calculations (pricing, commission, refunds) belong in dedicated domain services, not duplicated across controller actions.
   - **Zero Compiler Warnings:** All project code must compile cleanly without nullable reference warnings (`CS8601`, `CS8618`, etc.) or obsolete symbol notices.
4. **Zero Plaintext Secrets:** Under no circumstance may database passwords, SMTP credentials, or payment gateway API secrets be hardcoded in source code or committed to git. All credentials must reside in `appsettings.json`, environment variables, or encrypted rows in `ApiConfigurations`.
5. **Strictly Parameterized Data Access:** String concatenation in raw SQL queries is categorically forbidden. Every SQL statement must be executed through `DBHelper.cs` or explicit `SqlCommand.Parameters.AddWithValue` with explicit type matching to eliminate SQL Injection (OWASP A03).
6. **No Blind Retries:** If a compiler build or unit test fails, determine the root cause before attempting an alternate implementation.

---

## 2. Security & Compliance Framework (OWASP Top 10 & PCI-DSS)

### 2.1 OWASP Top 10 Mitigation Matrix

| OWASP Vulnerability | Platform Risk | Enforced Architectural Defense |
|:---|:---|:---|
| **A01: Broken Access Control** | IDOR on properties, bookings, or guest IDs | Multi-tenant scoping in all queries (`WHERE OwnerID = @CurrentUserID` for hosts; `WHERE DecoratorAssignedID = @CurrentUserID` for decorators). Strict separation of guest government IDs from property hosts. Expiring 15-minute HMAC tokens for staff review. |
| **A02: Cryptographic Failures** | Insecure password hashing or cleartext transmission | TLS 1.3 encryption in transit. PBKDF2/BCrypt salted hashing for user passwords. HMAC-SHA256 signature verification on Razorpay payment webhooks and 48-hour host activation tokens. |
| **A03: Injection** | SQL injection in raw ADO.NET queries; XSS in descriptions | 100% parameterized queries via `DBHelper.cs`. Automatic HTML encoding in Razor views. Backend string sanitization on user descriptions and reviews. |
| **A04: Insecure Design** | Double booking race conditions; unlimited attempts | In-memory sliding-window IP rate limiting (100 req/min). Database unique constraints on `(PropertyID, Dates)`. Idempotency keys on booking and refund transactions. |
| **A05: Security Misconfiguration** | Leaked stack traces; missing HTTP security headers | `SecurityHeadersMiddleware` enforcing CSP, HSTS, X-Frame-Options: DENY, and X-Content-Type-Options: nosniff. `GlobalExceptionFilter` catching all exceptions and sanitizing error responses. |
| **A06: Vulnerable & Outdated Components** | Vulnerabilities in 3rd-party libraries | Native .NET 10 stack. Minimal external packages (ClosedXML, Razorpay, Microsoft.Data.SqlClient). Continuous automated vulnerability scanning. |
| **A07: Identification & Auth Failures** | Credential stuffing; session hijacking | Secure HTTP-only, SameSite=Strict cookies with 7-day sliding expiration. OTP verification for password resets. Account lockout upon repeated failed logins. |
| **A08: Software & Data Integrity Failures** | Malicious file upload execution | Binary magic-byte header inspection on all uploads. Stripping directory traversals and assigning cryptographically random UUID filenames. |
| **A09: Security Logging & Monitoring** | Undetected administrative tampering | Structured `AuditLogs` recording actor ID, timestamp, entity type, action, and JSON state snapshot for every administrative mutation. |
| **A10: Server-Side Request Forgery (SSRF)** | Probing AWS/Azure metadata services via media URLs | DNS resolution of all external URLs. Immediate rejection of private RFC 1918 ranges, loopback (`127.0.0.1`), and cloud metadata (`169.254.169.254`). |

---

### 2.2 Payment Security & PCI-DSS Principle

1. **Zero Cardholder Storage:** The BookMyFun application servers never accept, inspect, serialize, or persist Primary Account Numbers (PAN), CVVs, or cardholder credentials.
2. **Client-Side Hosted Checkout:** All payment captures occur via Razorpay Hosted Checkout or Razorpay Elements.
3. **Cryptographic Webhook Verification:** All incoming payment and refund webhook notifications must validate the `X-Razorpay-Signature` against `HMAC-SHA256(webhook_body, webhook_secret)` before any database mutation occurs.
4. **Idempotency Safeguard:** Payment and refund API calls must transmit a unique `IdempotencyKey` (`Guid.NewGuid().ToString("N")`) to prevent accidental duplicate charges during network drops.

---

## 3. Media Ingestion & Binary Inspection Rules

### 3.1 Magic-Byte Binary Signatures
Client-supplied MIME types and file extensions are easily spoofed. The `MediaStorageService` must inspect the first 16 bytes of every file stream:

| Media Type | Allowed Extensions | Max File Size | Mandatory Magic-Byte Header (Hexadecimal) |
|:---|:---|:---:|:---|
| **JPEG Image** | `.jpg`, `.jpeg` | 10 MB | `FF D8 FF` |
| **PNG Image** | `.png` | 10 MB | `89 50 4E 47 0D 0A 1A 0A` |
| **WEBP Image** | `.webp` | 10 MB | `52 49 46 46` ... `57 45 42 50` |
| **MP4 Video** | `.mp4` | 100 MB | `00 00 00` [`18`\|`20`] `66 74 79 70` (`ftyp`) |
| **MOV Video** | `.mov` | 100 MB | `00 00 00 14 66 74 79 70 71 74` (`ftypqt`) |
| **Government ID (PDF)** | `.pdf` | 5 MB | `25 50 44 46` (`%PDF`) |
| **Government ID (Image)**| `.jpg`, `.png` | 5 MB | Matches JPEG or PNG signature above |
| **Setup Proofs** | `.jpg`, `.png`, `.mp4` | 25 MB | Validated through image or video signature |

**Executable binaries (e.g. `4D 5A` for PE/Windows executable, ELF, shell scripts) are rejected immediately.**

### 3.2 Filename Sanitization & Directory Separation
1. **Filename Discarding:** Client-supplied filenames must be discarded. The stored filename is generated via:
   ```csharp
   string storageFilename = $"{Guid.NewGuid():N}{sanitizedExtension}";
   ```
2. **Path Traversal Defense:** Filenames are stripped of `../`, `..\`, and illegal characters.
3. **Storage Tier Isolation:**
   - Public listing photos, videos, and décor proofs: Saved to `/wwwroot/uploads/media/`.
   - Restricted Government IDs: Saved to `/App_Data/PrivateDocuments/` (Outside web root; served only via authenticated controller with expiring token).

---

## 4. SSRF Defense Specification

When validating external media links (YouTube, Vimeo, Cloudinary, AWS S3):
1. **Protocol Constraint:** Only `https://` is permitted. `http://`, `file://`, `ftp://`, or `gopher://` are rejected.
2. **DNS Host Resolution:** The hostname is resolved to an IP address before any network communication occurs.
3. **IP Range Blacklist:**
   - `127.0.0.0/8` (Loopback)
   - `10.0.0.0/8` (RFC 1918 Private)
   - `172.16.0.0/12` (RFC 1918 Private)
   - `192.168.0.0/16` (RFC 1918 Private)
   - `169.254.0.0/16` (Link-Local & Cloud Metadata, specifically `169.254.169.254`)
   - `::1` (IPv6 Loopback)
   - `fc00::/7` (IPv6 Unique Local)

Any matching destination immediately aborts with a `SecurityViolationException`.

---

## 5. Centralized Domain Exception Hierarchy

Service-layer and domain validations must throw typed exceptions rather than generic `System.Exception`:

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

public class SecurityViolationException : AppException
{
    public SecurityViolationException(string message) : base(message, 400, "ERR_SECURITY_VIOLATION") { }
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

public class BusinessRuleException : AppException
{
    public BusinessRuleException(string message) : base(message, 422, "ERR_BUSINESS_RULE_VIOLATION") { }
}

public class PaymentGatewayException : AppException
{
    public PaymentGatewayException(string message) : base(message, 502, "ERR_GATEWAY_FAILURE") { }
}
```

The [`GlobalExceptionFilter`](file:///d:/Projects/BookMyFun/Services/Filters/GlobalExceptionFilter.cs) catches these exceptions and maps them to clean user-facing error views or JSON error envelopes.

---

## 6. C# & .NET 10 Coding Conventions

1. **File-Scoped Namespaces:** All files must use file-scoped namespace declarations (e.g. `namespace BookMyFun.Services;`).
2. **Null Value Handling in ADO.NET:** When binding nullable parameters to `SqlCommand`, always coalesce nulls with `DBNull.Value`:
   ```csharp
   cmd.Parameters.AddWithValue("@Field", (object?)model.NullableProperty ?? DBNull.Value);
   ```
3. **Async Throughout:** All I/O, database queries, and network calls must be asynchronous using `async Task<T>` with `await` throughout the entire call stack.
4. **Preserve Existing Comments:** Existing comments and documentation must be maintained during edits unless explicitly requested by the user.
