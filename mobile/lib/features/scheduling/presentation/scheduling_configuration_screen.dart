import 'dart:async';

import 'package:flutter/material.dart';

import '../../customer_vehicles/domain/customer_vehicles.dart';
import '../../auth/domain/auth_service.dart';
import '../../service_catalog/domain/service_catalog.dart';
import '../domain/scheduling.dart';
import 'scheduling_controller.dart';
import 'scheduling_widgets.dart';
import 'booking_screen.dart';

enum SchedulingSection { areas, requirements, availability }

class SchedulingConfigurationScreen extends StatefulWidget {
  const SchedulingConfigurationScreen({
    super.key,
    required this.controller,
    required this.section,
  });
  final SchedulingController controller;
  final SchedulingSection section;
  @override
  State<SchedulingConfigurationScreen> createState() =>
      _SchedulingConfigurationScreenState();
}

class _SchedulingConfigurationScreenState
    extends State<SchedulingConfigurationScreen> {
  SchedulingController get c => widget.controller;
  List<WorkArea> areas = [];
  List<CatalogServiceRecord> services = [];
  List<SchedulingEmployee> employees = [];
  List<SchedulingRequirement> requirements = [];
  List<EmployeeAvailability> windows = [];
  bool loaded = false, windowsLoaded = false;
  String? serviceId, employeeId;
  int page = 1;
  late DateTime day;
  @override
  void initState() {
    super.initState();
    day = c.date ?? DateTime.utc(2000);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted && c.active) unawaited(_load());
    });
  }

  Future<void> _load() async {
    if (c.busy) return;
    setState(() => loaded = false);
    switch (widget.section) {
      case SchedulingSection.areas:
        final v = await c.request(() => c.service.areas(c.business));
        if (!mounted || !c.active || v == null) return;
        areas = v;
      case SchedulingSection.requirements:
        final a = await c.request(() => c.service.areas(c.business));
        if (!mounted || !c.active || a == null) return;
        areas = a;
        final s = await c.request(
          () => schedulingPages((p) => c.catalog.list(c.business, page: p)),
        );
        if (!mounted || !c.active || s == null) return;
        services = s;
        if (!services.any((v) => v.id == serviceId)) {
          serviceId = null;
          requirements = [];
        }
        if (serviceId != null) {
          final r = await c.request(
            () => c.service.requirements(c.business, serviceId!),
          );
          if (!mounted || !c.active || r == null) return;
          requirements = r;
        }
      case SchedulingSection.availability:
        final e = await c.request(() => c.service.employees(c.business));
        if (!mounted || !c.active || e == null) return;
        employees = e;
        if (!employees.any((v) => v.membershipId == employeeId)) {
          employeeId = null;
          windows = [];
        }
        if (employeeId != null && !await _loadWindows()) return;
    }
    setState(() => loaded = true);
  }

  Future<bool> _loadWindows() async {
    if (!mounted || !c.active || c.busy || employeeId == null) return false;
    setState(() {
      windows = [];
      windowsLoaded = false;
    });
    try {
      final from = c.clock!.resolve(day),
          to = c.clock!.resolve(day.add(const Duration(days: 7)));
      final values = await c.request(
        () => c.service.availability(
          c.business,
          employeeId!,
          from,
          to,
          page: page,
        ),
      );
      if (!mounted || !c.active || values == null) return false;
      setState(() {
        windows = values;
        windowsLoaded = true;
      });
      return true;
    } catch (_) {
      c.show(
        'Bu tarih aralığının saat dilimi sınırları belirsiz. Başka tarih seçin.',
      );
      return false;
    }
  }

  Future<void> _archive(
    Future<bool> Function() action,
    String description,
  ) async {
    if (!await schedulingConfirm(
          context,
          'Arşivleme onayı',
          description,
          action: 'Arşivle',
        ) ||
        !mounted ||
        !c.active) {
      return;
    }
    final ok = await action();
    if (!mounted || !c.active) return;
    final error = c.message;
    await _load();
    if (!ok && c.active) {
      c.show(
        '${error ?? 'Sonuç doğrulanamadı.'} Güncel listeyi kontrol edin; otomatik tekrar yapılmadı.',
      );
    }
  }

  Future<void> _areaEditor([WorkArea? area]) async {
    final input = await showDialog<WorkAreaInput>(
      context: context,
      builder: (_) => _AreaDialog(area: area),
    );
    if (input == null || !mounted || !c.active) return;
    final result = await c.request(
      () => c.service.saveArea(c.business, input, id: area?.id),
    );
    if (!mounted || !c.active) return;
    final error = c.message;
    await _load();
    if (result == null && c.active) {
      c.show(
        '${error ?? 'Sonuç doğrulanamadı.'} Güncel listeyi kontrol edin; otomatik tekrar yapılmadı.',
      );
    }
  }

  Future<void> _requirementEditor(
    Offering offering,
    SchedulingRequirement? requirement,
  ) async {
    final activeAreas = areas.where((v) => v.archivedAt == null).toList();
    if (activeAreas.isEmpty) {
      c.show('Önce etkin bir çalışma alanı oluşturun.');
      return;
    }
    final input = await showDialog<RequirementInput>(
      context: context,
      builder: (_) => _RequirementDialog(
        areas: activeAreas,
        requirement: requirement,
        offering: offering,
      ),
    );
    if (input == null || !mounted || !c.active) return;
    final result = await c.request(
      () => c.service.saveRequirement(
        c.business,
        serviceId!,
        offering.category,
        input,
      ),
    );
    if (!mounted || !c.active) return;
    final error = c.message;
    await _load();
    if (result == null && c.active) {
      c.show(
        '${error ?? 'Sonuç doğrulanamadı.'} Güncel gereksinimleri kontrol edin.',
      );
    }
  }

  Future<void> _availabilityEditor([EmployeeAvailability? window]) async {
    final input = await Navigator.of(context).push<AvailabilityInput>(
      MaterialPageRoute(
        builder: (_) =>
            _AvailabilityEditor(clock: c.clock!, day: day, window: window),
      ),
    );
    if (input == null || !mounted || !c.active) return;
    final result = await c.request(
      () => c.service.saveAvailability(
        c.business,
        employeeId!,
        input,
        id: window?.id,
      ),
    );
    if (!mounted || !c.active) return;
    final error = c.message;
    await _load();
    if (result == null && c.active) {
      c.show(
        '${error ?? 'Sonuç doğrulanamadı.'} Güncel uygunluk listesini kontrol edin.',
      );
    }
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: c,
    builder: (context, _) => SchedulePage(
      controller: c,
      title: switch (widget.section) {
        SchedulingSection.areas => 'Çalışma alanları',
        SchedulingSection.requirements => 'Hizmet gereksinimleri',
        SchedulingSection.availability => 'Çalışan uygunluğu',
      },
      children: [
        OutlinedButton.icon(
          onPressed: c.busy ? null : _load,
          icon: const Icon(Icons.refresh),
          label: const Text('Listeyi yenile'),
        ),
        if (loaded)
          ...switch (widget.section) {
            SchedulingSection.areas => [
              FilledButton.icon(
                onPressed: c.busy ? null : _areaEditor,
                icon: const Icon(Icons.add),
                label: const Text('Çalışma alanı ekle'),
              ),
              if (areas.isEmpty) const Text('Henüz çalışma alanı yok.'),
              for (final a in areas)
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Text(
                          a.name,
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        Text(
                          'Kapasite: ${a.capacity} • ${a.archivedAt == null ? 'Etkin' : 'Arşivlendi'}',
                        ),
                        if (a.archivedAt == null)
                          Wrap(
                            spacing: 8,
                            children: [
                              TextButton(
                                onPressed: c.busy ? null : () => _areaEditor(a),
                                child: const Text('Düzenle'),
                              ),
                              TextButton(
                                onPressed: c.busy
                                    ? null
                                    : () => _archive(
                                        () async {
                                          var success = false;
                                          await c.request(() async {
                                            final r = await c.service
                                                .archiveArea(c.business, a);
                                            success = r is AuthSuccess<void>;
                                            return r;
                                          });
                                          return success;
                                        },
                                        '${a.name} arşivlenecek. Mevcut randevu kontrolleri sunucuda yapılır.',
                                      ),
                                child: const Text('Arşivle'),
                              ),
                            ],
                          ),
                      ],
                    ),
                  ),
                ),
            ],
            SchedulingSection.requirements => [
              schedulingSelect<String>(
                label: 'Hizmet',
                value: serviceId,
                options: {
                  for (final s in services)
                    s.id: '${s.name}${s.enabled ? '' : ' (devre dışı)'}',
                },
                onChanged: c.busy
                    ? null
                    : (v) {
                        setState(() {
                          serviceId = v;
                          requirements = [];
                        });
                        unawaited(_load());
                      },
              ),
              const Text(
                'Çalışan sayısı, fiyat ve süre Hizmetler ve Fiyatlar bölümündeki araç türü teklifinden gelir. Bu ekran yalnızca çalışma alanı ve tek etkin aşama onayını yönetir.',
              ),
              if (serviceId != null) ...[
                for (final o
                    in services
                        .where((s) => s.id == serviceId)
                        .first
                        .offerings) ...[
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Text(
                            vehicleCategories[o.category]!,
                            style: Theme.of(context).textTheme.titleMedium,
                          ),
                          Text(
                            '${o.employees} çalışan • ${o.price.display}\n${o.activeMinutes} dk etkin',
                          ),
                          Text(
                            requirements
                                        .where((r) => r.category == o.category)
                                        .firstOrNull
                                        ?.singleStageConfirmed ==
                                    true
                                ? 'Tek etkin aşama onaylı'
                                : 'Planlamaya kapalı',
                          ),
                          Text(
                            'Alan: ${areas.where((a) => a.id == requirements.where((r) => r.category == o.category).firstOrNull?.workAreaId).firstOrNull?.name ?? 'Yapılandırılmamış'}',
                          ),
                          TextButton(
                            onPressed:
                                c.busy ||
                                    !services
                                        .where((s) => s.id == serviceId)
                                        .first
                                        .enabled
                                ? null
                                : () => _requirementEditor(
                                    o,
                                    requirements
                                        .where((r) => r.category == o.category)
                                        .firstOrNull,
                                  ),
                            child: const Text('Gereksinimi düzenle'),
                          ),
                        ],
                      ),
                    ),
                  ),
                ],
                // Retained requirements remain disableable even after offering removal.
                for (final r in requirements)
                  if (r.singleStageConfirmed)
                    OutlinedButton(
                      onPressed: c.busy
                          ? null
                          : () async {
                              if (!await schedulingConfirm(
                                    context,
                                    'Planlama devre dışı bırakılsın mı?',
                                    '${vehicleCategories[r.category]} için tek etkin aşama onayı geri çekilecek.',
                                    action: 'Devre dışı bırak',
                                  ) ||
                                  !mounted ||
                                  !c.active) {
                                return;
                              }
                              final result = await c.request(
                                () =>
                                    c.service.disableRequirement(c.business, r),
                              );
                              if (!mounted || !c.active) return;
                              final error = c.message;
                              await _load();
                              if (result == null && c.active) {
                                c.show(error ?? 'İşlem sonucu doğrulanamadı.');
                              }
                            },
                      child: Text(
                        '${vehicleCategories[r.category]} planlamasını devre dışı bırak',
                      ),
                    ),
              ],
            ],
            SchedulingSection.availability => [
              Text('Saat dilimi: ${c.clock!.zoneId}'),
              schedulingSelect<String>(
                label: 'Etkin çalışan',
                value: employeeId,
                options: {for (final e in employees) e.membershipId: e.name},
                onChanged: c.busy
                    ? null
                    : (v) {
                        setState(() {
                          employeeId = v;
                          windows = [];
                          page = 1;
                        });
                        unawaited(_loadWindows());
                      },
              ),
              OutlinedButton(
                onPressed: c.busy
                    ? null
                    : () async {
                        final d = await showDatePicker(
                          context: context,
                          initialDate: DateTime(day.year, day.month, day.day),
                          firstDate: DateTime(2000),
                          lastDate: DateTime(2100),
                        );
                        if (!mounted || !c.active || d == null) return;
                        setState(() {
                          day = DateTime.utc(d.year, d.month, d.day);
                          page = 1;
                        });
                        if (employeeId != null) await _loadWindows();
                      },
                child: Text('7 günlük aralık başlangıcı: ${dateWire(day)}'),
              ),
              if (employeeId != null) ...[
                FilledButton(
                  onPressed: c.busy ? null : _availabilityEditor,
                  child: const Text('Uygunluk ekle'),
                ),
                if (windowsLoaded && windows.isEmpty)
                  const Text('Bu aralıkta uygunluk kaydı yok.'),
                if (!windowsLoaded && !c.busy)
                  OutlinedButton(
                    onPressed: _loadWindows,
                    child: const Text('Uygunluğu yeniden yükle'),
                  ),
                for (final w in windows)
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Text(
                            '${localDisplay(c.clock!.local(w.startsAtUtc))}\n${localDisplay(c.clock!.local(w.endsAtUtc))}',
                          ),
                          if (w.archivedAt != null)
                            const Text('Arşivlendi')
                          else
                            Wrap(
                              spacing: 8,
                              children: [
                                TextButton(
                                  onPressed: c.busy
                                      ? null
                                      : () => _availabilityEditor(w),
                                  child: const Text('Düzenle'),
                                ),
                                TextButton(
                                  onPressed: c.busy
                                      ? null
                                      : () => _archive(
                                          () async {
                                            var success = false;
                                            await c.request(() async {
                                              final r = await c.service
                                                  .archiveAvailability(
                                                    c.business,
                                                    w,
                                                  );
                                              success = r is AuthSuccess<void>;
                                              return r;
                                            });
                                            return success;
                                          },
                                          'Çalışanın bu uygunluk aralığı arşivlenecek.',
                                        ),
                                  child: const Text('Arşivle'),
                                ),
                              ],
                            ),
                        ],
                      ),
                    ),
                  ),
                Wrap(
                  spacing: 8,
                  children: [
                    Text('Sayfa $page'),
                    TextButton(
                      onPressed: c.busy || page == 1
                          ? null
                          : () {
                              page--;
                              unawaited(_loadWindows());
                            },
                      child: const Text('Önceki'),
                    ),
                    TextButton(
                      onPressed:
                          c.busy ||
                              !windowsLoaded ||
                              page >= 10000 ||
                              windows.length < 50
                          ? null
                          : () {
                              page++;
                              unawaited(_loadWindows());
                            },
                      child: const Text('Sonraki'),
                    ),
                  ],
                ),
              ],
            ],
          },
      ],
    ),
  );
}

