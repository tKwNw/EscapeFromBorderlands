using System;using System.Reflection;using System.Linq;using System.Collections;using System.Collections.Generic;using EFT.InventoryLogic;using HarmonyLib;
namespace EscapeFromBorderlands.Client;
internal static class Native
{
    private static readonly Dictionary<(Type, string), MemberInfo?> Members = new();
    internal static object? Get(object? obj, string name)
    {
        if (obj is null) return null;
        var key = (obj.GetType(), name);
        if (!Members.TryGetValue(key, out var m)) Members[key] = m = (MemberInfo?)AccessTools.Property(key.Item1, name) ?? AccessTools.Field(key.Item1, name);
        return m is PropertyInfo p ? p.GetValue(obj) : m is FieldInfo f ? f.GetValue(obj) : null;
    }
    internal static void Set(object obj, string name, object? value)
    {
        var p = AccessTools.Property(obj.GetType(), name);
        if (p?.SetMethod is not null) p.SetValue(obj, value); else AccessTools.Field(obj.GetType(), name)?.SetValue(obj, value);
    }
    internal static object? Call(object obj, string name, params object?[] args)
    {
        var method = AccessTools.GetDeclaredMethods(obj.GetType()).Concat(obj.GetType().GetMethods()).FirstOrDefault(m => m.Name == name && !m.ContainsGenericParameters && m.GetParameters().Length == args.Length);
        return method?.Invoke(obj, args);
    }
    internal static T? Component<T>(Item item) where T : class
    {
        var method = typeof(Item).GetMethods().First(m => m.Name == "GetItemComponent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
        return method.MakeGenericMethod(typeof(T)).Invoke(item, null) as T;
    }
    internal static float Number(object obj, string name) => Get(obj, name) is { } n ? Convert.ToSingle(n) : 0f;
    internal static IEnumerable<object> Items(object? value) => value is IEnumerable e ? e.Cast<object>() : Enumerable.Empty<object>();
}
