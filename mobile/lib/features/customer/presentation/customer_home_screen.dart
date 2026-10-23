import 'package:flutter/material.dart';

/// Temporary destination; customer operations are not implemented yet.
class CustomerHomeScreen extends StatelessWidget {
  const CustomerHomeScreen({super.key});
  @override
  Widget build(BuildContext context) => const Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text('Müşteri alanı'),
      Text(
        'Geçici ekran. Araç ve randevu işlemleri henüz kullanıma açık değil.',
      ),
    ],
  );
}
