# Future API integration boundary

Place shared transport configuration and HTTP infrastructure here when API contracts are approved. No network client, endpoint, credential storage, or backend call is implemented yet.

Feature-specific repository contracts belong in each feature's domain layer; ASP.NET Core adapters and DTO mapping belong in its data layer. Inject implementations at the app composition root. Select an HTTP package only when integration begins.

The server must enforce tenant membership, roles, and customer ownership on every protected operation. Navigation and client-supplied context never grant authorization. Do not infer account links from names or plates.
