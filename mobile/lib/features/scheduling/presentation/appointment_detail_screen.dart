import 'dart:async';

import 'package:flutter/material.dart';

import '../../customer_vehicles/domain/customer_vehicles.dart';
import '../domain/scheduling.dart';
import 'scheduling_controller.dart';
import 'scheduling_widgets.dart';
import 'booking_screen.dart';

class AppointmentDetailScreen extends StatefulWidget {
  const AppointmentDetailScreen({
    super.key,
    required this.controller,
    required this.id,
  });
  final SchedulingController controller;
  final String id;
  @override
  State<AppointmentDetailScreen> createState() =>
      _AppointmentDetailScreenState();
}

class _AppointmentDetailScreenState extends State<AppointmentDetailScreen> {
  SchedulingController get c => widget.controller;
  Appointment? a;
  List<WorkArea> areas = [];
  List<SchedulingEmployee> employees = [];
  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final value = await c.request(
      () => c.service.detail(c.business, widget.id),
    );
    if (!mounted || !c.active) return;
    setState(() => a = value);
    if (value == null) return;
    final areaValues = await c.request(() => c.service.areas(c.business));
    if (!mounted || !c.active) return;
    final employeeValues = await c.request(
      () => c.service.employees(c.business),
    );
    if (!mounted || !c.active) return;
    setState(() {
      areas = areaValues ?? [];
      employees = employeeValues ?? [];
    });
  }

  Future<void> _cancel() async {
    final current = a!;
    if (!await schedulingConfirm(
          context,
          'Randevu iptal edilsin mi?',
          '${current.customerName} • ${current.vehiclePlate}\nKaynak rezervasyonları serbest bırakılacak.',
          action: 'Randevuyu iptal et',
        ) ||
        !mounted ||
        !c.active) {
      return;
    }
    final value = await c.request(() => c.service.cancel(c.business, current));
    if (!mounted || !c.active) return;
    final failure = c.message;
    await _load();
    if (mounted && c.active) {
      c.show(
        value != null
            ? 'Randevu iptal edildi.'
            : '${failure ?? 'İptal sonucu doğrulanamadı.'} Güncel kayıt yüklendi; otomatik tekrar yapılmadı.',
      );
    }
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: c,
    builder: (context, _) {
      final value = a;
      return SchedulePage(
        controller: c,
        title: 'Randevu ayrıntısı',
        children: [
          OutlinedButton(
            onPressed: c.busy ? null : _load,
            child: const Text('Güncel kaydı yükle'),
          ),
          if (value != null) ...[
            Text(
              value.cancelled ? 'İptal edildi' : 'Onaylandı',
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            Text(
              '${value.customerName}\n${value.vehiclePlate}',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            Text('${value.serviceName} • ${vehicleCategories[value.category]}'),
            Text(
              '${value.price.display}\n${value.activeMinutes} dk etkin • ${value.waitingMinutes == null ? 'Bekleme belirtilmedi' : '${value.waitingMinutes} dk bekleme'}',
            ),
            Text(
              '${localDisplay(c.clock!.local(value.startsAtUtc))} – ${timeWire(c.clock!.local(value.activeEndsAtUtc))}\nGüncel işletme saat dilimi: ${c.clock!.zoneId}\nKayıt saat dilimi: ${value.timeZoneId}',
            ),
            Text(
              'Çalışma alanı: ${areas.where((v) => v.id == value.workAreaId).firstOrNull?.name ?? 'Geçmiş alan (${value.workAreaId})'}',
            ),
            Text(
              'Atanan çalışanlar:\n${value.employeeIds.map((id) => employees.where((e) => e.membershipId == id).firstOrNull?.name ?? 'Geçmiş çalışan ($id)').join('\n')}',
            ),
            const Text('Kaynak: İşletme sahibi — telefon kaydı'),
            if (!value.cancelled)
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  FilledButton(
                    onPressed: c.busy
                        ? null
                        : () async {
                            await Navigator.of(context).push(
                              MaterialPageRoute<void>(
                                builder: (_) => BookingScreen(
                                  controller: c,
                                  appointment: value,
                                ),
                              ),
                            );
                            if (mounted && c.active) await _load();
                          },
                    child: const Text('Yeniden planla'),
                  ),
                  OutlinedButton(
                    onPressed: c.busy ? null : _cancel,
                    child: const Text('Randevuyu iptal et'),
                  ),
                ],
              ),
            Text(
              'Randevu geçmişi',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            for (final e in value.events)
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Text(
                    '${e.sequence}. ${switch (e.kind) {
                      'Created' => 'Oluşturuldu',
                      'Rescheduled' => 'Yeniden planlandı',
                      _ => 'İptal edildi',
                    }}\n${localDisplay(c.clock!.local(e.occurredAt))}\n${e.previousStart == null ? '' : 'Önce: ${localDisplay(c.clock!.local(e.previousStart!))} – ${timeWire(c.clock!.local(e.previousEnd!))}\n'}Randevu: ${localDisplay(c.clock!.local(e.newStart))} – ${timeWire(c.clock!.local(e.newEnd))}',
                  ),
                ),
              ),
          ],
        ],
      );
    },
  );
}
