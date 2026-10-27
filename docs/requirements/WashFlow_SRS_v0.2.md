# WashFlow — Software Requirements Specification (SRS)

**Version:** 0.2 (Scope Revision)  
**Date:** 20 October 2026  
**Project:** WashFlow — Multi-Tenant Car Wash Management and Customer Booking Platform  
**Target platform:** Flutter mobile application (Android/iOS), ASP.NET Core Web API, PostgreSQL  
**Status:** Draft for supervisor review; subject to refinement  
**Related GitHub issue:** #1 — Document WashFlow Software Requirements

## 1. Introduction

### 1.1 Purpose
This document defines the functional, non-functional, and business requirements of WashFlow, a single mobile application for car wash business owners, employees, and customers. WashFlow combines business operations management with customer self-service booking. It establishes a proposed, testable scope for implementation and evaluation.

### 1.2 Problem Statement
In the pilot business, appointments are usually handled by telephone and remembered rather than recorded. Customer and vehicle histories are not systematically stored. Daily revenue and expenses are recorded in notebooks. During busy periods, limited service areas and staff availability create queues and uncertainty about waiting times. Customers also lack a direct way to discover participating businesses, see available time slots, and book appointments themselves. These conditions motivate a centralized, role-based management and booking platform.

### 1.3 Scope
WashFlow will serve multiple independent car wash businesses and their customers through **one Flutter mobile application** and a shared backend. The application will offer role-aware experiences for **business owners, employees, and customers**. Businesses will manage their employees, tenant-specific customer records, services, prices, appointments, work orders, payments, and expenses. Customers will be able to create personal accounts, manage their own vehicles, discover participating businesses, view published services and available slots, and request/book appointments. Business-entered phone bookings, walk-ins, and customer-created bookings must use the same scheduling and capacity rules.

A customer identity is platform-wide rather than owned by one tenant; a customer may book at several businesses. Tenant-specific operational data remains isolated. Customer information is disclosed to a business only to the extent necessary for a legitimate booking or business record. Automatic SMS, advanced loyalty plans, employee analytics, and offline synchronization are outside the initial MVP unless time permits. Customer self-service booking is part of the **revised intended MVP**, subject to supervisor feedback and schedule feasibility.

### 1.4 Terminology
- **Tenant:** An independent car wash business using WashFlow.
- **Owner:** A tenant-level administrator responsible for managing business operations.
- **Employee:** A tenant member with restricted access to assigned work.
- **Customer:** A platform user who may book services at multiple participating businesses.
- **Platform Administrator:** An internal role for platform operations, not a fourth customer-facing mobile experience.
- **Tenant membership:** A user's authorized relationship and role within a business.
- **Booking source:** The origin of an appointment (customer self-service, owner-entered phone booking, or walk-in).
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
1. **Flutter mobile client:** one Android/iOS application with Turkish-language customer, owner, and employee experiences; form validation, role-aware navigation, and API communication.
2. **ASP.NET Core Web API:** authentication, role/ownership authorization, tenant scoping, safe business discovery, customer booking, appointment and work-order business logic, and reporting endpoints.
3. **PostgreSQL database:** persistent platform users, tenant memberships, customer profiles, owned vehicles, business-specific customer records, services, appointments, work orders, payments, and expenses.

The API is the source of truth for authorization and scheduling decisions. Client-side filtering alone is insufficient for tenant isolation or customer privacy. The same backend scheduling service must validate owner-entered and customer-created bookings.

### 3.2 Multi-Tenancy and Customer Ownership
The initial design proposes a shared database and shared schema. Tenant-owned operational records carry a `TenantId` or have a verifiable tenant ownership path. All tenant operations must be scoped to authorized membership on the server. Users must not access another business's records by changing an identifier.

Customer accounts are **platform-wide**, not tenant-owned. Customer-owned vehicles and profiles are protected by user ownership rules. Businesses may maintain tenant-local records for phone and walk-in customers who do not have WashFlow accounts. Associating such records with a registered customer requires a verified linking process; matching a name or license plate alone is not proof of ownership. Booking records connect a business with an authorized customer and vehicle without granting that business unrestricted access to the customer's data elsewhere.

