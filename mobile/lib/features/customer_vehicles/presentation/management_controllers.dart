import 'dart:async';

import 'package:flutter/foundation.dart';

import '../../auth/domain/auth_service.dart';
import '../domain/customer_vehicles.dart';

abstract class ScopedManagementController extends ChangeNotifier {
  ScopedManagementController({
    required this.isCurrent,
    required this.onAuthorizationFailure,
  });
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  bool disposed = false, blocked = false;
  int generation = 0;
  String? message;
  bool current(int g) => !disposed && isCurrent() && g == generation;
  void changed() {
    if (!disposed && isCurrent()) notifyListeners();
  }

  Future<bool> failure(AuthError e, int g) async {
    if (!current(g)) return true;
    if ({
      AuthErrorCode.unauthenticated,
      AuthErrorCode.invalidCredentials,
      AuthErrorCode.forbidden,
    }.contains(e.code)) {
      blocked = true;
      message = managementError(e);
      changed();
      await onAuthorizationFailure(e);
      return true;
    }
    return false;
  }

  @override
  void dispose() {
    disposed = true;
    generation++;
    super.dispose();
  }
}

class DirectoryController extends ScopedManagementController {
  DirectoryController({
    required this.service,
    required this.business,
    required super.isCurrent,
    required super.onAuthorizationFailure,
    this.customer,
  });
  final CustomerVehicleService service;
  final String? business, customer;
  bool vehicleMode = false, loading = false, hasMore = false;
  int page = 1;
  String query = '';
  Timer? _debounce;
  List<LocalCustomerRecord> customers = [];
  List<VehicleRecord> vehicles = [];
  bool get isVehicles => business == null || customer != null || vehicleMode;
  void invalidate() {
    if (disposed || !isCurrent()) return;
    _debounce?.cancel();
    generation++;
    customers = [];
    vehicles = [];
    hasMore = false;
    loading = false;
    changed();
  }

  void search(String value) {
    if (disposed || blocked || !isCurrent()) return;
    _debounce?.cancel();
    generation++;
    customers = [];
    vehicles = [];
    query = value;
    loading = true;
    message = null;
    changed();
    _debounce = Timer(const Duration(milliseconds: 350), () => load());
  }

  void mode(bool vehicles) {
    if (disposed || blocked || !isCurrent()) return;
    vehicleMode = vehicles;
    unawaited(load());
  }

  Future<void> load({int nextPage = 1}) async {
    if (disposed || blocked || !isCurrent()) return;
    _debounce?.cancel();
    final g = ++generation;
    loading = true;
    customers = [];
    vehicles = [];
    message = null;
    changed();
    try {
      if (isVehicles) {
        final r = await service.vehicles(
          business: business,
          customer: customer,
          query: query,
          page: nextPage,
        );
        if (!current(g)) return;
        switch (r) {
          case AuthSuccess<RecordPage<VehicleRecord>>(:final value):
            if (value.items.any(
              (v) =>
                  v.businessId != business ||
                  (customer != null && v.localCustomerId != customer),
            )) {
              throw const FormatException();
            }
            vehicles = value.items;
            page = value.page;
            hasMore = value.hasMore;
          case AuthFailure<RecordPage<VehicleRecord>>(:final error):
            if (await failure(error, g)) return;
            message = managementError(error);
        }
      } else {
        final r = await service.customers(
          business!,
          query: query,
          page: nextPage,
        );
        if (!current(g)) return;
        switch (r) {
          case AuthSuccess<RecordPage<LocalCustomerRecord>>(:final value):
            if (value.items.any((c) => c.businessId != business)) {
              throw const FormatException();
            }
            customers = value.items;
            page = value.page;
            hasMore = value.hasMore;
          case AuthFailure<RecordPage<LocalCustomerRecord>>(:final error):
            if (await failure(error, g)) return;
            message = managementError(error);
        }
      }
    } catch (_) {
      if (current(g)) message = 'Kayıtlar yüklenemedi. Yeniden deneyin.';
    }
    if (current(g)) {
      loading = false;
      changed();
    }
  }

