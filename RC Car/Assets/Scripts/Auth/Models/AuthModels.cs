// Assets/Scripts/Auth/Models/AuthModels.cs
using System;

namespace Auth.Models
{
    /// <summary>
    /// 서버에서 받은 인증 응답
    /// </summary>
    [Serializable]
    public class AuthResponse
    {
        public bool success;
        public UserInfo user;
        public string error;
    }

    /// <summary>
    /// 로그인한 사용자 정보 (서버 응답 형식에 맞춤)
    /// </summary>
    [Serializable]
    public class UserInfo
    {
        public string userId;
        public string email;
        public string name;           // 서버에서 name으로 반환
        public string username;       // 호환성 유지
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
    /// 인증 결과
    /// </summary>
    public class AuthResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public UserInfo User { get; set; }
    }
}
