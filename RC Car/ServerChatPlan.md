# Server Chat Plan

## 1) 목표
- 목표: 현재 구현된 `Server / ServerCore / DummyClient / Unity Scripts(Server)`를 기준으로 채팅 기능을 안정적으로 완성하기.
- 이 문서는 "현재 코드 분석 + 구현 로드맵"을 함께 담은 실행용 정리 문서다.

## 2) 분석 범위
- Unity 클라이언트: `RC Car/Assets/Scripts/Server/*`
- 서버 앱: `../Server/*`
- 네트워크 코어: `../ServerCore/*`
- 더미 부하/테스트 클라이언트: `../DummyClient/*`

## 3) 전체 구조 한눈에
```text
Unity Client (Assets/Scripts/Server)
  └─ Connector -> ServerSession(PacketSession) -> PacketManager -> PacketHandler
                                   ↑
Server (Server/Program)
  └─ Listener -> ClientSession(PacketSession) -> PacketManager -> C_ChatHandler
                                                        └─ GameRoom.Broadcast -> S_Chat 전송

DummyClient
  └─ Connector(10개 세션) -> 주기적으로 C_Chat 전송 (250ms)

ServerCore
  └─ Listener/Connector/Session/RecvBuffer/SendBuffer/JobQueue/PriorityQueue
```

## 4) 공통 패킷 규약
- 헤더 구조: `[size(2)][packetId(2)][payload...]`
- 현재 ID:
  - `C_Chat = 1` (클라 -> 서버)
  - `S_Chat = 2` (서버 -> 클라)
- 문자열 직렬화: `Encoding.Unicode`, 길이 `ushort`.

## 5) ServerCore 상세 분석

### `Session.cs`
- `Session`:
  - `SocketAsyncEventArgs` 기반 비동기 송수신.
  - `_sendQueue` + `_pendingList`로 모아서 송신.
  - `RecvBuffer`에 누적 후 `OnRecv`가 처리한 길이만큼 소비.
- `PacketSession`:
  - 버퍼에서 `size`를 읽어 완전한 패킷 단위로 분리.
  - 여러 패킷이 한 번에 오면 연속 처리.

### `Listener.cs`
- `AcceptAsync`를 미리 여러 개 등록(register)해서 수락 처리량 확보.
- 새 연결마다 `sessionFactory()`로 세션 생성 후 `Start`.

### `Connector.cs`
- 클라이언트용 비동기 연결 헬퍼.

### `RecvBuffer.cs`
- `Clean()`으로 읽고 남은 데이터 앞으로 당겨 fragmentation 처리.

### `SendBuffer.cs`
- `ThreadLocal<SendBuffer>` 재사용.
- 패킷 직렬화 시 `Open/Close` 패턴으로 `ArraySegment<byte>` 확보.

### `JobQueue.cs`, `PriorityQueue.cs`
- `JobQueue`: 단일 플러시 루프(락 기반)로 순차 실행.
- `PriorityQueue`: `JobTimer`에서 실행 시점 기반 우선순위 큐로 사용.

## 6) Server 상세 분석

### `Program.cs`
- `Listener`를 `7777`에 바인딩해서 클라이언트 수신.
- `FlushRoom()`을 `JobTimer`에 250ms 간격으로 재등록.
- 메인 루프는 `while(true) JobTimer.Flush();` 형태.

### `Session/ClientSession.cs`
- 접속 시 `Program.Room.Enter(this)`를 room job으로 enqueue.
- 수신 패킷은 `PacketManager`로 위임.
- 종료 시 `SessionManager` 제거 + Room leave enqueue.

### `Session/SessionManager.cs`
- 세션 ID 발급/조회/삭제 담당.

### `Packet/ServerPacketManager.cs` + `PacketHandler.cs`
- `C_Chat` 수신 -> `C_ChatHandler`.
- 핸들러는 Room job으로 `Broadcast(session, chat)` 호출.

### `GameRoom.cs`
- `_sessions`에 참가자 유지.
- `Broadcast`는 즉시 전송하지 않고 `_pendingList`에 `S_Chat` segment 적재.
- `Flush()` 시 현재 pending을 전체 세션에 전송 후 clear.

### `JobTimer.cs`
- 실행 tick 기준으로 액션 예약/실행.
- `FlushRoom` 주기 실행의 스케줄러 역할.

## 7) DummyClient 상세 분석

### `Program.cs`
- 서버에 10개 세션 동시 연결.
- 무한 루프에서 250ms마다 각 세션이 `C_Chat` 전송.

### `SessionManager.cs`
- 세션 목록 보관 + `SendForEach()`로 일괄 송신.

