namespace RC.App.Networking
{
    /// <summary>
    /// 베이스 URL과 API 경로를 조합해 최종 요청 URL을 만든다.
    /// URL 조합 규칙을 한 곳에 모아 중복 구현을 줄이기 위한 유틸리티다.
    /// </summary>
    public static class ApiUrlResolver
    {
        /// <summary>
        /// baseUrl/route의 앞뒤 슬래시를 정리해 안전하게 결합한다.
        /// 둘 중 하나가 비어 있으면 다른 값을 그대로 반환한다.
        /// </summary>
        public static string Build(string baseUrl, string route)
        {
            string safeBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? string.Empty : baseUrl.Trim();
            string safeRoute = string.IsNullOrWhiteSpace(route) ? string.Empty : route.Trim();

            if (string.IsNullOrEmpty(safeBaseUrl))
                return safeRoute;

            if (string.IsNullOrEmpty(safeRoute))
                return safeBaseUrl;

            return safeBaseUrl.TrimEnd('/') + "/" + safeRoute.TrimStart('/');
        }
    }
}
