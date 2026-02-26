using UnityEngine;
using UnityEngine.UI;

public class ChangeMap : MonoBehaviour
{
    [System.Serializable]
    public struct SpawnPose
    {
        public Vector3 position;
        public Vector3 rotation;
    }

    [Header("맵 설정")]
    [Tooltip("Plane에 순서대로 적용할 머테리얼 목록")]
    public Material[] mapMaterials;

    [Tooltip("맵 인덱스와 매칭되는 차량 시작 위치/회전 목록")]
    public SpawnPose[] carSpawnPoses;

    [Tooltip("시작 시 현재 맵을 적용하고 차량을 시작 위치로 이동")]
    public bool applyCurrentMapOnStart = true;

    [Header("참조 설정")]
    [Tooltip("머테리얼을 적용할 플레인 렌더러")]
    public Renderer planeRenderer;

    [Tooltip("RC카 트랜스폼")]
    public Transform carTransform;

    [Tooltip("차량 물리 제어 스크립트(비우면 자동 탐색)")]
    public VirtualCarPhysics carPhysics;

    [Tooltip("리스타트 기준 위치 동기화용 ButtonRestart")]
    public ButtonRestart buttonRestart;

    [Tooltip("맵 변경 버튼(비우면 현재 오브젝트 Button 자동 사용)")]
    public Button changeMapButton;

    [SerializeField]
    private int currentMapIndex = 0;

    public int CurrentMapIndex => currentMapIndex;

    void Start()
    {
        TryAutoFindReferences();

        if (changeMapButton == null)
        {
            changeMapButton = GetComponent<Button>();
        }

        if (changeMapButton != null)
        {
            changeMapButton.onClick.AddListener(ChangeToNextMap);
        }

        if (applyCurrentMapOnStart)
        {
            ApplyMap(currentMapIndex, true);
        }
    }

    void OnDestroy()
    {
        if (changeMapButton != null)
        {
            changeMapButton.onClick.RemoveListener(ChangeToNextMap);
        }
    }

    /// <summary>
    /// 다음 맵 머테리얼로 전환합니다.
    /// </summary>
    public void ChangeToNextMap()
    {
        if (mapMaterials == null || mapMaterials.Length == 0)
        {
            Debug.LogWarning("[ChangeMap] 머테리얼 목록이 비어 있습니다.");
            return;
        }

        int nextIndex = (currentMapIndex + 1) % mapMaterials.Length;
        ApplyMap(nextIndex, true);
    }

    /// <summary>
    /// 인덱스로 맵을 적용합니다.
    /// </summary>
    public void ApplyMap(int mapIndex, bool moveCarToSpawn)
    {
        if (mapMaterials == null || mapMaterials.Length == 0)
        {
            Debug.LogWarning("[ChangeMap] 머테리얼 목록이 비어 있어 맵을 적용할 수 없습니다.");
            return;
        }

        currentMapIndex = NormalizeIndex(mapIndex, mapMaterials.Length);
        ApplyCurrentMaterial();

        if (moveCarToSpawn)
        {
            MoveCarToCurrentSpawn();
        }

        SyncRestartInitialPosition();

        Debug.Log($"[ChangeMap] 맵 변경 완료 -> 인덱스: {currentMapIndex}");
    }

    /// <summary>
    /// 현재 맵의 스폰 위치/회전을 가져옵니다.
    /// </summary>
    public bool TryGetCurrentSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        if (TryGetSpawnPose(currentMapIndex, out position, out rotation))
        {
            return true;
        }

        if (carTransform != null)
        {
            position = carTransform.position;
            rotation = carTransform.rotation;
            return true;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    private void ApplyCurrentMaterial()
    {
        if (planeRenderer == null)
        {
            Debug.LogWarning("[ChangeMap] 플레인 렌더러가 비어 있어 머테리얼을 적용할 수 없습니다.");
            return;
        }

        planeRenderer.sharedMaterial = mapMaterials[currentMapIndex];
    }

    private void MoveCarToCurrentSpawn()
    {
        if (carTransform == null)
        {
            Debug.LogWarning("[ChangeMap] 차량 트랜스폼이 비어 있어 차량을 이동할 수 없습니다.");
            return;
        }

        if (!TryGetSpawnPose(currentMapIndex, out Vector3 spawnPosition, out Quaternion spawnRotation))
        {
            Debug.LogWarning($"[ChangeMap] 맵 인덱스 {currentMapIndex}에 스폰 위치가 설정되지 않았습니다.");
            return;
        }

        StopCarMotion();
        carTransform.SetPositionAndRotation(spawnPosition, spawnRotation);
    }

    private void StopCarMotion()
    {
        if (carPhysics != null)
        {
            carPhysics.StopRunning();
        }

        Rigidbody rb = carTransform != null ? carTransform.GetComponent<Rigidbody>() : null;
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void SyncRestartInitialPosition()
    {
        if (buttonRestart == null)
        {
            return;
        }

        if (TryGetCurrentSpawnPose(out Vector3 spawnPosition, out Quaternion spawnRotation))
        {
            buttonRestart.SetNewInitialPosition(spawnPosition, spawnRotation);
        }
    }

    private bool TryGetSpawnPose(int mapIndex, out Vector3 position, out Quaternion rotation)
    {
        if (carSpawnPoses != null &&
            mapIndex >= 0 &&
            mapIndex < carSpawnPoses.Length)
        {
            SpawnPose spawnPose = carSpawnPoses[mapIndex];
            position = spawnPose.position;
            rotation = Quaternion.Euler(spawnPose.rotation);
            return true;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    private void TryAutoFindReferences()
    {
        if (planeRenderer == null)
        {
            GameObject planeObj = GameObject.Find("Plane");
            if (planeObj != null)
            {
                planeRenderer = planeObj.GetComponent<Renderer>();
            }
        }

        if (carTransform == null)
        {
            GameObject carObj = GameObject.FindGameObjectWithTag("Car");
            if (carObj == null)
            {
                carObj = GameObject.Find("Car");
            }
            if (carObj != null)
            {
                carTransform = carObj.transform;
            }
        }

        if (carPhysics == null && carTransform != null)
        {
            carPhysics = carTransform.GetComponent<VirtualCarPhysics>();
        }

        if (carPhysics == null)
        {
            carPhysics = FindObjectOfType<VirtualCarPhysics>();
        }

        if (buttonRestart == null)
        {
            buttonRestart = FindObjectOfType<ButtonRestart>();
        }
    }

    private static int NormalizeIndex(int index, int length)
    {
        if (length <= 0)
        {
            return 0;
        }

        int normalized = index % length;
        return normalized < 0 ? normalized + length : normalized;
    }
}


