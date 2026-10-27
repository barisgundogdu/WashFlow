import 'dart:async';

import 'package:flutter/material.dart';

import '../../auth/domain/auth_service.dart';
import '../domain/customer_vehicles.dart';
import 'management_controllers.dart';
import 'record_editor_screen.dart';

class CustomerDirectoryScreen extends StatefulWidget {
  const CustomerDirectoryScreen({
    super.key,
    required this.service,
    required this.business,
    required this.isCurrent,
    required this.onAuthorizationFailure,
    required this.onSwitch,
    required this.onLogout,
    this.customer,
  });
  final CustomerVehicleService service;
  final String? business, customer;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  final VoidCallback onSwitch, onLogout;
  @override
  State<CustomerDirectoryScreen> createState() =>
      _CustomerDirectoryScreenState();
}

class _CustomerDirectoryScreenState extends State<CustomerDirectoryScreen> {
  late final c = DirectoryController(
    service: widget.service,
    business: widget.business,
    customer: widget.customer,
    isCurrent: widget.isCurrent,
    onAuthorizationFailure: widget.onAuthorizationFailure,
  );
  LocalCustomerRecord? detail;
  bool detailLoading = false;
  int _detailGeneration = 0;
  String? notice;
  @override
  void initState() {
    super.initState();
    c.addListener(_changed);
    unawaited(_load());
  }

