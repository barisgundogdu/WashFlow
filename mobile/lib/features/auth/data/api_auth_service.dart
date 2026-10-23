import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;

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
class ApiAuthService implements AuthService, SessionService {
  ApiAuthService({
    required this.baseUrl,
    required this.store,
    http.Client? client,
    this.timeout = const Duration(seconds: 15),
    DateTime Function()? now,
  }) : _client = client ?? http.Client(),
       _now = now ?? DateTime.now {
    if (!baseUrl.hasAuthority ||
        baseUrl.userInfo.isNotEmpty ||
        baseUrl.hasQuery ||
        baseUrl.hasFragment ||
        (baseUrl.scheme != 'https' &&
            !(kDebugMode && baseUrl.scheme == 'http'))) {
      throw ArgumentError('API URL must use HTTPS (HTTP is debug-only).');
    }
  }
  final Uri baseUrl;
  final TokenStore store;
  final http.Client _client;
  final Duration timeout;
  final DateTime Function() _now;
  _Tokens? _tokens;
  Future<void> _tail = Future.value();

  Future<AuthResult<T>> _safe<T>(Future<T> Function() action) async {
    try {
      return AuthSuccess(await action());
    } on _SafeFailure catch (e) {
      return AuthFailure(AuthError(code: e.code));
    } on TimeoutException {
      return AuthFailure(AuthError(code: AuthErrorCode.timeout));
    } on http.ClientException {
      return AuthFailure(AuthError(code: AuthErrorCode.network));
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
    final request = http.Request(
      'POST',
      Uri.parse(
        '${baseUrl.toString().replaceFirst(RegExp(r"/+$"), "")}/api/v1/auth/$path',
      ),
    )..followRedirects = false;
    request.headers['Content-Type'] = 'application/json';
    if (access != null) request.headers['Authorization'] = 'Bearer $access';
    if (body != null) request.body = jsonEncode(body);
    final response = await (() async {
      final streamed = await _client.send(request);
      return http.Response.fromStream(streamed);
    })().timeout(timeout);
    if (response.statusCode != expected) {
      // Never display arbitrary server text, including HTML/proxy responses.
      String? code;
      try {
        code = (jsonDecode(response.body) as Map)['code'] as String?;
      } catch (_) {}
      throw _SafeFailure(switch (response.statusCode) {
        401 => AuthErrorCode.invalidCredentials,
        429 => AuthErrorCode.rateLimited,
        400 when code == 'invalidVerificationToken' =>
          AuthErrorCode.invalidVerificationToken,
        400 => AuthErrorCode.validation,
        503 => AuthErrorCode.unavailable,
        _ => AuthErrorCode.unexpected,
      });
    }
    return expected == 204
        ? {}
        : jsonDecode(response.body) as Map<String, dynamic>;
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

  /// Future protected API adapters use this boundary and still enforce server ACLs.
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
