using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class But_RoomList : MonoBehaviour
{
    [SerializeField] private Button _button;
    [SerializeField] private Button _but_Cancel;
    [SerializeField] private Button _but_Confirm;
    [SerializeField] private GameObject _roomListPanel;
    [SerializeField] private Transform _roomListContentRoot;
    [SerializeField] private Toggle _roomTogglePrefab;
    [SerializeField] private ToggleGroup _toggleGroup;
    [SerializeField] private bool _moveToSceneOnConfirm = true;
    [SerializeField] private string _targetSceneName = "03_NetworkCarTest";
    [SerializeField] private bool _bindOnEnable = true;
    [SerializeField] private bool _clearPreviousItemsOnRefresh = true;
    [SerializeField] private bool _debugLog = true;

    private readonly List<Toggle> _spawnedToggles = new List<Toggle>();
    private readonly Dictionary<string, ChatRoomSummaryInfo> _roomMap = new Dictionary<string, ChatRoomSummaryInfo>();
    private ChatRoomManager _boundManager;

    public string SelectedRoomId { get; private set; }

    private void OnEnable()
    {
        if (_button == null)
            _button = GetComponent<Button>();

        if (_bindOnEnable && _button != null)
        {
            _button.onClick.RemoveListener(OnClickFetchRoomList);
            _button.onClick.AddListener(OnClickFetchRoomList);
        }

        if (_but_Cancel != null)
        {
            _but_Cancel.onClick.RemoveListener(OnClickCancel);
            _but_Cancel.onClick.AddListener(OnClickCancel);
        }

        if (_but_Confirm != null)
        {
            _but_Confirm.onClick.RemoveListener(OnClickConfirm);
            _but_Confirm.onClick.AddListener(OnClickConfirm);
        }

        TryBindManagerEvents();
        TryResolveContentRoot();
    }

    private void OnDisable()
    {
        if (_bindOnEnable && _button != null)
            _button.onClick.RemoveListener(OnClickFetchRoomList);

        if (_but_Cancel != null)
            _but_Cancel.onClick.RemoveListener(OnClickCancel);

        if (_but_Confirm != null)
            _but_Confirm.onClick.RemoveListener(OnClickConfirm);

        UnbindManagerEvents();
    }

    public void OnClickFetchRoomList()
    {
        TryBindManagerEvents();

        if (_boundManager == null)
        {
            if (_debugLog)
                Debug.LogWarning("[But_RoomList] ChatRoomManager.Instance is null.");
            return;
        }

        _boundManager.FetchRoomList();
    }

    public void OnClickCancel()
    {
        if (_roomListPanel != null)
            _roomListPanel.SetActive(false);
    }

    public void OnClickConfirm()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoomId))
        {
            if (_debugLog)
                Debug.LogWarning("[But_RoomList] No room is selected.");
            return;
        }

        ChatRoomSummaryInfo selectedRoom;
        if (!_roomMap.TryGetValue(SelectedRoomId, out selectedRoom))
            selectedRoom = new ChatRoomSummaryInfo { RoomId = SelectedRoomId, Title = string.Empty };

        RoomSessionContext.Set(new RoomInfo
        {
            RoomId = selectedRoom.RoomId,
            RoomName = selectedRoom.Title,
            HostUserId = selectedRoom.OwnerUserId,
            CreatedAtUtc = selectedRoom.CreatedAtUtc
        });

        if (_roomListPanel != null)
            _roomListPanel.SetActive(false);

        if (_moveToSceneOnConfirm && !string.IsNullOrWhiteSpace(_targetSceneName))
            SceneManager.LoadScene(_targetSceneName);
    }

    private void TryBindManagerEvents()
    {
        ChatRoomManager manager = ChatRoomManager.Instance;
        if (manager == null)
            return;

        if (_boundManager == manager)
            return;

        UnbindManagerEvents();

        _boundManager = manager;
        _boundManager.OnListSucceeded += HandleRoomListSucceeded;
    }

    private void UnbindManagerEvents()
    {
        if (_boundManager == null)
            return;

        _boundManager.OnListSucceeded -= HandleRoomListSucceeded;
        _boundManager = null;
    }

    private void TryResolveContentRoot()
    {
        if (_roomListContentRoot != null)
            return;

        if (_roomListPanel == null)
            return;

        ScrollRect scrollRect = _roomListPanel.GetComponentInChildren<ScrollRect>(true);
        if (scrollRect != null && scrollRect.content != null)
            _roomListContentRoot = scrollRect.content;
    }

    private void HandleRoomListSucceeded(ChatRoomSummaryInfo[] rooms)
    {
        TryResolveContentRoot();

        if (_roomListPanel != null && !_roomListPanel.activeSelf)
            _roomListPanel.SetActive(true);

        if (_roomTogglePrefab == null || _roomListContentRoot == null)
        {
            if (_debugLog)
                Debug.LogWarning("[But_RoomList] Toggle prefab/content root is not assigned.");
            return;
        }

        if (_clearPreviousItemsOnRefresh)
            ClearSpawnedToggles();
        else
            _roomMap.Clear();

        if (rooms == null || rooms.Length == 0)
            return;

        for (int i = 0; i < rooms.Length; i++)
        {
            ChatRoomSummaryInfo room = rooms[i];
            if (room == null || string.IsNullOrWhiteSpace(room.RoomId))
                continue;

            _roomMap[room.RoomId] = room;

            Toggle toggle = Instantiate(_roomTogglePrefab, _roomListContentRoot);
            toggle.isOn = false;

            if (_toggleGroup != null)
                toggle.group = _toggleGroup;

            string roomId = room.RoomId;
            string roomTitle = string.IsNullOrWhiteSpace(room.Title)
                ? $"Room {roomId}"
                : room.Title;

            SetToggleLabel(toggle, roomTitle);
            toggle.onValueChanged.AddListener(isOn => OnRoomToggleValueChanged(roomId, isOn));

            _spawnedToggles.Add(toggle);
        }

        if (_spawnedToggles.Count > 0)
            _spawnedToggles[0].isOn = true;
    }

    private void OnRoomToggleValueChanged(string roomId, bool isOn)
    {
        if (isOn)
        {
            SelectedRoomId = roomId;
            return;
        }

        if (SelectedRoomId == roomId)
            SelectedRoomId = null;
    }

    private void ClearSpawnedToggles()
    {
        for (int i = 0; i < _spawnedToggles.Count; i++)
        {
            if (_spawnedToggles[i] != null)
                Destroy(_spawnedToggles[i].gameObject);
        }

        _spawnedToggles.Clear();
        _roomMap.Clear();
        SelectedRoomId = null;
    }

    private static void SetToggleLabel(Toggle toggle, string label)
    {
        TMP_Text tmpText = toggle.GetComponentInChildren<TMP_Text>(true);
        if (tmpText != null)
        {
            tmpText.text = label;
            return;
        }

        Text legacyText = toggle.GetComponentInChildren<Text>(true);
        if (legacyText != null)
            legacyText.text = label;
    }
}
