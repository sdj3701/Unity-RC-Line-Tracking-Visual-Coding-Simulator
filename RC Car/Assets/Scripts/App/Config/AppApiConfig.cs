using UnityEngine;

namespace RC.App.Config
{
    /// <summary>
    /// API 서버 환경별 설정을 보관하는 ScriptableObject다.
    /// Dev/Stage/Prod 베이스 URL과 요청 타임아웃을 중앙에서 관리한다.
    /// </summary>
    [CreateAssetMenu(fileName = "AppApiConfig", menuName = "RC/App API Config")]
    public sealed class AppApiConfig : ScriptableObject
    {
        [Header("Base URL")]
        [Tooltip("개발 환경 베이스 URL")]
        public string devBaseUrl = "http://localhost:5000";
        [Tooltip("스테이징 환경 베이스 URL")]
        public string stageBaseUrl = "";
        [Tooltip("운영 환경 베이스 URL")]
        public string prodBaseUrl = "http://ioteacher.com";

        [Header("Request")]
        [Tooltip("요청 타임아웃(초)")]
        [Min(1)]
        public int requestTimeoutSeconds = 15;

        /// <summary>
        /// 빌드 심볼에 따라 현재 실행 환경의 베이스 URL을 반환한다.
        /// </summary>
        public string CurrentBaseUrl
        {
            get
            {
#if API_ENV_PROD
                return prodBaseUrl;
#elif API_ENV_STAGE
                return stageBaseUrl;
#else
                return devBaseUrl;
#endif
            }
        }
    }
}
