import 'package:washflow/features/business_settings/domain/business_settings.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';

import 'session_fakes.dart';

BusinessSettings settings({
  String id = businessA,
  int version = 1,
  String name = 'İşletme A',
  List<OpeningDay>? hours,
}) => BusinessSettings(
  businessId: id,
  version: version,
  fields: {
    for (final k in settingsLimits.keys) k: null,
    'name': name,
    'timeZoneId': hours == null ? null : 'Europe/London',
  },
  weeklyHours: hours,
);

class FakeSettings implements BusinessSettingsService {
  BusinessSettings value = settings();
  final calls = <String>[];
  Future<AuthResult<BusinessSettings>> Function(String)? onRead;
  Future<AuthResult<BusinessSettings>> Function(BusinessSettings)? onSave;
  @override
  Future<AuthResult<BusinessSettings>> read(String id) async {
    calls.add('read:$id');
    return onRead == null ? AuthSuccess(value) : onRead!(id);
  }

  @override
  Future<AuthResult<BusinessSettings>> save(BusinessSettings input) async {
    calls.add('save:${input.businessId}');
    if (onSave != null) return onSave!(input);
    value = BusinessSettings(
      businessId: input.businessId,
      version: input.version + 1,
      fields: input.fields,
      weeklyHours: input.weeklyHours,
    );
    return AuthSuccess(value);
  }
}
