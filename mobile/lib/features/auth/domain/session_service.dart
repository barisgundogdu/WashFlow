import 'auth_service.dart';

/// Provides a current credential through serialized session handling.
/// Callers never read persisted credentials or rotate tokens themselves.
abstract interface class AccessTokenProvider {
  Future<AuthResult<String>> accessToken();
}

abstract interface class SessionService {
  Future<AuthResult<bool>> restoreSession();
  Future<AuthResult<void>> refreshSession();
  Future<AuthResult<void>> logout();
  Future<AuthResult<void>> verifyEmail(String token);
  Future<AuthResult<void>> resendVerification(String email);
}

String authErrorMessage(AuthError error) => switch (error.code) {
  AuthErrorCode.unauthenticated =>
    'Oturumunuz geçersiz. Lütfen yeniden giriş yapın.',
  AuthErrorCode.forbidden => 'Bu işlem için yetkiniz yok.',
  AuthErrorCode.invalidCredentials =>
    'Giriş yapılamadı. Bilgilerinizi ve e-posta doğrulamanızı kontrol edin.',
  AuthErrorCode.validation => 'Bilgileri kontrol edin. Adlar en fazla 200, e-posta 320, şifre 1024 karakter olabilir.',
  AuthErrorCode.network =>
    'Sunucuya ulaşılamadı. İnternet bağlantınızı kontrol edin.',
  AuthErrorCode.timeout => 'İstek zaman aşımına uğradı. Lütfen tekrar deneyin.',
  AuthErrorCode.rateLimited =>
    'Çok fazla istek gönderildi. Bir süre sonra tekrar deneyin.',
  AuthErrorCode.invalidVerificationToken =>
    'Doğrulama bilgisi geçersiz veya süresi dolmuş. Yeni e-posta isteyin.',
  AuthErrorCode.storage =>
    'Güvenli oturum kaydı kullanılamıyor. Lütfen tekrar deneyin.',
  _ => 'İşlem tamamlanamadı. Lütfen tekrar deneyin.',
};
