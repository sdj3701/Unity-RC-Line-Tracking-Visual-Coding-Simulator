# Arduino Plan (RC카 이동 코드 전체 점검)

작성일: 2026-03-10  
요청 조건 반영: 코드 수정 없이 문서 정리만 수행

## 1. 점검 범위와 결론

- RC카 이동에 관여하는 스크립트들을 전수 점검했다.
- `CreateBlock` 씬에서 실제로 연결되어 동작 중인 주행 파이프라인과, 프로젝트 내에 남아 있는 레거시/미사용 파이프라인이 공존한다.
- 현재 실제 주행은 `VirtualArduino` 계열(블록 실행기 + 가상 아두이노 + 모터드라이버 + 물리이동)로 동작한다.
- `RCCar`/`RCCarSensor`/`BlocksGenerated` 계열은 현재 씬 참조가 없어 사실상 미사용 상태다.
- `Assets/Scripts/Miro/Miro Algorithm.cs` 파일은 현재 프로젝트에 존재하지 않는다. 실제 존재 파일은 `Assets/Scripts/Miro/MiroAlgorithm.cs`이다.

## 2. 현재 실제 실행 흐름 (CreateBlock 씬 기준)

1. 블록 저장/불러오기 시 `BE2_UI_ContextMenuManager`가 XML 생성 후 JSON으로 변환한다.
2. 변환된 JSON을 `LatestRuntimeJson`에 보관하고 `OnCodeGenerated` 이벤트를 발행한다.
3. `BlockCodeExecutor`가 이벤트를 받아 프로그램을 다시 로드한다.
4. `VirtualArduinoMicro`도 같은 이벤트를 받아 변수 기반 핀 맵핑을 갱신한다.
5. 사용자가 UI 버튼으로 `VirtualCarPhysics.ToggleRunning()`을 호출하면 물리 루프가 시작된다.
6. `VirtualCarPhysics.FixedUpdate()`에서 매 틱 `BlockCodeExecutor.Tick()`을 실행한다.
7. `BlockCodeExecutor`의 `analogWrite` 노드는 `VirtualArduinoMicro.AnalogWrite()`로 전달된다.
8. `VirtualArduinoMicro`는 핀-기능 맵을 통해 `VirtualMotorDriver.OnFunctionWrite()`에 전달한다.
9. `VirtualMotorDriver`는 좌/우 모터 속도(-1~1)를 계산한다.
10. `VirtualCarPhysics`가 모터 속도를 읽어 `Rigidbody.MovePosition/MoveRotation`으로 차량을 이동시킨다.
11. 조건 분기/센서 읽기는 `VirtualLineSensor`를 통해 `FunctionDigitalRead/FunctionAnalogRead` 경로로 처리된다.

## 3. 파일별 상세 정리

### 3-1. 실제 주행 파이프라인 핵심 파일

| 파일 | 역할 | 핵심 포인트 |
|---|---|---|
| `Assets/Scripts/Core/VirtualArduino/VirtualCarPhysics.cs` | 실제 차량 물리 이동 적용 | `FixedUpdate()`에서 블록 실행 + 모터속도 기반 이동/회전 처리 |
| `Assets/Scripts/Core/VirtualArduino/BlockCodeExecutor.cs` | 런타임 JSON 실행기 | `init/loop/functions` 실행, `analogWrite/if/callFunction/wait` 처리 |
| `Assets/Scripts/Core/VirtualArduino/VirtualArduinoMicro.cs` | 핀/기능 라우팅 허브 | 핀 맵핑 갱신, peripheral 등록, `AnalogWrite`/`FunctionDigitalRead` 전달 |
| `Assets/Scripts/Core/VirtualArduino/VirtualMotorDriver.cs` | 모터 PWM -> 좌/우 속도 변환 | `leftMotorF/B`, `rightMotorF/B`를 -1~1 속도로 정규화 |
| `Assets/Scripts/Core/VirtualArduino/VirtualLineSensor.cs` | 라인센서 입력 생성 | Raycast + 텍스처 그레이샘플 기반 흑/백 판정 |
| `Assets/Scripts/Core/BE2XmlToRuntimeJson.cs` | 블록 XML -> 런타임 JSON 변환 | PWM/If/Call/Wait 등 노드를 JSON으로 직렬화 |
| `Assets/BlocksEngine2/Scripts/UI/ContextMenu/BE2_UI_ContextMenuManager.cs` | 저장/불러오기 + 실행기 갱신 트리거 | `LatestRuntimeJson` 갱신 + `OnCodeGenerated` 이벤트 발행 |

