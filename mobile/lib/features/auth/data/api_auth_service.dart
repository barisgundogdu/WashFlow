import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:flutter/foundation.dart';

import '../../../core/network/api_client.dart';
import '../domain/auth_service.dart';
import '../domain/session_service.dart';
import 'token_store.dart';

class _SafeFailure implements Exception {
  const _SafeFailure(this.code);
  final AuthErrorCode code;
}

class _Tokens {
  _Tokens(Map<String, dynamic> json)
    : access = json['accessToken'] as String,
      refresh = json['refreshToken'] as String,
      accessExpiry = DateTime.parse(json['accessTokenExpiresAt'] as String),
      sessionExpiry = DateTime.parse(json['sessionExpiresAt'] as String) {
    if (access.isEmpty ||
        refresh.isEmpty ||
        json['tokenType'] != 'Bearer' ||
        accessExpiry.isAfter(sessionExpiry)) {
      throw const FormatException();
    }
  }
  final String access, refresh;
  final DateTime accessExpiry, sessionExpiry;
  Map<String, dynamic> toJson() => {
    'accessToken': access,
    'refreshToken': refresh,
    'tokenType': 'Bearer',
    'accessTokenExpiresAt': accessExpiry.toUtc().toIso8601String(),
    'sessionExpiresAt': sessionExpiry.toUtc().toIso8601String(),
  };
}

