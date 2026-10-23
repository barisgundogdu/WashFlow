# Owner features

`OwnerHomeScreen` is a clearly labeled temporary destination reached only through fresh
server-authorized context selection. It implements no business operations or invitations.
Tenant-local customer/vehicle operations remain planned under SRS milestone A.
The authenticated shell owns the business context and clears the entire nested navigator
on refresh or context switch. Future business requests must independently enforce backend ACLs.
