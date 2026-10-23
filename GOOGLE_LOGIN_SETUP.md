# Google OAuth 2.0 Setup Guide for BookMyFun

This guide provides step-by-step instructions for configuring and enabling Google OAuth sign-in and registration for the BookMyFun platform.

---

## Prerequisites
- A Google Account (e.g., your business or project administrator email)
- Administrative access to your hosting environment (MonsterASP, IIS, or Cloud Server) to set environment variables
- Access to your domain DNS and live website URL

---

## Step 1: Create a Google Cloud Project
1. Navigate to the [Google Cloud Console](https://console.cloud.google.com/).
2. Click the project dropdown in the top header and select **New Project**.
3. Set **Project Name** to `BookMyFun` (or your preferred identifier).
4. Select your organization or leave as **No organization**, then click **Create**.
5. Once created, ensure your new project is selected in the project dropdown.

---

## Step 2: Configure the OAuth Consent Screen
1. In the Google Cloud Console left navigation menu, go to **APIs & Services** &rarr; **OAuth consent screen**.
2. Select **External** user type (allows any user with a Google account to sign in) and click **Create**.
3. Complete the **App Information** fields:
   - **App name:** `BookMyFun`
   - **User support email:** Select your administrator email (e.g., `support@YOUR-DOMAIN.com`).
   - **App logo:** Upload your official square app logo (120x120 px, PNG/JPEG).
4. Complete the **App Domain** fields:
   - **Application home page:** `https://YOUR-DOMAIN.com`
   - **Application privacy policy link:** `https://YOUR-DOMAIN.com/Home/Privacy` (or your public privacy policy URL)
   - **Application terms of service link:** `https://YOUR-DOMAIN.com/Home/Terms` (or your public terms URL)
5. Complete **Authorized domains:**
   - Add your top-level domain: `YOUR-DOMAIN.com` (e.g., `bookmyfun.in`).
6. Complete **Developer contact information:**
   - Enter your technical contact email.
7. Click **Save and Continue**.
8. In the **Scopes** step, click **Add or Remove Scopes**, select:
   - `.../auth/userinfo.email`
   - `.../auth/userinfo.profile`
   - `openid`
9. Click **Update** &rarr; **Save and Continue**.

---

## Step 3: Create a Web Application OAuth Client
1. Navigate to **APIs & Services** &rarr; **Credentials**.
2. Click **+ Create Credentials** at the top and select **OAuth client ID**.
3. Under **Application type**, select **Web application**.
4. Set **Name** to `BookMyFun Web Client`.

---

## Step 4: Add Authorised Redirect URIs for Local and Production
Under the OAuth client configuration, configure the URIs:

1. **Authorised JavaScript origins:**
   - Local: `https://localhost:7198` (or your local development port)
   - Production: `https://YOUR-DOMAIN.com`

2. **Authorised redirect URIs:**
   - Local: `https://localhost:7198/signin-google`
   - Production: `https://YOUR-DOMAIN.com/signin-google`

> [!IMPORTANT]
> The path must be exactly `/signin-google` without a trailing slash. This is the default callback handler path registered by ASP.NET Core `Microsoft.AspNetCore.Authentication.Google`.

3. Click **Create**.
4. A modal will display your **Client ID** and **Client Secret**. Copy these into a secure password manager or key vault.

---

## Step 5: Set the Environment Variables
Configure the client credentials securely in your hosting environment (do **not** commit these to `appsettings.json` or source control):

### Production (MonsterASP / IIS / Linux / Docker):
Set the following environment variables on the application server:
- `Google__ClientId` = `YOUR_GOOGLE_CLIENT_ID_PLACEHOLDER`
- `Google__ClientSecret` = `YOUR_GOOGLE_CLIENT_SECRET_PLACEHOLDER`

*(Note: On Windows/IIS, double underscore `Google__ClientId` automatically maps to the ASP.NET Core configuration key `Google:ClientId`).*

### Local Development (via dotnet user-secrets):
```bash
dotnet user-secrets set "Google:ClientId" "YOUR_GOOGLE_CLIENT_ID_PLACEHOLDER"
dotnet user-secrets set "Google:ClientSecret" "YOUR_GOOGLE_CLIENT_SECRET_PLACEHOLDER"
```

---

## Step 6: Enable Google Login Configuration Flag
Once credentials are set in the environment:

1. In `appsettings.json` (or via environment variable `GoogleLogin__Enabled=true`):
   ```json
   "GoogleLogin": {
     "Enabled": true
   }
   ```
2. Restart the ASP.NET Core application pool or container so the new environment variables and configuration flag are loaded.

---

## Step 7: Test in a Private Browser Window
1. Open a new Incognito / Private browsing window.
2. Navigate to `https://YOUR-DOMAIN.com/Account/Login` or `https://YOUR-DOMAIN.com/Account/Register`.
3. Verify that the **"Continue with Google"** / **"Sign Up with Google"** button appears.
4. Click the Google button and verify:
   - You are redirected to `accounts.google.com`.
   - The consent screen displays your app name (`BookMyFun`) and logo.
   - After selecting your Google account, you are redirected back to `https://YOUR-DOMAIN.com/signin-google` and signed in as a Client/Guest.

---

## Step 8: Publish the App (Move Out of Testing Status)
1. In the Google Cloud Console, return to **APIs & Services** &rarr; **OAuth consent screen**.
2. If **Publishing status** is set to **Testing**, only accounts explicitly added to the "Test users" list can log in.
3. Click **Publish App** and confirm.
4. For apps requesting standard scopes (`email`, `profile`, `openid`), full Google verification is **not** required unless sensitive or restricted scopes are added. Your app can now accept logins from all public Google accounts.