  @override
  void dispose() {
    _debounce?.cancel();
    customers = [];
    vehicles = [];
    super.dispose();
  }
}

class RecordEditorController extends ScopedManagementController {
  RecordEditorController({
    required this.service,
    required this.business,
    required this.customer,
    required this.vehicle,
    required this.customerRecord,
    required super.isCurrent,
    required super.onAuthorizationFailure,
  }) {
    fields = Map.of(vehicle?.fields ?? customerRecord?.fields ?? {});
    original = Map.of(fields);
  }
  final CustomerVehicleService service;
  final String? business, customer;
  final VehicleRecord? vehicle;
  final LocalCustomerRecord? customerRecord;
  late Map<String, String> fields, original;
  bool saving = false, confirmed = false, requiresReload = false;
  Object? saved;
  bool get dirty => !mapEquals(fields, original);
  bool get canEdit =>
      !disposed &&
      isCurrent() &&
      !blocked &&
      !saving &&
      !confirmed &&
      !requiresReload;
  void edit(String key, String value) {
    if (!canEdit) return;
    fields = {...fields, key: value};
    message = null;
    changed();
  }

  Future<void> save({required bool isVehicle}) async {
    if (!canEdit) return;
    final invalid = isVehicle
        ? validateVehicle(fields)
        : validateCustomer(fields);
    if (invalid != null) {
      message = invalid;
      changed();
      return;
    }
    final g = ++generation;
    saving = true;
    message = null;
    changed();
    AuthResult<Object?> result;
    try {
      result = isVehicle
          ? await service.saveVehicle(
              VehicleDraft(fields, id: vehicle?.id, version: vehicle?.version),
              business: business,
              customer: customer,
            )
          : await service.saveCustomer(
              business!,
              CustomerDraft(
                fields,
                id: customerRecord?.id,
                version: customerRecord?.version,
              ),
            );
    } catch (_) {
      result = AuthFailure(AuthError(code: AuthErrorCode.unexpected));
    }
    if (!current(g)) return;
    switch (result) {
      case AuthSuccess<Object?>(:final value):
        saved = value;
        confirmed = true;
        message = 'Kayıt sunucu tarafından kaydedildi.';
      case AuthFailure<Object?>(:final error):
        if (await failure(error, g)) return;
        requiresReload =
            uncertainWrite(error) ||
            error.code == AuthErrorCode.conflict ||
            error.code == AuthErrorCode.notFound;
        message = uncertainWrite(error)
            ? 'Sonuç doğrulanamadı; kayıt sunucuda tamamlanmış olabilir. İşlem tekrarlanmadı. Listeye dönüp güncel kayıtları kontrol edin.'
            : managementError(error);
    }
    if (current(g)) {
      saving = false;
      changed();
    }
  }

  Future<void> archive() async {
    if (!canEdit || vehicle == null) return;
    final g = ++generation;
    saving = true;
    changed();
    AuthResult<void> r;
    try {
      r = await service.archiveVehicle(vehicle!);
    } catch (_) {
      r = AuthFailure(AuthError(code: AuthErrorCode.unexpected));
    }
    if (!current(g)) return;
    switch (r) {
      case AuthSuccess<void>():
        confirmed = true;
        message = 'Araç arşivlendi.';
      case AuthFailure<void>(:final error):
        if (await failure(error, g)) return;
        requiresReload =
            uncertainWrite(error) ||
            error.code == AuthErrorCode.conflict ||
            error.code == AuthErrorCode.notFound;
        message = uncertainWrite(error)
            ? 'Arşivleme sonucu doğrulanamadı. İşlem tekrarlanmadı. Güncel listeyi kontrol edin.'
            : managementError(error);
    }
    if (current(g)) {
      saving = false;
      changed();
    }
  }

  @override
  void dispose() {
    fields.clear();
    original.clear();
    saved = null;
    super.dispose();
  }
}
