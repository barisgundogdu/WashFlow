import 'package:flutter/material.dart';

import '../../../app/routing/app_router.dart';
import '../../auth/domain/auth_service.dart';
import '../../auth/domain/auth_submission.dart';
import '../../auth/domain/context_service.dart';
import '../../auth/domain/session_service.dart';
import '../../auth/presentation/auth_form_screen.dart';
import '../../auth/presentation/verification_screen.dart';
import '../../auth/presentation/widgets/auth_layout.dart';
import '../domain/invitation_redemption_service.dart';
import 'invitation_redemption_controller.dart';

class _FlowSubmission implements AuthSubmission {
  _FlowSubmission(this.controller, this.registration);
  final InvitationRedemptionController controller;
  final bool registration;
  @override
  Future<void> submit(AuthFormInput input) async {
    if (registration) {
      await controller.register(
        fullName: input.fullName ?? '',
        email: input.email,
        password: input.password,
      );
    } else {
      await controller.login(input.email, input.password);
    }
  }
}

/// One disposable, in-memory flow; no credential-bearing route arguments.
class InvitationRedemptionScreen extends StatefulWidget {
  const InvitationRedemptionScreen({
    super.key,
    required this.auth,
    required this.session,
    required this.access,
    required this.contexts,
    required this.service,
  });
  final AuthService auth;
  final SessionService session;
  final AccessTokenProvider access;
  final ContextService contexts;
  final InvitationRedemptionService service;
  @override
  State<InvitationRedemptionScreen> createState() =>
      _InvitationRedemptionScreenState();
}

