import 'dart:async';

import 'package:flutter/material.dart';

import '../../../app/routing/app_router.dart';
import '../domain/auth_service.dart';
import '../domain/session_service.dart';
import 'widgets/auth_layout.dart';

/// No business screens or roles are inferred from a token or registration choice.
class SessionScreen extends StatefulWidget {
  const SessionScreen({super.key, required this.service});
  final SessionService? service;
  @override
  State<SessionScreen> createState() => _SessionScreenState();
}

class _SessionScreenState extends State<SessionScreen>
    with WidgetsBindingObserver {
  Timer? _timer;
  bool _checking = false;
  bool _busy = false;
  String? _message;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _timer = Timer.periodic(const Duration(seconds: 30), (_) => _check());
    WidgetsBinding.instance.addPostFrameCallback((_) => _check());
  }

  @override
  void dispose() {
    _timer?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) _check();
  }

  Future<void> _check() async {
    if (_busy || _checking || !mounted) return;
    _checking = true;
    final result = await widget.service?.refreshSession();
    _checking = false;
    if (!mounted || (result != null && result is! AuthFailure<void>)) return;
    Navigator.pushNamedAndRemoveUntil(context, AppRouter.login, (_) => false);
  }

  Future<void> _logout() async {
    setState(() {
      _busy = true;
      _message = null;
    });
    final result = await widget.service?.logout();
    if (!mounted) return;
    if (result is AuthFailure<void> &&
        result.error.code == AuthErrorCode.storage) {
      setState(() {
        _busy = false;
        _message = authErrorMessage(result.error);
      });
      return;
    }
    Navigator.pushNamedAndRemoveUntil(context, AppRouter.login, (_) => false);
    if (result is AuthFailure<void>) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Yerel oturum kapatıldı. Sunucuda çıkış doğrulanamadı.',
          ),
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) => AuthLayout(
    title: 'Oturum açıldı',
    subtitle: 'İşletme ve rol ekranları henüz kullanıma açık değil.',
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (_message != null) Text(_message!),
        FilledButton(
          onPressed: _busy ? null : _logout,
          child: const Text('Çıkış yap'),
        ),
      ],
    ),
  );
}
