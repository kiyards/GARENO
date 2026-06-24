using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class SafeAreaAnchor : MonoBehaviour
{
    public RectTransform rectTransform;
    private void Awake()
    {
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
    }
    void Start()
    {
        SetAnchor();
    }
    public void SetAnchor()
    {
        Rect safeArea = Screen.safeArea;
        Vector2 minAnchor = safeArea.position;
        Vector2 maxAnchor = minAnchor + safeArea.size;
        minAnchor.x /= Screen.width;
        minAnchor.y /= Screen.height;
        maxAnchor.x /= Screen.width;
        maxAnchor.y /= Screen.height;
        rectTransform.anchorMin = minAnchor;
        rectTransform.anchorMax = maxAnchor;
    }
}