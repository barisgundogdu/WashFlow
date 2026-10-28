import 'dart:convert';

import 'package:flutter/foundation.dart';

import '../../auth/domain/auth_service.dart';
import '../../business_settings/domain/business_settings.dart';
import '../../customer_vehicles/domain/customer_vehicles.dart';
import '../../service_catalog/domain/service_catalog.dart';
import '../domain/scheduling.dart';

class SchedulingController extends ChangeNotifier {
  SchedulingController({
    required this.service,
    required this.settings,
    required this.catalog,
    required this.customers,
    required this.business,
    required this.isCurrent,
    required this.onAuthorizationFailure,
  });
  final SchedulingService service;
  final BusinessSettingsService settings;
  final CatalogService catalog;
  final CustomerVehicleService customers;
  final String business;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  bool _disposed = false, blocked = false, busy = false, loaded = false;
  bool get active => !_disposed && !blocked && isCurrent();
  String? message;
  AuthError? lastError;
  BusinessClock? clock;
  DateTime? date;
  bool week = false;
  int page = 1;
  List<Appointment> appointments = [];
  BookingAttempt? attempt;
  bool uncertainBooking = false;
  void changed() {
    if (active) notifyListeners();
  }

  void show(String text) {
    if (active) {
      message = text;
      changed();
    }
  }

  Future<T?> request<T>(Future<AuthResult<T>> Function() action) async {
    if (!active || busy) return null;
    busy = true;
    message = null;
    lastError = null;
    changed();
    try {
      final result = await action();
      if (!active) return null;
      switch (result) {
        case AuthSuccess<T>(:final value):
          return value;
        case AuthFailure<T>(:final error):
          lastError = error;
          if ({
            AuthErrorCode.unauthenticated,
            AuthErrorCode.invalidCredentials,
            AuthErrorCode.forbidden,
          }.contains(error.code)) {
            blocked = true;
            appointments = [];
            attempt = null;
            await onAuthorizationFailure(error);
          } else {
            message = schedulingError(error);
          }
      }
    } catch (_) {
      lastError = AuthError(code: AuthErrorCode.unexpected);
      if (active) {
        message =
            'İşlem tamamlanamadı. Bağlantıyı kontrol edip yeniden deneyin.';
      }
    } finally {
      if (!_disposed) {
        busy = false;
        if (isCurrent()) notifyListeners();
      }
    }
    return null;
  }

  Future<void> loadCalendar({int nextPage = 1}) async {
    if (!active || busy) return;
    appointments = [];
    loaded = false;
    if (clock == null) {
      final value = await request(() => settings.read(business));
      if (!active || value == null) return;
      try {
        clock = BusinessClock(value.fields['timeZoneId'] ?? '');
        date = clock!.today;
      } catch (_) {
        show(
          'İşletmenin geçerli IANA saat dilimi gerekli. İşletme ayarlarını kontrol edin.',
        );
        return;
      }
    }
    final start = date!;
    final value = await request(
      () => service.calendar(
        business,
        start,
        start.add(Duration(days: week ? 7 : 1)),
        page: nextPage,
      ),
    );
    if (!active || value == null) return;
    if (value.any((v) => v.businessId != business)) {
      show('Takvim doğrulanamadı.');
      return;
    }
    appointments = value;
    page = nextPage;
    loaded = true;
    changed();
  }

  Future<Appointment?> book(BookingPayload payload) async {
    if (!active || busy) return null;
    final fingerprint = jsonEncode(payload.toJson());
    if (attempt?.fingerprint != fingerprint) {
      if (uncertainBooking) {
        show(
          'Önce sonucu belirsiz isteği aynı bilgilerle tekrar deneyin veya takvimden doğrulayın.',
        );
        return null;
      }
      attempt = BookingAttempt(payload);
    }
    final result = await request(() => service.create(business, attempt!));
    if (!active) return null;
    if (result == null) {
      // Keep the exact attempt even for validation/conflict: never rotate on retry.
      uncertainBooking = lastError == null || uncertainWrite(lastError!);
      show(
        '${message ?? 'Randevu onaylanamadı.'} Aynı bilgilerle tekrar deneyebilirsiniz; işlem anahtarı korunur.',
      );
      return null;
    }
    uncertainBooking = false;
    attempt = null;
    return result;
  }

  void abandonAttempt() {
    attempt = null;
    uncertainBooking = false;
    changed();
  }

  @override
  void dispose() {
    _disposed = true;
    appointments = [];
    attempt = null;
    super.dispose();
  }
}
