using System;using System.IO;
internal sealed class AppSettings {
 public int Version{get;set;}public bool Muted{get;set;}public int Volume{get;set;}internal string LoadError;
 internal static string FilePath{get{return Path.Combine(Common.Root,"data","settings.json");}}
 internal static AppSettings Load(){try{if(File.Exists(FilePath)){var x=Common.Json.Deserialize<AppSettings>(Common.ReadShared(FilePath));if(x==null||x.Version!=1||x.Volume<0||x.Volume>100)throw new InvalidDataException("设置内容无效");return x;}}catch(Exception ex){if(DataLocation.Ready)throw new InvalidDataException("正式声音设置读取失败，未用默认值覆盖",ex);return new AppSettings{Version=1,Muted=true,Volume=30,LoadError="设置未读取，使用默认静音："+ex.Message};}if(DataLocation.Ready)throw new InvalidDataException("正式声音设置缺失，未新建默认设置");return new AppSettings{Version=1,Muted=true,Volume=30};}
 internal void Save(){Common.ReplaceJson(FilePath,this);}
}