class _AreaDialog extends StatefulWidget {
  const _AreaDialog({this.area});
  final WorkArea? area;
  @override
  State<_AreaDialog> createState() => _AreaDialogState();
}

class _AreaDialogState extends State<_AreaDialog> {
  final form = GlobalKey<FormState>();
  late final name = TextEditingController(text: widget.area?.name ?? '');
  late final capacity = TextEditingController(
    text: widget.area?.capacity.toString() ?? '',
  );
  @override
  void dispose() {
    name.dispose();
    capacity.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(
      widget.area == null ? 'Yeni çalışma alanı' : 'Çalışma alanını düzenle',
    ),
    content: SingleChildScrollView(
      child: Form(
        key: form,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextFormField(
              controller: name,
              decoration: const InputDecoration(labelText: 'Alan adı'),
              maxLength: 200,
              validator: (v) =>
                  v == null || v.trim().isEmpty ? 'Alan adı girin.' : null,
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: capacity,
              decoration: const InputDecoration(
                labelText: 'Eş zamanlı araç kapasitesi',
              ),
              keyboardType: TextInputType.number,
              validator: (v) {
                final n = int.tryParse(v ?? '');
                return n == null || n < 1 || n > 2147483647
                    ? 'Pozitif bir tam sayı girin.'
                    : null;
              },
            ),
          ],
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Vazgeç'),
      ),
      FilledButton(
        onPressed: () {
          if (form.currentState!.validate()) {
            Navigator.pop(
              context,
              WorkAreaInput(
                name.text,
                int.parse(capacity.text),
                version: widget.area?.version,
              ),
            );
          }
        },
        child: const Text('Kaydet'),
      ),
    ],
  );
}

