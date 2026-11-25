import 'dart:async';
import 'dart:math';

import 'package:flutter/material.dart';

import '../auth/domain/auth_service.dart';
import '../scheduling/domain/scheduling.dart';
import 'work_orders.dart';

String workRequestId() {
  final random = Random.secure();
  final bytes = List.generate(16, (_) => random.nextInt(256));
  bytes[6] = (bytes[6] & 15) | 64;
  bytes[8] = (bytes[8] & 63) | 128;
  final hex = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
  return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20)}';
}

class WorkOrderScreen extends StatefulWidget {
  const WorkOrderScreen({
    super.key,
    required this.service,
    required this.business,
    required this.owner,
    required this.isCurrent,
    required this.onAuthorizationFailure,
    this.scheduling,
  });
  final WorkOrderService service;
  final SchedulingService? scheduling;
  final String business;
  final bool owner;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  @override
  State<WorkOrderScreen> createState() => _WorkOrderScreenState();
}

class _WorkOrderScreenState extends State<WorkOrderScreen>
    with WidgetsBindingObserver {
  List<WorkOrder> orders = [];
  String filter = 'Tümü';
  bool matches(WorkOrder order) => switch (filter) {
    'Bugün' => order.plannedLocal.startsWith(order.todayLocal),
    'Yaklaşan' =>
      order.status == 'Scheduled' && order.planned.isAfter(order.serverNow),
    'Etkin' => {'InProgress', 'Paused'}.contains(order.status),
    'Tamamlanan' => order.status == 'Completed',
    _ => true,
  };
  bool busy = false;
  String? error;
  int page = 1;
  int generation = 0;
  Timer? refresh;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    unawaited(load());
    refresh = Timer.periodic(const Duration(seconds: 30), (_) {
      if (WidgetsBinding.instance.lifecycleState == AppLifecycleState.resumed) {
        unawaited(load());
      }
    });
  }

  @override
  void dispose() {
    generation++;
    refresh?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(load());
  }

  Future<void> load() async {
    if (busy || !widget.isCurrent()) return;
    final g = ++generation;
    setState(() {
      busy = true;
      error = null;
    });
    final result = await widget.service.list(
      widget.business,
      widget.owner,
      page: page,
    );
    if (!mounted || g != generation || !widget.isCurrent()) return;
    if (result case AuthFailure<List<WorkOrder>>(:final error)) {
      if ({
        AuthErrorCode.unauthenticated,
        AuthErrorCode.forbidden,
      }.contains(error.code)) {
        await widget.onAuthorizationFailure(error);
        if (!mounted || !widget.isCurrent()) return;
      }
      setState(() {
        this.error = schedulingError(error);
        busy = false;
      });
    } else {
      setState(() {
        orders = (result as AuthSuccess<List<WorkOrder>>).value;
        busy = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(widget.owner ? 'İş emirleri' : 'Atanan işlerim'),
      actions: [
        IconButton(
          tooltip: 'Yenile',
          onPressed: busy ? null : load,
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    floatingActionButton: widget.owner && widget.scheduling != null
        ? FloatingActionButton.extended(
            label: const Text('Randevudan iş emri'),
            icon: const Icon(Icons.add),
            onPressed: busy
                ? null
                : () async {
                    await Navigator.of(context).push<void>(
                      MaterialPageRoute(
                        builder: (_) => _CreateOrder(
                          service: widget.service,
                          scheduling: widget.scheduling!,
                          business: widget.business,
                          isCurrent: widget.isCurrent,
                          onAuthorizationFailure: widget.onAuthorizationFailure,
                        ),
                      ),
                    );
                    if (mounted && widget.isCurrent()) await load();
                  },
          )
        : null,
    body: !widget.isCurrent()
        ? const SizedBox.shrink()
        : RefreshIndicator(
            onRefresh: load,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 16, 16, 100),
              children: [
                if (busy) const LinearProgressIndicator(),
                if (error != null)
                  Text(
                    error!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                if (!busy && error == null && orders.isEmpty)
                  const Padding(
                    padding: EdgeInsets.all(24),
                    child: Text('Henüz iş emri bulunmuyor.'),
                  ),
                Wrap(
                  spacing: 8,
                  children: [
                    for (final label in [
                      'Tümü',
                      'Bugün',
                      'Yaklaşan',
                      'Etkin',
                      'Tamamlanan',
                    ])
                      ChoiceChip(
                        label: Text(label),
                        selected: filter == label,
                        onSelected: busy
                            ? null
                            : (_) => setState(() => filter = label),
                      ),
                  ],
                ),
                if (!busy &&
                    error == null &&
                    orders.isNotEmpty &&
                    !orders.any(matches))
                  const Text('Bu sayfada seçilen duruma uygun iş bulunmuyor.'),
                for (final order in orders.where(matches))
                  Card(
                    child: ListTile(
                      title: Text('${order.plate} · ${order.service}'),
                      subtitle: Text(
                        '${workStatus[order.status]}\nİşletme saati: ${order.plannedLocal}${order.potentialConflict ? '\nSüre aşıldı. Sonraki randevuları kontrol edin.' : ''}',
                      ),
                      isThreeLine: true,
                      trailing: const Icon(Icons.chevron_right),
                      onTap: busy
                          ? null
                          : () async {
                              await Navigator.of(context).push<void>(
                                MaterialPageRoute(
                                  builder: (_) => WorkOrderDetailScreen(
                                    service: widget.service,
                                    business: widget.business,
                                    id: order.id,
                                    owner: widget.owner,
                                    isCurrent: widget.isCurrent,
                                    onAuthorizationFailure:
                                        widget.onAuthorizationFailure,
                                  ),
                                ),
                              );
                              if (mounted && widget.isCurrent()) await load();
                            },
                    ),
                  ),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    TextButton(
                      onPressed: busy || page == 1
                          ? null
                          : () {
                              page--;
                              unawaited(load());
                            },
                      child: const Text('Önceki'),
                    ),
                    Text('Sayfa $page'),
                    TextButton(
                      onPressed: busy || orders.length < 30
                          ? null
                          : () {
                              page++;
                              unawaited(load());
                            },
                      child: const Text('Sonraki'),
                    ),
                  ],
                ),
              ],
            ),
          ),
  );
}

class WorkOrderDetailScreen extends StatefulWidget {
  const WorkOrderDetailScreen({
    super.key,
    required this.service,
    required this.business,
    required this.id,
    required this.owner,
    required this.isCurrent,
    required this.onAuthorizationFailure,
  });
  final WorkOrderService service;
  final String business, id;
  final bool owner;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  @override
  State<WorkOrderDetailScreen> createState() => _WorkOrderDetailState();
}

class _WorkOrderDetailState extends State<WorkOrderDetailScreen>
    with WidgetsBindingObserver {
  WorkOrder? order;
  WorkClock? clock;
  Timer? ticker;
  Timer? refresh;
  bool busy = false;
  String? error;
  int generation = 0;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    unawaited(load());
    ticker = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted && widget.isCurrent()) setState(() {});
    });
    refresh = Timer.periodic(const Duration(seconds: 30), (_) {
      if (WidgetsBinding.instance.lifecycleState == AppLifecycleState.resumed) {
        unawaited(load());
      }
    });
  }

  @override
  void dispose() {
    generation++;
    ticker?.cancel();
    refresh?.cancel();
    clock?.dispose();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(load());
  }

  bool get current => mounted && widget.isCurrent();
  Future<void> result(AuthResult<WorkOrder> result, int g) async {
    if (!current || g != generation) return;
    if (result case AuthFailure<WorkOrder>(:final error)) {
      if ({
        AuthErrorCode.unauthenticated,
        AuthErrorCode.forbidden,
      }.contains(error.code)) {
        await widget.onAuthorizationFailure(error);
        if (!current) return;
      }
      setState(() {
        this.error = schedulingError(error);
        busy = false;
      });
    } else {
      final value = (result as AuthSuccess<WorkOrder>).value;
      clock?.dispose();
      setState(() {
        order = value;
        clock = WorkClock(value.serverNow);
        busy = false;
        error = null;
      });
    }
  }

  Future<void> load() async {
    if (busy || !widget.isCurrent()) return;
    final g = ++generation;
    setState(() {
      busy = true;
    });
    await result(
      await widget.service.detail(widget.business, widget.id, widget.owner),
      g,
    );
  }

  Future<void> act(WorkStage? stage, String action) async {
    if (busy || !widget.isCurrent() || order == null) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: Text(
          action == 'cancel'
              ? 'İş emri iptal edilsin mi?'
              : action == 'complete'
              ? 'Aşama tamamlansın mı?'
              : 'Aşama güncellensin mi?',
        ),
        content: const Text(
          'İşlem sunucuya kaydedilecek. Randevu saati otomatik değiştirilmeyecek.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const Text('Vazgeç'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, true),
            child: const Text('Onayla'),
          ),
        ],
      ),
    );
    if (confirmed != true || !current || busy) return;
    final g = ++generation;
    setState(() {
      busy = true;
      error = null;
    });
    await result(
      await widget.service.act(order!, stage, action, workRequestId()),
      g,
    );
  }

  @override
  Widget build(BuildContext context) {
    final a = order;
    return Scaffold(
      appBar: AppBar(
        title: const Text('İş emri ayrıntısı'),
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
              padding: const EdgeInsets.all(16),
              children: [
                if (busy) const LinearProgressIndicator(),
                if (error != null)
                  Text(
                    '$error\nSonuç belirsizse yeniden işlem yapmadan önce Yenile düğmesiyle sunucu durumunu kontrol edin.',
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                if (a != null) ...[
                  Text(
                    a.plate,
                    style: Theme.of(context).textTheme.headlineMedium,
                  ),
                  Text(a.service),
                  Text(workStatus[a.status]!),
                  Text(
                    '${a.stages.where((s) => s.status == 'Completed').length} / ${a.stages.length} aşama tamamlandı',
                  ),
                  LinearProgressIndicator(
                    value:
                        a.stages.where((s) => s.status == 'Completed').length /
                        a.stages.length,
                  ),
                  if (a.potentialConflict)
                    const Text(
                      'Tahmini süre aşıldı. Sonraki randevularla çakışma olabilir; otomatik kaydırma yapılmadı.',
                    ),
                  for (final stage in a.stages)
                    Card(
                      child: Padding(
                        padding: const EdgeInsets.all(16),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              '${stage.sequence}. ${stage.name}',
                              style: Theme.of(context).textTheme.titleMedium,
                            ),
                            Text(workStatus[stage.status]!),
                            Text('Tahmini süre: ${stage.minutes} dakika'),
                            if (stage.status == 'InProgress' ||
                                stage.status == 'Paused')
                              Semantics(
                                liveRegion: false,
                                child: Text(
                                  workRemaining(stage, clock!.now),
                                  style: Theme.of(context)
                                      .textTheme
                                      .headlineSmall,
                                ),
                              ),
                            if (stage.completed != null)
                              const Text('Tamamlanma sunucuya kaydedildi.'),
                            if (!widget.owner)
                              Wrap(
                                spacing: 8,
                                children: [
                                  if (stage.status == 'Ready')
                                    FilledButton(
                                      onPressed: busy
                                          ? null
                                          : () => act(stage, 'start'),
                                      child: const Text('Başlat'),
                                    ),
                                  if (stage.status == 'InProgress') ...[
                                    OutlinedButton(
                                      onPressed: busy
                                          ? null
                                          : () => act(stage, 'pause'),
                                      child: const Text('Duraklat'),
                                    ),
                                    FilledButton(
                                      onPressed: busy
                                          ? null
                                          : () => act(stage, 'complete'),
                                      child: const Text('Aşamayı tamamla'),
                                    ),
                                  ],
                                  if (stage.status == 'Paused')
                                    FilledButton(
                                      onPressed: busy
                                          ? null
                                          : () => act(stage, 'resume'),
                                      child: const Text('Devam et'),
                                    ),
                                ],
                              ),
                          ],
                        ),
                      ),
                    ),
                  if (widget.owner &&
                      {'Scheduled', 'InProgress', 'Paused'}.contains(a.status))
                    OutlinedButton(
                      onPressed: busy ? null : () => act(null, 'cancel'),
                      child: const Text('İş emrini iptal et'),
                    ),
                  if (widget.owner)
                    const Text(
                      'Atamayı değiştirmek için iş emrini açıkça iptal edin, randevunun çalışanlarını güncelleyin ve yeni iş emri oluşturun. Önceki işlemler geçmişte korunur.',
                    ),
                ],
              ],
            ),
    );
  }
}

