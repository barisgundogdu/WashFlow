# Final sprint schema evidence

25 November 2026. This inventory describes a **new disposable rehearsal database**, created on the separate PostgreSQL 17 test service at port 55439 and migrated through migration 11. It does not describe a migration of the preserved demo database. No existing demo records were inspected or changed for this capture.

- [Complete schema inventory](schema-after-sprint.json): **33 application tables, 340 user-defined application columns, 71 foreign keys, 11 applied migrations**. EF migration metadata and PostgreSQL's implicit xmin are excluded from application table/column counts.
- [Mermaid ERD source](schema-after-sprint.mmd) and [catalog-generated SVG](schema-after-sprint.svg) include all application tables and one relationship per physical foreign key. The standalone SVG groups tables by module and points arrows from child to parent; tooltips retain exact constraint definitions. It is generated directly from the same inventory, not a claimed Mermaid render. The JSON preserves complete key/check definitions, indexes, trigger definitions, length and numeric precision/scale; multiple edges between tables represent distinct physical/composite foreign keys.
- Existing historical [demo ERD and inventory](../database/README.md) remain intact, explicitly dated 15 November 2026: seven migrations and 23 application tables. These historical artifacts must not be mistaken for the new source schema.

The approved increments add WorkOrders, WorkOrderStages, WorkStageAssignments and WorkOrderEvents; ServiceStageTemplates and AppointmentStages; Payments, ManualIncomes, Expenses and PaymentMethods. Customer self-service extends the existing Appointments calendar with ownership references rather than adding another calendar. Restrictive foreign keys, source/amount/status checks, xmin concurrency, business-first transactions and immutable execution/financial history preserve consistency.

Each migration has a separate reviewed increment and upgrade test. No legacy wash completion, customer identity link, payment or expense was inferred/backfilled. Any eventual update of the preserved demo needs an explicitly approved target, backup and migration plan. Do not overwrite the historical demo evidence with this isolated schema capture.


## Reproduction artifacts

`read-schema.sql` contains only SELECT catalog queries. Execute it against the deliberately verified isolated target with a read-only connection, retaining the source/date wrapper used in the JSON. `render-schema.py` uses only Python's standard library and the captured JSON to regenerate the standalone SVG; `python3 docs/final-sprint/render-schema.py` validates the XML and table count. The Mermaid source is also supplied for other renderers. The SVG was rendered with Quick Look for visual review. No application package or global dependency was added.
