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

## 3. Global Layout Architecture & Navigation

### 3.1 Responsive Navigation Bar
- **Brand Identity:** High-contrast logo with an organic leaf/farm icon and bold modern typography.
- **Role-Aware Navigation:**
  - **Anonymous:** Explore Catalog, List Your Farm, Contact Us, Login / Sign Up.
  - **Client:** Explore, My Bookings, Saved Villas, Account Profile.
  - **Owner:** Listings Portfolio, Booking Calendar, Financial Payouts, Add Farm.
  - **Employee:** Onboarding Leads Pipeline, ID Verifications, Refunds Queue, Damage Claims.
  - **Admin:** Master Analytics, Financial Ledger, Onboarding Leads, Refunds, Users, Site Config.
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

### 4.2 Client Cancellation & Refund Transparency Modal
Eliminates customer anxiety by providing a real-time monetary calculation before the user confirms a cancellation.

```
┌──────────────────────────────────────────────────────────────────┐
│ Cancel Booking #1084                                        [✕]  │
├──────────────────────────────────────────────────────────────────┤
│ Property: Whispering Palms Farmhouse, Alibaug                    │
│ Reserved Dates: Nov 14, 2026 - Nov 16, 2026 (In 76 hours)       │
│ Total Paid:                                          ₹32,000.00  │
│                                                                  │
│ ┌──────────────────────────────────────────────────────────────┐ │
│ │ ℹ Active Policy: Free cancellation until 48 hours before stay│ │
│ │ Status: Eligible for Full Refund (less gateway charge)       │ │
│ └──────────────────────────────────────────────────────────────┘ │
│                                                                  │
│ Estimated Refund Calculation:                                    │
│   Gross Reservation Amount:                         ₹32,000.00   │
│   Cancellation Penalty Deducted:                   -     ₹0.00   │
│   Payment Gateway & Processing Fee (2%):           -   ₹640.00   │
│   ────────────────────────────────────────────────────────────   │
│   Net Refund Amount (Direct to Original Method):    ₹31,360.00   │
│                                                                  │
│ Reason for Cancellation (Optional):                              │
│ [ Change of personal schedule / Flight rescheduled          ▼ ]  │
│                                                                  │
│ [Keep Reservation]                  [Confirm & Issue Refund]     │
└──────────────────────────────────────────────────────────────────┘
```

---

### 4.3 Google Maps Interactive Geolocation Picker
Ensures accurate latitude/longitude coordinates and prevents address confusion.

```
┌────────────────────────────────────────────────────────────────────────────┐
│ Property Location & Geolocation Pin                                        │
│                                                                            │
│ Search Address: [ Tungarli Lake Road, Lonavala, Maharashtra          🔍 ]  │
│                                                                            │
│ ┌────────────────────────────────────────────────────────────────────────┐ │
│ │                                                  [+] [-]  [Satellite]  │ │
│ │                        📍 (Drag pin to adjust)                         │ │
│ │                                                                        │ │
│ │                               Map Canvas                               │ │
│ └────────────────────────────────────────────────────────────────────────┘ │
│ Formatted Address: Tungarli Lake Rd, Gold Valley, Lonavala, MH 410401      │
│ Latitude: 18.761234  |  Longitude: 73.415678 (Auto-populated)             │
└────────────────────────────────────────────────────────────────────────────┘
```

---

### 4.4 Dynamic Multi-Dimensional Admin Analytics Dashboard
Delivers real-time business intelligence with flexible time horizons and ClosedXML Excel / CSV streaming exports.

```
┌────────────────────────────────────────────────────────────────────────────┐
│ Financial Analytics & Executive Reporting                                  │
│                                                                            │
│ Time Presets: [ Today ] [ 7 Days ] [ Month to Date ] [ Year to Date ]      │
│ Custom Range: [ 2026-10-01 ] to [ 2026-10-15 ]             [ Apply Filter ]│
│                                                                            │
│ Export Actions:  [📗 Download Excel (.xlsx)]   [📄 Export CSV Data]        │
├────────────────────────────────────────────────────────────────────────────┤
│ ┌───────────────┐ ┌───────────────┐ ┌───────────────┐ ┌──────────────────┐ │
│ │ Gross Revenue │ │ Net Commission│ │ Host Payouts  │ │ Occupancy Rate   │ │
│ │  ₹14,85,000   │ │   ₹1,78,200   │ │  ₹13,06,800   │ │      78.4%       │ │
│ │ (+14% vs prev)│ │ (12% avg fee) │ │ (Disbursed)   │ │ (32 active farms)│ │
│ └───────────────┘ └───────────────┘ └───────────────┘ └──────────────────┘ │
│                                                                            │
│ Revenue & Occupancy Trends (Monthly / Quarterly Performance):              │
│ [ Bar / Line Chart Visualization Rendered via Chart.js / Canvas ]          │
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
