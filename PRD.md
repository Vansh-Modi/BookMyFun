# BookMyFun — Product Requirements Document (PRD)

## 1. Product Vision & Goals
BookMyFun is a premier multi-dashboard Farmhouse, Luxury Villa, and Resort rental platform in India. The objective of this release is to provide enterprise-grade reliability, transparent cancellation and refund calculation, streamlined host onboarding via lead pipelines, flexible dual media ingestion (files and external URLs), role-segregated ID verification, dynamic commission hierarchy, damage claim processing, and real-time operational analytics with military-grade application security.

---

## 2. Core Functional Additions

### Feature 1: Booking, Cancellation & Automated Refund Engine
- **Business Rationale:** Eliminate disputes between hosts and guests regarding cancellations through explicit policy parameters and automated financial breakdown.
- **Requirements:**
  1. Property listing schema configurable with:
     - `FreeCancellationWindowHours` (e.g., 48 hours before check-in).
     - `CancellationCutoffHours` (e.g., 24 hours before check-in; strictly non-refundable past this deadline).
     - `CancellationFeeType`: `'percentage'` or `'fixed'`.
     - `CancellationFeeValue`: Decimal amount or percentage.
  2. Snapshot of these parameters must be written onto the `Bookings` record at booking creation to preserve policy terms against future property modifications.
  3. **Refund Logic:**
     - `CheckIn - CurrentTime > FreeCancellationWindow`: 100% refund minus payment gateway fee (default 2%).
     - `FreeCancellationWindow >= CheckIn - CurrentTime > CancellationCutoff`: Refund = `TotalAmount - CancellationFee - GatewayFee`.
     - `CheckIn - CurrentTime <= CancellationCutoff` or post-check-in: Non-refundable (Refund = 0).
  4. Transparent client-side confirmation modal displaying gross amount, deductions, and estimated net refund.
  5. Staff operational queue in Admin/Employee dashboards enabling manual review and overrides.

### Feature 2: Owner Onboarding Flow Revision (Lead Intake Pipeline)
- **Business Rationale:** Prevent unverified hosts from creating substandard listings by replacing open self-registration with an application and verification pipeline.
- **Requirements:**
  1. Remove the public "Owner" radio button from `/Account/Register`. Public registration is strictly for Clients/Guests.
  2. Add public **"Partner With Us / List Your Farm"** page and modal (`/Home/Partner`):
     - Intake fields: Full Name, Phone, Email, Property Name, Location, Acreage/Type, Message.
  3. Staff Onboarding Leads Grid:
     - Display incoming leads in Employee and Admin dashboards with status filtering (`New`, `Contacted`, `Approved`, `Rejected`).
     - "Approve & Invite Host" action generates an inactive `User` account with a cryptographically secure 24-hour activation token delivered via email.

### Feature 3: Media, Amenities, Location & Dual Upload System
- **Business Rationale:** Accommodate host media stored across diverse channels (direct mobile uploads, YouTube tour videos, external CDN links) without server bottlenecks.
- **Requirements:**
  1. **Dual Photo & Video Input:** Every media entry accepts either direct binary file upload or external HTTPS URL.
  2. **Photo Constraints:** Minimum 5 photos per listing before approval can be granted. File formats: JPG, PNG, WEBP (≤10MB each).
  3. **Video Handling:** Toggle between direct video file upload (MP4, MOV, WEBM ≤100MB) or streaming link embed (YouTube, Vimeo, Google Drive view).
  4. **Amenities Selector:** Multi-select taxonomy categorized by category (e.g., Water & Pool, Outdoor & Lawn, Power & Tech, Kitchen & Dining, Caretaker & Housekeeping).
  5. **Google Maps Integration:** Places Autocomplete and interactive pin drag-and-drop to capture `Latitude`, `Longitude`, and `FormattedAddress`.