A person may hold multiple authorized roles or tenant memberships. The API must validate the active role and business context on every protected operation; a role switch must not elevate privileges.

**Approved business decision (Phase 6):** Each physical branch is one independent Business/tenant. A user may own multiple businesses; a membership may contain both Owner and Employee grants. A shared multi-branch tenant is outside this scope.

## 4. User Roles and Permissions

| Capability | Owner | Employee | Customer | Platform Administrator |
|---|---|---|---|---|
| Sign in | Yes | Yes | Yes | Internal access only |
| Manage own profile | Yes | Yes | Yes | As permitted |
| Manage tenant's customer/vehicle records | Yes | No | No | Not routinely |
| Manage own customer vehicles | If customer role | If customer role | Yes | No |
| Discover businesses and published services | Yes | Yes | Yes | As permitted |
| Create/reschedule/cancel tenant appointments | Yes | No | Own bookings only, per policy | Not routinely |
| Manage services and prices | Yes | No | No | Not routinely |
| View assigned vehicles and stages | Yes | Yes | No | Not routinely |
| Start/complete authorized work stages | Yes | Yes | No | Not routinely |
| Record payments/expenses and view financial reports | Yes | No | No | Not routinely |
| Manage employee accounts/assignments | Yes | No | No | Not routinely |
| Manage platform accounts/operations | No | No | No | Yes |

An owner is authorized within their tenant; a customer is authorized over their own profile, vehicles, and bookings. An employee can update only assigned work. A user may have multiple roles, but each request must be checked against the relevant role and ownership/membership. The platform administrator's exact interface and support-access policy remain design decisions; no unrestricted cross-tenant business-data access is granted by default.

## 5. Functional Requirements

Each requirement is identified for traceability and later verification. **MVP** means the revised planned first release, not an implemented feature.

| ID | Requirement | Priority | Suggested verification |
|---|---|---|---|
| FR-01 | Authorized owners can create/onboard a tenant account and sign in securely. | MVP | Onboarding/sign-in tests |
| FR-02 | The API restricts access to tenant-owned data to authorized members. | MVP | Cross-tenant access tests |
| FR-03 | The system enforces owner, employee, and customer permissions and ownership. | MVP | Role/ownership tests |
| FR-04 | Owners can create, edit, search, and view tenant-local customer records. | MVP | Customer CRUD tests |
| FR-05 | Owners can register vehicles for tenant-local customers; customers manage their own account-owned vehicles. | MVP | Vehicle ownership/CRUD tests |
| FR-06 | Owners can maintain services, estimated durations, and vehicle-category prices. | MVP | Catalog/pricing tests |
| FR-07 | Owners can create, reschedule, and cancel appointments. | MVP | Appointment lifecycle tests |
| FR-08 | The system prevents conflicting work-area and employee allocations, including concurrent bookings. | MVP | Conflict/concurrency tests |
| FR-09 | Owners can add walk-in vehicles to a queue and create work orders. | MVP | Walk-in scenario test |
| FR-10 | Work orders can contain multiple services and ordered work stages. | MVP | Multi-stage workflow test |
| FR-11 | Employees can view assigned work and update authorized stage statuses. | MVP | Employee permission tests |
| FR-12 | The system presents estimated service completion/waiting information, labeled as estimates. | MVP | Estimate tests |
| FR-13 | The system supports tenant-specific appointment lateness handling. | MVP | Late-arrival tests |
| FR-14 | Owners can record payments by cash, card, or bank transfer. | MVP | Payment tests |
| FR-15 | Owners can record expenses and view daily/weekly/monthly summaries. | MVP | Reporting tests |
| FR-16 | Owners can review authorized customer/vehicle service history for their business. | MVP | History/privacy tests |
| FR-17 | Owners can review employee throughput and active service time. | Later | Analytics tests |
| FR-18 | Owners can define customer subscription/loyalty packages. | Later | Package tests |
| FR-19 | The system can send automated customer notifications. | Later | Notification tests |
| FR-20 | Customers can register, sign in, and maintain a personal profile. | MVP | Customer authentication/profile tests |
| FR-21 | Customers can create and manage multiple vehicles associated with their own account. | MVP | Vehicle ownership tests |
| FR-22 | Customers can discover participating businesses and view published services and prices. | MVP | Discovery/catalog tests |
| FR-23 | Customers can view available slots and book a service for an owned vehicle. | MVP | End-to-end booking tests |
| FR-24 | Customer-created, owner-entered, and walk-in appointments use the same server-side capacity and conflict rules. | MVP | Cross-source/concurrency tests |
| FR-25 | Customers can view and cancel their own appointments under defined business policies. | MVP | Booking ownership/lifecycle tests |
| FR-26 | Customers can book at multiple businesses without exposing their full account or other-business history. | MVP | Cross-business privacy tests |
| FR-27 | Accounts with multiple authorized roles or tenant memberships can switch context without privilege escalation. | MVP | Context-switch tests |
| FR-28 | Unregistered phone/walk-in customers can be represented as tenant-local records; account linking requires verification. | MVP | Local-record/linking tests |

