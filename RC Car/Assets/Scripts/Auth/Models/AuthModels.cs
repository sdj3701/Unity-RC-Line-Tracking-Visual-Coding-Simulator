// Assets/Scripts/Auth/Models/AuthModels.cs
using System;

namespace Auth.Models
{
    /// <summary>
    /// ?좏겙 寃利?API???섑띁 ?묐떟 紐⑤뜽.
    /// </summary>
    [Serializable]
    public class AuthResponse
    {
        public bool success;
        public UserInfo user;
        public string error;
    }

    /// <summary>
    /// ID/PW 濡쒓렇???붿껌 紐⑤뜽.
    /// ?쒕쾭 怨꾩빟??留욎떠 userId ?ㅻ? ?ъ슜?쒕떎.
    /// </summary>
    [Serializable]
    public class LoginRequest
    {
        public string userId;
        public string password;
    }

    /// <summary>
    /// 濡쒓렇???묐떟?먯꽌 ?좏겙??data ?꾨옒???대젮?ㅻ뒗 寃쎌슦瑜??꾪븳 蹂댁“ 紐⑤뜽.
    /// </summary>
    [Serializable]
    public class LoginTokenData
    {
        public string accessToken;
        public string token;
        public string access_token;
        public string refreshToken;
        public string refresh;
        public string refresh_token;
    }

    /// <summary>
    /// ID/PW 濡쒓렇???묐떟 紐⑤뜽.
    /// 諛깆뿏???묐떟 蹂?뺤뿉 ?鍮꾪빐 ?щ윭 ?ㅻ? ?④퍡 ?좎뼵?쒕떎.
    /// </summary>
    [Serializable]
    public class LoginResponse
    {
        public bool success;
        public bool isSuccess;

        public string accessToken;
        public string token;
        public string access_token;

        public string refreshToken;
        public string refresh;
        public string refresh_token;

        public LoginTokenData data;
        public UserInfo user;

        public string errorCode;
        public string code;
        public string message;
        public string error;
        public string detail;
        public bool retryable;
    }

    /// <summary>
    /// 濡쒓렇?몃맂 ?ъ슜???뺣낫.
    /// </summary>
    [Serializable]
    public class UserInfo
    {
        public string userId;
        public string email;
        public string name;
        public string username;
        public string role;
        public string status;
        public string profileImageUrl;
        public string phone;
        public string roadAddress;
        public string detailAddress;
        public string zipCode;
        public bool agreeMarketing;
        public string createdAt;
        public string lastLogin;
    }

    /// <summary>
    /// ?좏겙 寃利?寃곌낵 紐⑤뜽.
    /// </summary>
    public class AuthResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public UserInfo User { get; set; }
    }

    /// <summary>
    /// 濡쒓렇??API 寃곌낵 紐⑤뜽.
    /// </summary>
    public class LoginResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public bool Retryable { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public UserInfo User { get; set; }
        public long StatusCode { get; set; }
    }
}
