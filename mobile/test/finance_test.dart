import 'dart:async';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/app/routing/app_router.dart';

import 'support/session_fakes.dart';

import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/core/network/api_client.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/finance/finance.dart';
import 'package:washflow/features/finance/finance_screen.dart';
import 'package:washflow/features/scheduling/domain/scheduling.dart';

import 'support/redemption_fakes.dart';

const b = '11111111-1111-4111-8111-111111111111',
    method = '22222222-2222-4222-8222-222222222222',
    entry = '33333333-3333-4333-8333-333333333333',
    work = '44444444-4444-4444-8444-444444444444',
    request = '55555555-5555-4555-8555-555555555555';
Map<String, dynamic> reportJson() => {
  'fromDate': '2026-11-25',
  'toDateExclusive': '2026-11-26',
  'businessDate': '2026-11-25',
  'timeZoneId': 'Europe/Istanbul',
  'currency': 'TRY',
  'earnedRevenue': '450.00',
  'collectedPayments': '150.00',
  'manualIncome': '0.00',
  'expenses': '100.00',
  'serviceOperatingResult': '350.00',
  'cashOperatingBalance': '50.00',
  'unpaidBalance': '300.00',
  'serviceRevenue': [
    {'label': 'Yıkama', 'amount': '450.00'},
  ],
  'expenseCategories': [
    {'label': 'Malzeme', 'amount': '100.00'},
  ],
  'paymentMethods': [
    {'label': 'Nakit', 'amount': '150.00'},
  ],
  'trends': [
    {
      'date': '2026-11-25',
      'earnedRevenue': '450.00',
      'collectedPayments': '150.00',
      'manualIncome': '0.00',
      'expenses': '100.00',
    },
  ],
};
Map<String, dynamic> settingsJson() => {
  'methods': [
    {'id': method, 'name': 'Nakit', 'archived': false, 'version': 2},
  ],
  'employees': [],
};
Map<String, dynamic> entryJson({String kind = 'expenses'}) => {
  'id': entry,
  'businessId': b,
  'kind': kind,
  'amount': '100.15',
  'currency': 'TRY',
  'occurredAtUtc': '2026-11-25T09:00:00Z',
  'category': kind == 'expenses' ? 'Malzeme' : null,
  'description': 'Deterjan',
  'reference': 'Fiş 1',
  'workOrderId': kind == 'payments' ? work : null,
  'paymentMethodId': kind == 'payments' ? method : null,
  'paymentMethodName': kind == 'payments' ? 'Nakit' : null,
  'employeeMembershipId': null,
  'reversesId': null,
  'correctsId': null,
  'correctionReason': null,
};

class Store implements BookingAttemptStore {
  String? raw;
  bool failWrite = false;
  @override
  Future<String?> read() async => raw;
  @override
  Future<void> write(String value) async {
    if (failWrite) throw StateError('failure');
    raw = value;
  }

  @override
  Future<void> clear() async {
    raw = null;
  }
}

class FakeFinance implements FinanceService {
  bool uncertain = false;
  final attempts = <FinanceAttempt>[];
  Completer<AuthResult<FinanceReport>>? delayed;
  @override
  Future<AuthResult<FinanceSettings>> settings(String business) async =>
      AuthSuccess(FinanceSettings(settingsJson()));
  @override
  Future<AuthResult<FinanceReport>> report(
    String business, {
    String? from,
    String? to,
  }) async => delayed == null
      ? AuthSuccess(FinanceReport(reportJson()))
      : await delayed!.future;
  @override
  Future<AuthResult<List<Receivable>>> receivables(
    String business,
    int page, {
    bool all = false,
  }) async => AuthSuccess([
    Receivable({
      'workOrderId': work,
      'vehiclePlate': '34 TEST 1',
      'serviceName': 'Yıkama',
      'completedAtUtc': '2026-11-25T09:00:00Z',
      'price': '450.00',
      'collected': '150.00',
      'remaining': '300.00',
      'status': 'Partial',
    }),
  ]);
  @override
  Future<AuthResult<List<FinanceEntry>>> entries(
    String business,
    String kind,
    int page,
  ) async => AuthSuccess([FinanceEntry(entryJson(kind: kind))]);
  @override
  Future<AuthResult<FinanceEntry>> submit(
    String business,
    FinanceAttempt attempt,
  ) async {
    attempts.add(attempt);
    return uncertain
        ? AuthFailure(AuthError(code: AuthErrorCode.timeout))
        : AuthSuccess(FinanceEntry(entryJson(kind: attempt.kind)));
  }

