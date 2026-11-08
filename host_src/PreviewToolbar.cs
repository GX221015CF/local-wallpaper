using System;using System.IO;using System.Drawing;using System.Diagnostics;using System.Windows.Forms;using System.Collections.Generic;using System.Web.Script.Serialization;
internal sealed partial class DesktopHost {
 readonly Label previewMessage=new Label();readonly Button confirmImage=new Button(),captureCover=new Button(),closePreview=new Button();
 Dictionary<string,object> previewRequest;long controlSequence;bool controlPending,previewReady;string pendingControl;object previewLayout;
 static readonly JavaScriptSerializer controlJson=new JavaScriptSerializer{MaxJsonLength=65536};
 static string TextField(Dictionary<string,object> d,string key){return d.ContainsKey(key)?Convert.ToString(d[key]):"";}
 void SetupPreviewControls(){
  previewRequest=controlJson.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(run,"request.json")));
  Text="本地壁纸 · 窗口预览";BackColor=Color.FromArgb(247,248,250);Font=new Font("Microsoft YaHei UI",9);
  var toolbar=new TableLayoutPanel{Dock=DockStyle.Top,Height=76,Padding=new Padding(14,10,14,10),ColumnCount=4,RowCount=1,BackColor=Color.FromArgb(247,248,250)};
  toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,106));toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,106));toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,104));
  previewMessage.Dock=DockStyle.Fill;previewMessage.ForeColor=Color.FromArgb(85,96,111);previewMessage.TextAlign=ContentAlignment.MiddleLeft;previewMessage.Text="正在准备预览…";previewMessage.AutoEllipsis=true;
  confirmImage.Text="确认画面";confirmImage.Name="ConfirmPreviewImage";captureCover.Text="截取封面";captureCover.Name="CapturePreviewCover";closePreview.Text="关闭预览";closePreview.Name="CloseThisPreview";
  foreach(var b in new[]{confirmImage,captureCover,closePreview}){b.Dock=DockStyle.Fill;b.Margin=new Padding(6,6,0,6);b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=Color.FromArgb(215,220,228);b.BackColor=Color.White;b.Enabled=false;}
  toolbar.Controls.Add(previewMessage,0,0);toolbar.Controls.Add(confirmImage,1,0);toolbar.Controls.Add(captureCover,2,0);toolbar.Controls.Add(closePreview,3,0);Controls.Add(toolbar);
  confirmImage.Click+=(s,e)=>SendPreviewAction("confirm");captureCover.Click+=(s,e)=>SendPreviewAction("capture-cover");closePreview.Click+=(s,e)=>SendPreviewAction("close");
  ArrangePreview();
 }
 void ArrangePreview(){
  Rectangle owner=Screen.PrimaryScreen.WorkingArea;try{var b=(System.Collections.IEnumerable)previewRequest["controllerBounds"];var values=new System.Collections.Generic.List<int>();foreach(var value in b)values.Add(Convert.ToInt32(value));if(values.Count==4&&values[2]>0&&values[3]>0)owner=new Rectangle(values[0],values[1],values[2],values[3]);}catch{}
  var area=Screen.FromRectangle(owner).WorkingArea;int right=area.Right-owner.Right-16,left=owner.Left-area.Left-16;
  int available=Math.Max(left,right);int width=Math.Min(940,Math.Max(480,area.Width-32));bool beside=available>=480;
  if(beside)width=Math.Min(940,available);int height=Math.Min(area.Height-32,(int)(width*.625)+114);height=Math.Max(260,height);
  int x=beside?(right>=left?owner.Right+12:owner.Left-width-12):area.Left+(area.Width-width)/2;
  int y=Math.Max(area.Top+8,Math.Min(owner.Top,area.Bottom-height-8));StartPosition=FormStartPosition.Manual;
  Bounds=new Rectangle(Math.Max(area.Left+8,Math.Min(x,area.Right-width-8)),y,width,height);MinimumSize=new Size(Math.Min(480,area.Width),Math.Min(320,area.Height));
  previewLayout=new{beside=beside,workingArea=new[]{area.X,area.Y,area.Width,area.Height},bounds=new[]{Left,Top,Width,Height},controlsInPreview=true,topmost=TopMost};
 }
 void SendPreviewAction(string action){
  if(mode!="preview"||controlPending||(!previewReady&&action!="close")||closed)return;
  string token=TextField(previewRequest,"runToken"),hash=TextField(previewRequest,"expectedSha256");
  if(token.Length!=32||hash.Length!=64)return;controlPending=true;pendingControl=action;confirmImage.Enabled=false;captureCover.Enabled=false;closePreview.Enabled=false;previewMessage.Text=action=="close"?"正在停止预览，请稍候…":action=="capture-cover"?"正在截取封面…":"正在核对本次实际加载的画面…";
  try{var request=new{version=1,token=token,run=run,resourceSha256=hash,sequence=++controlSequence,action=action,playerPid=Process.GetCurrentProcess().Id,playerCreation=Process.GetCurrentProcess().StartTime.ToUniversalTime().ToFileTimeUtc(),window=Handle.ToInt64(),render=surface.Handle.ToInt64()};
   AtomicControl(Path.Combine(run,"preview-control-request.json"),request);Write("preview-control-request",request);
  }catch(Exception ex){controlPending=false;previewMessage.Text="请求未送出："+ex.Message;closePreview.Enabled=true;}
 }
 void ReadPreviewStatus(){
  if(mode!="preview")return;string path=Path.Combine(run,"preview-control-status.json");if(!File.Exists(path))return;
  try{if(new FileInfo(path).Length>32768)return;var d=controlJson.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
   if(TextField(d,"token")!=TextField(previewRequest,"runToken")||TextField(d,"resourceSha256")!=TextField(previewRequest,"expectedSha256"))return;
   long ack=Convert.ToInt64(d["sequence"]);if(controlPending&&ack<controlSequence)return;
   if(controlPending&&pendingControl=="close"){previewMessage.Text=TextField(d,"message");return;}
   controlPending=false;previewReady=Convert.ToBoolean(d["ready"]);bool confirmed=Convert.ToBoolean(d["confirmed"]);
   confirmImage.Enabled=previewReady&&!confirmed;confirmImage.Text=confirmed?"已确认画面":"确认画面";captureCover.Enabled=previewReady&&d.ContainsKey("coverAllowed")&&Convert.ToBoolean(d["coverAllowed"]);captureCover.Text=d.ContainsKey("coverBusy")&&Convert.ToBoolean(d["coverBusy"])?"正在保存…":d.ContainsKey("coverExists")&&Convert.ToBoolean(d["coverExists"])?"替换封面":"截取封面";closePreview.Enabled=previewReady;previewMessage.Text=TextField(d,"message");
  }catch(IOException){}catch(Exception ex){previewMessage.Text="控制状态暂不可用："+ex.Message;}
 }
 static void AtomicControl(string path,object value){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,controlJson.Serialize(value));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
