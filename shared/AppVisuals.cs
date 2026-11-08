using System;using System.Drawing;using System.Reflection;
internal static class AppVisuals {
 internal static Icon LoadIcon(){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("LocalWallpaper.ico")){if(s==null)return (Icon)SystemIcons.Application.Clone();using(var icon=new Icon(s))return (Icon)icon.Clone();}}
}
