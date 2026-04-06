using System;

namespace RC.App.Defines
{
    /// <summary>
    /// 서버 API 경로를 중앙에서 관리한다.
    /// 엔드포인트 문자열 중복을 줄이고, 경로 변경 시 수정 지점을 단일화하기 위한 클래스다.
    /// </summary>
    public static class ApiRoutes
    {
        /// <summary>아이디/비밀번호 로그인 경로.</summary>
        public const string AuthLogin = "/api/auth/login";
        /// <summary>토큰 기반 사용자 검증 경로.</summary>
        public const string AuthMeByToken = "/api/users/me-by-token";

        /// <summary>룸 생성 경로.</summary>
        public const string RoomCreate = "/api/rooms";
        /// <summary>룸 작업 상태 조회 경로 템플릿.</summary>
        public const string RoomJobStatus = "/api/room-jobs/{jobId}";
        /// <summary>룸 상태 조회 경로 템플릿.</summary>
        public const string RoomStatus = "/api/rooms/{roomId}/status?jobId={jobId}";

        /// <summary>채팅방 목록/생성 경로.</summary>
        public const string ChatRooms = "/api/chat/rooms";
        /// <summary>입장 요청 승인/거절 경로 템플릿.</summary>
        public const string ChatJoinRequestDecision = "/api/chat/join-requests/{requestId}/decision";
        /// <summary>내 입장 요청 상태 조회 경로 템플릿.</summary>
        public const string ChatMyJoinRequestStatus = "/api/chat/my/join-request/{requestId}";
        /// <summary>블록 공유 저장 경로 템플릿.</summary>
        public const string ChatSaveBlockShareToMyLevel = "/api/chat/block-shares/{shareId}/save-to-my-level";
        /// <summary>유저 레벨 상세 조회 경로 템플릿.</summary>
        public const string UserLevelDetail = "/api/user-level/{seq}";

        /// <summary>
        /// roomId를 포함한 입장 요청 경로를 만든다.
        /// </summary>
        public static string ChatJoinRequest(string roomId)
            => "/api/chat/rooms/" + Escape(roomId) + "/join-request";

        /// <summary>
        /// roomId를 포함한 입장 요청 목록 경로를 만든다.
        /// </summary>
        public static string ChatJoinRequests(string roomId)
            => "/api/chat/rooms/" + Escape(roomId) + "/join-requests";

        /// <summary>
        /// roomId를 포함한 블록 공유 목록 경로를 만든다.
        /// </summary>
        public static string ChatBlockShares(string roomId)
            => "/api/chat/rooms/" + Escape(roomId) + "/block-shares";

        /// <summary>
        /// roomId/shareId를 포함한 블록 공유 상세 경로를 만든다.
        /// </summary>
        public static string ChatBlockShareDetail(string roomId, string shareId)
            => "/api/chat/rooms/" + Escape(roomId) + "/block-shares/" + Escape(shareId);

        /// <summary>
        /// URL 경로 세그먼트에 안전하게 들어갈 수 있도록 값을 인코딩한다.
        /// </summary>
        private static string Escape(string value)
        {
            string source = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            return Uri.EscapeDataString(source);
        }
    }
}
