import 'package:unorm_dart/unorm_dart.dart' as unorm;

import '../../auth/domain/auth_service.dart';
import '../../customer_vehicles/domain/customer_vehicles.dart';
import '../../invitations/domain/invitation.dart';

/// Exact kuruş; never parses or computes currency with binary floating point.
class Lira {
  const Lira(this.kurus);
  final int kurus;
  static Lira parse(String text, {bool turkish = false}) {
    final value = turkish ? text.trim().replaceAll(',', '.') : text;
    if (!RegExp(r'^(0|[1-9][0-9]{0,6})(\.[0-9]{1,2})?$').hasMatch(value)) {
      throw const FormatException();
    }
    final parts = value.split('.');
    return Lira(
      int.parse(parts[0]) * 100 +
          int.parse(parts.length == 1 ? '00' : parts[1].padRight(2, '0')),
    );
  }

  String get wire =>
      '${kurus ~/ 100}.${(kurus % 100).toString().padLeft(2, '0')}';
  String get display {
    final whole = (kurus ~/ 100).toString().replaceAllMapped(
      RegExp(r'\B(?=(\d{3})+(?!\d))'),
      (_) => '.',
    );
    return '$whole,${(kurus % 100).toString().padLeft(2, '0')} TL';
  }
}

class Offering {
  const Offering({
    required this.category,
    required this.price,
    required this.activeMinutes,
    this.waitingMinutes,
    required this.employees,
  });
  final String category;
  final Lira price;
  final int activeMinutes, employees;
  final int? waitingMinutes;
  factory Offering.fromJson(Map<String, dynamic> j) {
    if (j['currency'] != 'TRY' ||
        j['price'] is! String ||
        j['category'] is! String ||
        j['activeMinutes'] is! int ||
        j['employees'] is! int ||
        (j['waitingMinutes'] != null && j['waitingMinutes'] is! int)) {
      throw const FormatException();
    }
    final value = Offering(
      category: j['category'],
      price: Lira.parse(j['price']),
      activeMinutes: j['activeMinutes'],
      waitingMinutes: j['waitingMinutes'],
      employees: j['employees'],
    );
    if (!value.valid) throw const FormatException();
    return value;
  }
  bool get valid =>
      vehicleCategories.containsKey(category) &&
      price.kurus >= 0 &&
      price.kurus <= 999999999 &&
      activeMinutes >= 1 &&
      activeMinutes <= 10080 &&
      (waitingMinutes == null ||
          waitingMinutes! >= 0 && waitingMinutes! <= 43200) &&
      employees >= 1 &&
      employees <= 100;
  Map<String, dynamic> toJson() => {
    'category': category,
    'price': price.wire,
    'currency': 'TRY',
    'activeMinutes': activeMinutes,
    'waitingMinutes': waitingMinutes,
    'employees': employees,
  };
  String get estimate =>
      '$activeMinutes dk etkin • ${waitingMinutes == null ? 'bekleme belirtilmedi' : '$waitingMinutes dk bekleme'} • $employees çalışan';
}

/// Matches backend NFC, .NET whitespace trimming and its existing BMP-only text policy.
abstract final class CatalogText {
  static final _edgeWhitespace = RegExp(
    r'^[\u0009-\u000d\u0020\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+|[\u0009-\u000d\u0020\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+$',
  );
  static String normalize(String value) =>
      unorm.nfc(value.replaceAll(_edgeWhitespace, ''));
  static bool _unsupported(String value, {bool multiline = false}) =>
      value.codeUnits.any(
        (c) =>
            c >= 0xd800 && c <= 0xdfff ||
            (c < 0x20 || c >= 0x7f && c <= 0x9f) &&
                !(multiline && (c == 9 || c == 10 || c == 13)),
      );
  static String? nameError(String? value) {
    final text = value ?? '';
    if (_unsupported(text)) {
      return 'Hizmet adında emoji veya kontrol karakteri kullanmayın.';
    }
    final normalized = normalize(text);
    if (normalized.isEmpty) return 'Hizmet adı girin.';
    if (normalized.length > 200) {
      return 'Hizmet adı normalleştirme sonrasında en fazla 200 karakter olabilir.';
    }
    return null;
  }

