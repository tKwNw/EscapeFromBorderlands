using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using CecilInstruction=Mono.Cecil.Cil.Instruction;

// Minimal instruction/lookup surface required by the extracted production
// transpilers. Operands that are not inspected stay opaque; no game IL runs.
public sealed class CodeInstruction
{
    public OpCode opcode;
    public object? operand;
    internal object? Source;
    public CodeInstruction(OpCode code,object? argument=null){opcode=code;operand=argument;}
    public bool Calls(MethodInfo? method)=>(opcode==OpCodes.Call||opcode==OpCodes.Callvirt)&&Equals(operand,method);
    public bool LoadsField(FieldInfo? field)=>opcode==OpCodes.Ldfld&&Equals(operand,field);
}

public sealed class NativeInstructions
{
    readonly ModuleDefinition Module;
    readonly Dictionary<short,OpCode> Opcodes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
    readonly Dictionary<CodeInstruction,(OpCode Opcode,object? Operand)> Original=new();
    readonly Dictionary<int,object?> Operands=new();
    public NativeInstructions(string path){Module=ModuleDefinition.ReadModule(path);}
    public CodeInstruction[] Read(MethodBase method)
    {
        Original.Clear();var native=(MethodDefinition)Module.LookupToken(method.MetadataToken);
        return native.Body.Instructions.Select(i=>{
            object? operand=i.Operand;
            if(operand is MemberReference member){
                var token=member.MetadataToken.ToInt32();
                if(!Operands.TryGetValue(token,out var resolved)){
                    resolved=operand;
                    try{if(operand is FieldReference field)resolved=Resolve(field);else if(operand is MethodReference called)resolved=Resolve(called);}catch{ /* Opaque, unchanged native operand. */ }
                    Operands[token]=resolved;
                }
                operand=resolved;
            }
            var instruction=new CodeInstruction(Opcodes[i.OpCode.Value],operand){Source=i};Original[instruction]=(instruction.opcode,operand);return instruction;
        }).ToArray();
    }
    public int Changes(IEnumerable<CodeInstruction> instructions)=>instructions.Count(i=>!Original.TryGetValue(i,out var old)||i.opcode!=old.Opcode||!Equals(i.operand,old.Operand));
    static Type Resolve(TypeReference type,MethodBase? method=null,Type? owner=null)
    {
        if(type is GenericParameter generic)return generic.Type==GenericParameterType.Method?method!.GetGenericArguments()[generic.Position]:owner!.GetGenericArguments()[generic.Position];
        if(type is GenericInstanceType instance)return Meta.T(instance.ElementType.FullName.Replace('/','+')).MakeGenericType(instance.GenericArguments.Select(t=>Resolve(t,method,owner)).ToArray());
        if(type is ByReferenceType byref)return Resolve(byref.ElementType,method,owner).MakeByRefType();
        if(type is ArrayType array)return array.Rank==1?Resolve(array.ElementType,method,owner).MakeArrayType():Resolve(array.ElementType,method,owner).MakeArrayType(array.Rank);
        return Meta.T(type.FullName.Replace('/','+'));
    }
    static FieldInfo Resolve(FieldReference field)=>AccessTools.Field(Resolve(field.DeclaringType),field.Name)!;
    static MethodBase Resolve(MethodReference reference)
    {
        var owner=Resolve(reference.DeclaringType);
        var candidates=reference.Name==".ctor"?owner.GetConstructors(Meta.All).Cast<MethodBase>():owner.GetMethods(Meta.All|BindingFlags.DeclaredOnly).Where(m=>m.Name==reference.Name).Cast<MethodBase>();
        foreach(var candidate in candidates.Where(m=>m.GetParameters().Length==reference.Parameters.Count))
        {
            MethodBase method=candidate;
            if(reference is GenericInstanceMethod generic){if(method is not MethodInfo definition||!definition.IsGenericMethodDefinition)continue;method=definition.MakeGenericMethod(generic.GenericArguments.Select(t=>Resolve(t,null,owner)).ToArray());}
            var parameters=reference.Parameters.Select(p=>Resolve(p.ParameterType,method,owner));
            if(method.GetParameters().Select(p=>p.ParameterType).SequenceEqual(parameters))return method;
        }
        throw new MissingMethodException(reference.FullName);
    }
}
