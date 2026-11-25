# Live PostgreSQL ER diagram

The midterm report diagram describes `washflow_phase9e_db.public` on the local PostgreSQL 17.11 server. Its catalog was captured on 15 November 2026 against repository baseline `ec756d7`. The work is tracked in [Issue #14](https://github.com/barisgundogdu/WashFlow/issues/14).

The recorded demo API launch at `127.0.0.1:5080` used `washflow_phase9e_db`. There was no pre-existing API listener at the time of inspection. To verify the target, the same application was started temporarily on a separate loopback port with the recorded database target and read-only sessions. Its `/health/ready` endpoint returned HTTP 200 and PostgreSQL reported its tagged connection on `washflow_phase9e_db` in `pg_stat_activity`. The temporary API process was stopped after verification.

- [ER diagram source](live-erd.mmd)
- [Rendered SVG for the report](live-erd.svg)
- [Live catalog inventory](live-schema.json)
- [Read-only catalog extraction](read-schema.sql)

## Scope and notation

| Database, `public` schema | Application tables | EF metadata tables | Columns, including metadata | Foreign keys | Applied migrations |
| --- | ---: | ---: | ---: | ---: | ---: |
| `washflow_phase9e_db` (demo) | 23 | 1 | 219 | 38 | 7 |
| `washflow_db` (earlier local schema) | 10 | 1 | 73 | 12 | 2 |

The diagram shows each user-defined column in order, its PostgreSQL type, and `NULL` or `NOT NULL`. Spaces in types are shown as underscores. The note on a numeric column gives its exact precision and scale. `PK` and `FK` mark membership in a key. `UK` marks a single-column, unconditional unique index or constraint other than the primary key. The catalog inventory preserves composite key order, target columns, check constraints, defaults, unique index predicates, delete actions, and trigger definitions. PostgreSQL's implicit `xmin` system column is excluded from the user-defined column count. EF uses it for optimistic concurrency.

Each diagram edge represents one physical foreign key. The label lists its referencing columns in order. A solid edge denotes a foreign key contained in the child's primary key. The crow's foot describes the database cardinality. In particular, the filtered unique index on non-null `RefreshTokens.ReplacedByTokenId` limits references to a replacement token to one. `__EFMigrationsHistory` is labeled as EF metadata and has no application foreign keys.

## Comparison with EF Core

The SRS describes a preliminary model. This diagram uses the schema that PostgreSQL reports. `dotnet ef migrations has-pending-model-changes` found no difference between the current EF model and its latest migration snapshot.

A separate disposable PostgreSQL 17 container was built from the branch's migration SQL for comparison. The audited demo inventory matched the full seven-migration reference exactly across the captured tables, columns, types, nullability, defaults, constraints, indexes, triggers, and migration history. The final migration is `20261027125056_SchedulingPersistencePhase9B`. The demo schema includes 24 primary keys, 11 unique constraints, 52 unique indexes including constraint-backed indexes, and four application-defined triggers.

The separate `washflow_db` inventory matched a reference stopped at `20261021135215_EmailVerificationIssuanceIndexes`. It differs from the current EF schema as follows.

| Missing from `washflow_db` | Difference |
| --- | --- |
| Employee invitation lifecycle | Adds `CK_EmployeeInvitations_FinalState` and changes the predicate of `IX_EmployeeInvitations_BusinessId_NormalizedEmail`. |
| Business profile and hours | Adds 12 columns on `Businesses` and the `WeeklyOpeningDays` table. |
| Customer and vehicle ownership | Adds `LocalCustomers`, `LocalVehicles`, and `OwnedVehicles`. |
| Service catalog | Adds `BusinessServices` and `ServiceOfferings`. |
| Scheduling persistence | Adds `Appointments`, `AppointmentEvents`, `AppointmentResourceReservations`, `EmployeeAvailabilities`, `SchedulingCreateReceipts`, `ServiceSchedulingRequirements`, and `WorkAreas`. |

## Reproduction and validation

Run [read-schema.sql](read-schema.sql) with `psql -X -qAt -v ON_ERROR_STOP=1 -f` using an existing secure local connection configuration and explicitly select the intended database. The query opens a repeatable-read, read-only transaction and rolls it back. It reads PostgreSQL catalogs and EF migration IDs. Neither audited database was migrated or changed.

The Mermaid source and rendered SVG were checked against the live inventory for all 24 tables, 219 columns and 38 foreign keys. The SVG was generated with Mermaid CLI 11.12.0 as scalable text and vector paths. To regenerate it from this directory, run `mmdc -i live-erd.mmd -o live-erd.svg -b white`.

The push CI run after [PR #13](https://github.com/barisgundogdu/WashFlow/pull/13) merged into `feature/phase10-ui-ux` completed successfully on `ec756d7`. Both Backend (.NET and PostgreSQL) and Flutter analysis and tests passed. [GitHub Actions run](https://github.com/barisgundogdu/WashFlow/actions/runs/43708446155)


## Final sprint source schema (25 November 2026)

The preserved demo described above was **not migrated** during the final sprint. A separate new rehearsal database now verifies the eleven-migration source model: [schema evidence](../final-sprint/SCHEMA_EVIDENCE.md), [inventory](../final-sprint/schema-after-sprint.json), and [ERD](../final-sprint/schema-after-sprint.svg). It contains 33 application tables, 340 application columns and 71 foreign keys; counts exclude EF metadata/xmin. Keep the dated midterm demo evidence separate from this new isolated model.