### 3-2. 주행 보조 파일 (직접 구동은 아니지만 주행 상태 제어)

| 파일 | 역할 | 핵심 포인트 |
|---|---|---|
| `Assets/Scripts/Map/ChangeMap.cs` | 맵 전환 + 차량 스폰 이동 | 맵 변경 시 `VirtualCarPhysics.StopRunning()` 후 위치 이동 |
| `Assets/Scripts/Player/ButtonRestart.cs` | 차량 리스타트 | 리스타트 시 `StopRunning()` + 리지드바디 속도 초기화 |

### 3-3. 도메인 분리 파일 (RC 주행과 직접 무관)

| 파일 | 판단 | 이유 |
|---|---|---|
| `Assets/Scripts/Miro/MiroAlgorithm.cs` | RC 주행 직접 무관 | 미로 데이터 생성용 알고리즘이며 모터/물리 이동 호출 없음 |

## 4. 씬 연결 확인 결과 (중요)

- `CreateBlock.unity`에서 `Car` 오브젝트에 아래 컴포넌트가 실제 연결되어 있다.
- `VirtualCarPhysics`, `BlockCodeExecutor`, `VirtualMotorDriver`, `VirtualLineSensor`, `VirtualArduinoMicro`
- UI 버튼 이벤트로 `VirtualCarPhysics.ToggleRunning`, `ChangeMap.ChangeToNextMap`, `ButtonRestart.RestartCar`가 연결되어 있다.
- 반면 `RCCar`, `RCCarSensor`, `BlocksGenerated`, `RCCarRuntimeAdapter`는 `CreateBlock.unity`에서 스크립트 참조가 발견되지 않았다.

## 5. 필요 없는 코드 정리 Plan (이유 포함)

### 5-1. 1순위 제거/격리 후보

| 대상 | 현재 상태 | 왜 필요 없는가 | 정리 계획 |
|---|---|---|---|
| `Assets/Scripts/Car/RCCar.cs` | 미사용 | 씬 참조 없음, 구형 `BlocksGenerated` 아키텍처 의존, 현재 `VirtualArduino` 경로와 중복 | 즉시 삭제 대신 `LegacyArchive`로 1차 이동 후 문제 없으면 제거 |
| `Assets/Scripts/Car/RCCarSensor.cs` | 미사용 | 씬 참조 없음, 구형 센서 주입 방식, 현재 `VirtualLineSensor`와 기능 중복 | `RCCar.cs`와 세트로 동일 처리 |
| `Assets/Generated/BlocksGenerated.cs` | 미사용(생성물) | 하드코딩된 핀/루프 로직, 현재 실행 체인에서 호출되지 않음 | 생성물 유지 필요 여부 결정 후 미사용이면 제거 |
| `Assets/Scripts/Core/Legacy/RCCarRuntimeAdapter.cs` | 사실상 사본 | 파일 전체가 주석 처리되어 컴파일/런타임 기능 없음 | 파일 자체 삭제 또는 `Legacy` 문서로 대체 |
| `Assets/Scripts/Core/Legacy/RuntimeBlocksRunner.cs` | 사실상 사본 | 파일 전체가 주석 처리되어 기능 없음 | 위와 동일 |

### 5-2. 2순위 정리 후보 (활성 파일 내부 불필요 코드)

| 대상 | 현재 상태 | 왜 정리 대상인가 | 정리 계획 |
|---|---|---|---|
| `VirtualArduinoMicro.SetupDefaultPinMapping()` | 호출 없음 | 동작 경로에서 사용되지 않는 dead method | 호출 지점 없으면 제거 |
| `VirtualArduinoMicro`의 주석 처리된 구형 API 묶음 | 대량 주석 코드 | 유지비만 증가, 실제 동작 경로와 분리됨 | 문서화 후 삭제 |
| `BlockCodeExecutor.ConvertToBoolean(string)` | 호출 없음 | 오버로드 중 string 버전 미사용 | 제거 |
| `BE2XmlToRuntimeJson.Export()`와 `ExportToString()`의 중복 로직 | 동일 처리 중복 | 유지보수 시 버그/수정 누락 가능성 증가 | 공통 내부 메서드로 단일화 계획 수립 |

