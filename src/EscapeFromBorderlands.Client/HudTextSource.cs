using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace EscapeFromBorderlands.Client;

// HUD reference-valued members are stable metadata, not stable values. Compile
// the access once and still read the live template/font/material every update.
internal static class HudTextSource
{
    private static readonly Dictionary<(Type, string), Func<object, object?>> Readers = new();
    internal static object? Read(object? target, string name)
    {
        if (target is null) return null;
        var key = (target.GetType(), name);
        if (!Readers.TryGetValue(key, out var read)) Readers[key] = read = Bind(key.Item1, name);
        return read(target);
    }
    private static Func<object, object?> Bind(Type runtime, string name)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            PropertyInfo? property = null; FieldInfo? field = null;
            for (var type = runtime; type is not null && property is null; type = type.BaseType) property = type.GetProperty(name, flags);
            if (property is null)
                for (var type = runtime; type is not null && field is null; type = type.BaseType) field = type.GetField(name, flags);
            var getter = property?.GetGetMethod(true);
            var result = property?.PropertyType ?? field?.FieldType;
            if (runtime.IsValueType || result is null || result.IsValueType || result.IsByRef || result.IsPointer ||
                getter?.IsStatic == true || field?.IsStatic == true ||
                property is not null && (getter is null || property.GetIndexParameters().Length != 0))
                return value => Native.Get(value, name);
            var method = new DynamicMethod("EfbHudRead_" + name, typeof(object), new[] { typeof(object) }, typeof(HudTextSource), true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, getter?.DeclaringType ?? field!.DeclaringType!);
            if (getter is not null) il.Emit(OpCodes.Callvirt, getter); else il.Emit(OpCodes.Ldfld, field!);
            il.Emit(OpCodes.Ret);
            return (Func<object, object?>)method.CreateDelegate(typeof(Func<object, object?>));
        }
        catch { return value => Native.Get(value, name); }
    }
}
