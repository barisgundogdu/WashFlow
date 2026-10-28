import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;

enum ApiErrorCode {
  unauthenticated,
  forbidden,
  notFound,
  invalidInvitation,
  conflict,
  validation,
  invalidVerificationToken,
  rateLimited,
  unavailable,
  network,
  timeout,
  unexpected,
}

/// Contains no request, response body, credential, or provider exception.
class ApiException implements Exception {
  const ApiException(this.code, {this.problemCode});
  final String? problemCode;
  final ApiErrorCode code;
}

/// JSON transport only. Session storage/rotation and authorization stay outside.
/// Requests never follow redirects or retry, including after a 401 or timeout.
class ApiClient {
  ApiClient({
    required this.baseUrl,
    http.Client? client,
    this.timeout = const Duration(seconds: 15),
  }) : _client = client ?? http.Client() {
    if (!baseUrl.hasAuthority ||
        baseUrl.host.isEmpty ||
        baseUrl.userInfo.isNotEmpty ||
        baseUrl.hasQuery ||
        baseUrl.hasFragment ||
        (baseUrl.scheme != 'https' &&
            !(kDebugMode && baseUrl.scheme == 'http'))) {
      throw ArgumentError('API URL must use HTTPS (HTTP is debug-only).');
    }
    if (timeout <= Duration.zero) {
      throw ArgumentError('API timeout must be positive.');
    }
  }

  final Uri baseUrl;
  final Duration timeout;
  final http.Client _client;

  Future<Object?> get(
    String path, {
    String? access,
    int expected = 200,
    Map<String, String>? query,
  }) => _request('GET', path, access: access, expected: expected, query: query);

  Future<Object?> put(
    String path,
    Map<String, dynamic> body, {
    String? access,
    int expected = 200,
  }) => _request('PUT', path, body: body, access: access, expected: expected);

  Future<Object?> post(
    String path,
    Map<String, dynamic>? body, {
    String? access,
    int expected = 200,
    Set<int> alsoAccept = const {},
  }) => _request(
    'POST',
    path,
    body: body,
    access: access,
    expected: expected,
    alsoAccept: alsoAccept,
  );

  Future<Object?> _request(
    String method,
    String path, {
    Map<String, dynamic>? body,
    String? access,
    required int expected,
    Set<int> alsoAccept = const {},
    Map<String, String>? query,
  }) async {
    try {
      // Accept API paths only, never caller-supplied origins or query credentials.
      if (!RegExp(r'^/api/v1(?:/[a-zA-Z0-9_-]+)+/?$').hasMatch(path)) {
        throw const ApiException(ApiErrorCode.unexpected);
      }
      if (query != null &&
          query.keys.any(
            (key) => !const {
              'q',
              'page',
              'pageSize',
              'status',
              'fromDate',
              'toDateExclusive',
              'fromUtc',
              'toUtcExclusive',
              'category',
            }.contains(key),
          )) {
        throw const ApiException(ApiErrorCode.unexpected);
      }
      final prefix = baseUrl.path.replaceFirst(RegExp(r'/+$'), '');
      final request = http.Request(
        method,
        baseUrl.replace(path: '$prefix$path', queryParameters: query),
      )..followRedirects = false;
      request.headers['Accept'] = 'application/json';
      if (method == 'POST' || method == 'PUT') {
        request.headers['Content-Type'] = 'application/json';
      }
      if (access != null) request.headers['Authorization'] = 'Bearer $access';
      if (body != null) request.body = jsonEncode(body);
      final response = await (() async {
        final streamed = await _client.send(request);
        return http.Response.fromStream(streamed);
      })().timeout(timeout);
      if (response.statusCode != expected &&
          !alsoAccept.contains(response.statusCode)) {
        String? code;
        try {
          code = (jsonDecode(response.body) as Map)['code'] as String?;
        } catch (_) {
          // Arbitrary server/proxy text is never carried into an error.
        }
        throw ApiException(
          switch (response.statusCode) {
            401 => ApiErrorCode.unauthenticated,
            403 => ApiErrorCode.forbidden,
            404 => ApiErrorCode.notFound,
            400 when code == 'invalidInvitation' =>
              ApiErrorCode.invalidInvitation,
            409 => ApiErrorCode.conflict,
            429 => ApiErrorCode.rateLimited,
            400 when code == 'invalidVerificationToken' =>
              ApiErrorCode.invalidVerificationToken,
            400 => ApiErrorCode.validation,
            503 => ApiErrorCode.unavailable,
            _ => ApiErrorCode.unexpected,
          },
          problemCode:
              const {
                'resourceConflict',
                'scheduleConflict',
                'idempotencyConflict',
                'invalidStatus',
                'areaInUse',
                'capacityInUse',
                'availabilityInUse',
                'requirementInUse',
                'unsupportedStages',
                'serviceUnavailable',
                'areaUnavailable',
                'employeeUnavailable',
                'outsideHours',
                'hoursUnconfigured',
                'requirementChanged',
                'invalidLocalTime',
                'ambiguousLocalTime',
                'invalidEmployees',
              }.contains(code)
              ? code
              : null,
        );
      }
      if (expected == 204) return null;
      final json = jsonDecode(response.body);
      if (json is! Map<String, dynamic> && json is! List) {
        throw const ApiException(ApiErrorCode.unexpected);
      }
      return json;
    } on ApiException {
      rethrow;
    } on TimeoutException {
      throw const ApiException(ApiErrorCode.timeout);
    } on http.ClientException {
      throw const ApiException(ApiErrorCode.network);
    } catch (_) {
      throw const ApiException(ApiErrorCode.unexpected);
    }
  }
}