class _StageDraft {
  final name = TextEditingController();
  final minutes = TextEditingController();
  void dispose() {
    name.dispose();
    minutes.dispose();
  }
}

class _CreateOrder extends StatefulWidget {
  const _CreateOrder({
    required this.service,
    required this.scheduling,
    required this.business,
    required this.isCurrent,
    required this.onAuthorizationFailure,
  });
  final WorkOrderService service;
  final SchedulingService scheduling;
  final String business;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  @override
  State<_CreateOrder> createState() => _CreateOrderState();
}

class _CreateOrderState extends State<_CreateOrder> {
  DateTime day = DateTime.now();
  List<Appointment> appointments = [];
  Appointment? selected;
  List<_StageDraft> stages = [_StageDraft()];
  bool busy = false;
  String? error;
  int page = 1;
  String? requestId;
  @override
  void initState() {
    super.initState();
    unawaited(load());
  }

  @override
  void dispose() {
    for (final s in stages) {
      s.dispose();
    }
    super.dispose();
  }

  Future<void> load() async {
    if (busy || !widget.isCurrent()) return;
    setState(() {
      busy = true;
      error = null;
      selected = null;
    });
    final date = DateTime(day.year, day.month, day.day);
    final r = await widget.scheduling.calendar(
      widget.business,
      date,
      DateTime(date.year, date.month, date.day + 1),
      page: page,
    );
    if (!mounted || !widget.isCurrent()) return;
    if (r case AuthFailure<List<Appointment>>(:final error)) {
      if ({
        AuthErrorCode.unauthenticated,
        AuthErrorCode.forbidden,
      }.contains(error.code)) {
        await widget.onAuthorizationFailure(error);
        if (!mounted || !widget.isCurrent()) return;
      }
      setState(() {
        this.error = schedulingError(error);
        busy = false;
      });
    } else {
      setState(() {
        appointments = (r as AuthSuccess<List<Appointment>>).value;
        busy = false;
      });
    }
  }