  static String? descriptionError(String? value) {
    final text = value ?? '';
    if (_unsupported(text, multiline: true)) {
      return 'Açıklamada emoji veya desteklenmeyen kontrol karakteri kullanmayın.';
    }
    if (normalize(text).length > 2000) {
      return 'Açıklama normalleştirme sonrasında en fazla 2000 karakter olabilir.';
    }
    return null;
  }
}

class CatalogDraft {
  CatalogDraft({
    this.id,
    this.version,
    this.name = '',
    this.description = '',
    this.enabled = false,
    this.published = false,
    this.offerings = const [],
  });
  final String? id;
  final int? version;
  String name, description;
  bool enabled, published;
  List<Offering> offerings;
  String? validate() {
    final textError =
        CatalogText.nameError(name) ??
        CatalogText.descriptionError(description);
    if (textError != null) return textError;
    if (published && !enabled) return 'Yayınlamak için hizmet etkin olmalıdır.';
    if (offerings.length > 6 ||
        enabled && offerings.isEmpty ||
        offerings.any((o) => !o.valid) ||
        offerings.map((o) => o.category).toSet().length != offerings.length) {
      return 'Araç türlerini, fiyatları, süreleri ve çalışan sayılarını kontrol edin.';
    }
    if (id != null &&
        (version == null || version! < 1 || version! > 4294967295)) {
      return 'Güncel kayıt sürümü gereklidir.';
    }
    return null;
  }

  Map<String, dynamic> toJson() => {
    'name': CatalogText.normalize(name),
    'description': CatalogText.normalize(description),
    'enabled': enabled,
    'published': published,
    'offerings': offerings.map((o) => o.toJson()).toList(),
    if (version != null) 'version': version,
  };
}

class CatalogServiceRecord {
  CatalogServiceRecord({
    required this.id,
    required this.businessId,
    required this.version,
    required this.name,
    required this.description,
    required this.enabled,
    required this.published,
    required this.offerings,
  });
  final String id, businessId, name, description;
  final int version;
  final bool enabled, published;
  final List<Offering> offerings;
  factory CatalogServiceRecord.fromJson(Map<String, dynamic> j) {
    if (j['version'] is! int ||
        j['version'] < 1 ||
        j['version'] > 4294967295 ||
        j['name'] is! String ||
        j['description'] != null && j['description'] is! String ||
        j['enabled'] is! bool ||
        j['published'] is! bool ||
        j['offerings'] is! List) {
      throw const FormatException();
    }
    final v = CatalogServiceRecord(
      id: invitationId(j['id']),
      businessId: invitationId(j['businessId']),
      version: j['version'],
      name: j['name'],
      description: j['description'] ?? '',
      enabled: j['enabled'],
      published: j['published'],
      offerings: List.unmodifiable(
        (j['offerings'] as List).map(
          (o) => Offering.fromJson(o as Map<String, dynamic>),
        ),
      ),
    );
    if (v.draft().validate() != null) throw const FormatException();
    return v;
  }
  CatalogDraft draft({bool duplicate = false}) => CatalogDraft(
    id: duplicate ? null : id,
    version: duplicate ? null : version,
    name: duplicate ? '$name (kopya)' : name,
    description: description,
    enabled: duplicate ? false : enabled,
    published: duplicate ? false : published,
    offerings: List.of(offerings),
  );
}

abstract interface class CatalogService {
  Future<AuthResult<RecordPage<CatalogServiceRecord>>> list(
    String business, {
    String query = '',
    String status = 'all',
    int page = 1,
  });
  Future<AuthResult<CatalogServiceRecord>> read(String business, String id);
  Future<AuthResult<CatalogServiceRecord>> save(
    String business,
    CatalogDraft draft,
  );
  Future<AuthResult<void>> archive(
    String business,
    CatalogServiceRecord record,
  );
}