### 5.1 Example User Stories
**US-01 — Owner customer registration:** As an owner, I want to register a walk-in customer and their vehicle so that work orders can reference a local service history.

**Acceptance criteria:** The owner saves a valid local customer and vehicle through the app and API; another tenant cannot retrieve them; the record is not silently linked to an unrelated customer account.

**US-02 — Owner appointment scheduling:** As an owner, I want to schedule services using available areas and personnel so that incompatible jobs do not overlap.

**Acceptance criteria:** The API validates resource availability; conflicting bookings are rejected; authorized appointments can be updated or canceled.

**US-03 — Employee stage update:** As an employee, I want to start or complete an assigned stage so that the owner sees progress.

**Acceptance criteria:** Only assigned stages can be updated; employees cannot change prices or access financial records.

**US-04 — Daily financial overview:** As an owner, I want to review recorded payments and expenses so that I can monitor operating activity.

**Acceptance criteria:** Stored transactions produce tenant-scoped summaries that employees cannot access.

**US-05 — Customer account and vehicles:** As a customer, I want to register and maintain my own vehicles so that I can reuse them for bookings.

**Acceptance criteria:** Account and vehicle changes are validated; another customer cannot access or alter the records.

**US-06 — Customer self-service booking:** As a customer, I want to select a participating car wash, service, vehicle, and available time so that I can make my own reservation.

**Acceptance criteria:** The booking appears on the business calendar; the server applies the same resource conflict checks as owner bookings; the customer can view only their own bookings and cancel within policy.

**US-07 — Multi-business customer privacy:** As a customer, I want to book with different businesses without disclosing unrelated personal or service history.

**Acceptance criteria:** Businesses receive only necessary information for their own appointments; cross-tenant and cross-customer access tests fail safely.

## 6. Business Rules and Scheduling

### 6.1 Resource Model
Each tenant defines work areas, area capacities, employees, service duration estimates, and the area/staff requirements of services. The scheduling logic must consider:
- **Capacity:** A constrained area cannot host more simultaneous active jobs than its configured capacity.
- **Stage sequence:** A combined wash may require exterior cleaning before interior cleaning.
- **Staff availability:** The same employee cannot be allocated to overlapping active stages.
- **Duration estimates:** Service times are configurable and may be revised after inspection.
- **Drying:** Drying may occupy a vehicle for hours without occupying an active work bay; it must be modeled separately from active labor.

