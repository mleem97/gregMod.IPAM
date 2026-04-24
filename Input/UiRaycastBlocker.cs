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

        if (_root.activeSelf == block)
        {
            return;
        }

        // Must stay above the game's pause / system Overlay canvases (they were drawing over IMGUI IPAM).
        var cv = _root.GetComponent<Canvas>();
        if (cv != null)
        {
            cv.sortingOrder = 2_000_000;
        }

        _root.SetActive(block);
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
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2_000_000;
        canvas.overrideSorting = true;

        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _root.AddComponent<GraphicRaycaster>();

        var plate = new GameObject("Plate");
        plate.transform.SetParent(_root.transform, false);
        var rt = plate.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = plate.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;

        _root.SetActive(false);
    }
}
