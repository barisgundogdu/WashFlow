import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../invitations/domain/invitation.dart';
import '../domain/scheduling.dart';

/// One pending create per owner membership/business; never stores credentials.
class SecureBookingAttemptStore implements BookingAttemptStore {
  SecureBookingAttemptStore(String business, String membership)
    : _key =
          'washflow.scheduling.pending.v1.${invitationId(business)}.${invitationId(membership)}';
  final String _key;
  static const _storage = FlutterSecureStorage(
    iOptions: IOSOptions(
      accessibility: KeychainAccessibility.unlocked_this_device,
    ),
  );
  @override
  Future<String?> read() => _storage.read(key: _key);
  @override
  Future<void> write(String value) => _storage.write(key: _key, value: value);
  @override
  Future<void> clear() => _storage.delete(key: _key);
}
