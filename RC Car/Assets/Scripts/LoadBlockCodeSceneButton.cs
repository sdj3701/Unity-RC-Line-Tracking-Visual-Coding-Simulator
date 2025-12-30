using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LoadBlockCodeSceneButton : MonoBehaviour
{
    public Button loadButton; // 인스펙터에서 UI Button 할당

    void OnEnable()
    {
        if (loadButton != null)
        {
            loadButton.onClick.RemoveListener(CreateBlock); // Prevent duplicates
            loadButton.onClick.AddListener(CreateBlock);
        }
    }

    public void CreateBlock()
    {
        SceneManager.LoadScene("TestCreateBlock");
    }
}
