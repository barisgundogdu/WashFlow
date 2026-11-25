import 'package:flutter/material.dart';

class CustomerHomeScreen extends StatelessWidget {
  const CustomerHomeScreen({super.key, this.onVehicles, this.onBookings});
  final VoidCallback? onVehicles, onBookings;
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const Text('Müşteri alanı'),
      if (onVehicles != null)
        FilledButton(onPressed: onVehicles, child: const Text('Araçlarım')),
      if (onBookings != null)
        FilledButton(
          onPressed: onBookings,
          child: const Text('Randevularım ve işletmeler'),
        ),
    ],
  );
}
