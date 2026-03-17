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

        public static void Save(string accessToken, string refreshToken)
        {
            PlayerPrefs.SetString(AccessTokenKey, accessToken ?? string.Empty);

            if (string.IsNullOrEmpty(refreshToken))
                PlayerPrefs.DeleteKey(RefreshTokenKey);
            else
                PlayerPrefs.SetString(RefreshTokenKey, refreshToken);

            PlayerPrefs.Save();
        }

        public static string GetAccessToken()
        {
            return PlayerPrefs.GetString(AccessTokenKey, string.Empty);
        }

        public static string GetRefreshToken()
        {
            return PlayerPrefs.GetString(RefreshTokenKey, string.Empty);
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(AccessTokenKey);
            PlayerPrefs.DeleteKey(RefreshTokenKey);
            PlayerPrefs.Save();
        }
    }
}
