using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LoadBlockCodeSceneButton : MonoBehaviour
{
    public Button loadButton;

    private void OnEnable()
    {
        if (loadButton == null)
            return;

        loadButton.onClick.RemoveListener(CreateBlock);
        loadButton.onClick.AddListener(CreateBlock);
    }

    public void CreateBlock()
    {
        SceneManager.LoadScene("02_SingleCreateBlock");
    }
}
