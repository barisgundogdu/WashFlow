import 'package:flutter/foundation.dart';

import '../../auth/domain/auth_service.dart';
import '../../auth/domain/context_service.dart';
import '../../auth/domain/session_service.dart';
import '../domain/invitation_redemption_service.dart';

enum RedemptionStep {
  entry,
  choice,
  registration,
  verification,
  login,
  confirm,
  accepted,
  closed,
}

/// Route-owned memory only. Never exposes a credential getter or stores grants.
class InvitationRedemptionController extends ChangeNotifier {
  InvitationRedemptionController({
    required this.auth,
    required this.session,
    required this.access,
    required this.contexts,
    required this.service,
  }) {
    if (session is SessionIdentity) {
      _identity = session as SessionIdentity;
      _identity!.addListener(_identityChanged);
    }
  }
  final AuthService auth;
  final SessionService session;
  final AccessTokenProvider access;
  final ContextService contexts;
  final InvitationRedemptionService service;
  SessionIdentity? _identity;
  int? _expectedLogin, _expectedLogout;
  String? _credential;
  bool get hasCredential => _credential != null;
  String email = '';
  RedemptionStep _step = RedemptionStep.entry;
  RedemptionStep get step => _step;
  bool _busy = false, _disposed = false;
  bool get busy => _busy;
  bool authenticated = false;
  bool acceptedConfirmed = false;
  bool contextsLoaded = false;
  bool registrationUncertain = false;
  bool logoutNeedsRetry = false;
  bool acceptanceUncertain = false;
  String? message;
  int _generation = 0;
  bool _current(int generation) => !_disposed && generation == _generation;
  void _notify() {
    if (!_disposed) notifyListeners();
  }

  void _identityChanged() {
    final revision = _identity!.sessionRevision;
    if (_busy && revision == _expectedLogin) {
      _expectedLogin = null;
      return;
    }
    if (_busy && revision == _expectedLogout) {
      _expectedLogout = null;
      return;
    }
    cancel();
    authenticated = false;
    message = 'Hesap değişti. Davet bilgisi temizlendi. Devam etmek için daveti yeniden girin.';
    _notify();
  }

  void cancel() {
    if (_disposed) return;
    _generation++;
    _credential = null;
    email = '';
    _expectedLogin = null;
    _expectedLogout = null;
    _busy = false;
    _step = RedemptionStep.closed;
    contextsLoaded = false;
    acceptedConfirmed = false;
    acceptanceUncertain = false;
    registrationUncertain = false;
    logoutNeedsRetry = false;
    message = null;
    _notify();
  }

  Future<void> begin(String input) async {
    if (_disposed || _busy || _step != RedemptionStep.entry) return;
    final credential = input.trim();
    if (!RegExp(r'^[A-Za-z0-9_-]{43}$').hasMatch(credential)) {
      message = 'E-postadaki davet bilgisini eksiksiz girin. Biçim kontrolü davetin geçerliliğini doğrulamaz.';
      _notify();
      return;
    }
    _credential = credential;
    _busy = true;
    message = null;
    final generation = ++_generation;
    _notify();
    try {
      final result = await access.accessToken();
      if (!_current(generation)) return;
      authenticated = result is AuthSuccess<String> && result.value.isNotEmpty;
      _step = authenticated ? RedemptionStep.confirm : RedemptionStep.choice;
      if (result case AuthFailure<String>(:final error)
          when error.code != AuthErrorCode.invalidCredentials &&
              error.code != AuthErrorCode.unauthenticated) {
        message = redemptionErrorMessage(error);
      }
    } catch (_) {
      if (_current(generation)) {
        _step = RedemptionStep.choice;
        message = 'Oturum kontrol edilemedi. Giriş yapın.';
      }
    } finally {
      if (_current(generation)) {
        _busy = false;
        _notify();
      }
    }
  }

  void _move(RedemptionStep target) {
    if (_disposed ||
        _busy ||
        _credential == null ||
        acceptedConfirmed ||
        _step == RedemptionStep.closed) {
      return;
    }
    _generation++;
    _step = target;
    message = null;
    _notify();
  }

