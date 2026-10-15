# BookMyFun — Product Requirements Document (PRD)

---

## 1. Executive Summary & Product Vision

### 1.1 Product Vision
**BookMyFun** is a premier digital marketplace and hospitality management platform tailored specifically for short-term and event-based rentals of farmhouses, private luxury villas, eco-resorts, and estate stays across India. The platform bridges the trust gap between discerning guests seeking exclusive, private escapes and property owners seeking optimized occupancy, predictable monetization, and asset protection.

### 1.2 Core Business Objectives
- **Frictionless Guest Journey:** Deliver sub-second property discovery with flexible booking models (Full-Day, Morning/Evening Half-Day, Hourly Micro-stays).
- **Vetted Host Network:** Replace uncontrolled public host registrations with a curated partner lead pipeline and encrypted onboarding invitations.
- **Financial Transparency & Fairness:** Provide automated, policy-enforced cancellation quotes with transparent refund breakdowns, eliminating guest-host disputes.
- **Enterprise-Grade Asset Protection:** Safeguard host properties with refundable security deposits and an evidence-backed 24-hour damage claims adjudication engine.
- **Data Privacy & Compliance:** Fully comply with Indian hospitality KYC regulations while shielding raw guest government identification documents from property hosts.
- **Executive Operations & Analytics:** Empower administrators and support staff with real-time operational queues and multi-dimensional financial reporting (GMV, platform commission, pending payouts, occupancy) with native Excel (`.xlsx`) and CSV exports.

---

## 2. Target Market & User Personas

```
                     ┌────────────────────────────────────────────────────────┐
                     │                    User Personas                       │
                     └───────────────────┬────────────────────────────────────┘
                                         │
        ┌──────────────────┬─────────────┴────────────┬──────────────────┐
        │                  │                          │                  │
        ▼                  ▼                          ▼                  ▼
┌───────────────┐  ┌───────────────┐          ┌───────────────┐  ┌───────────────┐
│ Client (Guest)│  │  Owner (Host) │          │Employee (Ops) │  │Admin (Super)  │
│               │  │               │          │               │  │               │
│ • Families    │  │ • Farm Owners │          │ • Onboarding  │  │ • Platform    │
│ • Party Hosts │  │ • Villa Hosts │          │ • KYC Triage  │  │   Executives  │
│ • Corporate   │  │ • Estate Mgrs │          │ • Claims Adj. │  │ • Finance Dir.│
└───────────────┘  └───────────────┘          └───────────────┘  └───────────────┘
```

### 2.1 Persona 1: The Guest / Client ("Urban Escape Seeker")
- **Profile:** Working professionals, families, party organizers, or corporate managers planning staycations, birthdays, pool parties, or offsites within 2–4 hours of metro hubs.
- **Key Needs:** Transparent pricing, flexible daytime/evening slots without paying full overnight rates, instant availability confirmation, crystal-clear cancellation policies, and high-resolution photo/video tours.
- **Pain Points:** Hidden cleaning/security fees, unvetted rustic properties lacking basic amenities, non-transparent cancellation penalties, and fears of identity misuse when handing over physical ID copies to roadside caretakers.

### 2.2 Persona 2: The Property Host / Owner ("Farm Partner")
- **Profile:** Private landowners, farmhouse owners, or luxury villa hosts looking to monetize real estate during vacancies without operational overhead.
- **Key Needs:** Verified guest profiles, upfront security deposits, prompt 90% payout disbursement, asset protection against unruly parties, flexible dual-mode media upload (direct mobile files or YouTube tour videos), and tiered platform visibility.
- **Pain Points:** Property damage caused by guests without recourse, fraudulent booking cancellations, complex software interfaces, and platform commissions eroding margins.

### 2.3 Persona 3: Operations & Support Staff ("Platform Guardian")
- **Profile:** Platform operations executives, customer happiness agents, and trust & safety specialists.
- **Key Needs:** Dedicated operational triage queues to review inbound partner leads, securely verify client KYC documents without leaking PII, adjudicate damage claims fairly based on photo/video proof, and execute manual refund overrides.
- **Pain Points:** Scattered email inquiries, manual document inspection, lack of audit trails for staff overrides, and unresolved guest-host disputes.

