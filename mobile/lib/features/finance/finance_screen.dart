import 'dart:async';

import 'package:flutter/material.dart';

import '../auth/domain/auth_service.dart';
import '../scheduling/domain/scheduling.dart';
import '../service_catalog/domain/service_catalog.dart';
import '../work_orders/work_order_screen.dart' show workRequestId;
import 'finance.dart';

class FinanceScreen extends StatefulWidget {
  const FinanceScreen({
    super.key,
    required this.service,
    required this.business,
    required this.store,
    required this.isCurrent,
    required this.onAuthorizationFailure,
  });
  final FinanceService service;
  final String business;
  final BookingAttemptStore store;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  @override
  State<FinanceScreen> createState() => _FinanceScreenState();
}

class _FinanceScreenState extends State<FinanceScreen>
    with WidgetsBindingObserver {
  final scroll = ScrollController();
  bool busy = false, ready = false, all = false;
  int section = 0, page = 1;
  String kind = 'expenses';
  String? message;
  FinanceSettings? settings;
  FinanceReport? report;
  List<Receivable> receivables = [];
  List<FinanceEntry> entries = [];
  FinanceAttempt? pending;
  String? from, to, form, method, employee;
  Receivable? selectedWork;
  FinanceEntry? correction, reverse;
  final amount = TextEditingController(),
      category = TextEditingController(),
      description = TextEditingController(),
      reference = TextEditingController(),
      reason = TextEditingController(),
      methodName = TextEditingController();
  DateTime wall = DateTime.now();
  int? offset;
  bool current() => mounted && widget.isCurrent();
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    unawaited(initialize());
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    for (final c in [
      amount,
      category,
      description,
      reference,
      reason,
      methodName,
    ]) {
      c.dispose();
    }
    scroll.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed && !busy) unawaited(refresh());
  }

  Future<T?> value<T>(AuthResult<T> result) async {
    if (!current()) return null;
    if (result case AuthSuccess<T>(:final value)) return value;
    final error = (result as AuthFailure<T>).error;
    if (error.code == AuthErrorCode.unauthenticated ||
        error.code == AuthErrorCode.forbidden) {
      await widget.onAuthorizationFailure(error);
    }
    if (current()) setState(() => message = financeError(error));
    return null;
  }

  Future<void> run(Future<void> Function() operation) async {
    if (!current() || busy) return;
    setState(() {
      busy = true;
      message = null;
    });
    try {
      await operation();
    } catch (_) {
      if (current()) {
        setState(
          () => message = 'İşlem tamamlanamadı. Kaydedilmiş istek varsa aynı anahtarla yeniden kontrol edin.',
        );
      }
    } finally {
      if (current()) setState(() => busy = false);
    }
  }

  Future<void> initialize() => run(() async {
    final raw = await widget.store.read();
    if (!current()) return;
    pending = raw == null ? null : FinanceAttempt.decode(raw);
    ready = true;
    await loadSettings();
    await loadReport();
  });
  Future<void> loadSettings() async {
    final rows = await value(await widget.service.settings(widget.business));
    if (current() && rows != null) setState(() => settings = rows);
  }

  Future<void> loadReport() async {
    final rows = await value(
      await widget.service.report(widget.business, from: from, to: to),
    );
    if (current() && rows != null) setState(() => report = rows);
  }

  Future<void> loadReceivables() async {
    final rows = await value(
      await widget.service.receivables(widget.business, page, all: all),
    );
    if (current() && rows != null) setState(() => receivables = rows);
  }

  Future<void> loadEntries() async {
    final rows = await value(
      await widget.service.entries(widget.business, kind, page),
    );
    if (current() && rows != null) setState(() => entries = rows);
  }

  Future<void> refresh() => run(() async {
    if (!ready) {
      final raw = await widget.store.read();
      if (!current()) return;
      pending = raw == null ? null : FinanceAttempt.decode(raw);
      ready = true;
    }
    await loadSettings();
    if (section == 0) await loadReport();
    if (section == 1) await loadReceivables();
    if (section == 2) await loadEntries();
  });
  void openForm(
    String kind, {
    Receivable? work,
    FinanceEntry? old,
    FinanceEntry? reversing,
  }) {
    if (busy || pending != null || settings == null || report == null) return;
    final clock = BusinessClock(report!.zone);
    final local = clock.local(old?.at ?? DateTime.now().toUtc());
    setState(() {
      form = kind;
      selectedWork = work;
      correction = old;
      reverse = reversing;
      amount.text = old?.amount.wire ?? work?.remaining.wire ?? '';
      category.text = old?.category ?? '';
      description.text = old?.description ?? '';
      reference.text = old?.reference ?? '';
      reason.clear();
      employee = settings!.employees.any((e) => e.id == old?.employee)
          ? old?.employee
          : null;
      method = settings!.methods.where((m) => !m.archived).firstOrNull?.id;
      wall = DateTime.utc(
        local.year,
        local.month,
        local.day,
        local.hour,
        local.minute,
      );
      offset = null;
    });
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (current() && scroll.hasClients) scroll.jumpTo(0);
    });
  }

  Future<void> chooseDate() async {
    final selected = await showDatePicker(
      context: context,
      initialDate: DateTime(wall.year, wall.month, wall.day),
      firstDate: DateTime(2000),
      lastDate: DateTime(2100, 12, 31),
    );
    if (!current() || selected == null) return;
    setState(() {
      wall = DateTime.utc(
        selected.year,
        selected.month,
        selected.day,
        wall.hour,
        wall.minute,
      );
      offset = null;
    });
  }

  Future<void> chooseTime() async {
    final selected = await showTimePicker(
      context: context,
      initialTime: TimeOfDay(hour: wall.hour, minute: wall.minute),
    );
    if (!current() || selected == null) return;
    setState(() {
      wall = DateTime.utc(
        wall.year,
        wall.month,
        wall.day,
        selected.hour,
        selected.minute,
      );
      offset = null;
    });
  }

  Future<bool> confirm(String title, String body) async =>
      await showDialog<bool>(
        context: context,
        builder: (ctx) => AlertDialog(
          title: Text(title),
          content: Text(body),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(ctx, false),
              child: const Text('Vazgeç'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(ctx, true),
              child: const Text('Onayla'),
            ),
          ],
        ),
      ) ??
      false;
  Future<void> save() async {
    if (form == null || pending != null) return;
    Map<String, dynamic> body;
    try {
      if (reverse != null) {
        if (reason.text.trim().isEmpty) throw const FormatException();
        body = {'requestId': workRequestId(), 'reason': reason.text.trim()};
      } else {
        final money = Lira.parse(amount.text, turkish: true);
        if (money.kurus <= 0) throw const FormatException();
        if (form != 'payments' && category.text.trim().isEmpty ||
            correction != null && reason.text.trim().isEmpty ||
            form != 'expenses' && method == null) {
          throw const FormatException();
        }
        final at = BusinessClock(report!.zone)
            .resolve(wall, offsetMinutes: offset);
        body = {
          'requestId': workRequestId(),
          'amount': money.wire,
          'occurredAtUtc': at.toIso8601String(),
          'description': description.text.trim(),
          'reference': reference.text.trim(),
          if (form != 'payments') 'category': category.text.trim(),
          if (form == 'payments') 'workOrderId': selectedWork!.id,
          if (form != 'expenses') 'paymentMethodId': method,
          if (form == 'expenses' && employee != null)
            'employeeMembershipId': employee,
          if (correction != null) 'correctsId': correction!.id,
          if (correction != null) 'correctionReason': reason.text.trim(),
        };
      }
    } catch (_) {
      setState(
        () => message = 'Tutarı, kategoriyi, yöntemi ve işletme yerel saatini kontrol edin. Düzeltme için gerekçe gereklidir.',
      );
      if (scroll.hasClients) scroll.jumpTo(0);
      return;
    }
    if (!await confirm(
      reverse != null
          ? 'Ters kayıt oluştur'
          : correction != null
          ? 'Gideri düzelt'
          : 'Finans kaydını onayla',
      reverse != null
          ? '${reverse!.amount.display} tutarındaki kayıt silinmeden ters çevrilecek. Bu işlem gerçek para iadesi yapmaz.'
          : '${financeKinds[form]} • ${body['amount']} TRY\n${localDisplay(wall)} • ${report!.zone}${correction != null ? '\nEski tutar ters kayıtla korunacak.' : ''}',
    )) {
      return;
    }
    if (!current()) return;
    await run(() async {
      final attempt = FinanceAttempt(form!, body, reverse: reverse?.id);
      await widget.store.write(attempt.encode());
      if (!current()) return;
      setState(() => pending = attempt);
      await submitPending();
    });
  }

  Future<void> submitPending() async {
    final attempt = pending;
    if (attempt == null) return;
    final saved = await value(
      await widget.service.submit(widget.business, attempt),
    );
    if (!current() || saved == null) return;
    setState(() {
      form = null;
      correction = null;
      reverse = null;
      section = 2;
      kind = saved.kind;
      page = 1;
      message = 'Finans kaydı sunucuya kaydedildi.';
    });
    try {
      await widget.store.clear();
      if (current()) setState(() => pending = null);
    } catch (_) {
      if (current()) {
        setState(
          () => message = 'Kayıt sunucuda mevcut. Cihaz isteği temizlenemedi; aynı isteği kontrol etmek çift kayıt oluşturmaz.',
        );
      }
    }
    await loadEntries();
    await loadReport();
  }

  Future<void> discard() async {
    if (!await confirm(
      'Kaydedilmiş isteği bırak',
      'İstek sunucuya ulaşmış olabilir. Önce finans kayıtlarını kontrol edin. Yeni istek ayrı bir kayıt oluşturabilir.',
    )) {
      return;
    }
    if (!current()) return;
    await run(() async {
      await widget.store.clear();
      if (current()) setState(() => pending = null);
    });
  }

  Future<void> preset(String period) => run(() async {
    final today = DateTime.parse(report!.today);
    DateTime first, last;
    if (period == 'Bugün') {
      first = today;
      last = today.add(const Duration(days: 1));
    } else if (period == 'Bu hafta') {
      first = today.subtract(Duration(days: today.weekday - 1));
      last = first.add(const Duration(days: 7));
    } else {
      first = DateTime.utc(today.year, today.month);
      last = DateTime.utc(today.year, today.month + 1);
    }
    from = dateWire(first);
    to = dateWire(last);
    await loadReport();
  });
  Future<void> range() async {
    final today = DateTime.parse(report!.today);
    final chosen = await showDateRangePicker(
      context: context,
      firstDate: DateTime(2000),
      lastDate: DateTime(2100, 12, 30),
      initialDateRange: DateTimeRange(
        start: DateTime.parse(report!.from),
        end: DateTime.parse(report!.to).subtract(const Duration(days: 1)),
      ),
      currentDate: today,
    );
    if (!current() || chosen == null) return;
    await run(() async {
      from = dateWire(chosen.start);
      to = dateWire(chosen.end.add(const Duration(days: 1)));
      await loadReport();
    });
  }

  Widget pages(bool more, Future<void> Function() load) => Row(
    mainAxisAlignment: MainAxisAlignment.spaceBetween,
    children: [
      TextButton(
        onPressed: busy || page == 1
            ? null
            : () => run(() async {
                page--;
                await load();
              }),
        child: const Text('Önceki'),
      ),
      Text('Sayfa $page'),
      TextButton(
        onPressed: busy || !more
            ? null
            : () => run(() async {
                page++;
                await load();
              }),
        child: const Text('Sonraki'),
      ),
    ],
  );
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Finans yönetimi'),
      actions: [
        IconButton(
          onPressed: busy ? null : refresh,
          tooltip: 'Yenile',
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: Column(
      children: [
        if (message != null)
          Padding(
            padding: const EdgeInsets.all(12),
            child: Semantics(liveRegion: true, child: Text(message!)),
          ),
        Expanded(
          child: ListView(
            controller: scroll,
            padding: const EdgeInsets.all(16),
            children: [
              if (busy) const LinearProgressIndicator(),
              if (!ready || report == null || settings == null)
                FilledButton(
                  onPressed: busy ? null : refresh,
                  child: const Text('Finans verilerini yeniden yükle'),
                ),
              if (pending != null)
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(12),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        const Text(
                          'Son finans isteği cihazda korunuyor. Önce aynı isteği kontrol edin.',
                        ),
                        FilledButton(
                          onPressed: busy ? null : () => run(submitPending),
                          child: const Text(
                            'Kaydedilmiş finans isteğini kontrol et',
                          ),
                        ),
                        TextButton(
                          onPressed: busy ? null : discard,
                          child: const Text(
                            'Kayıtları kontrol ettim; isteği bırak',
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              Wrap(
                spacing: 8,
                children: [
                  for (final entry in const {
                    0: 'Özet',
                    1: 'Ödemeler',
                    2: 'Kayıtlar',
                    3: 'Yöntemler',
                  }.entries)
                    ChoiceChip(
                      label: Text(entry.value),
                      selected: section == entry.key,
                      onSelected: busy
                          ? null
                          : (_) {
                              setState(() {
                                section = entry.key;
                                form = null;
                                page = 1;
                              });
                              unawaited(
                                run(() async {
                                  if (section == 0) await loadReport();
                                  if (section == 1) await loadReceivables();
                                  if (section == 2) await loadEntries();
                                  if (section == 3) await loadSettings();
                                }),
                              );
                            },
                    ),
                ],
              ),
              if (form != null && report != null && settings != null)
                ...formView()
              else if (section == 0 && report != null)
                ...reportView()
              else if (section == 1)
                ...paymentView()
              else if (section == 2)
                ...entryView()
              else if (section == 3 && settings != null)
                ...methodView(),
            ],
          ),
        ),
      ],
    ),
  );
  List<Widget> reportView() {
    final r = report!;
    return [
      Text('${r.from} → ${r.to} (bitiş hariç) • ${r.zone}'),
      Wrap(
        spacing: 8,
        children: [
          for (final p in ['Bugün', 'Bu hafta', 'Bu ay'])
            OutlinedButton(
              onPressed: busy ? null : () => preset(p),
              child: Text(p),
            ),
          OutlinedButton(
            onPressed: busy ? null : range,
            child: const Text('Tarih aralığı'),
          ),
        ],
      ),
      for (final t in const {
        'earnedRevenue': 'Tamamlanan hizmet geliri',
        'collectedPayments': 'Tahsil edilen ödemeler',
        'manualIncome': 'Ek gelir',
        'expenses': 'Kaydedilen giderler',
        'serviceOperatingResult': 'Hizmet esaslı faaliyet sonucu',
        'cashOperatingBalance': 'Nakit faaliyet bakiyesi',
        'unpaidBalance': 'Toplam tahsil edilmemiş bakiye (tüm tarihler)',
      }.entries)
        Card(
          child: ListTile(
            title: Text(t.value),
            subtitle: Text(r.totals[t.key]!.display),
          ),
        ),
      const Text(
        'Hizmet geliri tamamlanan işlerden hesaplanır. Tahsilat tekrar gelire eklenmez. Nakit faaliyet bakiyesi = tahsilat + ek gelir − gider. Bu değerler muhasebe kârı değildir. Ters kayıtlar hatalı işlemin tarihini düzeltir; oluşturulma geçmişi korunur.',
      ),
      Wrap(
        spacing: 8,
        children: [
          FilledButton(
            onPressed: busy || pending != null
                ? null
                : () => openForm('expenses'),
            child: const Text('Gider ekle'),
          ),
          OutlinedButton(
            onPressed: busy || pending != null
                ? null
                : () => openForm('manual-income'),
            child: const Text('Ek gelir ekle'),
          ),
        ],
      ),
      ...breakdown('Hizmete göre kazanılan gelir', r.services),
      ...breakdown('Gider kategorileri', r.expenses),
      ...breakdown('Yönteme göre tahsilat + ek gelir', r.methods),
      const Divider(),
      const Text('Günlük eğilim • hizmet geliri / tahsilat / gider'),
      for (final t in r.trends)
        ListTile(
          title: Text(t.date),
          subtitle: Text(
            '${t.earned.display} / ${t.collected.display} / ${t.expenses.display}',
          ),
        ),
    ];
  }

  List<Widget> breakdown(
    String title,
    List<({String label, FinanceAmount amount})> rows,
  ) {
    final max = rows.fold<BigInt>(
      BigInt.zero,
      (v, r) => r.amount.kurus.abs() > v ? r.amount.kurus.abs() : v,
    );
    return [
      const Divider(),
      Text(title, style: Theme.of(context).textTheme.titleMedium),
      if (rows.isEmpty) const Text('Bu dönemde kayıt yok.'),
      for (final r in rows)
        Padding(
          padding: const EdgeInsets.symmetric(vertical: 8),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('${r.label}: ${r.amount.display}'),
              if (max > BigInt.zero)
                Semantics(
                  label: '${r.label} ${r.amount.display}',
                  child: LinearProgressIndicator(
                    value: (r.amount.kurus.abs().toDouble() / max.toDouble())
                        .clamp(0, 1),
                  ),
                ),
            ],
          ),
        ),
    ];
  }

  List<Widget> paymentView() => [
    SwitchListTile(
      title: const Text('Ödenmiş işler dahil'),
      value: all,
      onChanged: busy
          ? null
          : (v) {
              setState(() {
                all = v;
                page = 1;
              });
              unawaited(run(loadReceivables));
            },
    ),
    if (receivables.isEmpty && !busy)
      const Text('Bu listede tamamlanmış iş emri yok.'),
    for (final w in receivables)
      Card(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('${w.plate} • ${w.service}'),
              Text(
                'İş bedeli: ${w.price.display}\nTahsilat: ${w.collected.display}\nKalan: ${w.remaining.display}',
              ),
              Text(switch (w.status) {
                'Paid' => 'Ödendi',
                'Partial' => 'Kısmi ödeme',
                _ => 'Ödenmedi',
              }),
              if (w.remaining.kurus > BigInt.zero)
                FilledButton(
                  onPressed: busy || pending != null
                      ? null
                      : () => openForm('payments', work: w),
                  child: const Text('Ödeme kaydet'),
                ),
            ],
          ),
        ),
      ),
    pages(receivables.length == 20, loadReceivables),
  ];
  List<Widget> entryView() => [
    DropdownButtonFormField<String>(
      initialValue: kind,
      decoration: const InputDecoration(labelText: 'Kayıt türü'),
      items: [
        for (final e in financeKinds.entries)
          DropdownMenuItem(value: e.key, child: Text(e.value)),
      ],
      onChanged: busy
          ? null
          : (k) {
              if (k == null) return;
              setState(() {
                kind = k;
                page = 1;
              });
              unawaited(run(loadEntries));
            },
    ),
    if (kind != 'payments')
      FilledButton(
        onPressed: busy || pending != null ? null : () => openForm(kind),
        child: Text(kind == 'expenses' ? 'Gider ekle' : 'Ek gelir ekle'),
      ),
    if (entries.isEmpty && !busy) const Text('Henüz finans kaydı yok.'),
    for (final e in entries)
      Card(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                '${e.reverses != null ? "Ters kayıt • " : ""}${e.amount.display} • ${e.category ?? e.methodName ?? "Tahsilat"}',
              ),
              Text(
                '${report == null ? e.at.toIso8601String() : localDisplay(BusinessClock(report!.zone).local(e.at))}\n${e.description ?? ""}\n${e.reference ?? ""}',
              ),
              if (e.corrects != null)
                const Text('Önceki giderin düzeltme kaydı.'),
              if (e.reason != null) Text('Gerekçe: ${e.reason}'),
              if (e.reverses == null && !entries.any((r) => r.reverses == e.id))
                Wrap(
                  spacing: 8,
                  children: [
                    if (kind == 'expenses')
                      OutlinedButton(
                        onPressed: busy || pending != null
                            ? null
                            : () => openForm(kind, old: e),
                        child: const Text('Gideri düzelt'),
                      ),
                    TextButton(
                      onPressed: busy || pending != null
                          ? null
                          : () => openForm(kind, reversing: e),
                      child: const Text('Ters kayıtla iptal et'),
                    ),
                  ],
                ),
            ],
          ),
        ),
      ),
    pages(entries.length == 20, loadEntries),
  ];
  List<Widget> methodView() => [
    const Text(
      'Yöntemler işletmenize özeldir. Arşivleme geçmiş tahsilat adlarını değiştirmez.',
    ),
    TextField(
      controller: methodName,
      maxLength: 100,
      enabled: !busy,
      decoration: const InputDecoration(labelText: 'Yeni ödeme yöntemi'),
    ),
    FilledButton(
      onPressed: busy
          ? null
          : () => run(() async {
              final row = await value(
                await widget.service.addMethod(
                  widget.business,
                  methodName.text.trim(),
                ),
              );
              if (row != null && current()) {
                methodName.clear();
                await loadSettings();
              }
            }),
      child: const Text('Yöntem ekle'),
    ),
    for (final m in settings!.methods)
      ListTile(
        title: Text(m.name),
        subtitle: Text(m.archived ? 'Arşivlendi' : 'Etkin'),
        trailing: m.archived
            ? null
            : TextButton(
                onPressed: busy
                    ? null
                    : () async {
                        if (!await confirm('Yöntemi arşivle', m.name)) return;
                        if (!current()) return;
                        await run(() async {
                          await value(
                            await widget.service.archiveMethod(
                              widget.business,
                              m,
                            ),
                          );
                          await loadSettings();
                        });
                      },
                child: const Text('Arşivle'),
              ),
      ),
  ];
  List<Widget> formView() {
    final candidates = BusinessClock(report!.zone).candidates(wall);
    return [
      Text(
        reverse != null
            ? 'Ters kayıt'
            : correction != null
            ? 'Gider düzeltme'
            : financeKinds[form]!,
        style: Theme.of(context).textTheme.titleLarge,
      ),
      if (selectedWork != null)
        Text(
          '${selectedWork!.plate} • kalan ${selectedWork!.remaining.display}',
        ),
      if (reverse == null) ...[
        TextField(
          controller: amount,
          enabled: !busy && pending == null,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: const InputDecoration(
            labelText: 'Tutar (TRY)',
            hintText: '450,25',
          ),
        ),
        if (form != 'payments')
          TextField(
            controller: category,
            maxLength: 100,
            enabled: !busy && pending == null,
            decoration: const InputDecoration(
              labelText: 'Kategori',
              hintText: 'Kira, su, malzeme veya diğer',
            ),
          ),
        if (form != 'expenses')
          DropdownButtonFormField<String>(
            initialValue: method,
            decoration: const InputDecoration(labelText: 'Ödeme yöntemi'),
            items: [
              for (final m in settings!.methods.where((m) => !m.archived))
                DropdownMenuItem(value: m.id, child: Text(m.name)),
            ],
            onChanged: busy || pending != null
                ? null
                : (v) => setState(() => method = v),
          ),
        if (form == 'expenses')
          DropdownButtonFormField<String>(
            initialValue: employee,
            decoration: const InputDecoration(
              labelText: 'İlgili çalışan (isteğe bağlı)',
            ),
            items: [
              const DropdownMenuItem(
                value: null,
                child: Text('Çalışan seçilmedi'),
              ),
              for (final e in settings!.employees)
                DropdownMenuItem(value: e.id, child: Text(e.name)),
            ],
            onChanged: busy || pending != null
                ? null
                : (v) => setState(() => employee = v),
          ),
        OutlinedButton(
          onPressed: busy || pending != null ? null : chooseDate,
          child: Text('Kayıt tarihi: ${dateWire(wall)}'),
        ),
        OutlinedButton(
          onPressed: busy || pending != null ? null : chooseTime,
          child: Text('Saat: ${timeWire(wall)} • ${report!.zone}'),
        ),
        if (candidates.isEmpty)
          const Text('Bu yerel saat mevcut değil. Başka saat seçin.'),
        if (candidates.length > 1)
          DropdownButtonFormField<int>(
            initialValue: offset,
            decoration: const InputDecoration(
              labelText: 'Tekrarlanan saat için UTC farkı (dakika)',
            ),
            items: [
              for (final c in candidates)
                DropdownMenuItem(
                  value: BusinessClock(report!.zone)
                      .local(c)
                      .timeZoneOffset
                      .inMinutes,
                  child: Text(
                    'UTC ${BusinessClock(report!.zone).local(c).timeZoneOffset.inMinutes} dakika',
                  ),
                ),
            ],
            onChanged: busy || pending != null
                ? null
                : (v) => setState(() => offset = v),
          ),
        TextField(
          controller: description,
          maxLength: 1000,
          enabled: !busy && pending == null,
          decoration: const InputDecoration(
            labelText: 'Açıklama (isteğe bağlı)',
          ),
        ),
        TextField(
          controller: reference,
          maxLength: 200,
          enabled: !busy && pending == null,
          decoration: const InputDecoration(
            labelText: 'Belge / referans (isteğe bağlı)',
          ),
        ),
      ],
      if (correction != null || reverse != null)
        TextField(
          controller: reason,
          maxLength: 500,
          enabled: !busy && pending == null,
          decoration: const InputDecoration(labelText: 'Düzeltme gerekçesi'),
        ),
      FilledButton(
        onPressed: busy || pending != null ? null : save,
        child: const Text('Kaydı onayla'),
      ),
      TextButton(
        onPressed: busy ? null : () => setState(() => form = null),
        child: const Text('Vazgeç'),
      ),
    ];
  }
}
