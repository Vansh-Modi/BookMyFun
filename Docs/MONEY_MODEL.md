# Nayesta Settlement & Money Model

> **Document Status:** DRAFT FOR REVIEW — **NOT APPROVED.**  
> This document describes the proposed money-flow model for **Nayesta** (BookMyFun, Surat, Gujarat) and compares it against the current codebase. No code, configuration, or database changes have been made.  
> **Approval Rule:** This model is not approved until **all three** sign-off blocks in Section 9 (Owner, Lawyer, and Chartered Accountant) are completed.

---

## Section 1. Parameters (Named, With Source)

| Symbol | Meaning | Who Sets It | Where It Lives Today |
| :--- | :--- | :--- | :--- |
| **`B`** | Base price of the property stay (computed from `PricePerNight` and booking duration/slot: FullDay, HalfDay = 55%, Hourly = 1/10th per hour) | Owner | `Properties.PricePerNight` (`Models/Entities/Entities.cs:117`); computed in `BookingPricingService.CalculatePrice` (`Services/BookingPricingService.cs:19-30`) |
| **`r`** | Platform commission rate on stay (percentage, e.g. `10%` default) | Admin or Employee | Precedence: `Properties.CommissionRate` → `Users.CustomCommissionRate` → `SiteSettings["PlatformCommissionPercent"]` (fallback `10.0m`) in `CommissionService.CalculateDynamicCommissionAsync` (`Services/CommissionService.cs:46-77, 165-173`); snapshotted on `Bookings.CommissionRateSnapshot` |
| **`g`** | Payment gateway fee rate (currently `2%`) | Admin / Config | `SiteSettings["Refund:GatewayFeePercentage"]` (fallback `2.0m`) in `RefundEngineService.GetGatewayFeePercentageAsync` (`Services/RefundEngineService.cs:629-639`) |
| **`W`** | Free cancellation window in hours before check-in | Owner or Admin | `Properties.FreeCancellationWindowHours` (default `48`), snapshotted to `Bookings.FreeCancellationWindowHoursSnapshot` (`Models/Entities/Entities.cs:121, 201`) |
| **`C`** | Cancellation cutoff in hours before check-in (`C ≤ W`) | Admin | `Properties.CancellationCutoffHours` (default `24`), snapshotted to `Bookings.CancellationCutoffHoursSnapshot` (`Models/Entities/Entities.cs:122, 202`) |
| **`F_b`** | Band cancellation fee (for `C ≤ hours < W`): either a percentage of `B` or a fixed amount | Admin | `Properties.CancellationFeeType` (`'percentage'` or `'fixed'`) and `Properties.CancellationFeeValue` (default `10.00m`), snapshotted to `Bookings.CancellationFeeTypeSnapshot` and `Bookings.CancellationFeeValueSnapshot` (`Models/Entities/Entities.cs:123-124, 203-204`) |
| **`E`** | Event décor package price (Stay & Celebrate) | Event Manager | `EventPackages.BasePrice` (`Controllers/BookingController.cs:1080`), snapshotted on `Bookings.EventPackagePrice` |
| **`E_c`** | Event manager platform commission on décor (percentage rate, e.g. `10%` default) | Admin / Employee | Percentage rate. Precedence: `PropertyEventCommissions` → `Properties.EventCommissionRate` → `EventPackages.CommissionRate` → `Users.CustomCommissionRate` → `SiteSettings["EventPlatformCommissionPercent"]` → `SiteSettings["PlatformCommissionPercent"]` (fallback `10.0m`) in `CommissionService.CalculateEventCommissionAsync` (`Services/CommissionService.cs:79-163`); snapshotted on `Bookings.EventCommissionRateSnapshot` |
| **`D`** | Security deposit or damage claim amount | Owner / Admin | `Properties.SecurityDepositAmount` (default `0.00m`), snapshotted to `Bookings.SecurityDepositAmount` and `Bookings.SecurityDepositStatus` (`Models/Entities/Entities.cs:118, 196-197`), managed by `DamageClaimService` (`Services/DamageClaimService.cs`). **Still exists in schema and code**, though owner-level damage policy is pending decision (Section 5). |

---

## Section 2. Guest Total

### Target Model Formula
- **Stay only:**
  $$T = B + r \cdot B = B \cdot (1 + r)$$
- **Stay plus décor:**
  $$T = B + r \cdot B + E = B \cdot (1 + r) + E$$
  *(Platform commission on décor `E_c · E` is deducted from `E`, not added on top of `E`.)*

