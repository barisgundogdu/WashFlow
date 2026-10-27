import 'package:flutter/foundation.dart';

import '../../auth/domain/auth_service.dart';
import '../domain/business_settings.dart';

class BusinessSettingsController extends ChangeNotifier {
  BusinessSettingsController({
    required this.businessId,
    required this.service,
    required this.isCurrent,
    required this.onAuthorizationFailure,
  });
  final String businessId;
  final BusinessSettingsService service;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  BusinessSettings? persisted, draft;
  bool loading = false, saving = false, blocked = false, requiresReload = false;
  String? message;
  int epoch = 0, _generation = 0;
  bool _disposed = false;
  bool get dirty =>
      draft != null && draft!.fingerprint != persisted?.fingerprint;
  bool get canEdit =>
      !_disposed &&
      isCurrent() &&
      !blocked &&
      !loading &&
      !saving &&
      draft != null;
  bool _current(int g) => !_disposed && isCurrent() && g == _generation;
  void _notify() {
    if (!_disposed && isCurrent()) notifyListeners();
  }

  Future<bool> _auth(AuthError e, int g) async {
    if (!_current(g)) return true;
    if ({
      AuthErrorCode.unauthenticated,
      AuthErrorCode.invalidCredentials,
      AuthErrorCode.forbidden,
    }.contains(e.code)) {
      blocked = true;
      draft = null;
      persisted = null;
      message = settingsError(e);
      _notify();
      await onAuthorizationFailure(e);
      return true;
    }
    return false;
  }

  void field(String key, String value) {
    if (!canEdit) return;
    draft = draft!.change(fields: {...draft!.fields, key: value.trim()});
    message = null;
    _notify();
  }

  void hours(List<OpeningDay>? days) {
    if (!canEdit) return;
    draft = draft!.change(hours: days, clearHours: days == null);
    message = null;
    _notify();
  }

  Future<void> load() async {
    if (_disposed || !isCurrent() || saving || blocked) return;
    final g = ++_generation;
    loading = true;
    message = null;
    _notify();
    await _read(g);
    if (_current(g)) {
      loading = false;
      _notify();
    }
  }

  Future<void> _read(int g) async {
    AuthResult<BusinessSettings> r;
    try {
      r = await service.read(businessId);
    } catch (_) {
      r = AuthFailure(AuthError(code: AuthErrorCode.unexpected));
    }
    if (!_current(g)) return;
    switch (r) {
      case AuthSuccess<BusinessSettings>(:final value):
        if (value.businessId != businessId) {
          message = 'İşletme kaydı doğrulanamadı.';
          requiresReload = true;
          break;
        }
        persisted = value;
        draft = value;
        requiresReload = false;
        epoch++;
      case AuthFailure<BusinessSettings>(:final error):
        if (await _auth(error, g)) return;
        message = 'Güncel kayıt yüklenemedi. ${settingsError(error)}';
    }
  }

  Future<void> save() async {
    if (!canEdit || !dirty || requiresReload) return;
    final invalid = draft!.validate();
    if (invalid != null) {
      message = invalid;
      _notify();
      return;
    }
    final g = ++_generation;
    saving = true;
    message = null;
    _notify();
    AuthResult<BusinessSettings> r;
    try {
      r = await service.save(draft!);
    } catch (_) {
      r = AuthFailure(AuthError(code: AuthErrorCode.unexpected));
    }
    if (!_current(g)) return;
    switch (r) {
      case AuthSuccess<BusinessSettings>():
        requiresReload = true;
        message =
            'Kaydetme sunucu tarafından onaylandı. Güncel kayıt yükleniyor.';
        await _read(g);
        if (_current(g) && !requiresReload) {
          message = 'İşletme ayarları kaydedildi ve yeniden yüklendi.';
        } else if (_current(g) && !blocked) {
          message = 'Kaydetme sunucu tarafından onaylandı; güncel kayıt yüklenemedi. Yalnızca güncel kaydı yüklemeyi yeniden deneyin.';
        }
      case AuthFailure<BusinessSettings>(:final error):
        if (await _auth(error, g)) return;
        final uncertain = {
          AuthErrorCode.network,
          AuthErrorCode.timeout,
          AuthErrorCode.unexpected,
          AuthErrorCode.unavailable,
        }.contains(error.code);
        requiresReload = uncertain || error.code == AuthErrorCode.conflict;
        message = uncertain
            ? 'Kaydetme sonucu doğrulanamadı; sunucuda tamamlanmış olabilir. İşlem tekrarlanmadı. Güncel kaydı yükleyin.'
            : settingsError(error);
    }
    if (_current(g)) {
      saving = false;
      _notify();
    }
  }

  @override
  void dispose() {
    _disposed = true;
    _generation++;
    draft = null;
    persisted = null;
    super.dispose();
  }
}
