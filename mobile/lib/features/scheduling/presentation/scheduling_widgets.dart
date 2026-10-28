import 'package:flutter/material.dart';

import '../domain/scheduling.dart';
import 'scheduling_controller.dart';

class SchedulePage extends StatelessWidget {
  const SchedulePage({
    super.key,
    required this.controller,
    required this.title,
    required this.children,
  });
  final SchedulingController controller;
  final String title;
  final List<Widget> children;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: Text(title)),
    body: SafeArea(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 720),
          child: ListView(
            padding: const EdgeInsets.all(16),
            children: [
              if (controller.busy) const LinearProgressIndicator(),
              if (controller.message case final text?)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 12),
                  child: Semantics(
                    liveRegion: true,
                    child: Text(
                      text,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ),
                ),
              ...children.map(
                (v) => Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: v,
                ),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

Future<bool> schedulingConfirm(
  BuildContext context,
  String title,
  String description, {
  String action = 'Onayla',
}) async =>
    await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(title),
        content: Text(description),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Vazgeç'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: Text(action),
          ),
        ],
      ),
    ) ??
    false;

Widget schedulingSelect<T>({
  required String label,
  required T? value,
  required Map<T, String> options,
  required ValueChanged<T?>? onChanged,
}) => DropdownButtonFormField<T>(
  key: ValueKey((label, value, options.keys.join(','))),
  initialValue: value,
  isExpanded: true,
  decoration: InputDecoration(labelText: label),
  items: options.entries
      .map(
        (v) => DropdownMenuItem<T>(
          value: v.key,
          child: Text(v.value, maxLines: 2, overflow: TextOverflow.ellipsis),
        ),
      )
      .toList(),
  onChanged: onChanged,
);

class BusinessTimeField extends StatefulWidget {
  const BusinessTimeField({
    super.key,
    required this.clock,
    required this.label,
    required this.wall,
    required this.onChanged,
    this.offsetMinutes,
    this.enabled = true,
  });
  final BusinessClock clock;
  final String label;
  final DateTime wall;
  final int? offsetMinutes;
  final bool enabled;
  final void Function(DateTime, int?) onChanged;
  @override
  State<BusinessTimeField> createState() => _BusinessTimeFieldState();
}

class _BusinessTimeFieldState extends State<BusinessTimeField> {
  Future<void> _pick() async {
    final d = await showDatePicker(
      context: context,
      initialDate: DateTime(
        widget.wall.year,
        widget.wall.month,
        widget.wall.day,
      ),
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );
    if (d == null || !mounted) return;
    final t = await showTimePicker(
      context: context,
      initialTime: TimeOfDay(
        hour: widget.wall.hour,
        minute: widget.wall.minute,
      ),
    );
    if (t == null || !mounted) return;
    final wall = DateTime.utc(d.year, d.month, d.day, t.hour, t.minute);
    final values = widget.clock.candidates(wall);
    widget.onChanged(
      wall,
      values.length == 1
          ? widget.clock.local(values.single).timeZoneOffset.inMinutes
          : null,
    );
  }

  @override
  Widget build(BuildContext context) {
    final values = widget.clock.candidates(widget.wall);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        OutlinedButton.icon(
          onPressed: widget.enabled ? _pick : null,
          icon: const Icon(Icons.event),
          label: Text(
            '${widget.label}: ${localDisplay(widget.wall)}\n${widget.clock.zoneId}',
          ),
        ),
        if (values.isEmpty)
          const Text(
            'Bu yerel saat saat dilimi geçişinde mevcut değil. Başka saat seçin.',
          ),
        if (values.length > 1)
          schedulingSelect<int>(
            label: 'Belirsiz saat — UTC farkı',
            value: widget.offsetMinutes,
            options: {
              for (final v in values)
                widget.clock.local(v).timeZoneOffset.inMinutes:
                    'UTC ${widget.clock.local(v).timeZoneOffset.inMinutes >= 0 ? '+' : ''}${widget.clock.local(v).timeZoneOffset.inMinutes} dakika',
            },
            onChanged: widget.enabled
                ? (v) => widget.onChanged(widget.wall, v)
                : null,
          ),
      ],
    );
  }
}
