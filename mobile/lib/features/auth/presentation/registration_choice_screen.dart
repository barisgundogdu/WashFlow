import 'package:flutter/material.dart';

import '../../../app/routing/app_router.dart';
import 'widgets/auth_layout.dart';

class RegistrationChoiceScreen extends StatelessWidget {
  const RegistrationChoiceScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return AuthLayout(
      title: 'WashFlow’a katılın',
      subtitle: 'Size uygun kayıt formuyla başlayın.',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _Choice(
            icon: Icons.person_outline,
            title: 'Müşteri hesabı',
            description: 'Kişisel hesabınız için kayıt formu.',
            onTap: () =>
                Navigator.pushNamed(context, AppRouter.customerRegister),
          ),
          const SizedBox(height: 16),
          _Choice(
            icon: Icons.storefront_outlined,
            title: 'İşletme kaydı',
            description: 'İşletmeniz ve işletme sahibi için kayıt formu.',
            onTap: () =>
                Navigator.pushNamed(context, AppRouter.businessRegister),
          ),
          const SizedBox(height: 24),
          TextButton(
            onPressed: () =>
                Navigator.pushReplacementNamed(context, AppRouter.login),
            child: const Text('Zaten hesabınız var mı? Giriş yap'),
          ),
        ],
      ),
    );
  }
}

class _Choice extends StatelessWidget {
  const _Choice({
    required this.icon,
    required this.title,
    required this.description,
    required this.onTap,
  });
  final IconData icon;
  final String title;
  final String description;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Card(
      margin: EdgeInsets.zero,
      child: InkWell(
        borderRadius: BorderRadius.circular(20),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(20),
          child: Row(
            children: [
              Icon(
                icon,
                color: Theme.of(context).colorScheme.primary,
                size: 28,
              ),
              const SizedBox(width: 16),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(title, style: Theme.of(context).textTheme.titleMedium),
                    const SizedBox(height: 6),
                    Text(description),
                  ],
                ),
              ),
              const Icon(Icons.chevron_right),
            ],
          ),
        ),
      ),
    );
  }
}