  void showLogin() => _move(RedemptionStep.login);
  void showRegistration() => _move(RedemptionStep.registration);
  void showVerification([String? address]) {
    if (_busy) return;
    if (address != null) email = address.trim();
    _move(RedemptionStep.verification);
  }

  bool _uncertain(AuthError error) => {
    AuthErrorCode.network,
    AuthErrorCode.timeout,
    AuthErrorCode.unexpected,
    AuthErrorCode.unavailable,
  }.contains(error.code);

  Future<void> register({
    required String fullName,
    required String email,
    required String password,
  }) async {
    if (_disposed ||
        _busy ||
        _step != RedemptionStep.registration ||
        _credential == null) {
      return;
    }
    if (fullName.trim().isEmpty ||
        fullName.trim().length > 200 ||
        email.trim().isEmpty ||
        email.trim().length > 320 ||
        password.trim().isEmpty ||
        password.length > 1024) {
      message = redemptionErrorMessage(
        AuthError(code: AuthErrorCode.validation),
      );
      _notify();
      return;
    }
    this.email = email.trim();
    _busy = true;
    message = null;
    final generation = ++_generation;
    _notify();
    try {
      final result = await service.register(
        InvitationRegistrationRequest(
          token: _credential!,
          fullName: fullName,
          email: email,
          password: password,
        ),
      );
      if (!_current(generation)) return;
      switch (result) {
        case AuthSuccess<void>():
          registrationUncertain = false;
          _step = RedemptionStep.verification;
          message = 'Kayıt talebiniz alındı. Bu yanıt hesap oluşturulduğunu veya davetin geçerli olduğunu doğrulamaz. E-postanızı doğrulayıp giriş yapın; ardından daveti ayrıca kabul edin.';
        case AuthFailure<void>(:final error):
          registrationUncertain = _uncertain(error);
          message = registrationUncertain
              ? 'Kayıt isteğinin sonucu belirsiz; tamamlanmış olabilir. Otomatik tekrar gönderilmedi. Önce doğrulama veya giriş adımını deneyin. Yeniden kayıt isteği göndermek için bilgileri tekrar girin.'
              : redemptionErrorMessage(error);
      }
    } catch (_) {
      if (_current(generation)) {
        registrationUncertain = true;
        message = 'Kayıt sonucu belirsiz. Otomatik tekrar gönderilmedi. Önce doğrulama veya giriş yapmayı deneyin.';
      }
    } finally {
      if (_current(generation)) {
        _busy = false;
        _notify();
      }
    }
  }

  Future<void> login(String email, String password) async {
    if (_disposed ||
        _busy ||
        _step != RedemptionStep.login ||
        _credential == null) {
      return;
    }
    _busy = true;
    message = null;
    this.email = email.trim();
    contextsLoaded = false;
    final generation = ++_generation;
    _expectedLogin = _identity == null ? null : _identity!.sessionRevision + 1;
    _notify();
    try {
      final result = await auth.login(
        LoginRequest(email: email, password: password),
      );
      if (!_current(generation)) return;
      switch (result) {
        case AuthSuccess<LoginResult>():
          registrationUncertain = false;
          authenticated = true;
          _step = RedemptionStep.confirm;
        case AuthFailure<LoginResult>(:final error):
          authenticated = false;
          message = redemptionErrorMessage(error);
      }
    } catch (_) {
      if (_current(generation)) {
        message = 'Giriş sonucu alınamadı. Yeniden giriş yapın.';
      }
    } finally {
      if (_current(generation)) {
        _expectedLogin = null;
        _busy = false;
        _notify();
      }
    }
  }

