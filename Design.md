# BookMyFun — UI/UX Design & Component Specifications

## 1. Design System & Theming Tokens
BookMyFun uses a modern, uncluttered design system built on **Plus Jakarta Sans**, high-contrast typography, generous whitespace (1.5rem to 3rem padding), soft border radii (12px to 24px), and hand-crafted CSS custom properties supporting seamless light and dark mode transitions without FOUC (Flash of Unstyled Content).

### Color Tokens & Contrast Ratios
- **Surfaces:**
  - Light: `--bg-body: #f8fafc; --bg-card: #ffffff; --bg-table-head: #f1f5f9;`
  - Dark: `--bg-body: #0f172a; --bg-card: #1e293b; --bg-table-head: #0f172a;`
- **Brand Accents:**
  - Primary Indigo/Blue: `#2563eb` (Hover: `#1d4ed8`)
  - Warm Honey Amber: `#f59e0b` (Hover: `#d97706`)
  - Success Emerald: `#10b981` (Hover: `#059669`)
  - Danger Rose: `#ef4444` (Hover: `#dc2626`)
- **Card Aesthetics:**
  - Subtle borders: `1px solid var(--card-border)`
  - Elevated shadows: `0 10px 25px -5px rgba(0,0,0,0.05)` (Light), `0 10px 25px -5px rgba(0,0,0,0.4)` (Dark)
  - No loud saturated Bootstrap cards; all stat cards use soft pastel icon containers with crisp numeric typography.

---

## 2. Key Interactive Component Layouts

### 2.1 Dual File/URL Media Uploader
The dual uploader supports both high-resolution file uploads and direct external links (YouTube, Vimeo, Cloudinary, AWS S3) with an interactive preview grid.

```
┌────────────────────────────────────────────────────────────────────────┐
│  Property Media & Gallery Manager                                      │
│  Minimum 5 photos required. Videos optional.                           │
│                                                                        │
│  [📷 Add Photos]   [🎥 Add Video]                                     │
│  ────────────────────────────────────────────────────────────────────  │
│  Mode Selector: (●) Direct File Upload     (○) External Web Link / URL │
│                                                                        │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │   ⬆ Drag & Drop high-res photos here or Browse                   │  │
│  │   Supports JPG, PNG, WEBP up to 10MB each (Batch selection ok)   │  │
│  │   [Choose Files]                                                 │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│  OR: Enter External URL: [ https://images.unsplash.com/photo-...    ]  │
│      [+ Add URL to Gallery]                                            │
│                                                                        │
│  Gallery Items (5 items):                                              │
│  ┌───────────────┐ ┌───────────────┐ ┌───────────────┐ ┌─────────────┐ │
│  │ [Image Prev]  │ │ [Image Prev]  │ │ [Image Prev]  │ │ [YT Video]  │ │
│  │ ★ Primary     │ │ (File: 2.4MB) │ │ (URL: Ext)    │ │ (Embedded)  │ │
│  │ [Make Primary]│ │ [Make Primary]│ │ [Make Primary]│ │ [Play Prev] │ │
│  │ [🗑 Delete]    │ │ [🗑 Delete]    │ │ [🗑 Delete]    │ │ [🗑 Delete] │ │
│  └───────────────┘ └───────────────┘ └───────────────┘ └─────────────┘ │
└────────────────────────────────────────────────────────────────────────┘
```

#### States & Micro-interactions:
- **Idle / Dragover:** Drop zone pulses with a soft blue ring and subtle background tint (`rgba(37,99,235,0.06)`).
- **Client Validation:** Verifies file size (≤10MB images, ≤100MB videos) and extension before upload begins.
- **Upload Progress:** Shows individual item progress bars and transfer speed.
- **Fallback Switcher:** If direct file upload fails due to network drop or size limit, an inline alert displays: *"Upload failed. Would you like to provide an external link instead?"* with a one-click button to auto-fill the URL input.

---

### 2.2 Client Cancellation & Refund Transparency Modal
Prevents customer confusion by displaying exact monetary breakdowns before cancellation execution.

