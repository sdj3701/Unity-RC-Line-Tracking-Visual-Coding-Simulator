# Miro Algorithm Unity 적용 계획서

## 1) 문서 목적
- Roblox Lua로 작성된 미로 생성 로직을 Unity(C#) 환경으로 정확히 이식하기 위한 설계 문서다.
- 구현 코드는 포함하지 않고, 알고리즘 해석, 데이터 구조, Unity 적용 방법, 검증 기준을 상세히 정리한다.

## 2) 원본 Lua 코드 핵심 요약

### 2.1 파라미터와 데이터 의미
- `SIZE_X`, `SIZE_Y`, `SIZE_Z`:
  - 격자 셀 간격(월드 좌표 기준).
  - Unity 이식 시 라인 포인트 간 간격 계산 기준으로 사용된다.
- `MAZE_SIZE = 15`:
  - 미로 격자 크기(15x15).
  - 현재 로직은 홀수 크기를 전제로 동작한다.
- `MazeTextArray`:
  - 셀의 유형을 텍스트로 표현.
  - `"*"`: 정답 경로(시작에서 종료까지 최종 경로).
  - `"."`: 함정/분기 경로(최종 정답 경로에서 제외된 통로).
  - 값이 없는 칸(nil): 벽/비통로.
- `MazeVisitedArray`:
  - DFS 방문 여부 기록.
- 시작 좌표:
  - `(X1,Y1)=(2,1)`, `(X2,Y2)=(2,2)`를 시작 통로로 강제.
- 종료 관련 플래그:
  - `isMazeEnd`: 종료 지점 도달 여부.

### 2.2 생성 흐름
1. `SetMazeText()`에서 배열 초기화.
2. 시작점/입구 관련 셀을 `"*"` 및 방문 처리.
3. `subDFS(Y2,X2,Y1,X1)` 실행.
4. DFS 완료 후 `MazeTextArray`를 읽어 `"*"` 또는 `"."`인 칸만 통로 데이터로 확정.
5. Roblox 원본은 이 통로 데이터를 블록으로 시각화한다.

## 3) DFS 로직 심층 분석

### 3.1 이동 규칙(핵심)
- `dx/dy = {-2, +2, 0, 0} / {0, 0, -2, +2}`:
  - "다음 셀"은 현재 셀에서 2칸 떨어진 위치.
- `wx/wy = {-1, +1, 0, 0} / {0, 0, -1, +1}`:
  - 현재와 다음 셀 사이의 "중간 벽 셀" 위치.
- 의미:
  - 2칸 점프 구조로 "셀-벽-셀" 패턴을 유지하면서 미로를 판다.
  - 다음 셀이 미방문이면, 중간 벽 셀과 다음 셀을 통로로 전환한다.

### 3.2 방향 랜덤화 방식
- 4방향 순열 24개를 `arr`에 하드코딩.
- 각 DFS 호출마다 `num = random(24)` 하나를 뽑아, 해당 순열 순서대로 4방향 검사.
- 결과 특성:
  - 완전 랜덤 셔플과 유사하게 동작.
  - 호출 단위로 방향 우선순위가 고정되므로 재현 가능성(시드 고정 시) 확보가 쉽다.

### 3.3 경계/방문 조건
- 다음 셀 좌표가 내부 범위인지 검사:
  - `> 1` 그리고 `< MAZE_SIZE`
  - 즉, 외곽 경계는 직접 확장하지 않고 내부 공간 위주로 파고든다.
- 다음 셀이 미방문일 때만 진행.
- 진행 시 중간 벽 셀과 다음 셀 모두 방문 처리.

### 3.4 `"*"`와 `"."`가 결정되는 방식 (가장 중요한 부분)
- DFS 전개 중 기본은 새로 열린 경로를 `"."`으로 둔다.
- 단, 종료점 발견 전까지는 부모 경로를 `"*"`로 잠정 유지한다.
- 종료점 `(MAZE_SIZE-1, MAZE_SIZE-1)`에 도달하면:
  - 해당 지점까지 연결된 경로를 `"*"`로 확정.
  - `isMazeEnd = true`.
- 백트래킹 시 `isMazeEnd == false`인 분기는 `"."`으로 되돌린다.
- 결과:
  - 시작에서 종료까지 이어지는 단일 주 경로만 `"*"`.
  - 나머지 열린 통로는 `"."`.

### 3.5 종료점 처리 특징
- 종료 조건은 내부 셀 `(MAZE_SIZE-1, MAZE_SIZE-1)` 도달.
- 초기화에서 `(MAZE_SIZE, MAZE_SIZE-1)`도 `"*"`로 미리 지정됨.
- 따라서 내부 종료 셀에서 외곽 쪽으로 한 칸 더 연결된 출구 형태를 의도한 구조다.

## 4) Unity 적용 시 동등성 기준

### 4.1 반드시 동일하게 맞출 요소
- 2칸 점프 DFS 규칙.
- 중간 벽 셀 + 다음 셀 동시 개방.
- 시작/종료 좌표 체계.
- `"*"`(정답)와 `"."`(분기) 분류 로직.
- `"*"`/ `"."` 셀만 라인으로 시각화하는 출력 정책.

### 4.2 Unity에서 의도적으로 개선 가능한 요소
- 재생성 시 기존 오브젝트 정리 자동화.
- 난수 시드 옵션(재현 가능한 미로 디버깅).
- 재귀 DFS를 반복형(스택 기반)으로 바꿔 대형 미로에서 안정성 확보.
- 데이터와 렌더링 분리(테스트 가능성 향상).
- RC카 센서 호환을 위한 라인 두께/색상/충돌체 정책 명시.

## 5) Unity 설계안 (코드 없이 구조만)

### 5.1 책임 분리
- `MiroAlgorithm`(생성기):
  - 미로 텍스트/방문 배열 생성.
  - DFS 실행.
  - 최종 결과(셀 타입 맵) 반환.
- `MiroRenderer`(렌더러):
  - 셀 타입 맵을 받아 라인 경로로 변환해 렌더링.
  - 센서 감지가 가능한 표면(렌더러 + 콜라이더)을 보장.
- `MiroConfig`(설정 데이터):
  - `mazeSize`, `cellStep`, `lineWidth`, `seed`, 시작/종료 좌표, 부모 Transform 등.

### 5.2 배열 표현 권장
- Lua 1-based 인덱스 -> C# 0-based 인덱스로 변환 필요.
- 문서 기준 권장:
  - 내부 계산은 0-based로 통일.
  - 기존 Lua 좌표와 비교 검증 시 변환표를 별도로 유지.
- 셀 상태 권장 분류:
  - 벽(미개방), 정답 통로, 분기 통로.

### 5.3 좌표 매핑 규칙
- Lua는 `Position = Base + (SIZE_X*jj, 0, SIZE_Z*ii)` 형태.
- Unity도 동일 개념으로:
  - 그리드 X는 월드 X로,
  - 그리드 Y(행)는 월드 Z로 매핑.
- 주의:
  - Lua 인덱스가 1부터 시작했으므로, Unity에서 동일 배치를 원하면 오프셋 보정이 필요.

### 5.4 라인/시각화 정책
- Unity 출력은 블록이 아니라 라인으로 고정한다.
- `VirtualLineSensor` 기준으로 라인은 다음 조건을 만족해야 한다.
  - Raycast에 맞는 콜라이더가 있어야 한다.
  - 히트된 오브젝트에서 `Renderer.sharedMaterial`을 읽을 수 있어야 한다.
  - 최종 grayscale 값이 `blackThreshold` 이하가 되도록 라인을 어둡게 설정해야 한다.
- `"*"`와 `"."`는 의미상 구분하되, 센서용 주행 라인은 `*` 기준으로 우선 렌더링한다.
- `"."` 시각화가 필요하면 디버그용 보조 라인으로 분리한다.

### 5.5 머티리얼 에셋이 없는 경우 대응
- 가능하다. 단, "머티리얼 파일이 없음"과 "머티리얼 자체가 없음"은 다르다.
- 센서 코드 특성상 최종적으로는 런타임 머티리얼 인스턴스가 필요하다.
- 권장 방식:
  - 런타임에 기본 셰이더로 머티리얼을 생성하고 라인/바닥에 할당.
  - 배경은 밝게(흰색), 라인은 어둡게(검정) 설정해 센서 임계값을 안정적으로 통과.
- 머티리얼 에셋을 프로젝트에 미리 만들지 않아도 동작 가능하다.

## 6) 실제 이식 절차 (작업 순서)

1. 데이터 모델 확정
   - 셀 상태 정의(벽/정답/분기), 방문 배열, 설정 구조.
2. 인덱스 규칙 확정
   - Lua 좌표를 기준으로 C# 변환표 작성.
3. DFS 로직 이식
   - 방향 24순열, 2칸 점프, 중간 셀 개방, 종료 플래그 처리.
4. 결과 검증(텍스트)
   - 생성 직후 셀 맵을 로그/에디터 시각화로 확인.
5. 렌더링 연결
   - 셀 맵을 라인 경로로 변환하고 센서 감지 가능한 표면으로 생성.
6. 재생성 워크플로우
   - 기존 생성 오브젝트 정리 후 새로 생성.
7. 디버그 옵션 추가
   - seed 고정, 단계별 생성 확인, 통계 출력.
8. 센서 튜닝
   - 라인 색상, `blackThreshold`, `rayDistance`, `sensorMask`를 함께 튜닝.

## 7) 검증 계획

### 7.1 기능 검증
- 시작 셀과 종료 셀이 항상 통로인지 확인.
- `"*"` 경로가 시작에서 종료까지 연속 연결되는지 확인.
- `"."` 경로는 존재하되 정답 경로 외 분기인지 확인.
- 미로 경계 밖 접근이 없는지 확인.

### 7.2 시각 검증
- 라인 폭(`lineWidth`)이 RC카 센서 간격 대비 충분한지 확인.
- 라인 좌표 간격이 `SIZE_X`, `SIZE_Z` 기반 계산과 일치하는지 확인.
- 월드 기준 회전/스케일/부모 계층이 의도대로인지 확인.

### 7.3 센서 호환 검증
- 센서 Raycast가 라인 표면을 안정적으로 히트하는지 확인.
- 라인 grayscale이 `blackThreshold` 이하로 판정되는지 확인.
- 코너 구간에서 `forwardLookAheadDistance` 적용 시 이탈이 줄어드는지 확인.

### 7.4 안정성/성능 검증
- `MAZE_SIZE` 증가 시 생성 시간 측정.
- 재귀 깊이 한계(큰 맵) 확인.
- 반복 생성 시 메모리 누수/중복 오브젝트 점검.

## 8) 예상 리스크와 대응

- 인덱스 오프바이원 오류:
  - 1-based/0-based 변환표와 단위 테스트로 방지.
- 종료 경로 마킹 오류:
  - `isMazeEnd` 상태 전환 시점 로그로 추적.
- 대형 맵 재귀 스택 한계:
  - 필요 시 반복형 DFS로 전환.
- 랜덤성 차이로 결과 불일치:
  - 동일 시드 기준으로 Lua와 단계 비교.
- 센서 미감지 리스크:
  - 라인 오브젝트에 콜라이더/머티리얼/충분한 명암 대비를 강제 체크.

## 9) 프로젝트 반영 포인트
- 현재 `Assets/Scripts/Miro/Miro Algorithm.cs`는 비어 있으므로, 이 문서 기준으로 생성기 책임을 먼저 채우는 방식이 적합하다.
- 권장 우선순위:
  - 1차: Lua와 동등한 결과 재현.
  - 2차: 구조 분리(생성/렌더링/설정).
  - 3차: 디버그/성능 개선.

## 10) 완료 기준(Definition of Done)
- 동일 설정값에서 미로 생성 결과가 Lua 의도와 논리적으로 일치한다.
- 시작~종료 정답 경로(`"*"` 의미)가 명확히 분리된다.
- Unity 씬에서 블록 대신 라인 경로가 생성되고 RC카 센서가 이를 안정적으로 감지한다.
- 재생성/확장 시에도 구조가 유지되며 테스트 가능하다.

## 11) 추가 변경사항 요청
- 현제 로블록스에서는 블록으로 대체로 했는데 유니티에서는 블록이 아니라 선으로 변경해야함
  - RC카를 움직일때 센서가 선을 보고 움직이기 때문에 선으로 변경
- 머테리얼로 사용할게 없는데 구현이 가능하지?
- Miro Test Scene에서 작업을 할 생각이야 버튼을 누르면 미로를 생성하고 이것을 실시간으로 저장하는 방법도 있을까?

### 11.1 요청 반영 결과
- 계획서 전체를 블록 기반 출력에서 라인 기반 출력으로 수정 완료.
- 센서 코드(`VirtualLineSensor`) 기준 필수 조건(콜라이더 + 렌더러 + 머티리얼 읽기 가능)을 반영.
- 머티리얼 에셋이 없어도 런타임 생성 방식으로 구현 가능하도록 계획 반영.

12) 코드 생성할 때 주의사항
- 주석 및 디버그 한글 작성시 UTF-8이나 UTF-16을 사용해서 한글이 깨지지 않도록 주의
- 코드 함수마다 어떤 기능을 하는 함수인지 주석을 상세히 적어줘
- 코드 작업을 완료했으면 plan에 완료한 작업 체크 표시
- 수정 요청시 어디에서 문제가 있는지 예상하고 코드 작업을 하지말고 plan에 작성

