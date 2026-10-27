import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/customer_vehicles/domain/customer_vehicles.dart';

import 'session_fakes.dart';

const customerId = '66666666-6666-6666-6666-666666666666',
    vehicleId = '77777777-7777-7777-7777-777777777777';
const stamp = '2026-10-27T10:00:00Z';
Map<String, dynamic> customerJson({
  String business = businessA,
  String id = customerId,
  String name = 'Ayşe Yılmaz',
  int version = 1,
}) => {
  'id': id,
  'businessId': business,
  'fullName': name,
  'phone': '0555 123 45 67',
  'email': null,
  'notes': null,
  'version': version,
  'createdAt': stamp,
  'updatedAt': stamp,
};
Map<String, dynamic> vehicleJson({
  String? business = businessA,
  String? customer = customerId,
  String id = vehicleId,
  int version = 1,
  String plate = '34 ABC 123',
}) => {
  'id': id,
  'businessId': business,
  'localCustomerId': customer,
  'customerName': business == null ? null : 'Ayşe Yılmaz',
  'plate': plate,
  'brand': 'Test brand',
  'model': 'Test model',
  'category': 'Car',
  'modelYear': 2020,
  'color': null,
  'notes': null,
  'version': version,
  'createdAt': stamp,
  'updatedAt': stamp,
};

class FakeCustomerVehicles implements CustomerVehicleService {
  List<LocalCustomerRecord> people = [];
  List<VehicleRecord> cars = [];
  final calls = <String>[];
  Future<AuthResult<RecordPage<LocalCustomerRecord>>> Function(
    String,
    String,
    int,
  )?
  onCustomers;
  Future<AuthResult<LocalCustomerRecord>> Function(String, String)? onCustomer;
  Future<AuthResult<RecordPage<VehicleRecord>>> Function(
    String?,
    String?,
    String,
    int,
  )?
  onVehicles;
  Future<AuthResult<LocalCustomerRecord>> Function(String, CustomerDraft)?
  onSaveCustomer;
  Future<AuthResult<VehicleRecord>> Function(VehicleDraft, String?, String?)?
  onSaveVehicle;
  Future<AuthResult<void>> Function(VehicleRecord)? onArchive;
  @override
  Future<AuthResult<RecordPage<LocalCustomerRecord>>> customers(
    String b, {
    String query = '',
    int page = 1,
  }) async {
    calls.add('customers:$b:$query:$page');
    return onCustomers == null
        ? AuthSuccess(
            RecordPage(
              people
                  .where(
                    (p) =>
                        p.fullName.toLowerCase().contains(query.toLowerCase()),
                  )
                  .toList(),
              page,
              false,
            ),
          )
        : onCustomers!(b, query, page);
  }

  @override
  Future<AuthResult<LocalCustomerRecord>> customer(String b, String id) async {
    calls.add('customer:$b:$id');
    return onCustomer == null
        ? AuthSuccess(people.singleWhere((p) => p.id == id))
        : onCustomer!(b, id);
  }

  @override
  Future<AuthResult<LocalCustomerRecord>> saveCustomer(
    String b,
    CustomerDraft d,
  ) async {
    calls.add('saveCustomer:$b');
    if (onSaveCustomer != null) return onSaveCustomer!(b, d);
    final row = LocalCustomerRecord.fromJson({
      ...customerJson(business: b, version: (d.version ?? 0) + 1),
      ...d.fields,
    });
    people = [row, ...people.where((p) => p.id != row.id)];
    return AuthSuccess(row);
  }

  @override
  Future<AuthResult<RecordPage<VehicleRecord>>> vehicles({
    String? business,
    String? customer,
    String query = '',
    int page = 1,
  }) async {
    calls.add('vehicles:$business:$customer:$query:$page');
    return onVehicles == null
        ? AuthSuccess(
            RecordPage(
              cars
                  .where(
                    (v) =>
                        v.businessId == business &&
                        (customer == null || v.localCustomerId == customer),
                  )
                  .toList(),
              page,
              false,
            ),
          )
        : onVehicles!(business, customer, query, page);
  }

  @override
  Future<AuthResult<VehicleRecord>> saveVehicle(
    VehicleDraft d, {
    String? business,
    String? customer,
  }) async {
    calls.add('saveVehicle:$business:$customer');
    if (onSaveVehicle != null) return onSaveVehicle!(d, business, customer);
    final json = d.toJson();
    final row = VehicleRecord.fromJson({
      ...vehicleJson(
        business: business,
        customer: customer,
        version: (d.version ?? 0) + 1,
      ),
      ...json,
      'version': (d.version ?? 0) + 1,
    });
    cars = [row, ...cars.where((v) => v.id != row.id)];
    return AuthSuccess(row);
  }

  @override
  Future<AuthResult<void>> archiveVehicle(VehicleRecord v) async {
    calls.add('archive:${v.id}');
    if (onArchive != null) return onArchive!(v);
    cars.removeWhere((item) => item.id == v.id);
    return const AuthSuccess(null);
  }
}
