import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/core/network/api_client.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/customer_booking/customer_booking.dart';
import 'package:washflow/features/customer_booking/customer_booking_screen.dart';
import 'package:washflow/features/customer_vehicles/domain/customer_vehicles.dart';

import 'support/redemption_fakes.dart';

const b = '11111111-1111-4111-8111-111111111111',
    s = '22222222-2222-4222-8222-222222222222',
    v = '33333333-3333-4333-8333-333333333333',
    a = '44444444-4444-4444-8444-444444444444',
    r = '55555555-5555-4555-8555-555555555555';
Map<String, dynamic> businessJson() => {
  'id': b,
  'name': 'Pilot Yıkama',
  'city': 'İstanbul',
  'district': 'Kadıköy',
  'timeZoneId': 'Europe/Istanbul',
};
Map<String, dynamic> appointmentJson({
  bool canChange = true,
  String status = 'Confirmed',
}) => {
  'id': a,
  'businessId': b,
  'ownedVehicleId': v,
  'serviceId': s,
  'businessName': 'Pilot Yıkama',
  'vehiclePlate': '34 OWN 1',
  'serviceName': 'İç dış yıkama',
  'localStart': '30.11.2026 12:00 +03:00',
  'timeZoneId': 'Europe/Istanbul',
  'status': status,
  'workStatus': 'Scheduled',
  'price': '450.25',
  'currency': 'TRY',
  'version': 4,
  'activeMinutes': 30,
  'cancellationRescheduleCutoffMinutes': 120,
  'canChange': canChange,
  'startsAtUtc': '2026-11-30T09:00:00Z',
};
Map<String, dynamic> slotJson() => {
  'businessId': b,
  'serviceId': s,
  'ownedVehicleId': v,
  'vehiclePlate': '34 OWN 1',
  'serviceName': 'İç dış yıkama',
  'price': '450.25',
  'currency': 'TRY',
  'activeMinutes': 30,
  'serviceVersion': 7,
  'vehicleVersion': 8,
  'page': 1,
  'hasMore': false,
  'items': [
    {
      'startsAtUtc': '2026-11-30T09:00:00Z',
      'endsAtUtc': '2026-11-30T09:30:00Z',
      'localLabel': '12:00 +03:00',
    },
  ],
};
CustomerAttempt attempt() => CustomerAttempt(b, {
  'clientRequestId': r,
  'ownedVehicleId': v,
  'serviceId': s,
  'expectedServiceVersion': 7,
  'expectedVehicleVersion': 8,
  'startsAtUtc': '2026-11-30T09:00:00Z',
});

class MemoryAttempt implements CustomerAttemptStore {
  String? raw;
  int writes = 0;
  @override
  Future<String?> read() async => raw;
  @override
  Future<void> write(String value) async {
    writes++;
    raw = value;
  }

  @override
  Future<void> clear() async {
    raw = null;
  }
}

class BookingFake implements CustomerBookingService {
  final attempts = <CustomerAttempt>[];
  bool uncertain = false, cutoff = false;
  Completer<AuthResult<List<PublicBusiness>>>? delayed;
  @override
  Future<AuthResult<List<PublicBusiness>>> discover(
    String query,
    int page,
  ) async => delayed == null
      ? AuthSuccess([PublicBusiness(businessJson())])
      : await delayed!.future;
  @override
  Future<AuthResult<PublicBusinessDetail>> business(
    String id,
    int page,
  ) async => AuthSuccess(
    PublicBusinessDetail({
      'business': businessJson(),
      'services': [
        {
          'id': s,
          'name': 'İç dış yıkama',
          'offerings': [
            {
              'category': 'Car',
              'price': '450.25',
              'currency': 'TRY',
              'activeMinutes': 30,
              'schedulingConfigured': true,
            },
          ],
        },
      ],
      'hours': null,
      'cancellationRescheduleCutoffMinutes': 120,
      'page': page,
      'hasMore': false,
    }),
  );
  @override
  Future<AuthResult<CustomerSlots>> slots(
    String business,
    String service,
    String vehicle,
    DateTime date,
    int page, {
    String? appointment,
  }) async => AuthSuccess(CustomerSlots(slotJson()));
  @override
  Future<AuthResult<CustomerAppointment>> book(CustomerAttempt attempt) async {
    attempts.add(attempt);
    return uncertain
        ? AuthFailure(AuthError(code: AuthErrorCode.timeout))
        : AuthSuccess(CustomerAppointment(appointmentJson()));
  }

  @override
  Future<AuthResult<List<CustomerAppointment>>> mine(
    String status,
    int page,
  ) async =>
      AuthSuccess([CustomerAppointment(appointmentJson(canChange: !cutoff))]);
  @override
  Future<AuthResult<CustomerAppointment>> detail(String id) async =>
      AuthSuccess(CustomerAppointment(appointmentJson(canChange: !cutoff)));
  @override
  Future<AuthResult<CustomerAppointment>> change(
    CustomerAppointment row,
    String request, {
    DateTime? start,
  }) async => AuthSuccess(
    CustomerAppointment(
      appointmentJson(
        status: start == null ? 'Cancelled' : 'Confirmed',
        canChange: start != null,
      ),
    ),
  );
}

