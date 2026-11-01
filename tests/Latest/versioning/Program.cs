using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if(args.Length!=3)throw new ArgumentException("Usage: expected-semver client.dll server.dll");
var expected=args[0];
if(!Regex.IsMatch(expected,@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"))throw new Exception("Expected version must use MAJOR.MINOR.PATCH");
var opcodes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>unchecked((ushort)o.Value));
var results=new List<object>();
foreach(var path in args.Skip(1))
{
    using var file=File.OpenRead(path);using var pe=new PEReader(file);var md=pe.GetMetadataReader();
    var definition=md.GetAssemblyDefinition();var raw=definition.Version.ToString();
    if(raw!=expected+".0")throw new Exception(path+": CLR assembly version mismatch: "+raw);
    var attributes=new Dictionary<string,string>();string? pluginAttributeVersion=null;string? pluginConstantVersion=null;
    string AttributeName(CustomAttribute a)
    {
        if(a.Constructor.Kind!=HandleKind.MemberReference)return "";
        var parent=md.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent;
        if(parent.Kind!=HandleKind.TypeReference)return "";
        var t=md.GetTypeReference((TypeReferenceHandle)parent);
        return md.GetString(t.Namespace)+"."+md.GetString(t.Name);
    }
    foreach(var handle in definition.GetCustomAttributes())
    {
        var a=md.GetCustomAttribute(handle);var name=AttributeName(a);
        if(name is not ("System.Reflection.AssemblyFileVersionAttribute" or "System.Reflection.AssemblyInformationalVersionAttribute"))continue;
        var blob=md.GetBlobReader(a.Value);if(blob.ReadUInt16()!=1)throw new Exception("Invalid version attribute");
        attributes[name]=blob.ReadSerializedString()??"";
    }
    foreach(var key in new[]{"System.Reflection.AssemblyFileVersionAttribute","System.Reflection.AssemblyInformationalVersionAttribute"})
        if(!attributes.TryGetValue(key,out var value)||value!=expected)throw new Exception(path+": "+key+" does not match "+expected);
    var metadataVersions=new HashSet<string>();
    foreach(var handle in md.TypeDefinitions)
    {
        var type=md.GetTypeDefinition(handle);var name=md.GetString(type.Name);
        if(name=="Plugin")
        {
            foreach(var fieldHandle in type.GetFields())
            {
                var field=md.GetFieldDefinition(fieldHandle);
                if(md.GetString(field.Name)!="PluginVersion")continue;
                var constant=md.GetConstant(field.GetDefaultValue());pluginConstantVersion=Encoding.Unicode.GetString(md.GetBlobBytes(constant.Value));
            }
            foreach(var attributeHandle in type.GetCustomAttributes())
            {
                var a=md.GetCustomAttribute(attributeHandle);if(AttributeName(a)!="BepInEx.BepInPlugin")continue;
                var blob=md.GetBlobReader(a.Value);if(blob.ReadUInt16()!=1)throw new Exception("Invalid plugin attribute");
                _=blob.ReadSerializedString();_=blob.ReadSerializedString();pluginAttributeVersion=blob.ReadSerializedString();
            }
        }
        if(name!="ModMetadata")continue;
        foreach(var methodHandle in type.GetMethods())
        {
            var method=md.GetMethodDefinition(methodHandle);if(method.RelativeVirtualAddress==0)continue;
            var il=pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            for(var i=0;i<il.Length;)
            {
                ushort code=il[i++];if(code==0xfe)code=(ushort)(0xfe00|il[i++]);var op=opcodes[code];
                if(op.OperandType==OperandType.InlineString)
                {
                    var token=BitConverter.ToInt32(il,i);var value=md.GetUserString(MetadataTokens.UserStringHandle(token&0x00ffffff));
                    if(Regex.IsMatch(value,@"^[0-9]+\.[0-9]+\.[0-9]+$"))metadataVersions.Add(value);
                }
                i+=op.OperandType switch
                {
                    OperandType.InlineNone=>0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,
                    OperandType.InlineVar=>2,
                    OperandType.InlineI8 or OperandType.InlineR=>8,
                    OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(il,i),
                    _=>4
                };
            }
        }
    }
    if(path.Contains(".Client.",StringComparison.Ordinal)&&(pluginAttributeVersion!=expected||pluginConstantVersion!=expected))throw new Exception("Compiled BepInEx version mismatch");
    if(path.Contains(".Server.",StringComparison.Ordinal)&&!metadataVersions.SetEquals(new[]{expected}))throw new Exception("Compiled SPT metadata version mismatch: "+string.Join(",",metadataVersions));
    results.Add(new{assembly=Path.GetFileName(path),releaseVersion=expected,clrAssemblyVersion=raw,fileVersion=attributes["System.Reflection.AssemblyFileVersionAttribute"],informationalVersion=attributes["System.Reflection.AssemblyInformationalVersionAttribute"],pluginAttributeVersion,pluginConstantVersion,modMetadataVersions=metadataVersions});
}
Console.WriteLine(JsonSerializer.Serialize(new{version=expected,assemblies=results,errors=0},new JsonSerializerOptions{WriteIndented=true}));
