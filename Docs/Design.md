# BookMyFun — UI/UX Design System & Component Specifications

## 1. Design System Philosophy & Aesthetic Vision

BookMyFun employs a clean, modern, and uncluttered design system tailored for hospitality and vacation property management. The design prioritizes visual breathability, generous negative space, clear typographic hierarchy, soft border radii, and seamless light/dark mode transitions without Flash of Unstyled Content (FOUC).

### Core Design Tenets:
1. **Uncluttered & Clean:** Elimination of heavy bordered boxes and loud saturated backgrounds in favor of soft pastel surface tints, subtle borders (`rgba(0,0,0,0.08)` / `rgba(255,255,255,0.1)`), and floating card shadows.
2. **Typography First:** Built on the geometric sans-serif **Plus Jakarta Sans**, offering superior legibility across headings, numeric analytics, and data grids.
3. **Instant Visual Feedback:** Micro-interactions on buttons, form inputs, dropzones, and modals with smooth 200ms cubic-bezier transitions.
4. **Accessible by Default:** Full adherence to WCAG 2.1 AA standards with high-contrast text ratios (≥ 4.5:1 for body copy, ≥ 3:1 for large headers).

---

## 2. Color Palette & Theming Tokens

All colors, surface elevations, and typography tokens are declared as CSS custom properties in [`wwwroot/css/site.css`](file:///d:/Projects/BookMyFun/wwwroot/css/site.css).

### 2.1 Theme Tokens Matrix

| Token Name | Light Mode Value | Dark Mode Value | Usage Context |
|:---|:---|:---|:---|
| `--bg-body` | `#f8fafc` (Slate 50) | `#0f172a` (Slate 900) | Root application background |
| `--bg-card` | `#ffffff` (White) | `#1e293b` (Slate 800) | Stat cards, property cards, modals |
| `--bg-elevated` | `#ffffff` | `#334155` (Slate 700) | Dropdowns, tooltips, popovers |
| `--bg-table-head`| `#f1f5f9` (Slate 100) | `#0f172a` (Slate 900) | Data table header rows |
| `--text-primary` | `#0f172a` (Slate 900) | `#f8fafc` (Slate 50) | Primary titles, table rows, form inputs |
| `--text-secondary`| `#64748b` (Slate 500)| `#94a3b8` (Slate 400)| Subtitles, metadata, form labels |
| `--text-muted` | `#94a3b8` (Slate 400) | `#64748b` (Slate 500) | Timestamps, inactive hints |
| `--card-border` | `#e2e8f0` (Slate 200) | `#334155` (Slate 700) | Subtle container outlines |
| `--primary-blue` | `#2563eb` (Blue 600) | `#3b82f6` (Blue 500) | Primary CTA buttons, active tabs |
| `--primary-hover`| `#1d4ed8` (Blue 700) | `#60a5fa` (Blue 400) | Primary button hover state |
| `--accent-amber` | `#f59e0b` (Amber 500) | `#fbbf24` (Amber 400) | Featured badges, star ratings |
| `--success-green`| `#10b981` (Emerald 500)| `#34d399` (Emerald 400)| Verified badges, confirmed bookings |
| `--danger-red` | `#ef4444` (Red 500) | `#f87171` (Red 400) | Cancellation alerts, damage claims |

### 2.2 Anti-FOUC Inline Hydration Script

To prevent visual flashing when a user reloads the application in dark mode, the following inline script is executed in the `<head>` tag of [`Views/Shared/_Layout.cshtml`](file:///d:/Projects/BookMyFun/Views/Shared/_Layout.cshtml) prior to CSS rendering:

```html
<script>
  (function () {
    const savedTheme = localStorage.getItem('bmf_theme') || 
      (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    document.documentElement.setAttribute('data-bs-theme', savedTheme);
  })();
</script>
```

---

## 3. Global Layout Architecture & Navigation (5 Roles)

### 3.1 Responsive Navigation Bar
- **Brand Identity:** High-contrast logo with an organic leaf/farm icon and bold modern typography.
- **Role-Aware Navigation:**
  - **Anonymous:** Explore Catalog, List Your Farm, Contact Us, Login / Sign Up.
  - **Client:** Explore Stays, My Bookings, Saved Villas, Account Profile.
  - **Owner:** Listings Portfolio, Booking Calendar, Financial Payouts, Add Farm.
  - **Employee:** Onboarding Leads Pipeline, ID Verifications, Refunds Queue, Damage Claims.
  - **Admin:** Master Analytics, Financial Ledger, Onboarding Leads, Refunds, Users, Site Config.
  - **Event Manager / Decorator:** Decorator Dashboard, Décor Packages, Assigned Setups.
- **Theme Switcher Pill:** One-click toggle between Sun (Light) and Moon (Dark) modes with animated icon rotation.

### 3.2 Responsive Card Grid & Breakpoints
- **Mobile (`< 576px`):** Single column full-width cards with 1rem gutters and sticky bottom actions.
- **Tablet (`576px - 991px`):** 2-column grid for property listings and 2x2 grid for dashboard KPIs.
- **Desktop (`≥ 992px`):** 3-column or 4-column fluid CSS grid with 1.5rem gap and fixed sidebar navigation for administrative dashboards.

---

## 4. Key Interactive Component Layouts

### 4.1 Dual-Mode Property Media Manager
Allows hosts to provide listing media either via direct file upload (drag-and-drop) or external streaming links (YouTube, Vimeo, Cloudinary, AWS S3).

```
┌────────────────────────────────────────────────────────────────────────────┐
│  Property Media & Gallery Manager                                          │
│  Minimum 5 photos required. Videos optional.                               │
│                                                                            │
│  [📷 Photos (5)]   [🎥 Video Walkthrough (1)]                              │
│  ────────────────────────────────────────────────────────────────────────  │
│  Upload Mode:  (●) Direct File Upload        (○) External Web Link / Embed │
│                                                                            │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │   ⬆ Drag & drop high-resolution photos here or click to browse       │  │
│  │   Accepts JPG, PNG, WEBP up to 10MB each (Magic-byte inspected)      │  │
│  │   [Choose Photo Files]                                               │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│  OR: Enter External URL: [ https://images.unsplash.com/photo-154...      ] │
│      [+ Verify & Add External Media]                                       │
│                                                                            │
│  Active Listing Gallery (6 items):                                         │
│  ┌───────────────┐ ┌───────────────┐ ┌───────────────┐ ┌─────────────────┐ │
│  │ [Image Prev]  │ │ [Image Prev]  │ │ [Image Prev]  │ │ [YouTube Video] │ │
│  │ ★ Primary     │ │ (File: 2.1 MB)│ │ (URL: Ext)    │ │ (Embedded)      │ │
│  │ [Make Primary]│ │ [Make Primary]│ │ [Make Primary]│ │ [Play Preview]  │ │
│  │ [🗑 Remove]    │ │ [🗑 Remove]    │ │ [🗑 Remove]    │ │ [🗑 Remove]     │ │
│  └───────────────┘ └───────────────┘ └───────────────┘ └─────────────────┘ │
└────────────────────────────────────────────────────────────────────────────┘
```

#### Interactive States:
- **Dragover:** The dropzone border transitions from dashed grey to solid indigo (`#2563eb`) with an expanding pulse animation.
- **Client Validation:** Checks file size (`file.size <= 10 * 1024 * 1024` for photos, `100MB` for videos) before queuing.
- **SSRF Validation Spinner:** When an external link is pasted, a verification spinner tests DNS resolution against blocked IP ranges before rendering the preview card.

---

### 4.2 Transparent Cancellation & Automated Refund Modal
Displayed to clients before they commit to cancelling a confirmed reservation.

```
┌────────────────────────────────────────────────────────────────────────────┐
│  Confirm Booking Cancellation — Reservation #BMF-8841                      │
├────────────────────────────────────────────────────────────────────────────┤
│  Property: Whispering Pines Farm Villa (Lonavala)                          │
│  Check-In: 24 Oct 2026, 11:00 AM (Check-in is in 54 hours)                 │
│                                                                            │
│  Policy Snapshot Applied:                                                  │
│  - Free Cancellation Window: 48 Hours prior to check-in                    │
│  - Status: Currently within Free Cancellation Window                       │
│                                                                            │
│  Refund Financial Breakdown:                                               │
│  ┌───────────────────────────────────────────────────┬──────────────────┐  │
│  │ Gross Amount Paid:                                │     ₹ 24,000.00  │  │
│  │ Less: Payment Gateway Processing Fee (2% non-ref):│    - ₹   480.00  │  │
│  │ Less: Cancellation Penalty:                       │    - ₹     0.00  │  │
│  ├───────────────────────────────────────────────────┼──────────────────┤  │
│  │ Net Refund to Original Payment Method:            │     ₹ 23,520.00  │  │
│  └───────────────────────────────────────────────────┴──────────────────┘  │
│                                                                            │
│  Reason for Cancellation: [ Change of travel plans ▼ ]                     │
│  Additional Remarks:      [ Sudden family emergency... ]                   │
│                                                                            │
│  [ Keep Reservation ]                      [ ⚠ Confirm & Process Refund ]  │
└────────────────────────────────────────────────────────────────────────────┘
```

---

### 4.3 Host Onboarding Lead Qualification Pipeline
Dedicated triage pipeline for Operations (`/Employee/OnboardingLeads`) and Administrators (`/Admin/OnboardingLeads`).

```
┌────────────────────────────────────────────────────────────────────────────┐
│ Inbound Host Leads Pipeline (4 Pending Triage)                             │
├────────────────────────────────────────────────────────────────────────────┤
│ Search: [ Filter by name, phone, city... ]          Status: [ All Leads ▼ ]│
├─────────┬──────────────────────┬──────────┬──────────────┬────────┬────────┤
│ Lead ID │ Host Name & Phone    │ Property │ Location     │ Status │ Action │
├─────────┼──────────────────────┼──────────┼──────────────┼────────┼────────┤
│ #L-204  │ Vikram Singhania     │ Skyview  │ Karjat, MH   │ [New]  │[Review]│
│         │ +91 98201 12345      │ 5 Acres  │              │        │[Approve│
├─────────┼──────────────────────┼──────────┼──────────────┼────────┼────────┤
│ #L-203  │ Ananya Deshmukh      │ Riverside│ Alibaug, MH  │ [Cont] │[Approve│
│         │ +91 97654 98765      │ 3.5 Acre │              │        │[Reject]│
└─────────┴──────────────────────┴──────────┴──────────────┴────────┴────────┘
```

---

### 4.4 Dynamic Admin Analytics Dashboard with Excel & CSV Export
Executive dashboard displaying real-time financial metrics with multi-dimensional filtering.

```
┌────────────────────────────────────────────────────────────────────────────┐
│ Master Platform Performance & Financial Analytics                          │
│ Timeframe: [ Last 30 Days ▼ ]   Property: [ All Properties ▼ ] [ Apply ]    │
│ [ 📊 Export to Excel (.xlsx) ]   [ 📄 Export CSV ]                          │
├────────────────────────────────────────────────────────────────────────────┤
│ ┌───────────────┐ ┌───────────────┐ ┌───────────────┐ ┌──────────────────┐ │
│ │ Property GMV  │ │ Event GMV     │ │ Net Comm. (Cut│ │ Pending Payouts  │ │
│ │ ₹ 18,40,000   │ │ ₹ 3,85,000    │ │ ₹ 2,67,000    │ │ ₹ 19,58,000      │ │
│ │ (+14% stays)  │ │ (24 setups)   │ │ (12% avg fee) │ │ (Disbursed)      │ │
│ └───────────────┘ └───────────────┘ └───────────────┘ └──────────────────┘ │
└────────────────────────────────────────────────────────────────────────────┘
```

---

### 4.5 24-Hour Damage Claim Adjudication Panel
Provides hosts with an evidence submission portal and gives staff an interactive review workbench.

```
┌────────────────────────────────────────────────────────────────────────────┐
│ Damage Claim Review — Booking #1084                                        │
│ Claim Status: [ Pending Staff Adjudication ]    Deadline: [ 18h remaining] │
├────────────────────────────────────────────────────────────────────────────┤
│ Host: Rajesh Sharma (Green Meadows Villa)                                  │
│ Guest: Amit Verma (Aadhaar KYC Verified)                                   │
│ Security Deposit Pre-Authorized: ₹10,000.00                                │
│ Amount Claimed by Host:          ₹4,500.00                                 │
│                                                                            │
│ Itemized Cost Breakdown:                                                   │
│ - Poolside patio glass table replacement: ₹3,500.00                        │
│ - Lawn deep cleaning & debris removal:    ₹1,000.00                        │
│                                                                            │
│ Photographic Evidence (2 items):                                           │
│ [📷 Shattered_Table.jpg (2.8MB)]    [📷 Lawn_Debris.jpg (1.9MB)]           │
│                                                                            │
│ Staff Adjudication Actions:                                                │
│ Adjudicated Payout Amount: [ ₹ 4500.00 ]                                   │
│ Internal Notes: [ Receipts verified with local vendor invoice #8841 ]      │
│                                                                            │
│ [Reject Claim & Release All]              [Approve Deduction & Release Rem]│
└────────────────────────────────────────────────────────────────────────────┘
```

---

### 4.6 Event Décor Package Card & Proof-of-Setup Submission Modal
Allows Event Managers and Decorators to inspect assigned setups and upload photo/video proofs before marking a setup completed.

```
┌────────────────────────────────────────────────────────────────────────────┐
│ Assigned Event Setup — Booking #BMF-2091                                   │
│ Package: Regal Wedding & Anniversary Theme (₹45,000)                       │
├────────────────────────────────────────────────────────────────────────────┤
│ Property: Whispering Pines Villa (Lonavala)                                │
│ Client: Priya Sharma (+91 98210 44321)                                     │
│ Event Date: 28 Oct 2026 (Check-In: 11:00 AM)                               │
│ Current Status: [ InProgress ▼ ]                                           │
│                                                                            │
│ Client Theme Notes:                                                        │
│ "Warm fairy lighting across poolside gazebo, pastel pink rose arch at     │
│ entrance, and gold sequin stage backdrop."                                 │
│                                                                            │
│ Photographic & Video Completion Proofs:                                    │
│ Enter URLs or Upload Files:                                                │
│ [ https://images.unsplash.com/gazebo-decor-proof.jpg                     ] │
│ [ https://youtu.be/gazebo-walkthrough-proof                              ] │
│                                                                            │
│ Setup Lifecycle Milestone:                                                 │
│ ( ) Assigned   ( ) In Progress   (●) Completed & Ready for Inspection      │
│                                                                            │
│ [ Cancel ]                               [ 💾 Submit Proof & Update Status ]│
└────────────────────────────────────────────────────────────────────────────┘
```

---

### 4.7 Checkout Décor Add-on Switch & Live Total Recalculation
Enables clients to add event decoration packages directly at the booking checkout stage.

```
┌────────────────────────────────────────────────────────────────────────────┐
│ [✓] Add Event Décor & Celebration Package                                  │
│ Hosting a birthday, anniversary, or wedding? Add a managed package.        │
├────────────────────────────────────────────────────────────────────────────┤
│ Select Celebration Package:                                                │
│ [ Regal Wedding & Floral Gazebo Theme (Wedding) — ₹45,000                ▼ ]│
│                                                                            │
│ Special Instructions for Decorator:                                        │
│ [ Gold balloons, neon 'Happy 25th Anniversary' sign on stage...          ] │
│                                                                            │
│ Estimated Total Payable:                                                   │
│ Base Stay (₹24,000) + Décor (₹45,000) = ₹69,000.00                        │
└────────────────────────────────────────────────────────────────────────────┘
```

---

## 5. UI Accessibility & WCAG 2.1 AA Standards

1. **Color Contrast:** All body text meets or exceeds a `4.5:1` contrast ratio against both light (`#f8fafc`) and dark (`#0f172a`) surfaces.
2. **Focus Visibility:** Interactive elements receive a high-visibility 2px focus ring with an offset:
   ```css
   :focus-visible {
     outline: 2px solid var(--primary-blue);
     outline-offset: 2px;
   }
   ```
3. **Screen Reader Semantic Landmarks:** Use of `<header>`, `<nav>`, `<main>`, `<section>`, and `<footer>` elements with explicit `aria-label` tags for icon-only buttons.
4. **Form Labels:** All `<input>`, `<select>`, and `<textarea>` elements are programmatically paired with `<label for="...">` tags.
5. **No Motion Sickness:** Media queries support `prefers-reduced-motion: reduce`, disabling smooth transitions and pulsing animations for sensitive users.

---

## 6. Comprehensive Error & Empty Fallback States

1. **404 Resource Not Found:** Clean graphic state with link back to explore catalog or dashboard.
2. **403 Forbidden Access:** Displays role requirement pill ("Requires EventManager or Admin permissions") with zero stack disclosure.
3. **500 Server Error:** Friendly assistance page with a unique Correlation ID for customer support lookup.
4. **Empty Dashboard Queues:** Engaging illustrated empty states ("No pending damage claims to adjudicate", "All decorator setups currently fulfilled").
