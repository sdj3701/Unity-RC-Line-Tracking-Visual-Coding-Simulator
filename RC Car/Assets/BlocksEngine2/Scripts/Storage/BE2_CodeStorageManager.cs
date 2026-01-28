using System.Collections.Generic;
using UnityEngine;

namespace MG_BlocksEngine2.Storage
{
    /// <summary>
    /// 코드 저장소 매니저 - 싱글톤
    /// Storage Provider를 관리하고, UI와 저장소 사이의 중간 레이어 역할
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

        // 현재 사용 중인 저장소 Provider
        private ICodeStorageProvider _storageProvider;

        void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
                
                // 기본값: 로컬 저장소
                _storageProvider = new LocalStorageProvider();
                Debug.Log("[BE2_CodeStorageManager] 초기화 완료 (LocalStorageProvider)");
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 저장소 Provider 교체
        /// TODO: [향후 확장] DB 연결 시 DatabaseStorageProvider로 교체
        /// 예시: BE2_CodeStorageManager.Instance.SetStorageProvider(new DatabaseStorageProvider());
        /// </summary>
        public void SetStorageProvider(ICodeStorageProvider provider)
        {
            _storageProvider = provider;
            Debug.Log($"[BE2_CodeStorageManager] StorageProvider 변경: {provider.GetType().Name}");
        }

        /// <summary>
        /// 현재 Provider 반환
        /// </summary>
        public ICodeStorageProvider GetStorageProvider() => _storageProvider;

        #region Storage Operations (Provider에 위임)

        /// <summary>
        /// 코드 저장
        /// </summary>
        public bool SaveCode(string fileName, string xmlContent, string jsonContent)
        {
            if (_storageProvider == null)
            {
                Debug.LogError("[BE2_CodeStorageManager] StorageProvider가 설정되지 않음");
                return false;
            }
            return _storageProvider.SaveCode(fileName, xmlContent, jsonContent);
        }

        /// <summary>
        /// XML 불러오기
        /// </summary>
        public string LoadXml(string fileName)
        {
            if (_storageProvider == null) return null;
            return _storageProvider.LoadXml(fileName);
        }

        /// <summary>
        /// JSON 불러오기
        /// </summary>
        public string LoadJson(string fileName)
        {
            if (_storageProvider == null) return null;
            return _storageProvider.LoadJson(fileName);
        }

        /// <summary>
        /// 저장된 파일 목록
        /// </summary>
        public List<string> GetFileList()
        {
            if (_storageProvider == null) return new List<string>();
            return _storageProvider.GetFileList();
        }

        /// <summary>
        /// 파일 존재 확인
        /// </summary>
        public bool FileExists(string fileName)
        {
            if (_storageProvider == null) return false;
            return _storageProvider.FileExists(fileName);
        }

        /// <summary>
        /// 파일 삭제
        /// </summary>
        public bool DeleteCode(string fileName)
        {
            if (_storageProvider == null) return false;
            return _storageProvider.DeleteCode(fileName);
        }

        #endregion
    }
}