  @override
  Future<AuthResult<FinanceMethod>> addMethod(
    String business,
    String name,
  ) async => AuthSuccess(
    FinanceMethod({
      'id': method,
      'name': name,
      'archived': false,
      'version': 2,
    }),
  );
  @override
  Future<AuthResult<FinanceMethod>> archiveMethod(
    String business,
    FinanceMethod m,
  ) async => AuthSuccess(
    FinanceMethod({'id': m.id, 'name': m.name, 'archived': true, 'version': 3}),
  );
}

Widget screen(FakeFinance service, Store store, {bool Function()? current}) =>
    MaterialApp(
      home: FinanceScreen(
        service: service,
        business: b,
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
  await t.pumpAndSettle();
  await t.tap(f.last);
  await t.pumpAndSettle();
}

void main() {
  test(
    'finance amounts use exact kurus for large aggregates and negative balance',
    () {
      final x = FinanceAmount('99999999999999.99');
      expect(x.kurus, BigInt.parse('9999999999999999'));
      expect(x.wire, '99999999999999.99');
      expect(FinanceAmount('-100.15').display, '-100,15 TL');
      for (final invalid in ['1.001', '1e3', 'NaN', '1,00']) {
        expect(() => FinanceAmount(invalid), throwsFormatException);
      }
    },
  );
  test(
    'API rejects foreign tenant entries and keeps server conflict reason',
    () async {
      bool foreign = true;
      final requests = <http.Request>[];
      final api = ApiFinanceService(
        session: RedemptionSession()..signedIn = true,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((req) async {
            requests.add(req);
            return foreign
                ? http.Response(
                    jsonEncode([
                      {...entryJson(), 'businessId': work},
                    ]),
                    200,
                    headers: {
                      'content-type': 'application/json; charset=utf-8',
                    },
                  )
                : http.Response(jsonEncode({'code': 'overpayment'}), 409);
          }),
        ),
      );
      expect(
        await api.entries(b, 'expenses', 1),
        isA<AuthFailure<List<FinanceEntry>>>(),
      );
      expect(
        requests.single.url.path,
        '/api/v1/businesses/$b/finance/entries/expenses',
      );
      foreign = false;
      final result = await api.submit(
        b,
        FinanceAttempt('payments', {'requestId': request}),
      );
      expect(result, isA<AuthFailure<FinanceEntry>>());
      expect(
        financeError((result as AuthFailure<FinanceEntry>).error),
        contains('kalan borcu aşamaz'),
      );
    },
  );
  testWidgets(
    'summary distinguishes earned collected and cash without double counting',
    (t) async {
      await t.pumpWidget(screen(FakeFinance(), Store()));
      await t.pumpAndSettle();
      expect(find.text('Tamamlanan hizmet geliri'), findsOneWidget);
      expect(find.text('450,00 TL'), findsOneWidget);
      expect(find.text('150,00 TL'), findsOneWidget);
      expect(find.text('600,00 TL'), findsNothing);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'expense explicit confirmation persists decimal and supporting reference',
    (t) async {
      final service = FakeFinance();
      final store = Store();
      await t.pumpWidget(screen(service, store));
      await t.pumpAndSettle();
      await tap(t, 'Gider ekle');
      await t.enterText(
        find.widgetWithText(TextField, 'Tutar (TRY)'),
        '100,15',
      );
      await t.enterText(find.widgetWithText(TextField, 'Kategori'), 'Malzeme');
      await t.ensureVisible(
        find.widgetWithText(TextField, 'Belge / referans (isteğe bağlı)'),
      );
      await t.enterText(
        find.widgetWithText(TextField, 'Belge / referans (isteğe bağlı)'),
        'Fiş 1',
      );
      await tap(t, 'Kaydı onayla');
      expect(service.attempts, isEmpty);
      await tap(t, 'Onayla');
      expect(service.attempts, hasLength(1));
      expect(service.attempts.single.body['amount'], '100.15');
      expect(service.attempts.single.body['reference'], 'Fiş 1');
      expect(store.raw, isNull);
    },
  );
  testWidgets('partial payment shows remaining and chosen payment method', (
    t,
  ) async {
    final service = FakeFinance();
    await t.pumpWidget(screen(service, Store()));
    await t.pumpAndSettle();
    await tap(t, 'Ödemeler');
    expect(find.text('Kısmi ödeme'), findsOneWidget);
    await tap(t, 'Ödeme kaydet');
    await tap(t, 'Kaydı onayla');
    expect(service.attempts, isEmpty);
    await tap(t, 'Onayla');
    expect(service.attempts.single.body['workOrderId'], work);
    expect(service.attempts.single.body['paymentMethodId'], method);
    expect(service.attempts.single.body['amount'], '300.00');
  });
  testWidgets(
    'uncertain ledger request is never automatically replayed and retains key',
    (t) async {
      final service = FakeFinance()..uncertain = true;
      final attempt = FinanceAttempt('expenses', {
        'requestId': request,
        'amount': '100.15',
      });
      final store = Store()..raw = attempt.encode();
      await t.pumpWidget(screen(service, store));
      await t.pumpAndSettle();
      expect(service.attempts, isEmpty);
      await tap(t, 'Kaydedilmiş finans isteğini kontrol et');
      expect(store.raw, isNotNull);
      service.uncertain = false;
      await tap(t, 'Kaydedilmiş finans isteğini kontrol et');
      expect(service.attempts.map((e) => e.body['requestId']).toSet(), {
        request,
      });
      expect(store.raw, isNull);
    },
  );
  testWidgets('late finance report is discarded after context switch', (
    t,
  ) async {
    final service = FakeFinance()..delayed = Completer();
    bool current = true;
    await t.pumpWidget(screen(service, Store(), current: () => current));
    await t.pump();
    current = false;
    service.delayed!.complete(AuthSuccess(FinanceReport(reportJson())));
    await t.pump();
    expect(find.text('Tamamlanan hizmet geliri'), findsNothing);
    await t.pumpWidget(const SizedBox.shrink());
  });
  for (final role in ['Owner', 'Employee', 'Customer']) {
    testWidgets('finance navigation is owner only ($role)', (t) async {
      FlutterSecureStorage.setMockInitialValues({});
      final session = FakeSession();
      addTearDown(session.dispose);
      final contexts = FakeContexts(
        grants(
          customer: role == 'Customer',
          businesses: role == 'Customer'
              ? []
              : [
                  businessContext(roles: [role]),
                ],
        ),
      );
      await t.pumpWidget(
        WashFlowApp(
          authService: session,
          contextService: contexts,
          initialRoute: AppRouter.session,
          financeService: FakeFinance(),
        ),
      );
      await t.pumpAndSettle();
      if (role == 'Owner') {
        await tap(t, 'Finans yönetimi');
        expect(find.text('Tamamlanan hizmet geliri'), findsOneWidget);
        expect(contexts.calls, 2);
      } else {
        expect(find.text('Finans yönetimi'), findsNothing);
      }
      await t.pumpWidget(const SizedBox.shrink());
    });
  }
  testWidgets('storage failure prevents sending a new financial entry', (
    t,
  ) async {
    final service = FakeFinance();
    final store = Store()..failWrite = true;
    await t.pumpWidget(screen(service, store));
    await t.pumpAndSettle();
    await tap(t, 'Ödemeler');
    await tap(t, 'Ödeme kaydet');
    await tap(t, 'Kaydı onayla');
    await tap(t, 'Onayla');
    expect(service.attempts, isEmpty);
    expect(find.textContaining('İşlem tamamlanamadı'), findsOneWidget);
  });
  testWidgets('expense correction retains original id and requires reason', (
    t,
  ) async {
    final service = FakeFinance();
    await t.pumpWidget(screen(service, Store()));
    await t.pumpAndSettle();
    await tap(t, 'Kayıtlar');
    await tap(t, 'Gideri düzelt');
    await t.enterText(find.widgetWithText(TextField, 'Tutar (TRY)'), '80,00');
    await tap(t, 'Kaydı onayla');
    expect(service.attempts, isEmpty);
    expect(find.textContaining('gerekçe gereklidir'), findsOneWidget);
    await t.scrollUntilVisible(
      find.widgetWithText(TextField, 'Düzeltme gerekçesi'),
      250,
      scrollable: find.byType(Scrollable).first,
    );
    await t.enterText(
      find.widgetWithText(TextField, 'Düzeltme gerekçesi'),
      'Hatalı tutar',
    );
    await tap(t, 'Kaydı onayla');
    await tap(t, 'Onayla');
    expect(service.attempts.single.body['correctsId'], entry);
    expect(service.attempts.single.body['amount'], '80.00');
    expect(service.attempts.single.body['correctionReason'], 'Hatalı tutar');
  });
}
