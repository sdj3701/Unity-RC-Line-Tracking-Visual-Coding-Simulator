namespace Auth
{
    /// <summary>
    /// 서버 오류 코드를 사용자 메시지로 변환한다.
    /// </summary>
    public static class AuthErrorMapper
    {
        public const string ValidationError = "VALIDATION_ERROR";
        public const string InvalidCredentials = "INVALID_CREDENTIALS";
        public const string TokenExpiredOrInvalid = "TOKEN_EXPIRED_OR_INVALID";
        public const string AccountLocked = "ACCOUNT_LOCKED";
        public const string AccountDisabled = "ACCOUNT_DISABLED";
        public const string TooManyAttempts = "TOO_MANY_ATTEMPTS";
        public const string NetworkError = "NETWORK_ERROR";
        public const string InternalError = "INTERNAL_ERROR";
        public const string UnknownError = "UNKNOWN_ERROR";
        public const string AuthenticationBusy = "AUTHENTICATION_BUSY";

        public static string ToUserMessage(string errorCode, string fallbackMessage = null)
        {
            string normalized = string.IsNullOrWhiteSpace(errorCode)
                ? UnknownError
                : errorCode.Trim().ToUpperInvariant();

            switch (normalized)
            {
                case InvalidCredentials:
                    return "아이디 또는 비밀번호가 올바르지 않습니다.";
                case TokenExpiredOrInvalid:
                    return "로그인 인증 정보가 만료되었거나 유효하지 않습니다. 다시 로그인해 주세요.";
                case ValidationError:
                    return "입력 형식을 확인해 주세요.";
                case AccountLocked:
                    return "계정이 잠겼습니다. 관리자에게 문의하세요.";
                case AccountDisabled:
                    return "비활성화된 계정입니다.";
                case TooManyAttempts:
                    return "시도 횟수를 초과했습니다. 잠시 후 다시 시도하세요.";
                case NetworkError:
                    return "네트워크 연결을 확인해 주세요.";
                case InternalError:
                    return "서버 오류가 발생했습니다. 잠시 후 다시 시도하세요.";
                case AuthenticationBusy:
                    return "로그인 요청 처리 중입니다. 잠시만 기다려 주세요.";
                default:
                    if (!string.IsNullOrWhiteSpace(fallbackMessage))
                        return fallbackMessage;
                    return "알 수 없는 오류가 발생했습니다.";
            }
        }

        /// <summary>
        /// 로그인 API 실패 코드(HTTP -> 내부코드) 매핑.
        /// </summary>
        public static string FromStatusCode(long statusCode)
        {
            switch (statusCode)
            {
                case 400:
                    return ValidationError;
                case 401:
                    return InvalidCredentials;
                case 403:
                    return AccountLocked;
                case 429:
                    return TooManyAttempts;
                case 500:
                    return InternalError;
                default:
                    return UnknownError;
            }
        }
    }
}