  void _changed() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    _detailGeneration++;
    detail = null;
    c.removeListener(_changed);
    c.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    notice = null;
    if (widget.customer != null) {
      final g = ++_detailGeneration;
      c.invalidate();
      setState(() {
        detail = null;
        detailLoading = true;
      });
      AuthResult<LocalCustomerRecord> r;
      try {
        r = await widget.service.customer(widget.business!, widget.customer!);
      } catch (_) {
        r = AuthFailure(AuthError(code: AuthErrorCode.unexpected));
      }
      if (!mounted || !widget.isCurrent() || g != _detailGeneration) return;
      switch (r) {
        case AuthSuccess<LocalCustomerRecord>(:final value):
          if (value.businessId != widget.business ||
              value.id != widget.customer) {
            setState(() {
              detail = null;
              detailLoading = false;
              notice = 'Müşteri kaydı doğrulanamadı.';
            });
            return;
          }
          detail = value;
        case AuthFailure<LocalCustomerRecord>(:final error):
          if ({
            AuthErrorCode.unauthenticated,
            AuthErrorCode.invalidCredentials,
            AuthErrorCode.forbidden,
          }.contains(error.code)) {
            await widget.onAuthorizationFailure(error);
            return;
          }
          setState(() {
            detail = null;
            detailLoading = false;
            notice = managementError(error);
          });
          return;
      }
      setState(() => detailLoading = false);
    }
    await c.load();
  }

  Future<void> _edit({
    bool vehicle = false,
    VehicleRecord? item,
    LocalCustomerRecord? customer,
  }) async {
    if (!widget.isCurrent() || c.blocked || c.loading || detailLoading) return;
    final result = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => RecordEditorScreen(
          service: widget.service,
          business: widget.business,
          customer: widget.customer ?? item?.localCustomerId,
          isVehicle: vehicle,
          isCurrent: widget.isCurrent,
          onAuthorizationFailure: widget.onAuthorizationFailure,
          vehicle: item,
          customerRecord: customer,
        ),
      ),
    );
    if (!mounted || !widget.isCurrent()) return;
    // Always reconcile with GET, including a cancelled uncertain mutation. Never replay a write.
    await _load();
    if (mounted && widget.isCurrent() && result == true) {
      setState(() => notice = 'İşlem sunucu tarafından onaylandı.');
    }
  }

  Future<void> _detail(LocalCustomerRecord customer) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => CustomerDirectoryScreen(
          service: widget.service,
          business: widget.business,
          customer: customer.id,
          isCurrent: widget.isCurrent,
          onAuthorizationFailure: widget.onAuthorizationFailure,
          onSwitch: widget.onSwitch,
          onLogout: widget.onLogout,
        ),
      ),
    );
    if (mounted && widget.isCurrent()) await c.load();
  }

  Widget _vehicle(VehicleRecord v) => Card(
    child: ListTile(
      title: Text(v.plate),
      subtitle: Text(
        '${v.brand} ${v.model} · ${vehicleCategories[v.category]}\n${v.customerName.isEmpty ? 'Kişisel aracınız' : v.customerName}\nKayıt: ${v.id.substring(0, 8)}…${v.id.substring(v.id.length - 6)}',
      ),
      isThreeLine: true,
      trailing: const Icon(Icons.edit_outlined),
      onTap: () => _edit(vehicle: true, item: v),
    ),
  );
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(
        widget.business == null
            ? 'Araçlarım'
            : widget.customer != null
            ? 'Müşteri ayrıntıları'
            : 'Müşteriler ve araçlar',
      ),
    ),
    body: SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(24),
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 750),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (detailLoading)
                  const Center(child: CircularProgressIndicator()),
                if (detail case final d?) ...[
                  Text(
                    d.fullName,
                    style: Theme.of(context).textTheme.headlineSmall,
                  ),
                  if (d.phone.isNotEmpty) Text(d.phone),
                  if (d.email.isNotEmpty) Text(d.email),
                  if (d.notes.isNotEmpty) Text(d.notes),
                  TextButton(
                    onPressed: () => _edit(customer: d),
                    child: const Text('Müşteriyi düzenle'),
                  ),
                ],
                if (widget.business != null && widget.customer == null)
                  SegmentedButton<bool>(
                    segments: const [
                      ButtonSegment(value: false, label: Text('Müşteriler')),
                      ButtonSegment(value: true, label: Text('Araçlar')),
                    ],
                    selected: {c.vehicleMode},
                    onSelectionChanged: c.blocked
                        ? null
                        : (s) => c.mode(s.first),
                  ),
                const SizedBox(height: 16),
                TextField(
                  key: const ValueKey('record-search'),
                  enabled: !c.blocked && !detailLoading,
                  maxLength: 100,
                  decoration: InputDecoration(
                    labelText: widget.business == null
                        ? 'Plaka ara'
                        : 'Ad, telefon veya plaka ara',
                    prefixIcon: const Icon(Icons.search),
                  ),
                  onChanged: c.search,
                ),
                if (widget.business != null &&
                    widget.customer == null &&
                    c.vehicleMode)
                  FilledButton(
                    onPressed: c.blocked ? null : () => c.mode(false),
                    child: const Text('Araç eklemek için müşteri seç'),
                  )
                else if (widget.customer == null || detail != null)
                  FilledButton(
                    onPressed: c.loading || c.blocked || detailLoading
                        ? null
                        : () => _edit(vehicle: c.isVehicles),
                    child: Text(c.isVehicles ? 'Araç ekle' : 'Müşteri ekle'),
                  ),
                if (widget.business != null &&
                    c.vehicleMode &&
                    widget.customer == null)
                  const Text('Yeni araç eklemek için önce bir müşteri seçin.'),
                if (notice case final message?)
                  Semantics(liveRegion: true, child: Text(message)),
                if (c.message case final message?)
                  Semantics(liveRegion: true, child: Text(message)),
                if (c.loading)
                  const Center(child: CircularProgressIndicator())
                else if (c.message == null &&
                    c.customers.isEmpty &&
                    c.vehicles.isEmpty)
                  const Padding(
                    padding: EdgeInsets.all(24),
                    child: Text('Kayıt bulunamadı.'),
                  ),
                for (final customer in c.customers)
                  Card(
                    child: ListTile(
                      title: Text(customer.fullName),
                      subtitle: Text(
                        '${customer.phone.isEmpty ? 'Telefon belirtilmedi' : customer.phone}\nKayıt: ${customer.id.substring(0, 8)}…${customer.id.substring(customer.id.length - 6)}',
                      ),
                      trailing: const Icon(Icons.chevron_right),
                      onTap: () => _detail(customer),
                    ),
                  ),
                for (final v in c.vehicles) _vehicle(v),
                if (!c.loading && !c.blocked && !detailLoading)
                  Wrap(
                    alignment: WrapAlignment.spaceBetween,
                    children: [
                      TextButton(
                        onPressed: c.page > 1
                            ? () => c.load(nextPage: c.page - 1)
                            : null,
                        child: const Text('Önceki sayfa'),
                      ),
                      Text('Sayfa ${c.page}'),
                      TextButton(
                        onPressed: c.hasMore
                            ? () => c.load(nextPage: c.page + 1)
                            : null,
                        child: const Text('Sonraki sayfa'),
                      ),
                    ],
                  ),
                TextButton(
                  onPressed: c.loading || c.blocked ? null : _load,
                  child: const Text('Listeyi yenile'),
                ),
                TextButton(
                  onPressed: widget.onSwitch,
                  child: const Text('Hesap değiştir'),
                ),
                TextButton(
                  onPressed: widget.onLogout,
                  child: const Text('Çıkış yap'),
                ),
              ],
            ),
          ),
        ),
      ),
    ),
  );
}
