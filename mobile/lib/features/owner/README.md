# Owner features

The authenticated Owner home links to employee invitation management. Entry revalidates the
selected server Owner context. The screen is a nested route inside the current tenant navigator;
role/business/account changes and authorization failures dispose its state, forms and dialogs.
No root invitation route accepts a business or role supplied through route arguments.

Owner invitation APIs support metadata listing, creation by email, revocation with confirmation,
and resend. Success means a record was saved, not confirmed email delivery. Terminal rows disable
actions; expired, unaccepted/unrevoked rows may be resent. These are UI hints, not permissions:
every API call rechecks backend authorization/state. Mutation results, including errors/races, are
reconciled by GET without replaying the mutation. See the mobile README Phase 3 contract notes.

Owner home also exposes business settings (Phase 6) and **Müşteriler ve araçlar** (Phase 7), each
behind freshly validated server Owner context and private tenant navigation. Tenant-local customer
creation/search/edit/details and associated vehicle creation/search/edit/archive are implemented.
Personal Customer vehicles are a separate context/API. Other operational features remain planned;
recipient invitation redemption is implemented in the invitations feature, not this Owner module.
