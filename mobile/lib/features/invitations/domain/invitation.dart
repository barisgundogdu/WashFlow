/// Owner-visible metadata only. Credentials and delivery state are not in this DTO.
class EmployeeInvitation {
  const EmployeeInvitation._({
    required this.id,
    required this.email,
    required this.expiresAt,
    required this.acceptedAt,
    required this.revokedAt,
  });
  final String id;
  final String email;
  final DateTime expiresAt;
  final DateTime? acceptedAt;
  final DateTime? revokedAt;

  bool get canResend => acceptedAt == null && revokedAt == null;
  bool get canRevoke => acceptedAt == null && revokedAt == null;
  String statusLabel(DateTime now) {
    if (acceptedAt != null) return 'Kabul edildi';
    if (revokedAt != null) return 'İptal edildi';
    if (!expiresAt.isAfter(now)) return 'Süresi doldu';
    return 'Kabul bekliyor';
  }

  factory EmployeeInvitation.fromJson(Map<String, dynamic> json) {
    final id = invitationId(json['id']);
    final email = json['email'];
    if (email is! String ||
        email.trim().isEmpty ||
        email.length > 320 ||
        !json.containsKey('acceptedAt') ||
        !json.containsKey('revokedAt')) {
      throw const FormatException('Invalid invitation metadata.');
    }
    DateTime? optional(Object? value) => value == null ? null : _date(value);
    final accepted = optional(json['acceptedAt']);
    final revoked = optional(json['revokedAt']);
    final expires = _date(json['expiresAt']);
    if ((accepted != null && revoked != null) ||
        (accepted != null && !accepted.isBefore(expires))) {
      throw const FormatException('Invalid invitation state.');
    }
    return EmployeeInvitation._(
      id: id,
      email: email,
      expiresAt: expires,
      acceptedAt: accepted,
      revokedAt: revoked,
    );
  }
}

String invitationId(Object? value) {
  if (value is! String ||
      !RegExp(
        r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$',
      ).hasMatch(value) ||
      value == '00000000-0000-0000-0000-000000000000') {
    throw const FormatException('Invalid invitation scope identifier.');
  }
  return value.toLowerCase();
}

DateTime _date(Object? value) {
  if (value is! String || !RegExp(r'(Z|[+-]\d{2}:\d{2})$').hasMatch(value)) {
    throw const FormatException('Invalid invitation timestamp.');
  }
  return DateTime.parse(value).toUtc();
}
