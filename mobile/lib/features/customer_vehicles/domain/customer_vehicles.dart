import '../../auth/domain/auth_service.dart';
import '../../invitations/domain/invitation.dart';

const vehicleCategories = {
  'Car': 'Otomobil',
  'Suv': 'SUV',
  'Van': 'Minibüs / panelvan',
  'Pickup': 'Kamyonet',
  'Motorcycle': 'Motosiklet',
  'Other': 'Diğer',
};
String _text(
  Map<String, dynamic> j,
  String key,
  int max, {
  bool required = false,
}) {
  final v = j[key];
  if ((required && (v is! String || v.trim().isEmpty)) ||
      (v != null && v is! String) ||
      (v is String && v.length > max)) {
    throw const FormatException();
  }
  return v as String? ?? '';
}

int _version(Map<String, dynamic> j) {
  final v = j['version'];
  if (v is! int || v < 1 || v > 4294967295) throw const FormatException();
  return v;
}

DateTime _date(Map<String, dynamic> j, String key) {
  if (j[key] is! String) throw const FormatException();
  return DateTime.parse(j[key]);
}

class LocalCustomerRecord {
  LocalCustomerRecord({
    required this.id,
    required this.businessId,
    required this.fullName,
    required this.phone,
    required this.email,
    required this.notes,
    required this.version,
    required this.createdAt,
    required this.updatedAt,
  });
  final String id, businessId, fullName, phone, email, notes;
  final int version;
  final DateTime createdAt, updatedAt;
  factory LocalCustomerRecord.fromJson(Map<String, dynamic> j) =>
      LocalCustomerRecord(
        id: invitationId(j['id']),
        businessId: invitationId(j['businessId']),
        fullName: _text(j, 'fullName', 200, required: true),
        phone: _text(j, 'phone', 40),
        email: _text(j, 'email', 320),
        notes: _text(j, 'notes', 2000),
        version: _version(j),
        createdAt: _date(j, 'createdAt'),
        updatedAt: _date(j, 'updatedAt'),
      );
  Map<String, String> get fields => {
    'fullName': fullName,
    'phone': phone,
    'email': email,
    'notes': notes,
  };
}

class VehicleRecord {
  VehicleRecord({
    required this.id,
    required this.businessId,
    required this.localCustomerId,
    required this.customerName,
    required this.plate,
    required this.brand,
    required this.model,
    required this.category,
    required this.modelYear,
    required this.color,
    required this.notes,
    required this.version,
    required this.createdAt,
    required this.updatedAt,
  });
  final String id, customerName, plate, brand, model, category, color, notes;
  final String? businessId, localCustomerId;
  final int? modelYear;
  final int version;
  final DateTime createdAt, updatedAt;
  factory VehicleRecord.fromJson(Map<String, dynamic> j) {
    final b = j['businessId'], c = j['localCustomerId'], year = j['modelYear'];
    if ((b == null) != (c == null) ||
        (year != null && (year is! int || year < 1886 || year > 2100)) ||
        !vehicleCategories.containsKey(j['category'])) {
      throw const FormatException();
    }
    return VehicleRecord(
      id: invitationId(j['id']),
      businessId: b == null ? null : invitationId(b),
      localCustomerId: c == null ? null : invitationId(c),
      customerName: _text(j, 'customerName', 200),
      plate: _text(j, 'plate', 32, required: true),
      brand: _text(j, 'brand', 80, required: true),
      model: _text(j, 'model', 100, required: true),
      category: j['category'],
      modelYear: year,
      color: _text(j, 'color', 60),
      notes: _text(j, 'notes', 2000),
      version: _version(j),
      createdAt: _date(j, 'createdAt'),
      updatedAt: _date(j, 'updatedAt'),
    );
  }
  Map<String, String> get fields => {
    'plate': plate,
    'brand': brand,
    'model': model,
    'category': category,
    'modelYear': modelYear?.toString() ?? '',
    'color': color,
    'notes': notes,
  };
}

class RecordPage<T> {
  RecordPage(this.items, this.page, this.hasMore);
  final List<T> items;
  final int page;
  final bool hasMore;
}

class CustomerDraft {
  CustomerDraft(this.fields, {this.id, this.version});
  final Map<String, String> fields;
  final String? id;
  final int? version;
  Map<String, dynamic> toJson() => {
    for (final k in ['fullName', 'phone', 'email', 'notes'])
      k: fields[k]?.trim(),
    if (version != null) 'version': version,
  };
  String? validate() => validateCustomer(fields);
}