### `ServerSession.cs`, `Packet/*`
- 서버와 같은 패킷 스키마 사용.
- 수신 핸들러(`S_ChatHandler`)는 현재 출력 코드가 사실상 비활성.

## 8) Unity `Assets/Scripts/Server` 상세 분석

### `NetworkManager.cs`
- `Start()`에서 서버 `7777`에 단일 세션 연결.
- 현재는 연결만 하고 사용자 입력 기반 송신 로직은 없음.

### `Network/*`
- `Session`, `PacketSession`, `Connector`, `RecvBuffer`, `SendBuffer`가 `ServerCore` 복제 형태.

### `Packet/*`
- `ClientPacketManager`는 `S_Chat`만 처리.
- `PacketHandler.S_ChatHandler`는 `playerId == 1`일 때만 `Debug.Log` 출력.

## 9) 현재 채팅 흐름 (실제 동작)
1. DummyClient 또는 Unity가 `C_Chat` 전송.
2. Server `ClientSession`이 수신하고 `C_ChatHandler`로 전달.
3. `C_ChatHandler`가 `GameRoom.Broadcast` job enqueue.
4. `Broadcast`가 `S_Chat`를 `_pendingList`에 추가.
5. 250ms 주기의 `FlushRoom`에서 전체 세션으로 `S_Chat` 일괄 전송.
6. 클라이언트가 `S_Chat` 수신 후 각자 핸들러에서 처리.

## 10) 확인된 강점/리스크

### 강점
- 패킷 경계 처리(`size` 기반)가 명확함.
- ServerCore가 네트워크 공통 계층으로 분리되어 확장성 좋음.
- Room job queue + timer flush 구조로 논리적 직렬화가 쉬움.

### 리스크/개선 포인트
- 메인 루프 busy spin:
  - `Program`의 `while(true) JobTimer.Flush();`는 CPU를 계속 소모.
- 엔드포인트 선택:
  - `AddressList[0]`는 IPv6/원치 않는 NIC가 선택될 수 있음.
- Unity 수신 필터:
  - `playerId == 1` 조건 때문에 대부분 메시지가 안 보일 수 있음.
- DummyClient 세션 정리:
  - 끊긴 세션이 리스트에 남아 예외 유발 가능.
- 프로토콜/도메인 분리 부족:
  - `C_Chat`, `S_Chat` 외 채팅 도메인 이벤트(입장/퇴장/닉네임/시각)가 없음.
- 런타임 경고:
  - 빌드 성공했지만 `netcoreapp3.1` EOL 경고가 반복됨.

## 11) 채팅 기능 구현 계획 (MVP -> 확장)

### Phase 1: 안정화 (우선)
- 서버 루프에 짧은 sleep 또는 이벤트 기반 대기 추가.
- 바인딩/접속 IP를 설정 파일로 분리(`127.0.0.1`/LAN 선택 가능).
- DummyClient에서 disconnected 세션 제거 로직 추가.
- Unity `S_ChatHandler` 필터 제거 후 수신 로그 전체 확인.

### Phase 2: 채팅 MVP
- 공통 패킷 프로젝트(또는 생성 코드)로 `Server/Unity/DummyClient` 스키마 단일화.
- 패킷 확장:
  - `C_Chat`에 `clientMsgId`(중복 방지/추적)
  - `S_Chat`에 `timestamp`, `senderName`.
- 서버 validation:
  - 빈 문자열/최대 길이 제한/금지어(선택).

### Phase 3: Unity UI 연결
- 입력창 + 전송 버튼 + 메시지 리스트 UI 생성.
- Unity 메인 스레드 디스패처를 통해 네트워크 수신 결과를 UI에 반영.
- 송신 API(`SendChat(string msg)`)를 `NetworkManager` 또는 별도 ChatService로 분리.

### Phase 4: 테스트
- 단일 접속: 송수신 정상.
- 다중 접속(더미 10~100): 메시지 유실/지연/예외 체크.
- 강제 종료/재접속: 세션 정리 누수 확인.
- 패킷 깨짐/잘못된 size: 서버 방어 동작 확인.

## 12) 바로 실행할 TODO (권장 순서)
1. Unity `S_ChatHandler` 조건 제거하고 수신 전부 로그 확인.
2. Server `Program` 루프 CPU 개선.
3. DummyClient에서 죽은 세션 제거.
4. 채팅 UI 최소 버전(입력/전송/리스트) 추가.
5. 공통 패킷 정의 단일 소스로 통합.

