namespace RC.App.Defines
{
    /// <summary>
    /// 프로젝트에서 사용하는 씬 이름 상수를 한 곳에서 관리한다.
    /// 씬 이름 하드코딩을 줄여 변경 누락을 방지하기 위한 클래스다.
    /// </summary>
    public static class AppScenes
    {
        /// <summary>로그인 씬 이름.</summary>
        public const string Login = "00_Login";
        /// <summary>로비 씬 이름.</summary>
        public const string Lobby = "01_Lobby";
        /// <summary>싱글 블록 코드 생성 씬 이름.</summary>
        public const string SingleCreateBlock = "02_SingleCreateBlock";
        /// <summary>네트워크 차량 테스트 씬 이름.</summary>
        public const string NetworkCarTest = "03_NetworkCarTest";
    }
}
