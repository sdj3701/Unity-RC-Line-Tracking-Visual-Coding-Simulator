using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace MG_BlocksEngine2.Storage
{
    /// <summary>
    /// UI에서 사용하는 저장소 매니저 게이트웨이입니다.
    /// 로컬 저장소와 원격 DB 저장소 제공자 사이를 전환할 수 있습니다.
    /// </summary>
    public class BE2_CodeStorageManager : MonoBehaviour
    {
        private static BE2_CodeStorageManager _instance;
        public static BE2_CodeStorageManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<BE2_CodeStorageManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("BE2_CodeStorageManager");
                        _instance = go.AddComponent<BE2_CodeStorageManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        [Header("Storage")]
        [SerializeField] private bool _useRemoteStorage = true;

        private ICodeStorageProvider _storageProvider;
        private ICodeStorageProvider _localStorageProvider;
        private ICodeStorageProvider _remoteStorageProvider;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);

                _localStorageProvider = new LocalStorageProvider();
                _remoteStorageProvider = new DatabaseStorageProvider(_localStorageProvider);
                _storageProvider = _useRemoteStorage
                    ? _remoteStorageProvider
                    : _localStorageProvider;

                Debug.Log($"[BE2_CodeStorageManager] Initialized with provider: {_storageProvider.GetType().Name}");
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 런타임에 저장소 제공자를 교체합니다(예: 로컬 강제/커스텀 원격 제공자).
        /// </summary>
        public void SetStorageProvider(ICodeStorageProvider provider)
        {
            _storageProvider = provider;
            Debug.Log($"[BE2_CodeStorageManager] StorageProvider changed: {provider.GetType().Name}");
        }

        public ICodeStorageProvider GetStorageProvider() => _storageProvider;

        /// <summary>
        /// DB 연결 상태에 맞춰 원격/로컬 저장소를 전환합니다.
        /// </summary>
        public void SetRemoteStorageEnabled(bool enabled)
        {
            if (_localStorageProvider == null)
            {
                _localStorageProvider = new LocalStorageProvider();
            }

            if (_remoteStorageProvider == null)
            {
                _remoteStorageProvider = new DatabaseStorageProvider(_localStorageProvider);
            }

            _storageProvider = enabled ? _remoteStorageProvider : _localStorageProvider;
            Debug.Log($"[BE2_CodeStorageManager] Remote storage enabled: {enabled}. Active provider: {_storageProvider.GetType().Name}");
        }

        /// <summary>
        /// 현재 활성화된 제공자를 통해 XML + JSON을 저장합니다.
        /// </summary>
        public async Task<bool> SaveCodeAsync(string fileName, string xmlContent, string jsonContent, bool isModified)
        {
            if (_storageProvider == null)
            {
                Debug.LogError("[BE2_CodeStorageManager] StorageProvider is not set.");
                return false;
            }

            return await _storageProvider.SaveCodeAsync(fileName, xmlContent, jsonContent, isModified);
        }

        /// <summary>
        /// 현재 활성화된 제공자를 통해 XML을 불러옵니다.
        /// </summary>
        public async Task<string> LoadXmlAsync(string fileName)
        {
            if (_storageProvider == null)
            {
                return null;
            }

            return await _storageProvider.LoadXmlAsync(fileName);
        }

        /// <summary>
        /// 현재 활성화된 제공자를 통해 JSON을 불러옵니다.
        /// </summary>
        public async Task<string> LoadJsonAsync(string fileName)
        {
            if (_storageProvider == null)
            {
                return null;
            }

            return await _storageProvider.LoadJsonAsync(fileName);
        }

        /// <summary>
        /// 현재 활성화된 제공자를 통해 저장 파일 목록을 가져옵니다.
        /// </summary>
        public async Task<List<string>> GetFileListAsync()
        {
            if (_storageProvider == null)
            {
                return new List<string>();
            }

            return await _storageProvider.GetFileListAsync();
        }

        /// <summary>
        /// 현재 활성화된 제공자를 통해 파일 존재 여부를 확인합니다.
        /// </summary>
        public async Task<bool> FileExistsAsync(string fileName)
        {
            if (_storageProvider == null)
            {
                return false;
            }

            return await _storageProvider.FileExistsAsync(fileName);
        }

        /// <summary>
        /// 현재 활성화된 제공자를 통해 파일을 삭제합니다.
        /// </summary>
        public async Task<bool> DeleteCodeAsync(string fileName)
        {
            if (_storageProvider == null)
            {
                return false;
            }

            return await _storageProvider.DeleteCodeAsync(fileName);
        }
    }
}