## 13) 알아보기 쉬운 최종 정리
- 지금 구조는 "네트워크 코어(ServerCore) + 서버 로직(Server) + 테스트 클라(DummyClient) + Unity 클라"로 이미 분리되어 기본 뼈대는 좋다.
- 현재도 채팅은 동작 가능한 상태지만, Unity에서 메시지 표시 조건과 서버 루프 CPU 사용 방식 때문에 체감 동작이 불안정할 수 있다.
- 먼저 안정화(루프/IP/세션정리) 후 Unity UI를 붙이면 빠르게 MVP 채팅을 완성할 수 있다.
- 이후 패킷 스키마 단일화와 검증 로직을 넣으면 유지보수 가능한 구조로 올라간다.

TODO
- 엔드포인트를 AWS로 설정하면 따로 서버를 활성화 시키지 않아도 되는거지?
- 맞다면 AWS 설정하는 방법을 자세히 알려줘
- 추후에는 AWS를 사용해서 채팅기능을 사용할 예정인데 Server, ServerCore, DummyClient를 그대로 내비두면 되나?

## 14) AWS 배포 체크리스트 (TODO 답변 반영)

### Q1. AWS 엔드포인트를 쓰면 로컬 서버를 안 켜도 되나?
- 결론: **로컬 서버는 안 켜도 되지만, AWS 서버 프로세스는 반드시 켜져 있어야 한다.**
- 즉, "엔드포인트만 바꾸면 자동 동작"이 아니라, AWS 인스턴스에서 `Server` 실행 상태를 유지해야 한다.

### Q2. AWS 설정 방법 (현재 TCP 7777 기준)
1. EC2 인스턴스 생성
   - 운영체제는 Linux(예: Ubuntu) 권장.
   - 퍼블릭 IP가 있는 서브넷에 배치.
2. 보안 그룹(Security Group) 인바운드 설정
   - `TCP 7777`: 테스트 단계는 `0.0.0.0/0`, 운영 단계는 허용 IP 대역 최소화.
   - `SSH 22`: 내 IP만 허용.
3. 고정 주소 확보
   - Elastic IP 생성 후 EC2에 연결.
4. 서버 빌드/배포
   - 로컬에서:
     - `dotnet publish Server/Server.csproj -c Release -r linux-x64 --self-contained true -o ./publish/server`
   - EC2에 업로드 후 실행 권한 부여(`chmod +x Server`) 후 실행.
5. 서버 바인딩 주소 확인
   - `Program.cs`에서 바인딩 시 `IPAddress.Any` 사용 권장.
   - 예시: `new IPEndPoint(IPAddress.Any, 7777)`
6. 클라이언트 접속 주소 변경
   - Unity `NetworkManager.cs`와 DummyClient `Program.cs`의 endpoint를 Elastic IP(또는 도메인)로 변경.
7. 백그라운드 상시 실행
   - `systemd` 서비스 등록으로 재부팅 후 자동 시작.
8. 검증
   - 로컬/외부망에서 실제 접속 테스트.
   - 로그로 연결/수신/브로드캐스트 확인.

### Q3. Server / ServerCore / DummyClient를 그대로 두면 되나?
- 결론: **역할 분리는 그대로 유지해도 된다.**
- 권장 운영 방식:
  - `Server + ServerCore`: 운영 배포 대상.
  - `DummyClient`: 부하 테스트/통합 테스트 용도로 계속 유지.
- 단, 운영 전 아래 3개는 반영 권장:
  1. `netcoreapp3.1` -> `net8.0` 이상 업그레이드(EOL 대응).
  2. 패킷 정의 단일화(`GenPackets` 공통 프로젝트/생성 파이프라인).
  3. 설정 분리(`appsettings.json` 또는 환경변수로 IP/Port/로그 레벨 관리).

## 15) AWS 전환 시 코드 수정 포인트
- Server: [Program.cs](D:/Unity/Unity RC Line/Unity-RC-Line-Tracking-Visual-Coding-Simulator/Server/Program.cs)
  - 바인딩 주소를 `AddressList[0]` 방식 대신 `IPAddress.Any`로 변경.
- Unity Client: [NetworkManager.cs](D:/Unity/Unity RC Line/Unity-RC-Line-Tracking-Visual-Coding-Simulator/RC Car/Assets/Scripts/Server/NetworkManager.cs)
  - 서버 주소를 DNS/Elastic IP로 지정 가능하게 상수/설정화.
- DummyClient: [Program.cs](D:/Unity/Unity RC Line/Unity-RC-Line-Tracking-Visual-Coding-Simulator/DummyClient/Program.cs)
  - 로컬 DNS 의존 제거 후 AWS endpoint 사용.
