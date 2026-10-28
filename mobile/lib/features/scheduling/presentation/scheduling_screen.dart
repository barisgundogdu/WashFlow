import 'dart:async';

import 'package:flutter/material.dart';

import '../../auth/domain/auth_service.dart';
import '../../business_settings/domain/business_settings.dart';
import '../../customer_vehicles/domain/customer_vehicles.dart';
import '../../service_catalog/domain/service_catalog.dart';
import '../domain/scheduling.dart';
import '../data/secure_booking_attempt_store.dart';
import 'scheduling_controller.dart';
import 'scheduling_widgets.dart';
import 'booking_screen.dart';
import 'scheduling_configuration_screen.dart';
import 'appointment_detail_screen.dart';

class SchedulingScreen extends StatefulWidget {
  const SchedulingScreen({
    super.key,
    required this.business,
    this.ownerMembershipId,
    required this.service,
    required this.settings,
    required this.catalog,
    required this.customers,
    required this.isCurrent,
    required this.onAuthorizationFailure,
  });
  final String business;
  final String? ownerMembershipId;
  final SchedulingService service;
  final BusinessSettingsService settings;
  final CatalogService catalog;
  final CustomerVehicleService customers;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  @override
  State<SchedulingScreen> createState() => _SchedulingScreenState();
}

class _SchedulingScreenState extends State<SchedulingScreen> {
  late final SchedulingController c;
  @override
  void initState() {
    super.initState();
    c = SchedulingController(
      service: widget.service,
      attemptStore: widget.ownerMembershipId == null
          ? null
          : SecureBookingAttemptStore(
              widget.business,
              widget.ownerMembershipId!,
            ),
      settings: widget.settings,
      catalog: widget.catalog,
      customers: widget.customers,
      business: widget.business,
      isCurrent: widget.isCurrent,
      onAuthorizationFailure: widget.onAuthorizationFailure,
    );
    unawaited(c.loadCalendar());
  }

  @override
  void dispose() {
    c.dispose();
    super.dispose();
  }

  Future<void> _open(Widget page) async {
    await Navigator.of(context)
        .push(MaterialPageRoute<void>(builder: (_) => page));
    if (mounted && c.active) await c.loadCalendar();
  }

