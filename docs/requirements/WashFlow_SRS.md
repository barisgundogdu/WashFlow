# WashFlow — Software Requirements Specification (SRS)

**Version:** 0.1 (Initial Draft)  
**Date:** 13 October 2026  
**Project:** WashFlow — Multi-Tenant Car Wash Management System  
**Target platform:** Flutter mobile application (Android/iOS), ASP.NET Core Web API, PostgreSQL  
**Status:** Draft for supervisor review; subject to refinement  
**Related GitHub issue:** #1 — Document WashFlow Software Requirements

## 1. Introduction

### 1.1 Purpose
This document defines the initial functional, non-functional, and business requirements of WashFlow, a mobile application designed to help independent car wash businesses manage customers, vehicles, appointments, service execution, and financial records. It establishes an initial, testable scope for development and evaluation.

### 1.2 Problem Statement
In the pilot business, appointments are usually handled by telephone and remembered rather than recorded. Customer and vehicle histories are not systematically stored. Daily revenue and expenses are recorded in notebooks. During busy periods, limited service areas and staff availability create queues and uncertainty about waiting times. These conditions motivate a centralized, role-based management system.

### 1.3 Scope
WashFlow will serve multiple independent car wash businesses through a shared application and backend. Each business will manage its own customers, employees, service catalog, prices, appointments, work orders, payments, and expenses. The first release targets business owners and employees, **not customers**. A customer-facing booking application, automatic SMS integration, advanced loyalty plans, and employee analytics are outside the initial minimum viable product (MVP) unless time permits.

### 1.4 Terminology
- **Tenant:** An independent car wash business using WashFlow.
- **Owner:** A tenant-level administrator responsible for managing business operations.
- **Employee:** A tenant user with restricted access to assigned work.
- **Work order:** A record of services to be performed for a vehicle.
- **Work stage:** A step in a work order (e.g., exterior wash followed by interior cleaning).
- **Work area:** A constrained physical service area such as an exterior or interior bay.
- **MVP:** The first demonstrable end-to-end version of the system.

## 2. Requirements Elicitation and Pilot Business

The initial requirements are based on operational discussions about **Gündoğdu Oto Yıkama**, which will serve as the pilot business for validation. The figures below describe the pilot; they are **not** universal defaults for all tenants.

### 2.1 Facilities and Staff
- One exterior washing bay, one interior cleaning bay, and one additional outdoor work area (capacity: one car).
- A separate outdoor drying area; drying does not normally block the three active work areas.
- Three employees: one primarily assigned to exterior washing and two to interior cleaning.
- Typical throughput is approximately 11–12 cars per day, reaching 16–17 on busy days.
- Customers may wait on site in a dedicated waiting area.

### 2.2 Existing Workflow
1. A customer arrives or contacts the business by phone to arrange a service.
2. Staff identify the requested service and inspect the vehicle; optional additional services may be suggested.
3. Staff select an available work area and sequence work according to required stages and personnel.
4. For combined interior/exterior washing, the vehicle generally passes from the exterior stage to the interior stage.
5. The customer pays by cash, card, or bank transfer; income and expenses are currently recorded manually.
6. Staff inform waiting customers verbally and contact absent customers by phone when needed.

### 2.3 Operational Rules Observed in the Pilot
- The business generally serves walk-in customers in arrival order while using whichever compatible area is available.
- A customer more than **30 minutes late** for an ordinary appointment loses appointment priority; if a bay is free on arrival, service may start immediately, otherwise the customer joins the ordinary queue.
- Long services such as detailing or polishing may require next-day completion if started late (often after approximately 13:00–14:00); the vehicle may remain overnight.
- Service durations vary by vehicle condition and type. The system must support configurable estimates rather than rigid universal times.
- These rules must be configurable at the tenant level or clearly marked as pilot-specific policies.

## 3. Proposed System Overview

### 3.1 High-Level Architecture
The proposed solution consists of:
1. **Flutter mobile client:** owner and employee screens, input validation, and API communication.
2. **ASP.NET Core Web API:** authentication, authorization, tenant scoping, appointment and work-order business logic, reporting endpoints.
3. **PostgreSQL database:** persistent tenant, user, customer, vehicle, service, appointment, work-order, payment, and expense data.