## 13) 코드 작업 완료 체크
- [x] Lua 동등 DFS 생성기 구현 (`MiroAlgorithm`)
- [x] 직렬화 가능한 미로 데이터 모델 구현 (`MiroMazeData`, `MiroCellType`)
- [x] 라인 기반 렌더러 구현 (`MiroLineRenderer`)
- [x] JSON 저장/불러오기 구현 (`MiroMazePersistence`, `persistentDataPath`)
- [x] Test Scene 버튼 워크플로우 구현 (`MiroTestSceneController`: `Generate/Save/Load/Clear`)
- [x] `Generate` 직후 자동 저장 흐름 반영 (`autoSaveOnGenerate`)
- [x] 코드 컴파일 검증 완료 (`dotnet build Assembly-CSharp.csproj`, 오류 0)
- [x] 사용 방법 가이드 문서화
- [x] `Generate` 버튼 연속 랜덤 재생성(토글) 기능 구현

## 14) 사용 방법 (Miro Test Scene)

### 14.1 씬 기본 세팅
1. `Miro Test Scene`을 연다.
2. 빈 GameObject를 만들고 이름을 `MiroSystem`으로 지정한다.
3. `MiroSystem`에 아래 스크립트를 모두 추가한다.
   - `MiroAlgorithm`
   - `MiroLineRenderer`
   - `MiroMazePersistence`
   - `MiroTestSceneController`
