# Event Managers and Décor Partners

## 1. Overview
Event Managers and Décor Studios offer curated event styling and setups for guests on BookMyFun via the "Stay & Celebrate" checkout feature. To prevent platform disintermediation, protect traveler safety, and ensure setup quality, the application enforces strict contact privacy filters and staff portfolio approvals.

Primary controllers and services:
- [`EventManagerController.cs`](file:///d:/Projects/BookMyFun/Controllers/EventManagerController.cs)
- [`ContactPrivacyScanner.cs`](file:///d:/Projects/BookMyFun/Services/ContactPrivacyScanner.cs)
- [`AdminController.cs`](file:///d:/Projects/BookMyFun/Controllers/AdminController.cs) (`EventManagerVerifications`, `FlaggedContacts`)

---

## 2. Allowed vs. Blocked Profile Fields

When an event manager manages their profile via `POST /EventManager/Profile`, input is strictly filtered:

### 2.1 Allowed Fields
- `BusinessName`: Studio brand or trade name.
- `City`: Primary operating city.
- `State`: Primary operating state.
- `Bio`: Description of services and experience (subject to automated contact privacy scanning).
- `CancellationPolicy`: Terms governing décor cancellations (subject to privacy scanning).

### 2.2 Blocked Fields
The application explicitly checks for and rejects submissions containing any of the following fields:
- `Phone`
- `Email`
- `WhatsAppPhone`
- `ProfilePictureUrl`
- `Website`
- `SocialLinks`
- `LogoUrl`

If any of these fields are submitted, the request is rejected with the message:
> *"Custom profile pictures, logos, and contact details are strictly restricted to maintain platform safety."*

Profile avatars are locked to the system default: `/images/default-avatar.svg`.

---

## 3. Automated Contact-Detail Masking Rule

The [`ContactPrivacyScanner.cs`](file:///d:/Projects/BookMyFun/Services/ContactPrivacyScanner.cs) sanitizes all public text fields entered by event managers, including `Bio`, `CancellationPolicy`, and package descriptions.

### 3.1 Intercepted Patterns
1. **Phone Numbers**: 10-digit phone numbers, spaced numbers, hyphenated numbers, and numbers prefixed with `+91`, `0091`, or `91`.
2. **Email Addresses**: Any standard RFC email address format.
3. **Web Links & Social Handles**: URLs starting with `http`, `https`, or `www`, shorteners (`bit.ly`, `t.me`), domain extensions (`.com`, `.in`, `.co.in`, `.org`), and social domains (`wa.me`, `instagram.com`, `facebook.com`, `linkedin.com`).
4. **Solicitation Keywords**: Phrases intended to prompt offline contact (e.g., `"call me"`, `"reach us"`, `"whatsapp"`, `"dm me"`, `"message on wa"`, `"telegram"`, `"dial our"`).

### 3.2 Masking and Audit Logging
- When an infraction is detected, the offending snippet is replaced with:
  ```text
  [Contact Details Masked]
  ```
- An audit record is stored in the [`FlaggedContactAttempts`](file:///d:/Projects/BookMyFun/Docs/database.md#flaggedcontactattempts) table, recording:
  - `UserID`: Account ID of the submitter.
  - `ContentType`: Name of modified field (`Bio`, `PackageDescription`, etc.).
  - `DetectedPatterns`: List of matched pattern rules.
  - `OriginalContentSnippet`: Raw submitted text.
  - `SanitizedContent`: Cleaned text output.
- Administrators review flagged attempts at `/Admin/FlaggedContacts`.

---

## 4. Portfolio Showcase Approval Flow

Event managers display past decoration projects to guests on their public showcase page (`/EventManager/PublicProfile/{id}`).

1. **Upload**:
   - Event managers upload photos via `/EventManager/UploadPortfolio`.
   - Photos are stored in `/uploads/portfolio/`.
   - New items are created in [`EventManagerPortfolios`](file:///d:/Projects/BookMyFun/Docs/database.md#eventmanagerportfolios) with `IsApproved = 0` (`Pending Review`).
2. **Staff Moderation**:
   - Operations staff and administrators review submissions in `/Admin/EventManagerVerifications` and `/Employee/PartnerApprovals`.
   - Staff examine photos to verify aesthetic quality and confirm no phone numbers, watermarks, or social handles are displayed in the image.
3. **Approval**:
   - When approved, `IsApproved` is set to `1`, `ApprovedAt` is recorded, and `ApprovedByUserID` logs the reviewing staff member.
   - Only items with `IsApproved = 1` are shown on the public profile.

---

## 5. Partner Verification Statuses

An event manager's standing is stored in `Users.VerificationStatus`:

| Status | Meaning | Checkout Eligibility |
|:---|:---|:---:|
| `Pending` | Application submitted; identity or business credentials under review. | **Ineligible**: Packages do not appear in search or checkout. |
| `Verified` | Approved by staff. Active service area verified. | **Eligible**: Packages are displayed in "Stay & Celebrate" checkout. |
| `Rejected` | Application denied by moderation team. | **Ineligible**: Profile and packages disabled. |
| `Suspended` | Account disabled due to policy infractions (e.g. repeated contact circumvention). | **Ineligible**: All services hidden. |
