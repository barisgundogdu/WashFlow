import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/scheduling/domain/scheduling.dart';
import 'package:washflow/features/work_orders/work_orders.dart';
import 'package:washflow/features/work_orders/stage_template_screen.dart';

import 'support/catalog_fakes.dart';
import 'support/scheduling_fakes.dart';
import 'support/session_fakes.dart';

class StageService implements StageTemplateService {
  List<ServiceStagePlan>? saved;
  int? expected;
  bool conflict = false;
  @override
  Future<AuthResult<ServiceStageConfig>> stagePlan(
    String b,
    String s,
    String c,
  ) async => const AuthSuccess(
    ServiceStageConfig(8, [ServiceStagePlan('Dış yıkama', 20, areaId, 1)]),
  );
  @override
  Future<AuthResult<ServiceStageConfig>> saveStagePlan(
    String b,
    String s,
    String c,
    int? version,
    List<ServiceStagePlan> plans,
  ) async {
    saved = plans;
    expected = version;
    return conflict
        ? AuthFailure(
            SchedulingError(
              code: AuthErrorCode.conflict,
              reason: 'requirementInUse',
            ),
          )
        : AuthSuccess(ServiceStageConfig(9, plans));
  }
}

Future<void> show(WidgetTester t, StageService service) async {
  final scheduling = FakeScheduling()
    ..workAreas = [
      WorkArea.fromJson(areaJson()),
      WorkArea.fromJson({
        ...areaJson(),
        'id': '77777777-7777-4777-8777-777777777777',
        'name': 'İç alan',
      }),
    ];
  await t.pumpWidget(
    MaterialApp(
      home: StageTemplateScreen(
        service: service,
        catalog: FakeCatalog(),
        scheduling: scheduling,
        business: businessA,
        isCurrent: () => true,
        onAuthorizationFailure: (_) async {},
      ),
    ),
  );
  await t.pumpAndSettle();
  await t.tap(find.byType(DropdownButtonFormField<String>).first);
  await t.pumpAndSettle();
  await t.tap(find.text('Dış yıkama').last);
  await t.pumpAndSettle();
  await t.tap(find.byType(DropdownButtonFormField<String>).at(1));
  await t.pumpAndSettle();
  await t.tap(find.text('Otomobil').last);
  await t.pumpAndSettle();
}

Future<void> tap(WidgetTester t, String text) async {
  final f = find.text(text).last;
  await t.ensureVisible(f);
  await t.pumpAndSettle();
  await t.tap(f);
  await t.pumpAndSettle();
}

void main() {
  test('stage plan flag enables booking while legacy inactive requirements stay closed', () {
    expect(
      SchedulingRequirement.fromJson({
        ...requirementJson(),
        'singleStageConfirmed': false,
        'stagePlanEnabled': true,
      }).configured,
      isTrue,
    );
    expect(
      SchedulingRequirement.fromJson({
        ...requirementJson(),
        'singleStageConfirmed': false,
      }).configured,
      isFalse,
    );
  });
  testWidgets(
    'owner saves ordered stage durations, areas and staff using expected version',
    (t) async {
      final service = StageService();
      await show(t, service);
      await t.enterText(find.byType(TextField).at(1), '10');
      await tap(t, 'Aşama ekle');
      await t.enterText(find.byType(TextField).at(3), 'İç temizlik');
      await t.enterText(find.byType(TextField).at(4), '10');
      FocusManager.instance.primaryFocus?.unfocus();
      await t.pumpAndSettle();
      await t.ensureVisible(find.byKey(const ValueKey((1, areaId))));
      await t.pumpAndSettle();
      await t.tap(find.byKey(const ValueKey((1, areaId))));
      await t.pumpAndSettle();
      await t.tap(find.text('İç alan').last);
      await t.pumpAndSettle();
      await tap(t, 'Aşama planını kaydet');
      expect(service.expected, 8);
      expect(service.saved!.map((p) => p.minutes), [10, 10]);
      expect(service.saved![1].area, '77777777-7777-4777-8777-777777777777');
      expect(
        find.textContaining('Aşama planı sunucuya kaydedildi'),
        findsOneWidget,
      );
    },
  );
  testWidgets(
    'invalid totals never save and confirmed bookings conflict never shows success',
    (t) async {
      final service = StageService();
      await show(t, service);
      await t.enterText(find.byType(TextField).at(1), '21');
      await tap(t, 'Aşama planını kaydet');
      expect(service.saved, isNull);
      expect(find.textContaining('Süre toplamı 20 dakika'), findsOneWidget);
      await t.ensureVisible(find.byType(TextField).at(1));
      await t.enterText(find.byType(TextField).at(1), '20');
      service.conflict = true;
      await tap(t, 'Aşama planını kaydet');
      expect(service.saved, isNotNull);
      expect(find.textContaining('sunucuya kaydedildi'), findsNothing);
      expect(
        find.text('Mevcut randevular bu değişikliğe izin vermiyor.'),
        findsOneWidget,
      );
    },
  );
}