  Future<void> _date() async {
    final date = c.date!;
    final picked = await showDatePicker(
      context: context,
      initialDate: DateTime(date.year, date.month, date.day),
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );
    if (!mounted || !c.active || picked == null) return;
    c.date = DateTime.utc(picked.year, picked.month, picked.day);
    await c.loadCalendar();
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: c,
    builder: (context, _) => SchedulePage(
      controller: c,
      title: 'Planlama',
      children: [
        if (c.recoveryBlocked || c.uncertainBooking)
          OutlinedButton(
            onPressed: c.busy
                ? null
                : () async {
                    if (!await schedulingConfirm(
                          context,
                          'İşlem kaydı temizlensin mi?',
                          'Önce takvimde önceki randevunun oluşup oluşmadığını kontrol edin. Kayıt temizlendikten sonra eski işlem anahtarı geri alınamaz.',
                          action: 'Kontrol ettim, kaydı temizle',
                        ) ||
                        !context.mounted ||
                        !c.active) {
                      return;
                    }
                    if (await c.abandonAttempt() && mounted && c.active) {
                      await c.loadCalendar();
                    }
                  },
            child: const Text('Önceki işlem kaydını kontrol ettim'),
          ),
        if (c.uncertainBooking)
          const Text(
            'Sonucu doğrulanmamış bir kayıt isteği var. Yeni randevu ekranından aynı isteği tekrar deneyin veya takvimi kontrol edin.',
          ),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            FilledButton.icon(
              onPressed: c.busy || c.clock == null || c.recoveryBlocked
                  ? null
                  : () => _open(BookingScreen(controller: c)),
              icon: const Icon(Icons.add),
              label: const Text('Yeni randevu'),
            ),
            OutlinedButton(
              onPressed: c.busy
                  ? null
                  : () => _open(
                      SchedulingConfigurationScreen(
                        controller: c,
                        section: SchedulingSection.areas,
                      ),
                    ),
              child: const Text('Çalışma alanları'),
            ),
            OutlinedButton(
              onPressed: c.busy
                  ? null
                  : () => _open(
                      SchedulingConfigurationScreen(
                        controller: c,
                        section: SchedulingSection.requirements,
                      ),
                    ),
              child: const Text('Hizmet gereksinimleri'),
            ),
            OutlinedButton(
              onPressed: c.busy || c.clock == null
                  ? null
                  : () => _open(
                      SchedulingConfigurationScreen(
                        controller: c,
                        section: SchedulingSection.availability,
                      ),
                    ),
              child: const Text('Çalışan uygunluğu'),
            ),
          ],
        ),
        if (c.clock != null) ...[
          Text(
            'Randevu takvimi • ${c.clock!.zoneId}',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          SegmentedButton<bool>(
            segments: const [
              ButtonSegment(value: false, label: Text('Gün')),
              ButtonSegment(value: true, label: Text('Hafta')),
            ],
            selected: {c.week},
            onSelectionChanged: c.busy
                ? null
                : (v) {
                    c.week = v.single;
                    unawaited(c.loadCalendar());
                  },
          ),
          Row(
            children: [
              IconButton(
                tooltip: 'Önceki dönem',
                onPressed: c.busy
                    ? null
                    : () {
                        c.date = c.date!.subtract(
                          Duration(days: c.week ? 7 : 1),
                        );
                        unawaited(c.loadCalendar());
                      },
                icon: const Icon(Icons.chevron_left),
              ),
              Expanded(
                child: TextButton(
                  onPressed: c.busy ? null : _date,
                  child: Text(
                    '${dateWire(c.date!)}${c.week ? ' — ${dateWire(c.date!.add(const Duration(days: 6)))}' : ''}',
                  ),
                ),
              ),
              IconButton(
                tooltip: 'Sonraki dönem',
                onPressed: c.busy
                    ? null
                    : () {
                        c.date = c.date!.add(Duration(days: c.week ? 7 : 1));
                        unawaited(c.loadCalendar());
                      },
                icon: const Icon(Icons.chevron_right),
              ),
            ],
          ),
        ],
        OutlinedButton.icon(
          onPressed: c.busy ? null : () => c.loadCalendar(),
          icon: const Icon(Icons.refresh),
          label: const Text('Takvimi yenile'),
        ),
        if (c.loaded && c.appointments.isEmpty)
          const Card(
            child: Padding(
              padding: EdgeInsets.all(24),
              child: Text('Bu tarih aralığında randevu yok.'),
            ),
          ),
        for (final a in c.appointments)
          Card(
            child: ListTile(
              contentPadding: const EdgeInsets.all(16),
              leading: Icon(
                a.cancelled ? Icons.event_busy : Icons.event_available,
                color: a.cancelled
                    ? Theme.of(context).colorScheme.error
                    : Theme.of(context).colorScheme.primary,
              ),
              title: Text(
                '${localDisplay(c.clock!.local(a.startsAtUtc))} – ${timeWire(c.clock!.local(a.activeEndsAtUtc))}',
              ),
              subtitle: Text(
                '${a.customerName} • ${a.vehiclePlate}\n${a.serviceName}\n${a.cancelled ? 'İptal edildi' : 'Onaylandı'}',
              ),
              isThreeLine: true,
              onTap: c.busy
                  ? null
                  : () =>
                        _open(AppointmentDetailScreen(controller: c, id: a.id)),
            ),
          ),
        if (c.loaded)
          Wrap(
            spacing: 8,
            children: [
              Text('Sayfa ${c.page}'),
              TextButton(
                onPressed: c.busy || c.page == 1
                    ? null
                    : () => c.loadCalendar(nextPage: c.page - 1),
                child: const Text('Önceki sayfa'),
              ),
              TextButton(
                onPressed:
                    c.busy || c.page >= 10000 || c.appointments.length < 50
                    ? null
                    : () => c.loadCalendar(nextPage: c.page + 1),
                child: const Text('Sonraki sayfa'),
              ),
            ],
          ),
      ],
    ),
  );
}
