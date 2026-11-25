import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/core/network/api_client.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/scheduling/domain/scheduling.dart';
import 'package:washflow/features/work_orders/work_orders.dart';
import 'package:washflow/features/work_orders/work_order_screen.dart';

import 'support/redemption_fakes.dart';

const business = '11111111-1111-4111-8111-111111111111';
const orderId = '22222222-2222-4222-8222-222222222222';
const stageId = '33333333-3333-4333-8333-333333333333';
Map<String, dynamic> snapshot({
  String status = 'InProgress',
  int elapsed = 180,
}) => {
  'id': orderId,
  'businessId': business,
  'appointmentId': '44444444-4444-4444-8444-444444444444',
  'version': 2,
  'vehiclePlate': '34 TEST 1',
  'serviceName': 'Yıkama',
  'status': status,
  'plannedStartUtc': '2026-11-25T09:00:00Z',
  'serverNowUtc': '2026-11-25T09:03:00Z',
  'plannedStartLocal': '25.11.2026 12:00',
  'businessDateLocal': '25.11.2026',
  'potentialConflict': elapsed > 120,
  'timeZoneId': 'Europe/Istanbul',
  'stages': [
    {
      'id': stageId,
      'sequence': 1,
      'name': 'Dış yıkama',
      'estimatedMinutes': 2,
      'status': status == 'Scheduled' ? 'Ready' : status,
      'startedAtUtc': status == 'Scheduled'
          ? null
          : DateTime.utc(
              2026,
              11,
              25,
              9,
              3,
            ).subtract(Duration(seconds: elapsed)).toIso8601String(),
      'completedAtUtc': status == 'Completed' ? '2026-11-25T09:03:00Z' : null,
      'pausedAtUtc': null,
      'pausedMilliseconds': 0,
      'employeeMembershipIds': ['55555555-5555-4555-8555-555555555555'],
    },
  ],
};

class FakeWork implements WorkOrderService {
  Map<String, dynamic> value = snapshot();
  int reads = 0, actions = 0;
  @override
  Future<AuthResult<WorkOrder>> detail(String b, String id, bool owner) async {
    reads++;
    return AuthSuccess(WorkOrder(value));
  }

  @override
  Future<AuthResult<List<WorkOrder>>> list(
    String b,
    bool owner, {
    int page = 1,
  }) async => AuthSuccess([WorkOrder(value)]);
  @override
  Future<AuthResult<WorkOrder>> create(
    String b,
    Appointment a,
    List<({String name, int minutes})> s,
    String request,
  ) async => AuthSuccess(WorkOrder(value));
  @override
  Future<AuthResult<WorkOrder>> act(
    WorkOrder o,
    WorkStage? s,
    String action,
    String request,
  ) async {
    actions++;
    value = snapshot(status: action == 'complete' ? 'Completed' : 'InProgress');
    return AuthSuccess(WorkOrder(value));
  }
}

Widget detail(FakeWork service, {bool owner = false}) => MaterialApp(
  home: WorkOrderDetailScreen(
    service: service,
    business: business,
    id: orderId,
    owner: owner,
    isCurrent: () => true,
    onAuthorizationFailure: (_) async {},
  ),
);
void main() {
  test('remaining uses authoritative instants, pauses and overdue without completion', () {
    final now = DateTime.utc(2026, 11, 25, 9, 3);
    final stage = WorkOrder(snapshot()).stages.single;
    expect(stage.remaining(now), const Duration(minutes: -1));
    expect(workRemaining(stage, now), '1:00 gecikti');
    expect(stage.status, 'InProgress');
    final paused = Map<String, dynamic>.from(snapshot()['stages'][0] as Map)
      ..['status'] = 'Paused'
      ..['pausedAtUtc'] = '2026-11-25T09:01:00Z'
      ..['pausedMilliseconds'] = 30000;
    final p = WorkStage(paused);
    expect(p.remaining(now), const Duration(seconds: 90));
    expect(
      p.remaining(now.add(const Duration(hours: 2))),
      const Duration(seconds: 90),
    );
    final resumed = Map<String, dynamic>.from(paused)
      ..['status'] = 'InProgress'
      ..['pausedAtUtc'] = null
      ..['pausedMilliseconds'] = 150000;
    expect(WorkStage(resumed).remaining(now), const Duration(seconds: 90));
  });
  test(
    'API adapter scopes requests and rejects foreign tenant payload',
    () async {
      final requests = <http.Request>[];
      var foreign = false;
      final api = ApiWorkOrderService(
        session: RedemptionSession()..signedIn = true,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((r) async {
            requests.add(r);
            return http.Response(
              jsonEncode({
                ...snapshot(),
                if (foreign)
                  'businessId': '66666666-6666-4666-8666-666666666666',
              }),
              200,
              headers: {'content-type': 'application/json; charset=utf-8'},
            );
          }),
        ),
      );
      expect(
        await api.detail(business, orderId, false),
        isA<AuthSuccess<WorkOrder>>(),
      );
      expect(
        requests.single.url.path,
        '/api/v1/businesses/$business/employee/work-orders/$orderId',
      );
      expect(requests.single.headers['authorization'], isNotNull);
      expect(requests.single.url.query, isEmpty);
      foreign = true;
      expect(
        await api.detail(business, orderId, false),
        isA<AuthFailure<WorkOrder>>(),
      );
    },
  );
  testWidgets('overdue remains active and resume re-fetches server state', (
    tester,
  ) async {
    final service = FakeWork();
    await tester.pumpWidget(detail(service));
    await tester.pump();
    expect(find.text('1:00 gecikti'), findsOneWidget);
    expect(find.text('Aşamayı tamamla'), findsOneWidget);
    expect(service.actions, 0);
    await tester.pump(const Duration(seconds: 2));
    expect(service.actions, 0);
    service.value = snapshot(status: 'Completed');
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pump();
    await tester.pump();
    expect(service.reads, greaterThanOrEqualTo(2));
    expect(find.text('Aşamayı tamamla'), findsNothing);
    expect(find.text('Tamamlanma sunucuya kaydedildi.'), findsOneWidget);
    await tester.pumpWidget(const SizedBox.shrink());
  });
  testWidgets(
    'owner monitors without employee actions and sees overrun warning',
    (tester) async {
      final service = FakeWork();
      await tester.pumpWidget(detail(service, owner: true));
      await tester.pump();
      expect(find.text('Aşamayı tamamla'), findsNothing);
      expect(find.text('Duraklat'), findsNothing);
      expect(
        find.textContaining('otomatik kaydırma yapılmadı'),
        findsOneWidget,
      );
      expect(find.text('İş emrini iptal et'), findsOneWidget);
      await tester.pumpWidget(const SizedBox.shrink());
    },
  );
  testWidgets(
    'employee starts only after confirmation then sees server state',
    (tester) async {
      final service = FakeWork()
        ..value = snapshot(status: 'Scheduled', elapsed: 0);
      await tester.pumpWidget(detail(service));
      await tester.pump();
      await tester.tap(find.text('Başlat'));
      await tester.pumpAndSettle();
      expect(service.actions, 0);
      await tester.tap(find.text('Onayla'));
      await tester.pump();
      await tester.pump();
      expect(service.actions, 1);
      expect(find.text('Başlat'), findsNothing);
      expect(find.text('Aşamayı tamamla'), findsOneWidget);
      await tester.pumpWidget(const SizedBox.shrink());
    },
  );
}
