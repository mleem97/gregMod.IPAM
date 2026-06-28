using System;

using System.Reflection;

using UnityEngine;



namespace GregModIPAM;



/// <summary>Writes display names to game devices (reflection + AssetManagementDeviceLine + save remap).</summary>

internal static class DeviceNamingWriter

{

    private static readonly string[] DisplayNameWriteMembers =

    {

        "lastDisplayedLabel", "LastDisplayedLabel",

        "configuredServerName", "ConfiguredServerName", "rackServerName", "RackServerName",

        "contractServerName", "ContractServerName", "userServerName", "UserServerName",

        "pendingDisplayName", "PendingDisplayName", "txtName", "TxtName",

        "displayName", "DisplayName", "serverName", "ServerName", "deviceName", "DeviceName",

        "label", "Label", "labelText", "LabelText", "nameText", "NameText",

        "rackLabel", "RackLabel", "networkName", "NetworkName", "customName", "CustomName",

    };



    private static readonly string[] UiTextObjectMembers =

    {

        "deviceNameText", "DeviceNameText", "labelText", "LabelText", "txtName", "TxtName",

        "m_Player_Label", "displayText", "DisplayText", "floatingText", "FloatingText",

        "nameText", "NameText", "lineText", "LineText",

    };



    private static readonly string[] RefreshMethodNames =

    {

        "UpdateText", "UpdateDisplay", "RefreshBidirectionalLabel", "RefreshProtocolLabel", "ChangeText",

    };



    private static readonly string[] CommitStringMethodNames =

    {

        "OnEndEditingInputText",

    };



    internal static bool TrySetDeviceDisplayName(UnityEngine.Object o, string name)

    {

        if (o == null || string.IsNullOrWhiteSpace(name))

        {

            return false;

        }



        name = name.Trim();

        if (o is Server srv)

        {

            return TrySetServerDisplayName(srv, name);

        }



        if (o is NetworkSwitch sw)

        {

            return TrySetSwitchDisplayName(sw, name);

        }



        return TryWriteStringMembers(o, DisplayNameWriteMembers, name)

               || TrySetUiTextMembers(o, name);

    }



    internal static bool TrySetServerDisplayName(Server server, string name)

    {

        return TrySetServerDisplayName(server, name, null);

    }



    internal static bool TrySetServerDisplayName(

        Server server,

        string name,

        System.Collections.Generic.IReadOnlyDictionary<int, AssetManagementDeviceLine> lineByServerInstanceId)

    {

        if (server == null || string.IsNullOrWhiteSpace(name))

        {

            return false;

        }



        name = name.Trim();

        var oldId = TryGetDeviceId(server);

        var wrote = false;



        wrote |= TryWriteStringMembers(server, DisplayNameWriteMembers, name);

        wrote |= TrySetDirectLastDisplayedLabel(server, name);

        wrote |= TrySetUiTextMembers(server, name);

        wrote |= GameSubnetHelper.TrySetServerAssetLineDisplayName(server, name, lineByServerInstanceId);

        wrote |= TryInvokeRefreshMethods(server);

        wrote |= TryRemapDeviceId(oldId, name);

        wrote |= TryInvokeCommitStringMethods(server, name);



        return wrote;

    }



    internal static bool TrySetSwitchDisplayName(NetworkSwitch sw, string name)

    {

        if (sw == null || string.IsNullOrWhiteSpace(name))

        {

            return false;

        }



        name = name.Trim();

        var oldId = TryGetDeviceId(sw);

        var wrote = false;



        wrote |= TryWriteStringMembers(sw, DisplayNameWriteMembers, name);

        wrote |= TrySetDirectLastDisplayedLabel(sw, name);

        wrote |= TrySetUiTextMembers(sw, name);

        wrote |= TryRemapDeviceId(oldId, name);

        wrote |= TryInvokeRefreshMethods(sw);

        wrote |= TryInvokeCommitStringMethods(sw, name);



        return wrote;

    }



