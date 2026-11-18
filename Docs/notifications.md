# Notifications and Dispatch Architecture

## 1. Overview
BookMyFun features an event-driven notification dispatch pipeline managed by [`NotificationDispatcherService.cs`](file:///d:/Projects/BookMyFun/Services/Notifications/NotificationDispatcherService.cs). Outbound messages inform travelers, hosts, decorators, and operations staff of critical status changes.

Supported delivery channels:
- **In-App**: Notification bell drawer backed by [`UserNotifications`](file:///d:/Projects/BookMyFun/Docs/database.md#usernotifications) table.
- **Email**: Asynchronous queue backed by [`EmailQueueService.cs`](file:///d:/Projects/BookMyFun/Services/EmailQueueService.cs).
- **WhatsApp**: Direct transactional messaging powered by [`IWhatsAppService.cs`](file:///d:/Projects/BookMyFun/Services/CommunicationService.cs).

---

## 2. WhatsApp Opt-In and Verification Rule

WhatsApp messages deliver booking confirmations, gate access codes, and time-critical reminders. To prevent spam and comply with telecommunication regulations, dispatches to WhatsApp enforce a strict two-factor check:

```csharp
// Source: Services/Notifications/NotificationDispatcherService.cs line 533
if (user.NotificationWhatsAppEnabled && user.WhatsAppVerified)
{
    string? phone = user.WhatsAppPhone ?? user.Phone;
    // Deliver message
}
```

1. **Opt-In Flag**: `user.NotificationWhatsAppEnabled == true` (managed by user in `/Account/Profile` or `/Owner/NotificationSettings`).
2. **Verification State**: `user.WhatsAppVerified == true` (confirmed via mobile OTP validation).
3. **Target Number**: Uses `user.WhatsAppPhone` if set; otherwise falls back to primary `user.Phone`.

If either flag is false, the WhatsApp delivery is skipped silently without breaking the overall notification flow.

---

## 3. Deduplication Key Format

To prevent duplicate alerts when payment webhooks retry or users refresh return URLs, every dispatch is checked against the [`NotificationDispatches`](file:///d:/Projects/BookMyFun/Docs/database.md#notificationdispatches) ledger using an atomic deduplication key:

```text
{eventType}:{entityId}:{recipientUserId}:{channel}
```

### 3.1 Examples
- `BookingConfirmed:1042:58:InApp`
- `BookingConfirmed:1042:58:Email`
- `BookingConfirmed:1042:58:WhatsApp`
- `CheckInReminder:1042:58:WhatsApp`
- `SubscriptionExpired:0:12:Email`

### 3.2 Idempotent Execution
Before delivering a message through any channel, the dispatcher executes:
```csharp
bool isNewDispatch = await _userNotificationRepo.TryRecordDispatchAsync(key);
if (!isNewDispatch)
{
    _logger.LogInformation("Skipping duplicate notification dispatch: {Key}", key);
    return;
}
```
If the key already exists in `NotificationDispatches`, the send operation is aborted.

---

## 4. Notifications Event Catalog

| Event Name | Trigger Condition | Recipients | Delivery Channels |
|:---|:---|:---|:---|
| `BookingConfirmed` | Payment successfully verified | **Client**<br/>**Owner**<br/>**EventManager** (if décor booked)<br/>**Operations Staff** | Client: InApp, Email, WhatsApp<br/>Owner: InApp, Email, WhatsApp<br/>Decorator: InApp, Email, WhatsApp<br/>Staff: Email, InApp |
| `BookingCancelled` | Booking cancelled by guest or staff | **Client**<br/>**Owner**<br/>**EventManager** (if assigned) | InApp, Email, WhatsApp |
| `RefundProcessed` | Staff completes cancellation refund | **Client** | InApp, Email, WhatsApp |
| `EventStatusChanged` | Decorator updates setup progress | **EventManager**<br/>**Client** | InApp, Email, WhatsApp (Decorator)<br/>InApp (Client) |
| `CheckInReminder` | Daily background cron for tomorrow's stays | **Client**<br/>**Owner** | Client: InApp, Email, WhatsApp<br/>Owner: InApp, Email |
| `ReceiptAvailable` | Payment confirmation generated | **Client** | InApp, Email |
| `PropertyApproved` | Moderator approves property listing | **Owner** | InApp, Email, WhatsApp |
| `PropertyRejected` | Moderator rejects listing with reason | **Owner** | InApp, Email, WhatsApp |
| `PropertyPublished` | Host publishes listing live | **Owner** | InApp, Email |
| `PropertyUnpublished` | Host pauses listing or system unpublishes | **Owner** | InApp, Email |
| `TrialExpiringWarning` | 3 days before 60-day trial expires | **Owner** | InApp, Email, WhatsApp |
| `SubscriptionExpiringWarning` | 3 days before paid plan ends | **Owner** | InApp, Email, WhatsApp |
| `SubscriptionExpired` | Subscription end date reached | **Owner** | InApp, Email, WhatsApp |
| `SubscriptionActivated` | Plan renewed or scheduled upgrade starts | **Owner** | InApp, Email, WhatsApp |
| `PayoutDispatched` | Host/decorator earnings sent | **Owner** / **EventManager** | InApp, Email, WhatsApp |
| `DamageClaimUpdate` | Host files claim or status changes | **Owner**<br/>**Client** | InApp, Email |
| `PartnerLeadSubmitted` | New host or event manager application | **Operations Staff** | Email, InApp |
| `ClientDocumentSubmitted` | Guest uploads ID document (KYC) | **Operations Staff** | InApp, Email |
