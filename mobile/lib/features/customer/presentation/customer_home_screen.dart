import 'package:flutter/material.dart';

class CustomerHomeScreen extends StatelessWidget {
  const CustomerHomeScreen({super.key, this.onVehicles});
  final VoidCallback? onVehicles;
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const Text('Müşteri alanı'),
      if (onVehicles != null)
        FilledButton(onPressed: onVehicles, child: const Text('Araçlarım')),
      const Text('Randevu işlemleri henüz kullanıma açık değil.'),
    ],
  );
}