  Future<void> accept({bool deliberateRetry = false}) async {
    if (_disposed ||
        _busy ||
        _step != RedemptionStep.confirm ||
        !authenticated ||
        _credential == null ||
        acceptedConfirmed ||
        (acceptanceUncertain && !deliberateRetry)) {
      return;
    }
    _busy = true;
    message = null;
    final generation = ++_generation;
    _notify();
    try {
      final result = await service.accept(_credential!);
      if (!_current(generation)) return;
      switch (result) {
        case AuthSuccess<void>():
          _credential = null;
          acceptedConfirmed = true;
          acceptanceUncertain = false;
          _step = RedemptionStep.accepted;
          message = 'Davet kabul edildi. Hesap erişimleri yenileniyor.';
          await _readContexts(generation);
        case AuthFailure<void>(:final error):
          if (error.code == AuthErrorCode.unauthenticated ||
              error.code == AuthErrorCode.invalidCredentials) {
            authenticated = false;
            _step = RedemptionStep.login;
          }
          acceptanceUncertain = acceptanceUncertain || _uncertain(error);
          message =
              '${redemptionErrorMessage(error)}${acceptanceUncertain ? ' Kabul sonucu belirsiz; işlem tamamlanmış olabilir. Otomatik tekrar gönderilmedi. Hesap erişimlerini yenilemek bu davetin kabul edildiğini kanıtlamaz.' : ''}';
      }
    } catch (_) {
      if (_current(generation)) {
        acceptanceUncertain = true;
        message = 'Kabul sonucu belirsiz; işlem tamamlanmış olabilir. Otomatik tekrar gönderilmedi. Mevcut erişimlerinizi yenileyin.';
      }
    } finally {
      if (_current(generation)) {
        _busy = false;
        _notify();
      }
    }
  }

  Future<void> _readContexts(int generation) async {
    try {
      final result = await contexts.getContexts();
      if (!_current(generation)) return;
      switch (result) {
        case AuthSuccess():
          contextsLoaded = true;
          message = acceptedConfirmed
              ? 'Davet kabul edildi. Hesap erişimleri yenilendi.'
              : 'Mevcut hesap erişimleri yenilendi. Bu davetin kabul edildiği doğrulanmadı.';
        case AuthFailure(:final error):
          contextsLoaded = false;
          if (error.code == AuthErrorCode.unauthenticated ||
              error.code == AuthErrorCode.invalidCredentials) {
            authenticated = false;
          }
          message =
              '${acceptedConfirmed ? 'Davet kabul edildi; ' : ''}erişimler yenilenemedi. ${redemptionErrorMessage(error)}';
      }
    } catch (_) {
      if (_current(generation)) {
        contextsLoaded = false;
        message =
            '${acceptedConfirmed ? 'Davet kabul edildi; ' : ''}erişimler yenilenemedi. Yenilemeyi tekrar deneyebilirsiniz.';
      }
    }
  }

  Future<void> refreshContexts() async {
    if (_disposed ||
        _busy ||
        !authenticated ||
        _step == RedemptionStep.closed) {
      return;
    }
    _busy = true;
    final generation = ++_generation;
    _notify();
    await _readContexts(generation);
    if (_current(generation)) {
      _busy = false;
      _notify();
    }
  }

  Future<bool> logout() async {
    if (_disposed) return false;
    cancel();
    authenticated = false;
    _busy = true;
    final generation = _generation;
    _expectedLogout = _identity == null ? null : _identity!.sessionRevision + 1;
    _notify();
    try {
      final result = await session.logout();
      if (!_current(generation)) return false;
      if (result case AuthFailure<void>(:final error)) {
        message = error.code == AuthErrorCode.storage
            ? redemptionErrorMessage(error)
            : 'Yerel oturum kapatıldı. Sunucuda çıkış doğrulanamadı.';
        logoutNeedsRetry = error.code == AuthErrorCode.storage;
        return !logoutNeedsRetry;
      }
      return true;
    } catch (_) {
      if (_current(generation)) {
        logoutNeedsRetry = true;
        message = 'Çıkış doğrulanamadı. Çıkışı yeniden deneyin.';
      }
      return false;
    } finally {
      if (_current(generation)) {
        _expectedLogout = null;
        _busy = false;
        _notify();
      }
    }
  }

  @override
  void dispose() {
    _disposed = true;
    _generation++;
    _credential = null;
    email = '';
    _identity?.removeListener(_identityChanged);
    super.dispose();
  }
}
