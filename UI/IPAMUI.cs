using System;
using UnityEngine;
using UnityEngine.UI;
using Il2CppTMPro;
using System.Linq;
using System.Collections.Generic;
using greg.Core.UI;
using greg.Core.UI.Components;

using greg.Sdk.Services;

namespace greg.Mods.IPAM.UI;

public class IPAMUI : MonoBehaviour
{
    private Canvas _canvas;
    private RectTransform _listContainer;
    private List<GameObject> _rows = new();

    public static GameObject Create()
    {
        var canvas = GregUiService.CreateCanvas("IPAMUI", 998);
        var ui = canvas.gameObject.AddComponent<IPAMUI>();
        ui._canvas = canvas;

        var go = canvas.gameObject;


        // Layout Container
        var panel = GregUIBuilder.Panel("ipam.main")
            .Title("🌐 NETWORK IPAM & FLOW")
            .Position(GregUIAnchor.Center)
            .Size(800, 600)
            .Build();
        
        panel.PanelRoot.transform.SetParent(go.transform, false);
        ui._listContainer = panel.PanelRoot.transform.Find("Content") as RectTransform;
        
        // Add Header Row
        ui.CreateHeaderRow(ui._listContainer);

        ui.RefreshSwitchList();
        
        return go;
    }

    private void CreateHeaderRow(Transform parent)
    {
        var header = new GameObject("Header").AddComponent<HorizontalLayoutGroup>();
        header.transform.SetParent(parent, false);
        header.childForceExpandWidth = true;
        
        string[] labels = { "SWITCH ID", "RACK", "STATUS", "SUBNET", "VLANs" };
        foreach (var label in labels)
        {
            var txt = new GameObject(label).AddComponent<TextMeshProUGUI>();
            txt.transform.SetParent(header.transform, false);
            txt.text = label;
            txt.fontSize = 12;
            txt.fontStyle = FontStyles.Bold;
            txt.color = Color.gray;
        }
    }

    public void RefreshSwitchList()
    {
        foreach (var row in _rows) Destroy(row);
        _rows.Clear();

        var switches = GregSwitchDiscoveryService.ScanAll();
        foreach (var sw in switches)
        {
            AddSwitchRow(sw);
        }
    }

    private void AddSwitchRow(SwitchInfo sw)
    {
        var row = new GameObject("Row").AddComponent<HorizontalLayoutGroup>();
        row.transform.SetParent(_listContainer, false);
        row.childForceExpandWidth = true;
        _rows.Add(row.gameObject);

        // Analysis logic (simplified for UI)
        bool isIsolated = string.IsNullOrEmpty(sw.RackId);
        DeepFlowStatus status = sw.IsBroken ? DeepFlowStatus.Broken : (isIsolated ? DeepFlowStatus.Isolated : DeepFlowStatus.Active);

        Color statusColor = status switch
        {
            DeepFlowStatus.Active => Color.green,
            DeepFlowStatus.Isolated => Color.yellow,
            DeepFlowStatus.Broken => Color.red,
            _ => Color.gray
        };

        CreateCell(row.transform, sw.SwitchId ?? "SW-UNKNOWN");
        CreateCell(row.transform, sw.RackId ?? "N/A");
        CreateCell(row.transform, status.ToString(), statusColor);
        CreateCell(row.transform, "10.x.x.x/24"); // Metadata lookup required for real IP
        CreateCell(row.transform, "0");
    }

    private void CreateCell(Transform parent, string text, Color? color = null)
    {
        var txt = new GameObject("Cell").AddComponent<TextMeshProUGUI>();
        txt.transform.SetParent(parent, false);
        txt.text = text;
        txt.fontSize = 11;
        txt.color = color ?? Color.white;
    }
}

