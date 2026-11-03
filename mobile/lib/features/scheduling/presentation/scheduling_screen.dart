import 'dart:async';

import 'package:flutter/material.dart';

import '../../../app/theme/washflow_theme.dart';
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

  void _movePeriod(int direction) {
    c.date = c.date!.add(Duration(days: direction * (c.week ? 7 : 1)));
    unawaited(c.loadCalendar());
  }

  Future<void> _resolvePendingAttempt() async {
    if (!await schedulingConfirm(
          context,
          'İşlem kaydı temizlensin mi?',
          'Önce takvimde önceki randevunun oluşup oluşmadığını kontrol edin. Kayıt temizlendikten sonra eski işlem anahtarı geri alınamaz.',
          action: 'Kontrol ettim, kaydı temizle',
        ) ||
        !mounted ||
        !c.active) {
      return;
    }
    if (await c.abandonAttempt() && mounted && c.active) {
      await c.loadCalendar();
    }
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: c,
    builder: (context, _) {
      final dates = MaterialLocalizations.of(context);
      final theme = Theme.of(context);
      final date = c.date;
      final clock = c.clock;
      final groups = <DateTime, List<Appointment>>{};
      if (clock != null) {
        for (final appointment in c.appointments) {
          final local = clock.local(appointment.startsAtUtc);
          final day = DateTime.utc(local.year, local.month, local.day);
          (groups[day] ??= []).add(appointment);
        }
      }

      return SchedulePage(
        controller: c,
        title: null,
        actions: [
          IconButton(
            tooltip: 'Takvimi yenile',
            style: IconButton.styleFrom(minimumSize: const Size.square(48)),
            onPressed: c.busy ? null : () => c.loadCalendar(),
            icon: const Icon(Icons.refresh),
          ),
          PopupMenuButton<SchedulingSection>(
            tooltip: 'Planlama ayarları',
            icon: const Icon(Icons.settings_outlined),
            style: IconButton.styleFrom(minimumSize: const Size.square(48)),
            enabled: !c.busy,
            onSelected: (section) => _open(
              SchedulingConfigurationScreen(controller: c, section: section),
            ),
            itemBuilder: (_) => [
              const PopupMenuItem(
                value: SchedulingSection.areas,
                child: Text('Çalışma alanları'),
              ),
              const PopupMenuItem(
                value: SchedulingSection.requirements,
                child: Text('Hizmet gereksinimleri'),
              ),
              PopupMenuItem(
                value: SchedulingSection.availability,
                enabled: clock != null,
                child: const Text('Çalışan uygunluğu'),
              ),
            ],
          ),
        ],
        children: [
          Semantics(
            header: true,
            child: Text(
              'Randevu ajandası',
              style: theme.textTheme.headlineLarge,
            ),
          ),
          if (c.recoveryBlocked || c.uncertainBooking)
            Card(
              margin: EdgeInsets.zero,
              color: theme.colorScheme.secondaryContainer,
              child: Padding(
                padding: const EdgeInsets.all(WashFlowSpacing.lg),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  spacing: WashFlowSpacing.sm,
                  children: [
                    Text(
                      'Önceki kayıt isteği',
                      style: theme.textTheme.titleMedium,
                    ),
                    Text(
                      c.uncertainBooking
                          ? 'Sonucu doğrulanmamış bir kayıt isteği var. Yeni randevu ekranından aynı isteği tekrar deneyin veya takvimi kontrol edin.'
                          : 'Önceki işlem kaydı okunamadı. Devam etmeden önce takvimi kontrol edin.',
                    ),
                    OutlinedButton(
                      onPressed: c.busy ? null : _resolvePendingAttempt,
                      child: const Text(
                        'Önceki işlem kaydını kontrol ettim',
                        textAlign: TextAlign.center,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          FilledButton.icon(
            onPressed: c.busy || clock == null || c.recoveryBlocked
                ? null
                : () => _open(BookingScreen(controller: c)),
            icon: const Icon(Icons.add),
            label: const Text('Yeni randevu'),
          ),
          if (clock != null && date != null)
            Card(
              margin: EdgeInsets.zero,
              child: Padding(
                padding: const EdgeInsets.all(WashFlowSpacing.sm),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    SegmentedButton<bool>(
                      segments: const [
                        ButtonSegment(value: false, label: Text('Gün')),
                        ButtonSegment(value: true, label: Text('Hafta')),
                      ],
                      selected: {c.week},
                      onSelectionChanged: c.busy
                          ? null
                          : (value) {
                              c.week = value.single;
                              unawaited(c.loadCalendar());
                            },
                    ),
                    Row(
                      children: [
                        IconButton(
                          tooltip: 'Önceki dönem',
                          onPressed: c.busy ? null : () => _movePeriod(-1),
                          icon: const Icon(Icons.chevron_left),
                        ),
                        Expanded(
                          child: Tooltip(
                            message: 'Tarih seç',
                            child: TextButton(
                              onPressed: c.busy ? null : _date,
                              child: Text(
                                c.week
                                    ? '${dates.formatShortDate(date)} – ${dates.formatShortDate(date.add(const Duration(days: 6)))}'
                                    : dates.formatShortDate(date),
                                textAlign: TextAlign.center,
                              ),
                            ),
                          ),
                        ),
                        IconButton(
                          tooltip: 'Sonraki dönem',
                          onPressed: c.busy ? null : () => _movePeriod(1),
                          icon: const Icon(Icons.chevron_right),
                        ),
                      ],
                    ),
                    Text(
                      'İşletme saat dilimi: ${clock.zoneId}',
                      textAlign: TextAlign.center,
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
            ),
          if (c.busy && !c.loaded) const Text('Randevular yükleniyor…'),
          if (c.loaded && !c.busy && c.appointments.isEmpty)
            const Card(
              margin: EdgeInsets.zero,
              child: Padding(
                padding: EdgeInsets.all(WashFlowSpacing.xl),
                child: Text('Bu tarih aralığında randevu yok.'),
              ),
            ),
          if (clock != null)
            for (final MapEntry(key: day, value: appointments)
                in groups.entries) ...[
              if (c.week || day != date)
                Semantics(
                  header: true,
                  child: Text(
                    dates.formatFullDate(day),
                    style: theme.textTheme.titleMedium,
                  ),
                ),
              for (final appointment in appointments)
                _AgendaCard(
                  appointment: appointment,
                  clock: clock,
                  onTap: c.busy
                      ? null
                      : () => _open(
                          AppointmentDetailScreen(
                            controller: c,
                            id: appointment.id,
                          ),
                        ),
                ),
            ],
          if (c.loaded)
            Wrap(
              spacing: WashFlowSpacing.sm,
              crossAxisAlignment: WrapCrossAlignment.center,
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
      );
    },
  );
}

class _AgendaCard extends StatelessWidget {
  const _AgendaCard({
    required this.appointment,
    required this.clock,
    required this.onTap,
  });

  final Appointment appointment;
  final BusinessClock clock;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    return Card(
      margin: EdgeInsets.zero,
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(WashFlowSpacing.lg),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            spacing: WashFlowSpacing.xs,
            children: [
              Text(
                '${timeWire(clock.local(appointment.startsAtUtc))} – ${timeWire(clock.local(appointment.activeEndsAtUtc))}',
                style: theme.textTheme.titleMedium?.copyWith(
                  color: colors.primary,
                ),
              ),
              Text(appointment.customerName, style: theme.textTheme.titleLarge),
              Text(appointment.vehiclePlate),
              Text(appointment.serviceName),
              Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: WashFlowSpacing.sm,
                  vertical: WashFlowSpacing.xs,
                ),
                decoration: BoxDecoration(
                  color: appointment.cancelled
                      ? colors.errorContainer
                      : colors.primaryContainer,
                  borderRadius: WashFlowTheme.controlRadius,
                ),
                child: Text(
                  appointment.cancelled ? 'İptal edildi' : 'Onaylandı',
                  style: theme.textTheme.labelLarge?.copyWith(
                    color: appointment.cancelled
                        ? colors.onErrorContainer
                        : colors.onPrimaryContainer,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
