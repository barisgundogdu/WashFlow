import 'dart:async';

import 'package:flutter/foundation.dart';

import '../../auth/domain/auth_service.dart';
import '../../customer_vehicles/domain/customer_vehicles.dart';
import '../domain/service_catalog.dart';

class CatalogController extends ChangeNotifier {
  CatalogController({
    required this.service,
    required this.business,
    required this.isCurrent,
    required this.onAuthorizationFailure,
  });
  final CatalogService service;
  final String business;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  bool loading = false,
      saving = false,
      blocked = false,
      requiresReload = false,
      hasMore = false;
  bool _disposed = false;
  int _generation = 0, page = 1, epoch = 0;
  String query = '', status = 'all';
  String? message;
  List<CatalogServiceRecord> items = [];
  CatalogServiceRecord? selected;
  CatalogDraft? draft;
  Timer? _debounce;
  bool get active => !_disposed && isCurrent() && !blocked;
  bool get canMutate => active && !loading && !saving && !requiresReload;
  bool _current(int g) => active && g == _generation;
  void _notify() {
    if (!_disposed && isCurrent()) notifyListeners();
  }

  Future<bool> _failure(AuthError error, int g) async {
    if (!_current(g)) return true;
    if ({
      AuthErrorCode.forbidden,
      AuthErrorCode.unauthenticated,
      AuthErrorCode.invalidCredentials,
    }.contains(error.code)) {
      items = [];
      selected = null;
      draft = null;
      blocked = true;
      _notify();
      await onAuthorizationFailure(error);
      return true;
    }
    message = managementError(error);
    return false;
  }

  void search(String value) {
    if (!active || saving || draft != null) return;
    _debounce?.cancel();
    _generation++;
    query = value;
    items = [];
    loading = true;
    _notify();
    _debounce = Timer(const Duration(milliseconds: 350), load);
  }

  void filter(String value) {
    if (!active || saving) return;
    status = value;
    unawaited(load());
  }

  Future<void> load({int nextPage = 1}) async {
    if (!active || saving) return;
    _debounce?.cancel();
    final g = ++_generation;
    loading = true;
    items = [];
    selected = null;
    draft = null;
    message = null;
    _notify();
    try {
      final result = await service.list(
        business,
        query: query,
        status: status,
        page: nextPage,
      );
      if (!_current(g)) return;
      switch (result) {
        case AuthSuccess<RecordPage<CatalogServiceRecord>>(:final value):
          if (value.items.any((v) => v.businessId != business) ||
              value.page != nextPage) {
            throw const FormatException();
          }
          items = value.items;
          page = value.page;
          hasMore = value.hasMore;
          requiresReload = false;
        case AuthFailure<RecordPage<CatalogServiceRecord>>(:final error):
          if (await _failure(error, g)) return;
      }
    } catch (_) {
      if (_current(g)) message = 'Hizmetler yüklenemedi. Yeniden deneyin.';
    }
    if (_current(g)) {
      loading = false;
      _notify();
    }
  }

  Future<void> open(String id) async {
    if (!active || saving) return;
    _debounce?.cancel();
    final g = ++_generation;
    loading = true;
    selected = null;
    draft = null;
    message = null;
    _notify();
    await _read(id, g);
    if (_current(g)) {
      loading = false;
      _notify();
    }
  }

  Future<void> _read(String id, int g) async {
    try {
      final r = await service.read(business, id);
      if (!_current(g)) return;
      switch (r) {
        case AuthSuccess<CatalogServiceRecord>(:final value):
          if (value.businessId != business || value.id != id) {
            throw const FormatException();
          }
          selected = value;
          draft = null;
          requiresReload = false;
          epoch++;
        case AuthFailure<CatalogServiceRecord>(:final error):
          if (await _failure(error, g)) return;
          requiresReload = true;
          message = 'Güncel kayıt yüklenemedi. ${managementError(error)}';
      }
    } catch (_) {
      if (_current(g)) {
        requiresReload = true;
        message = 'Güncel kayıt doğrulanamadı. Listeyi yenileyin.';
      }
    }
  }

  void edit({bool create = false, bool duplicate = false}) {
    if (!canMutate) return;
    _debounce?.cancel();
    _generation++;
    draft = create ? CatalogDraft() : selected?.draft(duplicate: duplicate);
    message = null;
    epoch++;
    _notify();
  }

  Future<void> save(CatalogDraft input) async {
    if (!canMutate || draft == null || !identical(input, draft)) return;
    final invalid = input.validate();
    if (invalid != null) {
      message = invalid;
      _notify();
      return;
    }
    await _mutate(() => service.save(business, input));
  }

  Future<void> archive() async {
    if (!canMutate || selected == null) return;
    final record = selected!;
    await _mutate(() => service.archive(business, record), archived: true);
  }

  Future<void> _mutate(
    Future<AuthResult<Object?>> Function() action, {
    bool archived = false,
  }) async {
    final g = ++_generation;
    var archiveConfirmed = false;
    saving = true;
    message = null;
    _notify();
    try {
      final r = await action();
      if (!_current(g)) return;
      switch (r) {
        case AuthSuccess<Object?>(:final value):
          requiresReload = true;
          draft = null;
          if (!archived &&
              value is CatalogServiceRecord &&
              value.businessId == business) {
            await _read(value.id, g);
            if (_current(g)) {
              message = requiresReload
                  ? 'Kaydetme onaylandı; güncel kayıt yüklenemedi. İşlemi tekrarlamayın, listeyi yenileyin.'
                  : 'Hizmet kaydedildi ve yeniden yüklendi.';
            }
          } else if (archived) {
            archiveConfirmed = true;
            selected = null;
            items = [];
            message = 'Arşivleme onaylandı. Listeyi yenileyin.';
          } else {
            message = 'Kaydetme onaylandı; güncel kaydı listeyi yenileyerek kontrol edin.';
          }
        case AuthFailure<Object?>(:final error):
          if (await _failure(error, g)) return;
          final uncertain = {
            AuthErrorCode.network,
            AuthErrorCode.timeout,
            AuthErrorCode.unavailable,
            AuthErrorCode.unexpected,
          }.contains(error.code);
          requiresReload =
              uncertain ||
              error.code == AuthErrorCode.conflict ||
              error.code == AuthErrorCode.notFound;
          if (uncertain) message = 'İşlem sonucu doğrulanamadı; sunucuda tamamlanmış olabilir. Otomatik tekrar yapılmadı. Taslağı bırakarak listeyi yenileyin.';
          if (error.code == AuthErrorCode.conflict) message = 'Kayıt başka bir işlemle değişti. Taslağı bırakarak güncel kaydı yükleyin.';
      }
    } catch (_) {
      if (_current(g)) {
        requiresReload = true;
        message = 'İşlem sonucu doğrulanamadı. İşlemi tekrarlamadan listeyi yenileyin.';
      }
    }
    if (_current(g)) {
      saving = false;
      _notify();
      if (archiveConfirmed) {
        await load();
        if (active) {
          message = requiresReload
              ? 'Arşivleme onaylandı; liste yüklenemedi. Yeniden yükleyin.'
              : 'Hizmet arşivlendi; liste yeniden yüklendi.';
          _notify();
        }
      }
    }
  }

  @override
  void dispose() {
    _disposed = true;
    _generation++;
    _debounce?.cancel();
    items = [];
    selected = null;
    draft = null;
    super.dispose();
  }
}
