import 'dart:async';

import 'package:flutter/material.dart';

import '../../auth/domain/auth_service.dart';
import '../domain/business_settings.dart';
import 'business_settings_controller.dart';

const settingsLabels = <String, String>{
  'name': 'İşletme adı',
  'phone': 'Herkese açık telefon',
  'contactEmail': 'İletişim e-postası',
  'addressLine1': 'Adres satırı 1',
  'addressLine2': 'Adres satırı 2',
  'district': 'İlçe / semt',
  'city': 'Şehir',
  'region': 'Bölge / il',
  'postalCode': 'Posta kodu',
  'countryCode': 'Ülke kodu (iki harf)',
  'description': 'İşletme açıklaması',
  'timeZoneId': 'IANA saat dilimi',
};
const weekdays = [
  'Pazartesi',
  'Salı',
  'Çarşamba',
  'Perşembe',
  'Cuma',
  'Cumartesi',
  'Pazar',
];

class BusinessSettingsScreen extends StatefulWidget {
  const BusinessSettingsScreen({
    super.key,
    required this.businessId,
    required this.service,
    required this.isCurrent,
    required this.onAuthorizationFailure,
    required this.onSwitch,
    required this.onLogout,
  });
  final String businessId;
  final BusinessSettingsService service;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  final VoidCallback onSwitch, onLogout;
  @override
  State<BusinessSettingsScreen> createState() => _BusinessSettingsScreenState();
}

class _BusinessSettingsScreenState extends State<BusinessSettingsScreen> {
  bool _allowPop = false, _confirming = false;
  late final c = BusinessSettingsController(
    businessId: widget.businessId,
    service: widget.service,
    isCurrent: widget.isCurrent,
    onAuthorizationFailure: widget.onAuthorizationFailure,
  );
  @override
  void initState() {
    super.initState();
    c.addListener(_changed);
    unawaited(c.load());
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

  Future<bool> _discard() async {
    if (!c.dirty) return true;
    if (_confirming || !widget.isCurrent()) return false;
    _confirming = true;
    final discard =
        await showDialog<bool>(
          context: context,
          useRootNavigator: false,
          builder: (d) => AlertDialog(
            title: const Text('Kaydedilmemiş değişiklikler'),
            content: const Text(
              'Değişiklikler silinsin mi? Gönderilmiş bir istek geri alınamaz.',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(d, false),
                child: const Text('Düzenlemeye dön'),
              ),
              FilledButton(
                onPressed: () => Navigator.pop(d, true),
                child: const Text('Değişiklikleri sil'),
              ),
            ],
          ),
        ) ??
        false;
    _confirming = false;
    return discard;
  }

  Future<void> _reload() async {
    if (await _discard() && mounted && widget.isCurrent()) await c.load();
  }

  Future<void> _leave(VoidCallback action) async {
    if (await _discard() && mounted && widget.isCurrent()) action();
  }

  String _time(int? minute) => minute == null
      ? 'Seçin'
      : '${(minute ~/ 60).toString().padLeft(2, '0')}:${(minute % 60).toString().padLeft(2, '0')}';
  Future<void> _pick(OpeningDay day, bool opening) async {
    final old = opening ? day.opensAtMinute : day.closesAtMinute;
    final result = await showTimePicker(
      initialEntryMode: TimePickerEntryMode.input,
      context: context,
      useRootNavigator: false,
      initialTime: TimeOfDay(
        hour: old == null || old == 1440 ? 0 : old ~/ 60,
        minute: old == null ? 0 : old % 60,
      ),
      builder: (context, child) => MediaQuery(
        data: MediaQuery.of(context).copyWith(alwaysUse24HourFormat: true),
        child: child!,
      ),
    );
    if (result == null || !mounted || !widget.isCurrent() || !c.canEdit) return;
    final minute = result.hour * 60 + result.minute;
    _day(
      OpeningDay(
        day.day,
        false,
        opening ? minute : day.opensAtMinute,
        opening ? day.closesAtMinute : minute,
      ),
    );
  }

