public static class RoomSessionContext
{
    public static RoomInfo CurrentRoom { get; private set; }

    /// <summary>
    /// 현재 로비에서 확정된 룸 정보를 런타임 전역 컨텍스트에 저장한다.
    /// 씬 전환 후 다음 씬이 방 정보를 참조할 수 있도록 하는 진입점이다.
    /// </summary>
    /// <param name="roomInfo">씬 전환 대상 룸 정보</param>
    public static void Set(RoomInfo roomInfo)
    {
        CurrentRoom = roomInfo;
    }

    /// <summary>
    /// 저장된 룸 컨텍스트를 초기화한다.
    /// 로그아웃, 룸 이탈, 재입장 등의 시나리오에서 이전 상태 누수를 방지한다.
    /// </summary>
    public static void Clear()
    {
        CurrentRoom = null;
    }
}
