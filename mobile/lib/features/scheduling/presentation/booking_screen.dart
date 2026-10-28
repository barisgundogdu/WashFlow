import 'dart:async';

import 'package:flutter/material.dart';

import '../../auth/domain/auth_service.dart';
import '../../customer_vehicles/domain/customer_vehicles.dart';
import '../../service_catalog/domain/service_catalog.dart';
import '../domain/scheduling.dart';
import 'scheduling_controller.dart';
import 'scheduling_widgets.dart';
import 'appointment_detail_screen.dart';

/// Follows the existing paged endpoints; never silently truncates selection lists.
Future<AuthResult<List<T>>> schedulingPages<T>(
  Future<AuthResult<RecordPage<T>>> Function(int) fetch,
) async {
  final all = <T>[];
  for (var p = 1; p <= 10000; p++) {
    final result = await fetch(p);
    switch (result) {
      case AuthFailure<RecordPage<T>>(:final error):
        return AuthFailure(error);
      case AuthSuccess<RecordPage<T>>(:final value):
        all.addAll(value.items);
        if (!value.hasMore) return AuthSuccess(List.unmodifiable(all));
    }
  }
  return AuthFailure(AuthError(code: AuthErrorCode.unexpected));
}

class BookingScreen extends StatefulWidget {
  const BookingScreen({super.key, required this.controller, this.appointment});
  final SchedulingController controller;
  final Appointment? appointment;
  @override
  State<BookingScreen> createState() => _BookingScreenState();
}

class _BookingScreenState extends State<BookingScreen> {
  SchedulingController get c => widget.controller;
  bool get rescheduling => widget.appointment != null;
  List<LocalCustomerRecord> customers = [];
  List<VehicleRecord> vehicles = [];
  List<CatalogServiceRecord> services = [];
  List<WorkArea> areas = [];
  List<SchedulingEmployee> employees = [];
  List<SchedulingRequirement> requirements = [];
  String? customerId, vehicleId, serviceId;
  Set<String> selectedEmployees = {};
  late DateTime wall;
  int? offset;
  bool loaded = false, completed = false, stale = false;
  AvailabilityPreview? preview;
  CatalogServiceRecord? get service =>
      services.where((v) => v.id == serviceId).firstOrNull;
  VehicleRecord? get vehicle =>
      vehicles.where((v) => v.id == vehicleId).firstOrNull;
  Offering? get offering => service?.offerings
      .where((v) => v.category == vehicle?.category)
      .firstOrNull;
  SchedulingRequirement? get requirement =>
      requirements.where((v) => v.category == vehicle?.category).firstOrNull;
  bool get locked => c.busy || c.uncertainBooking || completed || stale;
  @override
  void initState() {
    super.initState();
    final a = widget.appointment;
    final initial = a == null ? c.date! : c.clock!.local(a.startsAtUtc);
    wall = DateTime.utc(
      initial.year,
      initial.month,
      initial.day,
      a == null ? 9 : initial.hour,
      a == null ? 0 : initial.minute,
    );
    final candidates = c.clock!.candidates(wall);
    offset = a == null
        ? (candidates.length == 1
              ? c.clock!.local(candidates.single).timeZoneOffset.inMinutes
              : null)
        : c.clock!.local(a.startsAtUtc).timeZoneOffset.inMinutes;
    selectedEmployees = a?.employeeIds ?? {};
    unawaited(_load());
  }

  Future<void> _load() async {
    setState(() {
      loaded = false;
      preview = null;
    });
    final roster = await c.request(() => c.service.employees(c.business));
    if (!mounted || !c.active || roster == null) return;
    employees = roster;
    if (!rescheduling) {
      final cs = await c.request(
        () =>
            schedulingPages((p) => c.customers.customers(c.business, page: p)),
      );
      if (!mounted || !c.active || cs == null) return;
      customers = cs;
      final ss = await c.request(
        () => schedulingPages(
          (p) => c.catalog.list(c.business, status: 'enabled', page: p),
        ),
      );
      if (!mounted || !c.active || ss == null) return;
      services = ss.where((v) => v.enabled).toList();
      final ws = await c.request(() => c.service.areas(c.business));
      if (!mounted || !c.active || ws == null) return;
      areas = ws;
    }
    setState(() => loaded = true);
  }

  Future<void> _customer(String? value) async {
    setState(() {
      customerId = value;
      vehicleId = null;
      vehicles = [];
      preview = null;
    });
    if (value == null) return;
    final rows = await c.request(
      () => schedulingPages(
        (p) => c.customers.vehicles(
          business: c.business,
          customer: value,
          page: p,
        ),
      ),
    );
    if (!mounted || !c.active || customerId != value) return;
    setState(() => vehicles = rows ?? []);
  }