### 5-3. 정리 작업 순서 (안전 중심)

1. 미사용 스크립트부터 분리(삭제 아님) 후 씬 실행/컴파일 검증.
2. 문제 없으면 완전 삭제.
3. 이후 활성 파일 내부 dead code 정리.
4. 마지막으로 변환기(`BE2XmlToRuntimeJson`) 중복 구조 단일화.

## 6. 앞으로 개선 사항 (아래)

1. 구조 단일화  
`VirtualArduino` 경로를 유일한 표준으로 확정하고, 레거시 경로를 문서와 코드에서 완전 분리.

2. JSON 처리 안정성 개선  
`BlockCodeExecutor`의 수동 문자열 파서를 표준 JSON 파서로 교체 계획 수립. 현재 방식은 포맷 변화에 취약.

3. 핀 맵핑 규칙 명확화  
`UpdatePinMappingFromVariables()`가 변수 “값”을 핀으로 해석하는 방식은 오탐 가능성이 있어, 변수명 규약 기반 맵핑으로 제한 필요.

4. 로그 부하 감소  
`FixedUpdate/Tick` 경로의 고빈도 `Debug.Log`는 런타임 성능에 영향이 커서 레벨 분리(개발/배포) 필요.

5. 주행 테스트 체크리스트 문서화  
`시작/정지`, `맵 변경`, `리스타트`, `센서 조건 분기`, `양측 센서 동시 true` 시나리오를 표준 테스트로 문서화.

6. 씬/스크립트 의존성 가시화  
“어떤 씬에서 어떤 스크립트를 쓰는지” 매트릭스를 문서로 관리해, 향후 미사용 코드 누적 방지.

## 7. 참고 메모

- 현재 프로젝트에서 RC카 이동 로직은 “블록 실행기 기반 물리 이동”과 “구형 직접 제어(미사용)”가 공존한다.
- 유지보수 비용과 버그 리스크를 줄이려면, 우선 레거시 코드 정리를 완료한 뒤 핵심 파이프라인만 남기는 것이 가장 효과적이다.


## 8. 센서/바퀴 배열 인덱스 고정 규칙 (헷갈림 방지)

아래 규칙을 "표준"으로 고정해서 사용한다.
이 문서 기준으로는 좌/우 인덱스를 절대 바꾸지 않는 것을 원칙으로 한다.

### 8-1. 최종 표준 매핑 (현재 코드 기준)

| 배열 | 인덱스 | 의미 | 비고 |
|---|---|---|---|
| `VirtualLineSensor.sensorObjects` | `0` | 왼쪽 센서 (`leftSensor`) | 1차 센서 |
| `VirtualLineSensor.sensorObjects` | `1` | 오른쪽 센서 (`rightSensor`) | 1차 센서 |
| `VirtualLineSensor.sensorObjects` | `2` | 왼쪽 보조 센서 (`leftSensor2`) | 선택 사용 |
| `VirtualLineSensor.sensorObjects` | `3` | 오른쪽 보조 센서 (`rightSensor2`) | 선택 사용 |
| `VirtualCarPhysics.wheels` | `0` | 왼쪽 바퀴 | 기본 2바퀴 구성 |
| `VirtualCarPhysics.wheels` | `1` | 오른쪽 바퀴 | 기본 2바퀴 구성 |

### 8-2. 코드에서 실제로 이렇게 해석됨

- `VirtualLineSensor`는 내부 상수로 `0=left`, `1=right`, `2=left2`, `3=right2`를 사용한다.
- `VirtualCarPhysics`는 바퀴 회전 시 `i % 2 == 0`이면 왼쪽 모터, `i % 2 == 1`이면 오른쪽 모터로 계산한다.
- 따라서 2개 바퀴 구성에서는 `wheels[0]=왼쪽`, `wheels[1]=오른쪽`이 맞다.

### 8-3. 회전(턴) 방향 해석 기준

`VirtualCarPhysics`에서 회전은 좌/우 모터 차이로 계산된다.

