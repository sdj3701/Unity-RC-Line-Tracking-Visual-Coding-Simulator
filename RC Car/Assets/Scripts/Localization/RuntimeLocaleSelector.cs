using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-500)]
public class RuntimeLocaleSelector : MonoBehaviour
{
    public static RuntimeLocaleSelector Instance { get; private set; }

    [Header("UI (Optional)")]
    [Tooltip("Preferred dropdown component.")]
    [SerializeField] private TMP_Dropdown _tmpDropdown;

    [Tooltip("Fallback dropdown component if TMP is not used.")]
    [SerializeField] private Dropdown _legacyDropdown;

    [Header("Persistence")]
    [SerializeField] private bool _dontDestroyOnLoad = true;
    [SerializeField] private bool _applySavedLocaleOnStart = true;
    [SerializeField] private string _playerPrefsKey = "app.locale.code";

    [Header("Debug")]
    [SerializeField] private bool _debugLog;

    private readonly List<Locale> _locales = new List<Locale>();
    private bool _isInitialized;
    private bool _isUpdatingDropdown;
    private string _pendingLocaleCode;
    private GameObject _persistenceTarget;

    public bool IsInitialized => _isInitialized;

    public IReadOnlyList<Locale> Locales => _locales;

    public string CurrentLocaleCode
    {
        get
        {
            Locale locale = LocalizationSettings.SelectedLocale;
            return locale == null ? string.Empty : locale.Identifier.Code;
        }
    }

    private void Awake()
    {
        if (_tmpDropdown == null)
            _tmpDropdown = GetComponent<TMP_Dropdown>();

        if (_legacyDropdown == null)
            _legacyDropdown = GetComponent<Dropdown>();

        _persistenceTarget = ResolvePersistenceTarget();

        if (Instance != null && Instance != this)
        {
            if (_tmpDropdown != null)
                _tmpDropdown.gameObject.SetActive(false);

            if (_legacyDropdown != null)
                _legacyDropdown.gameObject.SetActive(false);

            Destroy(this);
            return;
        }

        Instance = this;

        if (_dontDestroyOnLoad)
        {
            DontDestroyOnLoad(gameObject);

            if (_persistenceTarget != null && _persistenceTarget != gameObject)
                DontDestroyOnLoad(_persistenceTarget);
        }
    }

    private void OnEnable()
    {
        if (_tmpDropdown != null)
            _tmpDropdown.onValueChanged.AddListener(OnDropdownValueChanged);

        if (_legacyDropdown != null)
            _legacyDropdown.onValueChanged.AddListener(OnDropdownValueChanged);

        LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
    }

    private void Start()
    {
        StartCoroutine(InitializeRoutine());
    }

    private IEnumerator InitializeRoutine()
    {
        yield return LocalizationSettings.InitializationOperation;

        CacheLocales();

        if (!string.IsNullOrWhiteSpace(_pendingLocaleCode))
        {
            ApplyLocaleByCodeInternal(_pendingLocaleCode, saveToPrefs: true);
            _pendingLocaleCode = null;
        }
        else if (_applySavedLocaleOnStart)
        {
            string savedCode = PlayerPrefs.GetString(_playerPrefsKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(savedCode))
                ApplyLocaleByCodeInternal(savedCode, saveToPrefs: false);
        }

        RebuildDropdown();
        UpdateDropdownSelection(LocalizationSettings.SelectedLocale);

        _isInitialized = true;
    }

    private void OnDisable()
    {
        if (_tmpDropdown != null)
            _tmpDropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);