  Future<void> _service(String? value) async {
    setState(() {
      serviceId = value;
      requirements = [];
      preview = null;
    });
    if (value == null) return;
    final rows = await c.request(
      () => c.service.requirements(c.business, value),
    );
    if (!mounted || !c.active || serviceId != value) return;
    setState(() => requirements = rows ?? []);
  }

  ScheduleInput? _input() {
    final count = widget.appointment?.employees ?? offering?.employees;
    if (count == null ||
        selectedEmployees.length != count ||
        selectedEmployees.any(
          (id) => !employees.any((e) => e.membershipId == id),
        )) {
      c.show('Tam olarak ${count ?? 'gerekli sayıda'} etkin çalışan seçin.');
      return null;
    }
    if (!rescheduling &&
        (customerId == null ||
            vehicle == null ||
            service == null ||
            offering == null ||
            requirement?.singleStageConfirmed != true ||
            !areas.any(
              (a) => a.id == requirement!.workAreaId && a.archivedAt == null,
            ))) {
      c.show(
        'Müşteri, araç, etkin hizmet ve onaylı alan gereksinimini kontrol edin.',
      );
      return null;
    }
    try {
      final instant = c.clock!.resolve(wall, offsetMinutes: offset);
      return ScheduleInput(
        startLocal: localWire(wall),
        utcOffsetMinutes: c.clock!.local(instant).timeZoneOffset.inMinutes,
        employees: selectedEmployees,
      );
    } catch (_) {
      c.show('Geçerli bir yerel saat ve gerekiyorsa UTC farkı seçin.');
      return null;
    }
  }

  Future<void> _preview() async {
    final input = _input();
    if (input == null) return;
    final result = await c.request(
      () => c.service.preview(c.business, serviceId!, vehicle!.category, input),
    );
    if (mounted && c.active) setState(() => preview = result);
  }