  Future<void> save() async {
    if (busy || selected == null || !widget.isCurrent()) return;
    final plans = stages
        .map(
          (s) => (
            name: s.name.text.trim(),
            minutes: int.tryParse(s.minutes.text) ?? 0,
          ),
        )
        .toList();
    if (plans.any(
          (s) => s.name.isEmpty || s.name.length > 100 || s.minutes <= 0,
        ) ||
        plans.fold<int>(0, (sum, s) => sum + s.minutes) !=
            selected!.activeMinutes) {
      setState(
        () => error =
            'Aşama adlarını ve süreleri kontrol edin. Toplam süre ${selected!.activeMinutes} dakika olmalıdır.',
      );
      return;
    }
    setState(() {
      busy = true;
      error = null;
    });
    requestId ??= workRequestId();
    final r = await widget.service.create(
      widget.business,
      selected!,
      plans,
      requestId!,
    );
    if (!mounted || !widget.isCurrent()) return;
    if (r case AuthFailure<WorkOrder>(:final error)) {
      if ({
        AuthErrorCode.unauthenticated,
        AuthErrorCode.forbidden,
      }.contains(error.code)) {
        await widget.onAuthorizationFailure(error);
        if (!mounted || !widget.isCurrent()) return;
      }
      setState(() {
        this.error = schedulingError(error);
        busy = false;
      });
    } else {
      Navigator.of(context).pop();
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Randevudan iş emri')),
    body: !widget.isCurrent()
        ? const SizedBox.shrink()
        : ListView(
            padding: const EdgeInsets.all(16),
            children: [
              if (busy) const LinearProgressIndicator(),
              if (error != null)
                Text(
                  error!,
                  style: TextStyle(color: Theme.of(context).colorScheme.error),
                ),
              const Text(
                'Randevunun ayrılmış çalışma alanı ve çalışanları korunur. Aşamalar bu kaynaklarla sırayla yürütülür.',
              ),
              OutlinedButton(
                onPressed: busy
                    ? null
                    : () async {
                        final value = await showDatePicker(
                          context: context,
                          initialDate: day,
                          firstDate: DateTime(2020),
                          lastDate: DateTime(2100),
                        );
                        if (value != null && mounted) {
                          day = value;
                          page = 1;
                          requestId = null;
                          await load();
                        }
                      },
                child: Text(
                  'İşletme tarihi: ${day.year}-${day.month.toString().padLeft(2, '0')}-${day.day.toString().padLeft(2, '0')}',
                ),
              ),
              if (!busy && appointments.isEmpty)
                const Text('Bu tarihte randevu bulunmuyor.'),
              DropdownButtonFormField<String>(
                key: ValueKey((day, page, selected?.id)),
                initialValue: selected?.id,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Onaylı randevu'),
                items: [
                  for (final a in appointments.where(
                    (a) => a.status == 'Confirmed',
                  ))
                    DropdownMenuItem(
                      value: a.id,
                      child: Text(
                        '${a.vehiclePlate} · ${a.serviceName}',
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                ],
                onChanged: busy
                    ? null
                    : (id) {
                        setState(() {
                          selected = appointments.firstWhere((a) => a.id == id);
                          requestId = null;
                          stages.first.minutes.text =
                              '${selected!.activeMinutes}';
                          stages.first.name.text = selected!.serviceName;
                        });
                      },
              ),
              Row(
                children: [
                  TextButton(
                    onPressed: busy || page == 1
                        ? null
                        : () {
                            page--;
                            unawaited(load());
                          },
                    child: const Text('Önceki'),
                  ),
                  TextButton(
                    onPressed: busy || appointments.length < 50
                        ? null
                        : () {
                            page++;
                            unawaited(load());
                          },
                    child: const Text('Sonraki'),
                  ),
                ],
              ),
              for (var i = 0; i < stages.length; i++)
                Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: Column(
                    children: [
                      TextField(
                        controller: stages[i].name,
                        enabled: !busy,
                        maxLength: 100,
                        decoration: InputDecoration(
                          labelText: '${i + 1}. aşama adı',
                        ),
                        onChanged: (_) => requestId = null,
                      ),
                      TextField(
                        controller: stages[i].minutes,
                        enabled: !busy,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(
                          labelText: 'Tahmini süre (dakika)',
                        ),
                        onChanged: (_) => requestId = null,
                      ),
                    ],
                  ),
                ),
              TextButton(
                onPressed: busy || stages.length >= 20
                    ? null
                    : () {
                        setState(() {
                          stages.add(_StageDraft());
                          requestId = null;
                        });
                      },
                child: const Text('Aşama ekle'),
              ),
              if (stages.length > 1)
                TextButton(
                  onPressed: busy
                      ? null
                      : () {
                          setState(() {
                            stages.removeLast().dispose();
                            requestId = null;
                          });
                        },
                  child: const Text('Son aşamayı kaldır'),
                ),
              if (selected != null)
                Text('Toplam planlı süre: ${selected!.activeMinutes} dakika'),
              FilledButton(
                onPressed: busy || selected == null ? null : save,
                child: const Text('İş emrini oluştur'),
              ),
            ],
          ),
  );
}