### Feature 4: ID Verification & Privacy Governance
- **Business Rationale:** Comply with Indian hospitality regulations (KYC guest verification) while safeguarding guest personally identifiable information (PII) against unauthorized access.
- **Requirements:**
  1. Guests upload Government ID (Aadhaar, Passport, Driving License) during onboarding or checkout. Supports direct upload (PDF/JPG/PNG ≤5MB) or secure scanned link.
  2. **Storage Isolation:** ID files stored in a private, non-public storage directory using 15-minute signed expiring URLs.
  3. **Strict RBAC:** IDs visible only to the Client (owner), Employee, and Admin.
  4. **Host Privacy Shield:** Property Owners **cannot** view raw ID documents under any circumstance.
  5. **Verification Request Workflow:** If an owner flags an unverified booking, Employee/Admin reviews the document and marks the booking with a cryptographically signed "Verified Guest" badge or masked details (e.g. `XXXX-XXXX-4921`).

### Feature 5: Dynamic Commission Engine
- **Business Rationale:** Support differential monetization across property types and high-volume partner hosts.
- **Requirements:**
  1. Three-tier precedence hierarchy:
     $$\text{Effective Commission} = \text{Property.CommissionRate} \gg \text{Owner.CustomCommission} \gg \text{GlobalSettings.DefaultCommission}$$
  2. Commission snapshot (`CommissionRateSnapshot`, `PlatformCommissionAmount`, `OwnerPayoutAmount`) must be calculated and immutably written onto the `Bookings` record at creation time.

### Feature 6: Monetization & Subscription Tiers for Owners
- **Business Rationale:** Provide flexible host monetization through recurring listing capacity plans and managed listing services.
- **Requirements:**
  1. **Tier 1: Free / Starter:** Basic listing, standard search rank, default commission.
  2. **Tier 2: Featured / Spotlight:** Top search carousel, "Featured Farm" badge, lower commission.
  3. **Tier 3: Managed Concierge Pack (One-time):** Full professional drone photography and dedicated listing manager.
  4. Owner portal tier display with upgrade checkout; Admin manual override capability.

### Feature 7: Security Deposit & Damage Claim Architecture
- **Business Rationale:** Protect property hosts against property damage without exposing guests to arbitrary deductions.
- **Requirements:**
  1. Configurable `SecurityDepositAmount` collected via payment authorization/hold at booking checkout.
  2. **Damage Claim Module:**
     - Host may file a claim strictly within 24 hours post-checkout.
     - Mandatory itemized cost breakdown with photographic/video evidence.
     - Admin/Employee review and adjudication.
     - Upon approval, deducted funds transfer to host payout ledger; balance is automatically released to guest.

### Feature 8: Admin Management & Flexible Analytics Engine
- **Business Rationale:** Deliver actionable intelligence on revenue, occupancy, and host performance with multi-format export capabilities.
- **Requirements:**
  1. **Unified User Grid:** Role tabs (Clients, Owners, Employees, Admins), account suspension/reactivation, role assignment, and audit logs.
  2. **Dynamic Analytics:** Real-time GMV, Net Platform Commission, Pending Host Payouts, Active Properties, and Occupancy rates.
  3. **Multi-dimensional Filters:** Date presets (Today, 7D, MTD, YTD, Custom), Property, Owner, Status.
  4. **Export Engine:** Asynchronous CSV and Excel (.xlsx) generation with multi-sheet summaries.

---

## 3. Security & Compliance Protocols
- **File Upload Security:** Server-side magic-byte inspection (do not trust extensions or `Content-Type`), UUID renaming, path traversal sanitization, and virus-scan readiness.
- **SSRF Prevention:** External URLs strictly restricted to `https://` with DNS check to prohibit loopback, private IP ranges (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`), and cloud metadata APIs (`169.254.169.254`).
- **PCI-DSS Level 1 Principles:** Zero cardholder data stored on BookMyFun servers; client-side tokenization via Razorpay / Stripe SDKs.
- **Cryptographic Webhook Verification:** HMAC-SHA256 signature validation before processing gateway events.
- **Tenant Isolation:** Enforce object-level permissions preventing hosts from viewing another host's data via parameter tampering (IDOR prevention).