    private static bool TrySetDirectLastDisplayedLabel(object o, string name)

    {

        try

        {

            if (o is Server srv)

            {

                srv.lastDisplayedLabel = name;

                return string.Equals((srv.lastDisplayedLabel ?? "").Trim(), name, StringComparison.Ordinal);

            }



            if (o is NetworkSwitch sw)

            {

                sw.lastDisplayedLabel = name;

                return string.Equals((sw.lastDisplayedLabel ?? "").Trim(), name, StringComparison.Ordinal);

            }

        }

        catch

        {

            // Il2Cpp

        }



        return false;

    }



    private static string TryGetDeviceId(object o)

    {

        if (o == null)

        {

            return null;

        }



        for (var bt = o.GetType(); bt != null && bt != typeof(object); bt = bt.BaseType)

        {

            try

            {

                var m = bt.GetMethod(

                    "GetDeviceId",

                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,

                    null,

                    Type.EmptyTypes,

                    null);

                if (m != null && m.ReturnType == typeof(string))

                {

                    var id = m.Invoke(o, null) as string;

                    if (!string.IsNullOrWhiteSpace(id))

                    {

                        return id.Trim();

                    }

                }

            }

            catch

            {

                // Il2Cpp

            }



            if (TryReadStringMember(o, new[] { "deviceId", "DeviceId", "serverId", "ServerId", "ServerID" }, out var fromField)

                && !string.IsNullOrWhiteSpace(fromField))

            {

                return fromField.Trim();

            }

        }



        return null;

    }



    private static bool TryRemapDeviceId(string oldId, string newName)

    {

        if (string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newName))

        {

            return false;

        }



        if (string.Equals(oldId.Trim(), newName.Trim(), StringComparison.Ordinal))

        {

            return false;

        }



        var targets = new object[4];

        var count = 0;

        try

        {

            if (MainGameManager.instance != null)

            {

                targets[count++] = MainGameManager.instance;

            }

        }

        catch

        {

            // Il2Cpp

        }



        var wrote = false;

        for (var i = 0; i < count; i++)

        {

            wrote |= TryInvokeTwoStringMethod(targets[i], "RemapDeviceId", oldId.Trim(), newName.Trim());

        }



