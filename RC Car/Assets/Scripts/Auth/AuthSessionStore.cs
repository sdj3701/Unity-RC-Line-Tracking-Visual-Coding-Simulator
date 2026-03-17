using UnityEngine;

namespace Auth
{
    /// <summary>
    /// 로컬 세션 토큰 저장/삭제를 단일 경로로 관리한다.
    /// </summary>
    public static class AuthSessionStore
    {
        private const string AccessTokenKey = "auth_access_token";
        private const string RefreshTokenKey = "auth_refresh_token";

        /// <summary>
        /// access/refresh 토큰을 PlayerPrefs에 저장한다.
        /// refresh 토큰이 비어 있으면 해당 키를 삭제한다.
        /// </summary>
        /// <param name="accessToken">저장할 access token</param>
        /// <param name="refreshToken">저장할 refresh token</param>
        public static void Save(string accessToken, string refreshToken)
        {
            PlayerPrefs.SetString(AccessTokenKey, accessToken ?? string.Empty);

            if (string.IsNullOrEmpty(refreshToken))
                PlayerPrefs.DeleteKey(RefreshTokenKey);
            else
                PlayerPrefs.SetString(RefreshTokenKey, refreshToken);

            PlayerPrefs.Save();
        }

        /// <summary>
        /// 저장된 access token을 조회한다.
        /// </summary>
        /// <returns>저장된 access token, 없으면 빈 문자열</returns>
        public static string GetAccessToken()
        {
            return PlayerPrefs.GetString(AccessTokenKey, string.Empty);
        }

        /// <summary>
        /// 저장된 refresh token을 조회한다.
        /// </summary>
        /// <returns>저장된 refresh token, 없으면 빈 문자열</returns>
        public static string GetRefreshToken()
        {
            return PlayerPrefs.GetString(RefreshTokenKey, string.Empty);
        }

        /// <summary>
        /// 저장된 인증 토큰(access/refresh)을 모두 삭제한다.
        /// </summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(AccessTokenKey);
            PlayerPrefs.DeleteKey(RefreshTokenKey);
            PlayerPrefs.Save();
        }
    }
}
