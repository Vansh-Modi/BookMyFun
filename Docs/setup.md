# Local Setup and Server Configuration

This guide explains how to set up BookMyFun locally, configure development secrets, and set required environment variables on production servers.

---

## 1. Prerequisites

- **.NET SDK:** .NET 10.0 SDK (`net10.0`). Check with `dotnet --version`.  
  *(Source: `BookMyFun.csproj` line 4)*
- **Database Engine:** Microsoft SQL Server 2016 or higher, SQL Server Express, or LocalDB (`(localdb)\mssqllocaldb`).  
  *(Source: `db/schema.sql`, `Data/DBHelper.cs`)*
- **Git:** Git CLI for repository management.
- **Operating System:** Windows, Linux, or macOS.

---

## 2. Local Setup Steps

1. **Clone the repository:**
   ```bash
   git clone <repository_url>
   cd BookMyFun
   ```

2. **Restore dependencies:**
   ```bash
   dotnet restore
   ```

3. **Configure User Secrets for Local Development:**
   Initialize and populate user secrets using the project User Secrets ID (`98c50b54-c406-4d8c-8a99-00104625a700` in `BookMyFun.csproj`):
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\mssqllocaldb;Database=BookMyFun;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
   dotnet user-secrets set "Authentication:Google:ClientId" "your-google-client-id.apps.googleusercontent.com"
   dotnet user-secrets set "Authentication:Google:ClientSecret" "your-google-client-secret"
   dotnet user-secrets set "Razorpay:KeyId" "rzp_test_placeholder"
   dotnet user-secrets set "Razorpay:KeySecret" "placeholder_razorpay_secret"
   dotnet user-secrets set "Smtp:Host" "smtp.example.com"
   dotnet user-secrets set "Smtp:Port" "587"
   dotnet user-secrets set "Smtp:Username" "smtp-user-placeholder"
   dotnet user-secrets set "Smtp:Password" "smtp-password-placeholder"
   dotnet user-secrets set "WhatsApp:ApiKey" "whatsapp-token-placeholder"
   dotnet user-secrets set "WhatsApp:PhoneNumberId" "whatsapp-phone-id-placeholder"
   ```

4. **Initialize the Database:**
   In development mode (`ASPNETCORE_ENVIRONMENT=Development`), `DatabaseInitializer` runs automatically on startup.  
   Alternatively, execute `/db/schema.sql` against your local SQL Server instance.  
   *(Source: `Program.cs` lines 240-258)*

5. **Run the Application:**
   ```bash
   dotnet run
   ```
   Open `http://localhost:5144` or `https://localhost:7144` in your browser.

6. **Run the Test Suite:**
   ```bash
   dotnet test
   ```

---

## 3. Server Environment Variables

Configure these environment variables in your server hosting control panel (e.g. MonsterASP, Azure, IIS, or Docker container).

| Environment Variable Name | Purpose | Required / Optional | Source in Code |
| :--- | :--- | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | Sets runtime environment (`Production` or `Development`); disables auto-migration and enables strict security in production. | **Required** | `Program.cs`, `web.config` |
| `ConnectionStrings__DefaultConnection` | Database connection string for ADO.NET SQL Server operations. | **Required** | `Program.cs`, `Data/DBHelper.cs` |
| `Google__ClientId` | Google OAuth 2.0 client ID for guest social authentication. | Optional | `Program.cs`, `ApplicationOptions.cs` |
| `Google__ClientSecret` | Google OAuth 2.0 client secret for token verification. | Optional | `Program.cs`, `ApplicationOptions.cs` |
| `GOOGLE_CLIENT_ID` | Fallback alias for Google OAuth client ID. | Optional | `Program.cs` |
| `GOOGLE_CLIENT_SECRET` | Fallback alias for Google OAuth client secret. | Optional | `Program.cs` |
| `Razorpay__KeyId` | Public API key ID for Razorpay payment order creation. | Optional | `Services/PaymentGatewayService.cs` |
| `Razorpay__KeySecret` | Secret key for generating and verifying Razorpay payment signatures. | Optional | `Services/PaymentGatewayService.cs` |
| `Razorpay__WebhookSecret` | Secret key for validating inbound Razorpay webhook payload HMAC signatures. | Optional | `Controllers/RazorpayWebhookController.cs` |
| `Smtp__Host` | Outgoing SMTP email server hostname. | Optional | `Services/CommunicationService.cs` |
| `Smtp__Port` | Outgoing SMTP server port (default 587). | Optional | `Services/Configuration/ApplicationOptions.cs` |
| `Smtp__Username` | Username for SMTP server authentication. | Optional | `Services/CommunicationService.cs` |
| `Smtp__Password` | Password for SMTP server authentication. | Optional | `Services/CommunicationService.cs` |
| `Smtp__From` | Default sender email address for outgoing system emails. | Optional | `Services/Configuration/ApplicationOptions.cs` |
| `Smtp__EnableSsl` | Enables TLS/SSL for SMTP server communication (true or false). | Optional | `Services/Configuration/ApplicationOptions.cs` |
| `WhatsApp__ApiKey` | Bearer authorization token for Meta WhatsApp Cloud API. | Optional | `Services/CommunicationService.cs` |
| `WhatsApp__PhoneNumberId` | Registered sender phone number ID in Meta Cloud API. | Optional | `Services/CommunicationService.cs` |
| `WhatsApp__Endpoint` | Custom URL endpoint override for WhatsApp API gateway requests. | Optional | `Services/Configuration/ApplicationOptions.cs` |
| `Database__AutoMigrate` | Enables automatic execution of DDL startup scripts in production (defaults to false). | Optional | `Program.cs`, `ApplicationOptions.cs` |
| `Security__TokenSecret` | Secret key used to sign time-limited private vault document download links. | Optional | `Services/MediaStorageService.cs` |