### 2.4 Persona 4: Platform Administrator ("Executive Superuser")
- **Profile:** Founders, business heads, and finance directors.
- **Key Needs:** Real-time visibility into Gross Merchandise Value (GMV), net platform revenues, pending host liabilities, property occupancy indices, granular user role management, system white-label branding, and native ClosedXML Excel/CSV exports for accounting.
- **Pain Points:** Data silos, slow reporting queries, manual financial reconciliation, and vulnerabilities to application-layer attacks (SSRF, malicious file uploads, injection).

---

## 3. Comprehensive Feature Modules

### Module 1: Stays & Experiences Catalog, Discovery & Search
- **Multi-Criteria Filter Engine:**
  - Free-text location matching (e.g., "Lonavala", "Karjat", "Alibaug", "SoNA").
  - Property Type filter (Farmhouse, Luxury Villa, Eco-Resort, Pool Villa).
  - Suitable Occasion taxonomy (Pool Party, Wedding, Family Gathering, Corporate Offsite, Birthday, Weekend Getaway).
  - Price range sliders (Min/Max night rate).
  - Guest capacity threshold ($1$ to $1000+$ guests).
- **Public Property Showcase:**
  - High-res primary hero banner, interactive photo carousel, and dedicated video tour button.
  - Verified host badge and star rating summary aggregated from confirmed stays.
  - Interactive driving directions and Google Maps link based on precise GPS latitude/longitude.
  - Key amenities display with visual icons (Swimming Pool, Bonfire, BBQ, Gazebo, Caretaker, AC, WiFi, Power Backup).
  - Transparent cancellation policy breakdown card.

### Module 2: Flexible Multi-Duration Booking Engine
BookMyFun recognizes that farmhouses and villas are frequently rented for day picnics, evening celebrations, or hourly photo shoots rather than conventional 24-hour hotel stays:
1. **Full-Day Overnight Stay (`FullDay`):** Traditional check-in (11:00 AM) to check-out (10:00 AM next morning).
2. **Half-Day Morning Slot (`MorningSlot`):** 09:00 AM to 05:00 PM (ideal for daytime pool parties, family barbecues, and school reunions).
3. **Half-Day Evening Slot (`EveningSlot`):** 06:00 PM to 08:00 AM (ideal for evening cocktail parties, bonfires, and night stargazing).
4. **Hourly Micro-Stays (`Hourly`):** 4h, 6h, or 8h reservations with dynamically calculated pro-rata pricing.
- **Automated Conflict Prevention:** Prevents double bookings across overlapping full-day and half-day slots using atomic SQL check constraints.

### Module 3: Checkout, Dynamic Pricing & PCI-DSS Payment Gateway
- **Checkout Summary:**
  - Base rental amount calculated according to slot duration and seasonal rates.
  - Refundable Security Deposit hold clearly demarcated from base rental.
  - GST / Platform taxes (if applicable).
- **PCI-DSS Compliant Payment Flow:**
  - Direct integration with Razorpay Payment Gateway.
  - No raw credit card or CVV details are ever handled, logged, or stored on application servers.
  - Client initiates payment order (`/Booking/CreateOrder`); browser displays Razorpay Checkout modal; payment completion is verified server-side with HMAC-SHA256 signature verification (`/Booking/VerifyPayment`).
  - Automated SMS and WhatsApp confirmation dispatch upon successful payment receipt.

### Module 4: Cancellation Policy Engine & Transparent 3-Tier Refunds
- **Property-Level Parameters:**
  - `FreeCancellationWindowHours`: Deadline prior to check-in (e.g., 48 hours) where client receives full refund minus 2% gateway processing fee.
  - `CancellationCutoffHours`: Strict cutoff deadline (e.g., 24 hours) after which cancellations are completely non-refundable ($0$ refund).
  - `CancellationFeeType`: `'percentage'` (e.g., 10%) or `'fixed'` (e.g., ₹1,000) applied between the free window and the cutoff deadline.
  - `CancellationFeeValue`: Decimal value of the fee.
- **Immutable Policy Snapshotting:** To prevent retrospective policy changes from affecting active bookings, every reservation snapshots policy parameters into `Bookings` at creation:
  - `FreeCancellationWindowHoursSnapshot`
  - `CancellationCutoffHoursSnapshot`
  - `CancellationFeeTypeSnapshot`
  - `CancellationFeeValueSnapshot`
