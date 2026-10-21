import 'package:flutter/material.dart';

import '../../../app/routing/app_router.dart';
import '../domain/auth_submission.dart';
import 'widgets/auth_layout.dart';

class AuthFormScreen extends StatefulWidget {
  const AuthFormScreen({
    super.key,
    required this.kind,
    this.submission = const UnavailableAuthSubmission(),
  });

  final AuthFormKind kind;
  final AuthSubmission submission;

  @override
  State<AuthFormScreen> createState() => _AuthFormScreenState();
}

class _AuthFormScreenState extends State<AuthFormScreen> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _business = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _confirmation = TextEditingController();
  bool _busy = false;
  bool _hidePassword = true;
  bool _hideConfirmation = true;
  String? _error;

  bool get _login => widget.kind == AuthFormKind.login;
  bool get _owner => widget.kind == AuthFormKind.businessRegistration;
  String get _title => _login
      ? 'Tekrar hoş geldiniz'
      : _owner
      ? 'İşletme kaydı'
      : 'Müşteri kaydı';

  @override
  void dispose() {
    for (final controller in [
      _name,
      _business,
      _email,
      _password,
      _confirmation,
    ]) {
      controller.dispose();
    }
    super.dispose();
  }

  String? _required(String? value, String label) =>
      value == null || value.trim().isEmpty ? '$label alanını doldurun.' : null;

  Future<void> _submit() async {
    if (_busy || !_formKey.currentState!.validate()) return;
    FocusScope.of(context).unfocus();
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.submission.submit(
        AuthFormInput(
          kind: widget.kind,
          email: _email.text.trim(),
          password: _password.text,
          fullName: _login ? null : _name.text.trim(),
          businessName: _owner ? _business.text.trim() : null,
        ),
      );
      // This UI-only boundary never establishes a session, even if it returns.
      if (mounted) {
        setState(
          () => _error =
              'İşlem şu anda tamamlanamıyor. Lütfen daha sonra tekrar deneyin.',
        );
      }
    } on AuthServiceUnavailable {
      if (mounted) {
        setState(
          () => _error = _login
              ? 'Giriş hizmeti henüz kullanıma açık değil. Şu anda giriş yapılamıyor.'
              : 'Kayıt hizmeti henüz kullanıma açık değil. Hesabınız oluşturulmadı.',
        );
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'İşlem tamamlanamadı. Lütfen tekrar deneyin.');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Widget _passwordField({bool confirmation = false}) {
    final hidden = confirmation ? _hideConfirmation : _hidePassword;
    return TextFormField(
      key: ValueKey(confirmation ? 'confirmation' : 'password'),
      controller: confirmation ? _confirmation : _password,
      enabled: !_busy,
      obscureText: hidden,
      autocorrect: false,
      enableSuggestions: false,
      keyboardType: TextInputType.visiblePassword,
      autofillHints: confirmation
          ? null
          : [_login ? AutofillHints.password : AutofillHints.newPassword],
      textInputAction: !_login && !confirmation
          ? TextInputAction.next
          : TextInputAction.done,
      onFieldSubmitted: (_) {
        if (_login || confirmation) _submit();
      },
      decoration: InputDecoration(
        labelText: confirmation ? 'Şifre tekrarı' : 'Şifre',
        prefixIcon: const Icon(Icons.lock_outline),
        suffixIcon: IconButton(
          tooltip: hidden ? 'Şifreyi göster' : 'Şifreyi gizle',
          onPressed: _busy
              ? null
              : () => setState(() {
                  if (confirmation) {
                    _hideConfirmation = !hidden;
                  } else {
                    _hidePassword = !hidden;
                  }
                }),
          icon: Icon(
            hidden ? Icons.visibility_outlined : Icons.visibility_off_outlined,
          ),
        ),
      ),
      validator: (value) {
        if (value == null || value.isEmpty) {
          return confirmation ? 'Şifrenizi tekrar girin.' : 'Şifrenizi girin.';
        }
        if (confirmation && value != _password.text) {
          return 'Şifreler eşleşmiyor.';
        }
        return null;
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    return AuthLayout(
      title: _title,
      subtitle: _login
          ? 'WashFlow hesabınızla giriş yapın.'
          : _owner
          ? 'İşletme ve hesap bilgilerinizi girin.'
          : 'Kişisel hesap bilgilerinizi girin.',
      child: AutofillGroup(
        onDisposeAction: AutofillContextAction.cancel,
        child: Form(
          key: _formKey,
          autovalidateMode: AutovalidateMode.onUserInteraction,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (_owner) ...[
                TextFormField(
                  key: const ValueKey('businessName'),
                  controller: _business,
                  enabled: !_busy,
                  textCapitalization: TextCapitalization.words,
                  textInputAction: TextInputAction.next,
                  decoration: const InputDecoration(
                    labelText: 'İşletme adı',
                    prefixIcon: Icon(Icons.storefront_outlined),
                  ),
                  validator: (value) => _required(value, 'İşletme adı'),
                ),
                const SizedBox(height: 18),
              ],
              if (!_login) ...[
                TextFormField(
                  key: const ValueKey('fullName'),
                  controller: _name,
                  enabled: !_busy,
                  textCapitalization: TextCapitalization.words,
                  textInputAction: TextInputAction.next,
                  autofillHints: const [AutofillHints.name],
                  decoration: InputDecoration(
                    labelText: _owner
                        ? 'İşletme sahibinin adı soyadı'
                        : 'Ad soyad',
                    prefixIcon: const Icon(Icons.person_outline),
                  ),
                  validator: (value) => _required(value, 'Ad soyad'),
                ),
                const SizedBox(height: 18),
              ],
              TextFormField(
                key: const ValueKey('email'),
                controller: _email,
                enabled: !_busy,
                keyboardType: TextInputType.emailAddress,
                textInputAction: TextInputAction.next,
                autofillHints: const [AutofillHints.email],
                autocorrect: false,
                enableSuggestions: false,
                decoration: const InputDecoration(
                  labelText: 'E-posta',
                  prefixIcon: Icon(Icons.mail_outline),
                ),
                validator: (value) {
                  if (value == null || value.trim().isEmpty) {
                    return 'E-posta adresinizi girin.';
                  }
                  if (!RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$')
                      .hasMatch(value.trim())) {
                    return 'Geçerli bir e-posta adresi girin.';
                  }
                  return null;
                },
              ),
              const SizedBox(height: 18),
              _passwordField(),
              if (!_login) ...[
                const SizedBox(height: 18),
                _passwordField(confirmation: true),
              ],
              if (_login)
                Align(
                  alignment: Alignment.centerRight,
                  child: TextButton(
                    onPressed: _busy
                        ? null
                        : () => showDialog<void>(
                            context: context,
                            builder: (context) => AlertDialog(
                              title: const Text('Şifre yenileme'),
                              content: const Text(
                                'Şifre yenileme hizmeti henüz kullanıma açık değil.',
                              ),
                              actions: [
                                TextButton(
                                  onPressed: () => Navigator.pop(context),
                                  child: const Text('Tamam'),
                                ),
                              ],
                            ),
                          ),
                    child: const Text('Şifremi unuttum'),
                  ),
                ),
              const SizedBox(height: 20),
              if (_error != null) ...[
                Semantics(
                  liveRegion: true,
                  child: Container(
                    padding: const EdgeInsets.all(16),
                    decoration: BoxDecoration(
                      color: Theme.of(context).colorScheme.errorContainer,
                      borderRadius: BorderRadius.circular(16),
                    ),
                    child: Text(
                      _error!,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.onErrorContainer,
                      ),
                    ),
                  ),
                ),
                const SizedBox(height: 20),
              ],
              FilledButton(
                key: const ValueKey('submit'),
                onPressed: _busy ? null : _submit,
                child: _busy
                    ? const Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          SizedBox(
                            width: 18,
                            height: 18,
                            child: CircularProgressIndicator(
                              strokeWidth: 2,
                              semanticsLabel: 'İşlem sürüyor',
                            ),
                          ),
                          SizedBox(width: 12),
                          Text('İşlem sürüyor…'),
                        ],
                      )
                    : Text(_login ? 'Giriş yap' : 'Kayıt ol'),
              ),
              const SizedBox(height: 12),
              TextButton(
                onPressed: _busy
                    ? null
                    : () => Navigator.pushReplacementNamed(
                        context,
                        _login ? AppRouter.register : AppRouter.login,
                      ),
                child: Text(
                  _login
                      ? 'Hesabınız yok mu? Kayıt ol'
                      : 'Zaten hesabınız var mı? Giriş yap',
                ),
              ),
              const SizedBox(height: 16),
              Text(
                'Bu sürümde giriş ve kayıt hizmetleri henüz kullanıma açık değildir.',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ),
        ),
      ),
    );
  }
}
