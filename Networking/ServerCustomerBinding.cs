using System;
using System.Reflection;

namespace DHCPSwitches;

/// <summary>
/// Tries to move a <see cref="Server"/> to another contract via the game's API (reflection; IL2CPP best-effort).
/// </summary>
internal static class ServerCustomerBinding
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    internal static bool TryBindServerToCustomer(Server server, CustomerBase customer, out string error)
    {
        error = null;
        if (server == null)
        {
            error = "No server.";
            return false;
        }

        if (customer == null)
        {
            error = "No customer.";
            return false;
        }

        var t = server.GetType();
        var cbRuntime = customer.GetType();

        try
        {
            var item = customer.customerItem;
            if (item != null)
            {
                var it = item.GetType();
                for (var bt = t; bt != null; bt = bt.BaseType)
                {
                    foreach (var m in bt.GetMethods(Declared))
                    {
                        if (IsIgnorableAccessorName(m.Name))
                        {
                            continue;
                        }

                        var ps = m.GetParameters();
                        if (ps.Length != 1 || !ps[0].ParameterType.IsAssignableFrom(it))
                        {
                            continue;
                        }

                        if (!NameSuggestsCustomerSellerOrContract(m.Name))
                        {
                            continue;
                        }

                        m.Invoke(server, new object[] { item });
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        foreach (var methodName in new[]
                 {
                     "SetCustomer", "AssignCustomer", "LinkCustomer", "BindToCustomer", "SetCustomerBase", "AssignToCustomer",
                     "SetSeller", "AssignSeller", "LinkSeller", "BindSeller",
                 })
        {
            try
            {
                for (var bt = t; bt != null; bt = bt.BaseType)
                {
                    var m = bt.GetMethod(
                        methodName,
                        Inst,
                        null,
                        new[] { typeof(CustomerBase) },
                        null);
                    if (m != null)
                    {
                        m.Invoke(server, new object[] { customer });
                        return true;
                    }

                    m = bt.GetMethod(methodName, Inst);
                    if (m != null)
                    {
                        var ps = m.GetParameters();
                        if (ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(cbRuntime))
                        {
                            m.Invoke(server, new object[] { customer });
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        try
        {
            for (var bt = t; bt != null; bt = bt.BaseType)
            {
                foreach (var m in bt.GetMethods(Declared))
                {
                    if (IsIgnorableAccessorName(m.Name))
                    {
                        continue;
                    }

                    if (m.Name.IndexOf("ustomer", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    var ps = m.GetParameters();
                    if (ps.Length != 1)
                    {
                        continue;
                    }

                    if (!typeof(CustomerBase).IsAssignableFrom(ps[0].ParameterType))
                    {
                        continue;
                    }

                    m.Invoke(server, new object[] { customer });
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        return TrySetCustomerId(server, customer.customerID, out error);
    }

    internal static bool TrySetCustomerId(Server server, int newCustomerId, out string error)
    {
        error = null;
        if (server == null)
        {
            error = "No server.";
            return false;
        }

        var t = server.GetType();

        foreach (var name in new[] { "customerID", "CustomerID", "customerId", "CustomerId" })
        {
            for (var bt = t; bt != null; bt = bt.BaseType)
            {
                try
                {
                    var p = bt.GetProperty(name, Inst);
                    if (p == null)
                    {
                        continue;
                    }

                    if (p.CanWrite)
                    {
                        p.SetValue(server, newCustomerId, null);
                        return true;
                    }

                    var sm = p.GetSetMethod(true);
                    if (sm != null)
                    {
                        var ps = sm.GetParameters();
                        if (ps.Length == 1)
                        {
                            sm.Invoke(server, new[] { CoerceInt(ps[0].ParameterType, newCustomerId) });
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        foreach (var name in new[]
                 {
                     "customerID", "CustomerID", "customerId", "CustomerId", "_customerID", "_customerId",
                     "m_CustomerID", "m_customerId", "<customerID>k__BackingField", "<CustomerID>k__BackingField",
                 })
        {
            for (var bt = t; bt != null; bt = bt.BaseType)
            {
                try
                {
                    var f = bt.GetField(name, Inst);
                    if (f == null || f.IsLiteral || f.IsInitOnly)
                    {
                        continue;
                    }

                    if (IsIntegralType(f.FieldType))
                    {
                        f.SetValue(server, CoerceInt(f.FieldType, newCustomerId));
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        for (var bt = t; bt != null; bt = bt.BaseType)
        {
            try
            {
                foreach (var f in bt.GetFields(Declared))
                {
                    if (f.IsLiteral || f.IsInitOnly)
                    {
                        continue;
                    }

                    if (f.Name.IndexOf("ustomer", StringComparison.OrdinalIgnoreCase) < 0
                        && f.Name.IndexOf("eller", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    if (!IsIntegralType(f.FieldType))
                    {
                        continue;
                    }

                    f.SetValue(server, CoerceInt(f.FieldType, newCustomerId));
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        foreach (var methodName in new[]
                 {
                     "SetCustomerID", "set_CustomerID", "AssignCustomerID", "SetCustomerId", "AssignCustomerId",
                     "set_customerID", "set_customerId", "SetSellerID", "SetSellerId", "AssignSellerID",
                 })
        {
            for (var bt = t; bt != null; bt = bt.BaseType)
            {
                try
                {
                    foreach (var pt in new[] { typeof(int), typeof(uint), typeof(long), typeof(short) })
                    {
                        var m = bt.GetMethod(methodName, Inst, null, new[] { pt }, null);
                        if (m == null)
                        {
                            continue;
                        }

                        m.Invoke(server, new[] { CoerceInt(pt, newCustomerId) });
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        for (var bt = t; bt != null; bt = bt.BaseType)
        {
            try
            {
                foreach (var m in bt.GetMethods(Declared))
                {
                    var n = m.Name;
                    if (n.StartsWith("get_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var ps = m.GetParameters();
                    if (ps.Length != 1 || !IsIntegralType(ps[0].ParameterType))
                    {
                        continue;
                    }

                    var idHint = n.IndexOf("ustomer", StringComparison.OrdinalIgnoreCase) >= 0
                                 || n.IndexOf("Seller", StringComparison.OrdinalIgnoreCase) >= 0
                                 || (n.StartsWith("set_", StringComparison.OrdinalIgnoreCase)
                                     && n.IndexOf("ustomer", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!idHint)
                    {
                        continue;
                    }

                    m.Invoke(server, new[] { CoerceInt(ps[0].ParameterType, newCustomerId) });
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        error = "Could not set customer on Server (no writable customerID / seller / setter found).";
        return false;
    }

    private static bool NameSuggestsCustomerSellerOrContract(string methodName)
    {
        return methodName.IndexOf("ustomer", StringComparison.OrdinalIgnoreCase) >= 0
               || methodName.IndexOf("Seller", StringComparison.OrdinalIgnoreCase) >= 0
               || methodName.IndexOf("Contract", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsIgnorableAccessorName(string name)
    {
        return name.StartsWith("get_", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("add_", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("remove_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIntegralType(Type pt)
    {
        return pt == typeof(int) || pt == typeof(uint) || pt == typeof(long) || pt == typeof(short)
               || pt == typeof(ushort) || pt == typeof(byte) || pt == typeof(sbyte);
    }

    private static object CoerceInt(Type target, int v)
    {
        if (target == typeof(int))
        {
            return v;
        }

        if (target == typeof(uint))
        {
            return unchecked((uint)v);
        }

        if (target == typeof(long))
        {
            return (long)v;
        }

        if (target == typeof(short))
        {
            return (short)v;
        }

        if (target == typeof(ushort))
        {
            return (ushort)v;
        }

        if (target == typeof(byte))
        {
            return (byte)v;
        }

        if (target == typeof(sbyte))
        {
            return (sbyte)v;
        }

        return Convert.ChangeType(v, target);
    }
}
