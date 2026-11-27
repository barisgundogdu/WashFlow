# Owner finance increment

25 November 2026. Refs #7. Approved architectural increment 3, stacked on customer PR #20 (`945848e`).

## Working vertical slice

Turkish Flutter screens and owner-only backend support completed-work receivables, unpaid/partial/paid status, partial payments, cash/card/bank transfer and tenant-configured additional methods, manual categorized income, expenses with category/date/description/reference and optional tenant employee, paginated ledger history, expense correction and explicit reversals. Methods initialize once per business on first authorized finance access; archiving preserves historical method-name snapshots.

A completed work order's immutable appointment price is earned service revenue. Payments represent collection and are never added again to earned revenue. Reports distinguish completed service revenue, collected payments, manual income, expenses, service-based operating result (earned + manual − expense), cash operating balance (collected + manual − expense), and all-time unpaid balance. Neither result is labeled accounting profit. Dashboard supports today/week/month/custom half-open business-local date ranges, daily trends and service/category/method breakdown bars. Method breakdown includes collections and manual income with an explicit label.

Every write locks the business before freshly validating owner membership/grant, verified account and device session. Completed-work/payment-method/optional-employee relationships remain tenant scoped. Positive TRY decimal values are canonicalized without floating-point arithmetic; concurrent payments cannot exceed the remaining collectible balance. Flutter aggregate money uses exact BigInt kuruş, including totals above a single-entry maximum. Session revision and context generation reject late account-switch replies.

Device-secure pending requests are keyed by business and owner membership and stored before sending. Uncertain responses retain the same request key for explicit replay. A storage failure prevents sending. No automatic retry occurs.

## Corrections and audit semantics

Financial rows are append-only. Correcting an expense atomically inserts an exact reversal of the old row plus a linked replacement; failure of either insert rolls back both. Explicit cancellation creates a positive-amount reversal interpreted negatively in totals, retaining original amount, occurrence time, method/category/reference and actor history. Reversal cannot reverse another reversal or negate an original twice. Database triggers reject mutation/deletion and mismatched reversals. Old periods are restated because a reversal uses the erroneous entry's original occurrence date; the separate creation timestamp records when the correction was made. This is bookkeeping correction, **not a refund or a payment gateway operation**. Payment/manual-income correction is explicit reversal followed by a separate new record. Expenses are never physically deleted.

## Migration and compatibility

Migration 11 `20261125132208_OwnerFinancialLedger` adds Payments, ManualIncomes, Expenses and PaymentMethods, restrictive/composite FKs, amount/check constraints, indexes and append-only/reversal triggers. It does not backfill revenue, payments, expenses or completion facts. All previous appointment/customer/work-order data and schemas are preserved. Downgrade refuses to discard any financial or payment-method record. Offline SQL `/tmp/washflow-finance-upgrade.sql` reviewed; populated schema-10 upgrade and loss-guard tests passed on disposable PostgreSQL. **Existing demo database was not migrated.**

## Verification

- **444 backend tests passed**: 254 unit + 190 integration; zero failed/skipped. Log `/tmp/washflow-finance-backend5.log`.
- **370 Flutter tests passed**, zero failures/skips. Log `/tmp/washflow-finance-flutter-final2.log`.
- `flutter analyze --no-pub`: clean (`/tmp/washflow-finance-analyze-final2.log`). Whitespace check clean.
- Eight new PostgreSQL tests exercise completed wash → partial/full collection + manual income + expense → exact reports, concurrent overpayment prevention, price snapshot history, role/tenant/revocation and relationship denial, repeat-safe correction/reversal and immutable history, business-local midnight/exclusive boundaries, invalid precision/amounts, archived/configurable methods, payment reversal restoring balance, injected replacement failure rolling back the reversal, and populated additive upgrade/guarded downgrade.
- Twelve Flutter tests cover aggregate precision, safe adapters/tenant rejection, distinct earned/collected/cash values, explicit expense/payment confirmation, pending-request recovery, late-response disposal, owner-only navigation, device storage failure and correction reason/original reference.
- Native simulator/live backend rehearsal is a separate delivery check; do not infer deployment from automated module tests.
