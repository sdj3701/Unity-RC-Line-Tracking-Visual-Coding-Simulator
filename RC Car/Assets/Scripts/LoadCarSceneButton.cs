using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LoadCarSceneButton : MonoBehaviour
{
    public Button loadButton; // 인스펙터에서 UI Button 할당

    void OnEnable()
    {
        if (loadButton != null)
        {
            loadButton.onClick.RemoveListener(LoadCarScene); // Prevent duplicates
            loadButton.onClick.AddListener(LoadCarScene);
        }
    }

    public void LoadCarScene()
    {
        SceneManager.LoadScene("Car");
    }
}
