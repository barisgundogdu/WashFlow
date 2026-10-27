import 'dart:async';
import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/core/network/api_client.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/customer_vehicles/domain/customer_vehicles.dart';
import 'package:washflow/features/service_catalog/data/api_catalog_service.dart';
import 'package:washflow/features/service_catalog/domain/service_catalog.dart';

import 'support/catalog_fakes.dart';
import 'support/redemption_fakes.dart';
import 'support/session_fakes.dart';

http.Response response(Object raw, [int code = 200]) => http.Response(
  jsonEncode(raw),
  code,
  headers: {'content-type': 'application/json; charset=utf-8'},
);
void main() {
  test(
    'text validation uses NFC storage limits and preserves Turkish text',
    () {
      expect(CatalogText.nameError('\u0344' * 100), isNull);
      expect(CatalogText.nameError('\u0344' * 101), contains('200'));
      expect(CatalogText.descriptionError('\u0344' * 1000), isNull);
      expect(CatalogText.descriptionError('\u0344' * 1001), contains('2000'));
      expect(CatalogText.nameError('  ${'I\u0307' * 200}  '), isNull);
      final d = CatalogDraft(
        name: '  I\u0307c\u0327 yıkama  ',
        description: '\tAc\u0327ıklama\n',
      );
      expect(d.validate(), isNull);
      expect(d.toJson()['name'], 'İç yıkama');
      expect(d.toJson()['description'], 'Açıklama');
      expect(CatalogText.descriptionError('Satır\nİkinci\r\n\tSatır'), isNull);
      // FEFF is not .NET whitespace and must not be silently trimmed by Dart.
      expect(CatalogText.nameError('\ufeff'), isNull);
      expect(CatalogText.normalize('\ufeff'), '\ufeff');
    },
  );
  for (final value in [
    'Yıkama 🚗',
    'Yıkama\u0085',
    '\tYıkama',
    'Yıkama\ud800',
    'Yıkama\u009f',
  ]) {
    test('unsupported name rejected locally ${value.codeUnits}', () {
      expect(CatalogText.nameError(value), isNotNull);
      expect(CatalogDraft(name: value).validate(), isNotNull);
    });
  }
  for (final value in [
    'Açıklama 🚗',
    'Açıklama\u0085',
    'Açıklama\u009f',
    'Açıklama\u000b',
    'Açıklama\udfff',
  ]) {
    test('unsupported description rejected locally ${value.codeUnits}', () {
      expect(CatalogText.descriptionError(value), isNotNull);
    });
  }
  test('unsupported text never reaches HTTP', () async {
    var calls = 0;
    final s = ApiCatalogService(
      session: RedemptionSession()..signedIn = true,
      apiClient: ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((_) async {
          calls++;
          return response(catalogJson(), 201);
        }),
      ),
    );
    for (final draft in [
      CatalogDraft(name: 'Yıkama 🚗'),
      CatalogDraft(name: 'Yıkama', description: '\u0344' * 1001),
    ]) {
      final result =
          await s.save(businessA, draft) as AuthFailure<CatalogServiceRecord>;
      expect(result.error.code, AuthErrorCode.validation);
    }
    expect(calls, 0);
  });
  test('exact Turkish currency round trip and estimate', () {
    expect(Lira.parse('9999999.99').kurus, 999999999);
    expect(Lira.parse('450,10', turkish: true).wire, '450.10');
    expect(Lira.parse('450.10').display, '450,10 TL');
    expect(Lira.parse('1000').display, '1.000,00 TL');
    expect(Lira.parse('0').wire, '0.00');
    expect(
      catalogRecord().offerings.single.estimate,
      contains('120 dk bekleme'),
    );
  });
  for (final value in ['1.001', '-1', '10000000', '1e2', '01', '1,000', '']) {
    test(
      'rejects invalid exact money format $value',
      () => expect(() => Lira.parse(value), throwsFormatException),
    );
  }
  test('draft missing category never implies free; duplicate is inactive new draft', () {
    final d = CatalogDraft(name: 'Test', enabled: true);
    expect(d.validate(), isNotNull);
    final copy = catalogRecord().draft(duplicate: true);
    expect(copy.id, isNull);
    expect(copy.version, isNull);
    expect(copy.enabled, isFalse);
    expect(copy.published, isFalse);
    expect(copy.offerings.single.price.wire, '450.10');
  });
  test('unknown categories currencies duplicate rows and malformed versions rejected', () {
    for (final key in ['category', 'currency']) {
      final j = catalogJson();
      (j['offerings'] as List)[0][key] = 'unknown';
      expect(() => CatalogServiceRecord.fromJson(j), throwsFormatException);
    }
    final j = catalogJson();
    j['version'] = 0;
    expect(() => CatalogServiceRecord.fromJson(j), throwsFormatException);
    final d = catalogRecord().draft();
    d.offerings = [...d.offerings, ...d.offerings];
    expect(d.validate(), isNotNull);
  });
  test(
    'API list PUT POST archive use correct scope and exact JSON with no retry',
    () async {
      final requests = <http.Request>[];
      final s = ApiCatalogService(
        session: RedemptionSession()..signedIn = true,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((r) async {
            requests.add(r);
            if (r.url.path.endsWith('/archive')) return http.Response('', 204);
            if (r.method == 'GET') {
              return response({
                'items': [catalogJson()],
                'page': 1,
                'pageSize': 20,
                'hasMore': false,
              });
            }
            return response(
              catalogJson(version: 2),
              r.method == 'POST' ? 201 : 200,
            );
          }),
        ),
      );
      expect(
        await s.list(businessA, query: 'İç + %', status: 'enabled'),
        isA<AuthSuccess<RecordPage<CatalogServiceRecord>>>(),
      );
      expect(
        await s.save(businessA, catalogRecord().draft()),
        isA<AuthSuccess<CatalogServiceRecord>>(),
      );
      expect(
        await s.save(businessA, catalogRecord().draft(duplicate: true)),
        isA<AuthSuccess<CatalogServiceRecord>>(),
      );
      expect(
        await s.archive(businessA, catalogRecord()),
        isA<AuthSuccess<void>>(),
      );
      expect(requests.length, 4);
      expect(requests.first.url.queryParameters['q'], 'İç + %');
      expect(requests.first.url.queryParameters['status'], 'enabled');
      expect(requests[1].method, 'PUT');
      expect(jsonDecode(requests[1].body)['offerings'][0]['price'], '450.10');
      expect(
        requests.every(
          (r) => r.headers['authorization'] != null && !r.followRedirects,
        ),
        isTrue,
      );
    },
  );
  for (final entry in {
    400: AuthErrorCode.validation,
    401: AuthErrorCode.unauthenticated,
    403: AuthErrorCode.forbidden,
    404: AuthErrorCode.notFound,
    409: AuthErrorCode.conflict,
    429: AuthErrorCode.rateLimited,
    503: AuthErrorCode.unavailable,
  }.entries) {
    test('safe HTTP mapping ${entry.key} no mutation retries', () async {
      var calls = 0;
      final s = ApiCatalogService(
        session: RedemptionSession()..signedIn = true,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((_) async {
            calls++;
            return response({'detail': 'private provider text'}, entry.key);
          }),
        ),
      );
      final r = await s.save(
        businessA,
        catalogRecord().draft(),
      ) as AuthFailure<CatalogServiceRecord>;
      expect(r.error.code, entry.value);
      expect(calls, 1);
    });
  }
  test('cross tenant response rejected', () async {
    final s = ApiCatalogService(
      session: RedemptionSession()..signedIn = true,
      apiClient: ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient(
          (_) async => response(catalogJson(business: businessB)),
        ),
      ),
    );
    expect(
      await s.read(businessA, serviceId),
      isA<AuthFailure<CatalogServiceRecord>>(),
    );
  });
  test('account changes discard in-flight responses', () async {
    final pending = Completer<http.Response>();
    final session = RedemptionSession()..signedIn = true;
    final s = ApiCatalogService(
      session: session,
      apiClient: ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((_) => pending.future),
      ),
    );
    final future = s.read(businessA, serviceId);
    await Future<void>.delayed(Duration.zero);
    await session.logout();
    pending.complete(response(catalogJson()));
    expect(await future, isA<AuthFailure<CatalogServiceRecord>>());
  });
  test('network and timeout do not retry', () async {
    for (final timeout in [false, true]) {
      var calls = 0;
      final s = ApiCatalogService(
        session: RedemptionSession()..signedIn = true,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          timeout: const Duration(milliseconds: 2),
          client: MockClient((_) async {
            calls++;
            if (timeout) {
              await Future<void>.delayed(const Duration(milliseconds: 10));
              return response(catalogJson());
            }
            throw http.ClientException('private');
          }),
        ),
      );
      final r = await s.save(
        businessA,
        catalogRecord().draft(),
      ) as AuthFailure<CatalogServiceRecord>;
      expect(
        r.error.code,
        timeout ? AuthErrorCode.timeout : AuthErrorCode.network,
      );
      expect(calls, 1);
    }
  });
}
