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
 public static class Program {
  [STAThread] public static int Main(string[] args) {
   if(args.Contains("--self-test"))return Tests();
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   bool created;using(var mutex=new System.Threading.Mutex(true,"Local\\PC-Care-Studio-01",out created)) {
    if(!created){MessageBox.Show("PC Care Studio уже запущена.");return 0;}
    var app=new Application();
    try{using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme.xaml"))app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"startup-error.txt"),ex.ToString());return 2;}
    bool smoke=args.Contains("--ui-test");
    app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e){if(smoke){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-error.txt"),e.Exception.ToString());Environment.Exit(3);}MessageBox.Show(e.Exception.Message,"PC Care Studio");e.Handled=true;};var window=new MainWindow();if(smoke)window.Loaded+=async delegate {try{await window.SmokeTest(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"screenshots"));}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-error.txt"),ex.ToString());Environment.Exit(4);}};app.Run(window);return 0;
   }
  }
  static int Tests() {
   string root=Path.Combine(Core.Temp,"PCCare-Test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   Core.Data=Path.Combine(root,"TestData");
   try {
    string recent=Path.Combine(root,"recent.tmp"),old=Path.Combine(root,"old.tmp"),model=Path.Combine(root,"model.safetensors");
    File.WriteAllText(recent,"recent");File.WriteAllText(old,"old");File.WriteAllText(model,"weights");
    foreach(string p in new[]{old,model}){File.SetLastWriteTimeUtc(p,DateTime.UtcNow.AddDays(-10));File.SetCreationTimeUtc(p,DateTime.UtcNow.AddDays(-10));}
    if(Core.Eligible(recent,DateTime.UtcNow.AddDays(-7)))throw new Exception("Recent file accepted");
    if(Core.Eligible(model,DateTime.UtcNow.AddDays(-7)))throw new Exception("Model accepted");
    if(!Core.Eligible(old,DateTime.UtcNow.AddDays(-7)))throw new Exception("Old temp rejected");
    if(Core.Under(Core.Temp+"-other\\file.tmp",Core.Temp))throw new Exception("Prefix escape");
    var f=new FileInfo(old);var rows=new List<FileRow>{new FileRow{Selected=true,Source=old,Bytes=f.Length,Modified=f.LastWriteTimeUtc}};
    using(var locked=new FileStream(old,FileMode.Open,FileAccess.Read,FileShare.None)){Core.Clean(rows);if(!File.Exists(old))throw new Exception("Locked file removed");}
    File.AppendAllText(old,"changed");Core.Clean(rows);if(!File.Exists(old))throw new Exception("Changed file removed");
    File.WriteAllText(old,"old");File.SetLastWriteTimeUtc(old,DateTime.UtcNow.AddDays(-10));File.SetCreationTimeUtc(old,DateTime.UtcNow.AddDays(-10));f.Refresh();rows[0].Bytes=f.Length;rows[0].Modified=f.LastWriteTimeUtc;
    Core.Clean(rows);if(File.Exists(old)||!File.Exists(model)||!File.Exists(recent))throw new Exception("Deletion boundaries failed");
    if(Core.AllowedFolder(Environment.GetFolderPath(Environment.SpecialFolder.Windows)))throw new Exception("System folder allowed");
    string organizerRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads","PCCare-Test-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(organizerRoot);
    try {
     string photo=Path.Combine(organizerRoot,"photo.jpg");File.WriteAllText(photo,"test-photo");File.SetLastWriteTimeUtc(photo,DateTime.UtcNow.AddDays(-3));
     var plan=Core.Plan(organizerRoot);if(plan.Count!=1)throw new Exception("Organizer plan failed");Core.Organize(plan);if(File.Exists(photo)||!File.Exists(plan[0].Destination))throw new Exception("Move failed");Core.Undo();if(!File.Exists(photo)||File.Exists(plan[0].Destination))throw new Exception("Undo failed");
     plan=Core.Plan(organizerRoot);Core.Organize(plan);File.AppendAllText(plan[0].Destination,"user change");Core.Undo();if(File.Exists(photo)||!File.Exists(plan[0].Destination))throw new Exception("Changed-file undo protection failed");
     File.WriteAllText(photo,"conflict");File.SetLastWriteTimeUtc(photo,DateTime.UtcNow.AddDays(-3));if(Core.Plan(organizerRoot).Count!=0)throw new Exception("Collision was not skipped");
    }finally{Directory.Delete(organizerRoot,true);}
    return 0;
   }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-error.txt"),ex.ToString());return 1;}finally{Directory.Delete(root,true);}
  }
 }
}