        if (_legacyDropdown != null)
            _legacyDropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);

        LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool SetLocaleByCode(string localeCode)
    {
        if (string.IsNullOrWhiteSpace(localeCode))
            return false;

        if (!_isInitialized)
        {
            _pendingLocaleCode = localeCode.Trim();
            return true;
        }

        return ApplyLocaleByCodeInternal(localeCode, saveToPrefs: true);
    }

    public bool SetLocaleByIndex(int index)
    {
        if (!_isInitialized)
            return false;

        if (index < 0 || index >= _locales.Count)
            return false;

        Locale target = _locales[index];
        if (target == null)
            return false;

        LocalizationSettings.SelectedLocale = target;
        SaveLocaleCode(target.Identifier.Code);
        return true;
    }

    public void RebuildDropdown()
    {
        if (_tmpDropdown == null && _legacyDropdown == null)
            return;

        List<string> labels = new List<string>(_locales.Count);
        for (int i = 0; i < _locales.Count; i++)
        {
            Locale locale = _locales[i];
            if (locale == null)
            {
                labels.Add("(null)");
                continue;
            }

            string code = locale.Identifier.Code;
            labels.Add($"{locale.LocaleName} ({code})");
        }

        _isUpdatingDropdown = true;

        if (_tmpDropdown != null)
        {
            _tmpDropdown.ClearOptions();
            _tmpDropdown.AddOptions(labels);
            _tmpDropdown.interactable = labels.Count > 0;
        }

        if (_legacyDropdown != null)
        {
            _legacyDropdown.ClearOptions();
            _legacyDropdown.AddOptions(labels);
            _legacyDropdown.interactable = labels.Count > 0;
        }

        _isUpdatingDropdown = false;
    }

    private void CacheLocales()
    {
        _locales.Clear();

        IReadOnlyList<Locale> available = LocalizationSettings.AvailableLocales.Locales;
        for (int i = 0; i < available.Count; i++)
            _locales.Add(available[i]);

        if (_debugLog)
            Debug.Log($"[RuntimeLocaleSelector] Loaded {_locales.Count} locales.");
    }

    private bool ApplyLocaleByCodeInternal(string localeCode, bool saveToPrefs)
    {
        Locale target = FindLocaleByCode(localeCode);
        if (target == null)
        {
            if (_debugLog)
                Debug.LogWarning($"[RuntimeLocaleSelector] Locale not found: {localeCode}");
            return false;
        }

        LocalizationSettings.SelectedLocale = target;

        if (saveToPrefs)
            SaveLocaleCode(target.Identifier.Code);

        return true;
    }

    private Locale FindLocaleByCode(string localeCode)
    {
        if (string.IsNullOrWhiteSpace(localeCode))
            return null;

        string code = localeCode.Trim();
        for (int i = 0; i < _locales.Count; i++)
        {
            Locale locale = _locales[i];
            if (locale == null)
                continue;

            if (string.Equals(locale.Identifier.Code, code, System.StringComparison.OrdinalIgnoreCase))
                return locale;
        }

        return null;
    }

    private void OnDropdownValueChanged(int selectedIndex)
    {
        if (_isUpdatingDropdown)
            return;

        SetLocaleByIndex(selectedIndex);
    }

    private void OnSelectedLocaleChanged(Locale locale)
    {
        if (locale == null)
            return;

        SaveLocaleCode(locale.Identifier.Code);
        UpdateDropdownSelection(locale);
    }

    private void UpdateDropdownSelection(Locale locale)
    {
        if (locale == null)
            return;

        int selectedIndex = FindLocaleIndex(locale);
        if (selectedIndex < 0)
            return;

        _isUpdatingDropdown = true;

        if (_tmpDropdown != null)
        {
            _tmpDropdown.SetValueWithoutNotify(selectedIndex);
            _tmpDropdown.RefreshShownValue();
        }

        if (_legacyDropdown != null)
        {
            _legacyDropdown.SetValueWithoutNotify(selectedIndex);
            _legacyDropdown.RefreshShownValue();
        }

        _isUpdatingDropdown = false;
    }

    private int FindLocaleIndex(Locale locale)
    {
        if (locale == null)
            return -1;

        string code = locale.Identifier.Code;
        for (int i = 0; i < _locales.Count; i++)
        {
            Locale item = _locales[i];
            if (item == null)
                continue;

            if (string.Equals(item.Identifier.Code, code, System.StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void SaveLocaleCode(string localeCode)
    {
        if (string.IsNullOrWhiteSpace(localeCode))
            return;

        string current = PlayerPrefs.GetString(_playerPrefsKey, string.Empty);
        if (string.Equals(current, localeCode, System.StringComparison.Ordinal))
            return;

        PlayerPrefs.SetString(_playerPrefsKey, localeCode);
        PlayerPrefs.Save();

        if (_debugLog)
            Debug.Log($"[RuntimeLocaleSelector] Saved locale: {localeCode}");
    }

    private GameObject ResolvePersistenceTarget()
    {
        if (_tmpDropdown != null)
        {
            Transform tmpRoot = _tmpDropdown.transform.root;
            if (tmpRoot != null && tmpRoot.GetComponent<Canvas>() != null)
                return tmpRoot.gameObject;
        }

        if (_legacyDropdown != null)
        {
            Transform legacyRoot = _legacyDropdown.transform.root;
            if (legacyRoot != null && legacyRoot.GetComponent<Canvas>() != null)
                return legacyRoot.gameObject;
        }

        Transform ownRoot = transform.root;
        if (ownRoot != null && ownRoot.GetComponent<Canvas>() != null)
            return ownRoot.gameObject;

        return gameObject;
    }
}
