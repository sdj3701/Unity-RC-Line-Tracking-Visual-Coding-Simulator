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
    /// 로그인한 사용자 정보
    /// </summary>
    [Serializable]
    public class UserInfo
    {
        public string userId;
        public string username;
        public string email;
        // 필요에 따라 추가
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
