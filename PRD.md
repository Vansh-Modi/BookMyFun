# BookMyFun — Product Requirements Document (PRD)

---

## 1. Executive Summary & Product Vision

### 1.1 Product Vision
**BookMyFun** is a premier digital marketplace and hospitality management platform tailored specifically for short-term and event-based rentals of farmhouses, private luxury villas, eco-resorts, and estate stays across India. The platform bridges the trust gap between discerning guests seeking exclusive, private escapes and property owners seeking optimized occupancy, predictable monetization, asset protection, and end-to-end event decoration services.

### 1.2 Core Business Objectives
- **Frictionless Guest Journey:** Deliver sub-second property discovery with flexible booking models (Full-Day, Morning/Evening Half-Day, Hourly Micro-stays) and integrated event décor add-ons.
- **Vetted Host Network:** Replace uncontrolled public host registrations with a curated partner lead pipeline and encrypted onboarding invitations.
- **Financial Transparency & Fairness:** Provide automated, policy-enforced cancellation quotes with transparent refund breakdowns, eliminating guest-host disputes.
- **Enterprise-Grade Asset Protection:** Safeguard host properties with refundable security deposits and an evidence-backed 24-hour damage claims adjudication engine.
- **Data Privacy & Compliance:** Fully comply with Indian hospitality KYC regulations while shielding raw guest government identification documents from property hosts.
- **Event Décor Ecosystem (5th Dashboard):** Provide specialized tooling for Event Managers and Decorators to curate celebration packages (weddings, birthdays, anniversaries, corporate retreats), manage assigned farm setups, advance execution milestones, and verify setups via media proofs.
- **Executive Operations & Analytics:** Empower administrators and support staff with real-time operational queues and multi-dimensional financial reporting (Property GMV vs Event GMV, platform commission, pending payouts, occupancy) with native Excel (`.xlsx`) and CSV exports.

---

## 2. Target Market & User Personas

```
                     ┌────────────────────────────────────────────────────────────────────────┐
                     │                           User Personas                                │
                     └───────────────────┬────────────────────────────────────────────────────┘
                                         │
        ┌──────────────────┬─────────────┼────────────┬──────────────────┬────────────────────┐
        │                  │             │            │                  │                    │
        ▼                  ▼             ▼            ▼                  ▼                    ▼
┌───────────────┐  ┌───────────────┐           ┌───────────────┐  ┌───────────────┐    ┌──────────────────┐
│ Client (Guest)│  │  Owner (Host) │           │Employee (Ops) │  │Admin (Super)  │    │Event Mgr/Decorator│
│               │  │               │           │               │  │               │    │                  │
│ • Families    │  │ • Farm Owners │           │ • Onboarding  │  │ • Platform    │    │ • Theme Stylists │
│ • Party Hosts │  │ • Villa Hosts │           │ • KYC Triage  │  │   Executives  │    │ • Floral/Sound   │
│ • Corporate   │  │ • Estate Mgrs │           │ • Claims Adj. │  │ • Finance Dir.│    │ • On-Ground Crew │
└───────────────┘  └───────────────┘           └───────────────┘  └───────────────┘    └──────────────────┘
```

### 2.1 Persona 1: The Guest / Client ("Urban Escape Seeker")
- **Profile:** Working professionals, families, party organizers, or corporate managers planning staycations, birthdays, pool parties, or offsites within 2–4 hours of metro hubs.
- **Key Needs:** Transparent pricing, flexible daytime/evening slots without paying full overnight rates, instant availability confirmation, crystal-clear cancellation policies, high-resolution photo/video tours, and the option to add curated decoration packages directly at checkout.
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
- **Key Needs:** Real-time visibility into Gross Merchandise Value (GMV) split between Property stays and Event setups, net platform revenues, pending host liabilities, property occupancy indices, granular user role management, system white-label branding, and native ClosedXML Excel/CSV exports for accounting.
- **Pain Points:** Data silos, slow reporting queries, manual financial reconciliation, and vulnerabilities to application-layer attacks (SSRF, malicious file uploads, injection).

### 2.5 Persona 5: Event Manager / Decorator ("Celebration Architect")
- **Profile:** Professional decorators, event planners, floral/lighting stylists, and celebration management agencies.
- **Key Needs:** Dedicated portal to publish and price themed décor packages (Birthday, Wedding, Anniversary, Corporate, Custom), track upcoming event setups tied to farm bookings, coordinate staging with hosts, advance setup lifecycle stages, and submit photo/video completion proofs before guest arrival.
- **Pain Points:** Last-minute booking confusion, uncoordinated property access, lack of digital setup checklists, and payment friction.

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
2. **Half-Day Morning Slot (`MorningSlot`):** 08:00 AM to 02:00 PM (ideal for daytime pool parties, family barbecues, and school reunions).
3. **Half-Day Evening Slot (`EveningSlot`):** 03:00 PM to 09:00 PM (ideal for sunset celebrations, cocktail gatherings, and evening dinners).
4. **Night Stay Slot (`NightStay`):** 09:00 PM to 07:00 AM.
5. **Hourly Micro-Stays (`Hourly`):** 2h, 4h, 6h, or 8h reservations with dynamically calculated pro-rata pricing.
- **Automated Conflict Prevention:** Prevents double bookings across overlapping full-day and half-day slots using atomic SQL check constraints.