```
┌───────────────────────────────────────────────────────────┐
│ Cancel Booking #1084                                 [✕]  │
├───────────────────────────────────────────────────────────┤
│ Property: Mountain Crest Villa, Lonavala                  │
│ Dates: Dec 20, 2026 - Dec 22, 2026 (Check-in in 72 hours) │
│ Total Amount Paid:                           ₹25,000.00   │
│                                                           │
│ ┌───────────────────────────────────────────────────────┐ │
│ │ ℹ Policy: Free cancellation until 48 hrs before stay │ │
│ │ Status: Eligible for Full Refund (less gateway fee)   │ │
│ └───────────────────────────────────────────────────────┘ │
│                                                           │
│ Refund Breakdown:                                         │
│   Gross Booking Total:                      ₹25,000.00    │
│   Cancellation Penalty:                     -    ₹0.00    │
│   Payment Gateway & Processing Fee (2%):    -  ₹500.00    │
│   ─────────────────────────────────────────────────────   │
│   Estimated Refund Amount:                  ₹24,500.00    │
│                                                           │
│ Reason for Cancellation:                                  │
│ [ Change of travel plans / Family emergency           ▼ ] │
│                                                           │
│ [Keep My Booking]             [Confirm & Request Refund]  │
└───────────────────────────────────────────────────────────┘
```

---

### 2.3 Google Maps Interactive Location Picker
Enables precise geographical pin selection during property listing and gives guests an intuitive map view during exploration.

- **Listing Setup Flow:**
  - Search input with Google Places Autocomplete: User types "Tungarli, Lonavala".
  - Pin automatically snaps to the selected coordinates; user can fine-tune by dragging the pin.
  - Readouts for `Latitude`, `Longitude`, and `FormattedAddress` update in real-time in hidden form fields.
  - Fallback State: If Google Maps API fails to load (missing key, quota exceeded, or client offline), an accessible message displays: *"Map services currently unavailable. Please enter landmark and address manually."* Allows smooth form completion without hard-blocking submissions.

---

### 2.4 Admin Filterable Dynamic Analytics Grid
Empowers Superusers with real-time financial metrics, date-range presets, and granular filters.

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ Platform Analytics & Financial Metrics                                                 │
│ Presets: [Today] [Last 7D] [Month-To-Date] [Year-To-Date] [📅 Custom Date Range]       │
│ Filters: Property: [All Properties ▼]  Owner: [All Owners ▼]  Status: [Confirmed ▼]    │
│ [📊 Apply Filters]                                      [📥 Export Excel] [📄 CSV]     │
├────────────────────────────────────────────────────────────────────────────────────────┤
│ ┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐    │
│ │ Gross Merchandise│ │ Platform Cut(10%)│ │ Pending Payouts  │ │ Active Properties│    │
│ │ ₹1,480,000.00    │ │ ₹148,000.00      │ │ ₹42,500.00       │ │ 64               │    │
│ └──────────────────┘ └──────────────────┘ └──────────────────┘ └──────────────────┘    │
│                                                                                        │
│ Historical Period Breakdown:                                                           │
│ Period        Bookings   Gross Revenue   Platform Earnings   Host Payouts   Status     │
│ 2026-W41      18         ₹220,000.00     ₹22,000.00          ₹198,000.00    Settled    │
│ 2026-W42      24         ₹310,000.00     ₹31,000.00          ₹279,000.00    Settled    │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Error Fallback & Resilience UI Patterns

1. **Global Error Boundaries (`Views/Shared/Error.cshtml`):**
   - Clean illustration, human-understandable message, and distinct action buttons: *"Return to Home"* or *"Retry Operation"*.
   - Never exposes raw database exceptions or stack traces to end users.
2. **Accessible Toast Notifications:**
   - Positioned in bottom-right (`#toastContainer`).
   - Four distinct semantic states: Success (Emerald), Info (Blue), Warning (Amber), Error (Rose).
   - Dismissible with keyboard (`Escape`) and screen-reader compliant (`aria-live="polite"`).
3. **Inline Form Validation:**
   - Erroneous fields receive a subtle red border glow (`box-shadow: 0 0 0 3px rgba(239,68,68,0.15)`) with an icon and clear corrective instruction below the input.
