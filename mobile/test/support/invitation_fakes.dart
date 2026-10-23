import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/invitations/domain/invitation.dart';
import 'package:washflow/features/invitations/domain/owner_invitation_service.dart';

const invitationA = '66666666-6666-6666-6666-666666666666';
const invitationB = '77777777-7777-7777-7777-777777777777';
Map<String, dynamic> invitationJson({
  String id = invitationA,
  String email = 'employee@example.test',
  String? accepted,
  String? revoked,
  String? expiry,
}) => {
  'id': id,
  'email': email,
  'expiresAt':
      expiry ??
      DateTime.now().toUtc().add(const Duration(days: 1)).toIso8601String(),
  'acceptedAt': accepted,
  'revokedAt': revoked,
};
EmployeeInvitation invitation({
  String id = invitationA,
  String email = 'employee@example.test',
}) => EmployeeInvitation.fromJson(invitationJson(id: id, email: email));

class FakeInvitations implements OwnerInvitationService {
  List<EmployeeInvitation> items = [];
  final calls = <String>[];
  Future<AuthResult<List<EmployeeInvitation>>> Function(String)? onList;
  Future<AuthResult<EmployeeInvitation>> Function(String, String)? onCreate;
  Future<AuthResult<EmployeeInvitation>> Function(String, String)? onResend;
  Future<AuthResult<void>> Function(String, String)? onRevoke;
  @override
  Future<AuthResult<List<EmployeeInvitation>>> list(String businessId) async {
    calls.add('list:$businessId');
    return onList == null ? AuthSuccess(List.of(items)) : onList!(businessId);
  }

  @override
  Future<AuthResult<EmployeeInvitation>> create(
    String businessId,
    String email,
  ) async {
    calls.add('create:$businessId');
    if (onCreate != null) return onCreate!(businessId, email);
    final result = invitation(email: email);
    items = [result];
    return AuthSuccess(result);
  }

  @override
  Future<AuthResult<EmployeeInvitation>> resend(
    String businessId,
    String id,
  ) async {
    calls.add('resend:$businessId:$id');
    if (onResend != null) return onResend!(businessId, id);
    final result = invitation(id: invitationB);
    items = [result];
    return AuthSuccess(result);
  }

  @override
  Future<AuthResult<void>> revoke(String businessId, String id) async {
    calls.add('revoke:$businessId:$id');
    if (onRevoke != null) return onRevoke!(businessId, id);
    items = [];
    return const AuthSuccess(null);
  }
}
