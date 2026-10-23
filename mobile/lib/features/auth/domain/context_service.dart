import 'auth_service.dart';
import 'user_contexts.dart';

abstract interface class ContextService {
  /// Loads current grants for the authenticated user. No local fallback/cache.
  Future<AuthResult<UserContexts>> getContexts();
}