The API is the source of truth for authorization and scheduling decisions. Client-side filtering alone is insufficient for tenant isolation.

### 3.2 Multi-Tenancy
The initial design proposes a shared database and shared schema, with a `TenantId` on tenant-owned records. All relevant read/write operations must be scoped to the authenticated tenant on the server. Users must not be able to access another tenant's records by changing a request parameter or object identifier. A user-to-tenant membership model will determine business context and permissions.

**Design proposal pending confirmation:** One business location per tenant for the MVP. Multiple branches could be introduced later.

## 4. User Roles and Permissions

| Capability | Owner | Employee | Platform Administrator |
|---|---|---|---|
| Sign in | Yes | Yes | Yes |
| Manage own tenant's customers/vehicles | Yes | No | Not in routine operations |
| Manage appointments and services/prices | Yes | No | Not in routine operations |
| View assigned vehicles and stages | Yes | Yes | Not in routine operations |
| Start/complete assigned work stages | Yes | Yes | Not in routine operations |
| Record payments/expenses and view reports | Yes | No | Not in routine operations |
| Manage employee accounts/assignments | Yes | No | Not in routine operations |
| Manage tenant accounts/platform operations | No | No | Yes |

The platform administrator's exact interface and support-access policy remain design decisions. No cross-tenant access should be provided by default.

## 5. Functional Requirements

Each requirement is identified for traceability and later verification.

| ID | Requirement | Priority | Suggested verification |
|---|---|---|---|
| FR-01 | An authorized owner can create or activate a tenant account and sign in securely. | MVP | Registration/sign-in test |
| FR-02 | The API restricts access to data belonging to the authenticated tenant. | MVP | Cross-tenant access tests |
| FR-03 | The system enforces owner and employee permissions. | MVP | Role-based authorization tests |
| FR-04 | Owners can create, edit, search, and view customers. | MVP | Customer CRUD tests |
| FR-05 | Owners can register vehicles and associate them with customers. | MVP | Vehicle CRUD/relationship tests |
| FR-06 | Owners can maintain services, estimated durations, and vehicle-category prices. | MVP | Catalog/pricing tests |
| FR-07 | Owners can create, reschedule, and cancel appointments. | MVP | Appointment lifecycle tests |
| FR-08 | The system detects overlapping allocations for constrained work areas and staff. | MVP | Conflict scenario tests |
| FR-09 | Owners can add walk-in vehicles to a queue and create work orders. | MVP | Walk-in scenario test |
| FR-10 | Work orders can contain multiple services and ordered work stages. | MVP | Multi-stage workflow test |
| FR-11 | Employees can view assigned work and update authorized stage statuses. | MVP | Employee workflow/permission tests |
| FR-12 | The system presents estimated service completion/waiting information, labeled as an estimate. | MVP | Estimate calculation/display tests |
| FR-13 | The system supports tenant-specific appointment lateness handling. | MVP | Late-arrival scenario tests |
| FR-14 | Owners can record payments by cash, card, or bank transfer. | MVP | Payment recording tests |
| FR-15 | Owners can record expenses and view daily/weekly/monthly summaries. | MVP | Reporting aggregation tests |
| FR-16 | Owners can review customer and vehicle service history. | MVP | History retrieval tests |
| FR-17 | Owners can review employee throughput and active service time. | Later | Analytics tests |
| FR-18 | Owners can define and assign customer subscription/loyalty packages. | Later | Package lifecycle tests |
| FR-19 | The system can send automated customer notifications. | Later | Notification integration tests |

### 5.1 Example User Stories
**US-01 — Customer registration:** As an owner, I want to register a customer and their vehicle so that future appointments and work orders can reference their history.

**Acceptance criteria:** A valid customer and vehicle can be saved through the mobile UI and API; required fields are validated; the records persist in PostgreSQL; the owner can retrieve them; another tenant cannot retrieve them.

**US-02 — Appointment scheduling:** As an owner, I want to schedule a service using available work areas and personnel so that two incompatible jobs are not allocated to the same constrained resource at the same time.