- **Transparent Refund Engine:**
  - *Tier A (Free Window):* $\text{Refund} = \text{TotalPaid} - \text{GatewayFee (2\%)}$
  - *Tier B (Cutoff Window):* $\text{Refund} = \text{TotalPaid} - \text{CancellationFee} - \text{GatewayFee}$
  - *Tier C (Non-Refundable):* Inside cutoff deadline or post-checkin: $\text{Refund} = ₹0$
- **Client Quote Modal:** Real-time modal on `/Client/Index` displays the exact calculation before client confirms cancellation.
- **Staff Refund Operations Queue:** Admin and Employee dashboard queue (`/Admin/RefundsQueue`, `/Employee/RefundsQueue`) with capabilities to inspect automated quotes, verify gateway refund IDs, and execute staff overrides.

### Module 5: Host Monetization & Subscription Tiers
- **Subscription Plans:**
  - **Starter / Free:** Basic listing, organic search placement, standard 10% platform commission, up to 1 active listing.
  - **Featured / Spotlight:** Top search carousel, "Featured Farm" badge, optimized 8% platform commission, up to 3 active listings.
  - **Managed Concierge Pack:** Priority ranking, professional photography/drone visit by field staff, 7% commission, up to 10 active listings.
- **Owner Dashboard Experience:**
  - Active plan name, expiration date, active listings count vs. quota limit.
  - Direct upgrade portal integrated with payment gateway.
- **Admin Tier Management:** Ability to manually override host subscription tiers and assign custom negotiated commission rates.

### Module 6: Partner Lead Acquisition & Encrypted Host Onboarding
- **Public Self-Registration Elimination:** Prevent unauthorized or unvetted hosts from creating dummy listings. The public register page is strictly for Clients.
- **Public Partner Intake Portal (`/Home/Partner`):**
  - Fields: Full Name, Phone, Email, Property Name, Location, Acreage/Property Type, Message.
- **Staff Onboarding Pipeline (`/Admin/PartnerLeads`, `/Employee/PartnerLeads`):**
  - Leads categorized by `New`, `Contacted`, `Approved`, `Rejected`.
  - **"Approve & Invite Owner" Action:** Automatically creates an inactive `User` account with role `Owner` and issues an encrypted, time-limited (48-hour) activation token.
  - Automated email dispatch containing activation link (`/Account/ActivateHost?token=...`).
  - Host activates account, sets password, and gains immediate access to the Host Dashboard.

### Module 7: Dual-Mode Media Manager (Photos & Videos)
- **Dual Ingestion Channels:**
  1. **Direct Binary File Upload:** Drag-and-drop or file picker uploading to `/api/media/upload-file`. Validated via server-side magic bytes.
  2. **External URL / Embed:** Direct CDN links or streaming embeds (YouTube, Vimeo) validated against SSRF via `/api/media/verify-url`.
- **Photo Constraints:**
  - Minimum 5 photos required per listing before the listing can be published.
  - Real-time client-side photo counter badge changing from red to green when $\ge 5$ photos are provided.
  - Accepted formats: JPG, PNG, WEBP ($\le 10\text{MB}$ each).
- **Video Tour Toggle:**
  - Switch between "Upload Video File" (MP4, MOV, WEBM $\le 100\text{MB}$) and "Paste Video Link (YouTube/Vimeo)".
- **Media Gallery Grid:** Real-time thumbnail preview cards with primary cover photo selection and deletion controls.

### Module 8: Amenities Taxonomy & Geolocation
- **Amenities Master Catalog (`AmenitiesMaster`):** Pre-seeded with 12 foundational amenities categorized by Water & Pool, Tech, Outdoor, and Service:
  - Private Swimming Pool, Gazebo, Bonfire Pit, Barbecue Grill, Power Backup, Lawn Area, Caretaker, Pet-friendly, Sound System, High-Speed Wi-Fi, Air Conditioning, Free Parking.
- **Interactive Geolocation:**
  - Automatic browser GPS coordinate detection (`navigator.geolocation`).
  - Google Places formatted address input.
  - Exact `Latitude` and `Longitude` stored for every property.
  - Client listing displays interactive "Open in Google Maps" link with driving directions.

### Module 9: Guest ID Verification & KYC Privacy Vault
- **Compliance Rationale:** Mandatory compliance with local hospitality regulations requiring guest identification before occupancy.
- **Confidential Vault Storage:**
  - Government ID files (Aadhaar, Passport, Driving License) are stored in `App_Data/PrivateDocuments` outside the web root.
  - Access is governed by 15-minute cryptographically signed tokens (`HMACSHA256`).
