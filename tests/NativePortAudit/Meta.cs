using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

public static class Meta
{
    public const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static MetadataLoadContext Context=null!;
    static Assembly Game=null!,Client=null!;
    static readonly Dictionary<string,Type> Types=new();
    static readonly Dictionary<string,Type?> Lookups=new();
    static readonly Dictionary<int,int> Parents=new();
    public static void Initialize(string refs,string client,string? optionalUia=null)
    {
        var paths=Directory.GetFiles(Path.Combine(refs,"Client"),"*.dll",SearchOption.AllDirectories).Append(client)
            .Concat(optionalUia is null ? Array.Empty<string>() : new[]{optionalUia})
            .Concat(((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)).Select(Path.GetFullPath)
            .GroupBy(Path.GetFileName,StringComparer.OrdinalIgnoreCase).Select(g=>g.First());
        Context=new MetadataLoadContext(new PathAssemblyResolver(paths),"mscorlib");
        Game=Context.LoadFromAssemblyPath(Path.Combine(refs,"Client/Assembly-CSharp.dll"));
        Client=Context.LoadFromAssemblyPath(client);
        if(optionalUia is not null)foreach(var type in Context.LoadFromAssemblyPath(optionalUia).GetTypes())Types.TryAdd(type.FullName!,type);
        foreach(var a in new[]{Game,Client,Context.LoadFromAssemblyPath(Path.Combine(refs,"Client/System.Core.dll")),Context.LoadFromAssemblyPath(Path.Combine(refs,"Client/SPT/spt-common.dll")),Context.LoadFromAssemblyPath(Path.Combine(refs,"Client/UnityEngine.CoreModule.dll")),Context.LoadFromAssemblyPath(Path.Combine(refs,"Client/UnityEngine.AudioModule.dll")),Context.LoadFromAssemblyPath(Path.Combine(refs,"Client/UnityEngine.UI.dll")),Context.LoadFromAssemblyPath(Path.Combine(refs,"Client/Comfort.dll")),Context.CoreAssembly!})
            foreach(var t in a.GetTypes())Types.TryAdd(t.FullName!,t);
        using var pe=new PEReader(File.OpenRead(Path.Combine(refs,"Client/Assembly-CSharp.dll")));var md=pe.GetMetadataReader();
        foreach(var handle in md.TypeDefinitions){var parent=md.GetTypeDefinition(handle).BaseType;if(parent.Kind==HandleKind.TypeDefinition)Parents[MetadataTokens.GetToken(handle)]=MetadataTokens.GetToken(parent);}
    }
    public static Type T(string name)
    {
        if(Lookups.TryGetValue(name,out var cached))return cached??throw new Exception("Unavailable type "+name);
        var primitives=new Dictionary<string,string>{{"float","System.Single"},{"int","System.Int32"},{"string","System.String"},{"bool","System.Boolean"},{"object","System.Object"},{"void","System.Void"}};
        if(primitives.TryGetValue(name,out var scalar))return Types[scalar];
        if(name.EndsWith("<>"))return T(name[..^2]+"`1");
        if(name.EndsWith('>')){var i=name.IndexOf('<');var arguments=name[(i+1)..^1].Split(',').Select(T).ToArray();return T(name[..i]+"`"+arguments.Length).MakeGenericType(arguments);}
        if(Types.TryGetValue(name,out var direct))return direct;
        var matches=Types.Where(kv=>name.Contains('.') || kv.Value.DeclaringType is null).Where(kv=>kv.Key.Replace('+','.')==name||kv.Key.Replace('+','.').EndsWith("."+name,StringComparison.Ordinal)).Select(kv=>kv.Value).ToArray();
        Lookups[name]=matches.Length==1?matches[0]:null;
        return matches.Length==1?matches[0]:throw new Exception("Type resolution "+name+": "+matches.Length);
    }
    public static Type? AsyncType(MethodInfo method)
    {
        var attribute=method.GetCustomAttributesData().SingleOrDefault(a=>a.AttributeType.Name=="AsyncStateMachineAttribute");
        return attribute?.ConstructorArguments[0].Value as Type;
    }
    public static bool Assignable(Type parent,Type type)
    {
        if(parent.Assembly==Game&&type.Assembly==Game){var token=type.MetadataToken;do{if(token==parent.MetadataToken)return true;}while(Parents.TryGetValue(token,out token));return false;}
        return parent.IsAssignableFrom(type);
    }
    public static Type? Find(string name)=>Types.GetValueOrDefault(name);
    public static Assembly[] Assemblies()=>Context.GetAssemblies().ToArray();
    public static MethodBase[] AttributeTargets(string patch)
    {
        var type=Client.GetType("EscapeFromBorderlands.Client."+patch,true)!;
        var attribute=type.GetCustomAttributesData().Single(a=>a.AttributeType.FullName=="HarmonyLib.HarmonyPatch");
        var args=attribute.ConstructorArguments;
        if(args.Count<2)throw new Exception("The dynamic selector failed; no attribute target is available for "+patch);
        var target=(Type)args[0].Value!;var name=(string)args[1].Value!;
        Type[]? parameters=null;
        if(args.Count==3){if(args[2].ArgumentType.IsArray)parameters=((IEnumerable<CustomAttributeTypedArgument>)args[2].Value!).Select(a=>(Type)a.Value!).ToArray();else if(Convert.ToInt32(args[2].Value)==1)name="get_"+name;else throw new Exception("Unsupported Harmony method kind");}
        return new[]{AccessTools.Method(target,name,parameters)??throw new Exception("Missing attribute target "+patch)};
    }
}

public static class AccessTools
{
    static IEnumerable<Type> Owners(Type? type){while(type is not null){yield return type;type=type.BaseType;}}
    public static MethodInfo? Method(Type? type,string name,Type[]? parameters=null,Type[]? generics=null)=>Find(type,name,parameters,false);
    public static MethodInfo? DeclaredMethod(Type? type,string name,Type[]? parameters=null)=>Find(type,name,parameters,true);
    static MethodInfo? Find(Type? type,string name,Type[]? parameters,bool declared)
    {
        foreach(var owner in declared?(type is null?Array.Empty<Type>():new[]{type}):Owners(type))
        {
            var methods=owner.GetMethods(Meta.All|BindingFlags.DeclaredOnly).Where(m=>m.Name==name&&(parameters is null||m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(parameters))).ToArray();
            if(methods.Length>1)throw new AmbiguousMatchException(owner.FullName+"."+name);
            if(methods.Length==1)return methods[0];
        }
        return null;
    }
    public static MethodInfo? PropertyGetter(Type? type,string name)=>Owners(type).Select(t=>t.GetProperty(name,Meta.All|BindingFlags.DeclaredOnly)?.GetMethod).FirstOrDefault(m=>m is not null);
    public static FieldInfo? Field(Type? type,string name)=>Owners(type).Select(t=>t.GetField(name,Meta.All|BindingFlags.DeclaredOnly)).FirstOrDefault(m=>m is not null);
    public static ConstructorInfo? Constructor(Type type,Type[] parameters)=>type.GetConstructors(Meta.All).SingleOrDefault(c=>c.GetParameters().Select(p=>p.ParameterType).SequenceEqual(parameters));
    public static Type? TypeByName(string name)=>Meta.Find(name);
    public static Type[] GetTypesFromAssembly(Assembly assembly)=>assembly.GetTypes();
    public static MethodInfo? EnumeratorMoveNext(MethodBase method)
    {
        var attribute=method.GetCustomAttributesData().Single(a=>a.AttributeType.Name is "IteratorStateMachineAttribute" or "AsyncStateMachineAttribute");
        return ((Type)attribute.ConstructorArguments[0].Value!).GetMethod("MoveNext",Meta.All);
    }
}

// Controlled static readiness flag for the extracted factory transpiler.
public static class ReusableGrenadeRuntime { public static bool FactoryHookReady; }