  void _day(OpeningDay day) => c.hours([
    for (final d in c.draft!.weeklyHours!)
      if (d.day == day.day) day else d,
  ]);
  @override
  Widget build(BuildContext context) {
    final profile = c.draft;
    return PopScope(
      canPop: _allowPop || (!c.dirty && !c.saving),
      onPopInvokedWithResult: (didPop, result) async {
        if (!didPop && await _discard() && mounted && widget.isCurrent()) {
          setState(() => _allowPop = true);
          WidgetsBinding.instance.addPostFrameCallback((_) {
            if (mounted && widget.isCurrent()) Navigator.of(this.context).pop();
          });
        }
      },
      child: Scaffold(
        appBar: AppBar(title: const Text('İşletme ayarları')),
        body: SafeArea(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: Center(
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 650),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    if (c.loading)
                      const Center(child: CircularProgressIndicator()),
                    if (c.message case final message?)
                      Padding(
                        padding: const EdgeInsets.symmetric(vertical: 12),
                        child: Semantics(
                          liveRegion: true,
                          child: Text(message),
                        ),
                      ),
                    if (profile == null && !c.loading && !c.blocked)
                      const Text('İşletme bilgileri yüklenemedi.'),
                    if (profile != null) ...[
                      Text(
                        c.persisted?.fields['name'] ?? '',
                        style: Theme.of(context).textTheme.headlineSmall,
                      ),
                      const Text(
                        'Bu ayarlar yalnızca seçili işletmeye aittir. Boş alanlar henüz yapılandırılmamıştır.',
                      ),
                      for (final field in settingsLabels.entries)
                        Padding(
                          padding: const EdgeInsets.only(top: 16),
                          child: TextFormField(
                            key: ValueKey('${c.epoch}-${field.key}'),
                            initialValue: profile.fields[field.key] ?? '',
                            enabled: c.canEdit,
                            maxLength: settingsLimits[field.key],
                            maxLines: field.key == 'description' ? 3 : 1,
                            keyboardType: field.key == 'phone'
                                ? TextInputType.phone
                                : field.key == 'contactEmail'
                                ? TextInputType.emailAddress
                                : TextInputType.text,
                            decoration: InputDecoration(
                              labelText: field.value,
                              helperText: field.key == 'timeZoneId'
                                  ? 'Örnek biçim: Europe/Istanbul. İşletmenizin gerçek saat dilimini girin.'
                                  : null,
                            ),
                            onChanged: (v) => c.field(field.key, v),
                          ),
                        ),
                      const Divider(height: 32),
                      Text(
                        'Haftalık çalışma saatleri',
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      const Text(
                        'Yerel saatlerdir. Tek aralık, aynı gün; geceye taşan saatler desteklenmez. Kapanış için 24:00 seçilebilir.',
                      ),
                      SwitchListTile(
                        title: const Text('Haftalık saatleri yapılandır'),
                        subtitle: Text(
                          profile.weeklyHours == null
                              ? 'Henüz tanımlanmadı'
                              : 'Yedi gün yapılandırılıyor',
                        ),
                        value: profile.weeklyHours != null,
                        onChanged: c.canEdit
                            ? (v) => c.hours(
                                v
                                    ? [
                                        for (var i = 1; i <= 7; i++)
                                          OpeningDay(i, true, null, null),
                                      ]
                                    : null,
                              )
                            : null,
                      ),
                      for (final day in profile.weeklyHours ?? <OpeningDay>[])
                        Card(
                          child: Padding(
                            padding: const EdgeInsets.all(12),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                SwitchListTile(
                                  contentPadding: EdgeInsets.zero,
                                  title: Text(weekdays[day.day - 1]),
                                  subtitle: Text(
                                    day.closed ? 'Kapalı' : 'Açık',
                                  ),
                                  value: !day.closed,
                                  onChanged: c.canEdit
                                      ? (v) => _day(
                                          OpeningDay(day.day, !v, null, null),
                                        )
                                      : null,
                                ),
                                if (!day.closed)
                                  Wrap(
                                    spacing: 12,
                                    runSpacing: 8,
                                    children: [
                                      OutlinedButton(
                                        onPressed: c.canEdit
                                            ? () => _pick(day, true)
                                            : null,
                                        child: Text(
                                          'Açılış: ${_time(day.opensAtMinute)}',
                                        ),
                                      ),
                                      OutlinedButton(
                                        onPressed: c.canEdit
                                            ? () => _pick(day, false)
                                            : null,
                                        child: Text(
                                          'Kapanış: ${_time(day.closesAtMinute)}',
                                        ),
                                      ),
                                      TextButton(
                                        onPressed: c.canEdit
                                            ? () => _day(
                                                OpeningDay(
                                                  day.day,
                                                  false,
                                                  day.opensAtMinute,
                                                  1440,
                                                ),
                                              )
                                            : null,
                                        child: const Text('Gün sonu 24:00'),
                                      ),
                                    ],
                                  ),
                              ],
                            ),
                          ),
                        ),
                      if (c.dirty)
                        const Text('Kaydedilmemiş değişiklikler var.'),
                      FilledButton(
                        onPressed: c.canEdit && c.dirty && !c.requiresReload
                            ? c.save
                            : null,
                        child: Text(
                          c.saving ? 'Kaydediliyor…' : 'Değişiklikleri kaydet',
                        ),
                      ),
                    ],
                    TextButton(
                      onPressed: !c.loading && !c.saving && !c.blocked
                          ? _reload
                          : null,
                      child: const Text('Güncel kaydı yükle'),
                    ),
                    TextButton(
                      onPressed: () => _leave(widget.onSwitch),
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
      ),
    );
  }
}
