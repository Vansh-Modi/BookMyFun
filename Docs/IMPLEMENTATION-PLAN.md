# BookMyFun Implementation Plan

## Phase 0: Inspect and Precheck
- **Status:** Completed
- **Target Framework:** `net10.0`
- **Precheck Database:** `BookMyFun` (read from `ConnectionStrings:DefaultConnection` via User Secrets)
- **Files Touched/Added:**
  - `docs/IMPLEMENTATION-PLAN.md` (Plan documentation)

## Phase 1: Console Error and Gallery
- **Goal:** Fix JavaScript parsing failure on Property Details, enforce System.Text.Json passing of server values, unify gallery slides (images and videos) with single source of truth (`PropertyMedia`), support navigation/swipe/lazy loading, 404 handling, and secure media uploads.
- **Files to Modify/Add:**
  - `Views/Properties/Details.cshtml` (Fix unclosed function syntax error, remove raw Razor JS concatenation, unify image and video slides into Flipkart-style carousel, add play badges, counter, lazy loading, and touch swipe)
  - `Repositories/PropertyRepository.cs` (Filter out placeholder paths in query, read `PropertyMedia` ordered by `SortOrder`, then `MediaID`, ignore legacy `PropertyImages`, select needed columns, limit 30 items)
  - `Controllers/PropertiesController.cs` (Pass JSON-serialized data attributes or safe models)
  - `Services/MediaStorageService.cs` (Enforce file signature/magic bytes check for mp4, webm, jpg, png, webp; GUID file naming; size limits; store under `wwwroot/uploads/media`)
  - `Program.cs` (Ensure `wwwroot/uploads/media` is created and served statically)

## Phase 2: One Rule for Stay & Celebration
- **Goal:** Single source of truth for Stay & Celebrate eligibility across Checkout page, JSON endpoint, and booking submission.
- **Files to Modify/Add:**
  - `Services/Checkout/IStayCelebrationEligibilityService.cs` (New interface for eligibility and area matching)
  - `Services/Checkout/StayCelebrationEligibilityService.cs` (New implementation: check-in > 48h in Asia/Kolkata, property live, decorator meets verified, active, active package in category, approved portfolio row `IsApproved = 1`, normalized "City, State" area matching, 5-minute memory caching with invalidation)
  - `Controllers/BookingController.cs` (Adopt `StayCelebrationEligibilityService` in GET Checkout, JSON endpoint, and POST ConfirmBooking; remove old "occasion is not available" error; enforce server-authoritative flat package pricing; handle switch to standard stay on date change)
  - `Views/Booking/Checkout.cshtml` (Align with `stay-and-celebrate-mockup-v2.html`, show Stay & Celebrate popup menu only when selected, render top rated vs lowest price sorting, switch warning message on lead time invalidation, hide contact details)
  - `Repositories/EventPackageRepository.cs` and `Repositories/IEventPackageRepository.cs` (Support portfolio approval check and area query)
  - `Program.cs` (Register `IStayCelebrationEligibilityService`)

## Phase 3: Event Manager Review (Employee and Admin)
- **Goal:** Comprehensive back-office review workflow for Event Managers with status tabs, checklist warnings, secure document streaming, and audited actions.
- **Files to Modify/Add:**
  - `Controllers/EmployeeController.cs` (Add Event Managers list with tabs: Pending, Verified, Rejected, Suspended; detail view; actions: Verify, Reject with reason, Return to Pending, Suspend, Reactivate, Edit service areas with "City, State" normalization, Approve/Reject portfolio item with reason; secure document stream endpoint; ensure 403 on commission and site settings)
  - `Controllers/AdminController.cs` (Maintain full administrative rights, site settings, commission rates, staff accounts)
  - `Views/Employee/EventManagers.cshtml` (New view: Event managers list with tabs and pending badge count)
  - `Views/Employee/EventManagerDetail.cshtml` (New view: Profile, service area editor, uploaded documents, packages, portfolio, checklist warning)
  - `Views/Shared/_DashboardSidebarNav.cshtml` (Add "Event Managers" item with pending count badge)
  - `Repositories/UserRepository.cs` and `Repositories/IUserRepository.cs` (Status queries, audit logging, area normalization)
  - `Repositories/EventPackageRepository.cs` (Portfolio and package moderation helpers)

