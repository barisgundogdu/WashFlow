import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:washflow/features/auth/data/token_store.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  test(
    'secure adapter writes, reads across instances and deletes only its key',
    () async {
      FlutterSecureStorage.setMockInitialValues({'unrelated': 'keep'});
      const first = SecureTokenStore();
      expect(await first.read(), isNull);
      await first.write('session-json');
      const restarted = SecureTokenStore();
      expect(await restarted.read(), 'session-json');
      await restarted.clear();
      expect(await first.read(), isNull);
      expect(await const FlutterSecureStorage().read(key: 'unrelated'), 'keep');
    },
  );
}
