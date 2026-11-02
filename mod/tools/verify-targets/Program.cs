using Mono.Cecil; using Mono.Cecil.Cil;
// usage: verify-targets <GloomSpeed.dll> <GH_Data/Managed>
// Read-only. Checks every [HarmonyPatch] in the plugin against the game's assemblies: target exists, is unique,
// patch parameters match by name and type, and the target is big enough that Mono will not inline it into callers.
var pluginPath=args[0]; var managed=args[1];
var res=new DefaultAssemblyResolver(); res.AddSearchDirectory(managed);
var rp=new ReaderParameters{AssemblyResolver=res};
var plugin=AssemblyDefinition.ReadAssembly(pluginPath,rp);
int bad=0;
const int MonoInlineLimit=20; // mono/mini INLINE_LENGTH_LIMIT (IL bytes)
foreach(var t in plugin.MainModule.Types.OrderBy(t=>t.Name)){
  var at=t.CustomAttributes.FirstOrDefault(a=>a.AttributeType.Name=="HarmonyPatch"); if(at==null) continue;
  var tr=(TypeReference)at.ConstructorArguments[0].Value; var name=(string)at.ConstructorArguments[1].Value;
  var td=tr.Resolve();
  var ms=td.Methods.Where(m=>m.Name==name).ToList();
  if(ms.Count!=1){Console.WriteLine($"FAIL {t.Name}: {td.FullName}.{name} has {ms.Count} matches"); bad++; continue;}
  var m=ms[0]; var size=m.Body.CodeSize;
  var sig=$"{m.ReturnType.Name} {td.FullName}.{name}({string.Join(", ",m.Parameters.Select(p=>p.ParameterType.Name+" "+p.Name))})";
  var note=size<=MonoInlineLimit?$" WARN-INLINE(IL {size})":"";
  if(size<=MonoInlineLimit) bad++;
  Console.WriteLine($"OK   {t.Name} -> [{td.Module.Assembly.Name.Name}] {sig} ; {(m.IsPublic?"public":"non-public")} IL={size}{note}");
  foreach(var hook in t.Methods.Where(x=>x.Name is "Prefix" or "Postfix")){
    foreach(var p in hook.Parameters){
      if(p.Name=="__state") continue;
      if(p.Name=="__instance"){ var et=p.ParameterType; bool ok=IsAssignable(td,et); Console.WriteLine($"       {hook.Name} __instance:{et.Name} {(ok?"ok":"MISMATCH")}"); if(!ok)bad++; continue;}
      if(p.Name=="__result"){ var rt=p.ParameterType.IsByReference?((ByReferenceType)p.ParameterType).ElementType:p.ParameterType; bool ok=rt.FullName==m.ReturnType.FullName; Console.WriteLine($"       {hook.Name} __result:{rt.Name} {(ok?"ok":"MISMATCH")}"); if(!ok)bad++; continue; }
      var tp=m.Parameters.FirstOrDefault(x=>x.Name==p.Name);
      var pt=p.ParameterType.IsByReference?((ByReferenceType)p.ParameterType).ElementType:p.ParameterType;
      bool match=tp!=null&&(tp.ParameterType.FullName==pt.FullName);
      Console.WriteLine($"       {hook.Name} {p.ParameterType.Name} {p.Name}: {(match?"ok":"MISMATCH")}"); if(!match)bad++;
    }
    if(hook.Name=="Prefix"&&hook.ReturnType.Name!="Void"&&hook.ReturnType.Name!="Boolean"){Console.WriteLine("       Prefix return type invalid");bad++;}
  }
}
// Members reached by string (FieldRefAccess) or used as public API.
var fields=new (string asm,string type,string field,string ftype)[]{
 ("GH.Runtime","GlobalData","m_SpeedUpToggle","System.Boolean"),
 ("GH.Runtime","GUIAnimator","enableDebug","System.Boolean"),
 ("GH.Runtime","LeanTweenGUIAnimator","animationsInProcess","System.Collections.Generic.List`1<LTDescr>"),
 ("GH.Runtime","PhaseBannerHandler","showAnimation","GUIAnimator"),
 ("GH.Runtime","WorldspaceUI.AttackModBar","testTimeScaler","System.Single"),
 ("GH.Runtime","CameraController","m_FocusSpeed","System.Single"),
 ("GH.Runtime.FirstPass","LTDescr","time","System.Single"),
 ("GH.Runtime.FirstPass","LTDescr","delay","System.Single"),
 ("GH.Runtime","Choreographer","m_WaitState","Choreographer/CWaitState"),
};
foreach(var f in fields){
  var a=AssemblyDefinition.ReadAssembly(Path.Combine(managed,f.asm+".dll"),rp); var ty=a.MainModule.GetType(f.type);
  var fd=ty?.Fields.FirstOrDefault(x=>x.Name==f.field);
  bool ok=fd!=null&&fd.FieldType.FullName==f.ftype; Console.WriteLine($"{(ok?"OK  ":"FAIL")} field {f.type}.{f.field} : {fd?.FieldType.FullName}"); if(!ok)bad++;
}
// IL constants claimed in the report.
var gh=AssemblyDefinition.ReadAssembly(Path.Combine(managed,"GH.Runtime.dll"),rp);
var awake=gh.MainModule.GetType("SceneController").Methods.First(m=>m.Name=="Awake");
Console.WriteLine("SceneController.Awake sets GameSpeedIncreaseAmount to: "+string.Join(",",awake.Body.Instructions.Where(i=>i.Next?.Operand is MethodReference r&&r.Name=="set_GameSpeedIncreaseAmount"&&i.OpCode==OpCodes.Ldc_R4).Select(i=>i.Operand)));
Console.WriteLine(bad==0?"RESULT: all targets verified":$"RESULT: {bad} problem(s)");
return bad==0?0:1;
static bool IsAssignable(TypeDefinition target,TypeReference inst){ for(TypeDefinition c=target;c!=null;c=c.BaseType?.Resolve()) if(c.FullName==inst.FullName) return true; return false; }
