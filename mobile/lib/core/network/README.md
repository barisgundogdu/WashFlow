# Network boundary

Authentication transport currently lives in
`features/auth/data/api_auth_service.dart`, using an injected HTTP client,
bounded timeout and safe semantic errors. API origin is supplied through
`--dart-define=API_BASE_URL=...`.

Future protected API adapters must obtain a current token through the serialized
`ApiAuthService.accessToken()` boundary, handle server 401 safely, and never infer
tenant access from client-selected roles. No business APIs exist in this phase.
