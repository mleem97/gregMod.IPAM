using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DHCPSwitches;

/// <summary>
/// Full-screen transparent overlay canvas so EventSystem raycasts hit this layer first while IPAM is open,
/// instead of passing through to the game's menus. IMGUI still receives the same mouse in <see cref="IPAMOverlay.Draw"/>.
/// </summary>
internal static class UiRaycastBlocker
{
    private static GameObject _root;

    internal static void SetBlocking(bool block)
    {
        if (block)
        {
            EnsureRoot();
        }

        if (_root == null)
        {
            return;
        }

        if (block)
        {
            if (!_root.activeSelf)
            {
                _root.SetActive(true);
            }

            // Run every time IPAM is shown — game UI may create canvases later with higher sort orders.
            RefreshFrontStack();
        }
        else if (_root.activeSelf)
        {
            _root.SetActive(false);
        }
    }

    private static void RefreshFrontStack()
    {
        var cv = _root.GetComponent<Canvas>();
        if (cv != null)
        {
            ApplyTopCanvasMetadata(cv);
        }

        var gr = _root.GetComponent<GraphicRaycaster>();
        if (gr != null)
        {
            // Prefer blocking both 2D and 3D raycast hits when the UI module supports it.
            try
            {
                gr.blockingObjects = GraphicRaycaster.BlockingObjects.All;
            }
            catch
            {
                // Older UnityEngine.UI builds may not expose All — ignore.
            }
        }

        // Among canvases with identical layer + order, later siblings draw on top.
        _root.transform.SetAsLastSibling();
    }

    private static void ApplyTopCanvasMetadata(Canvas canvas)
    {
        canvas.overrideSorting = true;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Draw on the front-most sorting layer (project-defined), then max order within that layer.
        try
        {
            var layers = SortingLayer.layers;
            if (layers != null && layers.Length > 0)
            {
                canvas.sortingLayerID = layers[layers.Length - 1].id;
            }
        }
        catch
        {
            // Il2Cpp / stripped Editor tooling — keep default layer.
        }

        canvas.sortingOrder = short.MaxValue;
    }

    private static void EnsureRoot()
    {
        if (_root != null)
        {
            return;
        }

        if (EventSystem.current == null)
        {
            ModLogging.Warning(
                "DHCPSwitches: No EventSystem in scene — clicks may still reach UI behind IPAM.");
            return;
        }

        _root = new GameObject("DHCPSwitches_IPAMClickBlocker");
        UnityEngine.Object.DontDestroyOnLoad(_root);

        var canvas = _root.AddComponent<Canvas>();
        ApplyTopCanvasMetadata(canvas);

        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var raycaster = _root.AddComponent<GraphicRaycaster>();
        try
        {
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.All;
        }
        catch
        {
            // ignore
        }

        var plate = new GameObject("Plate");
        plate.transform.SetParent(_root.transform, false);
        var rt = plate.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = plate.AddComponent<Image>();
        // Fully transparent meshes are occasionally skipped by the UI batcher; tiny alpha keeps raycasts reliable.
        image.color = new Color(0f, 0f, 0f, 0.004f);
        image.raycastTarget = true;

        _root.SetActive(false);
    }
}
