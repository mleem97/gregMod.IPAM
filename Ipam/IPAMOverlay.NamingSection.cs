using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GregModIPAM;

// Naming convention mode inside the server edit popup (9005).

public static partial class IPAMOverlay
{
    private static string _inlineNamingPatternBuf = DefaultNamingPattern;
    private static string _inlineNamingConventionId = "";
    private static string _inlineNamingAbbrevBuf = "";
    private static int _inlineNamingSeqStart = 1;
    private static string _inlineNamingSeqStartBuf = "1";
    private static int _inlineNamingSeqStep = 1;
    private static int _inlineNamingSeqPad = 2;
    private static int _inlineNamingCounterScope;
    private static int _inlineNamingSortOrder;
    private static bool _inlineNamingDryRun;
    private static bool _inlineNamingSaveDialogOpen;
    private static string _inlineNamingSaveNameBuf = "";
    private static bool _inlineNamingSetCustomerDefault;
    private static bool _inlineNamingAutoApplyAfterAssign;
    private static Vector2 _inlineNamingPreviewScroll;
    private static Vector2 _inlineNamingConventionsScroll;
    private static string _inlineNamingConvRenameBuf = "";
    private static string _inlineNamingManualRowBuf = "";
    private static string _inlineNamingManualColBuf = "";

    private const string DefaultNamingPattern = "{col}-{row}-{Unit Size}";

    private readonly struct InlineNamingTokenDef
    {
        public readonly string Label;
        public readonly string Insert;

        public InlineNamingTokenDef(string label, string insert)
        {
            Label = label;
            Insert = insert;
        }
    }

    private static readonly InlineNamingTokenDef[] NamingTokenButtons =
    {
        new("Column", "{col}"),
        new("Row", "{row}"),
        new("Unit size", "{Unit Size}"),
        new("CustomerShort", "{customerShort}"),
        new("Seq", "{seq}"),
        new("color", "{color}"),
    };

    private static void ResetInlineNamingDraft()
    {
        _inlineNamingPatternBuf = DefaultNamingPattern;
        _inlineNamingConventionId = "";
        _inlineNamingAbbrevBuf = "";
        _inlineNamingSeqStart = 1;
        _inlineNamingSeqStartBuf = "1";
        _inlineNamingSeqStep = 1;
        _inlineNamingSeqPad = 2;
        _inlineNamingCounterScope = (int)NamingCounterScope.PerRack;
        _inlineNamingSortOrder = (int)NamingSortOrder.RackU;
        _inlineNamingDryRun = false;
        _inlineNamingSaveDialogOpen = false;
        _inlineNamingSaveNameBuf = "";
        _inlineNamingSetCustomerDefault = false;
        _inlineNamingAutoApplyAfterAssign = false;
        _inlineNamingPreviewScroll = Vector2.zero;
        _inlineNamingConventionsScroll = Vector2.zero;
        _inlineNamingConvRenameBuf = "";
        _inlineNamingManualRowBuf = "";
        _inlineNamingManualColBuf = "";
    }

    private static bool InlineNamingPatternUsesCol()
    {
        return InlineNamingPatternContainsToken("col");
    }

    private static bool InlineNamingPatternUsesRow()
    {
        return InlineNamingPatternContainsToken("row");
    }

