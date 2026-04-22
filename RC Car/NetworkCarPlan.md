# NetworkCarPlan

## 0. Status
- Updated: 2026-04-22
- Scope: `03_NetworkCarTest` hybrid room flow
- Goal: keep Photon for realtime networking and restore API room/share flow for XML/JSON share, detail, and save

## 1. Final Direction
Use both room systems at the same time.

- Photon room:
  - realtime session
  - runner join approval
  - car ownership and RPC
- API chat room:
  - join request
  - join request decision
  - block share list/detail/save-to-my-level
- API user-level:
  - XML/JSON payload storage

## 2. Identity Rules
These ids must not be mixed.

- `PhotonSessionName`
  - Photon networking identity
- `ApiRoomId`
  - ChatRoom API room identity
- `UserLevelSeq`
  - XML/JSON payload identity

## 3. Why Photon-only Broke
Legacy API flow used one server room row.

1. Host called API `CreateRoom`
2. Server created `ApiRoomId`
3. Client/Host both used the same `ApiRoomId`
4. Share/list/detail/save all stayed on the same room resource

After Photon migration, Photon session creation existed but API room creation was skipped.

Result:
- Photon `SessionName` existed
- API `ApiRoomId` did not exist or was not bound
- client upload and host detail/list refresh did not point to the same server room resource

## 4. Implemented Hybrid Flow

### 4.1 Host Create
1. Host creates API chat room first
2. Host receives `ApiRoomId`
3. Host creates Photon room
4. Photon session properties publish `apiRoomId`
5. Local room context stores both:
   - `ApiRoomId`
   - `PhotonSessionName`

### 4.2 Client Join
1. Client reads Photon room list
2. Selected Photon room includes:
   - `SessionName`
   - `ApiRoomId`
3. Client sends API join request with `ApiRoomId`
4. Client joins Photon room with `SessionName`
5. After join succeeds, local room context stores both ids

### 4.3 Host Accept
1. Host accepts Photon join request
2. Host UI looks up matching API join request by `userId`
3. Host UI mirrors the same accept/reject decision to API

### 4.4 Client Share XML/JSON
1. Client saves XML/JSON to `/api/user-level`
2. Client gets `UserLevelSeq`
3. Client uploads block share with:
   - `ApiRoomId`
   - `UserLevelSeq`
4. Host list/detail/refresh uses the same `ApiRoomId`

## 5. Resolver Rule in Code
API functions must resolve room id by `ApiRoomId`.

Photon functions must resolve room id by `PhotonSessionName`.

Implemented helper:
- `NetworkRoomIdentity.ResolveApiRoomId(...)`
- `NetworkRoomIdentity.ResolvePhotonSessionName(...)`
- `NetworkRoomIdentity.ApplyRoomContext(...)`

## 6. Files Changed

### Core identity and Fusion
- `Assets/Scripts/Lobby/LobbyRoomContracts.cs`
- `Assets/Scripts/Lobby/RoomSessionContext.cs`
- `Assets/Scripts/Network/Fusion/FusionLobbyService.cs`
- `Assets/Scripts/Network/Fusion/FusionRoomInfo.cs`
- `Assets/Scripts/Network/Fusion/FusionRoomService.cs`
- `Assets/Scripts/Network/Fusion/FusionRoomSessionContext.cs`

### Host create / client join
- `Assets/Scripts/Lobby/LobbyUIController.cs`
- `Assets/Scripts/ChatRoom/But_RoomList.cs`

### Approval bridge
- `Assets/Scripts/ChatRoom/HostJoinRequestMonitorUI.cs`
- `Assets/Scripts/ChatRoom/HostJoinRequestMonitorGUI.cs`

### Share/list/detail/save API room rewiring
- `Assets/Scripts/ChatRoom/ClientBlockShareUploadButton.cs`
- `Assets/Scripts/ChatRoom/ClientBlockShareListPanel.cs`
- `Assets/Scripts/ChatRoom/HostBlockShareAutoRefreshPanel.cs`
- `Assets/Scripts/ChatRoom/HostBlockShareSaveToMyLevelButton.cs`
- `Assets/Scripts/ChatRoom/NetworkRoomRosterPanel.cs`
- `Assets/Scripts/NetworkCar/HostNetworkCarCoordinator.cs`

## 7. Important Behavioral Change
API room resolution no longer uses Photon `SessionName` as the first room key.

Now:
- Photon networking path uses `SessionName`
- API share/join/detail/list path uses `ApiRoomId`

This is the main fix.

## 8. Remaining Risk
If API join request arrives later than Photon accept timing, API mirror approval may need retry.

Current code already retries API request lookup briefly before giving up.

If more delay exists on production server, extend retry window in:
- `HostJoinRequestMonitorUI.FindPendingApiJoinRequestAsync(...)`

## 9. Verification
- Build command:
  - `dotnet build Assembly-CSharp.csproj -nologo`
- Result:
  - success
  - warnings only
  - errors `0`

## 10. Completed Work (2026-04-22)
- Implemented hybrid room creation: API room first, Photon room second
- Added `ApiRoomId` to Photon room metadata and Fusion session context
- Added shared room identity resolver for API room vs Photon session separation
- Rewired API share/list/detail/save code to use `ApiRoomId`
- Updated client Photon join flow to request API join before Photon join
- Updated host accept UI to mirror Photon approval to API join decision
- Cleaned this plan document to keep only the current architecture, flow, and completed work
