using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
// Interactive integration harness: real mouse input, production handle, isolated data/preferences.
// Compile beside DesktopTidy.exe and close this background window with Alt+F4.
class NotesDockMousePreview {
 [STAThread] static void Main() {
  Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
  var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  var asm=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DesktopTidy.exe"));
  string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mouse-preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
  string key=@"Software\DesktopTidy-Tests\MousePreview-"+Guid.NewGuid().ToString("N");
  var type=asm.GetType("DesktopTidy.Core.WorkspaceController");
  var owner=Activator.CreateInstance(type,flags,null,new object[]{SystemFonts.MessageBoxFont,new Action<string>(Console.WriteLine),data},null);
  var store=Activator.CreateInstance(asm.GetType("DesktopTidy.Core.NotesStore"),flags,null,new object[]{Path.Combine(data,"notes.json")},null);
  type.GetProperty("Notes",flags).SetValue(owner,store,null);
  type.GetProperty("WebProfileDirectory",flags).SetValue(owner,Path.Combine(data,"WebView2"),null);
  type.GetField("EdgeNotesPaused",flags).SetValue(owner,true);
  var ctrlType=asm.GetType("DesktopTidy.UI.NotesEdgeController");
  var ctrl=Activator.CreateInstance(ctrlType,flags,null,new object[]{owner,new Action<string>(Console.WriteLine),key},null);
  type.GetField("EdgeNotesPaused",flags).SetValue(owner,true);
  var form=new Form{Text="Isolated mouse docking acceptance",StartPosition=FormStartPosition.Manual,FormBorderStyle=FormBorderStyle.None,Bounds=Screen.PrimaryScreen.WorkingArea};
  var label=new Label{Dock=DockStyle.Fill,Font=SystemFonts.MessageBoxFont};form.Controls.Add(label);
  string events=""; Control hooked=null; Action<string> trace=delegate(string v){events=DateTime.Now.ToString("HH:mm:ss.fff")+" "+v+"\n"+events; if(events.Length>3000)events=events.Substring(0,3000); File.AppendAllText(Path.Combine(data,"mouse.log"),DateTime.Now.ToString("HH:mm:ss.fff")+" "+v+"\n");};
  var timer=new Timer{Interval=100};timer.Tick+=delegate{
   var handle=(Form)ctrlType.GetField("dockHandle",flags).GetValue(ctrl);
   if(handle!=null && hooked==null){hooked=handle.Controls[0]; var b=hooked; b.MouseDown+=delegate{trace("down capture="+b.Capture);}; b.MouseUp+=delegate{trace("up capture="+b.Capture);}; b.MouseCaptureChanged+=delegate{trace("capture="+b.Capture+" buttons="+Control.MouseButtons);}; handle.LocationChanged+=delegate{trace("location="+handle.Location);};}
   label.Text="ISOLATED DATA / close this window to finish\nWork area: "+Screen.PrimaryScreen.WorkingArea+"\nHandle: "+(handle==null?"waiting":handle.Bounds.ToString())+"\nSide: "+type.GetField("EdgeNotesDockSide",flags).GetValue(owner)+"\nDragging: "+(handle==null?"waiting":handle.GetType().GetProperty("Dragging",flags).GetValue(handle,null).ToString())+"\n"+events;
  };timer.Start();
  Application.Run(form);timer.Dispose();((IDisposable)ctrl).Dispose();((IDisposable)owner).Dispose();Registry.CurrentUser.DeleteSubKeyTree(key,false);
 }
}