### 6.2 Late Arrivals
The pilot's 30-minute late-arrival rule will be represented as a tenant setting. After the grace period, the appointment's priority is lost; the arriving vehicle is serviced immediately only if compatible resources are available, otherwise it joins the walk-in queue. The same rule must be consistently applied regardless of whether the appointment was created by an owner or a customer. **Open question:** Whether and how resources are held during the grace period requires confirmation.

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
| NFR-01 | Passwords are never stored in plaintext; use a well-established password hashing mechanism. | Security review |
| NFR-02 | All protected endpoints require valid authentication and server-side authorization. | API authorization tests |
| NFR-03 | Tenant isolation applies to all relevant read/write operations. | Cross-tenant negative tests |
| NFR-04 | Inputs are validated by the API; database constraints protect integrity. | Validation/constraint tests |
| NFR-05 | Owner, employee, and customer flows are usable on common Android/iOS phone sizes, initially in Turkish. | Mobile usability tests |
| NFR-06 | Concurrent appointment requests cannot double-book constrained resources. | Concurrency integration tests |
| NFR-07 | API errors do not expose credentials or internal stack traces. | Error handling tests |
| NFR-08 | Repository CI runs formatting/linting, tests, and build checks on pushes/pull requests. | GitHub Actions evidence |
| NFR-09 | Financial records and work-order changes preserve consistent relationships and timestamps. | Data integrity tests |
| NFR-10 | Basic operations respond promptly under pilot workload; thresholds will be defined during evaluation. | Performance tests |
| NFR-11 | Customer profile, vehicle, and booking endpoints enforce object-level ownership, not merely a valid login. | Cross-customer authorization tests |
| NFR-12 | Public business discovery exposes only intended published information, not tenant financial or employee data. | Public API disclosure tests |
| NFR-13 | Customer identity and tenant-local records are linked only after verified authorization. | Account linking tests |
| NFR-14 | Role/context switching does not bypass backend authorization. | Role-switch security tests |

**Initial implementation assumption:** Internet connectivity is required for the MVP. Offline synchronization is a potential future feature. Sensitive personal data collection and retention rules must be reviewed before deployment.

## 8. Preliminary Data Model

Proposed entities (subject to ER diagram and privacy review):

- `Tenants` — business account and settings.
- `Users` — platform-wide authenticated identities.
- `TenantUsers` — business memberships and authorized roles.
- `CustomerProfiles` — personal profile linked to a platform user.
- `CustomerVehicles` — customer-owned vehicles, linked to their account.
- `BusinessCustomers` — tenant-local customer records, including unregistered phone/walk-in customers.
- `BusinessCustomerLinks` (proposed) — verified association between tenant-local records and registered accounts.
- `Employees` — business staff profiles and availability.
- `Services`, `ServicePrices` — tenant-specific service catalog and vehicle-category pricing.
- `WorkAreas` — bays/areas and capacities.
- `Appointments` — tenant-linked bookings, customer/vehicle references where applicable, booking source, and status.
- `WorkOrders`, `WorkOrderServices`, `WorkOrderStages` — execution and stage progress.
- `StageAssignments` — allocated employees and work areas over time.
- `Payments`, `Expenses` — tenant financial records.
- `AuditEvents` — proposed security/operational history.

Future-scope entities may include `SubscriptionPlans` and `CustomerSubscriptions`.

**Ownership distinction:** Tenant-owned records require a clear tenant ownership path. Platform customer profiles and vehicles require a user ownership path. Booking associations must be validated to prevent cross-tenant references, impersonation, and unauthorized access to customer data. The ER diagram and table names will be refined during design and migrations.

## 9. MVP Demonstration and Verification

The first end-to-end demonstration will focus on **authentication and a working owner-side customer/vehicle flow**; a subsequent milestone will demonstrate **customer self-service booking** through the same scheduling engine.

**Milestone A — Owner flow**
1. An owner signs in from the Flutter application.
2. The owner creates a tenant-local customer and vehicle record.
3. The Flutter app submits validated data to the ASP.NET Core API.
4. The API authenticates the owner, verifies tenant membership, and persists records in PostgreSQL.
5. The owner retrieves the saved records; negative tests reject unauthorized employees and other tenants.

