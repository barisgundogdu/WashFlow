import 'dart:async';

import 'package:flutter/material.dart';

import '../../auth/domain/auth_service.dart';
import '../../customer_vehicles/domain/customer_vehicles.dart';
import '../domain/service_catalog.dart';
import 'catalog_controller.dart';

class CatalogScreen extends StatefulWidget {
  const CatalogScreen({
    super.key,
    required this.service,
    required this.business,
    required this.businessName,
    required this.isCurrent,
    required this.onAuthorizationFailure,
    required this.onSwitch,
    required this.onLogout,
  });
  final CatalogService service;
  final String business, businessName;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  final Future<void> Function() onSwitch, onLogout;
  @override
  State<CatalogScreen> createState() => _CatalogScreenState();
}

class _CatalogScreenState extends State<CatalogScreen> {
  late CatalogController c;
  @override
  void initState() {
    super.initState();
    c = CatalogController(
      service: widget.service,
      business: widget.business,
      isCurrent: widget.isCurrent,
      onAuthorizationFailure: widget.onAuthorizationFailure,
    )..addListener(_changed);
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

  Future<bool> _confirm(String title, String body) async =>
      await showDialog<bool>(
        context: context,
        useRootNavigator: false,
        builder: (context) => AlertDialog(
          title: Text(title),
          content: Text(body),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Vazgeç'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Onayla'),
            ),
          ],
        ),
      ) ??
      false;
  Future<bool> _discard() async =>
      c.draft == null ||
      await _confirm(
        'Taslak bırakılsın mı?',
        'Kaydedilmemiş değişiklikler silinecek.',
      );
  Future<void> _refresh() async {
    if (await _discard() && mounted && c.active) await c.load();
  }

  Future<void> _back() async {
    if (c.saving || !await _discard() || !mounted || !c.active) return;
    if (c.draft != null || c.selected != null) {
      await c.load();
    } else {
      Navigator.pop(context);
    }
  }

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: false,
    onPopInvokedWithResult: (didPop, result) {
      if (!didPop) unawaited(_back());
    },
    child: Scaffold(
      appBar: AppBar(
        title: const Text('Hizmetler ve Fiyatlar'),
        leading: BackButton(onPressed: c.saving ? null : _back),
        actions: [
          IconButton(
            tooltip: 'Yenile',
            onPressed: c.saving ? null : _refresh,
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: SafeArea(
        child: SingleChildScrollView(
          keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                widget.businessName,
                style: Theme.of(context).textTheme.titleLarge,
              ),
              if (c.message != null)
                Semantics(liveRegion: true, child: Text(c.message!)),
              if (c.loading || c.saving)
                const Center(child: CircularProgressIndicator()),
              if (!c.blocked) ...[
                if (c.draft != null)
                  CatalogEditor(
                    key: ValueKey(c.epoch),
                    draft: c.draft!,
                    enabled: c.canMutate,
                    onSave: () => c.save(c.draft!),
                    onCancel: _refresh,
                  )
                else if (c.selected != null)
                  ..._details()
                else
                  ..._list(),
                if (c.requiresReload)
                  FilledButton(
                    onPressed: c.saving ? null : _refresh,
                    child: const Text('Güncel listeyi yükle'),
                  ),
              ],
              const SizedBox(height: 24),
              TextButton(
                onPressed: c.saving
                    ? null
                    : () async {
                        if (await _discard() && mounted && c.active) {
                          await widget.onSwitch();
                        }
                      },
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
  );
  List<Widget> _list() => [
    TextFormField(
      key: const ValueKey('catalog-search'),
      initialValue: c.query,
      maxLength: 100,
      decoration: const InputDecoration(labelText: 'Hizmet ara'),
      onChanged: c.search,
    ),
    DropdownButtonFormField<String>(
      isExpanded: true,
      initialValue: c.status,
      decoration: const InputDecoration(labelText: 'Durum'),
      items: const [
        DropdownMenuItem(value: 'all', child: Text('Tümü')),
        DropdownMenuItem(value: 'enabled', child: Text('Etkin')),
        DropdownMenuItem(value: 'disabled', child: Text('Devre dışı')),
      ],
      onChanged: c.saving ? null : (v) => c.filter(v!),
    ),
    FilledButton(
      onPressed: c.canMutate ? () => c.edit(create: true) : null,
      child: const Text('Hizmet ekle'),
    ),
    if (c.items.isEmpty) const Text('Gösterilecek hizmet bulunamadı.'),
    for (final s in c.items)
      Card(
        child: ListTile(
          title: Text(s.name),
          subtitle: Text(
            '${s.enabled ? 'Etkin' : 'Devre dışı'} • ${s.published ? 'Müşterilere açık' : 'Yayınlanmamış'}\n${s.offerings.isEmpty ? 'Sunulan araç türü yok' : s.offerings.map((o) => o.price.display).join(' / ')}',
          ),
          onTap: c.canMutate ? () => c.open(s.id) : null,
        ),
      ),
    Wrap(
      crossAxisAlignment: WrapCrossAlignment.center,
      spacing: 8,
      children: [
        TextButton(
          onPressed: c.page > 1 && !c.saving
              ? () => c.load(nextPage: c.page - 1)
              : null,
          child: const Text('Önceki'),
        ),
        Text('Sayfa ${c.page}'),
        TextButton(
          onPressed: c.hasMore && !c.saving
              ? () => c.load(nextPage: c.page + 1)
              : null,
          child: const Text('Sonraki'),
        ),
      ],
    ),
    TextButton(
      onPressed: c.saving ? null : _refresh,
      child: const Text('Yeniden dene'),
    ),
  ];
  List<Widget> _details() {
    final s = c.selected!;
    return [
      Text(s.name, style: Theme.of(context).textTheme.headlineSmall),
      Text(s.description),
      Text(
        '${s.enabled ? 'Etkin' : 'Devre dışı'} • ${s.published ? 'Müşterilere açık' : 'Yayınlanmamış'}',
      ),
      for (final o in s.offerings)
        ListTile(
          title: Text('${vehicleCategories[o.category]} • ${o.price.display}'),
          subtitle: Text(o.estimate),
        ),
      const Text(
        'Süreler tahminidir. Bekleme/kuruma, etkin çalışma alanı kullanımını belirtmez.',
      ),
      FilledButton(
        onPressed: c.canMutate ? () => c.edit() : null,
        child: const Text('Düzenle'),
      ),
      TextButton(
        onPressed: c.canMutate ? () => c.edit(duplicate: true) : null,
        child: const Text('Kopyadan yeni taslak'),
      ),
      TextButton(
        onPressed: c.canMutate
            ? () async {
                if (await _confirm(
                      'Hizmet arşivlensin mi?',
                      'Hizmet yeni işlemlerde kullanılamaz. Geçmiş kayıtlar korunur.',
                    ) &&
                    mounted &&
                    c.canMutate &&
                    identical(s, c.selected)) {
                  await c.archive();
                }
              }
            : null,
        child: const Text('Arşivle'),
      ),
    ];
  }
}

class CatalogEditor extends StatefulWidget {
  const CatalogEditor({
    super.key,
    required this.draft,
    required this.enabled,
    required this.onSave,
    required this.onCancel,
  });
  final CatalogDraft draft;
  final bool enabled;
  final VoidCallback onSave, onCancel;
  @override
  State<CatalogEditor> createState() => _CatalogEditorState();
}

class _CatalogEditorState extends State<CatalogEditor> {
  final form = GlobalKey<FormState>();
  late final TextEditingController name, description;
  final values = <String, List<TextEditingController>>{};
  final offered = <String>{};
  @override
  void initState() {
    super.initState();
    name = TextEditingController(text: widget.draft.name);
    description = TextEditingController(text: widget.draft.description);
    for (final key in vehicleCategories.keys) {
      final rows = widget.draft.offerings.where((o) => o.category == key);
      final o = rows.isEmpty ? null : rows.first;
      if (o != null) offered.add(key);
      values[key] = [
        o?.price.wire.replaceAll('.', ',') ?? '',
        o?.activeMinutes.toString() ?? '',
        o?.waitingMinutes?.toString() ?? '',
        o?.employees.toString() ?? '',
      ].map((s) => TextEditingController(text: s)).toList();
    }
  }

  @override
  void dispose() {
    name.dispose();
    description.dispose();
    for (final row in values.values) {
      for (final controller in row) {
        controller.dispose();
      }
    }
    super.dispose();
  }

  String? _number(String? value, int max, {bool optional = false}) {
    if (optional && (value?.isEmpty ?? true)) return null;
    final n = int.tryParse(value ?? '');
    return n == null || n < (optional ? 0 : 1) || n > max
        ? '${optional ? 0 : 1}–$max arasında tam sayı girin.'
        : null;
  }

  @override
  Widget build(BuildContext context) => Form(
    key: form,
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          widget.draft.id == null ? 'Yeni hizmet taslağı' : 'Hizmeti düzenle',
        ),
        TextFormField(
          controller: name,
          enabled: widget.enabled,
          maxLength: 200,
          decoration: const InputDecoration(labelText: 'Hizmet adı'),
          onChanged: (v) => widget.draft.name = v,
          validator: CatalogText.nameError,
        ),
        TextFormField(
          controller: description,
          enabled: widget.enabled,
          maxLength: 2000,
          minLines: 2,
          maxLines: 5,
          decoration: const InputDecoration(labelText: 'Açıklama'),
          onChanged: (v) => widget.draft.description = v,
          validator: CatalogText.descriptionError,
        ),
        SwitchListTile(
          title: const Text('Hizmet etkin'),
          value: widget.draft.enabled,
          onChanged: widget.enabled
              ? (v) => setState(() {
                  widget.draft.enabled = v;
                  if (!v) widget.draft.published = false;
                })
              : null,
        ),
        SwitchListTile(
          title: const Text('Müşterilere yayınla'),
          value: widget.draft.published,
          onChanged: widget.enabled && widget.draft.enabled
              ? (v) => setState(() => widget.draft.published = v)
              : null,
        ),
        const Text(
          'Yalnızca sunduğunuz araç türlerini seçin. Sıfır fiyat açıkça ücretsizdir. Süreler tahminidir.',
        ),
        for (final category in vehicleCategories.entries) ...[
          SwitchListTile(
            title: Text(category.value),
            subtitle: Text(
              offered.contains(category.key) ? 'Sunuluyor' : 'Sunulmuyor',
            ),
            value: offered.contains(category.key),
            onChanged: widget.enabled
                ? (v) => setState(() {
                    if (v) {
                      offered.add(category.key);
                    } else {
                      offered.remove(category.key);
                    }
                  })
                : null,
          ),
          if (offered.contains(category.key)) ...[
            TextFormField(
              controller: values[category.key]![0],
              enabled: widget.enabled,
              keyboardType: const TextInputType.numberWithOptions(
                decimal: true,
              ),
              decoration: InputDecoration(
                labelText: '${category.value} fiyatı (TL)',
              ),
              validator: (v) {
                try {
                  Lira.parse(v ?? '', turkish: true);
                  return null;
                } catch (_) {
                  return '0–9.999.999,99 TL; en fazla iki ondalık basamak girin.';
                }
              },
            ),
            for (var i = 1; i < 4; i++)
              TextFormField(
                controller: values[category.key]![i],
                enabled: widget.enabled,
                keyboardType: TextInputType.number,
                decoration: InputDecoration(
                  labelText:
                      '${category.value} ${['', 'etkin süre (dk)', 'bekleme / kuruma (dk, isteğe bağlı)', 'çalışan sayısı'][i]}',
                ),
                validator: (v) => _number(
                  v,
                  i == 1
                      ? 10080
                      : i == 2
                      ? 43200
                      : 100,
                  optional: i == 2,
                ),
              ),
          ],
        ],
        FilledButton(
          onPressed: widget.enabled
              ? () {
                  if (!form.currentState!.validate()) return;
                  widget.draft.name = name.text;
                  widget.draft.description = description.text;
                  widget.draft.offerings = offered.map((key) {
                    final v = values[key]!;
                    return Offering(
                      category: key,
                      price: Lira.parse(v[0].text, turkish: true),
                      activeMinutes: int.parse(v[1].text),
                      waitingMinutes: v[2].text.isEmpty
                          ? null
                          : int.parse(v[2].text),
                      employees: int.parse(v[3].text),
                    );
                  }).toList();
                  widget.onSave();
                }
              : null,
          child: const Text('Kaydet'),
        ),
        TextButton(
          onPressed: widget.enabled ? widget.onCancel : null,
          child: const Text('Vazgeç'),
        ),
      ],
    ),
  );
}