### Does `T = B + r·B` hold today?
**No.** In the current codebase, the platform commission `r · B` is **not** added to the guest's total. Instead, the guest pays `B` (or `B + E`), and `r · B` is deducted from the owner's payout.

### Worked Example (`B = 10,000`, `r = 10%`)
- **Target Model:** `T = 10,000 + (10% × 10,000) = 10,000 + 1,000 = 11,000`.
- **Current Code (`Controllers/BookingController.cs:1084` & `Services/CommissionService.cs:74-75`):**
  - `totalAmount = propertyStayAmount + eventPackagePrice = 10,000 + 0 = 10,000`.
  - `platformCommission = 10,000 × 10% = 1,000`.
  - `ownerPayout = 10,000 − 1,000 = 9,000`.

---

## Section 3. Cancellation Outcomes

Let `h` be the hours remaining from cancellation time to check-in.

> **Gateway Fee Rule (Owner-Stated):** Each party bears `g` (currently `2%`) of the gross amount it receives.  
> **Status:** *owner-stated, lawyer and Razorpay to confirm.*

### 3(a) Free Window (`h ≥ W`)
- **Before gateway fee (`g`):**
  - **Guest refund:** `T`
  - **Owner:** `0`
  - **Event manager:** `0`
  - **Platform:** `0`
