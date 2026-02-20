# RC Car 프로젝트 인수인계 문서

작성일: 2026-02-19  
프로젝트 경로: `RC Car`

## 1. 프로젝트 한 줄 요약

BlocksEngine2로 만든 블록 코드를 런타임 JSON으로 변환하고, 가상 Arduino(센서/모터)로 RC카 라인트레이싱을 시뮬레이션하는 Unity 프로젝트입니다.

## 2. 개발 환경

- Unity: `2021.3.45f1` (`ProjectSettings/ProjectVersion.txt:1`)
- Product/Company: `RC Car` / `DefaultCompany` (`ProjectSettings/ProjectSettings.asset:15`)
- 주요 패키지: URP, TextMeshPro, uGUI, VisualScripting (`Packages/manifest.json`)

## 3. 씬 구성과 현재 진입 구조

- 인증 씬: `Assets/Scenes/TestLogin.unity`
- 메인 작업 씬: `Assets/Scenes/TestCreateBlock.unity`
- 주행 전용 씬: `Assets/Scenes/Car.unity`
- 현재 Build Settings 등록 씬: `Assets/Scenes/TestCreateBlock.unity` 1개만 등록 (`ProjectSettings/EditorBuildSettings.asset:7`)

참고:
- `TestLogin`에는 `AuthTokenReceiver + AuthManager`가 배치됨 (`Assets/Scenes/TestLogin.unity:399`)
- `TestCreateBlock`과 `Car` 둘 다 신형 실행 체인(`VirtualCarPhysics/BlockCodeExecutor/VirtualArduinoMicro`)을 사용함 (`Assets/Scenes/TestCreateBlock.unity:62428`, `Assets/Scenes/Car.unity:255`)

## 4. 핵심 실행 아키텍처

### 4.1 인증(Auth)

- URL Scheme 등록/처리: `rccar://...` (`Assets/Scripts/Auth/ProtocolRegistrar.cs:19`, `Assets/Scripts/Auth/AuthTokenReceiver.cs:67`)
- 토큰 파싱 후 인증 호출: `AuthManager.AuthenticateWithToken` (`Assets/Scripts/Auth/AuthTokenReceiver.cs:89`)
- 서버 토큰 검증 API 호출: `AuthManager.ValidateTokenWithServer` (`Assets/Scripts/Auth/AuthManager.cs:156`)
- 성공 시 사용자 상태 저장 + 게임 씬 로드 (`Assets/Scripts/Auth/AuthManager.cs:120`)
- 로컬 토큰 저장: `PlayerPrefs(auth_access_token/auth_refresh_token)` (`Assets/Scripts/Auth/AuthManager.cs:211`)

### 4.2 블록 코드 저장/로드

- 저장 진입점: `BE2_UI_ContextMenuManager.SaveCodeWithNameAsync` (`Assets/BlocksEngine2/Scripts/UI/ContextMenu/BE2_UI_ContextMenuManager.cs:122`)
- XML 생성 후 JSON 변환: `BE2XmlToRuntimeJson.ExportToString` (`Assets/BlocksEngine2/Scripts/UI/ContextMenu/BE2_UI_ContextMenuManager.cs:148`)
- 저장소 게이트웨이: `BE2_CodeStorageManager` (`Assets/BlocksEngine2/Scripts/Storage/BE2_CodeStorageManager.cs:10`)
- 로컬 저장소: `LocalStorageProvider` (`Assets/BlocksEngine2/Scripts/Storage/LocalStorageProvider.cs:14`)
- 로컬 저장 시 `SavedCodes/*.xml,json` + 실행용 `BlocksRuntime.xml/json` 동시 갱신 (`Assets/BlocksEngine2/Scripts/Storage/LocalStorageProvider.cs:45`)

### 4.3 주행 실행

- 런타임 JSON 로드/실행: `BlockCodeExecutor.LoadProgram/Tick` (`Assets/Scripts/Core/VirtualArduino/BlockCodeExecutor.cs:88`, `Assets/Scripts/Core/VirtualArduino/BlockCodeExecutor.cs:123`)
- 센서/모터 핀 라우팅: `VirtualArduinoMicro` (`Assets/Scripts/Core/VirtualArduino/VirtualArduinoMicro.cs:100`)
- 모터 PWM -> 좌/우 속도 변환: `VirtualMotorDriver.OnFunctionWrite` (`Assets/Scripts/Core/VirtualArduino/VirtualMotorDriver.cs:36`)
- 물리 적용: `VirtualCarPhysics.FixedUpdate` (`Assets/Scripts/Core/VirtualArduino/VirtualCarPhysics.cs:110`)
- 센서 판독: `VirtualLineSensor.SampleSensor` (`Assets/Scripts/Core/VirtualArduino/VirtualLineSensor.cs:64`)