  Future<void> _submit() async {
    final input = _input();
    if (input == null) return;
    if (!await schedulingConfirm(
          context,
          rescheduling ? 'Yeniden planlama onayı' : 'Randevu onayı',
          '${rescheduling ? widget.appointment!.serviceName : service!.name}\n${localDisplay(wall)} • ${c.clock!.zoneId}\n${rescheduling ? widget.appointment!.price.display : offering!.price.display}\n${selectedEmployees.length} çalışan\nUygunluk kaydetme sırasında yeniden kontrol edilir.',
          action: rescheduling ? 'Yeniden planla' : 'Randevuyu oluştur',
        ) ||
        !mounted ||
        !c.active) {
      return;
    }
    if (rescheduling) {
      final value = await c.request(
        () => c.service.reschedule(c.business, widget.appointment!, input),
      );
      if (!mounted || !c.active) return;
      if (value != null) {
        Navigator.pop(context);
        return;
      }
      setState(() => stale = true);
      c.show(
        '${c.message ?? 'Sonuç doğrulanamadı.'} Ayrıntıya dönüp güncel kaydı yükleyin; otomatik tekrar yapılmadı.',
      );
    } else {
      final result = await c.book(
        BookingPayload(
          customerId: customerId!,
          vehicleId: vehicleId!,
          serviceId: serviceId!,
          serviceVersion: service!.version,
          vehicleVersion: vehicle!.version,
          schedule: input,
        ),
      );
      if (!mounted || !c.active || result == null) return;
      setState(() => completed = true);
      // Replay intentionally returns the original snapshot. Always open current detail.
      await Navigator.of(context).pushReplacement(
        MaterialPageRoute<void>(
          builder: (_) => AppointmentDetailScreen(controller: c, id: result.id),
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: c,
    builder: (context, _) => PopScope(
      canPop: !c.busy && !c.uncertainBooking,
      onPopInvokedWithResult: (didPop, _) async {
        if (didPop || c.busy || !c.uncertainBooking) return;
        if (await schedulingConfirm(
              context,
              'Kaydetme sonucu doğrulanmadı',
              'Randevu sunucuda oluşmuş olabilir. Çıkarsanız yeni kayıt oluşturmadan önce takvimi kontrol edin.',
              action: 'Takvime dön',
            ) &&
            context.mounted &&
            c.active) {
          c.abandonAttempt();
          Navigator.pop(context);
        }
      },
      child: SchedulePage(
        controller: c,
        title: rescheduling ? 'Yeniden planla' : 'Yeni randevu',
        children: [
          if (!loaded)
            OutlinedButton(
              onPressed: c.busy ? null : _load,
              child: const Text('Seçenekleri yeniden yükle'),
            ),
          if (loaded) ...[
            if (!rescheduling) ...[
              schedulingSelect<String>(
                label: 'Müşteri',
                value: customerId,
                options: {for (final v in customers) v.id: v.fullName},
                onChanged: locked ? null : _customer,
              ),
              if (customers.isEmpty)
                const Text(
                  'Önce Müşteriler ve araçlar bölümünden müşteri ekleyin.',
                ),
              schedulingSelect<String>(
                label: 'Müşterinin aracı',
                value: vehicleId,
                options: {
                  for (final v in vehicles)
                    v.id: '${v.plate} • ${vehicleCategories[v.category]}',
                },
                onChanged: locked
                    ? null
                    : (v) => setState(() {
                        vehicleId = v;
                        preview = null;
                      }),
              ),
              schedulingSelect<String>(
                label: 'Etkin hizmet',
                value: serviceId,
                options: {for (final v in services) v.id: v.name},
                onChanged: locked ? null : _service,
              ),
              if (vehicle != null && service != null) ...[
                Text(
                  'Araç türü teklifi: ${vehicleCategories[vehicle!.category]}',
                ),
                if (offering == null)
                  const Text('Bu araç türü için hizmet teklifi bulunmuyor.')
                else
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Text(
                        '${offering!.price.display}\n${offering!.estimate}',
                      ),
                    ),
                  ),
                Text(
                  'Çalışma alanı: ${areas.where((a) => a.id == requirement?.workAreaId).firstOrNull?.name ?? 'Yapılandırılmamış'}\nAlan hizmet gereksinimiyle belirlenir.',
                ),
                if (requirement?.singleStageConfirmed != true)
                  const Text(
                    'Bu araç türü için tek etkin aşama onayı yok. Hizmet gereksinimlerini yapılandırın.',
                  ),
              ],
            ] else
              Text(
                '${widget.appointment!.customerName} • ${widget.appointment!.vehiclePlate}\n${widget.appointment!.serviceName}\nFiyat, süre ve çalışma alanı mevcut randevudan korunur.',
              ),
            BusinessTimeField(
              clock: c.clock!,
              label: 'Başlangıç',
              wall: wall,
              offsetMinutes: offset,
              enabled: !locked,
              onChanged: (v, o) => setState(() {
                wall = v;
                offset = o;
                preview = null;
              }),
            ),
            Text(
              'Çalışan seçimi • ${widget.appointment?.employees ?? offering?.employees ?? '—'} kişi gerekli',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            if (employees.isEmpty) const Text('Etkin çalışan bulunmuyor.'),
            for (final e in employees)
              CheckboxListTile(
                contentPadding: EdgeInsets.zero,
                title: Text(e.name),
                value: selectedEmployees.contains(e.membershipId),
                onChanged: locked
                    ? null
                    : (v) => setState(() {
                        if (v == true) {
                          selectedEmployees.add(e.membershipId);
                        } else {
                          selectedEmployees.remove(e.membershipId);
                        }
                        preview = null;
                      }),
              ),
            if (selectedEmployees.any(
              (id) => !employees.any((e) => e.membershipId == id),
            ))
              const Text(
                'Önceki atamada artık etkin olmayan çalışan var. Yeni etkin çalışanlar seçin.',
              ),
            if (!rescheduling) ...[
              OutlinedButton(
                onPressed: locked ? null : _preview,
                child: const Text('Uygunluğu kontrol et'),
              ),
              if (preview != null)
                Text(
                  preview!.available
                      ? 'Şu an uygun. Bu kontrol rezervasyon oluşturmaz.'
                      : 'Seçilen alan veya çalışanlar uygun değil. Saat veya çalışanları değiştirin.',
                ),
              const Text(
                'Fiyat ve süre sunucudaki katalogdan alınır. Bekleme süresi etkin alan kapasitesini kullanmaz.',
              ),
            ],
            FilledButton(
              onPressed: c.busy || completed || stale ? null : _submit,
              child: Text(
                c.uncertainBooking
                    ? 'Aynı isteği tekrar dene'
                    : rescheduling
                    ? 'Yeniden planlamayı onayla'
                    : 'Randevuyu onayla',
              ),
            ),
            if (c.uncertainBooking)
              OutlinedButton(
                onPressed: c.busy
                    ? null
                    : () async {
                        if (await schedulingConfirm(
                              context,
                              'Yeni bir denemeye geçilsin mi?',
                              'Önce takvimden önceki kaydın oluşup oluşmadığını doğrulayın. Aynı bilgilerle tekrar deneme mevcut anahtarı korur.',
                              action: 'Takvime dön',
                            ) &&
                            context.mounted &&
                            c.active) {
                          c.abandonAttempt();
                          Navigator.pop(context);
                        }
                      },
                child: const Text('Takvimden sonucu kontrol et'),
              ),
          ],
        ],
      ),
    ),
  );
}
