# BookMyFun

BookMyFun is a web platform for booking farmhouses, luxury villas, and private resorts. Guests can book standard stays or combine stays with event décor styling via "Stay & Celebrate". The platform handles multi-role dashboards, partner onboarding, property approvals, listing subscriptions, identity verification (KYC), and payment processing via Razorpay.

The application is built with ASP.NET Core (.NET 10) and SQL Server. It uses raw parameterized ADO.NET without an ORM.

---

## User Roles

BookMyFun has five distinct user roles:

1. **Client (Guest)**
   - Searches and filters properties by location, dates, capacity, and occasion.
   - Books stays as Full Day, Half Day, or Hourly.
   - Selects "Stay & Celebrate" décor add-ons when booking more than 48 hours in advance in supported areas.
   - Uploads government ID (Aadhaar, Passport, Driving License) for KYC verification.
   - Pays via Razorpay and downloads GST booking receipts.
   - Cancels bookings subject to platform refund rules.

2. **Owner (Host)**
   - Lists and edits farmhouses, villas, and resort properties.
   - Manages availability calendars and blackout dates.
   - Receives a 60-day free trial on onboarding approval.
   - Purchases listing subscriptions (Starter, Growth, Premium) to keep properties active.
   - Files security deposit damage claims within 24 hours of checkout.
   - Configures email and WhatsApp notification channels.

3. **EventManager (Décor Partner)**
   - Configures active service areas by city and state.
   - Creates and manages décor packages across five occasion categories.
   - Receives assigned event setups from "Stay & Celebrate" bookings.
   - Uploads portfolio setup photos for staff moderation.
   - Edits public profile with automated contact privacy filtering.

4. **Employee (Operations & Moderation)**
   - Reviews and approves or rejects new property listings.
   - Adjudicates guest government identity documents (KYC).
   - Reviews incoming host and event manager partner applications.
   - Resolves customer support inquiries.
   - Processes cancellation refund requests and damage claims.

5. **Admin (System Administrator)**
   - Has full platform oversight across all operational queues.
   - Manages employee and owner accounts.
   - Configures subscription plans and pricing matrices.
   - Reviews privacy violations in flagged contact attempts.
   - Oversees financial payouts, commission splits, and audit logs.
   - Modifies global platform settings.

---

## How to Run Locally

### Prerequisites
- **.NET SDK**: .NET 10.0 SDK or later.
- **Database**: Microsoft SQL Server 2019/2022 or SQL Server Express.
- **Operating System**: Windows, macOS, or Linux.

### Steps

1. **Clone the repository**:
   ```bash
   git clone <repository-url>
   cd BookMyFun
   ```

2. **Configure Database Connection**:
   Set your local connection string using .NET User Secrets:
   ```bash
   dotnet user-secrets init
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost\SQLEXPRESS;Database=BookMyFun;Trusted_Connection=True;TrustServerCertificate=True;"
   ```
   Alternatively, update `ConnectionStrings:DefaultConnection` in `appsettings.Development.json`.

3. **Initialize Database**:
   The application automatically creates tables and baseline seed data on startup.
   To run manual migrations, execute the scripts in the `/db` directory in this order:
   1. `db/schema.sql`
   2. `db/performance_indexes.sql`
   3. `db/update_booking_status_constraint.sql`
   4. `db/add_user_notifications.sql`
   5. `db/add_is_featured_to_properties.sql`
   6. `db/listing_subscription_rules.sql`
   7. `db/link_missing_property_occasions.sql`

4. **Build and Run**:
   ```bash
   dotnet restore
   dotnet build
   dotnet run
   ```
   Open your browser and navigate to `https://localhost:7142` or `http://localhost:5142`.

---

## How to Deploy to MonsterASP

MonsterASP provides Windows IIS shared hosting for ASP.NET Core applications.

### 1. Compile and Publish Release Artifacts
Run the publish command from the project root:
```bash
dotnet publish BookMyFun.csproj -c Release -r win-x64 --self-contained false -o ./publish
```

### 2. Verify `web.config`
Ensure the output `./publish/web.config` file contains:
- `processPath="dotnet"`
- `arguments=".\BookMyFun.dll"`
- `hostingModel="inprocess"`
- `stdoutLogEnabled="false"` (change to `true` if diagnosing startup issues)
- WebP and AVIF static MIME type mappings.

### 3. Configure Server Environment Variables
In the MonsterASP hosting control panel, configure the following environment variables (do not commit real values to source control):
- `ASPNETCORE_ENVIRONMENT`: `Production`
- `ConnectionStrings__DefaultConnection`: MonsterASP SQL Server connection string.
- `Razorpay__KeyId`: Razorpay Key ID.
- `Razorpay__KeySecret`: Razorpay Key Secret.
- `Razorpay__WebhookSecret`: Razorpay Webhook Secret.
- `Smtp__Host`: SMTP server hostname.
- `Smtp__Port`: SMTP server port (e.g. 587).
- `Smtp__Username`: SMTP authentication username.
- `Smtp__Password`: SMTP authentication password.
- `Smtp__From`: Platform outbound email address.
- `WhatsApp__ApiKey`: WhatsApp Cloud API token.
- `WhatsApp__PhoneNumberId`: WhatsApp registered phone number ID.
- `Security__TokenSecret`: Cryptographic secret for signing private document tokens.

### 4. Deploy Files
1. Zip the contents of `./publish/` (or use `publish.zip`).
2. Upload and extract into the MonsterASP `site/wwwroot` directory via FTP or the MonsterASP File Manager.
3. Ensure a `logs` directory exists in the root if enabling stdout debugging.

### 5. Execute Database Migration Scripts
Log in to MonsterASP WebAdmin (myLittleAdmin) or connect via SQL Server Management Studio (SSMS) using the MonsterASP database credentials. Run the `/db` scripts in numerical order as detailed in [Database Documentation](file:///d:/Projects/BookMyFun/Docs/database.md).

---

## Documentation Index

Detailed platform documentation is located in the `/docs` directory:
- [Setup Guide](file:///d:/Projects/BookMyFun/Docs/setup.md): Prerequisites, local development secrets, and server environment variables.
- [Database Architecture](file:///d:/Projects/BookMyFun/Docs/database.md): Data dictionary of all tables, script execution order, nullability, and defaults.
- [Roles and Access Control](file:///d:/Projects/BookMyFun/Docs/roles-and-access.md): Authorization policies, permissions matrix, and navigation isolation.
- [Booking and Checkout Flow](file:///d:/Projects/BookMyFun/Docs/booking-flow.md): Duration types, availability, Stay & Celebrate, 48-hour rule, flat fees, KYC gate, and payment.
- [Listing Lifecycle](file:///d:/Projects/BookMyFun/Docs/listing-lifecycle.md): Property states, approval workflow, 60-day trial, and subscription management.
- [Event Managers](file:///d:/Projects/BookMyFun/Docs/event-managers.md): Profile fields, privacy masking, portfolio moderation, and verification statuses.
- [Notifications](file:///d:/Projects/BookMyFun/Docs/notifications.md): Events catalog, recipients, multi-channel dispatch, WhatsApp opt-in, and deduplication keys.
- [Errors and Logging](file:///d:/Projects/BookMyFun/Docs/errors-and-logging.md): Custom status code pages (404, 403, 401, 500), correlation IDs, and stdout logs.
- [Known Issues](file:///d:/Projects/BookMyFun/Docs/known-issues.md): Open bugs and limitations with verified citations.
- [Changelog](file:///d:/Projects/BookMyFun/Docs/CHANGELOG.md): Dated release history and feature milestones.
