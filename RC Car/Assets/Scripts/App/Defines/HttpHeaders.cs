namespace RC.App.Defines
{
    /// <summary>
    /// HTTP 통신에서 공통으로 사용하는 헤더 키를 관리한다.
    /// 문자열 오타를 방지하고 헤더 사용 규칙을 통일하기 위한 클래스다.
    /// </summary>
    public static class HttpHeaders
    {
        /// <summary>응답 포맷 지정 헤더.</summary>
        public const string Accept = "Accept";
        /// <summary>인증 토큰 전달 헤더.</summary>
        public const string Authorization = "Authorization";
        /// <summary>요청 본문 타입 지정 헤더.</summary>
        public const string ContentType = "Content-Type";
        /// <summary>멱등 요청 식별 헤더.</summary>
        public const string IdempotencyKey = "Idempotency-Key";
    }
}
