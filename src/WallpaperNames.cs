using System;
using System.IO;
using System.Linq;

internal static class WallpaperNames {
 internal const int MaximumLength = 80;
 internal static string Validate(string input) {
  if(input == null || input.Any(Char.IsControl)) throw new InvalidDataException("名称不能包含换行或控制字符。");
  string name=input.Trim();
  if(name.Length==0)throw new InvalidDataException("请输入壁纸名称。");
  if(name.Length>MaximumLength)throw new InvalidDataException("名称最多80个字符。");
  return name;
 }
 internal static string ActiveName(WallpaperLibrary library,string activeId,string original) {
  var item=library.Doc.Items.FirstOrDefault(row=>row.Id==activeId);
  return item==null?original:item.Name;
 }
 internal static void Rename(WallpaperLibrary library,WallpaperItem item,string input) {
  string name=Validate(input);
  if(library.Error!=null)throw new InvalidDataException(library.Error);
  if(item==null||!library.Doc.Items.Contains(item))throw new InvalidDataException("该壁纸已不在当前库中。");
  string oldName=item.Name,oldCustom=item.CustomName,oldOriginal=item.NameBeforeCustom;
  if(oldCustom==name&&oldName==name)return;
  item.NameBeforeCustom=oldOriginal??oldName;
  item.CustomName=name;item.Name=name;
  try{library.Save();}
  catch{item.Name=oldName;item.CustomName=oldCustom;item.NameBeforeCustom=oldOriginal;throw;}
 }
}
