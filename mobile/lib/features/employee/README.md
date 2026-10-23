# Employee features

`EmployeeHomeScreen` is a clearly labeled temporary destination reached only through fresh
server-authorized context selection. Assigned-work operations remain unimplemented.
Future APIs must restrict updates to assigned work and prevent management or financial access.
Owner and Employee grants in one membership produce separate selectable experiences.


Phase 4 recipient onboarding lives in `features/invitations`: manual credential entry,
invitation-specific registration, shared email verification/login and explicit acceptance.
It reloads server contexts rather than inferring or creating roles locally. No Customer profile
or business is automatically created. This does not implement assigned-work functionality.