4. `MiroTestSceneController`의 참조 필드를 같은 오브젝트의 컴포넌트로 연결한다.
   - `algorithm` -> `MiroAlgorithm`
   - `lineRenderer` -> `MiroLineRenderer`
   - `persistence` -> `MiroMazePersistence`

### 14.2 Inspector 권장 초기값
- `MiroAlgorithm`
  - `mazeSize = 15`
  - `cellStepX = 5`, `cellStepZ = 5`
  - 재현 테스트 시 `useFixedSeed = true`, `fixedSeed = 12345`
- `MiroLineRenderer`
  - `lineWidth = 0.75` (센서 폭보다 충분히 크게 시작)
  - `lineThickness = 0.03`
  - `renderMainPath = true`
  - `renderBranchPath = false` (처음에는 정답 라인만 렌더링)
  - `createGroundPlane = true`, `groundColor = white`, `mainPathColor = black`
- `MiroMazePersistence`
  - `fileName = miro_latest.json`
- `MiroTestSceneController`
  - `autoSaveOnGenerate = true`
  - `autoLoadOnStart = false` (원하면 true)

### 14.3 UI 버튼 연결 방법
1. Canvas에 버튼 4개를 만든다.
   - `Generate`
   - `Save`
   - `Load`
   - `Clear`