**Acceptance criteria:** The system displays a proposed time; overlapping incompatible bookings are rejected or flagged; an appointment can be updated/cancelled; tenant policies and service duration estimates are applied.

**US-03 — Employee stage update:** As an employee, I want to mark an assigned stage as started or completed so that the owner can see current progress.

**Acceptance criteria:** Assigned stages appear on the employee screen; allowed status transitions persist; employees cannot edit prices or financial records; employees cannot access another tenant's work.

**US-04 — Daily financial overview:** As an owner, I want to see recorded payments and expenses for a selected day so that I can understand business activity.

**Acceptance criteria:** Payment methods and expenses can be recorded; totals are computed from persisted records; the report is tenant-scoped; reports are inaccessible to employees.

## 6. Business Rules and Scheduling

### 6.1 Resource Model
Each tenant defines work areas, area capacities, employees, service duration estimates, and the area/staff requirements of services. The scheduling logic must consider:
- **Capacity:** A constrained area cannot host more simultaneous active jobs than its configured capacity.
- **Stage sequence:** A combined wash may require exterior cleaning before interior cleaning.
- **Staff availability:** The same employee cannot be allocated to overlapping active stages.
- **Duration estimates:** Service times are configurable and may be revised after inspection.
- **Drying:** Drying may occupy a vehicle for hours without occupying an active work bay; it must be modeled separately from active labor.

### 6.2 Late Arrivals
The pilot's 30-minute late-arrival rule will be represented as a tenant setting. After the grace period, the appointment's priority is lost; the arriving vehicle is serviced immediately only if compatible resources are available, otherwise it joins the walk-in queue. **Open question:** Whether and how resources are held during the grace period requires confirmation.

### 6.3 Long-Duration Services
Detailing, polishing, and other long-duration services may span the business closing time or require overnight vehicle storage. The system must support estimated completion on a later date. The pilot's approximate 13:00–14:00 operational guideline is **not** a fixed system-wide cutoff.

### 6.4 Pilot Service Examples
The following are sample **pilot** values; other businesses set their own prices and durations.

| Service | Example price (automobile) | Estimated active time | Area/notes |
|---|---:|---|---|
| Exterior wash | 450 TRY | ~20 min | Exterior bay; 1 employee |
| Interior wash | 450 TRY | ~20 min | Interior bay; 2 employees |
| Interior + exterior | 700 TRY | ~45 min total | Sequential stages; flexible split |
| Quick wax | 200 TRY | ~3 min | Exterior bay |
| Full interior detailing | 6,000 TRY | ~2 h | Additional ~3 h drying |
| Polishing (pasta-cila) | 8,000 TRY | ~4 h | Work area configurable |
| Ceramic coating | 3,000 TRY | ~20 min | Work area configurable |
| Engine cleaning (waterless) | 700 TRY | ~15 min | Work area configurable |
| Wheel cleaning | 500 TRY | ~10 min | Exterior bay |
| Resin/scuff removal | 500 TRY | ~10 min | Work area configurable |
| Seat-only cleaning | 3,500 TRY | ~1 h | Additional ~2–3 h drying; staffing TBD |

Prices differ for SUV, commercial/pickup, minivan, and motorcycle categories. These values must be stored in a configurable service-price catalog, not embedded in application code.

## 7. Non-Functional Requirements

| ID | Requirement | Proposed verification |
|---|---|---|
| NFR-01 | Passwords are never stored in plaintext; use a well-established password hashing mechanism. | Authentication/security review |
| NFR-02 | All protected endpoints require valid authentication and server-side authorization. | API authorization tests |
| NFR-03 | Tenant data isolation applies to list, detail, create, update, and delete operations. | Automated negative tests |
| NFR-04 | Inputs are validated on the API; database constraints protect integrity. | Invalid-input and constraint tests |
| NFR-05 | Core flows are usable on common Android/iOS phone screen sizes. | Manual usability tests |
| NFR-06 | Appointment creation handles simultaneous requests without double-booking constrained resources. | Concurrent-request integration tests |
| NFR-07 | API errors are handled gracefully without exposing credentials or internal stack traces. | Error-handling tests |
| NFR-08 | Repository CI runs formatting/linting, tests, and build checks on each push/pull request. | GitHub Actions run evidence |
| NFR-09 | Financial records and work-order changes preserve consistent relationships and timestamps. | Data integrity tests |
| NFR-10 | Basic operations should respond promptly under the expected pilot workload; measurable thresholds will be set during evaluation. | Performance testing |