### Module 3: Checkout, Dynamic Pricing & PCI-DSS Payment Gateway
- **Checkout Summary & Real-Time Calculation:**
  - Base rental amount calculated according to duration type and date range.
  - Optional Event Décor & Celebration package selection with real-time total updates.
  - Refundable Security Deposit hold clearly demarcated from base rental.
  - GST / Platform taxes (if applicable).
- **PCI-DSS Compliant Payment Flow:**
  - Hosted Razorpay checkout order generation.
  - Signature verification (`HMAC-SHA256`) on callback.
  - Immediate booking confirmation with transactional WhatsApp and Email receipts.

### Module 4: Policy-Enforced Cancellation & Automated Refund Engine
- **Configurable Policy Windows (Configured by Host; editable by Staff):**
  - `FreeCancellationWindowHours`: e.g. 48 hours prior to check-in.
  - `CancellationCutoffHours`: Strict deadline after which cancellations are non-refundable (e.g. 24 hours prior to check-in).
  - `CancellationFeeType`: 'percentage' | 'fixed'.
  - `CancellationFeeValue`: Numerical penalty applied outside the free window.
- **Automated Refund Rules:**
  - Prior to free cancellation window: 100% refund minus 2% payment gateway processing fee.
  - Between free window and cutoff: Total amount minus cancellation penalty and 2% gateway processing fee.
  - After cutoff or post-check-in: 0% refund (Non-refundable).
- **Client Transparency Modal:** Real-time breakdown showing exact gross paid, gateway fee, penalty deduction, and net refund prior to cancellation confirmation.
- **Staff Refund Operations Queue:** Admin and Employee override console to inspect dispute reasons and manually issue gateway refunds.

### Module 5: Curated Partner Lead Pipeline & Tokenized Host Onboarding
- **Elimination of Self-Registration:** Public registration is restricted to clients (`Client` role).
- **Partner Intake Form (`/Home/Partner`):** Full Name, Phone, Email, Property Name, Location, Acreage, and Notes.
- **Staff Qualification Console (`/Admin/OnboardingLeads`, `/Employee/OnboardingLeads`):**
  - Leads status: `New`, `Contacted`, `Approved`, `Rejected`.
  - One-click "Approve & Invite" generates an encrypted, 48-hour time-limited `HMAC-SHA256` token sent via email.
- **Host Activation Form (`/Account/ActivateHost`):** Prospective hosts click the secure token link to set their password, confirm farm details, and immediately enter the Owner Dashboard.

### Module 6: Dual-Mode Media Manager (Direct Upload + External URLs)
- **Photos:** Minimum 5 photos enforced per listing.
  - Option A: Drag-and-drop file upload (JPEG, PNG, WEBP up to 10MB each) with server magic-byte validation.
  - Option B: External direct URL embedding (Cloudinary, S3, CDN links).
- **Videos:**
  - Option A: Direct MP4, MOV, WEBM upload up to 100MB.
  - Option B: External video embedding (YouTube, Vimeo, Google Drive view URLs).
- **Security Protections:** Server-side magic-byte inspection prevents executable masquerading; SSRF DNS verification blocks loopback (`127.0.0.1`), private RFC 1918 ranges, and cloud metadata endpoints (`169.254.169.254`).

### Module 7: Categorized Amenities Taxonomy
- Curated amenities database with Bootstrap Icons and categories (Comfort, Leisure, Outdoor, Essentials, Safety).
- Multi-select checkbox interface with count badges and filter facets.

### Module 8: Google Maps & Precise Geolocation Integration
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
  - **Property GMV:** Gross merchandise volume from property stay reservations.
  - **Event GMV:** Gross merchandise volume from celebration/décor package add-ons.
  - **Total GMV:** Combined platform gross transacted volume.
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

### Module 15: Event Décor Packages & On-Ground Setup Fulfillment System (5th Dashboard)
- **Décor Packages Management (`/EventManager/Packages`, `/EventManager/CreatePackage`):**
  - Category classification: `Birthday`, `Anniversary`, `Wedding`, `Corporate`, `Custom`.
  - Base pricing, rich description, and bulleted included items.
  - Dual media showcase (cover photo upload or high-res showcase URL).
  - Status toggle (`Active` / `Inactive`).
- **Client Add-on Integration at Checkout:**
  - Guests toggle "Add Event Décor & Celebration Package" switch.
  - Live selection dynamically updates the estimated total payable.
  - Guests specify custom theme notes (e.g. "Neon lights, pink & gold theme, fairy lights on gazebo").
- **Assigned Setups & Milestone Lifecycle (`/EventManager/AssignedSetups`):**
  - Decorators access a tailored dashboard filtering reservations assigned to their crew.
  - Five-stage milestone lifecycle:
    1. `Pending`: Booking created; awaiting crew assignment.
    2. `Assigned`: Crew assigned to venue.
    3. `InProgress`: Decorators actively staging venue on event date.
    4. `Completed`: Setup finished on-ground; photographic proof uploaded.
    5. `Inspected`: Platform staff or host verified and cleared setup.
- **Proof-of-Setup Verification Modal:**
  - Decorators upload or link photo/video proofs of the completed setup before the guest checks in.

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
- **Atomic Ledgers:** Database mutations across bookings, payments, refunds, and décor setups execute within atomic database transactions.
- **Clean Code Standard:** Pristine, modular code with zero commented-out code snippets, zero redundant variables, and strict DRY architecture.
