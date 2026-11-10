using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class RenameWallpaperDialog : MaterialDialog {
 internal readonly FloatingNameInput Editor=new FloatingNameInput();
 internal readonly Label Validation=new Label();
 internal readonly SoftButton SaveButton=new SoftButton{Text="保存名称",Primary=true};
 internal readonly SoftButton CancelButtonControl=new SoftButton{Text="取消",DialogResult=DialogResult.Cancel};
 readonly Action<string> save;
 internal RenameWallpaperDialog(string current,Action<string> commit){
  save=commit;Text="重命名壁纸";StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(480,250);MinimumSize=new Size(440,270);MaximizeBox=false;MinimizeBox=false;
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=4};
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,66));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));Controls.Add(layout);
  layout.Controls.Add(new Label{Text="给这张壁纸起个容易找到的名字",AutoSize=true,ForeColor=Theme.Muted},0,0);
  Editor.Dock=DockStyle.Fill;Editor.Input.Text=current;Editor.Input.MaxLength=WallpaperNames.MaximumLength+1;layout.Controls.Add(Editor,0,1);
  Validation.Dock=DockStyle.Fill;Validation.ForeColor=Color.Firebrick;layout.Controls.Add(Validation,0,2);
  var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};foreach(var b in new[]{SaveButton,CancelButtonControl}){b.Size=new Size(120,38);buttons.Controls.Add(b);}layout.Controls.Add(buttons,0,3);
  SaveButton.Click+=(s,e)=>TrySave();CancelButtonControl.Click+=(s,e)=>CancelEdit();Editor.Input.TextChanged+=(s,e)=>ValidateInput();AcceptButton=SaveButton;CancelButton=CancelButtonControl;
  Shown+=(s,e)=>{Editor.Input.Focus();Editor.Input.SelectAll();};ValidateInput();
 }
 internal void CancelEdit(){DialogResult=DialogResult.Cancel;Close();}
 protected override bool ProcessDialogKey(Keys keyData){if(keyData==Keys.Escape){CancelEdit();return true;}return base.ProcessDialogKey(keyData);}
 void ValidateInput(){try{WallpaperNames.Validate(Editor.Input.Text);Validation.Text="最多80个字符；ID、文件和确认保持关联。";Validation.ForeColor=Theme.Muted;SaveButton.Enabled=true;}catch(Exception error){Validation.Text=error.Message;Validation.ForeColor=Color.Firebrick;SaveButton.Enabled=false;}}
 internal bool TrySave(){try{save(WallpaperNames.Validate(Editor.Input.Text));DialogResult=DialogResult.OK;Close();return true;}catch(Exception error){Validation.Text="未保存："+error.Message;Validation.ForeColor=Color.Firebrick;Editor.Input.Focus();return false;}}
}

internal sealed partial class ControlWindow {
#if TEST_BUILD
 internal bool TestSuppressRenameShow;
#endif
 RenameWallpaperDialog renameWindow;
 readonly SoftButton renameWallpaper=new SoftButton();
 void RenameWallpaper(WallpaperItem item){
  if(item==null||closing)return;
  if(renameWindow!=null&&!renameWindow.IsDisposed){renameWindow.Activate();return;}
  var dialog=CreateRenameEditor(item);
  renameWindow=dialog;dialog.FormClosed+=(s,e)=>{renameWindow=null;if(!closing&&!IsDisposed){if(selected!=null&&selected.Id==item.Id)renameWallpaper.Focus();else cards.Focus();}};
#if TEST_BUILD
  if(TestSuppressRenameShow)return;
#endif
  dialog.Show(this);
 }
 RenameWallpaperDialog CreateRenameEditor(WallpaperItem item){
  return new RenameWallpaperDialog(item.Name,name=>{
   WallpaperNames.Rename(library,item,name);RefreshNameLabels();RefreshSelection();RefreshSettingsState();
   homeNotice.Present("名称已保存");Write("wallpaper-renamed",new{id=item.Id,resourceId=item.ResourceId,name=item.Name,playbackRestarted=false,run=CurrentRun});
  });
 }
 void InstallCardMenu(WallpaperCard card){
  var menu=new MaterialMenu();menu.Items.Add("重命名",null,(s,e)=>RenameWallpaper(card.Item));menu.Prepare(uiScale);card.ContextMenuStrip=menu;
  card.Disposed+=(s,e)=>menu.Dispose();
 }
}
