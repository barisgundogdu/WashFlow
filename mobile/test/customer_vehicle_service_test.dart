import 'dart:convert';
import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/core/network/api_client.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/customer_vehicles/data/api_customer_vehicle_service.dart';
import 'package:washflow/features/customer_vehicles/domain/customer_vehicles.dart';

import 'support/customer_vehicle_fakes.dart';
import 'support/session_fakes.dart';
import 'support/redemption_fakes.dart';

http.Response json(Object value, [int code = 200]) => http.Response(
  jsonEncode(value),
  code,
  headers: {'content-type': 'application/json; charset=utf-8'},
);
void main() {
  test('GET safely encodes bounded search params and PUT uses versioned JSON bearer', () async {
    final requests = <http.Request>[];
    final s = ApiCustomerVehicleService(
      session: RedemptionSession()..signedIn = true,
      apiClient: ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((r) async {
          requests.add(r);
          return r.method == 'GET'
              ? json({
                  'items': [customerJson()],
                  'page': 1,
                  'pageSize': 20,
                  'hasMore': false,
                })
              : json(customerJson(version: 2));
        }),
      ),
    );
    expect(
      await s.customers(businessA, query: 'Ayşe + %'),
      isA<AuthSuccess<RecordPage<LocalCustomerRecord>>>(),
    );
    expect(
      await s.saveCustomer(
        businessA,
        CustomerDraft({'fullName': 'Changed'}, id: customerId, version: 1),
      ),
      isA<AuthSuccess<LocalCustomerRecord>>(),
    );
    expect(requests.first.url.queryParameters['q'], 'Ayşe + %');
    expect(requests.last.method, 'PUT');
    expect(
      requests.last.url.path,
      '/api/v1/businesses/$businessA/customers/$customerId',
    );
    expect((jsonDecode(requests.last.body) as Map)['version'], 1);
    expect(
      requests.every(
        (r) => r.headers['authorization'] != null && !r.followRedirects,
      ),
      isTrue,
    );
  });
  test(
    'owned endpoints never accept user/profile ids and archive uses version',
    () async {
      final requests = <http.Request>[];
      final s = ApiCustomerVehicleService(
        session: RedemptionSession()..signedIn = true,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((r) async {
            requests.add(r);
            if (r.url.path.endsWith('/archive')) return http.Response('', 204);
            return json(vehicleJson(business: null, customer: null), 201);
          }),
        ),
      );
      final v = await s.saveVehicle(
        VehicleDraft({
          'plate': '34 ABC123',
          'brand': 'Test',
          'model': 'Test',
          'category': 'Car',
        }),
      );
      expect(v, isA<AuthSuccess<VehicleRecord>>());
      await s.archiveVehicle((v as AuthSuccess<VehicleRecord>).value);
      expect(requests.first.url.path, '/api/v1/me/vehicles');
      final body = jsonDecode(requests.first.body) as Map;
      expect(
        body.containsKey('userId') ||
            body.containsKey('customerProfileId') ||
            body.containsKey('businessId'),
        isFalse,
      );
      expect(requests.last.url.path, '/api/v1/me/vehicles/$vehicleId/archive');
    },
  );
  for (final pair in {
    400: AuthErrorCode.validation,
    401: AuthErrorCode.unauthenticated,
    403: AuthErrorCode.forbidden,
    404: AuthErrorCode.notFound,
    409: AuthErrorCode.conflict,
    429: AuthErrorCode.rateLimited,
  }.entries) {
    test('${pair.key} safe error mapping no retry', () async {
      int count = 0;
      final s = ApiCustomerVehicleService(
        session: RedemptionSession()..signedIn = true,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((r) async {
            count++;
            return http.Response('private response', pair.key);
          }),
        ),
      );
      final r = await s.saveCustomer(
        businessA,
        CustomerDraft({'fullName': 'Valid'}),
      );
      expect((r as AuthFailure).error.code, pair.value);
      expect(count, 1);
    });
  }
  test(
    'foreign tenant and ambiguous vehicle ownership payloads rejected',
    () async {
      for (final foreign in [true, false]) {
        final s = ApiCustomerVehicleService(
          session: RedemptionSession()..signedIn = true,
          apiClient: ApiClient(
            baseUrl: Uri.parse('https://example.test'),
            client: MockClient(
              (r) async => json({
                'items': foreign
                    ? [customerJson(business: businessB)]
                    : [vehicleJson(business: null)],
                'page': 1,
                'pageSize': 20,
                'hasMore': false,
              }),
            ),
          ),
        );
        expect(
          foreign ? await s.customers(businessA) : await s.vehicles(),
          isA<AuthFailure>(),
        );
      }
    },
  );
  test('timeouts and network failures dispatch each write once', () async {
    for (final timeout in [false, true]) {
      int calls = 0;
      final p = Completer<http.Response>();
      final s = ApiCustomerVehicleService(
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
      expect(
        await s.saveCustomer(businessA, CustomerDraft({'fullName': 'Valid'})),
        isA<AuthFailure>(),
      );
      expect(calls, 1);
      if (timeout) p.complete(json(customerJson(), 201));
    }
  });
  test('local form validation and category/year limits', () {
    expect(validateCustomer({'fullName': ''}), isNotNull);
    expect(validateCustomer({'fullName': 'Valid', 'phone': 'bad'}), isNotNull);
    expect(
      validateVehicle({
        'plate': '34 ABC123',
        'brand': 'Test',
        'model': 'Test',
        'category': 'Other',
        'modelYear': '3000',
      }),
      isNotNull,
    );
    expect(
      validateVehicle({
        'plate': 'ab-123-cd',
        'brand': 'Test',
        'model': 'Test',
        'category': 'Other',
      }),
      isNull,
    );
  });
  test('transport rejects arbitrary credential query keys', () async {
    int calls = 0;
    final api = ApiClient(
      baseUrl: Uri.parse('https://example.test'),
      client: MockClient((r) async {
        calls++;
        return json({});
      }),
    );
    await expectLater(
      api.get('/api/v1/me/vehicles', query: {'token': 'synthetic'}),
      throwsA(isA<ApiException>()),
    );
    expect(calls, 0);
  });

  test(
    'account revision changes during token acquisition prevent dispatch',
    () async {
      final session = RedemptionSession()..signedIn = true;
      int calls = 0;
      session.onAccess = () async {
        session.changeIdentity();
        return const AuthSuccess('synthetic');
      };
      final service = ApiCustomerVehicleService(
        session: session,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((r) async {
            calls++;
            return json({});
          }),
        ),
      );
      expect(await service.vehicles(), isA<AuthFailure>());
      expect(calls, 0);
    },
  );
  test(
    'account revision changes after HTTP cannot return previous personal data',
    () async {
      final session = RedemptionSession()..signedIn = true;
      final service = ApiCustomerVehicleService(
        session: session,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((r) async {
            session.changeIdentity();
            return json({
              'items': [vehicleJson(business: null, customer: null)],
              'page': 1,
              'pageSize': 20,
              'hasMore': false,
            });
          }),
        ),
      );
      final r = await service.vehicles();
      expect((r as AuthFailure).error.code, AuthErrorCode.unauthenticated);
    },
  );
}
