using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 현재 맵 기준 시작 위치로 RC카를 리스타트합니다.
/// </summary>
public class ButtonRestart : MonoBehaviour
{
    [Header("대상 설정")]
    [Tooltip("대상 차량 트랜스폼(비우면 태그/이름으로 자동 탐색)")]
    public Transform carTransform;

    [Header("옵션 참조")]
    [Tooltip("물리 제어 스크립트(비우면 자동 탐색)")]
    public VirtualCarPhysics carPhysics;

    [Tooltip("현재 맵과 리스타트 위치 동기화용 ChangeMap")]
    public ChangeMap changeMap;

    [Tooltip("활성화 시 리스타트 전에 현재 맵 스폰 위치로 기준값을 동기화")]
    public bool syncWithCurrentMapOnRestart = true;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool isInitialized = false;

    private Button restartButton;

    void Start()
    {
        TryAutoFindReferences();

        if (!SyncInitialStateFromMap())
        {
            SaveInitialState();
        }

        restartButton = GetComponent<Button>();
        if (restartButton != null)
        {
            restartButton.onClick.AddListener(RestartCar);
        }
    }

    void OnDestroy()
    {
        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(RestartCar);
        }
    }

    /// <summary>
    /// 현재 차량 위치/회전을 리스타트 기준값으로 저장합니다.
    /// </summary>
    public void SaveInitialState()
    {
        if (carTransform != null)
        {
            initialPosition = carTransform.position;
            initialRotation = carTransform.rotation;
            isInitialized = true;
            Debug.Log($"[ButtonRestart] 초기 상태 저장 - 위치: {initialPosition}, 회전: {initialRotation.eulerAngles}");
        }
        else
        {
            Debug.LogWarning("[ButtonRestart] 차량 트랜스폼을 찾지 못해 초기 상태를 저장할 수 없습니다.");
        }
    }

    /// <summary>
    /// 현재 맵 스폰 위치로 리스타트 기준값을 동기화합니다.
    /// </summary>
    public bool SyncInitialStateFromMap()
    {
        if (changeMap == null)
        {
            return false;
        }

        if (changeMap.TryGetCurrentSpawnPose(out Vector3 position, out Quaternion rotation))
        {
            SetNewInitialPosition(position, rotation);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 저장된 기준 위치로 차량을 리스타트합니다.
    /// </summary>
    public void RestartCar()
    {
        if (carTransform == null)
        {
            TryAutoFindReferences();
        }

        if (syncWithCurrentMapOnRestart)
        {
            SyncInitialStateFromMap();
        }

        if (!isInitialized)
        {
            Debug.LogWarning("[ButtonRestart] 초기 상태가 저장되지 않아 리스타트할 수 없습니다.");
            return;
        }

        if (carTransform == null)
        {
            Debug.LogWarning("[ButtonRestart] 차량 트랜스폼이 비어 있어 리스타트할 수 없습니다.");
            return;
        }

        if (carPhysics != null)
        {
            carPhysics.StopRunning();
        }

        Rigidbody rb = carTransform.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        carTransform.position = initialPosition;
        carTransform.rotation = initialRotation;

        Debug.Log($"[ButtonRestart] 차량 리스타트 완료 - 위치: {initialPosition}");
    }

    /// <summary>
    /// 외부에서 리스타트 기준 위치를 설정합니다.
    /// </summary>
    public void SetNewInitialPosition(Vector3 position, Quaternion rotation)
    {
        initialPosition = position;
        initialRotation = rotation;
        isInitialized = true;
        Debug.Log($"[ButtonRestart] 새 기준 위치 설정 - 위치: {position}");
    }

    private void TryAutoFindReferences()
    {
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

        if (changeMap == null)
        {
            changeMap = FindObjectOfType<ChangeMap>();
        }
    }
}



