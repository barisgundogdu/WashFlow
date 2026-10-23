import 'package:flutter/foundation.dart';

import '../../auth/domain/auth_service.dart';
import '../../invitations/domain/invitation.dart';
import '../../invitations/domain/owner_invitation_service.dart';

enum InvitationListPhase { loading, ready, failed, blocked }

/// One route, one immutable business scope. No persistent or cross-tenant cache.
class EmployeeInvitationsController extends ChangeNotifier {
  EmployeeInvitationsController({
    required this.businessId,
    required this.service,
    required this.isCurrent,
    required this.onAuthorizationFailure,
  });
  final String businessId;
  final OwnerInvitationService service;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  bool _disposed = false;
  int _generation = 0;
  bool _mutating = false;
  bool get mutating => _mutating;
  InvitationListPhase _phase = InvitationListPhase.loading;
  InvitationListPhase get phase => _phase;
  List<EmployeeInvitation> _items = const [];
  List<EmployeeInvitation> get items => _items;
  String? _message;
  String? get message => _message;
  String? _error;
  String? get error => _error;
  bool get canMutate =>
      !_disposed &&
      isCurrent() &&
      !_mutating &&
      _phase == InvitationListPhase.ready;
  bool _valid(int generation) =>
      !_disposed && isCurrent() && generation == _generation;
  void _notify() {
    if (!_disposed && isCurrent()) notifyListeners();
  }

  Future<bool> _auth(AuthError error, int generation) async {
    if (!_valid(generation)) return true;
    if (error.code != AuthErrorCode.forbidden &&
        error.code != AuthErrorCode.unauthenticated &&
        error.code != AuthErrorCode.invalidCredentials) {
      return false;
    }
    _items = const [];
    _phase = InvitationListPhase.blocked;
    _error = invitationErrorMessage(error);
    _notify();
    await onAuthorizationFailure(error);
    return true;
  }

  Future<void> load() async {
    if (_disposed ||
        !isCurrent() ||
        _mutating ||
        _phase == InvitationListPhase.blocked) {
      return;
    }
    final generation = ++_generation;
    _message = null;
    _error = null;
    await _read(generation);
  }

  Future<void> _read(int generation) async {
    _items = const [];
    _phase = InvitationListPhase.loading;
    _notify();
    AuthResult<List<EmployeeInvitation>> result;
    try {
      result = await service.list(businessId);
    } catch (_) {
      result = AuthFailure(AuthError(code: AuthErrorCode.unexpected));
    }
    if (!_valid(generation)) return;
    switch (result) {
      case AuthSuccess<List<EmployeeInvitation>>(:final value):
        _items = List.unmodifiable(value);
        _phase = InvitationListPhase.ready;
      case AuthFailure<List<EmployeeInvitation>>(:final error):
        if (await _auth(error, generation)) return;
        _phase = InvitationListPhase.failed;
        _error =
            '${_error == null ? '' : '${_error!}\n'}Liste yenilenemedi. ${invitationErrorMessage(error)}';
    }
    _notify();
  }

  Future<bool> create(String email) async {
    if (!canMutate) return false;
    final trimmed = email.trim();
    if (trimmed.length > 320 ||
        !RegExp(r'^[^\s@]+@[^\s@]+$').hasMatch(trimmed)) {
      _error = invitationErrorMessage(
        AuthError(code: AuthErrorCode.validation),
      );
      _notify();
      return false;
    }
    return _mutate(
      () => service.create(businessId, trimmed),
      'Davet kaydedildi. E-posta teslimi doğrulanmadı.',
    );
  }

  Future<void> resend(String id) async {
    if (!canMutate || !_items.any((item) => item.id == id && item.canResend)) {
      return;
    }
    await _mutate(
      () => service.resend(businessId, id),
      'Yeni davet kaydedildi. Önceki davet kodu geçersizdir. E-posta teslimi doğrulanmadı.',
    );
  }

  /// Caller obtains explicit confirmation; eligibility is rechecked after the dialog.
  Future<void> revoke(String id) async {
    if (!canMutate || !_items.any((item) => item.id == id && item.canRevoke)) {
      return;
    }
    await _mutate(() => service.revoke(businessId, id), 'Davet iptal edildi.');
  }

  Future<bool> _mutate<T>(
    Future<AuthResult<T>> Function() action,
    String success,
  ) async {
    if (!canMutate) return false;
    _mutating = true;
    _error = null;
    _message = null;
    final generation = ++_generation;
    _notify();
    var recorded = false;
    try {
      AuthResult<T> result;
      try {
        result = await action();
      } catch (_) {
        result = AuthFailure(AuthError(code: AuthErrorCode.unexpected));
      }
      if (!_valid(generation)) return false;
      switch (result) {
        case AuthSuccess<T>():
          recorded = true;
          _message = success;
        case AuthFailure<T>(:final error):
          if (await _auth(error, generation)) return false;
          final uncertain = {
            AuthErrorCode.network,
            AuthErrorCode.timeout,
            AuthErrorCode.unexpected,
            AuthErrorCode.unavailable,
          }.contains(error.code);
          _error = uncertain
              ? 'İşlemin sonucu doğrulanamadı; sunucuda tamamlanmış olabilir. Güncel listeyi kontrol edin. İşlem otomatik tekrarlanmadı.'
              : invitationErrorMessage(error);
      }
      // Reconcile success, races and uncertain outcomes with a read, never replay a write.
      await _read(generation);
      return recorded && _valid(generation);
    } finally {
      if (_valid(generation)) {
        _mutating = false;
        _notify();
      }
    }
  }

  @override
  void dispose() {
    _disposed = true;
    _generation++;
    _items = const [];
    super.dispose();
  }
}
