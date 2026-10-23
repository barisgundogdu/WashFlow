import 'package:flutter/material.dart';

import '../../../app/routing/app_router.dart';

class WelcomeScreen extends StatelessWidget {
  const WelcomeScreen({super.key, this.redemptionEnabled = false});
  final bool redemptionEnabled;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 480),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Icon(
                    Icons.local_car_wash_outlined,
                    size: 72,
                    color: theme.colorScheme.primary,
                  ),
                  const SizedBox(height: 24),
                  Text(
                    'WashFlow',
                    style: theme.textTheme.headlineLarge,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 12),
                  Text(
                    'WashFlow’a hoş geldiniz',
                    style: theme.textTheme.titleLarge,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 16),
                  const Text(
                    'Aracınız ve işletmeniz için tek bir uygulama.',
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 40),
                  FilledButton(
                    onPressed: () =>
                        Navigator.pushNamed(context, AppRouter.login),
                    child: const Text('Giriş yap'),
                  ),
                  const SizedBox(height: 12),
                  OutlinedButton(
                    style: OutlinedButton.styleFrom(
                      minimumSize: const Size(0, 56),
                    ),
                    onPressed: () =>
                        Navigator.pushNamed(context, AppRouter.register),
                    child: const Text('Kayıt ol'),
                  ),
                  if (redemptionEnabled)
                    TextButton(
                      onPressed: () => Navigator.pushNamedAndRemoveUntil(
                        context,
                        AppRouter.redeem,
                        (_) => false,
                      ),
                      child: const Text('Davet koduyla katıl'),
                    ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
