# WashFlow midterm thesis progress report

The [compiled report](washflow-midterm-progress-report.pdf) is in English, with the Turkish abstract required by the template. The source adapts [Istanbul Arel University's thesis template](https://github.com/kocatekin/arel_thesis_latex_template/tree/866a986d808475984d49764073ad7f862fa2cd31). The university logo is from that revision. The only change to the style file corrects the spelling of “partial fulfillment” on the cover. The report retains the template chapter structure and IEEEtran bibliography.

## Build

From this directory, run:

```sh
tectonic --reruns 3 --outdir /tmp thesis.tex
```

Alternatively, run `latexmk -pdf thesis.tex` from this directory.

The architecture and business relationship overview are drawn in `thesis.tex`. The landscape appendix uses `figures/live-erd.pdf`, a vector export of the verified [Mermaid source](../database/live-erd.mmd) with its layout direction changed from `LR` to `TB`. The layout preserves the 24 tables, 219 columns and 38 foreign keys captured in the [live schema inventory](../database/live-schema.json). Open the [original SVG](../database/live-erd.svg) or zoom into the PDF for individual column labels.

## Screenshot capture

The six included captures were taken from the actual Flutter app on an iPhone 18 Pro simulator. The capture harness used the app's real API adapters, a local ASP.NET Core process, and a disposable PostgreSQL instance. The customer and vehicle records were synthetic. The calendar capture shows the empty agenda. The harness completed successfully.

Use the actual Flutter app against a separate local API and disposable PostgreSQL instance seeded only with synthetic records. Follow the [backend setup](../../backend/README.md), with a dedicated database and the Development memory email provider. From `mobile/`, run:

```sh
flutter devices
flutter run -d <simulator-id> --dart-define=API_BASE_URL=http://127.0.0.1:5197
```

Use `127.0.0.1:5197` for an iOS simulator if the isolated API listens on that port. On the default Android emulator, use `10.0.2.2:5197`. Capture on iOS using `xcrun simctl io <simulator-id> screenshot <png-path>`. Save each PNG under `docs/thesis/screenshots/` and rebuild the report. The appendix shows a labeled placeholder for each missing PNG.

| PNG filename | Screen and capture steps |
| --- | --- |
| `01-sign-in.png` | From the welcome screen, select **Giriş yap**. Capture the blank sign-in form with the keyboard hidden. |
| `02-sign-up.png` | Return to the welcome screen. Select **Kayıt ol**, then **İşletme kaydı**. Capture the blank owner registration form. |
| `03-owner-dashboard.png` | Verify and sign in with a synthetic owner for **WashFlow Demo**. Open its owner context and capture the dashboard. |
| `04-customer-management.png` | Open **Müşteriler ve araçlar**, create **Demo Müşteri** with **Müşteri ekle**, and capture the customer list. |
| `05-vehicle-management.png` | Open **Demo Müşteri**, select **Araç ekle**, and save a synthetic vehicle such as **34 WFL 26**. Capture the customer details and saved vehicle. |
| `06-appointment-scheduling.png` | Configure business hours and the **Europe/Istanbul** time zone, return to the dashboard, select **Planlama ve randevular**, and capture **Randevu ajandası**. For an occupied agenda, add a synthetic service, work area, and staff availability, then create a test appointment. |

Review each image for visible personal data, passwords, invitation or verification codes, and tokens before adding it to the report.

## Before submission

The Undertaking reproduces the two paragraphs in the original template, with `WashFlow` substituted for `Project Title`. Before submission, the student must confirm that the declaration applies and follow any separate institutional requirements for a signature or date.


## Final sprint evidence (25 November 2026)

The dated midterm figures above remain historical. The final source now includes employee stage execution, customer self-service and an audited owner financial ledger. [Final delivery status](../final-sprint/FINAL_STATUS.md), [native rehearsal](../final-sprint/LIVE_REHEARSAL.md), [test matrix](../final-sprint/TEST_MATRIX.md) and [isolated source-schema evidence](../final-sprint/SCHEMA_EVIDENCE.md) provide actual results for the final report. Verified results: 445 backend tests, 370 Flutter tests and one complete native real-HTTP/PostgreSQL scenario passed. Four new native screenshots are under `docs/final-sprint/screenshots/`; they are not replacements for the historical figures. The new schema has 33 application tables/71 FKs/11 migrations on a new disposable target. The preserved demo was not migrated. Integrating these results into the final thesis PDF and reviewing the presentation remains a separate academic editing step; this source update does not claim a rebuilt final thesis.
