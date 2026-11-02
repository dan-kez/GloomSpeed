using AssetsTools.NET; using AssetsTools.NET.Extra;
// usage: assetdump <GH_Data dir> <class[,class...]> : prints serialized script fields of matching MonoBehaviours. Read-only.
var data=args[0]; var wanted=new HashSet<string>(args[1].Split(','));
var managed=Path.Combine(data,"Managed");
var files=Directory.GetFiles(data).Where(f=>{var n=Path.GetFileName(f);return n=="globalgamemanagers.assets"||n=="resources.assets"||(n.StartsWith("sharedassets")&&n.EndsWith(".assets"))||(n.StartsWith("level")&&!n.Contains('.'));}).OrderBy(f=>f).ToList();
foreach(var f in files){
 var am=new AssetsManager(); am.UseTemplateFieldCache=true; am.MonoTempGenerator=new MonoCecilTempGenerator(managed);
 AssetsFileInstance inst; try{ using var fsr=new FileStream(f,FileMode.Open,FileAccess.Read,FileShare.ReadWrite); inst=am.LoadAssetsFile(new FileStream(f,FileMode.Open,FileAccess.Read,FileShare.ReadWrite),f,false);}catch(Exception e){Console.WriteLine("ERR "+f+" "+e.Message);continue;}
 var ver=inst.file.Metadata.UnityVersion; var hasTT=inst.file.Metadata.TypeTreeEnabled;
 if(f.EndsWith("level0")) Console.WriteLine($"# ver={ver} typetree={hasTT}");
 foreach(var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour)){
  try{
   var r=inst.file.Reader; r.Position=info.GetAbsoluteByteOffset(inst.file);
   // manual base header
   var start=r.Position; r.ReadInt32(); r.ReadInt64(); r.ReadByte(); r.Align(); var fid=r.ReadInt32(); var pid=r.ReadInt64();
   var si=fid==0?inst:inst.GetDependency(am,fid-1); if(si==null) continue; var mi=si.file.GetAssetInfo(pid); if(mi==null) continue;
   var rr=si.file.Reader; var save=rr.Position; rr.Position=mi.GetAbsoluteByteOffset(si.file);
   string S(){var n=rr.ReadInt32(); var b=rr.ReadBytes(n); rr.Align(); return System.Text.Encoding.UTF8.GetString(b);} 
   S(); rr.ReadInt32(); rr.ReadBytes(16); var cls=S(); var ns=S(); var asm=S(); if(!si.Equals(inst)) rr.Position=save;
   if(!wanted.Contains(cls)) continue;
   var bf=BuildBase(); var gen=new MonoCecilTempGenerator(managed);
   var tf=gen.GetTemplateField(bf,asm,ns,cls,new UnityVersion(ver));
   r.Position=start; var field=tf.MakeValue(r);
   Console.WriteLine($"=== {Path.GetFileName(f)} pid={info.PathId} {ns}.{cls} name='{field["m_Name"].AsString}'"); Dump(field,0);
  }catch(Exception e){Console.WriteLine("ERR "+Path.GetFileName(f)+" "+info.PathId+" "+e.GetType().Name+" "+e.Message);}
 }
 am.UnloadAll();
}
static AssetTypeTemplateField BuildBase(){
 AssetTypeTemplateField F(string t,string n,AssetValueType v,bool al=false,params AssetTypeTemplateField[] k)=>new AssetTypeTemplateField{Name=n,Type=t,ValueType=v,IsArray=false,IsAligned=al,HasValue=v!=AssetValueType.None,Children=k.ToList()};
 AssetTypeTemplateField P(string n)=>F("PPtr<Object>",n,AssetValueType.None,false,F("int","m_FileID",AssetValueType.Int32),F("SInt64","m_PathID",AssetValueType.Int64));
 var str=new AssetTypeTemplateField{Name="m_Name",Type="string",ValueType=AssetValueType.String,HasValue=true,IsAligned=true,Children=new()};
 var arr=new AssetTypeTemplateField{Name="Array",Type="Array",IsArray=true,IsAligned=true,ValueType=AssetValueType.Array,HasValue=true,Children=new(){F("int","size",AssetValueType.Int32),F("char","data",AssetValueType.UInt8)}};
 str.Children.Add(arr);
 return F("MonoBehaviour","Base",AssetValueType.None,false,P("m_GameObject"),F("UInt8","m_Enabled",AssetValueType.UInt8,true),P("m_Script"),str);
}
static void Dump(AssetTypeValueField f,int d){
 var pad=new string(' ',d*2);
 foreach(var c in f.Children){ var t=c.TemplateField;
  if(t.IsArray||t.Type=="vector"){ Console.WriteLine($"{pad}{t.Name}: [{c.Children.Count}]"); if(c.Children.Count<8)Dump(c,d+1); continue; }
  if(c.Children.Count>0 && !t.HasValue){ if(t.Type.StartsWith("PPtr")){Console.WriteLine($"{pad}{t.Name}: ptr {c["m_FileID"].AsInt}:{c["m_PathID"].AsLong}");continue;} if(t.Name=="data"||t.Type=="AnimationCurve"){Console.WriteLine($"{pad}{t.Name}:<{t.Type}>");continue;} Console.WriteLine($"{pad}{t.Name}:"); Dump(c,d+1); continue; }
  Console.WriteLine($"{pad}{t.Name} = {(c.Value==null?"<?>":c.AsString)}");
 }
}
