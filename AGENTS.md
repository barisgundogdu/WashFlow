# WashFlow — Agent Guidelines

## Project and Purpose

**Project:** WashFlow – Multi-Tenant Car Wash Management System

WashFlow brings multiple independent car wash businesses, their employees, and their customers together in one mobile application. It combines business operations management with customer self-service booking.

Use [WashFlow SRS v0.2](docs/requirements/WashFlow_SRS_v0.2.md) as the requirements baseline, together with current GitHub Issues and explicit user instructions. The SRS is a draft subject to supervisor feedback; planned features must not be described as already implemented.

## Technology Stack

- **Flutter and Dart:** One Android/iOS mobile application.
- **ASP.NET Core Web API:** Backend services and business logic.
- **PostgreSQL and Entity Framework Core:** Database persistence and data access.
- **JWT-based authentication:** Authentication for protected API access.

## User Roles

1. **Business owner:** Manage their business, employees, tenant-local customers, vehicles, services, prices, appointments, and finances, including payments, expenses, and reports.
2. **Employee:** View assigned work and update authorized work stages. Employees must not gain owner-level management or financial access.
3. **Customer:** Register, sign in, add and manage their own vehicles, discover participating businesses, create appointments, and track their own appointments. Cancellation and rescheduling must follow confirmed business policies.

## Core Rules

- Use a single Flutter application with role-aware experiences.
- The application's user interface must be in Turkish, including validation and user-facing error messages.
- English may be used for code identifiers and technical file names.
- Isolate every business's operational data from other businesses. Enforce tenant scoping for all relevant reads and writes on the backend.
- Customer accounts are platform-wide. Customers may book appointments at multiple businesses without exposing unrelated account information or other-business service history.
- Distinguish customer-owned profiles and vehicles from tenant-local customer records. Linking these records requires verified authorization; a matching name or license plate is insufficient.
- Enforce authorization on the backend as well as in Flutter. Validate roles, tenant membership, and record ownership; client-side visibility or filtering is insufficient.
- Check appointment conflicts against employee availability and work-area capacity, including concurrent booking requests and ordered service stages.
- Apply the same backend scheduling rules to customer bookings, owner-entered phone bookings, and walk-ins.
- Follow the current SRS and relevant GitHub tasks. Keep implementation scope and acceptance criteria consistent with them.
- Do not finalize product decisions by assuming requirements that have not been explicitly defined. Identify unresolved decisions and obtain clarification before implementing behavior that depends on them.
- Do not add unnecessary features or expand the agreed scope. Automated notifications, advanced loyalty packages, employee analytics, and offline synchronization remain outside the initial MVP unless explicitly approved.
- Treat pilot-business prices, durations, capacities, and policies as configurable examples rather than universal hard-coded rules.

## Development Workflow

- Track work through GitHub Issues and the Kanban columns **TODO**, **DOING**, and **DONE**. Board status must reflect actual progress.
- The first development task is **Issue #9: Flutter Project Setup and Authentication Screens**. Review its current description and acceptance criteria before implementation.
- Make small, testable changes tied to the relevant issue and acceptance criteria.
- Explain significant architectural changes and request user approval before implementing them.
- Do not push to GitHub without the user's explicit permission.
- Do not delete existing files without the user's explicit permission.
- At the end of each task, summarize changed files and tests or checks performed. Clearly state when tests were not run and why.
- Suggest appropriate commit messages that reference the relevant issue. Do not imply that a suggested commit has already been created.
- Implement incrementally in line with the SRS: establish a secure owner-side end-to-end customer/vehicle flow before adding customer self-service booking.

## Academic Context

WashFlow is a university graduation project. Thesis work, software development, and the GitHub Kanban board must progress together. Account for the supervisor's reporting and documentation requirements.

Maintain traceability between requirements, GitHub Issues, implementation, tests, and thesis evidence. Report actual results accurately, and keep scope changes consistent across the SRS, GitHub tasks, and thesis documentation.
