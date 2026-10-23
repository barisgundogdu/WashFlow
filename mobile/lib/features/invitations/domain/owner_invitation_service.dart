import '../../auth/domain/auth_service.dart';
import 'invitation.dart';

abstract interface class OwnerInvitationService {
  Future<AuthResult<List<EmployeeInvitation>>> list(String businessId);
  Future<AuthResult<EmployeeInvitation>> create(
    String businessId,
    String email,
  );
  Future<AuthResult<void>> revoke(String businessId, String invitationId);
  Future<AuthResult<EmployeeInvitation>> resend(
    String businessId,
    String invitationId,
  );
}

String invitationErrorMessage(AuthError error) => switch (error.code) {
  AuthErrorCode.validation =>
    'Geçerli bir e-posta adresi girin. E-posta 320 karakteri aşamaz.',
  AuthErrorCode.invalidInvitation =>
    'Davetin durumu değişmiş olabilir. Güncel listeyi kontrol edin.',
  AuthErrorCode.notFound =>
    'Davet veya kaynak bulunamadı. Güncel listeyi kontrol edin.',
  AuthErrorCode.conflict => 'Davet mevcut durumla çakışıyor. Bekleyen daveti ve işletme üyeliğini kontrol edin.',
  AuthErrorCode.forbidden => 'Bu işletmede davet yönetimi için yetkiniz yok.',
  AuthErrorCode.unauthenticated || AuthErrorCode.invalidCredentials =>
    'Oturumunuz geçersiz. Lütfen yeniden giriş yapın.',
  AuthErrorCode.rateLimited =>
    'Çok fazla istek gönderildi. Bir süre sonra tekrar deneyin.',
  AuthErrorCode.network =>
    'Sunucuya ulaşılamadı. İnternet bağlantınızı kontrol edin.',
  AuthErrorCode.timeout => 'İstek zaman aşımına uğradı.',
  AuthErrorCode.storage =>
    'Güvenli oturum kaydı kullanılamıyor. Lütfen yeniden giriş yapın.',
  AuthErrorCode.unavailable =>
    'Hizmet şu anda kullanılamıyor. Daha sonra tekrar deneyin.',
  _ => 'İşlem sonucu doğrulanamadı. Güncel listeyi kontrol edin.',
};
