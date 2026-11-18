# BookMyFun Changelog

All notable changes, architectural milestones, and feature releases for the BookMyFun platform are documented in this file.

---

## [v2.4.0] - November 2026

### Added
- **Stay & Celebrate Checkout Workflow**:
  - Two-option toggle on property checkout between "Standard Stay" and "Stay & Celebrate".
  - Dedicated eligibility endpoint (`GET /Booking/CelebrationEligibility`) evaluating:
    - 48-hour advance notice in Asia/Kolkata timezone (`India Standard Time`, UTC+05:30).
    - Service area coverage matching property city and state with active, verified event managers.
  - Occasion chips filter (`Birthday Party`, `Anniversary`, `Wedding / Reception`, `Corporate / Offsite`, `Custom Gathering`).
  - Décor manager cards with ratings, completed events, and sort toggles ("Top rated" and "Lowest price").
  - "You might also like" fallback displaying 3 to 7 alternative packages when an occasion has no local matches.
  - Single flat décor fee pricing structure per reservation.
- **Automated Contact Privacy Scanner**:
  - Regular expression engine intercepting phone numbers, email addresses, external URLs, and solicitation keywords across public text fields.
  - Replacement with `[Contact Details Masked]` token.
  - Centralized audit tracking in `FlaggedContactAttempts` table.
- **Listing Lifecycle & Subscription Worker**:
  - Background worker (`SubscriptionLifecycleBackgroundService`) running hourly.
  - 60-day complimentary host trial on initial onboarding.
  - Automated listing unpublish upon subscription expiry with zero grace period.
  - 3-day advance trial and subscription expiry warnings via WhatsApp, Email, and In-App notifications.
  - Automatic restoration of unpublished listings upon plan renewal.
- **Multi-Party Notification Dispatcher**:
  - Event-driven notifications across In-App, Email, and WhatsApp channels.
  - Strict WhatsApp opt-in and verification validation (`NotificationWhatsAppEnabled && WhatsAppVerified`).
  - Atomic deduplication keys (`{eventType}:{entityId}:{recipientUserId}:{channel}`) recorded in `NotificationDispatches`.
- **Role-Based Navigation Framework**:
  - Unified responsive dashboard layout supporting header dropdowns, collapsible left sidebar, tablet icon rail, and mobile drawer.
  - Strict role isolation: each role sees only their respective dashboard and navigation tree.
- **Versioned Legal Policy Compliance**:
  - Centralized legal document storage in `LegalDocuments` table.
  - Mandatory consent tracking in `PolicyAcceptances` capturing user IP address, document version, and submission page URL.

### Changed
- Refactored `BookingController.cs` and `PropertiesController.cs` into decoupled, single-responsibility controllers.
- Updated Razorpay payment verification with cryptographic HMAC SHA256 validation and idempotent webhook processing.
- Updated database status constraints on `Bookings.Status` to support `PaymentFailed`.
- Enhanced `web.config` with WebP and AVIF MIME type mappings and IIS in-process hosting configuration.

### Deprecated
- Removed legacy test OTP bypass in production environments; all OTP verifications strictly require genuine delivery.
- Deprecated legacy `PropertyImages` table in favor of optimized `PropertyMedia` records.