### 4.4 UI 제어

- 실행 토글 버튼은 `ToggleRunning`을 호출 (`Assets/Scenes/TestCreateBlock.unity:22751`)
- 리셋 버튼은 `ButtonRestart.RestartCar` 호출 (`Assets/Scenes/TestCreateBlock.unity:115442`)
- 핀 표시 UI는 `PinMappingVisualizer` 사용 (`Assets/Scenes/TestCreateBlock.unity:101441`)

## 5. 데이터 저장 위치

- `Application.persistentDataPath/SavedCodes/{파일명}.xml`
- `Application.persistentDataPath/SavedCodes/{파일명}.json`
- `Application.persistentDataPath/BlocksRuntime.xml`
- `Application.persistentDataPath/BlocksRuntime.json`

근거: `Assets/BlocksEngine2/Scripts/Storage/LocalStorageProvider.cs:20`, `Assets/BlocksEngine2/Scripts/Storage/LocalStorageProvider.cs:62`

## 6. 현재 리스크/주의사항

1. 인증 테스트 토큰 하드코딩
- 코드에 JWT가 하드코딩되어 있음 (`Assets/Scripts/Auth/AuthManager.cs:36`)
- 씬 직렬화에도 저장됨 (`Assets/Scenes/TestLogin.unity:417`)

2. 인증 API가 HTTP
- `_serverBaseUrl`이 `http://...` 사용 중 (`Assets/Scripts/Auth/AuthManager.cs:21`)

3. 원격 저장 API 기본값 미설정
- `DatabaseStorageProvider` URL이 `YOUR_SERVER_HOST` 플레이스홀더 상태 (`Assets/BlocksEngine2/Scripts/Storage/DatabaseStorageProvider.cs:21`)
- 미설정이면 로컬 저장 폴백 동작
- `BE2_CodeStorageManager` 기본값이 원격 저장 활성화라서 초기에는 원격 시도 후 폴백이 발생함 (`Assets/BlocksEngine2/Scripts/Storage/BE2_CodeStorageManager.cs:29`)

4. 구형/미사용 코드 공존
- `Assets/Generated/BlocksGenerated.cs`
- `Assets/Scripts/Car/RCCarSensor.cs`
- `Assets/Scripts/Core/Legacy/*`

5. 디버그 로그 과다
- `BlockCodeExecutor`, `VirtualArduinoMicro`, `VirtualCarPhysics`에 프레임 단위 로그가 많아 성능 저하 가능 (`Assets/Scripts/Core/VirtualArduino/BlockCodeExecutor.cs:141`, `Assets/Scripts/Core/VirtualArduino/VirtualCarPhysics.cs:133`)

6. Build Settings 미정리
- 실제 운영 씬 흐름 대비 빌드 등록 씬이 1개뿐임 (`ProjectSettings/EditorBuildSettings.asset:7`)

7. Car 씬 버튼 직렬화 잔재
- `But_Code` 버튼에 `RuntimeBlocksRunner` 정적 바인딩 흔적(메서드 비어 있음)이 남아 있음 (`Assets/Scenes/Car.unity:2286`)
- 실제 동작은 `LoadBlockCodeSceneButton`의 `OnEnable`에서 런타임 리스너를 다시 붙여 처리함 (`Assets/Scripts/LoadBlockCodeSceneButton.cs:11`)

## 7. 빠른 운영 체크리스트

1. `TestLogin` 실행 후 인증 성공 시 `TestCreateBlock` 진입 확인
2. 블록 저장 후 `persistentDataPath/BlocksRuntime.json` 갱신 확인
3. `TestCreateBlock`에서 실행 버튼으로 `ToggleRunning` 동작 확인
4. 리셋 버튼으로 위치/속도 초기화 확인
5. 핀 매핑 UI 라벨 색상 변경 확인

## 8. 다음 담당자 첫 작업 권장 순서

1. 인증 토큰/URL 외부 설정화
- 하드코딩 제거, 환경 분리(Editor/Dev/Prod)

2. Build Settings 정리
- `TestLogin -> TestCreateBlock -> Car` 흐름에 맞게 등록

3. 구형 코드 정리
- Legacy/Generated 경로 사용 여부 확정 후 제거 또는 문서화

4. 로그 레벨 제어 추가
- `showDebugLogs` 플래그를 주행/아두이노 파트 전체에 일관 적용