class _RequirementDialog extends StatefulWidget {
  const _RequirementDialog({
    required this.areas,
    required this.offering,
    this.requirement,
  });
  final List<WorkArea> areas;
  final Offering offering;
  final SchedulingRequirement? requirement;
  @override
  State<_RequirementDialog> createState() => _RequirementDialogState();
}

class _RequirementDialogState extends State<_RequirementDialog> {
  String? area;
  bool confirmed = false;
  @override
  void initState() {
    super.initState();
    area = widget.areas.any((a) => a.id == widget.requirement?.workAreaId)
        ? widget.requirement!.workAreaId
        : null;
    confirmed = widget.requirement?.singleStageConfirmed ?? false;
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Hizmet gereksinimi'),
    content: SingleChildScrollView(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            '${vehicleCategories[widget.offering.category]} • ${widget.offering.employees} çalışan',
          ),
          schedulingSelect<String>(
            label: 'Gerekli çalışma alanı',
            value: area,
            options: {for (final a in widget.areas) a.id: a.name},
            onChanged: (v) => setState(() => area = v),
          ),
          CheckboxListTile(
            contentPadding: EdgeInsets.zero,
            title: const Text('Bu hizmet tek etkin aşamada tamamlanır'),
            value: confirmed,
            onChanged: (v) => setState(() => confirmed = v ?? false),
          ),
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Vazgeç'),
      ),
      FilledButton(
        onPressed: area == null
            ? null
            : () => Navigator.pop(
                context,
                RequirementInput(
                  area!,
                  confirmed,
                  version: widget.requirement?.version,
                ),
              ),
        child: const Text('Kaydet'),
      ),
    ],
  );
}