- **Owner-Shielded Access:** Property Owners **cannot** view raw ID documents under any circumstance to prevent PII exposure and identity theft.
- **Staff KYC Triage Queue (`/Admin/IdVerifications`, `/Employee/IdVerifications`):**
  - Operations staff inspect documents via secure expiring link.
  - Staff approve (issuing a "Verified Guest" badge) or reject (with feedback reason: "Document blurry", "Expired ID", etc.).

### Module 10: Host Protection & 24h Damage Claims
- **Security Deposit Collection:** Pre-authorized hold or deposit captured at checkout.
- **24-Hour Post-Checkout Window:** Hosts must file damage claims strictly within 24 hours of guest checkout.
- **Itemized Claim Submission (`/Owner/CreateClaim`):**
  - Claim amount capped at the collected security deposit.
  - Detailed incident description and itemized loss breakdown.
  - Direct upload or external link attachment of damage photos/videos.
- **Adjudication Console (`/Admin/DamageClaims`, `/Employee/DamageClaims`):**
  - Operations staff review evidence.
  - Rulings: Approve (deduct approved sum and transfer to host, release remaining balance to guest) or Reject (release full deposit hold to guest).

### Module 11: Dynamic Analytics & Reporting Engine
- **Real-Time KPI Metrics:**
  - **Gross Merchandise Value (GMV):** Total confirmed booking volume.
  - **Net Platform Commission:** Actual revenue earned by the platform.
  - **Pending Host Payouts:** Gross amounts minus platform cut payable to hosts.
  - **Active Properties:** Live published inventory.
  - **Occupancy Rate:** Percentage utilization index over 30 days.
- **Multi-Dimensional Filters:** Time Period (Today, 7D, MTD, YTD, Custom Date Range) + Property, Owner, and Booking Status filters.
- **Export Deliverables:**
  - **ClosedXML Native Excel (`.xlsx`):** Professional spreadsheet with emerald headers, cell borders, currency formatting, and column auto-fit.
  - **Raw CSV Stream:** Clean comma-delimited data for external ingestion.

### Module 12: Customer Support Desk & Contact Us Ticketing
- Public inquiry intake form (`/Home/Contact`) capturing Name, Email, Phone, Subject, and Message.
- Operations review queue (`/Admin/SupportQueries`, `/Employee/SupportQueries`) with ticket status tracking (`New`, `Responded`).

### Module 13: Verified Guest Reviews & Reputation System
- Only guests with confirmed, completed stays can submit ratings ($1$ to $5$ stars) and qualitative feedback.
- Average rating computed and showcased across property discovery cards and detail headers.

### Module 14: Dark / Light Mode Theming & Dynamic Branding
- Instant dark/light mode toggle with anti-FOUC script preventing theme flash.
- CSS variables for background, text, borders, and cards dynamically adjusted.
- White-label configuration (`SiteSettingsService`) for Site Name, Tagline, Phone Helpline, WhatsApp number, and Logo URL.

---

## 4. Non-Functional Requirements (NFRs)

### 4.1 Performance & Latency
- **Time to First Byte (TTFB):** $< 150\text{ms}$ on standard broadband.
- **Database Query Latency:** All parameterized ADO.NET queries must execute in $< 50\text{ms}$ through targeted indexes on `BookingID`, `PropertyID`, `OwnerID`, and `CreatedAt`.
- **Media Upload Throughput:** Direct file uploads stream asynchronously to disk without loading entire payloads into server RAM.

### 4.2 Security & Data Privacy
- **OWASP Top 10 Compliance:** Mitigate injection via parameterized queries, cross-site scripting via Razor auto-encoding, and broken access control via tenant validation.
- **Magic-Byte Binary Inspection:** Verify file headers on server before saving.
- **SSRF Defense:** Reject loopback, link-local, private RFC 1918, and AWS/GCP metadata (`169.254.169.254`) on external media links.
- **PCI-DSS Compliance:** Never handle or store raw payment card data.

### 4.3 High Availability & Data Integrity
- **Idempotency:** Payment webhooks and cancellation refund calls enforce idempotency keys to eliminate double-charging or duplicate payouts.
- **Atomic Ledgers:** Database mutations across bookings, payments, and refunds execute within atomic database transactions.