**Milestone B — Customer flow**
1. A customer registers and signs in to the same Flutter application.
2. The customer adds a personally owned vehicle.
3. The customer finds a participating business and published service, then requests an available slot.
4. The API validates ownership and tenant scheduling capacity transactionally before confirming the booking.
5. The booking appears in the business appointment calendar and the customer's own history.
6. Tests reject conflicting simultaneous bookings, unauthorized access to another customer's data, and cross-tenant record access.

Evidence should include working mobile screens, API endpoints, database tables/migrations, automated tests, GitHub Issues, linked commits, and passing GitHub Actions workflows. These are **planned demonstrations**, not completed results.

## 10. Delivery Plan and Traceability

| Phase | Planned focus | Expected evidence |
|---|---|---|
| 1 — Analysis and setup | Requirements revision, GitHub Issues/Project, initial thesis sections, role-aware mockups and ERD | Updated SRS, board, mockups, diagrams |
| 2 — Foundation | Flutter skeleton, API, PostgreSQL, authentication, owner/employee/customer roles and permissions | Working owner-side end-to-end flow |
| 3 — Operations and booking | Service catalog, capacity-aware appointments, customer self-booking, work orders, employee stage updates | Business and customer booking demos, tests |
| 4 — Finance and evaluation | Payments, expenses, reports, privacy/concurrency testing, thesis revisions | Evaluation evidence and final documentation |

Each implementation task should have a GitHub Issue with acceptance criteria; commits should reference relevant issues. The Kanban board must reflect actual progress. Related issues include #2 (authentication), #3 (customers/vehicles), #4 (scheduling), #8 (roles), #9 (Flutter authentication UI), #10 (customer profiles), and #11 (customer self-service booking).

## 11. Assumptions, Open Questions, and Scope Control

The following require confirmation before being treated as final implementation policies:

1. Whether tenant onboarding is self-service or requires platform approval.
2. Resolved: each physical branch is an independent Business/tenant (see Section 3.2).
3. How owners invite/create employees and reset their access.
4. Whether appointments reserve work areas during the 30-minute grace period.
5. How to resolve conflicts when actual stage durations exceed estimates.
6. Which work area and staffing rules apply to seat-only washing.
7. Whether customer notifications remain manual in the MVP.
8. Whether platform administrators need a dedicated interface.
9. Whether customer bookings are confirmed immediately after capacity validation or require business approval.
10. Customer cancellation/rescheduling windows and business-specific booking policies.
11. What business/customer contact details are visible before and after a booking.
12. How tenant-local walk-in customer records are verified and linked to customer accounts.
13. Resolved: multiple memberships and separate Owner/Employee experiences are supported through server-authorized context selection.
14. Supervisor confirmation of the expanded customer self-service MVP scope and timeline.

The expanded scope should be implemented incrementally: complete a secure owner-side vertical slice before adding customer booking. Any deferred requirement must be reflected consistently in the SRS, GitHub Issues, and thesis.

## 12. Document Status and Change Log

| Version | Date | Change |
|---|---|---|
| 0.1 | 2026-10-13 | Initial requirements draft based on pilot-business discussions and owner/employee mobile scope. |
| 0.2 | 2026-10-20 | Revised scope to one Flutter app for owners, employees and customers; added customer accounts, booking, privacy and updated requirements. |

**Review note:** This is a revised SRS draft, not a claim that the system is already implemented or tested. Requirements, diagrams, and acceptance criteria will be updated as supervisor feedback and implementation evidence become available.

### Implementation evidence — Phase 7 (27 October 2026)

FR-04/05/21/28 and US-01 now have an integrated customer/vehicle management slice, with separate
Owner tenant-local records and Customer-owned vehicles, scoped search/pagination, versioned edits
and vehicle archival. No automatic linking, scheduling, work-order execution or cross-business
sharing is implemented. This note records implementation status without changing the requirements.
See [Phase 7 evidence](../features/customer-vehicle-management-phase7.md) for exact tests and limits.