class _AvailabilityEditor extends StatefulWidget {
  const _AvailabilityEditor({
    required this.clock,
    required this.day,
    this.window,
  });
  final BusinessClock clock;
  final DateTime day;
  final EmployeeAvailability? window;
  @override
  State<_AvailabilityEditor> createState() => _AvailabilityEditorState();
}

class _AvailabilityEditorState extends State<_AvailabilityEditor> {
  late DateTime start, end;
  int? startOffset, endOffset;
  String? error;
  @override
  void initState() {
    super.initState();
    final w = widget.window;
    final s = w == null
        ? widget.day.add(const Duration(hours: 9))
        : widget.clock.local(w.startsAtUtc);
    final e = w == null
        ? widget.day.add(const Duration(hours: 17))
        : widget.clock.local(w.endsAtUtc);
    start = DateTime.utc(s.year, s.month, s.day, s.hour, s.minute);
    end = DateTime.utc(e.year, e.month, e.day, e.hour, e.minute);
    final ss = widget.clock.candidates(start),
        ee = widget.clock.candidates(end);
    startOffset = w != null
        ? widget.clock.local(w.startsAtUtc).timeZoneOffset.inMinutes
        : ss.length == 1
        ? widget.clock.local(ss.single).timeZoneOffset.inMinutes
        : null;
    endOffset = w != null
        ? widget.clock.local(w.endsAtUtc).timeZoneOffset.inMinutes
        : ee.length == 1
        ? widget.clock.local(ee.single).timeZoneOffset.inMinutes
        : null;
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Uygunluk aralığı')),
    body: SafeArea(
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          BusinessTimeField(
            clock: widget.clock,
            label: 'Başlangıç',
            wall: start,
            offsetMinutes: startOffset,
            onChanged: (v, o) => setState(() {
              start = v;
              startOffset = o;
            }),
          ),
          const SizedBox(height: 16),
          BusinessTimeField(
            clock: widget.clock,
            label: 'Bitiş',
            wall: end,
            offsetMinutes: endOffset,
            onChanged: (v, o) => setState(() {
              end = v;
              endOffset = o;
            }),
          ),
          if (error != null) Text(error!),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: () {
              try {
                final input = AvailabilityInput(
                  widget.clock.resolve(start, offsetMinutes: startOffset),
                  widget.clock.resolve(end, offsetMinutes: endOffset),
                  version: widget.window?.version,
                );
                if (input.validate() case final text?) {
                  setState(() => error = text);
                  return;
                }
                Navigator.pop(context, input);
              } catch (_) {
                setState(() => error = 'Geçerli saat ve UTC farkı seçin.');
              }
            },
            child: const Text('Kaydet'),
          ),
        ],
      ),
    ),
  );
}