    private static bool InlineNamingPatternContainsToken(string tokenKey)
    {
        var pat = _inlineNamingPatternBuf ?? "";
        return pat.IndexOf("{" + tokenKey, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void LoadInlineNamingFromConvention(NamingConventionEntry conv)
    {
        if (conv == null)
        {
            return;
        }

        _inlineNamingConventionId = conv.Id ?? "";
        _inlineNamingPatternBuf = conv.Pattern ?? "";
        _inlineNamingSeqStart = conv.SeqStart > 0 ? conv.SeqStart : 1;
        _inlineNamingSeqStartBuf = _inlineNamingSeqStart.ToString(CultureInfo.InvariantCulture);
        _inlineNamingSeqStep = conv.SeqStep > 0 ? conv.SeqStep : 1;
        _inlineNamingSeqPad = conv.SeqPad;
        _inlineNamingCounterScope = (int)NamingConventionStore.ParseScope(conv.CounterScope);
        _inlineNamingSortOrder = (int)NamingConventionStore.ParseSort(conv.SortOrder);
        _inlineNamingAutoApplyAfterAssign = conv.AutoApplyAfterAssign;
        _inlineNamingConvRenameBuf = conv.Name ?? "";
        _inlineNamingManualRowBuf = conv.ManualRow ?? "";
        _inlineNamingManualColBuf = conv.ManualCol ?? "";
    }

    private static void SyncInlineNamingSeqStartFromBuffer()
    {
        if (int.TryParse((_inlineNamingSeqStartBuf ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && n >= 1)
        {
            _inlineNamingSeqStart = n;
        }
    }

    private static NamingApplyOptions BuildInlineNamingOptions()
    {
        SyncInlineNamingSeqStartFromBuffer();
        return new NamingApplyOptions
        {
            Pattern = _inlineNamingPatternBuf ?? "",
            SeqStart = Mathf.Max(1, _inlineNamingSeqStart),
            SeqStep = Mathf.Max(1, _inlineNamingSeqStep),
            SeqPad = Mathf.Max(0, _inlineNamingSeqPad),
            CounterScope = (NamingCounterScope)_inlineNamingCounterScope,
            SortOrder = (NamingSortOrder)_inlineNamingSortOrder,
            DryRun = _inlineNamingDryRun,
            ManualRow = (_inlineNamingManualRowBuf ?? "").Trim().ToUpperInvariant(),
            ManualCol = (_inlineNamingManualColBuf ?? "").Trim(),
        };
    }

    private static bool TryValidateInlineNamingRackFields(out string error)
    {
        error = null;
        if (InlineNamingPatternUsesRow() && string.IsNullOrWhiteSpace(_inlineNamingManualRowBuf))
        {
            error = "Enter rack row letter (e.g. B) under Rack position.";
            return false;
        }

        if (InlineNamingPatternUsesCol() && string.IsNullOrWhiteSpace(_inlineNamingManualColBuf))
        {
            error = "Enter rack column number (e.g. 25) under Rack position.";
            return false;
        }

        return true;
    }

    private static void DrawInlineNamingSection(float px, ref float py, float iw, CustomerBase cust)
    {
        CollectSelectedServersIntoScratch();
        var n = SelectedServersScratch.Count;

        GUI.Label(new Rect(px, py, iw, 22f), "Naming convention", _stSectionTitle);
        py += 26f;

        GUI.Label(new Rect(px, py, iw, 20f), $"{n} server(s) selected.", _stMuted);
        py += 22f;

        if (cust != null)
        {
            var cn = GetCustomerName(cust);
            GUI.Label(
                new Rect(px, py, iw, 20f),
                $"Customer   #{cust.customerID}  {Trunc(cn ?? "", 40)}  (optional for {{customerShort}} token)",
                _stMuted);
            py += 24f;

            GUI.Label(new Rect(px, py + 2f, 88f, 22f), "Short name", _stFormLabel);
            DrawIpamFormTextField(
                new Rect(px + 92f, py, Mathf.Min(iw - 96f, 120f), 22f),
                IpamFormFocusInlineNamingAbbrev,
                16,
                IpamTextFieldKind.Name);
            py += 28f;
        }
        else
        {
            GUI.Label(
                new Rect(px, py, iw, 36f),
                "No customer selected — tokens like {customerShort} stay empty unless you pick a customer above.",
                _stHint);
            py += 40f;
        }

        DrawInlineNamingConventionList(px, ref py, iw, cust);

        GUI.Label(new Rect(px, py + 2f, 56f, 22f), "Pattern", _stFormLabel);
        var patternFieldW = iw - 64f - 58f;
        DrawIpamFormTextField(
            new Rect(px + 60f, py, patternFieldW, 22f),
            IpamFormFocusInlineNamingPattern,
            128,
            IpamTextFieldKind.Name);
        if (ImguiButtonOnce(new Rect(px + 60f + patternFieldW + 6f, py, 52f, 22f), "Clear", 9085, _stMutedBtn))
        {
            ClearInlineNamingPattern();
        }

        py += 28f;

        DrawInlineNamingTokenPicker(px, ref py, iw);

        if (InlineNamingPatternUsesRow() || InlineNamingPatternUsesCol())
        {
            GUI.Label(new Rect(px, py, iw, 18f), "Rack position (same for all servers in this apply)", _stFormLabel);
            py += 20f;
            if (InlineNamingPatternUsesRow())
            {
                GUI.Label(new Rect(px, py + 2f, 36f, 22f), "Row", _stFormLabel);
                DrawIpamFormTextField(
                    new Rect(px + 40f, py, 40f, 22f),
                    IpamFormFocusInlineNamingManualRow,
                    2,
                    IpamTextFieldKind.Name);
            }

            if (InlineNamingPatternUsesCol())
            {
                var colX = InlineNamingPatternUsesRow() ? px + 92f : px + 40f;
                GUI.Label(new Rect(colX, py + 2f, 52f, 22f), "Column", _stFormLabel);
                DrawIpamFormTextField(
                    new Rect(colX + 56f, py, 48f, 22f),
                    IpamFormFocusInlineNamingManualCol,
                    3,
                    IpamTextFieldKind.VlanIdDigits);
            }

            py += 28f;
            GUI.Label(
                new Rect(px, py, iw, 32f),
                "Enter the rack grid label for this batch (row letter + column number, e.g. B and 25 → B25). Apply once per rack.",
                _stHint);
            py += 34f;
        }

        GUI.Label(new Rect(px, py + 2f, 36f, 22f), "Seq", _stFormLabel);
        var cx = px + 40f;
        if (ImguiButtonOnce(new Rect(cx, py, 28f, 22f), "−", 9071, _stMutedBtn))
        {
            _inlineNamingSeqStart = Mathf.Max(1, _inlineNamingSeqStart - 1);
            _inlineNamingSeqStartBuf = _inlineNamingSeqStart.ToString(CultureInfo.InvariantCulture);
        }

        cx += 32f;
        DrawIpamFormTextField(new Rect(cx, py, 36f, 22f), IpamFormFocusInlineNamingSeqStart, 4, IpamTextFieldKind.VlanIdDigits);
        cx += 40f;
        if (ImguiButtonOnce(new Rect(cx, py, 28f, 22f), "+", 9072, _stMutedBtn))
        {
            _inlineNamingSeqStart++;
            _inlineNamingSeqStartBuf = _inlineNamingSeqStart.ToString(CultureInfo.InvariantCulture);
        }

        cx += 34f;
        GUI.Label(new Rect(cx, py + 2f, 44f, 22f), "Digits", _stFormLabel);
        cx += 48f;
        if (ImguiButtonOnce(new Rect(cx, py, 28f, 22f), "−", 9073, _stMutedBtn))
        {
            _inlineNamingSeqPad = Mathf.Max(0, _inlineNamingSeqPad - 1);
        }

        cx += 32f;
        GUI.Label(new Rect(cx, py + 2f, 16f, 22f), _inlineNamingSeqPad.ToString(CultureInfo.InvariantCulture), _stTableCell);
        cx += 20f;
        if (ImguiButtonOnce(new Rect(cx, py, 28f, 22f), "+", 9074, _stMutedBtn))
        {
            _inlineNamingSeqPad = Mathf.Min(6, _inlineNamingSeqPad + 1);
        }

        py += 28f;
        GUI.Label(
            new Rect(px, py, iw, 32f),
            "Seq = starting number for {seq}. Digits = width (0 → 1,2,3…  2 → 01,02,03…).",
            _stHint);
        py += 34f;

        GUI.Label(new Rect(px, py + 2f, 88f, 22f), "Seq resets", _stFormLabel);
        var scopeLabels = new[] { "whole batch", "each rack", "each customer", "each row", "each column" };
        _inlineNamingCounterScope = Mathf.Clamp(_inlineNamingCounterScope, 0, scopeLabels.Length - 1);
        if (ImguiButtonOnce(new Rect(px + 92f, py, 22f, 22f), "◀", 9075, _stMutedBtn))
        {
            _inlineNamingCounterScope = (_inlineNamingCounterScope + scopeLabels.Length - 1) % scopeLabels.Length;
        }

        GUI.Label(new Rect(px + 118f, py + 2f, 108f, 22f), scopeLabels[_inlineNamingCounterScope], _stTableCell);
        if (ImguiButtonOnce(new Rect(px + 230f, py, 22f, 22f), "▶", 9075 + 1, _stMutedBtn))
        {
            _inlineNamingCounterScope = (_inlineNamingCounterScope + 1) % scopeLabels.Length;
        }

        GUI.Label(new Rect(px + 258f, py + 2f, 36f, 22f), "Order", _stFormLabel);
        var sortLabels = new[] { "rack U", "column", "IP", "selection" };
        _inlineNamingSortOrder = Mathf.Clamp(_inlineNamingSortOrder, 0, sortLabels.Length - 1);
        if (ImguiButtonOnce(new Rect(px + 298f, py, 22f, 22f), "◀", 9076, _stMutedBtn))
        {
            _inlineNamingSortOrder = (_inlineNamingSortOrder + sortLabels.Length - 1) % sortLabels.Length;
        }

        GUI.Label(new Rect(px + 324f, py + 2f, 72f, 22f), sortLabels[_inlineNamingSortOrder], _stTableCell);
        if (ImguiButtonOnce(new Rect(px + 400f, py, 22f, 22f), "▶", 9076 + 1, _stMutedBtn))
        {
            _inlineNamingSortOrder = (_inlineNamingSortOrder + 1) % sortLabels.Length;
        }

        py += 28f;
        GUI.Label(
            new Rect(px, py, iw, 32f),
            "Seq resets = when {seq} goes back to the start number. Order = which server gets 1 first.",
            _stHint);
        py += 34f;

        _inlineNamingDryRun = ImguiToggleOnce(
            new Rect(px, py, 160f, 22f),
            _inlineNamingDryRun,
            9077,
            new GUIContent("Dry run only"));
        py += 26f;

        var preview = NamingTemplateEngine.BuildPreview(
            SelectedServersScratch,
            cust,
            BuildInlineNamingOptions());
        const float rowH = 22f;
        var rowW = Mathf.Max(120f, iw - 18f);
        GUI.Label(new Rect(px, py, iw, 20f), "Preview", _stFormLabel);
        py += 22f;
        var previewRows = Mathf.Min(preview.Count, 32);
        for (var i = 0; i < previewRows; i++)
        {
            var row = preview[i];
            var warn = string.IsNullOrEmpty(row.Warning) ? "" : $"  ! {row.Warning}";
            GUI.Label(
                new Rect(px, py, rowW, rowH),
                $"{Trunc(row.OldName, 22)} → {Trunc(row.NewName, 28)}{warn}",
                string.IsNullOrEmpty(row.Warning) ? _stMuted : _stError);
            py += rowH;
        }

        py += 4f;

        if (_inlineNamingSaveDialogOpen)
        {
            GUI.Label(new Rect(px, py + 2f, 96f, 22f), "Save as…", _stFormLabel);
            DrawIpamFormTextField(
                new Rect(px + 100f, py, Mathf.Min(iw - 104f, 200f), 22f),
                IpamFormFocusInlineNamingSaveName,
                64,
                IpamTextFieldKind.Name);
            py += 28f;
            if (cust != null)
            {
                _inlineNamingSetCustomerDefault = ImguiToggleOnce(
                    new Rect(px, py, iw, 22f),
                    _inlineNamingSetCustomerDefault,
                    9078,
                    new GUIContent("Set as default for this customer"));
                py += 24f;
                if (_inlineNamingSetCustomerDefault)
                {
                    _inlineNamingAutoApplyAfterAssign = ImguiToggleOnce(
                        new Rect(px + 16f, py, iw - 16f, 22f),
                        _inlineNamingAutoApplyAfterAssign,
                        9079,
                        new GUIContent("Auto-apply after Contract+DHCP / IPAM assign"));
                    py += 24f;
                }
            }
        }
        else if (ImguiButtonOnce(new Rect(px, py, 148f, 24f), "Save as convention…", 9080, _stMutedBtn))
        {
            _inlineNamingSaveDialogOpen = true;
            _inlineNamingSaveNameBuf = _inlineNamingSaveNameBuf ?? "";
        }

        if (_inlineNamingSaveDialogOpen)
        {
            if (ImguiButtonOnce(new Rect(px + 156f, py, 80f, 24f), "Save", 9081, _stPrimaryBtn))
            {
                TrySaveInlineNamingConvention(cust);
            }

            if (ImguiButtonOnce(new Rect(px + 244f, py, 80f, 24f), "Cancel", 9086, _stMutedBtn))
            {
                _inlineNamingSaveDialogOpen = false;
                _inlineNamingSaveNameBuf = "";
            }

            py += 28f;
        }
        else
        {
            py += 28f;
        }
    }

    private static void DrawInlineNamingConventionList(float px, ref float py, float iw, CustomerBase cust)
    {
        var conventions = NamingConventionStore.GetConventions();
        GUI.Label(new Rect(px, py, iw, 20f), "Saved conventions", _stFormLabel);
        py += 22f;

        const float rowH = 24f;
        var customSelected = string.IsNullOrEmpty(_inlineNamingConventionId);
        var customStyle = customSelected ? _stPrimaryBtn : _stMutedBtn;
        if (ImguiButtonOnce(new Rect(px, py, iw, rowH - 2f), "(custom pattern)", 9065, customStyle))
        {
            _inlineNamingConventionId = "";
            _inlineNamingConvRenameBuf = "";
            _inlineNamingPatternBuf = DefaultNamingPattern;
            _ipamFormNamingPatternSelectAll = false;
        }

        py += rowH;

        if (conventions.Count > 0)
        {
            var rowW = Mathf.Max(120f, iw - 18f);
            for (var i = 0; i < conventions.Count; i++)
            {
                var conv = conventions[i];
                if (conv == null)
                {
                    continue;
                }

                var selected = string.Equals(conv.Id, _inlineNamingConventionId, StringComparison.Ordinal);
                var label = Trunc(conv.Name ?? "?", 48);
                if (ImguiButtonOnce(
                        new Rect(px, py, rowW, rowH - 2f),
                        label,
                        9066 + i,
                        selected ? _stPrimaryBtn : _stMutedBtn))
                {
                    LoadInlineNamingFromConvention(conv);
                }

                py += rowH;
            }

            py += 6f;
        }
        else
        {
            GUI.Label(new Rect(px, py, iw, 20f), "No saved conventions yet — use Save as convention… below.", _stHint);
            py += 24f;
        }

        if (!string.IsNullOrEmpty(_inlineNamingConventionId))
        {
            GUI.Label(new Rect(px, py + 2f, 40f, 22f), "Name", _stFormLabel);
            DrawIpamFormTextField(
                new Rect(px + 44f, py, Mathf.Min(iw - 248f, 200f), 22f),
                IpamFormFocusInlineNamingConvName,
                64,
                IpamTextFieldKind.Name);
            var bx = px + iw - 196f;
            if (ImguiButtonOnce(new Rect(bx, py, 72f, 22f), "Rename", 9082, _stMutedBtn))
            {
                TryRenameSelectedNamingConvention();
            }

            bx += 78f;
            if (ImguiButtonOnce(new Rect(bx, py, 56f, 22f), "Update", 9083, _stMutedBtn))
            {
                TryUpdateSelectedNamingConvention(cust);
            }

            bx += 62f;
            if (ImguiButtonOnce(new Rect(bx, py, 56f, 22f), "Delete", 9084, _stMutedBtn))
            {
                TryDeleteSelectedNamingConvention();
            }

            py += 28f;
        }
    }

    private static void TryRenameSelectedNamingConvention()
    {
        if (!NamingConventionStore.TryRenameConvention(
                _inlineNamingConventionId,
                _inlineNamingConvRenameBuf,
                out var err))
        {
            _inlineAssignError = err;
            return;
        }

        _inlineAssignError = "";
    }

    private static void TryUpdateSelectedNamingConvention(CustomerBase cust)
    {
        SyncInlineNamingSeqStartFromBuffer();
        if (!NamingConventionStore.TryUpdateConventionById(
                _inlineNamingConventionId,
                _inlineNamingPatternBuf,
                _inlineNamingSeqStart,
                _inlineNamingSeqStep,
                _inlineNamingSeqPad,
                (NamingCounterScope)_inlineNamingCounterScope,
                (NamingSortOrder)_inlineNamingSortOrder,
                cust?.customerID ?? -1,
                _inlineNamingManualRowBuf,
                _inlineNamingManualColBuf,
                out var err))
        {
            _inlineAssignError = err;
            return;
        }

        _inlineAssignError = "";
    }

    private static void TryDeleteSelectedNamingConvention()
    {
        if (!NamingConventionStore.TryDeleteConvention(_inlineNamingConventionId, out var err))
        {
            _inlineAssignError = err;
            return;
        }

        _inlineNamingConventionId = "";
        _inlineNamingConvRenameBuf = "";
        _inlineAssignError = "";
    }

    private static void ClearInlineNamingPattern()
    {
        _inlineNamingPatternBuf = "";
        _inlineNamingConventionId = "";
        _ipamFormNamingPatternSelectAll = false;
        if (_ipamFormFieldFocus == IpamFormFocusInlineNamingPattern)
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
        }
    }

    private static void DrawInlineNamingTokenPicker(float px, ref float py, float iw)
    {
        var x = px;
        var rowY = py;
        for (var i = 0; i < NamingTokenButtons.Length; i++)
        {
            var def = NamingTokenButtons[i];
            var tw = Mathf.Max(52f, def.Label.Length * 7f + 12f);
            if (x + tw > px + iw && x > px)
            {
                x = px;
                rowY += 26f;
            }

            if (ImguiButtonOnce(new Rect(x, rowY, tw, 22f), def.Label, 9090 + i, _stMutedBtn))
            {
                AppendInlineNamingToken(def.Insert);
            }

            x += tw + 4f;
        }

        py = rowY + 28f;
    }

    private static void AppendInlineNamingToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        var buf = _inlineNamingPatternBuf ?? "";
        if (!string.IsNullOrEmpty(buf) && !buf.EndsWith("-", StringComparison.Ordinal))
        {
            buf += "-";
        }

        if (buf.Length + token.Length > 128)
        {
            return;
        }

        _inlineNamingPatternBuf = buf + token;
    }

    private static void TrySaveInlineNamingConvention(CustomerBase cust)
    {
        var name = (_inlineNamingSaveNameBuf ?? "").Trim();
        if (!NamingConventionStore.TrySaveConvention(
                name,
                _inlineNamingPatternBuf,
                _inlineNamingSeqStart,
                _inlineNamingSeqStep,
                _inlineNamingSeqPad,
                (NamingCounterScope)_inlineNamingCounterScope,
                (NamingSortOrder)_inlineNamingSortOrder,
                cust?.customerID ?? -1,
                _inlineNamingManualRowBuf,
                _inlineNamingManualColBuf,
                out var entry,
                out var err))
        {
            _inlineAssignError = err;
            return;
        }

        _inlineNamingConventionId = entry?.Id ?? "";
        if (cust != null && _inlineNamingSetCustomerDefault && !string.IsNullOrEmpty(entry?.Id))
        {
            NamingConventionStore.SetCustomerDefaultConvention(
                cust.customerID,
                entry.Id,
                _inlineNamingAutoApplyAfterAssign);
        }

        if (cust != null && !string.IsNullOrWhiteSpace(_inlineNamingAbbrevBuf))
        {
            NamingConventionStore.SetCustomerAbbreviation(cust.customerID, _inlineNamingAbbrevBuf);
        }

        _inlineNamingSaveDialogOpen = false;
        _inlineAssignError = "";
    }

    private static void ApplyInlineNamingAssign()
    {
        _inlineAssignError = "";
        CollectSelectedServersIntoScratch();
        if (SelectedServersScratch.Count == 0)
        {
            _inlineAssignError = "No servers in selection.";
            return;
        }

        var cust = _inlineAssignCustomer;
        if (cust != null && !string.IsNullOrWhiteSpace(_inlineNamingAbbrevBuf))
        {
            NamingConventionStore.SetCustomerAbbreviation(cust.customerID, _inlineNamingAbbrevBuf);
        }

        if (string.IsNullOrWhiteSpace(_inlineNamingPatternBuf))
        {
            _inlineAssignError = "Enter a naming pattern.";
            return;
        }

        if (!TryValidateInlineNamingRackFields(out var rackErr))
        {
            _inlineAssignError = rackErr;
            return;
        }

        var options = BuildInlineNamingOptions();
        var preview = NamingTemplateEngine.BuildPreview(SelectedServersScratch, cust, options);
        var dup = preview.Find(r => !string.IsNullOrEmpty(r.Warning)
                                    && r.Warning.StartsWith("duplicate", StringComparison.OrdinalIgnoreCase));
        if (dup != null)
        {
            _inlineAssignError = "Collision: two devices would get the same name.";
            return;
        }

        if (_inlineNamingDryRun)
        {
            _inlineAssignError = "Dry run OK — preview only, no names written.";
            return;
        }

        if (!NamingTemplateEngine.TryApply(preview, options, cust, out var err))
        {
            _inlineAssignError = err ?? "Naming apply failed.";
            return;
        }

        InvalidateDeviceCache();
        BeginImGuiInputRecoveryBurst();
        _inlineAssignError = "";
    }

    internal static void TryAutoApplyNamingAfterAssign(CustomerBase cust, IReadOnlyList<Server> servers)
    {
        if (cust == null || servers == null || servers.Count == 0)
        {
            return;
        }

        var convId = NamingConventionStore.TryGetCustomerDefaultConventionId(cust.customerID);
        var conv = NamingConventionStore.TryGetConventionById(convId);
        if (conv == null || !conv.AutoApplyAfterAssign)
        {
            return;
        }

        var options = new NamingApplyOptions
        {
            Pattern = conv.Pattern ?? "",
            SeqStart = conv.SeqStart > 0 ? conv.SeqStart : 1,
            SeqStep = conv.SeqStep > 0 ? conv.SeqStep : 1,
            SeqPad = conv.SeqPad,
            CounterScope = NamingConventionStore.ParseScope(conv.CounterScope),
            SortOrder = NamingConventionStore.ParseSort(conv.SortOrder),
            ManualRow = (conv.ManualRow ?? "").Trim().ToUpperInvariant(),
            ManualCol = (conv.ManualCol ?? "").Trim(),
        };

        var preview = NamingTemplateEngine.BuildPreview(servers, cust, options);
        if (NamingTemplateEngine.TryApply(preview, options, cust, out _))
        {
            InvalidateDeviceCache();
        }
    }
}