- 왼쪽 모터 > 오른쪽 모터: 차량이 왼쪽으로 도는 동작
- 오른쪽 모터 > 왼쪽 모터: 차량이 오른쪽으로 도는 동작
- 좌/우 모터 동일: 직진

즉, 센서/바퀴 인덱스가 바뀌면 턴 방향이 반대로 느껴질 수 있다.

### 8-4. 핀/기능과 좌우 대응 (참고)

기본 핀 매핑(`VirtualArduinoMicro`)은 다음과 같다.

- 왼쪽 센서: `leftSensor` -> Pin `3`
- 오른쪽 센서: `rightSensor` -> Pin `4`
- 왼쪽 모터 전/후진: `leftMotorF`/`leftMotorB` -> Pin `9`/`11`
- 오른쪽 모터 전/후진: `rightMotorF`/`rightMotorB` -> Pin `6`/`10`

### 8-5. Inspector 고정 체크리스트 (매번 플레이 전 확인)

1. `VirtualLineSensor.sensorObjects[0]`이 실제 왼쪽 센서 오브젝트인지 확인
2. `VirtualLineSensor.sensorObjects[1]`이 실제 오른쪽 센서 오브젝트인지 확인
3. `VirtualCarPhysics.wheels[0]`이 실제 왼쪽 바퀴 오브젝트인지 확인
4. `VirtualCarPhysics.wheels[1]`이 실제 오른쪽 바퀴 오브젝트인지 확인
5. 오브젝트 이름을 `Sensor_L`, `Sensor_R`, `Wheel_L`, `Wheel_R`처럼 명확하게 유지

### 8-6. 혼동 포인트 정리

- 활성 경로(`VirtualArduino`)와 구형 경로(`RCCar`, `RCCarSensor`, `BlocksGenerated`)를 동시에 보면서 비교하면 좌/우 규칙이 바뀌는 것처럼 보일 수 있다.
- 실제 `CreateBlock` 씬 주행은 `VirtualArduino` 경로가 기준이므로, 이 섹션의 표준 인덱스를 기준으로 판단한다.

## 9. 검은색 선 감지 시 반환값과 실제 동작 (추가)

### 9-1. 반환값 규칙 (VirtualLineSensor 기준)

`VirtualLineSensor.OnFunctionRead()`의 최종 반환은 `whiteMeansTrue` 설정에 따라 달라진다.

- 현재 기본/권장 설정: `whiteMeansTrue = true` (CreateBlock 씬도 이 값)
- 이 설정에서 검은선 감지 시 반환값:
  - DigitalRead(boolean): `false`
  - AnalogRead(float): `0f` (`OnFunctionAnalogRead`는 bool을 1/0으로 변환)

즉, 현재 프로젝트 기본값에서는 "검은선 = 0(false)", "흰색 = 1(true)"로 해석된다.

참고로 `whiteMeansTrue = false`로 바꾸면 반대로 동작한다.

- 검은선: `true` / `1f`
- 흰색: `false` / `0f`

### 9-2. 센서가 검은선에 닿았을 때 내부에서 일어나는 일

`VirtualLineSensor.SampleSensor()` 내부 흐름은 다음 순서다.

1. 센서 위치에서 Raycast 샘플링
   - 중앙 + 주변 샘플(`sampleRadius`, `extraSamples`)
   - 전방 예측 샘플(`forwardLookAheadDistance`, `forwardLookAheadSamples`)
2. 맞은 면의 텍스처/색상에서 그레이스케일 추출
3. `minGray <= blackThreshold`이면 `rawIsBlack = true`
4. `rawIsBlack = true`면 `blackUntilTime = 현재시각 + blackHoldSeconds` 저장
5. 잠깐 라인을 벗어나도 hold 시간 내면 `blackLatched = true` 유지
6. 최종 `isBlack = rawIsBlack || blackLatched`
7. 반환값 계산
   - `whiteMeansTrue = true`면 `return !isBlack`
   - `whiteMeansTrue = false`면 `return isBlack`

핵심: 검은선이 순간적으로 끊겨 보여도 `blackHoldSeconds` 때문에 짧은 시간은 검은선 감지가 유지될 수 있다.

### 9-3. 반환값이 BlockCodeExecutor에서 어떻게 사용되는지

현재 기본 설정(`whiteMeansTrue = true`) 기준:

