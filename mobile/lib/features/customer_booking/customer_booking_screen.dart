import 'dart:async';

import 'package:flutter/material.dart';

import '../auth/domain/auth_service.dart';
import '../customer_vehicles/domain/customer_vehicles.dart';
import '../scheduling/domain/scheduling.dart';
import '../work_orders/work_order_screen.dart' show workRequestId;
import 'customer_booking.dart';

/// One account-scoped flow. The server remains authoritative for every mutation.
class CustomerBookingScreen extends StatefulWidget {
  const CustomerBookingScreen({
    super.key,
    required this.service,
    required this.vehicles,
    required this.store,
    required this.isCurrent,
    required this.onAuthorizationFailure,
  });
  final CustomerBookingService service;
  final CustomerVehicleService vehicles;
  final CustomerAttemptStore store;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  @override
  State<CustomerBookingScreen> createState() => _CustomerBookingScreenState();
}

class _CustomerBookingScreenState extends State<CustomerBookingScreen>
    with WidgetsBindingObserver {
  final search = TextEditingController();
  bool busy = false, ready = false, history = false;
  String? message;
  int section = 0,
      businessPage = 1,
      servicePage = 1,
      vehiclePage = 1,
      slotPage = 1,
      minePage = 1;
  bool moreBusinesses = false, moreVehicles = false;
  List<PublicBusiness> businesses = [];
  List<VehicleRecord> vehicles = [];
  List<CustomerAppointment> appointments = [];
  PublicBusinessDetail? business;
  PublicService? service;
  VehicleRecord? vehicle;
  CustomerSlots? slots;
  CustomerAppointment? selected;
  bool rescheduling = false;
  DateTime date = DateTime.now();
  CustomerAttempt? pending;
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
    search.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed && !busy) {
      unawaited(refresh());
    }
  }

  Future<T?> value<T>(AuthResult<T> result) async {
    if (!current()) return null;
    if (result case AuthSuccess<T>(:final value)) return value;
    final error = (result as AuthFailure<T>).error;
    if (error.code == AuthErrorCode.unauthenticated ||
        error.code == AuthErrorCode.forbidden) {
      await widget.onAuthorizationFailure(error);
    }
    if (current()) setState(() => message = customerBookingError(error));
    return null;
  }

  Future<void> run(Future<void> Function() action) async {
    if (busy || !current()) return;
    setState(() {
      busy = true;
      message = null;
    });
    try {
      await action();
    } catch (_) {
      if (current()) {
        setState(
          () => message = 'İşlem tamamlanamadı. Kaydedilmiş isteği koruyarak yeniden deneyin.',
        );
      }
    } finally {
      if (current()) setState(() => busy = false);
    }
  }

  Future<void> initialize() => run(() async {
    final raw = await widget.store.read();
    if (!current()) return;
    if (raw != null) {
      pending = CustomerAttempt.decode(raw);
    }
    ready = true;
    await discover();
  });
  Future<void> discover() async {
    final rows = await value(
      await widget.service.discover(search.text.trim(), businessPage),
    );
    if (!current() || rows == null) return;
    setState(() {
      businesses = rows;
      moreBusinesses = rows.length == 20;
    });
  }

  Future<void> loadVehicles() async {
    final rows = await value(await widget.vehicles.vehicles(page: vehiclePage));
    if (!current() || rows == null) return;
    setState(() {
      vehicles = rows.items;
      moreVehicles = rows.hasMore;
    });
  }

  Future<void> openBusiness(String id) async {
    final d = await value(await widget.service.business(id, servicePage));
    if (!current() || d == null) return;
    setState(() {
      business = d;
      service = null;
      vehicle = null;
      slots = null;
      selected = null;
      rescheduling = false;
    });
    await loadVehicles();
  }

  Future<void> loadMine() async {
    final rows = await value(
      await widget.service.mine(history ? 'history' : 'upcoming', minePage),
    );
    if (!current() || rows == null) return;
    setState(() => appointments = rows);
  }

  Future<void> openAppointment(String id) async {
    final row = await value(await widget.service.detail(id));
    if (!current() || row == null) return;
    setState(() {
      selected = row;
      slots = null;
      rescheduling = false;
    });
  }

  Future<void> loadSlots() async {
    final b = rescheduling ? selected!.business : business?.business.id;
    final s = rescheduling ? selected!.service : service?.id;
    final v = rescheduling ? selected!.vehicle : vehicle?.id;
    if (b == null || s == null || v == null) {
      setState(() => message = 'Kendi aracınızı ve hizmeti seçin.');
      return;
    }
    final rows = await value(
      await widget.service.slots(
        b,
        s,
        v,
        date,
        slotPage,
        appointment: rescheduling ? selected!.id : null,
      ),
    );
    if (!current() || rows == null) return;
    setState(() => slots = rows);
  }

  Future<void> refresh() => run(() async {
    if (!ready) {
      final raw = await widget.store.read();
      if (!current()) return;
      pending = raw == null ? null : CustomerAttempt.decode(raw);
      ready = true;
    }
    if (selected != null) {
      await openAppointment(selected!.id);
    } else if (section == 1) {
      await loadMine();
    } else if (business != null) {
      await openBusiness(business!.business.id);
    } else {
      await discover();
    }
  });
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
  Future<void> create(CustomerSlot slot) async {
    final quote = slots!;
    if (!await confirm(
      'Randevuyu onayla',
      '${quote.plate}\n${quote.serviceName}\n${dateWire(date)} ${slot.label}\n${quote.minutes} dakika • ${quote.price.display}\n\nKapasite uygunsa hemen onaylanır. Başlangıca ${business!.cutoff} dakika kalana kadar iptal veya erteleme yapabilirsiniz.',
    )) {
      return;
    }
    if (!current()) return;
    await run(() async {
      final attempt = CustomerAttempt(quote.business, {
        'clientRequestId': workRequestId(),
        'ownedVehicleId': quote.vehicle,
        'serviceId': quote.service,
        'expectedServiceVersion': quote.serviceVersion,
        'expectedVehicleVersion': quote.vehicleVersion,
        'startsAtUtc': slot.start.toUtc().toIso8601String(),
      });
      // Save before sending. An uncertain response never generates another reservation key.
      await widget.store.write(attempt.encode());
      if (!current()) return;
      setState(() => pending = attempt);
      await submitPending();
    });
  }

  Future<void> submitPending() async {
    final attempt = pending;
    if (attempt == null) return;
    final row = await value(await widget.service.book(attempt));
    if (!current() || row == null) return;
    setState(() {
      selected = row;
      business = null;
      slots = null;
      section = 1;
      message = row.status == 'Confirmed'
          ? 'Randevunuz onaylandı.'
          : 'Bu istek daha önce kaydedilmiş; güncel randevu gösteriliyor.';
    });
    try {
      await widget.store.clear();
      if (current()) setState(() => pending = null);
    } catch (_) {
      if (current()) {
        setState(
          () => message = 'Randevu kaydedildi. Cihazdaki istek temizlenemedi; aynı isteği kontrol etmek yeni randevu oluşturmaz.',
        );
      }
    }
  }

  Future<void> discardPending() async {
    if (!await confirm(
      'Kaydedilmiş isteği bırak',
      'İstek sunucuya ulaşmış olabilir. Önce Randevularım listesini kontrol edin. Yeni istek ayrı bir randevu oluşturabilir.',
    )) {
      return;
    }
    if (!current()) return;
    await run(() async {
      await widget.store.clear();
      if (current()) setState(() => pending = null);
    });
  }

  Future<void> cancel() async {
    final row = selected!;
    if (!await confirm(
      'Randevuyu iptal et',
      '${row.local}\n${row.serviceName}\nRandevu ve kaynak rezervasyonları iptal edilecek.',
    )) {
      return;
    }
    if (!current()) return;
    await run(() async {
      final changed = await value(
        await widget.service.change(row, workRequestId()),
      );
      if (current() && changed != null) setState(() => selected = changed);
    });
  }

  Future<void> move(CustomerSlot slot) async {
    final row = selected!;
    if (!await confirm(
      'Randevuyu ertele',
      'Yeni saat: ${dateWire(date)} ${slot.label}\nFiyat: ${row.price.display}\nYeni saat uygun değilse mevcut randevunuz korunur.',
    )) {
      return;
    }
    if (!current()) return;
    await run(() async {
      final changed = await value(
        await widget.service.change(row, workRequestId(), start: slot.start),
      );
      if (current() && changed != null) {
        setState(() {
          selected = changed;
          rescheduling = false;
          slots = null;
        });
      }
    });
  }

  Future<void> pickDate() async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: date.isBefore(DateTime(now.year, now.month, now.day))
          ? now
          : date,
      firstDate: DateTime(now.year, now.month, now.day),
      lastDate: DateTime(now.year + 2, 12, 31),
    );
    if (!current() || picked == null) return;
    setState(() {
      date = picked;
      slots = null;
      slotPage = 1;
    });
  }

  Widget pages(int page, bool more, VoidCallback previous, VoidCallback next) =>
      Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          TextButton(
            onPressed: busy || page == 1 ? null : previous,
            child: const Text('Önceki'),
          ),
          Text('Sayfa $page'),
          TextButton(
            onPressed: busy || !more ? null : next,
            child: const Text('Sonraki'),
          ),
        ],
      );
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Randevularım ve işletmeler'),
      actions: [
        IconButton(
          onPressed: busy ? null : refresh,
          tooltip: 'Yenile',
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: ListView(
      padding: const EdgeInsets.all(16),
      children: [
        if (busy) const LinearProgressIndicator(),
        if (message != null) Semantics(liveRegion: true, child: Text(message!)),
        if (!ready)
          FilledButton(
            onPressed: busy ? null : initialize,
            child: const Text('Verileri yeniden yükle'),
          ),
        if (pending != null)
          Card(
            child: Padding(
              padding: const EdgeInsets.all(12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Text(
                    'Son rezervasyon isteği cihazda korunuyor. Aynı isteği kontrol etmek çift rezervasyon oluşturmaz.',
                  ),
                  FilledButton(
                    onPressed: busy ? null : () => run(submitPending),
                    child: const Text('Kaydedilmiş isteği kontrol et'),
                  ),
                  TextButton(
                    onPressed: busy ? null : discardPending,
                    child: const Text('Listeyi kontrol ettim; isteği bırak'),
                  ),
                ],
              ),
            ),
          ),
        SegmentedButton<int>(
          segments: const [
            ButtonSegment(value: 0, label: Text('İşletme bul')),
            ButtonSegment(value: 1, label: Text('Randevularım')),
          ],
          selected: {section},
          onSelectionChanged: busy
              ? null
              : (s) {
                  setState(() {
                    section = s.first;
                    business = null;
                    selected = null;
                    slots = null;
                    rescheduling = false;
                  });
                  unawaited(run(section == 0 ? discover : loadMine));
                },
        ),
        const SizedBox(height: 16),
        if (selected != null)
          ...appointmentView()
        else if (section == 1)
          ...mineView()
        else if (business != null)
          ...businessView()
        else
          ...discoveryView(),
      ],
    ),
  );
  List<Widget> discoveryView() => [
    TextField(
      controller: search,
      maxLength: 100,
      enabled: !busy,
      decoration: const InputDecoration(labelText: 'İşletme, il veya ilçe'),
      onSubmitted: (_) => run(() async {
        businessPage = 1;
        await discover();
      }),
    ),
    FilledButton(
      onPressed: busy
          ? null
          : () => run(() async {
              businessPage = 1;
              await discover();
            }),
      child: const Text('Ara'),
    ),
    if (businesses.isEmpty && !busy)
      const Text('Yayınlanmış hizmeti olan işletme bulunamadı.'),
    for (final b in businesses)
      ListTile(
        title: Text(b.name),
        subtitle: Text([b.city, b.district].whereType<String>().join(' / ')),
        trailing: const Icon(Icons.chevron_right),
        onTap: busy
            ? null
            : () => run(() async {
                servicePage = 1;
                vehiclePage = 1;
                await openBusiness(b.id);
              }),
      ),
    pages(
      businessPage,
      moreBusinesses,
      () => run(() async {
        businessPage--;
        await discover();
      }),
      () => run(() async {
        businessPage++;
        await discover();
      }),
    ),
  ];
  List<Widget> businessView() {
    final b = business!;
    return [
      TextButton(
        onPressed: busy
            ? null
            : () => setState(() {
                business = null;
                slots = null;
              }),
        child: const Text('İşletmelere dön'),
      ),
      Text(b.business.name, style: Theme.of(context).textTheme.headlineSmall),
      if (b.business.address != null) Text(b.business.address!),
      if (b.business.phone != null) Text(b.business.phone!),
      if (b.business.description != null) Text(b.business.description!),
      Text('İşletme saat dilimi: ${b.business.zone ?? "Belirlenmemiş"}'),
      if (b.hours == null)
        const Text('Çalışma saatleri henüz tanımlanmamış.')
      else
        for (final h in b.hours!)
          Text(
            '${const ["Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar"][(h["day"] as int) - 1]}: ${h["closed"] == true ? "Kapalı" : "${minuteLabel(h["opensAtMinute"] as int)}–${minuteLabel(h["closesAtMinute"] as int)}"}',
          ),
      Text('İptal / erteleme sınırı: başlangıca ${b.cutoff} dakika kala.'),
      const Divider(),
      const Text('Kendi aracınız'),
      if (vehicles.isEmpty)
        const Text(
          'Araç bulunamadı. Ana menüde Araçlarım bölümünden araç ekleyin.',
        ),
      for (final v in vehicles)
        CheckboxListTile(
          value: vehicle?.id == v.id,
          title: Text('${v.plate} • ${vehicleCategories[v.category]}'),
          onChanged: busy || pending != null
              ? null
              : (_) => setState(() {
                  vehicle = v;
                  service = null;
                  slots = null;
                }),
        ),
      pages(
        vehiclePage,
        moreVehicles,
        () => run(() async {
          vehiclePage--;
          vehicle = null;
          service = null;
          slots = null;
          await loadVehicles();
        }),
        () => run(() async {
          vehiclePage++;
          vehicle = null;
          service = null;
          slots = null;
          await loadVehicles();
        }),
      ),
      const Text('Hizmet seçin'),
      for (final s in b.services)
        for (final o in s.offerings.where(
          (o) => o.category == vehicle?.category,
        ))
          CheckboxListTile(
            value: service?.id == s.id,
            title: Text('${s.name} • ${o.price.display}'),
            subtitle: Text(
              '${o.minutes} dakika${o.configured ? "" : " • Randevu ayarları eksik"}',
            ),
            onChanged: busy || pending != null || !o.configured
                ? null
                : (_) => setState(() {
                    service = s;
                    slots = null;
                  }),
          ),
      pages(
        servicePage,
        b.hasMore,
        () => run(() async {
          servicePage--;
          await openBusiness(b.business.id);
        }),
        () => run(() async {
          servicePage++;
          await openBusiness(b.business.id);
        }),
      ),
      if (vehicle != null && service != null && pending == null) ...slotView(),
    ];
  }

  String minuteLabel(int minute) =>
      '${(minute ~/ 60).toString().padLeft(2, '0')}:${(minute % 60).toString().padLeft(2, '0')}';
  List<Widget> slotView() => [
    OutlinedButton(
      onPressed: busy ? null : pickDate,
      child: Text('Tarih: ${dateWire(date)} (işletme yerel tarihi)'),
    ),
    FilledButton(
      onPressed: busy
          ? null
          : () => run(() async {
              slotPage = 1;
              await loadSlots();
            }),
      child: const Text('Uygun saatleri göster'),
    ),
    if (slots != null) ...[
      Text(
        '${slots!.plate} • ${slots!.serviceName} • ${slots!.minutes} dakika • ${slots!.price.display}',
      ),
      const Text(
        'Saatler anlık uygunluğu gösterir. Onay sırasında kapasite yeniden kontrol edilir.',
      ),
      if (slots!.items.isEmpty) const Text('Bu tarihte uygun saat bulunamadı.'),
      for (final s in slots!.items)
        OutlinedButton(
          onPressed: busy ? null : () => rescheduling ? move(s) : create(s),
          child: Text(s.label),
        ),
      pages(
        slotPage,
        slots!.hasMore,
        () => run(() async {
          slotPage--;
          await loadSlots();
        }),
        () => run(() async {
          slotPage++;
          await loadSlots();
        }),
      ),
    ],
  ];
  List<Widget> mineView() => [
    SwitchListTile(
      title: const Text('Geçmiş randevular'),
      value: history,
      onChanged: busy
          ? null
          : (v) {
              setState(() {
                history = v;
                minePage = 1;
              });
              unawaited(run(loadMine));
            },
    ),
    if (appointments.isEmpty && !busy)
      const Text('Bu listede randevunuz bulunmuyor.'),
    for (final a in appointments)
      ListTile(
        title: Text('${a.businessName} • ${a.plate}'),
        subtitle: Text(
          '${a.local}\n${a.serviceName} • ${a.price.display}\n${statusText(a)}',
        ),
        isThreeLine: true,
        onTap: busy ? null : () => run(() => openAppointment(a.id)),
      ),
    pages(
      minePage,
      appointments.length == 20,
      () => run(() async {
        minePage--;
        await loadMine();
      }),
      () => run(() async {
        minePage++;
        await loadMine();
      }),
    ),
  ];
  String statusText(CustomerAppointment a) => a.status == 'Cancelled'
      ? 'İptal edildi'
      : switch (a.workStatus) {
          'Completed' => 'Tamamlandı',
          'InProgress' => 'İşlem devam ediyor',
          _ => 'Onaylandı',
        };
  List<Widget> appointmentView() {
    final a = selected!;
    return [
      TextButton(
        onPressed: busy
            ? null
            : () => run(() async {
                selected = null;
                rescheduling = false;
                slots = null;
                await loadMine();
              }),
        child: const Text('Randevu listesine dön'),
      ),
      Text(a.businessName, style: Theme.of(context).textTheme.headlineSmall),
      Text(a.plate),
      Text(a.serviceName),
      Text('${a.local} • ${a.zone}'),
      Text('${a.minutes} dakika • ${a.price.display}'),
      Text(statusText(a)),
      Text('İptal / erteleme: başlangıca ${a.cutoff} dakika kalana kadar.'),
      if (a.canChange) ...[
        OutlinedButton(
          onPressed: busy ? null : cancel,
          child: const Text('Randevuyu iptal et'),
        ),
        FilledButton(
          onPressed: busy
              ? null
              : () => setState(() {
                  rescheduling = !rescheduling;
                  slots = null;
                  slotPage = 1;
                  date =
                      DateTime.tryParse(
                        a.local.split(' ').first.split('.').reversed.join('-'),
                      ) ??
                      DateTime.now();
                }),
          child: Text(rescheduling ? 'Ertelemeden vazgeç' : 'Randevuyu ertele'),
        ),
        if (rescheduling) ...slotView(),
      ] else if (a.status == 'Confirmed')
        const Text('Bu randevu şu anda değiştirilemez. İşletmeyle görüşün.'),
    ];
  }
}
