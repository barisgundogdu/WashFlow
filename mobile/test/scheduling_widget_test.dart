import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/app/routing/app_router.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/customer_vehicles/domain/customer_vehicles.dart';
import 'package:washflow/features/scheduling/domain/scheduling.dart';
import 'package:washflow/features/scheduling/data/secure_booking_attempt_store.dart';
import 'package:washflow/features/scheduling/presentation/appointment_detail_screen.dart';
import 'package:washflow/features/scheduling/presentation/booking_screen.dart';
import 'package:washflow/features/scheduling/presentation/scheduling_configuration_screen.dart';
import 'package:washflow/features/scheduling/presentation/scheduling_controller.dart';
import 'package:washflow/features/scheduling/presentation/scheduling_screen.dart';

import 'support/catalog_fakes.dart';
import 'support/customer_vehicle_fakes.dart' as cv;
import 'support/scheduling_fakes.dart';
import 'support/session_fakes.dart';
import 'support/settings_fakes.dart';

SchedulingController makeController(FakeScheduling service) =>
    SchedulingController(
        service: service,
        settings: FakeSettings()
          ..value = settings().change(
            fields: {...settings().fields, 'timeZoneId': 'Europe/Istanbul'},
          ),
        catalog: FakeCatalog(),
        customers: cv.FakeCustomerVehicles()
          ..people = [LocalCustomerRecord.fromJson(cv.customerJson())]
          ..cars = [VehicleRecord.fromJson(cv.vehicleJson())],
        business: businessA,
        isCurrent: () => true,
        onAuthorizationFailure: (_) async {},
      )
      ..clock = BusinessClock('Europe/Istanbul')
      ..date = DateTime.utc(2030, 1, 2);

Future<void> openPage(WidgetTester tester, Widget page) async {
  tester.view.physicalSize = const Size(1000, 2200);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
  await tester.pumpWidget(
    MaterialApp(
      locale: const Locale('tr'),
      supportedLocales: const [Locale('tr')],
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
      home: page,
    ),
  );
  await tester.pumpAndSettle();
}

Future<void> tapText(WidgetTester tester, String text) async {
  final finder = find.text(text).last;
  await tester.ensureVisible(finder);
  await tester.tap(finder);
  await tester.pumpAndSettle();
}

Future<void> choose(WidgetTester tester, String label, String value) async {
  final finder = find.byWidgetPredicate(
    (w) => w is InputDecorator && w.decoration.labelText == label,
  );
  await tester.ensureVisible(finder);
  await tester.tap(finder);
  await tester.pumpAndSettle();
  await tester.tap(find.text(value).last);
  await tester.pumpAndSettle();
}

Future<void> selectBooking(WidgetTester tester) async {
  await choose(tester, 'Müşteri', 'Ayşe Yılmaz');
  await choose(tester, 'Müşterinin aracı', '34 ABC 123 • Otomobil');
  await choose(tester, 'Etkin hizmet', 'Dış yıkama');
  await tapText(tester, 'Deniz Çalışan');
}