2. 각 버튼의 `OnClick()`에 `MiroSystem` 오브젝트를 등록한다.
3. 함수 연결:
   - `Generate` 버튼 -> `MiroTestSceneController.Generate`
   - `Save` 버튼 -> `MiroTestSceneController.SaveCurrent`
   - `Load` 버튼 -> `MiroTestSceneController.LoadLatest`
   - `Clear` 버튼 -> `MiroTestSceneController.ClearLines`
4. 현재 기본 동작:
   - `continuousRandomOnGenerate = true`이면 `Generate`를 누르는 순간 미로가 주기적으로 계속 바뀐다.
   - 자동 생성 중 `Generate`를 다시 누르면 정지한다(`generateButtonTogglesAuto = true`일 때).

### 14.4 실행 순서
1. 플레이 모드에서 `Generate`를 누른다.
2. 미로가 `autoGenerateIntervalSeconds` 간격으로 계속 랜덤 변경되는지 확인한다.
3. 정지하려면 `Generate`를 한 번 더 누른다(토글 모드 기준).
4. `autoSaveOnGenerate = true`이고 `saveEachAutoGeneration = true`면 매 틱 저장된다.
5. `Clear`를 누른 뒤 `Load`를 눌러 저장본이 복원되는지 확인한다.

### 14.5 저장 파일 위치 확인
- 저장 경로는 `Application.persistentDataPath` 기준이다.
- 파일명은 `MiroMazePersistence.fileName` 값 사용.
- 기본 파일명 기준 실제 파일: `<persistentDataPath>/miro_latest.json`

### 14.6 RC카 센서 연동 체크
1. `VirtualLineSensor`의 `sensorMask`에 라인 레이어가 포함되어 있는지 확인한다.
2. 센서가 라인을 못 읽으면 아래 순서로 조정한다.
   - `MiroLineRenderer.lineWidth` 증가
   - 라인 색상을 더 검정에 가깝게 조정
   - `VirtualLineSensor.blackThreshold` 소폭 상향(예: 0.2 -> 0.25)
3. 배경은 밝게, 라인은 어둡게 유지해 명암 대비를 확보한다.

### 14.7 자주 발생하는 문제와 빠른 확인
- `Generate`를 눌러도 안 보임:
  - `MiroTestSceneController` 참조 3개가 비어있는지 확인.
  - 카메라 위치/스케일 확인.
- `Load`가 동작 안 함:
  - 저장 파일이 먼저 생성되었는지 확인(`Generate` 또는 `Save` 선행 필요).
- 센서가 라인을 감지 못함:
  - 라인 오브젝트 Collider 존재 여부 확인.
  - 라인 색상/임계값(`blackThreshold`) 재조정.
