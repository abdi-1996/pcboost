using System;using System.IO;using System.Diagnostics;using System.Reflection;using System.Windows.Forms;using System.Drawing;using Microsoft.Win32;
class Setup:Form {
 string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","PCCareStudio");Button install;Label label;CheckBox desktop;bool remove,finished;
 [STAThread] static void Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new Setup(Array.IndexOf(args,"--uninstall")>=0));}
 Setup(bool uninstall){remove=uninstall;Text=remove?"Удаление PC Care Studio":"Установка PC Care Studio 1.0";Width=590;Height=400;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(15,24,39);ForeColor=Color.FromArgb(236,242,250);Font=new Font("Segoe UI",10);
  var title=new Label{Text="PC CARE STUDIO",Font=new Font("Segoe UI",24,FontStyle.Bold),Left=30,Top=30,Width=510,Height=50};Controls.Add(title);
  label=new Label{Text=remove?"Приложение и ярлыки будут удалены.\nНастройки и журналы останутся в профиле пользователя.":"Диагностика • очистка • VRAM • ИИ\n\nУстановка для текущего пользователя, без администратора.\n\n"+dir,Left=32,Top=95,Width=510,Height=145};Controls.Add(label);
  desktop=new CheckBox{Text="Создать ярлык на Рабочем столе",Checked=true,Left=32,Top=247,Width=400,Visible=!remove};Controls.Add(desktop);
  install=new Button{Text=remove?"Удалить":"Установить",Left=325,Top=292,Width=200,Height=44,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(35,108,98)};install.Click+=delegate{Go();};Controls.Add(install);
 }
 string StartLink(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"PC Care Studio.lnk");}string DesktopLink(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"PC Care Studio.lnk");}
 void Resource(string name,string target){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){if(s==null)throw new IOException("Отсутствует файл "+name);using(var output=File.Create(target))s.CopyTo(output);}}
 void Link(string path){Type t=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(t),link=null;try{link=t.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});link.GetType().InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{Path.Combine(dir,"PC-Care-Studio.exe")});link.GetType().InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{dir});link.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);}finally{if(link!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}}
 void Go(){if(finished){if(!remove)Process.Start(Path.Combine(dir,"PC-Care-Studio.exe"));Close();return;}install.Enabled=false;try{foreach(var p in Process.GetProcessesByName("PC-Care-Studio")){p.Dispose();throw new Exception("Сначала завершите PC Care Studio через меню в трее.");}
  string registry=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\PCCareStudio";
  if(remove){if(MessageBox.Show("Удалить PC Care Studio?",Text,MessageBoxButtons.YesNo)!=DialogResult.Yes){install.Enabled=true;return;}
   foreach(var f in new[]{"PC-Care-Studio.exe","README-RU.txt"}){string path=Path.Combine(dir,f);if(File.Exists(path))File.Delete(path);}foreach(var l in new[]{StartLink(),DesktopLink()})if(File.Exists(l))File.Delete(l);
   using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true))if(k!=null)k.DeleteValue("PCCareStudio",false);Registry.CurrentUser.DeleteSubKeyTree(registry,false);
   label.Text="Программа удалена.\nНастройки и журналы сохранены.\nПосле закрытия можно удалить оставшийся Uninstall.exe.";install.Text="Закрыть";finished=true;
  }else{Directory.CreateDirectory(dir);Resource("PC-Care-Studio.exe",Path.Combine(dir,"PC-Care-Studio.exe"));Resource("README-RU.txt",Path.Combine(dir,"README-RU.txt"));string uninstaller=Path.Combine(dir,"Uninstall.exe");if(!String.Equals(Assembly.GetExecutingAssembly().Location,uninstaller,StringComparison.OrdinalIgnoreCase))File.Copy(Assembly.GetExecutingAssembly().Location,uninstaller,true);Link(StartLink());if(desktop.Checked)Link(DesktopLink());
   using(var k=Registry.CurrentUser.CreateSubKey(registry)){k.SetValue("DisplayName","PC Care Studio");k.SetValue("DisplayVersion","1.0.0");k.SetValue("Publisher","PC Care Studio");k.SetValue("InstallLocation",dir);k.SetValue("UninstallString","\""+uninstaller+"\" --uninstall");k.SetValue("DisplayIcon",Path.Combine(dir,"PC-Care-Studio.exe"));k.SetValue("NoModify",1);k.SetValue("NoRepair",1);}
   label.Text="Установка завершена.\nВсе файлы приложения находятся в:\n"+dir;install.Text="Открыть";finished=true;desktop.Visible=false;
  }
  install.Enabled=true;
 }catch(Exception ex){MessageBox.Show(ex.Message,Text);install.Enabled=true;}}

}
