# BookMyFun Environment Configuration & Setup Guide

This document outlines the required and optional environment variables used to configure the BookMyFun application. In ASP.NET Core, hierarchical configuration keys can be provided as environment variables by replacing colons (`:`) with double underscores (`__`).

---

## 1. Database Connection

| Environment Variable | Configuration Key | Required | Description |
| :--- | :--- | :--- | :--- |
| `ConnectionStrings__DefaultConnection` | `ConnectionStrings:DefaultConnection` | **Yes** | ADO.NET SQL Server connection string specifying the database server host, database name, authentication credentials, and connection security flags. |

---

## 2. Payment Gateway (Razorpay)

| Environment Variable | Configuration Key | Required | Description |
| :--- | :--- | :--- | :--- |
| `Razorpay__KeyId` | `Razorpay:KeyId` | **Yes** | Razorpay merchant public Key ID used to initialize checkout orders on the server and client. |
| `Razorpay__KeySecret` | `Razorpay:KeySecret` | **Yes** | Razorpay merchant secret key used on the server for checkout HMAC-SHA256 signature verification. |
| `Razorpay__WebhookSecret` | `Razorpay:WebhookSecret` | **Yes** | Razorpay webhook secret configured in Razorpay Dashboard used for verifying the `X-Razorpay-Signature` header on incoming webhook deliveries. |

---

## 3. External Authentication (Google OAuth 2.0)

| Environment Variable | Configuration Key | Required | Description |
| :--- | :--- | :--- | :--- |
| `Authentication__Google__ClientId` | `Authentication:Google:ClientId` | Optional | Google OAuth 2.0 Client ID obtained from the Google Cloud Console (Credentials) for web application sign-in. |
| `Authentication__Google__ClientSecret` | `Authentication:Google:ClientSecret` | Optional | Google OAuth 2.0 Client Secret corresponding to the Client ID used to exchange authorization codes for access tokens. |
| `Authentication__Google__ProjectEmail` | `Authentication:Google:ProjectEmail` | Optional | Administrative/project Google account email address associated with the OAuth credentials and project management. |

---

## 4. Email Notifications (SMTP)

| Environment Variable | Configuration Key | Required | Description |
| :--- | :--- | :--- | :--- |
| `Smtp__Host` | `Smtp:Host` | Optional | SMTP mail server hostname used for sending transactional emails, OTP verification codes, and booking receipts. |
| `Smtp__Port` | `Smtp:Port` | Optional | TCP port number for SMTP communication (typically `587` for STARTTLS or `465` for SSL). |
| `Smtp__Username` | `Smtp:Username` | Optional | Username or account email used to authenticate with the SMTP relay server. |
| `Smtp__Password` | `Smtp:Password` | Optional | Password or application-specific password used to authenticate with the SMTP relay server. |
| `Smtp__From` | `Smtp:From` | Optional | Default sender email address displayed on outbound customer and host emails. |
| `Smtp__EnableSsl` | `Smtp:EnableSsl` | Optional | Boolean flag (`true` or `false`) indicating whether TLS/SSL encryption is enforced for outbound email dispatch. |

---

## 5. WhatsApp & SMS Notifications

| Environment Variable | Configuration Key | Required | Description |
| :--- | :--- | :--- | :--- |
| `WhatsApp__ApiKey` | `WhatsApp:ApiKey` | Optional | Bearer API token for authenticating with the WhatsApp Business Cloud API or messaging gateway. |
| `WhatsApp__PhoneNumberId` | `WhatsApp:PhoneNumberId` | Optional | Meta / WhatsApp Cloud API Sender Phone Number ID used as the sender origin for transactional notifications. |
| `WhatsApp__Endpoint` | `WhatsApp:Endpoint` | Optional | Destination API URL endpoint for dispatching WhatsApp messages. |

---

## 6. Runtime Environment

| Environment Variable | Required | Description |
| :--- | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | Recommended | Application hosting environment (`Development`, `Staging`, or `Production`). Controls developer exception pages, HTTPS enforcement, and security header configurations. |
