import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/customer_vehicles/domain/customer_vehicles.dart';
import 'package:washflow/features/service_catalog/domain/service_catalog.dart';

import 'session_fakes.dart';

const serviceId = '66666666-6666-6666-6666-666666666666';
Map<String, dynamic> catalogJson({
  String business = businessA,
  int version = 1,
}) => {
  'id': serviceId,
  'businessId': business,
  'name': 'Dış yıkama',
  'description': 'Açıklama',
  'version': version,
  'enabled': true,
  'published': true,
  'offerings': [
    {
      'category': 'Car',
      'price': '450.10',
      'currency': 'TRY',
      'activeMinutes': 20,
      'waitingMinutes': 120,
      'employees': 1,
    },
  ],
};
CatalogServiceRecord catalogRecord({
  String business = businessA,
  int version = 1,
}) => CatalogServiceRecord.fromJson(
  catalogJson(business: business, version: version),
);

class FakeCatalog implements CatalogService {
  int lists = 0, reads = 0, saves = 0, archives = 0;
  String? lastQuery, lastStatus, lastBusiness;
  int? lastPage;
  Future<AuthResult<RecordPage<CatalogServiceRecord>>> Function()? listHandler;
  Future<AuthResult<CatalogServiceRecord>> Function()? readHandler, saveHandler;
  Future<AuthResult<void>> Function()? archiveHandler;
  CatalogDraft? savedDraft;
  @override
  Future<AuthResult<RecordPage<CatalogServiceRecord>>> list(
    String business, {
    String query = '',
    String status = 'all',
    int page = 1,
  }) async {
    lists++;
    lastBusiness = business;
    lastQuery = query;
    lastStatus = status;
    lastPage = page;
    return listHandler == null
        ? AuthSuccess(
            RecordPage([catalogRecord(business: business)], page, false),
          )
        : listHandler!();
  }

  @override
  Future<AuthResult<CatalogServiceRecord>> read(
    String business,
    String id,
  ) async {
    reads++;
    return readHandler == null
        ? AuthSuccess(catalogRecord(business: business, version: 2))
        : readHandler!();
  }

  @override
  Future<AuthResult<CatalogServiceRecord>> save(
    String business,
    CatalogDraft draft,
  ) async {
    saves++;
    savedDraft = draft;
    return saveHandler == null
        ? AuthSuccess(catalogRecord(business: business, version: 2))
        : saveHandler!();
  }

  @override
  Future<AuthResult<void>> archive(
    String business,
    CatalogServiceRecord record,
  ) async {
    archives++;
    return archiveHandler == null ? const AuthSuccess(null) : archiveHandler!();
  }
}