- **After gateway fee (`g` on each party's received amount):**
  - **Guest refund (net):** `T · (1 − g)` (gateway fee borne by guest: `g · T`)
  - **Owner (net):** `0`
  - **Event manager (net):** `0`
  - **Platform (net):** `0`

### 3(b) Band (`C ≤ h < W`)
Let the retained base amount on the stay be:
- If `F_b` is a percentage: `R_b = F_b · B`
- If `F_b` is a fixed amount: `R_b = min(F_b, B)`

Platform commission on `R_b` is `r · R_b`.  
Retained total is `R_total = R_b + r · R_b = R_b · (1 + r)`.

- **Before gateway fee (`g`):**
  - **Guest refund:** `T − R_total` *(for stay+décor, `E` is refunded to the guest if décor is cancelled in the band)*
  - **Owner:** `R_b`
  - **Event manager:** `0` *(décor is not retained in the band; event manager receives `0`)*
  - **Platform:** `r · R_b`
- **After gateway fee (`g` on each party's received amount):**
  - **Guest refund (net):** `(T − R_total) · (1 − g)`
  - **Owner (net):** `R_b · (1 − g)`
  - **Event manager (net):** `0`
  - **Platform (net):** `(r · R_b) · (1 − g)`

### 3(c) Under Cutoff (`h < C`)
- **Event manager rule:** The owner has stated that the **event manager receives nothing (`0`) on cancellation after the cutoff** (*owner-stated, lawyer to confirm*).
- **Stay only — before gateway fee (`g`):**
  - **Guest refund:** `0`
  - **Owner:** `B`
  - **Event manager:** `0`
  - **Platform:** `r · B`
- **Stay only — after gateway fee (`g`):**
  - **Guest refund (net):** `0`
  - **Owner (net):** `B · (1 − g)`
  - **Event manager (net):** `0`
  - **Platform (net):** `(r · B) · (1 − g)`
- **Stay plus décor — before gateway fee (`g`):**
  - **Guest refund:** `0`
  - **Event manager:** `0` (*owner-stated, lawyer to confirm*)
  - **Owner & Platform:** `B + r · B + E` total is retained (with `B` to Owner, `r · B` to Platform, and `E` allocated per lawyer-confirmed rule — shown in Section 6 with `E` retained by Owner so all rows sum to `T`, subject to lawyer confirmation).
- **Stay plus décor — after gateway fee (`g`):**
  - Each receiving party bears `g` (`2%`) of what it receives; Guest receives `0`; Event manager receives `0`.

---

## Section 4. Settlement (Normal Completed Stay)

- **Owner share release trigger:** Released after the cancellation cutoff `C` has passed (*owner-stated*).
- **Owner share release cap:** Subject to a **7-day cap** from payment/booking as the maximum hold period before release (*owner-stated*).
- **Event manager share:** `E · (1 − E_c)` is released at the **same time** as the owner's share (*owner-stated*).
- **Platform commission:** `r · B` (plus `E_c · E` when décor is booked) is recognised as platform revenue upon booking completion / settlement release (subject to CA confirmation on GST/accrual timing).
- **Custody of funds between payment and release:** Open question whether funds sit in the **platform's Razorpay/bank account** until release or in a **Razorpay Route / linked account** with delayed transfer (**LAWYER DECISION**).

---

## Section 5. Damage Claims

> **Status:** **OWNER DECISION PENDING** — Do not implement until one option is selected.

| Option | Description | Guest | Owner | Event Manager | Platform |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Option 1: Charge saved method after claim** | No upfront deposit `D` is collected. If damage occurs, owner files a claim with proof; admin/staff adjudicates; guest's saved payment method is charged `D_approved`. | Pays `0` upfront; charged `D_approved` only if claim approved | Receives `D_approved · (1 − g)` (or `D_approved` if gateway fee borne by guest) | `0` | `0` (passes through approved claim) |
| **Option 2: Damage protection fee** | Guest pays a small non-refundable protection fee `P` at checkout into a damage pool / coverage. | Pays `P` at checkout; `0` post-stay claim charge up to coverage cap | Receives adjudicated claim `D_approved` from pool/insurer | `0` | Retains `P` (net of claims & `g`) or passes to insurer |
| **Option 3: No damage money** | Platform collects no deposit `D` and processes no post-stay damage charges; owners handle disputes off-platform or via insurance. | `0` | `0` | `0` | `0` |

---

## Section 6. Worked Examples (`B = 10,000`, `r = 10%`, `g = 2%`)

All amounts in INR. Every row sums to the Guest's gross payment `T`, and `Gateway total = 2% × T`.

### 6.1 Stay Only, Free Cancellation (`T = 11,000`)
| Stage | Guest | Owner | Event manager | Platform | Gateway total |
| :--- | ---: | ---: | ---: | ---: | ---: |
| Before gateway fee | 11,000.00 | 0.00 | 0.00 | 0.00 | 0.00 |
| **After gateway fee (`2%`)** | **10,780.00** | **0.00** | **0.00** | **0.00** | **220.00** |

*Arithmetic check:* `10,780 + 0 + 0 + 0 + 220 = 11,000` (`T`). Gateway total = `2% × 11,000 = 220`.

### 6.2 Stay Only, Band Fee at 40% of B (`R_b = 4,000`, `T = 11,000`)
Retained base `R_b = 4,000`; platform commission on retained = `10% × 4,000 = 400`; retained total = `4,400`; guest gross refund = `11,000 − 4,400 = 6,600`.

| Stage | Guest | Owner | Event manager | Platform | Gateway total |
| :--- | ---: | ---: | ---: | ---: | ---: |
| Before gateway fee | 6,600.00 | 4,000.00 | 0.00 | 400.00 | 0.00 |
| **After gateway fee (`2%`)** | **6,468.00** | **3,920.00** | **0.00** | **392.00** | **220.00** |

*Arithmetic check:* `6,468 + 3,920 + 0 + 392 + 220 = 11,000` (`T`). Gateway breakdown: `132 + 80 + 0 + 8 = 220 = 2% × 11,000`.

### 6.3 Stay Only, After Cutoff (`T = 11,000`)
| Stage | Guest | Owner | Event manager | Platform | Gateway total |
| :--- | ---: | ---: | ---: | ---: | ---: |
| Before gateway fee | 0.00 | 10,000.00 | 0.00 | 1,000.00 | 0.00 |
| **After gateway fee (`2%`)** | **0.00** | **9,800.00** | **0.00** | **980.00** | **220.00** |

*Arithmetic check:* `0 + 9,800 + 0 + 980 + 220 = 11,000` (`T`). Gateway breakdown: `0 + 200 + 0 + 20 = 220 = 2% × 11,000`.

### 6.4 Stay Plus Décor (`E = 5,000`, `E_c = 10%`), Free Cancellation (`T = 16,000`)
| Stage | Guest | Owner | Event manager | Platform | Gateway total |
| :--- | ---: | ---: | ---: | ---: | ---: |
| Before gateway fee | 16,000.00 | 0.00 | 0.00 | 0.00 | 0.00 |
| **After gateway fee (`2%`)** | **15,680.00** | **0.00** | **0.00** | **0.00** | **320.00** |

*Arithmetic check:* `15,680 + 0 + 0 + 0 + 320 = 16,000` (`T`). Gateway total = `2% × 16,000 = 320`.

### 6.5 Stay Plus Décor (`E = 5,000`, `E_c = 10%`), After Cutoff (`T = 16,000`)
*(Using owner-stated rule that Event Manager receives `0` after cutoff, with `E = 5,000` retained by Owner pending lawyer confirmation on `E`'s disposition.)*

| Stage | Guest | Owner | Event manager | Platform | Gateway total |
| :--- | ---: | ---: | ---: | ---: | ---: |
| Before gateway fee | 0.00 | 15,000.00 | 0.00 | 1,000.00 | 0.00 |
| **After gateway fee (`2%`)** | **0.00** | **14,700.00** | **0.00** | **980.00** | **320.00** |

*Arithmetic check:* `0 + 14,700 + 0 + 980 + 320 = 16,000` (`T`). Gateway breakdown: `0 + 300 + 0 + 20 = 320 = 2% × 16,000`.

---

## Section 7. Current Code vs Model

| File & Line | What Current Code Does | Matches Section 2 or 3? |
| :--- | :--- | :--- |
| `Controllers/BookingController.cs:1084` | Computes `totalAmount = propertyStayAmount + eventPackagePrice` (`T = B` or `B + E`), without adding `r · B`. | **No** (Model: `T = B + r · B` or `B + r · B + E`). |
| `Services/CommissionService.cs:46-77` | `CalculateDynamicCommissionAsync` deducts `commission = B · r` from `B` (`ownerPayout = B − commission`). | **No** (Model adds `r · B` to guest total `T` so Owner base is `B`). |
| `Services/CommissionService.cs:79-163` | `CalculateEventCommissionAsync` deducts `E_c · E` from `E` (`managerPayout = E − E_c · E`). | **Yes** for completed stay (`E` is part of `T`, `E_c` deducted from `E`). |
| `Services/RefundEngineService.cs:116-128` | Free window hardcoded to `≥ 168h` (7 days) instead of `W` (`FreeCancellationWindowHoursSnapshot`, default 48h); deducts 2% gateway fee from guest refund. | **No** (Hardcoded `168h` ignores `W`). |
| `Services/RefundEngineService.cs:129-151` | Band hardcoded to `72h ≤ h < 168h` with fixed 50%/50% split instead of `C ≤ h < W` and `F_b`; `gatewayFeeDeducted = 0`; applies `50%` to `amountPaid` (including `E`). | **No** (Ignores `W`, `C`, `F_b`, gateway fee `g`, and `B` vs `E` separation). |
| `Services/RefundEngineService.cs:152-177` | Under cutoff hardcoded to `< 72h` instead of `< C`; `gatewayFeeDeducted = 0`; `ownerShare = amountPaid` (includes `E`); `commission = amountPaid · r`; no Event Manager cancellation rule. | **No** (Ignores `C`, `g`, and Event Manager `0` payout rule). |
| `Services/RefundEngineService.cs:292-340` | Adjusts `OwnerPayoutAmount` on cancellation using `amountPaid` without adjusting `EventManagerPayoutAmount` or applying 2% gateway fee per recipient. | **No** (Event Manager payout not zeroed/adjusted on cancellation). |
| `Services/DamageClaimService.cs:40-175` & `Controllers/BookingController.cs:1192-1194` | Captures `SecurityDepositAmount` from property and tracks `Held`/`Released`/`Claimed` via `DamageClaims`. | **Pending** (Section 5: Owner decision pending among 3 options). |

---

## Section 8. Open Decisions

1. **LAWYER DECISION (Custody of Funds):** Between guest payment and payout release, should funds be held in Nayesta's platform account or in a Razorpay Route / linked account with delayed transfer?
2. **Owner-stated, lawyer to confirm (Event Manager Post-Cutoff Cancellation):** Is it legally compliant under Indian contract/consumer law for the Event Manager to receive `0` on a guest cancellation after cutoff `C`, and if so, does `E` go to the Owner or to the Platform?
3. **Owner-stated, lawyer and Razorpay to confirm (Per-Party Gateway Fee):** Can Nayesta deduct `g` (`2%`) from each party's outgoing amount (including guest refunds and partner payouts) under Razorpay's terms and Indian consumer/payment regulations?
4. **Owner-stated, lawyer to confirm (Owner Payout Release & 7-Day Cap):** Are the owner payout release trigger (after cutoff `C`) and the 7-day maximum hold cap compliant with RBI/payment-aggregator and sole-proprietorship rules?
5. **Owner-stated, lawyer to confirm (Event Manager Release Timing):** Should the Event Manager's share be released at the same time as the Owner's share (after cutoff `C` / 7-day cap)?
6. **OWNER DECISION PENDING (Damage Claims):** Which damage-claim model should Nayesta adopt: (1) charge saved method after approved claim, (2) upfront non-refundable damage protection fee, or (3) no damage money on-platform?

---

## Section 9. Sign-Off

> **Note:** This settlement money model is **not approved** until all three signatures and dates below are filled in.

- **Owner Name:** \_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_  **Date:** \_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_
- **Lawyer Name:** \_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_  **Date:** \_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_
- **CA Name:** \_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_  **Date:** \_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_