/// No credential logging, JWT role inference, automatic POST retries or redirects.
/// All session mutations are serialized: a rotated token is never reused.
class ApiAuthService extends ChangeNotifier
    implements
        AuthService,
        SessionService,
        AccessTokenProvider,
        SessionIdentity {
  ApiAuthService({
    required this.baseUrl,
    required this.store,
    http.Client? client,
    this.timeout = const Duration(seconds: 15),
    DateTime Function()? now,
  }) : apiClient = ApiClient(
         baseUrl: baseUrl,
         client: client,
         timeout: timeout,
       ),
       _now = now ?? DateTime.now;
  final Uri baseUrl;
  final TokenStore store;

  /// Reuse this transport for protected adapters with this session's origin.
  final ApiClient apiClient;
  final Duration timeout;
  final DateTime Function() _now;
  _Tokens? _tokens;
  Future<void> _tail = Future.value();
  int _sessionRevision = 0;
  @override
  int get sessionRevision => _sessionRevision;

  void _changeIdentity() {
    _sessionRevision++;
    notifyListeners();
  }

  Future<AuthResult<T>> _safe<T>(Future<T> Function() action) async {
    try {
      return AuthSuccess(await action());
    } on _SafeFailure catch (e) {
      return AuthFailure(AuthError(code: e.code));
    } on ApiException catch (e) {
      return AuthFailure(
        AuthError(
          code: switch (e.code) {
            // Login failures retain the existing credential/verification message.
            ApiErrorCode.unauthenticated => AuthErrorCode.invalidCredentials,
            ApiErrorCode.forbidden => AuthErrorCode.forbidden,
            ApiErrorCode.conflict => AuthErrorCode.conflict,
            ApiErrorCode.validation => AuthErrorCode.validation,
            ApiErrorCode.invalidVerificationToken =>
              AuthErrorCode.invalidVerificationToken,
            ApiErrorCode.rateLimited => AuthErrorCode.rateLimited,
            ApiErrorCode.unavailable => AuthErrorCode.unavailable,
            ApiErrorCode.network => AuthErrorCode.network,
            ApiErrorCode.timeout => AuthErrorCode.timeout,
            ApiErrorCode.unexpected => AuthErrorCode.unexpected,
          },
        ),
      );
    } catch (_) {
      return AuthFailure(AuthError(code: AuthErrorCode.unexpected));
    }
  }

  Future<AuthResult<T>> _serial<T>(Future<T> Function() action) {
    final result = _tail.then((_) => _safe(action));
    _tail = result.then((_) {});
    return result;
  }

  Future<Map<String, dynamic>> _post(
    String path,
    Map<String, dynamic>? body, {
    String? access,
    int expected = 200,
  }) async {
    final json = await apiClient.post(
      '/api/v1/auth/$path',
      body,
      access: access,
      expected: expected,
    );
    return expected == 204 ? {} : json as Map<String, dynamic>;
  }

  Future<void> _save(_Tokens tokens) async {
    try {
      await store.write(jsonEncode(tokens.toJson()));
    } catch (_) {
      _tokens = null;
      try {
        await store.clear();
      } catch (_) {}
      throw const _SafeFailure(AuthErrorCode.storage);
    }
    _tokens = tokens;
  }

  Future<void> _clear() async {
    _tokens = null;
    try {
      await store.clear();
    } catch (_) {
      throw const _SafeFailure(AuthErrorCode.storage);
    }
  }

  @override
  Future<AuthResult<LoginResult>> login(LoginRequest request) =>
      _serial(() async {
        _changeIdentity();
        await _clear();
        final tokens = _Tokens(
          await _post('login', {
            'email': request.email.trim(),
            'password': request.password,
          }),
        );
        await _save(tokens);
        return const LoginResult();
      });

  @override
  Future<AuthResult<CustomerRegistrationResult>> registerCustomer(
    CustomerRegistrationRequest request,
  ) => _safe(() async {
    await _post('customers/register', {
      'fullName': request.fullName.trim(),
      'email': request.email.trim(),
      'password': request.password,
    }, expected: 202);
    return const CustomerRegistrationResult();
  });

  @override
  Future<AuthResult<BusinessRegistrationResult>> registerBusiness(
    BusinessRegistrationRequest request,
  ) => _safe(() async {
    await _post('businesses/register', {
      'businessName': request.businessName.trim(),
      'ownerFullName': request.ownerFullName.trim(),
      'email': request.email.trim(),
      'password': request.password,
    }, expected: 202);
    return const BusinessRegistrationResult();
  });

  Future<void> _rotate() async {
    final old = _tokens;
    if (old == null || !old.sessionExpiry.isAfter(_now())) {
      await _clear();
      throw const _SafeFailure(AuthErrorCode.invalidCredentials);
    }
    // Clear persisted predecessor BEFORE sending. On uncertain delivery never retry
    // it: the server may have consumed it and replay would revoke the family.
    await _clear();
    await _save(_Tokens(await _post('refresh', {'refreshToken': old.refresh})));
  }

  @override
  Future<AuthResult<bool>> restoreSession() => _serial(() async {
    _changeIdentity();
    String? raw;
    try {
      raw = await store.read();
    } catch (_) {
      throw const _SafeFailure(AuthErrorCode.storage);
    }
    if (raw == null) return false;
    try {
      _tokens = _Tokens(jsonDecode(raw) as Map<String, dynamic>);
    } catch (_) {
      await _clear();
      return false;
    }
    await _rotate(); // Server validates revocation even when access has not expired.
    return true;
  });

  @override
  Future<AuthResult<void>> refreshSession() => _serial(() async {
    if (_tokens == null) {
      throw const _SafeFailure(AuthErrorCode.invalidCredentials);
    }
    if (!_tokens!.accessExpiry.isAfter(
      _now().add(const Duration(seconds: 30)),
    )) {
      await _rotate();
    }
  });

  /// Protected API adapters use this boundary and still enforce server ACLs.
  @override
  Future<AuthResult<String>> accessToken() => _serial(() async {
    if (_tokens == null) {
      throw const _SafeFailure(AuthErrorCode.invalidCredentials);
    }
    if (!_tokens!.accessExpiry.isAfter(
      _now().add(const Duration(seconds: 30)),
    )) {
      await _rotate();
    }
    return _tokens!.access;
  });

  @override
  Future<AuthResult<void>> logout() => _serial(() async {
    _changeIdentity();
    try {
      if (_tokens != null) {
        if (!_tokens!.accessExpiry.isAfter(_now())) await _rotate();
        await _post('logout', null, access: _tokens!.access, expected: 204);
      }
    } finally {
      await _clear();
    }
  });

  @override
  Future<AuthResult<void>> verifyEmail(String token) => _safe(() async {
    await _post('email/verify', {'token': token.trim()});
  });

  @override
  Future<AuthResult<void>> resendVerification(String email) => _safe(() async {
    await _post('email/resend', {'email': email.trim()}, expected: 202);
  });
}