class _InvitationRedemptionScreenState
    extends State<InvitationRedemptionScreen> {
  late final _controller = InvitationRedemptionController(
    auth: widget.auth,
    session: widget.session,
    access: widget.access,
    contexts: widget.contexts,
    service: widget.service,
  );
  final _credential = TextEditingController();
  bool _confirming = false;
  bool _leaving = false;
  DialogRoute<bool>? _confirmation;
  @override
  void initState() {
    super.initState();
    _controller.addListener(_changed);
  }

  void _changed() {
    if (!mounted) return;
    if (_controller.step != RedemptionStep.entry) _credential.clear();
    setState(() {});
    final dialog = _confirmation;
    if (_controller.step == RedemptionStep.closed && dialog != null) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted && dialog.isActive) dialog.navigator?.removeRoute(dialog);
      });
    }
  }

  @override
  void dispose() {
    _credential.clear();
    _credential.dispose();
    _controller.removeListener(_changed);
    _controller.dispose();
    super.dispose();
  }

  Future<void> _leave({bool login = false, bool session = false}) async {
    if (_leaving) return;
    _leaving = true;
    _controller.cancel();
    _credential.clear();
    var toSession = session;
    if (!login && !session) {
      // This is only a return destination; the shell still reloads backend grants.
      try {
        final current = await widget.access.accessToken();
        toSession = current is AuthSuccess<String> && current.value.isNotEmpty;
      } catch (_) {
        toSession = false;
      }
    }
    if (!mounted) return;
    Navigator.of(context, rootNavigator: true).pushNamedAndRemoveUntil(
      login
          ? AppRouter.login
          : toSession
          ? AppRouter.session
          : AppRouter.welcome,
      (_) => false,
    );
  }

  Future<void> _logout() async {
    final done = await _controller.logout();
    if (mounted && done) {
      final message = _controller.message;
      final messenger = ScaffoldMessenger.of(context);
      await _leave(login: true);
      if (message != null && messenger.mounted) {
        messenger.showSnackBar(SnackBar(content: Text(message)));
      }
    }
  }

  Future<void> _accept() async {
    if (_confirming || _controller.busy || !_controller.hasCredential) return;
    _confirming = true;
    final route = DialogRoute<bool>(
      context: context,
      builder: (dialog) => AlertDialog(
        title: const Text('Davet kabul onayı'),
        content: Text(
          _controller.acceptanceUncertain
              ? 'Önceki istek tamamlanmış olabilir. Bu işlem otomatik tekrarlanmadı. Bilerek tekrar denemek istiyor musunuz? İşletme adı bu adımda doğrulanamaz.'
              : 'Davet edilen doğrulanmış hesabınızla devam ettiğinizden emin olun. İşletme adı kabul öncesinde doğrulanamaz. Daveti kabul etmek istiyor musunuz?',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialog, false),
            child: const Text('Vazgeç'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialog, true),
            child: const Text('Kabul etmeyi onayla'),
          ),
        ],
      ),
    );
    _confirmation = route;
    final confirmed = await Navigator.of(context).push(route);
    _confirmation = null;
    _confirming = false;
    if (mounted && confirmed == true) {
      await _controller.accept(
        deliberateRetry: _controller.acceptanceUncertain,
      );
    }
  }

  Widget _body() {
    final controller = _controller;
    return switch (controller.step) {
      RedemptionStep.registration || RedemptionStep.login => AuthFormScreen(
        key: ValueKey(controller.step),
        kind: controller.step == RedemptionStep.registration
            ? AuthFormKind.customerRegistration
            : AuthFormKind.login,
        titleOverride: controller.step == RedemptionStep.registration
            ? 'Davetli çalışan kaydı'
            : 'Davet için giriş',
        subtitleOverride: 'Davet edilen e-posta adresini kullanın. Bu akış müşteri profili veya işletme kaydı oluşturmaz. Giriş ve kayıt, daveti otomatik kabul etmez.',
        submission: _FlowSubmission(
          controller,
          controller.step == RedemptionStep.registration,
        ),
        onSuccess: (_) {},
        onVerify: controller.showVerification,
        onAlternative: controller.step == RedemptionStep.registration
            ? controller.showLogin
            : controller.showRegistration,
        alternativeLabel: controller.step == RedemptionStep.registration
            ? 'Mevcut hesabımla giriş yap'
            : 'Davet için hesap oluştur',
      ),
      RedemptionStep.verification => VerificationScreen(
        key: const ValueKey('invitation-verification'),
        service: widget.session,
        email: controller.email,
        onLogin: controller.showLogin,
      ),
      _ => AuthLayout(
        allowBack: false,
        title: 'Çalışan davetini kullan',
        subtitle: 'İşletme adı kabul öncesinde doğrulanamaz. Davet edilen doğrulanmış e-posta hesabınızı kullanın.',
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (controller.step == RedemptionStep.entry) ...[
              TextField(
                key: const ValueKey('invitation-credential'),
                controller: _credential,
                obscureText: true,
                autocorrect: false,
                enableSuggestions: false,
                enabled: !controller.busy,
                decoration: const InputDecoration(
                  labelText: 'E-postadaki davet bilgisi',
                ),
              ),
              FilledButton(
                onPressed: controller.busy
                    ? null
                    : () => controller.begin(_credential.text),
                child: const Text('Davetle devam et'),
              ),
            ],
            if (controller.step == RedemptionStep.choice) ...[
              FilledButton(
                onPressed: controller.showLogin,
                child: const Text('Mevcut hesabımla giriş yap'),
              ),
              TextButton(
                onPressed: controller.showRegistration,
                child: const Text('Davet için hesap oluştur'),
              ),
            ],
            if (controller.step == RedemptionStep.confirm) ...[
              const Text(
                'Giriş yapılmış bir oturum var. Davet edilen doğrulanmış e-posta hesabıyla giriş yaptığınızdan emin olun. Bu bilgi henüz davetin geçerli olduğunu göstermez.',
              ),
              FilledButton(
                onPressed: controller.busy ? null : _accept,
                child: Text(
                  controller.acceptanceUncertain
                      ? 'Kabul isteğini bilerek yeniden dene'
                      : 'Daveti kabul et',
                ),
              ),
              TextButton(
                onPressed: controller.busy ? null : _logout,
                child: const Text('Farklı hesap için çıkış yap'),
              ),
              const Text(
                'Çıkış yapılırsa davet bilgisi temizlenir; yeniden girmeniz gerekir.',
              ),
            ],
            if (controller.step == RedemptionStep.accepted)
              const Text(
                'Kabul, sunucu tarafından doğrulandı. Erişimler yalnızca sunucudan alınır.',
              ),
            if ((controller.step == RedemptionStep.confirm ||
                    controller.step == RedemptionStep.accepted) &&
                controller.authenticated)
              TextButton(
                onPressed: controller.busy ? null : controller.refreshContexts,
                child: const Text('Hesap erişimlerini yenile'),
              ),
            if (controller.contextsLoaded && controller.authenticated)
              FilledButton(
                onPressed: controller.busy ? null : () => _leave(session: true),
                child: Text(
                  controller.acceptedConfirmed
                      ? 'Hesabıma devam et'
                      : 'Mevcut erişimlerimle devam et',
                ),
              ),
            if (controller.step == RedemptionStep.accepted &&
                !controller.authenticated)
              TextButton(
                onPressed: () => _leave(login: true),
                child: const Text('Yeniden giriş yap'),
              ),
            if (controller.step == RedemptionStep.closed &&
                controller.logoutNeedsRetry)
              TextButton(
                onPressed: controller.busy ? null : _logout,
                child: const Text('Çıkışı yeniden dene'),
              ),
            if (controller.busy)
              const Center(child: CircularProgressIndicator()),
          ],
        ),
      ),
    };
  }

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: false,
    onPopInvokedWithResult: (didPop, _) {
      if (!didPop) _leave();
    },
    child: Scaffold(
      body: LayoutBuilder(
        builder: (context, constraints) => Column(
          children: [
            Expanded(
              child: MediaQuery.removeViewInsets(
                context: context,
                removeBottom: true,
                child: _body(),
              ),
            ),
            ConstrainedBox(
              constraints: BoxConstraints(
                maxHeight: constraints.maxHeight * .35,
              ),
              child: SingleChildScrollView(
                child: SafeArea(
                  top: false,
                  child: Material(
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        if (_controller.message case final message?)
                          Padding(
                            padding: const EdgeInsets.all(12),
                            child: Semantics(
                              liveRegion: true,
                              child: Text(message),
                            ),
                          ),
                        if (_controller.registrationUncertain &&
                            !_controller.busy)
                          TextButton(
                            onPressed: () => _controller.showVerification(),
                            child: const Text('Önce e-posta doğrulamayı dene'),
                          ),
                        const Text(
                          'Akışı iptal etmek, sunucuya gönderilmiş bir isteği geri almaz.',
                        ),
                        TextButton(
                          onPressed: () => _leave(),
                          child: const Text('Davet akışını iptal et'),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    ),
  );
}