## Phase 4: Notifications
- **Goal:** Reliable transactional outbox, deduplication, retry engine, hourly check-in reminder, and comprehensive notification matrix across In-App, Email, and WhatsApp.
- **Files to Modify/Add:**
  - `Models/Entities/NotificationOutbox.cs` (New entity for `NotificationOutbox`)
  - `Repositories/INotificationOutboxRepository.cs` & `Repositories/NotificationOutboxRepository.cs` (New repository for inserting outbox rows and updating retry attempts/status in same transaction as business events)
  - `Services/Notifications/INotificationDispatcherService.cs` & `Services/Notifications/NotificationDispatcherService.cs` (Align with 26-row notification matrix, WEA/EA/A rules, WhatsApp opt-in & template enforcement, deduplication key `{EventType}:{EntityId}:{UserId}:{Channel}`)
  - `Services/Notifications/NotificationOutboxBackgroundService.cs` (New hosted service polling `NotificationOutbox` with up to 3 retries and exponential backoff)
  - `Services/Notifications/CheckInReminderBackgroundService.cs` (Hourly hosted service sending once per booking the day before check-in)
  - `Controllers/NotificationsController.cs` (Endpoints for unread count, list, mark all as read)
  - `Views/Shared/_Layout.cshtml` (Bell notification dropdown with unread badge, list, and "Mark all as read" button)
  - `Program.cs` (Register outbox repository, background worker, and options)

## Phase 5: Staff Queue and Data Health Check
- **Goal:** Unified "Needs Attention" queue for staff and read-only Data Health Check for Admin.
- **Files to Modify/Add:**
  - `Controllers/EmployeeController.cs` & `Controllers/AdminController.cs` ("Needs Attention" queue endpoint with pagination 25/page)
  - `Views/Employee/NeedsAttention.cshtml` (Unified queue listing pending manager verifications, portfolio items, property reviews, client ID checks, partner requests, refunds, and damage claims)
  - `Services/Health/IDataHealthCheckService.cs` & `Services/Health/DataHealthCheckService.cs` (Read-only diagnostics: missing media files/placeholders, live properties with no décor coverage, verified managers without active package/approved portfolio, un-normalizable service areas, expired subscriptions, failed outbox rows)
  - `Views/Admin/DataHealthCheck.cshtml` (Admin view rendering diagnostics with direct record links)
  - `Views/Shared/_DashboardSidebarNav.cshtml` (Add Needs Attention and Data Health Check sidebar items)
  - `Program.cs` (Register health check service)

## Phase 6: Schema (Numbered Migrations, No DDL at Runtime)
- **Goal:** Idempotent, numbered migration scripts under `/db/migrations`, `dbo.SchemaVersion` tracking, standalone `tools/DbMigrate` runner, and startup version comparison with feature degradation (no DDL at runtime).
- **Files to Modify/Add:**
  - `db/migrations/001_baseline_check.sql` (Verifies core tables exist)
  - `db/migrations/002_user_notifications.sql` (Creates `UserNotifications` if missing with indexes)
  - `db/migrations/003_copy_legacy_property_images.sql` (Copies unmigrated `PropertyImages` into `PropertyMedia`)
  - `db/migrations/004_notification_outbox.sql` (Creates `NotificationOutbox` and `NotificationDispatches` if missing with indexes)
  - `db/migrations/005_query_indexes.sql` (Creates performance indexes idempotently)
  - `db/migrations/006_confirm_media_type.sql` (Validates `MediaType` columns)
  - `tools/DbMigrate/DbMigrate.csproj` & `tools/DbMigrate/Program.cs` (Console CLI runner reading connection string from env var or user secrets)
  - `Program.cs` (Remove runtime `DatabaseInitializer` DDL, compare current `SchemaVersion`, warn and gracefully disable unmigrated features)

## Phase 7: Security and Performance
- **Goal:** Anti-forgery tokens on all POSTs, strict role authorization policies, upload magic-bytes checks, input sanitization, rate limiting, and query optimizations.
- **Files to Modify/Add:**
  - `Services/Security/RateLimitingMiddleware.cs` (Add endpoint-specific limits for eligibility and notifications)
  - `Services/MediaStorageService.cs` (Magic bytes validation and static file hosting)
  - `Controllers/BookingController.cs`, `Controllers/EmployeeController.cs`, `Controllers/AdminController.cs` (Anti-forgery and 403 checks)

## Phase 8: Verify
- **Goal:** Validate compilation, test suite execution, local scenario verification, and reporting.

## Phase 9: Publish Zip
- **Goal:** Generate MonsterASP deployment zip (`BookMyFun-monsterasp-YYYYMMDD-HHmm.zip`) with forward-slash entries, clean configuration, no secrets, no forbidden files, and SHA-256 hash file.
