# Network boundary — Flutter integration Phase 1

`ApiClient` owns JSON GET/POST transport with an injected `http.Client`, validated API origin,
deployment prefix support, a bounded whole-response timeout, and safe semantic errors. It supports
object/list JSON and empty 204 responses. API paths cannot change the origin or contain query
credentials. Redirects and automatic retries are disabled. It never logs requests, credentials,
provider exceptions or response bodies. Origin comes from `--dart-define=API_BASE_URL=...`;
HTTPS is required outside debug builds.

`ApiAuthService` uses this transport and remains the sole session-storage/rotation owner.
Its `AccessTokenProvider.accessToken()` implementation serializes session mutations and clears
the predecessor before refresh. Protected adapters must use this boundary, not read secure storage
or rotate/replay refresh credentials themselves.

Compose context reads with the same transport and authenticated session:

```dart
final contexts = ApiContextService(apiClient: auth.apiClient, session: auth);
final result = await contexts.getContexts();
```

Phase 2 wires this composition in `main.dart` and injects it into the authenticated shell. Pair transport
and session from the same configured API; never use an untrusted/user-provided transport origin.
`ApiContextService` GETs `/api/v1/auth/contexts` with the returned bearer credential. It has no cache,
JWT decoding, role headers, user overrides, or local grant fallback. 401 yields `unauthenticated`;
403 yields `forbidden`. A context failure does not clear a possibly newer session or replay requests.
The authenticated `SessionController` handles these outcomes and discards stale responses
following logout, account changes or tenant switches. Future business adapters must report
authorization failures with the originating controller generation; 403 revalidates contexts and
401 exits the authenticated shell. Never attach an old request failure to a new controller.
Context snapshots never replace backend ACLs.


Phase 3 adds `ApiOwnerInvitationService` using the same transport/access-token provider and explicit
business-scoped URLs. It is stateless and never stores a global current tenant. Account revisions
are checked around asynchronous access-token acquisition so a queued old-account write cannot
acquire and dispatch with a new account's credential. An already-dispatched server write cannot
be rolled back merely by leaving a screen. Controllers discard its late UI result.

Transport now distinguishes 404 (`notFound`) and 400 `invalidInvitation`; the invitation adapter
maps these to safe feature messages. Existing authentication/context adapters retain their prior
safe unexpected-error behavior for these endpoint-inapplicable statuses. No raw response/detail
text is carried into error messages. Mutation retries remain disabled for every status and timeout.