1. `VirtualArduinoMicro.FunctionDigitalRead("leftSensor/rightSensor")`가 `false`를 받음
2. `BlockCodeExecutor.TryReadSensorAsInt()`에서 `false -> 0`으로 변환
3. `if/ifElse` 조건 비교:
   - `conditionValue = 0`이면 조건 참
   - `conditionValue = 1`이면 조건 거짓
4. 조건이 참인 body에서 `analogWrite`가 실행되면 모터 출력이 바뀌고 회전/주행 동작이 바뀜

추가로 중요한 점:

- `resetMotorOutputsToStopEachTick = true`이면 매 Tick 시작 시 모터를 기본 정지값으로 초기화한다.
- 따라서 해당 Tick에서 조건 body가 모터 출력을 다시 쓰지 않으면 차량은 정지 상태를 유지한다.

### 9-4. 빠른 판단표 (현재 기본값: whiteMeansTrue=true)

| 센서가 보는 색 | DigitalRead | AnalogRead | sensorAsInt | conditionValue=0 | conditionValue=1 |
|---|---|---|---|---|---|
| 검은색 선 | `false` | `0f` | `0` | 참(실행) | 거짓(미실행) |
| 흰색 바닥 | `true` | `1f` | `1` | 거짓(미실행) | 참(실행) |

### 9-5. 양쪽 센서 동시 처리 주의

`BlockCodeExecutor`의 "both sensors true" 번갈아 실행 로직은 말 그대로 두 센서 값이 모두 `true`일 때만 동작한다.

- 현재 기본 설정(`whiteMeansTrue=true`)에서는 "둘 다 흰색"일 때만 해당 로직이 발동
- "둘 다 검은색"은 둘 다 `false`이므로 해당 로직이 발동하지 않는다

이 부분이 체감 동작(회전 우선순위/분기 실행 순서)과 다르게 느껴질 수 있는 대표적인 원인이다.

## 10. `Sensor_right` 조건 분기 이슈 노트 (2026-03-10)

### 10-1. 증상
- 블록 패턴: `if (Sensor_right) { Right_turn(trun); }`
- 기대 동작: 우회전 (왼쪽 바퀴가 더 크게 구동)
- 실제 동작: 좌회전

### 10-2. 직접 원인
- 이 문제는 보통 `if` 조건식 자체가 원인이 아니다.
- 직접 원인은 `Right_turn` 내부의 모터 출력 조합이다.
- 생성 코드 기준으로 `Right_turn`은 현재 다음과 같이 출력한다.
- `pin_wheel_right_forward = Speed`
- `pin_wheel_left_forward = stop`
- 즉 오른쪽 바퀴만 전진하는 조합이라, 현재 차동 구동 해석에서 좌회전 동작이 나온다.

참고 위치:
- `Assets/Generated/BlocksGenerated.cs:18` (`Right_turn` 정의)
- `Assets/Generated/BlocksGenerated.cs:22` (`pin_wheel_right_forward`에 `Speed` 기록)
- `Assets/Generated/BlocksGenerated.cs:23` (`pin_wheel_left_forward`에 `stop` 기록)
- `Assets/Generated/BlocksGenerated.cs:77` (`if (digitalRead(Sensor_right))`)

### 10-3. 추가 점검사항
- `VirtualLineSensor`에서 `whiteMeansTrue = true`면 센서 `true`는 검은색이 아니라 흰색 의미다.
- 검은 선 감지 기준으로 분기하려면 `conditionValue = 0`을 사용하거나 `whiteMeansTrue` 설정을 변경해야 한다.
- 의미 혼선을 줄이려면 바퀴 핀 변수는 기본 매핑과 일치시킨다.
- `pin_wheel_left_forward = 9`
- `pin_wheel_left_back = 11`
- `pin_wheel_right_forward = 6`
- `pin_wheel_right_back = 10`

### 10-4. 결론
- 이 경우 먼저 고쳐야 할 위치는 `Sensor_right` 읽기 자체가 아니라 `Right_turn` 함수 본문이다.
- 함수 이름(`Right_turn`/`left_turn`)과 실제 PWM 출력 패턴이 같은 의미인지 반드시 검증해야 한다.
- float angular = (rightMotor - leftMotor) * maxAngularSpeed * Time.fixedDeltaTime;
- rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, angular, 0f)); 수정

