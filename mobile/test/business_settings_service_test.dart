import 'dart:async';
import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/core/network/api_client.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/business_settings/data/api_business_settings_service.dart';
import 'package:washflow/features/business_settings/domain/business_settings.dart';

import 'support/settings_fakes.dart';
import 'support/session_fakes.dart';
import 'support/redemption_fakes.dart';

void main() {
  test('parse valid persisted baseline', () {
    expect(
      BusinessSettings.fromJson({
        'businessId': businessA,
        ...settings().toJson(),
      }).fields['name'],
      'İşletme A',
    );
  });
  test('real settings GET/POST contracts body version scoped bearer and no retries', () async {
    final requests = <http.Request>[];
    final service = ApiBusinessSettingsService(
      session: RedemptionSession()..signedIn = true,
      apiClient: ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((r) async {
          requests.add(r);
          return http.Response(
            jsonEncode({'businessId': businessA, ...settings().toJson()}),
            200,
            headers: {'content-type': 'application/json; charset=utf-8'},
          );
        }),
      ),
    );
    expect(await service.read(businessA), isA<AuthSuccess<BusinessSettings>>());
    expect(
      await service.save(settings()),
      isA<AuthSuccess<BusinessSettings>>(),
    );
    expect(requests.map((r) => r.method), ['GET', 'POST']);
    for (final r in requests) {
      expect(r.url.path, '/api/v1/businesses/$businessA/settings');
      expect(r.headers['authorization'] != null, isTrue);
      expect(r.followRedirects, isFalse);
    }
    final body = jsonDecode(requests.last.body) as Map;
    expect(body['version'], 1);
    expect(body.containsKey('businessId'), isFalse);
    expect(body['weeklyHours'], isNull);
  });
  for (final pair in {
    400: AuthErrorCode.validation,
    401: AuthErrorCode.unauthenticated,
    403: AuthErrorCode.forbidden,
    404: AuthErrorCode.notFound,
    409: AuthErrorCode.conflict,
    429: AuthErrorCode.rateLimited,
  }.entries) {
    test('${pair.key} safely mapped', () async {
      int calls = 0;
      final s = ApiBusinessSettingsService(
        session: RedemptionSession()..signedIn = true,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((r) async {
            calls++;
            return http.Response('private body', pair.key);
          }),
        ),
      );
      final result = await s.save(settings());
      expect((result as AuthFailure).error.code, pair.value);
      expect(calls, 1);
    });
  }
  test('reject malformed and cross-business response payloads', () async {
    for (final json in [
      {},
      {'businessId': businessB, ...settings().toJson()},
      {
        'businessId': businessA,
        ...settings().toJson(),
        'weeklyHours': [
          {'day': 1, 'closed': true, 'opensAtMinute': 100},
        ],
      },
    ]) {
      final s = ApiBusinessSettingsService(
        session: RedemptionSession()..signedIn = true,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((r) async => http.Response(jsonEncode(json), 200)),
        ),
      );
      expect(await s.read(businessA), isA<AuthFailure<BusinessSettings>>());
    }
  });
  test('timeout and network do not retry writes', () async {
    for (final timeout in [false, true]) {
      int calls = 0;
      final p = Completer<http.Response>();
      final s = ApiBusinessSettingsService(
        session: RedemptionSession()..signedIn = true,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          timeout: const Duration(milliseconds: 5),
          client: MockClient((r) async {
            calls++;
            if (timeout) return p.future;
            throw http.ClientException('private');
          }),
        ),
      );
      expect(await s.save(settings()), isA<AuthFailure<BusinessSettings>>());
      expect(calls, 1);
      if (timeout) p.complete(http.Response('{}', 200));
    }
  });
  test('unknown tenant credential and day contracts fail safely', () {
    expect(
      () => BusinessSettings.fromJson({
        'businessId': businessA,
        ...settings().toJson(),
        'version': 0,
      }),
      throwsFormatException,
    );
    expect(
      () => OpeningDay.fromJson({
        'day': 1,
        'closed': false,
        'opensAtMinute': 1000,
        'closesAtMinute': 500,
      }),
      throwsFormatException,
    );
  });
}
