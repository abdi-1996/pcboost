using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PCCare {
 public class FileRow {
  public bool Selected {get;set;} public string Source {get;set;} public string Destination {get;set;}
  public long Bytes {get;set;} public DateTime Modified {get;set;} public string Hash {get;set;}
  public string Size {get {return Core.Size(Bytes);}}
 }
 public class ProcessRow { public DateTime Started {get;set;} public int PID {get;set;} public string Name {get;set;} public string RAM {get;set;} public long Bytes {get;set;} }
 public class Snapshot { public double CPU; public uint Load; public ulong Total, Free; public string GPU="Нет данных NVIDIA"; public string Disks=""; }
 public static partial class Core {
  public static string Data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PC-Care-Studio");
  public static readonly string Temp=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Temp"));
  public static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=16*1024*1024};
  [StructLayout(LayoutKind.Sequential)] class Memory {public uint Length=64, Load; public ulong Total, Free, Page, PageFree, Virtual, VirtualFree, Extended;}
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GlobalMemoryStatusEx([In,Out] Memory m);
  [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
  [StructLayout(LayoutKind.Sequential)] struct FileData {public uint Attributes;public System.Runtime.InteropServices.ComTypes.FILETIME Created,Accessed,Written;public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle,out FileData data);
  [StructLayout(LayoutKind.Sequential)] struct Disposition {[MarshalAs(UnmanagedType.U1)] public bool Delete;}
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle,int kind,ref Disposition data,uint size);
  static void DeleteExact(FileRow r) {
   using(var h=CreateFile(r.Source,0x80010000,0,IntPtr.Zero,3,0x00200000,IntPtr.Zero)) {
    if(h.IsInvalid)throw new IOException("File locked or unavailable");FileData d;
    if(!GetFileInformationByHandle(h,out d))throw new IOException("Cannot inspect file handle");
    long size=((long)d.SizeHigh<<32)|d.SizeLow;
    long modified=((long)d.Written.dwHighDateTime<<32)|(uint)d.Written.dwLowDateTime;
    if((d.Attributes&(uint)(FileAttributes.ReparsePoint|FileAttributes.Directory|FileAttributes.System|FileAttributes.ReadOnly))!=0||size!=r.Bytes||DateTime.FromFileTimeUtc(modified)!=r.Modified)throw new IOException("File changed");
    var disposition=new Disposition{Delete=true};
    if(!SetFileInformationByHandle(h,4,ref disposition,1))throw new IOException("Cannot delete file");
   }
  }
  static long prevIdle,prevTotal;
  public static string Size(double b) {return b>=1073741824 ? (b/1073741824).ToString("0.0")+" ГБ" : (b/1048576).ToString("0.0")+" МБ";}
  public static void Log(string s) {Directory.CreateDirectory(Data); File.AppendAllText(Path.Combine(Data,"operations.log"),DateTime.Now.ToString("s")+" "+s+Environment.NewLine,Encoding.UTF8);}
  public static bool Under(string p,string root) {return Path.GetFullPath(p).StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);}
  public static bool NoLinks(string p) {
   try {string c=Path.GetFullPath(p); while(!String.IsNullOrEmpty(c)) {if((File.GetAttributes(c)&FileAttributes.ReparsePoint)!=0)return false;c=Path.GetDirectoryName(c);} return true;} catch {return false;}
  }
  static readonly HashSet<string> SafeExtensions=new HashSet<string>(new[]{".tmp",".temp"},StringComparer.OrdinalIgnoreCase);
  public static bool Eligible(string path,DateTime cutoff) {
   if(!Under(path,Temp)||!NoLinks(path)||!SafeExtensions.Contains(Path.GetExtension(path)))return false;
   try {var f=new FileInfo(path);return f.Exists && f.LastWriteTimeUtc<cutoff && f.CreationTimeUtc<cutoff && (f.Attributes&(FileAttributes.ReadOnly|FileAttributes.System))==0;} catch{return false;}
  }
  public static List<FileRow> Scan() {
   var result=new List<FileRow>();var stack=new Stack<string>();stack.Push(Temp);var cutoff=DateTime.UtcNow.AddDays(-7);
   while(stack.Count>0 && result.Count<20000) {
    var dir=stack.Pop();if(!NoLinks(dir))continue;
    try {foreach(var p in Directory.GetFiles(dir))if(Eligible(p,cutoff)){var f=new FileInfo(p);result.Add(new FileRow{Selected=true,Source=p,Bytes=f.Length,Modified=f.LastWriteTimeUtc});}
     foreach(var d in Directory.GetDirectories(dir))stack.Push(d);
    } catch(UnauthorizedAccessException){} catch(IOException){}
   }return result;
  }
  public static string Clean(List<FileRow> rows) {
   int count=0,skip=0;long bytes=0;foreach(var r in rows.Where(x=>x.Selected)) {
    try {var f=new FileInfo(r.Source);if(!Eligible(r.Source,DateTime.UtcNow.AddDays(-7))||f.Length!=r.Bytes||f.LastWriteTimeUtc!=r.Modified){skip++;continue;}
     DeleteExact(r);count++;bytes+=r.Bytes;try{Log("Удалён временный файл: "+r.Source);}catch{}
    }catch {skip++;}
   } return "Удалено: "+count+", освобождено "+Size(bytes)+". Пропущено: "+skip;
  }
  static string Category(string p) {
   string e=Path.GetExtension(p).ToLowerInvariant();
   if(new[]{".jpg",".jpeg",".png",".webp",".gif",".heic"}.Contains(e))return "Фото";
   if(new[]{".mp4",".mov",".mkv",".avi"}.Contains(e))return "Видео";
   if(new[]{".pdf",".docx",".xlsx",".pptx",".txt"}.Contains(e))return "Документы";
   if(new[]{".zip",".7z",".rar"}.Contains(e))return "Архивы";
   if(new[]{".mp3",".wav",".flac"}.Contains(e))return "Аудио";
   return null;
  }
  public static bool AllowedFolder(string dir) {
   // Only ordinary folders under these two known user locations may be organized.
   string user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
   var roots=new[]{Path.Combine(user,"Downloads"),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)};
   return NoLinks(dir)&&roots.Any(r=>String.Equals(Path.GetFullPath(dir).TrimEnd('\\'),Path.GetFullPath(r).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)||Under(dir,r));
  }
  public static List<FileRow> Plan(string dir) {
   if(!AllowedFolder(dir))throw new Exception("Выберите Загрузки, Рабочий стол или их подпапку. Папки программ и моделей не поддерживаются.");
   if(File.Exists(Path.Combine(dir,"main.py"))||Directory.Exists(Path.Combine(dir,"models"))||Directory.Exists(Path.Combine(dir,".git")))throw new Exception("Похоже на папку проекта. Сортировка отменена.");
   var rows=new List<FileRow>();foreach(var p in Directory.GetFiles(dir)) {
    var c=Category(p);if(c==null||!NoLinks(p))continue;var f=new FileInfo(p);
    if(f.LastWriteTimeUtc>DateTime.UtcNow.AddHours(-24))continue;
    string dst=Path.Combine(dir,"Разобрано",c,f.Name);if(File.Exists(dst))continue;
    rows.Add(new FileRow{Selected=true,Source=p,Destination=dst,Bytes=f.Length,Modified=f.LastWriteTimeUtc});
   }return rows;
  }
  public static string Digest(string p) {using(var s=File.OpenRead(p))using(var h=SHA256.Create())return Convert.ToBase64String(h.ComputeHash(s));}
  public static string Organize(List<FileRow> rows) {
   Directory.CreateDirectory(Data);string journal=Path.Combine(Data,"moves-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff")+".jsonl");int done=0,skip=0;
   foreach(var r in rows.Where(x=>x.Selected))try {
    if(!AllowedFolder(Path.GetDirectoryName(r.Source))||!NoLinks(r.Source)||File.Exists(r.Destination)){skip++;continue;}
    var f=new FileInfo(r.Source);if(f.Length!=r.Bytes||f.LastWriteTimeUtc!=r.Modified){skip++;continue;}
    Directory.CreateDirectory(Path.GetDirectoryName(r.Destination));if(!NoLinks(Path.GetDirectoryName(r.Destination))){skip++;continue;}
    r.Hash=Digest(r.Source);
    // Write-ahead journal supports recovery after a crash between journal and move.
    File.AppendAllText(journal,Json.Serialize(r)+Environment.NewLine,Encoding.UTF8);
    File.Move(r.Source,r.Destination);done++;
   }catch{skip++;}
   Log("Сортировка: "+done+". Журнал: "+journal);return "Перемещено: "+done+". Пропущено: "+skip;
  }
  public static string Undo() {
   Directory.CreateDirectory(Data);string j=Directory.GetFiles(Data,"moves-*.jsonl").OrderByDescending(x=>x).FirstOrDefault();if(j==null)return "Нет сортировок для отмены.";
   int done=0,skip=0;foreach(string line in File.ReadAllLines(j,Encoding.UTF8).Reverse())try {
    var r=Json.Deserialize<FileRow>(line);
    if(File.Exists(r.Source)&&!File.Exists(r.Destination)&&Digest(r.Source)==r.Hash)continue;
    if(File.Exists(r.Source)||!File.Exists(r.Destination)){skip++;continue;}
    if(!AllowedFolder(Path.GetDirectoryName(r.Source))||!Under(r.Destination,Path.Combine(Path.GetDirectoryName(r.Source),"Разобрано"))||!NoLinks(r.Destination)||Digest(r.Destination)!=r.Hash){skip++;continue;}
    File.Move(r.Destination,r.Source);done++;
   }catch{skip++;}
   if(skip==0)File.Move(j,j+".undone");Log("Отмена сортировки: "+done+"; пропущено: "+skip);
   return "Возвращено: "+done+". Пропущено: "+skip+". Изменённые файлы не трогаем.";
  }
  public static async Task<string> Run(string exe,string args,int milliseconds) {
   var info=new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
   using(var p=Process.Start(info)) {
    Task<string> output=p.StandardOutput.ReadToEndAsync(),error=p.StandardError.ReadToEndAsync();
    var wait=Task.Run(()=>p.WaitForExit(milliseconds));if(!await wait){try{p.Kill();}catch{}throw new Exception("Истекло время ожидания диагностики.");}
    string text=await output;await error;if(p.ExitCode!=0)throw new Exception("Диагностика недоступна (код "+p.ExitCode+").");return text.Trim();
   }
  }
  public static string Nvidia() {
   var options=new[]{Path.Combine(Environment.SystemDirectory,"nvidia-smi.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation","NVSMI","nvidia-smi.exe")};
   return options.FirstOrDefault(File.Exists);
  }
  public static async Task<Snapshot> ReadStats() {
   var m=new Memory();if(!GlobalMemoryStatusEx(m))throw new Exception("Не удалось получить RAM.");
   long idle,kernel,user;double cpu=-1;if(GetSystemTimes(out idle,out kernel,out user)){long total=kernel+user;if(prevTotal>0&&total>prevTotal)cpu=100.0*(1.0-(double)(idle-prevIdle)/(total-prevTotal));prevIdle=idle;prevTotal=total;}
   var s=new Snapshot{CPU=cpu,Load=m.Load,Total=m.Total,Free=m.Free};var n=Nvidia();if(n!=null)try{s.GPU=await Run(n,"--query-gpu=name,memory.used,memory.total,utilization.gpu,temperature.gpu,driver_version --format=csv,noheader,nounits",3500);}catch{s.GPU="NVIDIA: данные временно недоступны";}
   s.Disks=String.Join(Environment.NewLine,DriveInfo.GetDrives().Where(d=>d.IsReady&&d.DriveType==DriveType.Fixed).Select(d=>d.Name+" свободно "+Size(d.AvailableFreeSpace)+" / "+Size(d.TotalSize)));
   return s;
  }
  public static List<ProcessRow> Processes() {
   var list=new List<ProcessRow>();foreach(var p in Process.GetProcesses())using(p)try{list.Add(new ProcessRow{Started=p.StartTime.ToUniversalTime(),PID=p.Id,Name=p.ProcessName,Bytes=p.WorkingSet64,RAM=Size(p.WorkingSet64)});}catch{}
   return list.OrderByDescending(x=>x.Bytes).Take(40).ToList();
  }
  public static string Hardware() {
   var text=new StringBuilder();foreach(var q in new[]{"SELECT Name FROM Win32_Processor","SELECT Caption,Version,BuildNumber FROM Win32_OperatingSystem","SELECT Name,DriverVersion FROM Win32_VideoController","SELECT Model,Size,Status FROM Win32_DiskDrive"}) {
    try {using(var s=new ManagementObjectSearcher(q))using(var results=s.Get())foreach(ManagementObject o in results)using(o){foreach(PropertyData p in o.Properties)text.Append(p.Name+": "+p.Value+"  ");text.AppendLine();}}catch{text.AppendLine("Часть сведений WMI недоступна.");}
   }return text.ToString();
  }
  public static string Advice(Snapshot s) {
   if(s==null)return "Дождитесь первого измерения.";var a=new List<string>();
   if(s.Load>85)a.Add("RAM занята более чем на 85%. Проверьте процессы. Если такая нагрузка сохраняется во время генерации, увеличение RAM может уменьшить подкачку.");
   if(s.CPU>90)a.Add("Высокая загрузка CPU. Повторите измерение через минуту, прежде чем делать вывод об апгрейде.");
   foreach(var d in DriveInfo.GetDrives().Where(d=>d.IsReady&&d.DriveType==DriveType.Fixed))if((double)d.AvailableFreeSpace/d.TotalSize<0.15)a.Add("Мало свободного места на "+d.Name+". Проверьте крупные файлы и временные данные.");
   if(a.Count==0)a.Add("По текущему измерению критической нагрузки RAM, CPU или заполнения дисков не обнаружено.");
   a.Add("Для решения об апгрейде измеряйте нагрузку во время вашей обычной работы. Очистка диска не увеличивает мощность GPU.");return String.Join("\n\n",a);
  }
 }

}
