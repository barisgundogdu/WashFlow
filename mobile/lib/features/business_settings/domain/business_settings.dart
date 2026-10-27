import 'dart:convert';

import '../../auth/domain/auth_service.dart';
import '../../invitations/domain/invitation.dart';

const settingsLimits = <String, int>{
  'name': 200,
  'phone': 40,
  'contactEmail': 320,
  'addressLine1': 250,
  'addressLine2': 250,
  'district': 100,
  'city': 100,
  'region': 100,
  'postalCode': 32,
  'countryCode': 2,
  'description': 2000,
  'timeZoneId': 100,
};

class OpeningDay {
  const OpeningDay(
    this.day,
    this.closed,
    this.opensAtMinute,
    this.closesAtMinute,
  );
  final int day;
  final bool closed;
  final int? opensAtMinute, closesAtMinute;
  bool get valid =>
      day >= 1 &&
      day <= 7 &&
      (closed
          ? opensAtMinute == null && closesAtMinute == null
          : opensAtMinute != null &&
                closesAtMinute != null &&
                opensAtMinute! >= 0 &&
                opensAtMinute! < closesAtMinute! &&
                closesAtMinute! <= 1440);
  Map<String, Object?> toJson() => {
    'day': day,
    'closed': closed,
    'opensAtMinute': opensAtMinute,
    'closesAtMinute': closesAtMinute,
  };
  factory OpeningDay.fromJson(Map<String, dynamic> j) {
    if (j['day'] is! int ||
        j['closed'] is! bool ||
        (j['opensAtMinute'] != null && j['opensAtMinute'] is! int) ||
        (j['closesAtMinute'] != null && j['closesAtMinute'] is! int)) {
      throw const FormatException();
    }
    final d = OpeningDay(
      j['day'],
      j['closed'],
      j['opensAtMinute'],
      j['closesAtMinute'],
    );
    if (!d.valid) {
      throw const FormatException();
    }
    return d;
  }
}

class BusinessSettings {
  BusinessSettings({
    required this.businessId,
    required this.version,
    required Map<String, String?> fields,
    List<OpeningDay>? weeklyHours,
  }) : fields = Map.unmodifiable(fields),
       weeklyHours = weeklyHours == null
           ? null
           : List.unmodifiable(weeklyHours);
  final String businessId;
  final int version;
  final Map<String, String?> fields;
  final List<OpeningDay>? weeklyHours;
  Map<String, Object?> toJson() => {
    ...fields,
    'version': version,
    'weeklyHours': weeklyHours?.map((d) => d.toJson()).toList(),
  };
  BusinessSettings change({
    Map<String, String?>? fields,
    List<OpeningDay>? hours,
    bool clearHours = false,
  }) => BusinessSettings(
    businessId: businessId,
    version: version,
    fields: fields ?? this.fields,
    weeklyHours: clearHours ? null : hours ?? weeklyHours,
  );
  String get fingerprint => jsonEncode(toJson());
  String? validate() {
    if ((fields['name'] ?? '').trim().isEmpty) {
      return 'İşletme adı gereklidir.';
    }
    for (final e in settingsLimits.entries) {
      if ((fields[e.key]?.length ?? 0) > e.value) {
        return 'Alan uzunluk sınırını aşıyor.';
      }
    }
    final phone = fields['phone'] ?? '';
    if (phone.isNotEmpty &&
        (!RegExp(r'[0-9]').hasMatch(phone) ||
            !RegExp(r'^[0-9+ ()./-]+$').hasMatch(phone))) {
      return 'Geçerli bir telefon numarası girin.';
    }
    final email = fields['contactEmail'] ?? '';
    if (email.isNotEmpty && !RegExp(r'^[^\s@]+@[^\s@]+$').hasMatch(email)) {
      return 'Geçerli bir iletişim e-postası girin.';
    }
    final country = fields['countryCode'] ?? '';
    if (country.isNotEmpty && !RegExp(r'^[A-Za-z]{2}$').hasMatch(country)) {
      return 'İki harfli ülke kodu girin.';
    }
    if (weeklyHours != null &&
        ((fields['timeZoneId'] ?? '').isEmpty ||
            weeklyHours!.length != 7 ||
            weeklyHours!.map((d) => d.day).toSet().length != 7 ||
            weeklyHours!.any((d) => !d.valid))) {
      return 'Saat dilimini ve yedi günün geçerli saatlerini belirtin.';
    }
    return null;
  }

  factory BusinessSettings.fromJson(Map<String, dynamic> j) {
    final id = invitationId(j['businessId']);
    if (j['version'] is! int ||
        j['version'] <= 0 ||
        j['version'] > 4294967295 ||
        j['name'] is! String ||
        (j['name'] as String).trim().isEmpty ||
        !j.containsKey('weeklyHours')) {
      throw const FormatException();
    }
    final fields = <String, String?>{};
    for (final e in settingsLimits.entries) {
      final v = j[e.key];
      if ((v != null && v is! String) || (v is String && v.length > e.value)) {
        throw const FormatException();
      }
      fields[e.key] = v as String?;
    }
    final raw = j['weeklyHours'];
    if (raw != null && raw is! List) {
      throw const FormatException();
    }
    final days = raw == null
        ? null
        : (raw as List)
              .map((d) => OpeningDay.fromJson(d as Map<String, dynamic>))
              .toList();
    final s = BusinessSettings(
      businessId: id,
      version: j['version'],
      fields: fields,
      weeklyHours: days,
    );
    if (s.validate() != null) {
      throw const FormatException();
    }
    return s;
  }
}

abstract interface class BusinessSettingsService {
  Future<AuthResult<BusinessSettings>> read(String businessId);
  Future<AuthResult<BusinessSettings>> save(BusinessSettings settings);
}

String settingsError(AuthError error) => switch (error.code) {
  AuthErrorCode.conflict => 'Başka bir oturum kaydı değiştirdi. Güncel kaydı yükleyin; değişiklikleriniz otomatik uygulanmadı.',
  AuthErrorCode.validation =>
    'Alanları, IANA saat dilimini ve çalışma saatlerini kontrol edin.',
  AuthErrorCode.forbidden => 'Bu işletmenin ayarlarını yönetme yetkiniz yok.',
  AuthErrorCode.unauthenticated || AuthErrorCode.invalidCredentials =>
    'Oturumunuz geçersiz. Yeniden giriş yapın.',
  AuthErrorCode.network || AuthErrorCode.timeout =>
    'Sunucuya ulaşılamadı veya istek zaman aşımına uğradı.',
  AuthErrorCode.rateLimited =>
    'Çok fazla istek gönderildi. Bir süre sonra tekrar deneyin.',
  _ => 'İşlem tamamlanamadı. Daha sonra yeniden deneyin.',
};
