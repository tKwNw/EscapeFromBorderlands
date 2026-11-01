using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if(args.Length is not (4 or 5))throw new ArgumentException("references client.dll source-root output.json [optional-uia.dll]");
Meta.Initialize(Path.GetFullPath(args[0]),Path.GetFullPath(args[1]),args.Length==5?Path.GetFullPath(args[4]):null);
var source=Path.GetFullPath(args[2]);
var output=Path.GetFullPath(args[3]);
var sources=new List<string>();
var inventory=new List<object>();
foreach(var file in Directory.GetFiles(Path.Combine(source,"src/EscapeFromBorderlands.Client"),"*.cs").Order())
{
    var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(file));
    foreach(var type in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
    {
        var methods=type.Members.OfType<MethodDeclarationSyntax>().Where(m=>m.Identifier.ValueText is "TargetMethod" or "TargetMethods" or "Transpiler" || type.Identifier.ValueText == "LegendaryQuestItems" && m.Identifier.ValueText == "ContainsCalls" || type.Identifier.ValueText == "LegendaryHollywoodFx" && m.Identifier.ValueText is "Find" or "Targets" || type.Identifier.ValueText == "UseItemsAnywhereCompatibility" && m.Identifier.ValueText is "IsCollector" or "CollectorSignature").ToArray();
        if(methods.Length==0)continue;
        var fields=type.Members.OfType<FieldDeclarationSyntax>().ToArray();
        var used=methods.SelectMany(m=>m.DescendantNodes().OfType<IdentifierNameSyntax>()).Select(n=>n.Identifier.ValueText).ToHashSet();
        var selected=new List<FieldDeclarationSyntax>();
        bool changed;
        do{
            changed=false;
            foreach(var field in fields)
                if(!selected.Contains(field)&&field.Declaration.Variables.Any(v=>used.Contains(v.Identifier.ValueText)))
                {selected.Add(field);used.UnionWith(field.DescendantNodes().OfType<IdentifierNameSyntax>().Select(n=>n.Identifier.ValueText));changed=true;}
        }while(changed);
        var rewrite=new SelectorRewriter();
        var members=selected.Select(f=>(MemberDeclarationSyntax)f).Concat(methods).Select(m=>rewrite.Visit(m)!.ToFullString());
        var generated="using System;using System.Collections;using System.Collections.Generic;using System.Linq;using System.Reflection;using System.Runtime.CompilerServices;using System.Reflection.Emit;\nnamespace ExtractedSelectors {public static class "+type.Identifier.ValueText+"{\n"+string.Join("\n",members)+"\n}}";
        sources.Add(generated);
        inventory.Add(new{patch=type.Identifier.ValueText,file=Path.GetFileName(file),selectors=methods.Count(m=>m.Identifier.ValueText is "TargetMethod" or "TargetMethods"),transpilers=methods.Count(m=>m.Identifier.ValueText=="Transpiler")});
    }
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(Path.ChangeExtension(output,"generated.cs"),string.Join("\n",sources));
var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Append(typeof(Meta).Assembly.Location).Distinct().Select(p=>MetadataReference.CreateFromFile(p));
var compilation=CSharpCompilation.Create("ExtractedSelectors_"+Guid.NewGuid().ToString("N"),sources.Select(s=>CSharpSyntaxTree.ParseText(s)),references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var bytes=new MemoryStream();
var emit=compilation.Emit(bytes);
if(!emit.Success)throw new Exception(string.Join("\n",emit.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
var extracted=Assembly.Load(bytes.ToArray());
var results=new List<object>();int pass=0,fail=0,total=0,skipped=0;
var selectedTargets=new Dictionary<string,MethodBase[]>();
foreach(var type in extracted.GetTypes().Where(t=>t.Namespace=="ExtractedSelectors"&&t.DeclaringType is null).OrderBy(t=>t.Name))
{
    try{
        var selector=type.GetMethods(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly).SingleOrDefault(m=>m.Name is "TargetMethod" or "TargetMethods");
        if(selector is null)continue;
        var value=selector.Invoke(null,null);
        if(value is null && type.Name=="UseItemsAnywhereGrenadeWheelPatch" && Meta.Find("UseItemsAnywhere.QuickUseWheel.QuickUseWheelInventory") is null)
        {skipped++;results.Add(new{patch=type.Name,status="SKIP",reason="Optional UIA assembly absent; controlled adapter tests run separately"});continue;}
        var targets=value is MethodBase one?new[]{one}:value is IEnumerable<MethodBase> many?many.ToArray():throw new Exception("Selector returned null");
        if(targets.Length==0&&type.Name=="LegendaryHollywoodFxBlastPatch")
        {skipped++;results.Add(new{patch=type.Name,status="SKIP",reason="Optional HollywoodFX assembly absent; controlled adapter tests run separately"});continue;}
        if(targets.Length==0||targets.Any(t=>t is null))throw new Exception("Empty or null target");
        total+=targets.Length;pass++;
        selectedTargets[type.Name]=targets;
        results.Add(new{patch=type.Name,status="PASS",targets=targets.Select(t=>new{owner=t.DeclaringType!.FullName,name=t.Name,token=t.MetadataToken,parameters=t.GetParameters().Select(p=>p.Name).ToArray()}).ToArray()});
    }catch(Exception e){fail++;results.Add(new{patch=type.Name,status="FAIL",error=e.GetBaseException().Message});}
}
var transpilers=new List<object>();var ilReader=new NativeInstructions(Path.Combine(Path.GetFullPath(args[0]),"Client/Assembly-CSharp.dll"));
foreach(var type in extracted.GetTypes().Where(t=>t.Namespace=="ExtractedSelectors"&&t.DeclaringType is null).OrderBy(t=>t.Name))
{
    var transpiler=type.GetMethod("Transpiler",BindingFlags.Static|BindingFlags.NonPublic);
    if(transpiler is null)continue;
    Console.WriteLine("Checking native IL: "+type.Name);
    try{
        var targets=selectedTargets.GetValueOrDefault(type.Name)??Meta.AttributeTargets(type.Name);
        foreach(var target in targets)
        {
            var instructions=ilReader.Read(target);
            var arguments=transpiler.GetParameters().Length==1?new object[]{instructions}:new object[]{instructions,target};
            var patched=((IEnumerable<CodeInstruction>)transpiler.Invoke(null,arguments)!).ToArray();
            var inputCount=instructions.Length;var changes=ilReader.Changes(patched);
            if(changes==0)throw new Exception("Transpiler made no changes");
            transpilers.Add(new{patch=type.Name,target=target.DeclaringType!.FullName+"."+target.Name,status="PASS",inputInstructions=inputCount,outputInstructions=patched.Length,changes});
        }
    }catch(Exception e){transpilers.Add(new{patch=type.Name,status="FAIL",error=e.GetBaseException().Message});}
}
var uiaComposition=new List<object>();
if(args.Length==5)
{
 var patch=extracted.GetType("ExtractedSelectors.BackpackConcreteInventorySearchPatch",true)!;
 var transpiler=patch.GetMethod("Transpiler",BindingFlags.Static|BindingFlags.NonPublic)!;
 foreach(var target in selectedTargets[patch.Name])
 {
  try
  {
   var input=ilReader.Read(target);int substitutions=0;
   // Model UIA's call substitution using its ACTUAL supplied method metadata.
   // The mapping and original binary IL are checked by audit_uia.py. The UIA
   // transpiler and game method bodies themselves do not execute here.
   foreach(var op in input)
    if(op.operand is MethodInfo query&&query.Name=="GetReachableItemsOfTypeNonAlloc"&&query.DeclaringType==Meta.T("InventoryController"))
    {
     var name=target.DeclaringType==Meta.T("BotReload")?"CollectNativeItems":target.DeclaringType==Meta.T("EFT.PlayerInputTranslator")?"CollectGrenades":"CollectReloadItems";
     op.opcode=OpCodes.Call;op.operand=Meta.T("UseItemsAnywhere.Patches.InventorySlotQueries").GetMethods(Meta.All).Single(m=>m.Name==name).MakeGenericMethod(query.GetGenericArguments());substitutions++;
    }
   if(substitutions!=1)throw new Exception("Expected one modeled UIA query substitution");
   var outputCode=((IEnumerable<CodeInstruction>)transpiler.Invoke(null,new object[]{input,target})!).ToArray();
   var expected=target.DeclaringType==Meta.T("BotReload")?"BotMagazines":target.DeclaringType==Meta.T("EFT.PlayerInputTranslator")?"Grenades":"Magazines";
   var calls=outputCode.Where(op=>op.operand is MethodInfo m&&m.DeclaringType?.Name=="BackpackInventoryAccess").Select(op=>(MethodInfo)op.operand!).ToArray();
   if(calls.Length!=1||calls[0].Name!=expected||calls[0].GetParameters().Length!=3)throw new Exception("Wrong composed adapter");
   uiaComposition.Add(new{target=target.DeclaringType!.FullName+"."+target.Name,status="PASS",adapter=expected,nativeInstructions=input.Length});
  }
  catch(Exception e){uiaComposition.Add(new{target=target.DeclaringType!.FullName+"."+target.Name,status="FAIL",error=e.GetBaseException().Message});}
 }
}
var compositionFailures=uiaComposition.Count(t=>JsonSerializer.SerializeToElement(t).GetProperty("status").GetString()=="FAIL");
var transpilerFailures=transpilers.Count(t=>JsonSerializer.SerializeToElement(t).GetProperty("status").GetString()=="FAIL");
var report=new{scope="Source selector/transpiler bodies evaluated against metadata-only native types and decoded IL. Optional UIA call substitution is modeled from the inspected binary using actual collector metadata. No UIA transpiler, EFT/Unity gameplay or native client Harmony installation executes here.",selectors=pass+fail+skipped,targets=total,passed=pass,skipped,failed=fail+transpilerFailures+compositionFailures,inventory,results,transpilers,uiaComposition};
File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Selector audit: {pass} PASS, {fail} FAIL, {total} native targets");
Console.WriteLine($"Transpiler audit: {transpilers.Count-transpilerFailures} PASS, {transpilerFailures} FAIL");
return fail+transpilerFailures+compositionFailures==0?0:1;

sealed class SelectorRewriter:CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitTypeOfExpression(TypeOfExpressionSyntax node)=>SyntaxFactory.ParseExpression("Meta.T("+JsonSerializer.Serialize(node.Type.ToString())+")").WithTriviaFrom(node);
    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if(node.Expression.ToString()=="AppDomain.CurrentDomain.GetAssemblies")
            return SyntaxFactory.ParseExpression("Meta.Assemblies()").WithTriviaFrom(node);
        if(node.Expression is IdentifierNameSyntax n&&n.Identifier.ValueText=="nameof")
        {
            var arg=node.ArgumentList.Arguments.Single().Expression.ToString();
            return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,SyntaxFactory.Literal(arg.Split('.').Last())).WithTriviaFrom(node);
        }
        if(node.Expression is MemberAccessExpressionSyntax access&&access.Name.Identifier.ValueText=="IsAssignableFrom")
            return SyntaxFactory.ParseExpression("Meta.Assignable("+Visit(access.Expression)+","+Visit(node.ArgumentList.Arguments.Single().Expression)+")").WithTriviaFrom(node);
        return base.VisitInvocationExpression(node);
    }
    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if(node.Name.Identifier.ValueText=="StateMachineType"&&node.Expression is InvocationExpressionSyntax call&&call.Expression is MemberAccessExpressionSyntax access&&access.Name is GenericNameSyntax name&&name.Identifier.ValueText=="GetCustomAttribute")
            return SyntaxFactory.ParseExpression("Meta.AsyncType("+Visit(access.Expression)+")").WithTriviaFrom(node);
        return base.VisitMemberAccessExpression(node);
    }
    public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
    {
        if(node.WhenNotNull is MemberBindingExpressionSyntax member&&member.Name.Identifier.ValueText=="StateMachineType"&&node.Expression is InvocationExpressionSyntax call&&call.Expression is MemberAccessExpressionSyntax access&&access.Name is GenericNameSyntax name&&name.Identifier.ValueText=="GetCustomAttribute")
            return SyntaxFactory.ParseExpression("Meta.AsyncType("+Visit(access.Expression)+")").WithTriviaFrom(node);
        return base.VisitConditionalAccessExpression(node);
    }
}
