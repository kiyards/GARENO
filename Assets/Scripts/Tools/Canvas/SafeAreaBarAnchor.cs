using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class SafeAreaBarAnchor : MonoBehaviour
{
    [Header("Rects")]
    public RectTransform baseRect;   // parent (this)
    public RectTransform safeRect;   // keeps its designed height, sits below unsafe
    public RectTransform unsafeRect; // starts at 0, extends to top unsafe inset

    [Header("Options")]
    public float overlayBleed = 0f;          // how much the unsafe area can bleed over the top (e.g. for shadows)
    public bool applyHorizontalInsets = true; // also respect left/right safe area for safeRect

    float _safeHeight;                 // original designed height (canvas units)
    Vector2 _safeOrigAnchoredPos;      // original local anchored pos (preserve X, baseline Y)
    bool _cached;

    void Reset()
    {
        baseRect = GetComponent<RectTransform>();
    }

    void Awake()
    {
        if (!baseRect) baseRect = GetComponent<RectTransform>();
        CacheOriginals();
    }

    void OnEnable()
    {
        Apply(force: true);
    }

    // React to orientation/resolution/canvas scaler changes
    void OnRectTransformDimensionsChange()
    {
        Apply();
    }

    void CacheOriginals()
    {
        if (_cached || !safeRect) return;
        _safeHeight = Mathf.Max(0f, safeRect.rect.height);
        _safeOrigAnchoredPos = safeRect.anchoredPosition;
        _cached = true;
    }

    public void Init() => Apply(force: true); // keep your API

    void Apply(bool force = false)
    {
        if (!baseRect || !safeRect) return;
        if (!_cached) CacheOriginals();

        // Safe area (pixels)
        Rect sa = Screen.safeArea;
        float topInsetPx = Screen.height - sa.yMax;
        float leftInsetPx = sa.xMin;
        float rightInsetPx = Screen.width - sa.xMax;

        // Convert to canvas units
        float scale = 1f;
        var canvas = baseRect.GetComponentInParent<Canvas>();
        if (canvas)
        {
            var root = canvas.rootCanvas ? canvas.rootCanvas : canvas;
            if (root) scale = root.scaleFactor;
        }
        float topInset = topInsetPx / scale;
        float leftInset = leftInsetPx / scale;
        float rightInset = rightInsetPx / scale;

        if (unsafeRect)
        {
            // Ensure children are top-pinned (no vertical stretch), horizontally stretched
            unsafeRect.anchorMin = new Vector2(0f, 1f);
            unsafeRect.anchorMax = new Vector2(1f, 1f);
            unsafeRect.pivot = new Vector2(0.5f, 1f);

            // Unsafe strip: sits at the very top, height = top inset + bleed
            unsafeRect.sizeDelta = new Vector2(unsafeRect.sizeDelta.x, topInset + overlayBleed);
            unsafeRect.anchoredPosition = new Vector2(unsafeRect.anchoredPosition.x, 0f);
            // Fill outer width
            unsafeRect.offsetMin = new Vector2(0f, unsafeRect.offsetMin.y);
            unsafeRect.offsetMax = new Vector2(0f, unsafeRect.offsetMax.y);
        }

        safeRect.anchorMin = new Vector2(0f, 1f);
        safeRect.anchorMax = new Vector2(1f, 1f);
        safeRect.pivot = new Vector2(0.5f, 1f);

        // Safe header: original designed height, pushed down by the unsafe top inset
        safeRect.sizeDelta = new Vector2(safeRect.sizeDelta.x, _safeHeight);
        safeRect.anchoredPosition = new Vector2(_safeOrigAnchoredPos.x, -topInset);

        // Optionally respect left/right safe area for the header content
        if (applyHorizontalInsets)
        {
            safeRect.offsetMin = new Vector2(leftInset, safeRect.offsetMin.y);
            safeRect.offsetMax = new Vector2(-rightInset, safeRect.offsetMax.y);
        }
        else
        {
            // Fill full width if not applying horizontal insets
            safeRect.offsetMin = new Vector2(0f, safeRect.offsetMin.y);
            safeRect.offsetMax = new Vector2(0f, safeRect.offsetMax.y);
        }

        // Parent height = unsafe (top inset) + safe header height
        baseRect.pivot = new Vector2(baseRect.pivot.x, 1f);
        baseRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, topInset + _safeHeight);
        baseRect.anchoredPosition = Vector2.zero;
    }
}