using System;
using System.Reflection;

namespace EscapeFromBorderlands.Client;

// Bind once, retaining reflection when the host cannot bind a protected method.
// This still performs MemberwiseClone: reference fields and runtime type survive.
internal static class NativeShallowClone
{
    private static readonly MethodInfo Method = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly Func<object, object> CopyObject = Bind();
    private static Func<object, object> Bind()
    {
        try { return (Func<object, object>)Delegate.CreateDelegate(typeof(Func<object, object>), Method); }
        catch { return value => Method.Invoke(value, null)!; }
    }
    internal static object Copy(object value) => CopyObject(value);
    private static bool Prepared;
    internal static void Prepare()
    {
        if (Prepared) return;
        // Warm the delegate/reflection fallback with a plain managed object.
        // No native hit or projectile is constructed or modified.
        Prepared = true;
        try { _ = CopyObject(new object()); }
        catch (Exception) { /* Optional warmup must not interrupt menu Update. */ }
    }
}
