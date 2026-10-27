import 'package:flutter/material.dart';

import '../../auth/domain/auth_service.dart';
import '../domain/customer_vehicles.dart';
import 'management_controllers.dart';

const customerLabels = {
  'fullName': 'Ad soyad',
  'phone': 'Telefon',
  'email': 'E-posta',
  'notes': 'Notlar',
};
const vehicleLabels = {
  'plate': 'Plaka',
  'brand': 'Marka',
  'model': 'Model',
  'category': 'Araç türü',
  'modelYear': 'Model yılı',
  'color': 'Renk',
  'notes': 'Notlar',
};
const fieldLimits = {
  'fullName': 200,
  'phone': 40,
  'email': 320,
  'notes': 2000,
  'plate': 32,
  'brand': 80,
  'model': 100,
  'color': 60,
  'modelYear': 4,
};

class RecordEditorScreen extends StatefulWidget {
  const RecordEditorScreen({
    super.key,
    required this.service,
    required this.business,
    required this.customer,
    required this.isVehicle,
    required this.isCurrent,
    required this.onAuthorizationFailure,
    this.vehicle,
    this.customerRecord,
  });
  final CustomerVehicleService service;
  final String? business, customer;
  final bool isVehicle;
  final VehicleRecord? vehicle;
  final LocalCustomerRecord? customerRecord;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  @override
  State<RecordEditorScreen> createState() => _RecordEditorScreenState();
}

class _RecordEditorScreenState extends State<RecordEditorScreen> {
  late final c = RecordEditorController(
    service: widget.service,
    business: widget.business,
    customer: widget.customer,
    vehicle: widget.vehicle,
    customerRecord: widget.customerRecord,
    isCurrent: widget.isCurrent,
    onAuthorizationFailure: widget.onAuthorizationFailure,
  );
  bool _allowPop = false, _dialog = false;
  @override
  void initState() {
    super.initState();
    c.addListener(_changed);
  }

  void _changed() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    c.removeListener(_changed);
    c.dispose();
    super.dispose();
  }

  Future<bool> _confirm(String title, String message) async {
    if (_dialog || !widget.isCurrent()) return false;
    _dialog = true;
    final result = await showDialog<bool>(
      context: context,
      useRootNavigator: false,
      builder: (d) => AlertDialog(
        title: Text(title),
        content: Text(message),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(d, false),
            child: const Text('Vazgeç'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(d, true),
            child: const Text('Onayla'),
          ),
        ],
      ),
    );
    _dialog = false;
    return result == true;
  }

  Future<void> _back() async {
    if (!c.dirty ||
        c.confirmed ||
        await _confirm(
          'Kaydedilmemiş değişiklikler',
          'Değişiklikler silinsin mi? Gönderilmiş istek geri alınamaz.',
        )) {
      if (mounted && widget.isCurrent()) {
        setState(() => _allowPop = true);
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (mounted && widget.isCurrent()) {
            Navigator.of(context).pop(c.confirmed);
          }
        });
      }
    }
  }

  Future<void> _save() async {
    FocusScope.of(context).unfocus();
    await c.save(isVehicle: widget.isVehicle);
    if (mounted && widget.isCurrent() && c.confirmed) {
      Navigator.of(context).pop(true);
    }
  }

  Future<void> _archive() async {
    if (await _confirm(
      'Aracı arşivle',
      'Araç etkin listeden kaldırılacak; kayıt ve gelecekteki bağlantıları korunur.',
    )) {
      if (mounted && widget.isCurrent()) {
        await c.archive();
        if (mounted && widget.isCurrent() && c.confirmed) {
          Navigator.of(context).pop(true);
        }
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final labels = widget.isVehicle ? vehicleLabels : customerLabels;
    return PopScope(
      canPop: _allowPop || (!c.dirty && !c.saving) || c.confirmed,
      onPopInvokedWithResult: (didPop, result) {
        if (!didPop) _back();
      },
      child: Scaffold(
        appBar: AppBar(
          title: Text(
            widget.isVehicle
                ? (widget.vehicle == null ? 'Araç ekle' : 'Aracı düzenle')
                : (widget.customerRecord == null
                      ? 'Müşteri ekle'
                      : 'Müşteriyi düzenle'),
          ),
        ),
        body: SafeArea(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: Center(
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 650),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    if (widget.isVehicle)
                      const Text(
                        'Aynı plaka birden fazla kayıtta bulunabilir. Plaka eşleşmesi sahiplik kanıtı değildir.',
                      ),
                    if (widget.isVehicle && widget.vehicle != null)
                      Text('Kayıt: ${widget.vehicle!.id}'),
                    for (final e in labels.entries)
                      Padding(
                        padding: const EdgeInsets.only(top: 16),
                        child: e.key == 'category'
                            ? DropdownButtonFormField<String>(
                                key: const ValueKey('category'),
                                initialValue: c.fields['category'],
                                isExpanded: true,
                                decoration: InputDecoration(labelText: e.value),
                                items: [
                                  for (final category
                                      in vehicleCategories.entries)
                                    DropdownMenuItem(
                                      value: category.key,
                                      child: Text(category.value),
                                    ),
                                ],
                                onChanged: c.canEdit
                                    ? (v) => c.edit('category', v ?? '')
                                    : null,
                              )
                            : TextFormField(
                                key: ValueKey(e.key),
                                initialValue: c.fields[e.key] ?? '',
                                enabled: c.canEdit,
                                decoration: InputDecoration(labelText: e.value),
                                maxLength: fieldLimits[e.key],
                                maxLines: e.key == 'notes' ? 3 : 1,
                                keyboardType: e.key == 'modelYear'
                                    ? TextInputType.number
                                    : e.key == 'phone'
                                    ? TextInputType.phone
                                    : e.key == 'email'
                                    ? TextInputType.emailAddress
                                    : TextInputType.text,
                                onChanged: (v) => c.edit(e.key, v),
                              ),
                      ),
                    if (c.message case final message?)
                      Semantics(
                        liveRegion: true,
                        child: Padding(
                          padding: const EdgeInsets.symmetric(vertical: 12),
                          child: Text(message),
                        ),
                      ),
                    if (c.dirty && !c.confirmed)
                      const Text('Kaydedilmemiş değişiklikler var.'),
                    FilledButton(
                      key: const ValueKey('record-save'),
                      onPressed: c.canEdit ? _save : null,
                      child: Text(c.saving ? 'Kaydediliyor…' : 'Kaydet'),
                    ),
                    if (widget.vehicle != null)
                      TextButton(
                        onPressed: c.canEdit ? _archive : null,
                        child: const Text('Aracı arşivle'),
                      ),
                    TextButton(
                      onPressed: _back,
                      child: Text(
                        c.requiresReload
                            ? 'Listeye dön ve kontrol et'
                            : 'İptal',
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
