using UnityEngine;
using UnityEngine.UI;

public class DragImageController : MonoBehaviour
{
    public static DragImageController Instance;
    public Image dragPreviewImage;

    private void Awake()
    {
        Instance = this;
        dragPreviewImage.gameObject.SetActive(false);
    }

    public void Show(Sprite sprite)
    {
        dragPreviewImage.sprite = sprite;
        dragPreviewImage.gameObject.SetActive(true);
    }

    public void Move(Vector3 position)
    {
        dragPreviewImage.transform.position = position;
    }

    public void Hide()
    {
        dragPreviewImage.gameObject.SetActive(false);
    }
}