**Initial implementation assumption:** Internet connectivity is required for the MVP. Offline synchronization is a potential future feature, not an established requirement.

## 8. Preliminary Data Model

Proposed entities (subject to ER diagram review):

- `Tenants` — business account and settings.
- `Users`, `TenantUsers` — authentication identities and tenant memberships/roles.
- `Employees` — business staff profiles and availability.
- `Customers`, `Vehicles` — tenant-scoped customer and vehicle registry.
- `Services`, `ServicePrices` — tenant-specific service catalog and vehicle-category pricing.
- `WorkAreas` — bays/areas and capacities.
- `Appointments` — scheduled visits and appointment status.
- `WorkOrders`, `WorkOrderServices`, `WorkOrderStages` — execution and stage progress.
- `StageAssignments` — allocated employees and work areas over time.
- `Payments`, `Expenses` — financial records.
- `AuditEvents` — security-relevant or important operational change history (proposed).

Future-scope entities may include `SubscriptionPlans` and `CustomerSubscriptions`.

All tenant-owned entities must have a clear tenant ownership path, and foreign keys must not permit cross-tenant references. The ER diagram will be revised alongside actual migrations.

## 9. MVP Demonstration and Verification

The first end-to-end demonstration will focus on **owner login and customer/vehicle registration**:

1. The owner signs in from the Flutter mobile application.
2. The owner enters customer and vehicle details.
3. The mobile app submits validated data to ASP.NET Core Web API.
4. The API authenticates the request, applies tenant scoping, and persists the records in PostgreSQL.
5. The owner retrieves the saved customer and vehicle in the application.
6. Negative tests confirm unauthorized employees and other tenants cannot access or alter these records.

Evidence should include working mobile screens, API endpoints, database tables/migrations, automated tests, GitHub Issues, linked commits, and passing GitHub Actions workflows.

## 10. Delivery Plan and Traceability

| Phase | Planned focus | Expected evidence |
|---|---|---|
| 1 — Analysis and setup | Requirements, GitHub Issues/Project, initial thesis sections, preliminary ERD | SRS, board, diagrams |
| 2 — Foundation | Flutter skeleton, API, PostgreSQL, authentication, tenant scoping | Running end-to-end customer/vehicle flow |
| 3 — Operations | Service catalog, appointments, work orders, staff-stage updates | Functional demos and tests |
| 4 — Finance and evaluation | Payments, expenses, summaries, usability/security testing, thesis revisions | Evaluation results and final documentation |

Each implementation task should be represented by a GitHub Issue with acceptance criteria. Commit messages should reference the relevant issue number. The Kanban board must reflect actual progress rather than anticipated completion.

## 11. Assumptions, Open Questions, and Scope Control

The following items require confirmation before being treated as final requirements:

1. Whether tenant registration is self-service or approved by a platform administrator.
2. Whether the MVP supports one location or multiple branches per business.
3. How owners invite/create employee accounts and reset access.
4. Whether appointments reserve work areas during the 30-minute grace period.
5. How to resolve conflicts when actual stage durations exceed estimates.
6. Which work area and staffing rules apply to seat-only washing.
7. Whether customer notifications remain manual in the MVP.
8. Whether platform administrators require a dedicated interface in the MVP.

## 12. Document Status and Change Log

| Version | Date | Change |
|---|---|---|
| 0.1 | 2026-10-13 | Initial requirements draft based on pilot-business discussions and agreed mobile/backend/database scope. |

**Review note:** This is an initial SRS draft, not a claim that the system is already implemented or tested. Requirements, diagrams, and acceptance criteria will be updated as supervisor feedback and implementation evidence become available.