void main() {
  setUp(() => FlutterSecureStorage.setMockInitialValues({}));
  testWidgets('owner recovers persisted request without new selections', (
    tester,
  ) async {
    final pending = BookingAttempt(bookingPayload(time: '2030-01-02T11:00'));
    await SecureBookingAttemptStore(
      businessA,
      employeeId,
    ).write(jsonEncode(pending.toJson()));
    final s = FakeScheduling();
    final c = makeController(s);
    addTearDown(c.dispose);
    await openPage(
      tester,
      SchedulingScreen(
        business: businessA,
        ownerMembershipId: employeeId,
        service: s,
        settings: c.settings,
        catalog: c.catalog,
        customers: c.customers,
        isCurrent: () => true,
        onAuthorizationFailure: (_) async {},
      ),
    );
    expect(find.textContaining('Sonucu doğrulanmamış'), findsOneWidget);
    await tapText(tester, 'Yeni randevu');
    await tapText(tester, 'Takvimden sonucu kontrol et');
    await tapText(tester, 'Takvime dön');
    expect(
      await SecureBookingAttemptStore(businessA, employeeId).read(),
      jsonEncode(pending.toJson()),
    );
    await tapText(tester, 'Yeni randevu');
    await tapText(tester, 'Aynı isteği tekrar dene');
    await tapText(tester, 'Aynı isteği gönder');
    expect(s.creates.single.toJson(), pending.toJson());
    expect(find.text('Randevu ayrıntısı'), findsOneWidget);
    expect(
      await SecureBookingAttemptStore(businessA, employeeId).read(),
      isNull,
    );
  });
  testWidgets(
    'calendar loading error retry empty and day/week date navigation',
    (tester) async {
      final s = FakeScheduling();
      final pending = Completer<AuthResult<List<Appointment>>>();
      s.calendarHandler = () => pending.future;
      final c = makeController(s);
      addTearDown(c.dispose);
      // SchedulingScreen owns a separate controller; use its injected dependencies.
      await tester.pumpWidget(
        MaterialApp(
          home: SchedulingScreen(
            business: businessA,
            service: s,
            settings: c.settings,
            catalog: c.catalog,
            customers: c.customers,
            isCurrent: () => true,
            onAuthorizationFailure: (_) async {},
          ),
        ),
      );
      await tester.pump();
      expect(find.byType(LinearProgressIndicator), findsOneWidget);
      pending.complete(AuthFailure(AuthError(code: AuthErrorCode.network)));
      await tester.pumpAndSettle();
      expect(find.textContaining('ulaşılamadı'), findsOneWidget);
      expect(find.text('Bu tarih aralığında randevu yok.'), findsNothing);
      s.calendarHandler = null;
      await tapText(tester, 'Takvimi yenile');
      expect(find.text('Bu tarih aralığında randevu yok.'), findsOneWidget);
      await tapText(tester, 'Hafta');
      expect(
        s.calendarRanges.last.$2.difference(s.calendarRanges.last.$1),
        const Duration(days: 7),
      );
      final before = s.calendarRanges.last.$1;
      await tester.tap(find.byTooltip('Sonraki dönem'));
      await tester.pumpAndSettle();
      expect(s.calendarRanges.last.$1, before.add(const Duration(days: 7)));
    },
  );

  testWidgets(
    'booking validates then selects customer vehicle offering employee and previews',
    (tester) async {
      final s = FakeScheduling();
      final actual = makeController(s);
      addTearDown(actual.dispose);
      await openPage(tester, BookingScreen(controller: actual));
      await tapText(tester, 'Randevuyu onayla');
      expect(s.creates, isEmpty);
      expect(find.textContaining('etkin çalışan seçin'), findsOneWidget);
      await selectBooking(tester);
      expect(
        find.textContaining('Çalışma alanı: Yıkama alanı'),
        findsOneWidget,
      );
      expect(find.textContaining('450,10 TL'), findsOneWidget);
      await tapText(tester, 'Uygunluğu kontrol et');
      expect(s.previews, 1);
      expect(
        find.textContaining('Bu kontrol rezervasyon oluşturmaz'),
        findsOneWidget,
      );
      await tapText(tester, 'Randevuyu onayla');
      await tapText(tester, 'Randevuyu oluştur');
      expect(s.creates, hasLength(1));
      expect(s.creates.single.payload.vehicleId, cv.vehicleId);
      expect(s.creates.single.payload.schedule.employeeMembershipIds, [
        employeeId,
      ]);
      expect(find.text('Randevu ayrıntısı'), findsOneWidget);
      expect(s.details, 1);
    },
  );

  testWidgets(
    'booking timeout locks edits and retries identical key and payload',
    (tester) async {
      final s = FakeScheduling()
        ..createHandler = () async =>
            AuthFailure(AuthError(code: AuthErrorCode.timeout));
      final c = makeController(s);
      addTearDown(c.dispose);
      await openPage(tester, BookingScreen(controller: c));
      await selectBooking(tester);
      await tapText(tester, 'Randevuyu onayla');
      await tapText(tester, 'Randevuyu oluştur');
      expect(c.uncertainBooking, isTrue);
      expect(
        tester
            .widget<CheckboxListTile>(find.byType(CheckboxListTile))
            .onChanged,
        isNull,
      );
      await tapText(tester, 'Aynı isteği tekrar dene');
      await tapText(tester, 'Aynı isteği gönder');
      expect(s.creates, hasLength(2));
      expect(s.creates.first.toJson(), s.creates.last.toJson());
    },
  );

  testWidgets(
    'stale booking requires reloading catalog and vehicle before new attempt',
    (tester) async {
      final s = FakeScheduling()
        ..createHandler = () async => AuthFailure(
          SchedulingError(
            code: AuthErrorCode.conflict,
            reason: 'scheduleConflict',
          ),
        );
      final c = makeController(s);
      addTearDown(c.dispose);
      await openPage(tester, BookingScreen(controller: c));
      await selectBooking(tester);
      await tapText(tester, 'Randevuyu onayla');
      await tapText(tester, 'Randevuyu oluştur');
      expect(
        tester
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'Randevuyu onayla'),
            )
            .onPressed,
        isNull,
      );
      await tapText(tester, 'Seçenekleri yeniden yükle');
      expect(
        tester
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'Randevuyu onayla'),
            )
            .onPressed,
        isNotNull,
      );
    },
  );

  testWidgets(
    'unconfirmed requirement prevents booking and failed vehicle options can retry',
    (tester) async {
      final s = FakeScheduling()..requirementValues = [];
      final c = makeController(s);
      addTearDown(c.dispose);
      final customers = c.customers as cv.FakeCustomerVehicles;
      customers.onVehicles = (_, _, _, _) async =>
          AuthFailure(AuthError(code: AuthErrorCode.network));
      await openPage(tester, BookingScreen(controller: c));
      await choose(tester, 'Müşteri', 'Ayşe Yılmaz');
      expect(find.textContaining('ulaşılamadı'), findsOneWidget);
      customers.onVehicles = null;
      await tapText(tester, 'Seçenekleri yeniden yükle');
      await choose(tester, 'Müşterinin aracı', '34 ABC 123 • Otomobil');
      await choose(tester, 'Etkin hizmet', 'Dış yıkama');
      await tapText(tester, 'Deniz Çalışan');
      await tapText(tester, 'Randevuyu onayla');
      expect(s.creates, isEmpty);
      expect(find.textContaining('onaylı alan gereksinimini'), findsOneWidget);
    },
  );

  testWidgets(
    'detail shows history; cancellation requires confirmation and current version',
    (tester) async {
      final s = FakeScheduling();
      final actual = makeController(s);
      addTearDown(actual.dispose);
      await openPage(
        tester,
        AppointmentDetailScreen(controller: actual, id: appointmentId),
      );
      expect(find.text('Randevu geçmişi'), findsOneWidget);
      expect(find.textContaining('1. Oluşturuldu'), findsOneWidget);
      expect(find.textContaining('2030'), findsWidgets);
      await tapText(tester, 'Randevuyu iptal et');
      await tapText(tester, 'Vazgeç');
      expect(s.cancellations, 0);
      await tapText(tester, 'Randevuyu iptal et');
      await tapText(tester, 'Randevuyu iptal et');
      expect(s.cancellations, 1);
      expect(s.versions.single, 10);
      expect(find.text('İptal edildi'), findsOneWidget);
      expect(find.text('Yeniden planla'), findsNothing);
    },
  );

  testWidgets(
    'successful cancel does not claim a failed detail refresh succeeded',
    (tester) async {
      final s = FakeScheduling();
      s.detailHandler = () async => s.details == 1
          ? AuthSuccess(Appointment.fromJson(appointmentJson()))
          : AuthFailure(AuthError(code: AuthErrorCode.network));
      final c = makeController(s);
      addTearDown(c.dispose);
      await openPage(
        tester,
        AppointmentDetailScreen(controller: c, id: appointmentId),
      );
      await tapText(tester, 'Randevuyu iptal et');
      await tapText(tester, 'Randevuyu iptal et');
      expect(s.cancellations, 1);
      expect(s.details, 2);
      expect(find.textContaining('Güncel ayrıntı yüklenemedi'), findsOneWidget);
      expect(find.text('Yeniden planla'), findsNothing);
    },
  );

  for (final conflict in [false, true]) {
    testWidgets(
      'reschedule echoes version and refreshes details (conflict=$conflict)',
      (tester) async {
        final s = FakeScheduling();
        if (conflict) {
          s.rescheduleHandler = () async => AuthFailure(
            SchedulingError(
              code: AuthErrorCode.conflict,
              reason: 'scheduleConflict',
            ),
          );
        }
        final c = makeController(s);
        addTearDown(c.dispose);
        await openPage(
          tester,
          AppointmentDetailScreen(controller: c, id: appointmentId),
        );
        await tapText(tester, 'Yeniden planla');
        await tapText(tester, 'Yeniden planlamayı onayla');
        await tapText(tester, 'Yeniden planla');
        expect(s.versions, [10]);
        expect(s.schedules.single.employeeMembershipIds, [employeeId]);
        if (conflict) {
          expect(
            find.textContaining('Ayrıntıya dönüp güncel kaydı yükleyin.'),
            findsWidgets,
          );
          expect(
            tester
                .widget<FilledButton>(
                  find.widgetWithText(
                    FilledButton,
                    'Yeniden planlamayı onayla',
                  ),
                )
                .onPressed,
            isNull,
          );
          await tester.tap(find.byType(BackButton));
          await tester.pumpAndSettle();
        }
        expect(find.text('Randevu ayrıntısı'), findsOneWidget);
        expect(s.details, 2);
      },
    );
  }

  testWidgets(
    'work area validates capacity and updates using version; archive confirmation',
    (tester) async {
      final s = FakeScheduling();
      final actual = makeController(s);
      addTearDown(actual.dispose);
      await openPage(
        tester,
        SchedulingConfigurationScreen(
          controller: actual,
          section: SchedulingSection.areas,
        ),
      );
      await tapText(tester, 'Düzenle');
      await tester.enterText(find.byType(TextFormField).last, '0');
      await tapText(tester, 'Kaydet');
      expect(s.areaInputs, isEmpty);
      expect(find.text('Pozitif bir tam sayı girin.'), findsOneWidget);
      await tester.enterText(find.byType(TextFormField).last, '3');
      await tapText(tester, 'Kaydet');
      expect(s.areaInputs.single.capacity, 3);
      expect(s.areaInputs.single.version, 7);
      await tapText(tester, 'Arşivle');
      await tapText(tester, 'Arşivle');
      expect(s.areaArchives, 1);
    },
  );

  testWidgets(
    'requirement selects API work area and explicit single stage confirmation',
    (tester) async {
      final s = FakeScheduling()..requirementValues = [];
      final c = makeController(s);
      addTearDown(c.dispose);
      await openPage(
        tester,
        SchedulingConfigurationScreen(
          controller: c,
          section: SchedulingSection.requirements,
        ),
      );
      await choose(tester, 'Hizmet', 'Dış yıkama');
      await tapText(tester, 'Gereksinimi düzenle');
      await choose(tester, 'Gerekli çalışma alanı', 'Yıkama alanı');
      await tapText(tester, 'Bu hizmet tek etkin aşamada tamamlanır');
      await tapText(tester, 'Kaydet');
      expect(s.requirementInputs.single.workAreaId, areaId);
      expect(s.requirementInputs.single.singleStageConfirmed, isTrue);
      await tapText(tester, 'Otomobil planlamasını devre dışı bırak');
      await tapText(tester, 'Devre dışı bırak');
      expect(s.disables, 1);
    },
  );

  testWidgets(
    'removed service on configuration refresh clears selection safely',
    (tester) async {
      final c = makeController(FakeScheduling());
      addTearDown(c.dispose);
      await openPage(
        tester,
        SchedulingConfigurationScreen(
          controller: c,
          section: SchedulingSection.requirements,
        ),
      );
      await choose(tester, 'Hizmet', 'Dış yıkama');
      (c.catalog as FakeCatalog).listHandler = () async =>
          AuthSuccess(RecordPage([], 1, false));
      await tapText(tester, 'Listeyi yenile');
      expect(tester.takeException(), isNull);
      expect(find.text('Gereksinimi düzenle'), findsNothing);
    },
  );

  testWidgets(
    'availability uses bounded business-local week and versioned UTC editor',
    (tester) async {
      final s = FakeScheduling();
      final actual = makeController(s);
      addTearDown(actual.dispose);
      await openPage(
        tester,
        SchedulingConfigurationScreen(
          controller: actual,
          section: SchedulingSection.availability,
        ),
      );
      await choose(tester, 'Etkin çalışan', 'Deniz Çalışan');
      expect(s.availabilityRanges.single.$1, DateTime.utc(2030, 1, 1, 21));
      expect(s.availabilityRanges.single.$2, DateTime.utc(2030, 1, 8, 21));
      await tapText(tester, 'Düzenle');
      await tapText(tester, 'Kaydet');
      expect(
        s.availabilityInputs.single.startsAtUtc,
        DateTime.utc(2030, 1, 2, 6),
      );
      expect(s.availabilityInputs.single.version, 9);
      await tapText(tester, 'Arşivle');
      await tapText(tester, 'Arşivle');
      expect(s.availabilityArchives, 1);
    },
  );

  testWidgets('availability errors do not show empty success and can retry', (
    tester,
  ) async {
    final s = FakeScheduling()
      ..availabilityHandler = () async =>
          AuthFailure(AuthError(code: AuthErrorCode.network));
    final c = makeController(s);
    addTearDown(c.dispose);
    await openPage(
      tester,
      SchedulingConfigurationScreen(
        controller: c,
        section: SchedulingSection.availability,
      ),
    );
    await choose(tester, 'Etkin çalışan', 'Deniz Çalışan');
    expect(find.text('Bu aralıkta uygunluk kaydı yok.'), findsNothing);
    expect(find.textContaining('ulaşılamadı'), findsOneWidget);
    s.availabilityHandler = null;
    await tapText(tester, 'Uygunluğu yeniden yükle');
    expect(find.text('Düzenle'), findsOneWidget);
  });

  testWidgets('late options after disposal never update widget state', (
    tester,
  ) async {
    final s = FakeScheduling();
    final c = makeController(s);
    addTearDown(c.dispose);
    final pending = Completer<AuthResult<List<WorkArea>>>();
    s.areasHandler = () => pending.future;
    await tester.pumpWidget(MaterialApp(home: BookingScreen(controller: c)));
    await tester.pump();
    await tester.pumpWidget(const SizedBox.shrink());
    pending.complete(AuthSuccess(s.workAreas));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });

  for (final role in ['Owner', 'Employee', 'Customer']) {
    testWidgets('scheduling navigation is owner only ($role)', (tester) async {
      final session = FakeSession();
      addTearDown(session.dispose);
      final s = FakeScheduling();
      final c = makeController(s);
      addTearDown(c.dispose);
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
      await openPage(tester, const SizedBox.shrink());
      await tester.pumpWidget(
        WashFlowApp(
          authService: session,
          contextService: contexts,
          initialRoute: AppRouter.session,
          schedulingService: s,
          settingsService: c.settings,
          catalogService: c.catalog,
          customerVehicleService: c.customers,
        ),
      );
      await tester.pumpAndSettle();
      if (role == 'Owner') {
        expect(find.text('Planlama ve randevular'), findsOneWidget);
        await tapText(tester, 'Planlama ve randevular');
        expect(contexts.calls, 2);
        expect(find.text('Planlama'), findsOneWidget);
        expect(s.calendars, 1);
      } else {
        expect(find.text('Planlama ve randevular'), findsNothing);
        expect(s.calendars, 0);
      }
    });
  }
}