        return wrote;

    }



    private static bool TrySetUiTextMembers(object o, string value)

    {

        if (o == null || string.IsNullOrWhiteSpace(value))

        {

            return false;

        }



        var wrote = false;

        for (var bt = o.GetType(); bt != null && bt != typeof(object); bt = bt.BaseType)

        {

            foreach (var memberName in UiTextObjectMembers)

            {

                try

                {

                    var p = bt.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (p != null && p.CanRead)

                    {

                        wrote |= TrySetTextOnComponent(p.GetValue(o), value);

                    }



                    var f = bt.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (f != null)

                    {

                        wrote |= TrySetTextOnComponent(f.GetValue(o), value);

                    }

                }

                catch

                {

                    // Il2Cpp

                }

            }

        }



        return wrote;

    }



    private static bool TrySetTextOnComponent(object textComponent, string value)

    {

        if (textComponent == null || string.IsNullOrWhiteSpace(value))

        {

            return false;

        }



        for (var bt = textComponent.GetType(); bt != null && bt != typeof(object); bt = bt.BaseType)

        {

            var textProp = bt.GetProperty(

                "text",

                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            if (textProp != null && textProp.CanWrite && textProp.PropertyType == typeof(string))

            {

                try

                {

                    textProp.SetValue(textComponent, value);

                    return true;

                }

                catch

                {

                    // Il2Cpp

                }

            }

        }



        return false;

    }



    private static bool TryInvokeRefreshMethods(object o)

    {

        var invoked = false;

        foreach (var methodName in RefreshMethodNames)

        {

            invoked |= TryInvokeParameterlessMethod(o, methodName);

        }



        return invoked;

    }



    private static bool TryInvokeCommitStringMethods(object o, string name)

    {

        var invoked = false;

        foreach (var methodName in CommitStringMethodNames)

        {

            invoked |= TryInvokeStringMethod(o, methodName, name);

        }



        return invoked;

    }



    private static bool TryInvokeParameterlessMethod(object o, string methodName)

    {

        if (o == null || string.IsNullOrEmpty(methodName))

        {

            return false;

        }



        for (var bt = o.GetType(); bt != null && bt != typeof(object); bt = bt.BaseType)

        {

            try

            {

                var m = bt.GetMethod(

                    methodName,

                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,

                    null,

                    Type.EmptyTypes,

                    null);

                if (m != null && m.ReturnType == typeof(void) && !m.IsAbstract)

                {

                    m.Invoke(o, null);

                    return true;

                }

            }

            catch

            {

                // Il2Cpp

            }

        }



        return false;

    }



    private static bool TryInvokeStringMethod(object o, string methodName, string arg)

    {

        if (o == null || string.IsNullOrEmpty(methodName))

        {

            return false;

        }



        for (var bt = o.GetType(); bt != null && bt != typeof(object); bt = bt.BaseType)

        {

            try

            {

                var m = bt.GetMethod(

                    methodName,

                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,

                    null,

                    new[] { typeof(string) },

                    null);

                if (m != null && m.ReturnType == typeof(void) && !m.IsAbstract)

                {

                    m.Invoke(o, new object[] { arg });

                    return true;

                }

            }

            catch

            {

                // Il2Cpp

            }

        }



        return false;

    }



    private static bool TryInvokeTwoStringMethod(object o, string methodName, string arg0, string arg1)

    {

        if (o == null || string.IsNullOrEmpty(methodName))

        {

            return false;

        }



        for (var bt = o.GetType(); bt != null && bt != typeof(object); bt = bt.BaseType)

        {

            try

            {

                var m = bt.GetMethod(

                    methodName,

                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,

                    null,

                    new[] { typeof(string), typeof(string) },

                    null);

                if (m != null && m.ReturnType == typeof(void) && !m.IsAbstract)

                {

                    m.Invoke(o, new object[] { arg0, arg1 });

                    return true;

                }

            }

            catch

            {

                // Il2Cpp

            }

        }



        return false;

    }



    private static bool TryWriteStringMembers(object o, string[] names, string value)

    {

        if (o == null || names == null)

        {

            return false;

        }



        var wrote = false;

        for (var bt = o.GetType(); bt != null && bt != typeof(object); bt = bt.BaseType)

        {

            foreach (var memberName in names)

            {

                try

                {

                    var p = bt.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (p != null && p.CanWrite && p.PropertyType == typeof(string))

                    {

                        p.SetValue(o, value);

                        wrote = true;

                    }



                    var f = bt.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (f != null && f.FieldType == typeof(string))

                    {

                        f.SetValue(o, value);

                        wrote = true;

                    }

                }

                catch

                {

                    // Il2Cpp

                }

            }

        }



        return wrote;

    }



    private static bool TryReadStringMember(object o, string[] names, out string value)

    {

        value = null;

        if (o == null || names == null)

        {

            return false;

        }



        for (var bt = o.GetType(); bt != null && bt != typeof(object); bt = bt.BaseType)

        {

            foreach (var memberName in names)

            {

                try

                {

                    var p = bt.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (p != null && p.CanRead && p.PropertyType == typeof(string))

                    {

                        value = p.GetValue(o) as string;

                        if (!string.IsNullOrWhiteSpace(value))

                        {

                            return true;

                        }

                    }



                    var f = bt.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (f != null && f.FieldType == typeof(string))

                    {

                        value = f.GetValue(o) as string;

                        if (!string.IsNullOrWhiteSpace(value))

                        {

                            return true;

                        }

                    }

                }

                catch

                {

                    // Il2Cpp

                }

            }

        }



        return false;

    }

}


