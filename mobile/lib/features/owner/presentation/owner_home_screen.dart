import 'package:flutter/material.dart';

/// Temporary destination; no operational or invitation actions are exposed.
class OwnerHomeScreen extends StatelessWidget {
  const OwnerHomeScreen({super.key});
  @override
  Widget build(BuildContext context) => const Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text('İşletme sahibi alanı'),
      Text('Geçici ekran. İşletme yönetimi henüz kullanıma açık değil.'),
    ],
  );
}
