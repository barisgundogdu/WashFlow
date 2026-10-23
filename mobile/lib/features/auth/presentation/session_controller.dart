import 'package:flutter/foundation.dart';

import '../domain/auth_service.dart';
import '../domain/context_service.dart';
import '../domain/session_service.dart';
import '../domain/user_contexts.dart';

enum ExperienceRole { customer, owner, employee }

enum SessionPhase { loading, ready, failed, loggingOut, signedOut }

/// Constructed exclusively from a fresh server context snapshot.
class AuthorizedExperience {
  const AuthorizedExperience._(this.id, this.role, this.business);
  final String id;
  final ExperienceRole role;
  final BusinessContext? business;
  String get label => switch (role) {
    ExperienceRole.customer => 'Müşteri',
    ExperienceRole.owner => '${business!.businessName} · İşletme sahibi',
    ExperienceRole.employee => '${business!.businessName} · Çalışan',
  };

  static List<AuthorizedExperience> fromContexts(UserContexts contexts) => [
    if (contexts.customerContext case final customer?)
      AuthorizedExperience._(
        'customer:${customer.customerProfileId}',
        ExperienceRole.customer,
        null,
      ),
    for (final business in contexts.businessContexts) ...[
      if (business.roles.contains(BusinessRole.owner))
        AuthorizedExperience._(
          '${business.businessId}:${business.membershipId}:owner',
          ExperienceRole.owner,
          business,
        ),
      if (business.roles.contains(BusinessRole.employee))
        AuthorizedExperience._(
          '${business.businessId}:${business.membershipId}:employee',
          ExperienceRole.employee,
          business,
        ),
    ],
  ];
}

/// Owns only in-memory context/navigation state. Backend ACLs remain authoritative.
class SessionController extends ChangeNotifier {
  SessionController({required this.session, required this.contexts}) {
    if (session is SessionIdentity) {
      _identity = session as SessionIdentity;
      _identity!.addListener(_identityChanged);
    }
  }
  final SessionService? session;
  final ContextService? contexts;
  SessionIdentity? _identity;
  bool _disposed = false;
  bool _checking = false;
  bool _ending = false;
  int? _ownLogoutRevision;
  int _generation = 0;
  int get generation => _generation;
  SessionPhase _phase = SessionPhase.loading;
  SessionPhase get phase => _phase;
  List<AuthorizedExperience> _options = const [];
  List<AuthorizedExperience> get options => _options;
  AuthorizedExperience? _selected;
  AuthorizedExperience? get selected => _selected;
  String? _message;
  String? get message => _message;

  bool _current(int generation) => !_disposed && generation == _generation;
  void _clear(SessionPhase phase) {
    _generation++;
    _options = const [];
    _selected = null;
    _phase = phase;
    _message = null;
    notifyListeners();
  }

  void _identityChanged() {
    if (_disposed) return;
    // Ignore only this logout's identity signal, never a subsequent account change.
    if (_ending && _identity?.sessionRevision == _ownLogoutRevision) return;
    _ending = false;
    _clear(SessionPhase.signedOut);
  }

  Future<void> refresh({bool choose = false, String? requestedId}) async {
    if (_disposed || _ending || _phase == SessionPhase.signedOut) return;
    final preferred = choose ? null : requestedId ?? _selected?.id;
    _clear(SessionPhase.loading);
    final generation = _generation;
    AuthResult<UserContexts> result;
    try {
      result = contexts == null || session == null
          ? AuthFailure(AuthError(code: AuthErrorCode.unavailable))
          : await contexts!.getContexts();
    } catch (_) {
      result = AuthFailure(AuthError(code: AuthErrorCode.unexpected));
    }
    if (!_current(generation)) return;
    switch (result) {
      case AuthFailure<UserContexts>(:final error):
        _fail(error);
      case AuthSuccess<UserContexts>(:final value):
        _options = List.unmodifiable(AuthorizedExperience.fromContexts(value));
        _selected = null;
        if (preferred != null) {
          for (final option in _options) {
            if (option.id == preferred) _selected = option;
          }
        }
        if (requestedId != null && _selected == null) {
          _message = 'Seçilen erişim artık kullanılamıyor. Güncel seçeneklerden devam edin.';
        } else if (_selected == null && _options.length == 1) {
          _selected = _options.single;
        }
        _phase = SessionPhase.ready;
    }
    notifyListeners();
  }

  /// Selection is revalidated, even if the picker was populated moments ago.
  Future<void> select(String id, {required int generation}) async {
    if (!_current(generation) ||
        _phase != SessionPhase.ready ||
        !_options.any((option) => option.id == id)) {
      return;
    }
    await refresh(requestedId: id);
  }

  Future<void> switchContext() => refresh(choose: true);

  /// Call with the originating request generation, never a later context's epoch.
  Future<void> handleAuthorizationFailure(
    AuthError error, {
    required int generation,
  }) async {
    if (!_current(generation) || _ending) return;
    if (error.code == AuthErrorCode.forbidden) {
      await refresh();
    } else if (_unauthenticated(error)) {
      _clear(SessionPhase.signedOut);
    }
  }

  /// Hide content while backgrounded and invalidate every in-flight response.
  void suspend() {
    if (!_disposed && !_ending && _phase != SessionPhase.signedOut) {
      _clear(SessionPhase.loading);
    }
  }

  Future<void> checkSession() async {
    if (_disposed || _ending || _checking || _phase != SessionPhase.ready) {
      return;
    }
    _checking = true;
    final generation = _generation;
    try {
      final result = await session?.refreshSession();
      if (!_current(generation)) return;
      if (result case AuthFailure<void>(:final error)) {
        _clear(SessionPhase.failed);
        _fail(error);
        notifyListeners();
      }
    } catch (_) {
      if (_current(generation)) {
        _clear(SessionPhase.failed);
        _message = authErrorMessage(AuthError(code: AuthErrorCode.unexpected));
        notifyListeners();
      }
    } finally {
      _checking = false;
    }
  }

  bool _unauthenticated(AuthError error) =>
      error.code == AuthErrorCode.unauthenticated ||
      error.code == AuthErrorCode.invalidCredentials;
  void _fail(AuthError error) {
    _phase = _unauthenticated(error)
        ? SessionPhase.signedOut
        : SessionPhase.failed;
    _message = authErrorMessage(error);
  }

  Future<void> logout() async {
    if (_disposed || _ending) return;
    _ending = true;
    _ownLogoutRevision = _identity == null
        ? null
        : _identity!.sessionRevision + 1;
    _clear(SessionPhase.loggingOut);
    final generation = _generation;
    AuthResult<void>? result;
    try {
      result = await session?.logout();
    } catch (_) {
      result = AuthFailure(AuthError(code: AuthErrorCode.unexpected));
    }
    if (!_current(generation)) return;
    _ending = false;
    if (result case AuthFailure<void>(:final error)
        when error.code == AuthErrorCode.storage) {
      _phase = SessionPhase.failed;
      _message = authErrorMessage(error);
    } else {
      _phase = SessionPhase.signedOut;
      if (result is AuthFailure<void>) {
        _message = 'Yerel oturum kapatıldı. Sunucuda çıkış doğrulanamadı.';
      }
    }
    notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    _generation++;
    _identity?.removeListener(_identityChanged);
    super.dispose();
  }
}
