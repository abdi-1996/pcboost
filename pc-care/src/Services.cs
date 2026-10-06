using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Win32;
namespace PCCare {
 public class Settings {
  public string Comfy="http://127.0.0.1:8188",Model="",Folder="";public bool AutoClean=false,Tray=true,ProtectComfy=true;public int Minutes=30;
  public static Settings Load(){try{return Core.Json.Deserialize<Settings>(File.ReadAllText(Path.Combine(Core.Data,"settings.json")));}catch{return new Settings();}}
  public void Save(){Directory.CreateDirectory(Core.Data);string path=Path.Combine(Core.Data,"settings.json");Core.Atomic(path,Encoding.UTF8.GetBytes(Core.Json.Serialize(this)));}
 }
 public static partial class Core {
  public static void Atomic(string path,byte[] bytes){Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllBytes(tmp,bytes);if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
  public static void SecretSave(string name,string value){Atomic(Path.Combine(Data,name),ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser));}
  public static string SecretLoad(string name){try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(Data,name)),null,DataProtectionScope.CurrentUser));}catch{return null;}}
  public static async Task<string> PowerShell(string script,int timeout){string exe=Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe");return await Run(exe,"-NoLogo -NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $ErrorActionPreference='Stop'; "+script)),timeout);}
  public static async Task<string> Health(){return await PowerShell("Get-PhysicalDisk | Select-Object FriendlyName,MediaType,HealthStatus,OperationalStatus,Size | Format-List | Out-String -Width 160",30000);}
  public static async Task<string> Updates(){return await PowerShell("$s=New-Object -ComObject Microsoft.Update.Session; $r=$s.CreateUpdateSearcher().Search(\"IsInstalled=0 and IsHidden=0\"); 'Available updates: '+$r.Updates.Count; foreach($u in $r.Updates){$u.Title}",120000);}
  public static string Startup(){var b=new StringBuilder();try{using(var s=new ManagementObjectSearcher("SELECT Name,Command,Location FROM Win32_StartupCommand"))using(var rows=s.Get())foreach(ManagementObject o in rows)using(o)b.AppendLine(Convert.ToString(o["Name"])+"\n"+Convert.ToString(o["Command"])+"\n"+Convert.ToString(o["Location"])+"\n");}catch(Exception e){b.AppendLine(e.Message);}return b.Length==0?"Записи автозагрузки не найдены.":b.ToString();}
  public static List<FileRow> LargeFiles(string root){if(!Directory.Exists(root)||!NoLinks(root))throw new Exception("Папка недоступна или является ссылкой.");var files=new List<FileRow>();var stack=new Stack<string>();stack.Push(root);int visited=0;
   while(stack.Count>0&&visited++<20000){string d=stack.Pop();if(!NoLinks(d))continue;try{foreach(string p in Directory.GetFiles(d)){var f=new FileInfo(p);if(f.Length>=100*1024*1024&&NoLinks(p))files.Add(new FileRow{Source=p,Bytes=f.Length,Modified=f.LastWriteTimeUtc});}foreach(string sub in Directory.GetDirectories(d))stack.Push(sub);}catch(UnauthorizedAccessException){}catch(IOException){}}
   return files.OrderByDescending(x=>x.Bytes).Take(200).ToList();
  }
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd,string root,uint flags);
  public static void EmptyBin(IntPtr hwnd){int result=SHEmptyRecycleBin(hwnd,null,0);if(result!=0&&result!=unchecked((int)0x800704C7))throw new Exception("Windows не очистила корзину. Код: "+result);}
  public static void OpenLocation(string file){if(!File.Exists(file))throw new Exception("Файл уже отсутствует.");Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+file+"\""){UseShellExecute=true});}
  public static void AutoStart(bool enable){using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(enable)k.SetValue("PCCareStudio","\""+Process.GetCurrentProcess().MainModule.FileName+"\" --tray");else k.DeleteValue("PCCareStudio",false);}}
  public static bool AutoStarts(){using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return k!=null&&k.GetValue("PCCareStudio")!=null;}
 }
}
