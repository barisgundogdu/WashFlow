import 'package:flutter/material.dart';

import '../../../app/routing/app_router.dart';
import '../domain/auth_service.dart';
import '../domain/session_service.dart';
import 'widgets/auth_layout.dart';

class VerificationScreen extends StatefulWidget {
  const VerificationScreen({super.key, required this.service, this.email = ''});
  final SessionService? service;
  final String email;
  @override
  State<VerificationScreen> createState() => _VerificationScreenState();
}

class _VerificationScreenState extends State<VerificationScreen> {
  late final _email = TextEditingController(text: widget.email);
  final _token = TextEditingController();
  bool _busy = false;
  bool _verified = false;
  String? _message;
  @override
  void dispose() {
    _email.dispose();
    _token.dispose();
    super.dispose();
  }

  Future<void> _run(bool verify) async {
    if (_busy || _verified) return;
    if ((verify ? _token.text : _email.text).trim().isEmpty) {
      setState(
        () => _message = verify
            ? 'E-postadaki doğrulama bilgisini girin.'
            : 'E-posta adresinizi girin.',
      );
      return;
    }
    setState(() {
      _busy = true;
      _message = null;
    });
    try {
      final service = widget.service;
      final result = service == null
          ? AuthFailure<void>(AuthError(code: AuthErrorCode.unavailable))
          : await (verify
                ? service.verifyEmail(_token.text)
                : service.resendVerification(_email.text));
      if (!mounted) return;
      setState(() {
        _message = switch (result) {
          AuthFailure<void>(:final error)
              when verify &&
                  {
                    AuthErrorCode.network,
                    AuthErrorCode.timeout,
                    AuthErrorCode.unexpected,
                  }.contains(error.code) =>
            'Doğrulama sonucu alınamadı. İşlem tamamlanmış olabilir. Önce giriş yapmayı deneyin; giriş yapamazsanız yeni doğrulama e-postası isteyin.',
          AuthFailure<void>(:final error) => authErrorMessage(error),
          _ =>
            verify
                ? 'E-posta adresiniz doğrulandı. Giriş yapabilirsiniz.'
                : 'Adres uygunsa doğrulama e-postası gönderilecektir.',
        };
        // A confirmed success is terminal for this screen. Do not allow a later
        // resend or replay to replace it with an error about a consumed token.
        if (verify && result is AuthSuccess<void>) _verified = true;
        if (verify) _token.clear();
      });
    } catch (_) {
      if (mounted) {
        setState(
          () => _message = 'İşlem tamamlanamadı. Lütfen tekrar deneyin.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AuthLayout(
    title: 'E-posta doğrulama',
    subtitle: 'Kayıt talebiniz alındıysa erişim için e-posta doğrulaması gerekir. E-postadaki doğrulama bilgisini girin; ardından giriş yapın.',
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextField(
          controller: _token,
          enabled: !_busy && !_verified,
          obscureText: true,
          autocorrect: false,
          enableSuggestions: false,
          decoration: const InputDecoration(labelText: 'Doğrulama bilgisi'),
        ),
        const SizedBox(height: 16),
        FilledButton(
          onPressed: _busy || _verified ? null : () => _run(true),
          child: const Text('E-postayı doğrula'),
        ),
        const SizedBox(height: 24),
        TextField(
          controller: _email,
          enabled: !_busy && !_verified,
          keyboardType: TextInputType.emailAddress,
          decoration: const InputDecoration(labelText: 'E-posta'),
        ),
        TextButton(
          onPressed: _busy || _verified ? null : () => _run(false),
          child: const Text('Doğrulama e-postasını yeniden gönder'),
        ),
        if (_message != null)
          Semantics(liveRegion: true, child: Text(_message!)),
        TextButton(
          onPressed: _busy
              ? null
              : () => Navigator.pushNamedAndRemoveUntil(
                  context,
                  AppRouter.login,
                  (_) => false,
                ),
          child: const Text('Girişe dön'),
        ),
      ],
    ),
  );
}
