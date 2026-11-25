import 'dart:async';

import 'package:flutter/material.dart';

import '../auth/domain/auth_service.dart';
import '../customer_vehicles/domain/customer_vehicles.dart';
import '../service_catalog/domain/service_catalog.dart';
import '../scheduling/domain/scheduling.dart';
import 'work_orders.dart';

class StageTemplateScreen extends StatefulWidget {
  const StageTemplateScreen({
    super.key,
    required this.service,
    required this.catalog,
    required this.scheduling,
    required this.business,
    required this.isCurrent,
    required this.onAuthorizationFailure,
  });
  final StageTemplateService service;
  final CatalogService catalog;
  final SchedulingService scheduling;
  final String business;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  @override
  State<StageTemplateScreen> createState() => _StageTemplateState();
}

class _StageRow {
  _StageRow(ServiceStagePlan plan)
    : name = TextEditingController(text: plan.name),
      minutes = TextEditingController(text: '${plan.minutes}'),
      employees = TextEditingController(text: '${plan.employees}'),
      area = plan.area;
  final TextEditingController name, minutes, employees;
  String area;
  void dispose() {
    name.dispose();
    minutes.dispose();
    employees.dispose();
  }
}

class _StageTemplateState extends State<StageTemplateScreen> {
  final scroll = ScrollController();
  void revealMessage() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (current && scroll.hasClients) {
        unawaited(
          scroll.animateTo(
            0,
            duration: const Duration(milliseconds: 200),
            curve: Curves.easeOut,
          ),
        );
      }
    });
  }

  List<CatalogServiceRecord> services = [];
  List<WorkArea> areas = [];
  List<_StageRow> rows = [];
  CatalogServiceRecord? selected;
  Offering? offering;
  int? version;
  bool busy = false, hasMore = false;
  int page = 1;
  String? error;
  bool get current => mounted && widget.isCurrent();
  @override
  void initState() {
    super.initState();
    unawaited(load());
  }

  @override
  void dispose() {
    for (final row in rows) {
      row.dispose();
    }
    scroll.dispose();
    super.dispose();
  }

  Future<T?> handle<T>(AuthResult<T> result) async {
    if (!current) return null;
    if (result case AuthFailure<T>(:final error)) {
      if ({
        AuthErrorCode.forbidden,
        AuthErrorCode.unauthenticated,
      }.contains(error.code)) {
        await widget.onAuthorizationFailure(error);
        if (!current) return null;
      }
      setState(() => this.error = schedulingError(error));
      revealMessage();
      return null;
    }
    return (result as AuthSuccess<T>).value;
  }

  Future<void> load({bool more = false}) async {
    if (busy || !widget.isCurrent()) return;
    setState(() {
      busy = true;
      error = null;
      if (!more) {
        selected = null;
        offering = null;
        services = [];
        page = 1;
      }
    });
    final list = await handle(
      await widget.catalog.list(widget.business, page: more ? page + 1 : 1),
    );
    if (!current) return;
    if (list != null) {
      services = [...services, ...list.items.where((s) => s.enabled)];
      page = list.page;
      hasMore = list.hasMore;
    }
    final workAreas = await handle(
      await widget.scheduling.areas(widget.business),
    );
    if (!current) return;
    setState(() {
      if (workAreas != null) {
        areas = workAreas.where((a) => a.archivedAt == null).toList();
      }
      busy = false;
    });
  }

  Future<void> readPlan() async {
    if (busy || selected == null || offering == null || !widget.isCurrent()) {
      return;
    }
    setState(() {
      busy = true;
      error = null;
    });
    final result = await handle(
      await widget.service.stagePlan(
        widget.business,
        selected!.id,
        offering!.category,
      ),
    );
    if (!current) return;
    if (result != null) {
      for (final row in rows) {
        row.dispose();
      }
      version = result.version;
      rows =
          (result.stages.isNotEmpty
                  ? result.stages
                  : [
                      ServiceStagePlan(
                        selected!.name,
                        offering!.activeMinutes,
                        areas.firstOrNull?.id ?? '',
                        offering!.employees,
                      ),
                    ])
              .map(_StageRow.new)
              .toList();
    }
    setState(() => busy = false);
  }

  Future<void> save() async {
    if (busy || selected == null || offering == null || !widget.isCurrent()) {
      return;
    }
    final plans = rows
        .map(
          (r) => ServiceStagePlan(
            r.name.text.trim(),
            int.tryParse(r.minutes.text) ?? 0,
            r.area,
            int.tryParse(r.employees.text) ?? 0,
          ),
        )
        .toList();
    if (plans.isEmpty ||
        plans.any(
          (p) =>
              p.name.isEmpty ||
              p.name.length > 100 ||
              p.minutes < 1 ||
              p.employees < 1 ||
              p.employees > offering!.employees ||
              !areas.any((a) => a.id == p.area),
        ) ||
        plans.fold<int>(0, (sum, p) => sum + p.minutes) !=
            offering!.activeMinutes ||
        plans.map((p) => p.employees).reduce((a, b) => a > b ? a : b) !=
            offering!.employees) {
      setState(
        () => error =
            'Süre toplamı ${offering!.activeMinutes} dakika, en yüksek çalışan gereksinimi ${offering!.employees} olmalıdır. Aşama alanlarını ve adlarını kontrol edin.',
      );
      revealMessage();
      return;
    }
    setState(() {
      busy = true;
      error = null;
    });
    final result = await handle(
      await widget.service.saveStagePlan(
        widget.business,
        selected!.id,
        offering!.category,
        version,
        plans,
      ),
    );
    if (!current) return;
    setState(() {
      busy = false;
      if (result != null) {
        version = result.version;
        error = 'Aşama planı sunucuya kaydedildi. Yeni randevular bu plana göre doğrulanacak.';
      }
    });
    revealMessage();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Hizmet aşamaları'),
      actions: [
        IconButton(
          tooltip: 'Yenile',
          onPressed: busy ? null : load,
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: !widget.isCurrent()
        ? const SizedBox.shrink()
        : ListView(
            controller: scroll,
            padding: const EdgeInsets.all(16),
            children: [
              if (busy) const LinearProgressIndicator(),
              if (error != null) Text(error!),
              const Text(
                'Her aşama için süre, çalışma alanı ve gereken çalışan sayısını belirleyin. Aşamalar sırayla rezerve edilir; mevcut randevular otomatik değiştirilmez.',
              ),
              if (!busy && services.isEmpty)
                const Text(
                  'Önce hizmet kataloğunda etkin bir hizmet oluşturun.',
                ),
              DropdownButtonFormField<String>(
                key: ValueKey(selected?.id),
                initialValue: selected?.id,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Hizmet'),
                items: [
                  for (final s in services)
                    DropdownMenuItem(
                      value: s.id,
                      child: Text(s.name, overflow: TextOverflow.ellipsis),
                    ),
                ],
                onChanged: busy
                    ? null
                    : (id) {
                        setState(() {
                          selected = services.firstWhere((s) => s.id == id);
                          offering = null;
                        });
                      },
              ),
              if (hasMore)
                TextButton(
                  onPressed: busy ? null : () => load(more: true),
                  child: const Text('Daha fazla hizmet yükle'),
                ),
              if (selected != null)
                DropdownButtonFormField<String>(
                  key: ValueKey((selected?.id, offering?.category)),
                  initialValue: offering?.category,
                  decoration: const InputDecoration(labelText: 'Araç türü'),
                  items: [
                    for (final o in selected!.offerings)
                      DropdownMenuItem(
                        value: o.category,
                        child: Text(vehicleCategories[o.category]!),
                      ),
                  ],
                  onChanged: busy
                      ? null
                      : (category) {
                          offering = selected!.offerings.firstWhere(
                            (o) => o.category == category,
                          );
                          unawaited(readPlan());
                        },
                ),
              if (offering != null) ...[
                Text(
                  'Toplam: ${offering!.activeMinutes} dakika · en fazla ${offering!.employees} çalışan',
                ),
                for (var i = 0; i < rows.length; i++)
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(12),
                      child: Column(
                        children: [
                          TextField(
                            controller: rows[i].name,
                            enabled: !busy,
                            maxLength: 100,
                            decoration: InputDecoration(
                              labelText: '${i + 1}. aşama adı',
                            ),
                          ),
                          TextField(
                            controller: rows[i].minutes,
                            enabled: !busy,
                            keyboardType: TextInputType.number,
                            decoration: const InputDecoration(
                              labelText: 'Tahmini süre (dakika)',
                            ),
                          ),
                          TextField(
                            controller: rows[i].employees,
                            enabled: !busy,
                            keyboardType: TextInputType.number,
                            decoration: const InputDecoration(
                              labelText: 'Gereken çalışan sayısı',
                            ),
                          ),
                          DropdownButtonFormField<String>(
                            key: ValueKey((i, rows[i].area)),
                            initialValue: areas.any((a) => a.id == rows[i].area)
                                ? rows[i].area
                                : null,
                            isExpanded: true,
                            decoration: const InputDecoration(
                              labelText: 'Çalışma alanı',
                            ),
                            items: [
                              for (final a in areas)
                                DropdownMenuItem(
                                  value: a.id,
                                  child: Text(
                                    a.name,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                ),
                            ],
                            onChanged: busy
                                ? null
                                : (id) {
                                    setState(() => rows[i].area = id!);
                                  },
                          ),
                        ],
                      ),
                    ),
                  ),
                if (areas.isEmpty)
                  const Text(
                    'Önce planlama ayarlarından etkin bir çalışma alanı oluşturun.',
                  ),
                TextButton(
                  onPressed: busy || rows.length >= 20 || areas.isEmpty
                      ? null
                      : () {
                          setState(
                            () => rows.add(
                              _StageRow(
                                ServiceStagePlan('', 1, areas.first.id, 1),
                              ),
                            ),
                          );
                        },
                  child: const Text('Aşama ekle'),
                ),
                if (rows.length > 1)
                  TextButton(
                    onPressed: busy
                        ? null
                        : () {
                            setState(() => rows.removeLast().dispose());
                          },
                    child: const Text('Son aşamayı kaldır'),
                  ),
                FilledButton(
                  onPressed: busy || areas.isEmpty ? null : save,
                  child: const Text('Aşama planını kaydet'),
                ),
              ],
            ],
          ),
  );
}