class VehiclesFake implements CustomerVehicleService {
  @override
  Future<AuthResult<RecordPage<VehicleRecord>>> vehicles({
    String? business,
    String? customer,
    String query = '',
    int page = 1,
  }) async => AuthSuccess(
    RecordPage(
      [
        VehicleRecord.fromJson({
          'id': v,
          'businessId': null,
          'localCustomerId': null,
          'customerName': '',
          'plate': '34 OWN 1',
          'brand': 'Test',
          'model': 'Test',
          'category': 'Car',
          'version': 8,
          'createdAt': '2026-11-25T00:00:00Z',
          'updatedAt': '2026-11-25T00:00:00Z',
        }),
      ],
      1,
      false,
    ),
  );
  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

Widget screen(
  BookingFake service,
  MemoryAttempt store, {
  bool Function()? current,
}) => MaterialApp(
  home: CustomerBookingScreen(
    service: service,
    vehicles: VehiclesFake(),
    store: store,
    isCurrent: current ?? () => true,
    onAuthorizationFailure: (_) async {},
  ),
);
Future<void> tap(WidgetTester t, String text) async {
  final f = find.text(text);
  if (f.evaluate().isEmpty) {
    await t.scrollUntilVisible(
      f,
      250,
      scrollable: find.byType(Scrollable).first,
    );
  }
  await t.ensureVisible(f.last);
  await t.tap(f.last);
  await t.pumpAndSettle();
}

void main() {
  test('adapter scopes slots and rejects foreign owned vehicle', () async {
    final requests = <http.Request>[];
    bool foreign = false;
    final api = ApiCustomerBookingService(
      session: RedemptionSession()..signedIn = true,
      apiClient: ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((req) async {
          requests.add(req);
          return http.Response(
            jsonEncode({...slotJson(), if (foreign) 'ownedVehicleId': r}),
            200,
            headers: {'content-type': 'application/json; charset=utf-8'},
          );
        }),
      ),
    );
    expect(
      await api.slots(b, s, v, DateTime(2026, 11, 30), 1),
      isA<AuthSuccess<CustomerSlots>>(),
    );
    expect(requests.single.url.queryParameters, {
      'date': '2026-11-30',
      'page': '1',
    });
    expect(requests.single.url.path, contains('/vehicles/$v/slots'));
    expect(requests.single.headers['authorization'], isNotNull);
    foreign = true;
    expect(
      await api.slots(b, s, v, DateTime(2026, 11, 30), 1),
      isA<AuthFailure<CustomerSlots>>(),
    );
  });
  test('create accepts replay 200 and retains original request id', () async {
    Map<String, dynamic>? body;
    final api = ApiCustomerBookingService(
      session: RedemptionSession()..signedIn = true,
      apiClient: ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((req) async {
          body = jsonDecode(req.body);
          return http.Response(
            jsonEncode(appointmentJson(status: 'Cancelled', canChange: false)),
            200,
            headers: {'content-type': 'application/json; charset=utf-8'},
          );
        }),
      ),
    );
    final result = await api.book(CustomerAttempt.decode(attempt().encode()));
    expect(result, isA<AuthSuccess<CustomerAppointment>>());
    expect(
      (result as AuthSuccess<CustomerAppointment>).value.status,
      'Cancelled',
    );
    expect(body!['clientRequestId'], r);
  });
  testWidgets(
    'select business own car and price before explicit confirmation',
    (t) async {
      final service = BookingFake();
      final store = MemoryAttempt();
      await t.pumpWidget(screen(service, store));
      await t.pumpAndSettle();
      await tap(t, 'Pilot Yıkama');
      await tap(t, '34 OWN 1 • Otomobil');
      await tap(t, 'İç dış yıkama • 450,25 TL');
      await tap(t, 'Uygun saatleri göster');
      await tap(t, '12:00 +03:00');
      expect(service.attempts, isEmpty);
      expect(find.textContaining('120 dakika'), findsWidgets);
      await tap(t, 'Onayla');
      expect(service.attempts, hasLength(1));
      expect(store.writes, 1);
      expect(store.raw, isNull);
      expect(find.text('Randevunuz onaylandı.'), findsOneWidget);
      expect(service.attempts.single.body['expectedServiceVersion'], 7);
      expect(service.attempts.single.body['expectedVehicleVersion'], 8);
    },
  );
  testWidgets('uncertain create uses same stored key and never auto replays', (
    t,
  ) async {
    final service = BookingFake()..uncertain = true;
    final store = MemoryAttempt()..raw = attempt().encode();
    await t.pumpWidget(screen(service, store));
    await t.pumpAndSettle();
    expect(service.attempts, isEmpty);
    await tap(t, 'Kaydedilmiş isteği kontrol et');
    expect(service.attempts, hasLength(1));
    expect(store.raw, isNotNull);
    service.uncertain = false;
    await tap(t, 'Kaydedilmiş isteği kontrol et');
    expect(service.attempts, hasLength(2));
    expect(service.attempts.map((e) => e.body['clientRequestId']).toSet(), {r});
    expect(store.raw, isNull);
  });
  testWidgets('cutoff keeps appointment but hides mutation actions', (t) async {
    final service = BookingFake()..cutoff = true;
    await t.pumpWidget(screen(service, MemoryAttempt()));
    await t.pumpAndSettle();
    await tap(t, 'Randevularım');
    await tap(t, 'Pilot Yıkama • 34 OWN 1');
    expect(find.text('Randevuyu iptal et'), findsNothing);
    expect(find.text('Randevuyu ertele'), findsNothing);
    expect(find.textContaining('şu anda değiştirilemez'), findsOneWidget);
  });
  testWidgets('account switch discards late discovery response', (t) async {
    final service = BookingFake()..delayed = Completer();
    bool current = true;
    await t.pumpWidget(
      screen(service, MemoryAttempt(), current: () => current),
    );
    await t.pump();
    current = false;
    service.delayed!.complete(AuthSuccess([PublicBusiness(businessJson())]));
    await t.pump();
    expect(find.text('Pilot Yıkama'), findsNothing);
    await t.pumpWidget(const SizedBox.shrink());
  });
}
