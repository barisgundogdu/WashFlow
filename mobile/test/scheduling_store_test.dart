import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/features/scheduling/data/secure_booking_attempt_store.dart';

import 'support/scheduling_fakes.dart';
import 'support/session_fakes.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUp(() => FlutterSecureStorage.setMockInitialValues({}));
  test('journal survives store replacement and isolates business and owner membership', () async {
    final store = SecureBookingAttemptStore(businessA, employeeId);
    await store.write('pending');
    expect(
      await SecureBookingAttemptStore(businessA, employeeId).read(),
      'pending',
    );
    expect(
      await SecureBookingAttemptStore(businessB, employeeId).read(),
      isNull,
    );
    expect(await SecureBookingAttemptStore(businessA, areaId).read(), isNull);
    await store.clear();
    expect(await store.read(), isNull);
  });
}
