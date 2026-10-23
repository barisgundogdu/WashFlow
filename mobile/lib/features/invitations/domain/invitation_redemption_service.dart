import '../../auth/domain/auth_service.dart';

/// No record-generated diagnostics containing credentials/passwords.
class InvitationRegistrationRequest {
  const InvitationRegistrationRequest({
    required this.token,
    required this.fullName,
    required this.email,
    required this.password,
  });
  final String token, fullName, email, password;
}

abstract interface class InvitationRedemptionService {
  Future<AuthResult<void>> register(InvitationRegistrationRequest request);
  Future<AuthResult<void>> accept(String credential);
}

String redemptionErrorMessage(AuthError error) => switch (error.code) {
  AuthErrorCode.invalidInvitation => 'Davet kabul edilemedi. Bilgi geçersiz, süresi dolmuş, iptal edilmiş veya kullanılmış olabilir; davet edilen doğrulanmış e-posta hesabıyla giriş yaptığınızı kontrol edin. Gerekirse işletme sahibinden yeni davet isteyin.',
  AuthErrorCode.conflict => 'Davet, hesabın işletmedeki üyelik durumuyla çakışıyor. Mevcut erişimlerinizi yenileyin veya işletme sahibiyle görüşün.',
  AuthErrorCode.unauthenticated =>
    'Oturumunuz geçersiz. Davet edilen hesabınızla yeniden giriş yapın.',
  AuthErrorCode.invalidCredentials =>
    'Giriş yapılamadı. Bilgilerinizi ve e-posta doğrulamanızı kontrol edin.',
  AuthErrorCode.forbidden => 'Bu işlem için yetkiniz yok. Doğru doğrulanmış hesapla giriş yaptığınızı kontrol edin.',
  AuthErrorCode.notFound =>
    'Davet hizmeti veya kaynak bulunamadı. Daha sonra yeniden deneyin.',
  AuthErrorCode.validation => 'Bilgileri kontrol edin. Geçerli e-posta, ad (en fazla 200 karakter) ve şifre (en fazla 1024 karakter) gereklidir.',
  AuthErrorCode.rateLimited =>
    'Çok fazla istek gönderildi. Bir süre sonra tekrar deneyin.',
  AuthErrorCode.storage =>
    'Güvenli oturum kaydı kullanılamıyor. Lütfen tekrar deneyin.',
  AuthErrorCode.network =>
    'Sunucuya ulaşılamadı. İnternet bağlantınızı kontrol edin.',
  AuthErrorCode.timeout => 'İstek zaman aşımına uğradı.',
  AuthErrorCode.unavailable => 'Hizmet şu anda kullanılamıyor.',
  _ => 'İşlem sonucu doğrulanamadı.',
};
