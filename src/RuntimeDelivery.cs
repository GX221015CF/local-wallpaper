using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

// Verifies files shipped with this program. External engine files are checked
// separately against RuntimeProvisioning's user-selected, pinned location.
internal static class RuntimeDelivery {
 internal const string ContractHash="3e773a7cde1ef773ad0730901251fe390462b22f4bf35a927acd2e8675f08b96";
 static Dictionary<string,string> Rows(string path){var root=Common.Read(path);if(!root.ContainsKey("files"))throw new InvalidDataException("运行清单缺少files");var rows=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(object item in (System.Collections.IEnumerable)root["files"]){var row=Common.Obj(item);string name=Common.S(row,"name"),hash=Common.S(row,"sha256");if(String.IsNullOrWhiteSpace(name)||name=="."||name==".."||Path.GetFileName(name)!=name||name.Contains(":")||!PackageAnalysis.HashText(hash)||rows.ContainsKey(name))throw new InvalidDataException("运行清单路径、哈希或重名无效");rows.Add(name,hash);}return rows;}
 internal static int Verify(string root){
  string contract=Path.Combine(root,"rules","runtime-components.json"),manifest=Path.Combine(root,"build","runtime-manifest.json");LocalImport.NoLinks(contract);
  if(Common.Hash(contract)!=ContractHash)throw new InvalidDataException("已审查运行依赖契约身份不符");var expected=Rows(contract);
  if(expected.Count!=24||expected["Desktop317495Host-refined.exe"]!=Common.HostHash)throw new InvalidDataException("运行依赖契约或自有宿主身份不符");
  LocalImport.NoLinks(manifest);var actual=Rows(manifest);if(actual.Count!=1||!actual.ContainsKey("Desktop317495Host-refined.exe")||actual["Desktop317495Host-refined.exe"]!=Common.HostHash)throw new InvalidDataException("自有宿主发布清单不符");
  string host=Path.Combine(root,"build","runtime","Desktop317495Host-refined.exe");LocalImport.NoLinks(host);if(Common.Hash(host)!=Common.HostHash)throw new InvalidDataException("自有播放器宿主缺失或身份不符");
  string rules=Path.Combine(root,"rules","v3-structure-families.json");LocalImport.NoLinks(rules);if(new FileInfo(rules).Length==0)throw new InvalidDataException("缺少结构规则");Common.Read(rules);return actual.Count;
 }
}