class VehicleDraft {
  VehicleDraft(this.fields, {this.id, this.version});
  final Map<String, String> fields;
  final String? id;
  final int? version;
  Map<String, dynamic> toJson() => {
    for (final k in ['plate', 'brand', 'model', 'category', 'color', 'notes'])
      k: fields[k]?.trim(),
    'modelYear': (fields['modelYear'] ?? '').trim().isEmpty
        ? null
        : int.tryParse(fields['modelYear']!.trim()),
    if (version != null) 'version': version,
  };
  String? validate() => validateVehicle(fields);
}

String? validateCustomer(Map<String, String> f) {
  if ((f['fullName'] ?? '').trim().isEmpty ||
      (f['fullName']?.length ?? 0) > 200) {
    return 'Ad soyad gereklidir (en fazla 200 karakter).';
  }
  final phone = f['phone'] ?? '';
  if (phone.isNotEmpty &&
      (!RegExp(r'[0-9]').hasMatch(phone) ||
          !RegExp(r'^[0-9+ ()./-]+$').hasMatch(phone) ||
          phone.length > 40)) {
    return 'Geçerli telefon numarası girin.';
  }
  final email = f['email'] ?? '';
  if (email.isNotEmpty &&
      (!RegExp(r'^[^\s@]+@[^\s@]+$').hasMatch(email) || email.length > 320)) {
    return 'Geçerli e-posta adresi girin.';
  }
  if ((f['notes']?.length ?? 0) > 2000) {
    return 'Notlar en fazla 2000 karakter olabilir.';
  }
  return null;
}

String? validateVehicle(Map<String, String> f) {
  for (final e in {'plate': 32, 'brand': 80, 'model': 100}.entries) {
    if ((f[e.key] ?? '').trim().isEmpty || (f[e.key]?.length ?? 0) > e.value) {
      return 'Plaka, marka ve model gereklidir; alan sınırlarını kontrol edin.';
    }
  }
  if (!vehicleCategories.containsKey(f['category'])) return 'Araç türü seçin.';
  final y = (f['modelYear'] ?? '').trim();
  final n = int.tryParse(y);
  if (y.isNotEmpty && (n == null || n < 1886 || n > 2100)) {
    return 'Model yılı 1886–2100 arasında olmalıdır.';
  }
  if ((f['color']?.length ?? 0) > 60 || (f['notes']?.length ?? 0) > 2000) {
    return 'Renk veya not uzunluk sınırını aşıyor.';
  }
  return null;
}

abstract interface class CustomerVehicleService {
  Future<AuthResult<RecordPage<LocalCustomerRecord>>> customers(
    String business, {
    String query = '',
    int page = 1,
  });
  Future<AuthResult<LocalCustomerRecord>> customer(String business, String id);
  Future<AuthResult<LocalCustomerRecord>> saveCustomer(
    String business,
    CustomerDraft draft,
  );
  Future<AuthResult<RecordPage<VehicleRecord>>> vehicles({
    String? business,
    String? customer,
    String query = '',
    int page = 1,
  });
  Future<AuthResult<VehicleRecord>> saveVehicle(
    VehicleDraft draft, {
    String? business,
    String? customer,
  });
  Future<AuthResult<void>> archiveVehicle(VehicleRecord vehicle);
}

String managementError(AuthError e) => switch (e.code) {
  AuthErrorCode.validation =>
    'Alanları kontrol edin. Geçersiz veya fazla uzun bilgi gönderildi.',
  AuthErrorCode.forbidden => 'Bu kayıtlara erişim yetkiniz yok.',
  AuthErrorCode.unauthenticated || AuthErrorCode.invalidCredentials =>
    'Oturumunuz geçersiz. Yeniden giriş yapın.',
  AuthErrorCode.notFound => 'Kayıt bulunamadı veya artık erişilemiyor.',
  AuthErrorCode.conflict => 'Kayıt başka bir oturumda değişti. Güncel listeyi yükleyip kaydı yeniden açın.',
  AuthErrorCode.rateLimited =>
    'Çok fazla istek gönderildi. Bir süre sonra yeniden deneyin.',
  _ => 'Sunucuya ulaşılamadı veya işlem tamamlanamadı. Yeniden deneyin.',
};
bool uncertainWrite(AuthError e) => {
  AuthErrorCode.network,
  AuthErrorCode.timeout,
  AuthErrorCode.unexpected,
  AuthErrorCode.unavailable,
}.contains(e.code);
