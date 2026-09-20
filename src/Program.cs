using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.CodeDom.Compiler;
using System.Reflection;
using Microsoft.CSharp;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class AppVersion {
    public static string Current {
        get { var attribute=(AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(),typeof(AssemblyInformationalVersionAttribute));return attribute==null?"development":attribute.InformationalVersion; }
    }
}

internal static class AppIdentity {
    public const string Name="myTaskTiny";
    // Retained only to migrate existing installations and coordinate with old versions.
    public const string LegacyName="myTinyTask";
    public const string Repository="Crabyy/"+Name;
    public const string LegacyRepository="Crabyy/"+LegacyName;
    public static void ImportSettings(string executable,string localData) {
        if(!string.Equals(Path.GetFileNameWithoutExtension(executable),Name,StringComparison.OrdinalIgnoreCase))return;
        foreach(var suffix in new string[]{"settings.json","recordings.json","created-files.json"}) {
            CopyIfMissing(Path.Combine(Path.GetDirectoryName(executable),LegacyName+"."+suffix),Path.ChangeExtension(executable,suffix));
        }
        CopyIfMissing(Path.Combine(localData,LegacyName,"updates.json"),Path.Combine(localData,Name,"updates.json"));
    }
    static void CopyIfMissing(string oldPath,string newPath) {
        if(File.Exists(oldPath)&&!File.Exists(newPath)){Directory.CreateDirectory(Path.GetDirectoryName(newPath));File.Copy(oldPath,newPath,false);}
    }
    public static void Test() {
        string root=Path.Combine(Path.GetTempPath(),Name+"-migration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            string exe=Path.Combine(root,Name+".exe"),data=Path.Combine(root,"local-data");
            foreach(var suffix in new string[]{"settings.json","recordings.json","created-files.json"})File.WriteAllText(Path.Combine(root,LegacyName+"."+suffix),"legacy fixture");
            Directory.CreateDirectory(Path.Combine(data,LegacyName));File.WriteAllText(Path.Combine(data,LegacyName,"updates.json"),"legacy updates");
            ImportSettings(exe,data);
            foreach(var suffix in new string[]{"settings.json","recordings.json","created-files.json"})if(File.ReadAllText(Path.ChangeExtension(exe,suffix))!="legacy fixture")throw new Exception("Legacy file migration failed.");
            if(File.ReadAllText(Path.Combine(data,Name,"updates.json"))!="legacy updates")throw new Exception("Update preferences migration failed.");
            File.WriteAllText(Path.ChangeExtension(exe,"settings.json"),"new settings");ImportSettings(exe,data);
            if(File.ReadAllText(Path.ChangeExtension(exe,"settings.json"))!="new settings"||!File.Exists(Path.Combine(root,LegacyName+".settings.json")))throw new Exception("Migration overwrote existing data.");
            string export=Path.Combine(root,"exported.exe");ImportSettings(export,data);if(File.Exists(Path.ChangeExtension(export,"settings.json")))throw new Exception("Export imported recorder settings.");
        }finally{Directory.Delete(root,true);}
    }
}

public class ReleaseAsset { public string name {get;set;} public string state {get;set;} public long size {get;set;} }
public class ReleaseInfo {
    public string tag_name {get;set;}
    public string body {get;set;}
    public bool draft {get;set;}
    public bool prerelease {get;set;}
    public List<ReleaseAsset> assets {get;set;}
    public Version Number {get {return UpdateService.ParseVersion(tag_name);}}
    internal string SourceRepository;
    public string Page {get {return "https://github.com/"+(SourceRepository??AppIdentity.Repository)+"/releases/tag/"+Uri.EscapeDataString(tag_name);}}
}
public class UpdatePreferences {
    public bool CheckOnStartup {get;set;}
    public string SkippedVersion {get;set;}
    public UpdatePreferences() {CheckOnStartup=true;}
    public static UpdatePreferences Load(string path) {
        try {if(File.Exists(path))return Recording.Serializer().Deserialize<UpdatePreferences>(File.ReadAllText(path))??new UpdatePreferences();}catch{}
        return new UpdatePreferences();
    }
    public void Save(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";
        try{File.WriteAllText(temp,Recording.Serializer().Serialize(this));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
internal static class UpdateService {
    public static Version ParseVersion(string tag) {
        if(string.IsNullOrEmpty(tag))return null;
        if(tag.StartsWith("v",StringComparison.OrdinalIgnoreCase))tag=tag.Substring(1);
        if(!System.Text.RegularExpressions.Regex.IsMatch(tag,@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"))return null;
        Version version;return Version.TryParse(tag,out version)?version:null;
    }
    public static ReleaseInfo ParseRelease(string json) {
        var release=Recording.Serializer().Deserialize<ReleaseInfo>(json);
        if(release==null||release.draft||release.prerelease||release.Number==null)return null;
        // A tag or source-only release is not an installable application update.
        if(release.assets==null||!release.assets.Exists(a=>a!=null&&a.state=="uploaded"&&a.size>0&&(a.name=="myTaskTiny.exe"||a.name=="myTaskTiny-"+release.Number+".zip"||a.name==AppIdentity.LegacyName+".exe"||a.name==AppIdentity.LegacyName+"-"+release.Number+".zip")))return null;
        return release;
    }
    public static bool ShouldOffer(ReleaseInfo release,string current,string skipped,bool manual) {
        var number=ParseVersion(current);
        return release!=null&&number!=null&&release.Number>number&&(manual||!string.Equals(release.Number.ToString(),skipped,StringComparison.Ordinal));
    }
    public static ReleaseInfo Fetch() {
        return FetchFrom(AppIdentity.Repository)??FetchFrom(AppIdentity.LegacyRepository);
    }
    static ReleaseInfo FetchFrom(string repository) {
        System.Net.ServicePointManager.SecurityProtocol|=System.Net.SecurityProtocolType.Tls12;
        var request=(System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://api.github.com/repos/"+repository+"/releases/latest");
        request.UserAgent="myTaskTiny/"+AppVersion.Current;
        request.Accept="application/vnd.github+json";request.Headers["X-GitHub-Api-Version"]="2022-11-28";
        request.Timeout=15000;request.ReadWriteTimeout=15000;
        try {
            using(var response=(System.Net.HttpWebResponse)request.GetResponse())
            using(var reader=new StreamReader(response.GetResponseStream())) {
                var content=new System.Text.StringBuilder();var buffer=new char[4096];int count;
                while((count=reader.Read(buffer,0,buffer.Length))>0){content.Append(buffer,0,count);if(content.Length>2000000)throw new IOException("Release response is too large.");}
                var release=ParseRelease(content.ToString());if(release!=null)release.SourceRepository=repository;return release;
            }
        }catch(System.Net.WebException ex){var response=ex.Response as System.Net.HttpWebResponse;if(response!=null){bool missing=response.StatusCode==System.Net.HttpStatusCode.NotFound;response.Dispose();if(missing)return null;}throw;}
    }
    public static void Test() {
        if(ParseVersion("v1.10.0")<=ParseVersion("1.9.9")||ParseVersion("1.0.0-beta")!=null||ParseVersion("../1.2.3")!=null)throw new Exception("Update version parsing failed.");
        var release=new ReleaseInfo{tag_name="v2.0.0",body="Changes",assets=new List<ReleaseAsset>{new ReleaseAsset{name="myTaskTiny.exe",state="uploaded",size=100}}};
        var parsed=ParseRelease(Recording.Serializer().Serialize(release));
        if(!ShouldOffer(parsed,"1.9.0",null,false)||ShouldOffer(parsed,"2.0.0",null,false)||ShouldOffer(parsed,"3.0.0",null,false)||ShouldOffer(parsed,"1.9.0","2.0.0",false)||!ShouldOffer(parsed,"1.9.0","2.0.0",true))throw new Exception("Update selection failed.");
        if(parsed.Page!="https://github.com/Crabyy/myTaskTiny/releases/tag/v2.0.0")throw new Exception("Update URL failed.");
        release.assets[0].name=AppIdentity.LegacyName+"-2.0.0.zip";if(ParseRelease(Recording.Serializer().Serialize(release))==null)throw new Exception("Legacy release compatibility failed.");
        release.prerelease=true;if(ParseRelease(Recording.Serializer().Serialize(release))!=null)throw new Exception("Prerelease offered.");release.prerelease=false;
        release.draft=true;if(ParseRelease(Recording.Serializer().Serialize(release))!=null)throw new Exception("Draft offered.");release.draft=false;
        release.assets.Clear();if(ParseRelease(Recording.Serializer().Serialize(release))!=null)throw new Exception("Source-only release offered.");
        string dir=Path.Combine(Path.GetTempPath(),"myTaskTiny-updates-"+Guid.NewGuid().ToString("N")),path=Path.Combine(dir,"updates.json");
        try{var prefs=new UpdatePreferences{CheckOnStartup=false,SkippedVersion="2.0.0"};prefs.Save(path);var loaded=UpdatePreferences.Load(path);if(loaded.CheckOnStartup||loaded.SkippedVersion!="2.0.0")throw new Exception("Update preferences failed.");prefs.CheckOnStartup=true;prefs.Save(path);if(!UpdatePreferences.Load(path).CheckOnStartup)throw new Exception("Update preferences replacement failed.");}finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
// Ask Windows Attachment Services to apply download-origin and antivirus policy.
// A failure is propagated; the updater never removes a block or zone marking.
internal static class AttachmentPolicy {
    [ComImport,Guid("4125DD96-E03A-4103-8F70-E0597D803B9C")]
    class AttachmentServices {}
    [ComImport,Guid("73DB1241-1E85-4581-8E4F-A81E1D0F8C57"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAttachmentExecute {
        void SetClientTitle([MarshalAs(UnmanagedType.LPWStr)]string title);
        void SetClientGuid(ref Guid guid);
        void SetLocalPath([MarshalAs(UnmanagedType.LPWStr)]string path);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)]string name);
        void SetSource([MarshalAs(UnmanagedType.LPWStr)]string source);
        void SetReferrer([MarshalAs(UnmanagedType.LPWStr)]string referrer);
        void CheckPolicy();
        void Prompt(IntPtr window,int prompt,out int action);
        void Save();
    }
    public static void Apply(string path,Uri source) {
        var service=(IAttachmentExecute)new AttachmentServices();
        try {
            service.SetClientTitle(AppIdentity.Name);
            var id=new Guid("a92e7d76-257f-44a6-8c3d-cb5cbf061e68");service.SetClientGuid(ref id);
            service.SetLocalPath(path);service.SetSource(source.AbsoluteUri);service.Save();
        }finally{Marshal.ReleaseComObject(service);}
    }
}

public class UpdatePlan {
    public string Target {get;set;}
    public string Directory {get;set;}
    public string ExpectedHash {get;set;}
    public string OriginalHash {get;set;}
    public string StartupToken {get;set;}
}
internal static class AutoUpdate {
    internal delegate void Downloader(Uri url,Stream output,long limit,System.Threading.CancellationToken cancel,Action<int> progress);
    const long MaximumExecutableSize=50000000;
    public static bool CanInstall(ReleaseInfo release) {
        return release!=null&&release.assets!=null&&release.assets.Exists(a=>a!=null&&a.name=="myTaskTiny.exe"&&a.state=="uploaded"&&a.size>0&&a.size<=MaximumExecutableSize)&&release.assets.Exists(a=>a!=null&&a.name=="SHA256SUMS.txt"&&a.state=="uploaded"&&a.size>0&&a.size<=65536);
    }
    internal static string ReadHash(string text) {
        string result=null;
        foreach(string line in text.Split('\n')) {
            var match=System.Text.RegularExpressions.Regex.Match(line.Trim(),@"^([a-fA-F0-9]{64})\s+\*?myTaskTiny\.exe$");
            if(!match.Success)continue;
            if(result!=null)throw new IOException("The release checksum is ambiguous.");
            result=match.Groups[1].Value;
        }
        if(result==null)throw new IOException("The release has no valid checksum for myTaskTiny.exe.");
        return result;
    }
    internal static bool AllowedDownload(Uri uri) {
        return uri.Scheme==Uri.UriSchemeHttps&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&(uri.Host=="github.com"||uri.Host=="release-assets.githubusercontent.com"||uri.Host=="objects.githubusercontent.com"||uri.Host=="github-releases.githubusercontent.com");
    }
    internal static void Download(Uri url,Stream output,long limit,System.Threading.CancellationToken cancel,Action<int> progress) {
        System.Net.ServicePointManager.SecurityProtocol|=System.Net.SecurityProtocolType.Tls12;
        for(int redirects=0;redirects<6;redirects++) {
            cancel.ThrowIfCancellationRequested();
            if(!AllowedDownload(url))throw new IOException("The update download is not on an approved GitHub HTTPS host.");
            var request=(System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            request.UserAgent="myTaskTiny/"+AppVersion.Current;request.AllowAutoRedirect=false;request.Timeout=15000;request.ReadWriteTimeout=15000;
            using(cancel.Register(()=>request.Abort())) {
                try {
                    using(var response=(System.Net.HttpWebResponse)request.GetResponse()) {
                        int code=(int)response.StatusCode;
                        if(code==301||code==302||code==303||code==307||code==308){url=new Uri(url,response.Headers["Location"]);continue;}
                        if(code!=200||response.ContentLength>limit)throw new IOException("The update download has an unexpected size or status.");
                        using(var input=response.GetResponseStream())CopyDownload(input,output,limit,response.ContentLength,cancel,progress);
                        return;
                    }
                }catch(System.Net.WebException){cancel.ThrowIfCancellationRequested();throw;}
            }
        }
        throw new IOException("The update download redirected too many times.");
    }
    internal static void CopyDownload(Stream input,Stream output,long limit,long length,System.Threading.CancellationToken cancel,Action<int> progress) {
        var buffer=new byte[65536];long total=0;int count;
        while(true) {
            cancel.ThrowIfCancellationRequested();count=input.Read(buffer,0,buffer.Length);if(count==0)break;
            total+=count;if(total>limit)throw new IOException("The update download is too large.");
            output.Write(buffer,0,count);if(progress!=null)progress(length>0?(int)Math.Min(100,total*100/length):0);
        }
        if(length>=0&&total!=length)throw new IOException("The update download was incomplete.");
    }
    public static UpdatePlan Prepare(ReleaseInfo release,string target,System.Threading.CancellationToken cancel,Action<int> progress) {
        var plan=Prepare(release,target,cancel,progress,Download);
        try {
            cancel.ThrowIfCancellationRequested();
            AttachmentPolicy.Apply(Path.Combine(plan.Directory,"download.exe"),new Uri("https://github.com/"+AppIdentity.Repository+"/releases/download/"+Uri.EscapeDataString(release.tag_name)+"/myTaskTiny.exe"));
            cancel.ThrowIfCancellationRequested();
            if(!string.Equals(UninstallService.Hash(Path.Combine(plan.Directory,"download.exe")),plan.ExpectedHash,StringComparison.OrdinalIgnoreCase))throw new IOException("The update changed during Windows security verification.");
            return plan;
        }catch{Cleanup(plan);throw;}
    }
    internal static UpdatePlan Prepare(ReleaseInfo release,string target,System.Threading.CancellationToken cancel,Action<int> progress,Downloader download) {
        if(!CanInstall(release)||!UpdateService.ShouldOffer(release,AppVersion.Current,null,true))throw new IOException("This release cannot be installed automatically. Use the release page instead.");
        cancel.ThrowIfCancellationRequested();target=Path.GetFullPath(target);
        var plan=new UpdatePlan{Target=target,StartupToken=Guid.NewGuid().ToString("N"),OriginalHash=UninstallService.Hash(target),Directory=Path.Combine(Path.GetDirectoryName(target),".myTaskTiny-update-"+Guid.NewGuid().ToString("N"))};
        try {
            Directory.CreateDirectory(plan.Directory);
            string baseUrl="https://github.com/"+AppIdentity.Repository+"/releases/download/"+Uri.EscapeDataString(release.tag_name)+"/";
            using(var checksum=new MemoryStream()) {
                download(new Uri(baseUrl+"SHA256SUMS.txt"),checksum,65536,cancel,null);
                plan.ExpectedHash=ReadHash(System.Text.Encoding.UTF8.GetString(checksum.ToArray()));
            }
            string staged=Path.Combine(plan.Directory,"download.exe");
            using(var output=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None))download(new Uri(baseUrl+"myTaskTiny.exe"),output,MaximumExecutableSize,cancel,progress);
            cancel.ThrowIfCancellationRequested();
            long expectedSize=release.assets.Find(a=>a!=null&&a.name=="myTaskTiny.exe").size;
            if(new FileInfo(staged).Length!=expectedSize||!string.Equals(UninstallService.Hash(staged),plan.ExpectedHash,StringComparison.OrdinalIgnoreCase))throw new IOException("The downloaded update did not match the published checksum. Your current app has not been changed.");
            var info=FileVersionInfo.GetVersionInfo(staged);
            if(info.ProductName!=AppIdentity.Name||UpdateService.ParseVersion(info.ProductVersion)!=release.Number)throw new IOException("The downloaded executable does not match the announced app version.");
            return plan;
        }catch{Cleanup(plan);throw;}
    }
    public static void Cleanup(UpdatePlan plan) {
        if(plan==null||!Directory.Exists(plan.Directory))return;
        // Delete only updater-owned files. Never recursively delete the application folder.
        foreach(string name in new string[]{"download.exe","install.ps1","plan.json","ready.txt","started.txt"})try{File.Delete(Path.Combine(plan.Directory,name));}catch{}
        try{Directory.Delete(plan.Directory,false);}catch{}
    }
    public static void ConfirmStartup(string[] args) {
        if(args.Length!=3||args[0]!="--updated")return;
        string exe=Assembly.GetExecutingAssembly().Location,folder=Path.GetFullPath(args[1]);
        if(!string.Equals(Path.GetDirectoryName(folder),Path.GetDirectoryName(exe),StringComparison.OrdinalIgnoreCase)||!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(folder),@"^\.myTaskTiny-update-[a-f0-9]{32}$"))return;
        string json=Path.Combine(folder,"plan.json");if(!File.Exists(json)||new FileInfo(json).Length>8192)return;
        var plan=Recording.Serializer().Deserialize<UpdatePlan>(File.ReadAllText(json));
        if(plan==null||plan.StartupToken!=args[2]||!string.Equals(plan.Directory,folder,StringComparison.OrdinalIgnoreCase)||!string.Equals(plan.Target,exe,StringComparison.OrdinalIgnoreCase)||!string.Equals(plan.ExpectedHash,UninstallService.Hash(exe),StringComparison.OrdinalIgnoreCase))return;
        File.WriteAllText(Path.Combine(folder,"started.txt"),Process.GetCurrentProcess().Id+":"+plan.StartupToken);
    }
    public static Process Start(UpdatePlan plan,int parent,bool quiet) {
        string script=Path.Combine(plan.Directory,"install.ps1"),json=Path.Combine(plan.Directory,"plan.json");
        File.WriteAllText(script,WorkerScript,System.Text.Encoding.UTF8);File.WriteAllText(json,Recording.Serializer().Serialize(plan),System.Text.Encoding.UTF8);
        string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
        var process=Process.Start(new ProcessStartInfo(powershell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\" -PlanPath \""+json+"\" -ParentId "+parent+(quiet?" -Quiet":"")){UseShellExecute=false,CreateNoWindow=true});
        if(process==null)throw new IOException("Could not start the update installer.");
        var wait=Stopwatch.StartNew();
        while(!File.Exists(Path.Combine(plan.Directory,"ready.txt"))&&!process.HasExited&&wait.ElapsedMilliseconds<10000)System.Threading.Thread.Sleep(50);
        // A quiet fixture may finish and clean its ready marker before this polling loop sees it.
        if(!File.Exists(Path.Combine(plan.Directory,"ready.txt"))&&!(quiet&&process.HasExited)) {process.Dispose();throw new IOException("The update installer could not start. Your current app is still running.");}
        return process;
    }
    internal static void TestStartupConfirmation() {
        string exe=Assembly.GetExecutingAssembly().Location;
        var plan=new UpdatePlan{Target=exe,Directory=Path.Combine(Path.GetDirectoryName(exe),".myTaskTiny-update-"+Guid.NewGuid().ToString("N")),ExpectedHash=UninstallService.Hash(exe),StartupToken=Guid.NewGuid().ToString("N")};
        Directory.CreateDirectory(plan.Directory);
        try {
            File.WriteAllText(Path.Combine(plan.Directory,"plan.json"),Recording.Serializer().Serialize(plan));
            ConfirmStartup(new string[]{"--updated",plan.Directory,"wrong-token"});
            string marker=Path.Combine(plan.Directory,"started.txt");if(File.Exists(marker))throw new Exception("Startup accepted wrong token.");
            ConfirmStartup(new string[]{"--updated",plan.Directory,plan.StartupToken});
            if(File.ReadAllText(marker)!=Process.GetCurrentProcess().Id+":"+plan.StartupToken)throw new Exception("Startup confirmation failed.");
        }finally{Cleanup(plan);}
    }
    public static void Test() {
        TestStartupConfirmation();
        if(AllowedDownload(new Uri("http://github.com/test"))||AllowedDownload(new Uri("https://github.com.example.com/test"))||AllowedDownload(new Uri("https://example.com/test"))||!AllowedDownload(new Uri("https://release-assets.githubusercontent.com/test")))throw new Exception("Update download host validation failed.");
        bool refused=false;try{ReadHash("invalid checksum");}catch(IOException){refused=true;}if(!refused)throw new Exception("Invalid update checksum accepted.");
        string root=Path.Combine(Path.GetTempPath(),"myTaskTiny-auto-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            string policyFile=Path.Combine(root,"attachment-policy.txt");File.WriteAllText(policyFile,"harmless download-policy test");
            AttachmentPolicy.Apply(policyFile,new Uri("https://github.com/"+AppIdentity.Repository+"/releases"));
            if(File.ReadAllText(policyFile)!="harmless download-policy test")throw new Exception("Windows attachment policy modified the text fixture.");
            string fixture=Path.Combine(root,"fixture.exe"),badStart=Path.Combine(root,"bad-start.exe"),silentExit=Path.Combine(root,"silent-exit.exe"),target=Path.Combine(root,"myTaskTiny.exe");
            string metadata="[assembly:System.Reflection.AssemblyProduct(\"myTaskTiny\")][assembly:System.Reflection.AssemblyInformationalVersion(\"2.0.0\")]";
            using(var compiler=new CSharpCodeProvider()) {
                var options=new CompilerParameters(new string[]{"System.dll"},fixture){GenerateExecutable=true,CompilerOptions="/target:winexe"};
                var result=compiler.CompileAssemblyFromSource(options,metadata+"class TestApp {static void Main(string[] args){System.IO.File.WriteAllText(\"restarted.txt\",\"yes\");if(args.Length==3)System.IO.File.WriteAllText(System.IO.Path.Combine(args[1],\"started.txt\"),System.Diagnostics.Process.GetCurrentProcess().Id+\":\"+args[2]);}}");
                if(result.Errors.HasErrors)throw new Exception("Update fixture build failed.");
                options.OutputAssembly=badStart;result=compiler.CompileAssemblyFromSource(options,metadata+"class TestApp {static void Main(){System.Environment.Exit(9);}}");
                if(result.Errors.HasErrors)throw new Exception("Update failure fixture build failed.");
                options.OutputAssembly=silentExit;result=compiler.CompileAssemblyFromSource(options,metadata+"class TestApp {static void Main(){}}");
                if(result.Errors.HasErrors)throw new Exception("Update silent-exit fixture build failed.");
            }
            File.WriteAllText(target,"original app");File.WriteAllText(Path.Combine(root,"keep.mtt"),"recording");File.WriteAllText(Path.Combine(root,"myTaskTiny.settings.json"),"settings");
            var release=new ReleaseInfo{tag_name="v2.0.0",assets=new List<ReleaseAsset>{new ReleaseAsset{name="myTaskTiny.exe",state="uploaded",size=new FileInfo(fixture).Length},new ReleaseAsset{name="SHA256SUMS.txt",state="uploaded",size=82}}};
            string payload=fixture;bool badHash=false,cancelTransfer=false;
            Downloader transfer=(uri,output,limit,cancel,progress)=>{
                if(cancelTransfer)throw new OperationCanceledException();
                byte[] bytes=uri.AbsolutePath.EndsWith("SHA256SUMS.txt")?System.Text.Encoding.UTF8.GetBytes((badHash?new string('0',64):UninstallService.Hash(payload))+"  myTaskTiny.exe\n"):File.ReadAllBytes(payload);
                using(var input=new MemoryStream(bytes))CopyDownload(input,output,limit,bytes.Length,cancel,progress);
            };
            badHash=true;refused=false;try{Prepare(release,target,System.Threading.CancellationToken.None,null,transfer);}catch(IOException){refused=true;}
            if(!refused||File.ReadAllText(target)!="original app"||Directory.GetDirectories(root).Length!=0)throw new Exception("Failed verification changed the app or left staging files.");badHash=false;
            cancelTransfer=true;refused=false;try{Prepare(release,target,System.Threading.CancellationToken.None,null,transfer);}catch(OperationCanceledException){refused=true;}
            if(!refused||Directory.GetDirectories(root).Length!=0)throw new Exception("Cancelled download was not cleaned up.");cancelTransfer=false;
            release.tag_name="v3.0.0";refused=false;try{Prepare(release,target,System.Threading.CancellationToken.None,null,transfer);}catch(IOException){refused=true;}
            if(!refused||Directory.GetDirectories(root).Length!=0)throw new Exception("Mismatched executable version accepted.");release.tag_name="v2.0.0";
            var plan=Prepare(release,target,System.Threading.CancellationToken.None,null,transfer);
            string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
            using(var parent=Process.Start(new ProcessStartInfo(powershell,"-NoProfile -NonInteractive -Command Start-Sleep -Seconds 3"){UseShellExecute=false,CreateNoWindow=true}))
            using(var worker=Start(plan,parent.Id,true)) {
                if(!parent.HasExited&&File.ReadAllText(target)!="original app")throw new Exception("Update replaced app before parent exit.");
                if(!worker.WaitForExit(15000)||worker.ExitCode!=0)throw new Exception("Update replacement/restart failed.");
            }
            if(UninstallService.Hash(target)!=UninstallService.Hash(fixture)||!File.Exists(Path.Combine(root,"restarted.txt"))||Directory.Exists(plan.Directory)||File.ReadAllText(Path.Combine(root,"keep.mtt"))!="recording"||File.ReadAllText(Path.Combine(root,"myTaskTiny.settings.json"))!="settings")throw new Exception("Update did not preserve user files, restart, or clean staging.");
            payload=badStart;release.assets[0].size=new FileInfo(payload).Length;plan=Prepare(release,target,System.Threading.CancellationToken.None,null,transfer);
            using(var worker=Start(plan,-1,true)){if(!worker.WaitForExit(15000)||worker.ExitCode!=1)throw new Exception("Startup failure not detected.");}
            if(UninstallService.Hash(target)!=UninstallService.Hash(fixture)||Directory.Exists(plan.Directory))throw new Exception("Update rollback failed: restored="+(UninstallService.Hash(target)==UninstallService.Hash(fixture))+", staging="+Directory.Exists(plan.Directory));
            payload=silentExit;release.assets[0].size=new FileInfo(payload).Length;plan=Prepare(release,target,System.Threading.CancellationToken.None,null,transfer);
            using(var worker=Start(plan,-1,true)){if(!worker.WaitForExit(15000)||worker.ExitCode!=1)throw new Exception("Exit without startup confirmation accepted.");}
            if(UninstallService.Hash(target)!=UninstallService.Hash(fixture)||Directory.Exists(plan.Directory))throw new Exception("Unconfirmed startup did not restore the previous app.");
            payload=fixture;release.assets[0].size=new FileInfo(payload).Length;plan=Prepare(release,target,System.Threading.CancellationToken.None,null,transfer);File.WriteAllText(Path.Combine(plan.Directory,"download.exe"),"tampered");
            using(var worker=Start(plan,-1,true)){if(!worker.WaitForExit(15000)||worker.ExitCode!=1)throw new Exception("Installer accepted changed download.");}
            if(UninstallService.Hash(target)!=UninstallService.Hash(fixture))throw new Exception("Changed download replaced the installed app.");
            plan=Prepare(release,target,System.Threading.CancellationToken.None,null,transfer);
            using(var locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var worker=Start(plan,-1,true)){if(!worker.WaitForExit(15000)||worker.ExitCode!=1)throw new Exception("Locked executable replacement did not fail safely.");}
            if(UninstallService.Hash(target)!=UninstallService.Hash(fixture)||Directory.Exists(plan.Directory))throw new Exception("Replacement failure changed the installed app.");
            using(var input=new MemoryStream(new byte[10]))using(var output=new MemoryStream()) {
                refused=false;try{CopyDownload(input,output,5,10,System.Threading.CancellationToken.None,null);}catch(IOException){refused=true;}if(!refused)throw new Exception("Oversized download accepted.");
            }
            using(var input=new MemoryStream(new byte[3]))using(var output=new MemoryStream()) {
                refused=false;try{CopyDownload(input,output,10,10,System.Threading.CancellationToken.None,null);}catch(IOException){refused=true;}if(!refused)throw new Exception("Incomplete download accepted.");
            }
        }finally{Directory.Delete(root,true);}
    }
    public const string WorkerScript=@"param([Parameter(Mandatory=$true)][string]$PlanPath,[int]$ParentId,[switch]$Quiet)
$ErrorActionPreference = 'Stop'
$plan = $null
$changed = $false
$parentExited = $false
$started = $null
$failure = $null
try {
    $plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
    $download = Join-Path $plan.Directory 'download.exe'
    $backup = Join-Path $plan.Directory 'previous.exe'
    Set-Content -LiteralPath (Join-Path $plan.Directory 'ready.txt') -Value 'ready'
    $parent = $null
    try { $parent = [Diagnostics.Process]::GetProcessById($ParentId) } catch [ArgumentException] {}
    if ($parent -and -not $parent.WaitForExit(30000)) { throw 'The app did not close. The update was not installed.' }
    $parentExited = $true
    if ((Get-FileHash -LiteralPath $plan.Target -Algorithm SHA256).Hash -ne $plan.OriginalHash) { throw 'The installed app changed while downloading. The update was not installed.' }
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $plan.ExpectedHash) { throw 'The downloaded update changed. The update was not installed.' }
    [IO.File]::Replace($download,$plan.Target,$backup)
    $changed = $true
    $arguments = '--updated ""' + $plan.Directory + '"" ' + $plan.StartupToken
    $started = Start-Process -FilePath $plan.Target -ArgumentList $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($plan.Target)) -PassThru
    $confirmation = Join-Path $plan.Directory 'started.txt'
    $expected = [string]$started.Id + ':' + $plan.StartupToken
    $wait = [Diagnostics.Stopwatch]::StartNew()
    $ready = $false
    while ($wait.ElapsedMilliseconds -lt 15000) {
        if ((Test-Path -LiteralPath $confirmation) -and (Get-Content -LiteralPath $confirmation -Raw) -eq $expected) { $ready = $true; break }
        if ($started.HasExited) { break }
        Start-Sleep -Milliseconds 100
    }
    if (-not $ready) { throw 'The updated app did not confirm successful startup.' }
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
} catch {
    $failure = $_.Exception.Message
    if ($changed -and (Test-Path -LiteralPath $backup)) {
        try {
            if ($started -and -not $started.HasExited) { [void]$started.CloseMainWindow(); if (-not $started.WaitForExit(5000)) { throw 'The new app is still running.' } }
            if ((Get-FileHash -LiteralPath $plan.Target -Algorithm SHA256).Hash -ne $plan.ExpectedHash) { throw 'The installed app changed after replacement.' }
            [IO.File]::Replace($backup,$plan.Target,$download)
            $changed = $false
            $failure += ' The previous version was restored.'
        } catch { $failure += ' Recovery could not finish. The previous executable is at: ' + $backup }
    }
    if ($parentExited -and -not $Quiet -and -not $changed) {
        try { [void](Start-Process -FilePath $plan.Target -WorkingDirectory ([IO.Path]::GetDirectoryName($plan.Target))) } catch {}
    }
    if (-not $Quiet) { Add-Type -AssemblyName System.Windows.Forms; [void][Windows.Forms.MessageBox]::Show($failure,'myTaskTiny update') }
} finally {
    if ($plan) {
        foreach ($name in @('download.exe','plan.json','ready.txt','started.txt','install.ps1')) { Remove-Item -LiteralPath (Join-Path $plan.Directory $name) -Force -ErrorAction SilentlyContinue }
        try { [IO.Directory]::Delete($plan.Directory,$false) } catch {}
    }
}
if ($failure) { if ($Quiet) { Write-Output $failure }; exit 1 }
";
}
internal class InstallUpdateDialog : Form {
    readonly System.Threading.CancellationTokenSource cancel=new System.Threading.CancellationTokenSource();
    bool working=true;
    public UpdatePlan Plan {get;private set;}
    public InstallUpdateDialog(ReleaseInfo release,string target) : this(release,target,AutoUpdate.Prepare) {}
    internal InstallUpdateDialog(ReleaseInfo release,string target,Func<ReleaseInfo,string,System.Threading.CancellationToken,Action<int>,UpdatePlan> prepare) {
        Text="Updating myTaskTiny";AutoScaleDimensions=new SizeF(96F,96F);AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(420,160);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;Font=new Font("Segoe UI",9);BackColor=Theme.Canvas;
        var label=new Label{Text="Downloading and verifying the update…",Location=new Point(20,20),Size=new Size(380,22)};Controls.Add(label);
        var progress=new ProgressBar{Location=new Point(20,52),Size=new Size(380,20)};Controls.Add(progress);
        Controls.Add(new Label{Text="The app will restart. Recordings and settings are kept.",Location=new Point(20,82),Size=new Size(380,22)});
        var stop=new ToolButton{Text="Cancel",Location=new Point(300,116),Size=new Size(100,28)};Controls.Add(stop);
        Action requestCancel=()=>{cancel.Cancel();stop.Enabled=false;label.Text="Cancelling…";};stop.Click+=(s,e)=>requestCancel();
        FormClosing+=(s,e)=>{if(working){e.Cancel=true;requestCancel();}};
        Shown+=async (s,e)=> {
            var report=new Progress<int>(value=>{if(!IsDisposed)progress.Value=Math.Max(0,Math.Min(100,value));});
            try {
                Plan=await System.Threading.Tasks.Task.Run(()=>prepare(release,target,cancel.Token,value=>((IProgress<int>)report).Report(value)));
                if(cancel.IsCancellationRequested){AutoUpdate.Cleanup(Plan);Plan=null;working=false;DialogResult=DialogResult.Cancel;}
                else {working=false;DialogResult=DialogResult.OK;}
            } catch(OperationCanceledException){working=false;DialogResult=DialogResult.Cancel;}
            catch(Exception ex){MessageBox.Show(this,"The update could not be installed. Your current app has not been changed.\n\n"+ex.Message+"\n\nYou can try again or use Open release page.","myTaskTiny update",MessageBoxButtons.OK,MessageBoxIcon.Information);working=false;DialogResult=DialogResult.Cancel;}
            finally{working=false;Close();}
        };
    }
    internal static void Test() {
        bool cancelled=false;
        using(var dialog=new InstallUpdateDialog(null,null,(release,target,token,progress)=>{progress(50);token.WaitHandle.WaitOne(5000);cancelled=token.IsCancellationRequested;token.ThrowIfCancellationRequested();throw new Exception("Cancellation was not requested.");}))
        using(var close=new Timer{Interval=200}) {
            close.Tick+=(s,e)=>{close.Stop();using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-progress-preview.png"));}dialog.Close();};
            dialog.Shown+=(s,e)=>close.Start();
            if(dialog.ShowDialog()!=DialogResult.Cancel||!cancelled||dialog.Plan!=null)throw new Exception("Closing update progress did not cancel the download.");
        }
        var expected=new UpdatePlan();
        using(var dialog=new InstallUpdateDialog(null,null,(release,target,token,progress)=>expected))if(dialog.ShowDialog()!=DialogResult.OK||dialog.Plan!=expected)throw new Exception("Completed download did not reach installation.");
    }
    protected override void Dispose(bool disposing){if(disposing)cancel.Dispose();base.Dispose(disposing);}
}

internal class UpdateDialog : Form {
    public UpdateDialog(ReleaseInfo release) {
        SuspendLayout();
        Text="Update available";AutoScaleDimensions=new SizeF(96F,96F);AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(460,372);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;ShowIcon=false;Font=new Font("Segoe UI",9);BackColor=Theme.Canvas;ForeColor=Theme.Text;
        Controls.Add(new Label{Text="myTaskTiny "+release.Number+" is available",Location=new Point(20,16),AutoSize=true,Font=new Font("Segoe UI Semibold",12f)});
        Controls.Add(new Label{Text="You have "+AppVersion.Current+"  ·  Release notes",Location=new Point(21,44),AutoSize=true,ForeColor=Theme.Muted});
        var notes=new TextBox{Text=string.IsNullOrWhiteSpace(release.body)?"No release notes provided.":release.body.Replace("\r\n","\n").Replace("\n",Environment.NewLine),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.FixedSingle,Location=new Point(20,68),Size=new Size(420,170),BackColor=Color.White,ForeColor=Theme.Text};Controls.Add(notes);
        Controls.Add(new Label{Text=AutoUpdate.CanInstall(release)?"Update now downloads, verifies, and installs this version, then restarts the app. Your recordings and settings are kept.":"Automatic installation is unavailable for this release. Use Open release page to download it manually.",Location=new Point(20,248),Size=new Size(420,32),ForeColor=Theme.Muted});
        var manual=new LinkLabel{Text="Open release page",Location=new Point(20,284),AutoSize=true};manual.LinkClicked+=(s,e)=>{DialogResult=DialogResult.Retry;};Controls.Add(manual);
        var footer=Theme.Footer(new Rectangle(0,316,460,56));Controls.Add(footer);
        var skip=new ToolButton{Text="Skip this version",Location=new Point(20,13),Size=new Size(124,30),DialogResult=DialogResult.Ignore};footer.Controls.Add(skip);
        var later=new ToolButton{Text="Later",Location=new Point(228,13),Size=new Size(88,30),DialogResult=DialogResult.Cancel};footer.Controls.Add(later);CancelButton=later;
        var download=new ToolButton{Text="Update now",Enabled=AutoUpdate.CanInstall(release),Style=ButtonStyle.Primary,Location=new Point(324,13),Size=new Size(116,30),DialogResult=DialogResult.Yes};footer.Controls.Add(download);AcceptButton=download;
        ActiveControl=download;Shown+=(s,e)=>notes.Select(0,0);
        ResumeLayout(false);
    }
}
public class HotkeySettings {
    public int Record {get;set;}
    public int Play {get;set;}
    public int Stop {get;set;}
    public HotkeySettings() { Record=(int)Keys.F8; Play=(int)Keys.F9; Stop=(int)Keys.F12; }
    public static List<Keys> Choices() {
        var keys=new List<Keys>();
        for(int i=(int)Keys.F1;i<=(int)Keys.F24;i++)keys.Add((Keys)i);
        for(int i=(int)Keys.A;i<=(int)Keys.Z;i++)keys.Add((Keys)i);
        for(int i=(int)Keys.D0;i<=(int)Keys.D9;i++)keys.Add((Keys)i);
        for(int i=(int)Keys.NumPad0;i<=(int)Keys.NumPad9;i++)keys.Add((Keys)i);
        keys.AddRange(new Keys[]{Keys.Pause,Keys.Scroll,Keys.Insert,Keys.Delete,Keys.Home,Keys.End,Keys.PageUp,Keys.PageDown,Keys.Space,Keys.Tab,Keys.Escape});
        return keys;
    }
    public static string Name(int key) { if(key>=(int)Keys.D0&&key<=(int)Keys.D9)return (key-(int)Keys.D0).ToString();return ((Keys)key).ToString(); }
    public void Validate() {
        var choices=Choices();
        if(!choices.Contains((Keys)Record)||!choices.Contains((Keys)Play)||!choices.Contains((Keys)Stop))throw new Exception("Choose a supported key for each action.");
        if(Record==Play||Record==Stop||Play==Stop)throw new Exception("Record, Play, and Stop must use different keys.");
    }
    public int ActionFor(uint key) { return key==Record?1:key==Play?2:key==Stop?3:0; }
    public static HotkeySettings Load(string path) {
        if(!File.Exists(path))return new HotkeySettings();
        var settings=Recording.Serializer().Deserialize<HotkeySettings>(File.ReadAllText(path));
        if(settings==null)throw new Exception("Empty shortcut settings.");settings.Validate();return settings;
    }
    public void Save(string path) {
        Validate();string temp=path+".tmp";
        try {File.WriteAllText(temp,Recording.Serializer().Serialize(this));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
internal enum Tone { Ready, Recording, Active, Waiting, Warning }
internal enum ButtonStyle { Secondary, Primary, Danger, Playback, Waiting }

internal static class Theme {
    public static readonly Color Canvas=Color.FromArgb(248,248,248),FooterFill=Color.FromArgb(240,240,240),Card=Color.White,Line=Color.FromArgb(224,224,224),Border=Color.FromArgb(196,196,196),
        Text=Color.FromArgb(28,28,28),Muted=Color.FromArgb(94,94,94),Disabled=Color.FromArgb(150,150,150),
        Accent=Color.FromArgb(0,95,184),AccentHover=Color.FromArgb(0,82,160),AccentDown=Color.FromArgb(0,68,135),
        Danger=Color.FromArgb(196,43,28),DangerHover=Color.FromArgb(172,36,23),DangerDown=Color.FromArgb(148,30,20),
        Ready=Color.FromArgb(16,124,16),PlaybackHover=Color.FromArgb(12,105,12),PlaybackDown=Color.FromArgb(8,85,8),
        WaitingFill=Color.FromArgb(255,224,151),WaitingHover=Color.FromArgb(255,213,118),WaitingDown=Color.FromArgb(244,196,87),Warning=Color.FromArgb(130,78,0);
    static readonly float scale=DetectScale();
    static float DetectScale() { using(var g=Graphics.FromHwnd(IntPtr.Zero))return g.DpiX/96f; }
    public static int S(int value) { return (int)Math.Round(value*scale); }
    public static Color ToneColor(Tone tone) { return tone==Tone.Recording?Danger:tone==Tone.Active?Ready:(tone==Tone.Warning||tone==Tone.Waiting)?Warning:Ready; }
    public static GraphicsPath Round(Rectangle r,int radius) {
        var path=new GraphicsPath();int d=Math.Max(1,radius*2);
        path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    public const TextFormatFlags Line1=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis;
    public static Size KeySize(string text,Font font) {
        var size=TextRenderer.MeasureText(text,font,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);
        return new Size(size.Width+S(12),S(18));
    }
    public static void DrawKey(Graphics g,Rectangle r,string text,Font font,Color fore,Color border,Color fill) {
        using(var path=Round(r,S(4))) {
            using(var brush=new SolidBrush(fill))g.FillPath(brush,path);
            using(var pen=new Pen(border))g.DrawPath(pen,path);
        }
        TextRenderer.DrawText(g,text,font,r,fore,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    }
    public static Panel Footer(Rectangle bounds) {
        var panel=new Panel{Bounds=bounds,BackColor=FooterFill};
        panel.Paint+=(s,e)=>{using(var pen=new Pen(Line))e.Graphics.DrawLine(pen,0,0,panel.Width,0);};
        return panel;
    }
}

// Flat, owner-drawn button. Optionally shows the keyboard shortcut as a key cap on the right.
internal class ToolButton : Button {
    bool hot,down;
    string hint="";
    ButtonStyle style=ButtonStyle.Secondary;
    readonly Font hintFont=new Font("Segoe UI",8.5f);
    public string Hint { get {return hint;} set {value=value??"";if(hint!=value){hint=value;Invalidate();}} }
    public ButtonStyle Style { get {return style;} set {if(style!=value){style=value;Invalidate();}} }
    public ToolButton() {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;
    }
    protected override void Dispose(bool disposing) { if(disposing)hintFont.Dispose();base.Dispose(disposing); }
    protected override void OnMouseEnter(EventArgs e) { hot=true;Invalidate();base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot=false;Invalidate();base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if(e.Button==MouseButtons.Left){down=true;Invalidate();}base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down=false;Invalidate();base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { hot=down=false;Invalidate();base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate();base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate();base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(Parent==null?Theme.Canvas:Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        var r=new Rectangle(0,0,Width-1,Height-1);
        Color fill,border,fore,keyFill,keyBorder,keyFore;
        if(!Enabled) {fill=Color.FromArgb(246,246,246);border=Color.FromArgb(226,226,226);fore=Theme.Disabled;keyFill=Color.Transparent;keyBorder=Color.FromArgb(214,214,214);keyFore=Theme.Disabled;}
        else if(style!=ButtonStyle.Secondary) {
            bool danger=style==ButtonStyle.Danger,playback=style==ButtonStyle.Playback,waiting=style==ButtonStyle.Waiting;
            Color normal=danger?Theme.Danger:playback?Theme.Ready:waiting?Theme.WaitingFill:Theme.Accent;
            Color hover=danger?Theme.DangerHover:playback?Theme.PlaybackHover:waiting?Theme.WaitingHover:Theme.AccentHover;
            Color pressed=danger?Theme.DangerDown:playback?Theme.PlaybackDown:waiting?Theme.WaitingDown:Theme.AccentDown;
            fill=down?pressed:hot?hover:normal;
            border=waiting?Theme.Warning:fill;fore=waiting?Theme.Text:Color.White;keyFill=Color.FromArgb(38,255,255,255);keyBorder=waiting?Theme.Warning:Color.FromArgb(120,255,255,255);keyFore=fore;
        } else {
            fill=down?Color.FromArgb(232,232,232):hot?Color.FromArgb(244,244,244):Color.White;
            border=hot||down?Color.FromArgb(166,166,166):Theme.Border;fore=Theme.Text;keyFill=Color.FromArgb(247,247,247);keyBorder=Color.FromArgb(208,208,208);keyFore=Theme.Muted;
        }
        using(var path=Theme.Round(r,Theme.S(4))) {
            using(var brush=new SolidBrush(fill))g.FillPath(brush,path);
            using(var pen=new Pen(border))g.DrawPath(pen,path);
        }
        int pad=Theme.S(10);
        if(hint.Length>0) {
            var size=Theme.KeySize(hint,hintFont);
            var cap=new Rectangle(Width-Theme.S(8)-size.Width,(Height-size.Height)/2,size.Width-1,size.Height-1);
            Theme.DrawKey(g,cap,hint,hintFont,keyFore,keyBorder,keyFill);
            TextRenderer.DrawText(g,Text,Font,new Rectangle(pad,0,cap.X-pad-Theme.S(4),Height),fore,Theme.Line1);
        } else TextRenderer.DrawText(g,Text,Font,new Rectangle(Theme.S(6),0,Width-Theme.S(12),Height),fore,Theme.Line1|TextFormatFlags.HorizontalCenter);
        if(Focused&&ShowFocusCues) {
            using(var path=Theme.Round(Rectangle.Inflate(r,-Theme.S(3),-Theme.S(3)),Theme.S(2)))using(var pen=new Pen(style==ButtonStyle.Secondary?Theme.Accent:Color.White))g.DrawPath(pen,path);
        }
    }
}

// Single status line: colored state dot, bold headline, then supporting detail in muted text.
internal class StatusLine : Control {
    string headline="",info="";
    Tone tone=Tone.Ready;
    readonly Font headlineFont=new Font("Segoe UI Semibold",9.5f);
    public string Headline { get {return headline;} set {value=value??"";if(headline!=value){headline=value;Invalidate();}} }
    public string Info { get {return info;} set {value=value??"";if(info!=value){info=value;Invalidate();}} }
    public Tone Tone { get {return tone;} set {if(tone!=value){tone=value;Invalidate();}} }
    public StatusLine() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);TabStop=false; }
    protected override void Dispose(bool disposing) { if(disposing)headlineFont.Dispose();base.Dispose(disposing); }
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;
        Color background=tone==Tone.Active?Color.FromArgb(226,243,229):tone==Tone.Waiting?Color.FromArgb(255,242,209):tone==Tone.Recording?Color.FromArgb(253,232,230):(Parent==null?Theme.Canvas:Parent.BackColor);
        g.Clear(background);g.SmoothingMode=SmoothingMode.AntiAlias;
        int dot=Theme.S(8);
        using(var brush=new SolidBrush(Theme.ToneColor(tone)))g.FillEllipse(brush,Theme.S(2),(Height-dot)/2,dot,dot);
        int x=dot+Theme.S(10);
        int headWidth=TextRenderer.MeasureText(headline,headlineFont,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine).Width+Theme.S(2);
        TextRenderer.DrawText(g,headline,headlineFont,new Rectangle(x,0,Math.Min(headWidth,Width-x),Height),tone==Tone.Ready?Theme.Text:Theme.ToneColor(tone),Theme.Line1);
        int infoX=x+headWidth+Theme.S(10);
        if(info.Length>0&&infoX<Width)TextRenderer.DrawText(g,info,Font,new Rectangle(infoX,0,Width-infoX,Height),tone==Tone.Warning?Theme.Warning:Theme.Muted,Theme.Line1);
    }
}

// Flat colors for the popup menu (the stock renderer uses dated gradients).
internal class FlatColors : ProfessionalColorTable {
    public override Color ToolStripDropDownBackground { get {return Color.White;} }
    public override Color ImageMarginGradientBegin { get {return Color.White;} }
    public override Color ImageMarginGradientMiddle { get {return Color.White;} }
    public override Color ImageMarginGradientEnd { get {return Color.White;} }
    public override Color MenuBorder { get {return Theme.Border;} }
    public override Color MenuItemBorder { get {return Color.FromArgb(229,229,229);} }
    public override Color MenuItemSelected { get {return Color.FromArgb(240,240,240);} }
    public override Color MenuItemSelectedGradientBegin { get {return Color.FromArgb(240,240,240);} }
    public override Color MenuItemSelectedGradientEnd { get {return Color.FromArgb(240,240,240);} }
    public override Color SeparatorDark { get {return Theme.Line;} }
    public override Color SeparatorLight { get {return Color.White;} }
    public override Color CheckBackground { get {return Color.FromArgb(229,241,251);} }
    public override Color CheckSelectedBackground { get {return Color.FromArgb(229,241,251);} }
    public override Color CheckPressedBackground { get {return Color.FromArgb(229,241,251);} }
}

internal class KeyCap : Control {
    readonly Font keyFont=new Font("Segoe UI",8.5f);
    public KeyCap(string text) { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Text=text;TabStop=false; }
    protected override void Dispose(bool disposing) { if(disposing)keyFont.Dispose();base.Dispose(disposing); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Parent==null?Theme.Canvas:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Theme.DrawKey(e.Graphics,new Rectangle(0,0,Width-1,Height-1),Text,keyFont,Theme.Muted,Color.FromArgb(208,208,208),Color.White);
    }
}

internal class HotkeyDialog : Form {
    ComboBox record,play,stop;
    public HotkeySettings Selection {get;private set;}
    public HotkeyDialog(HotkeySettings current) {
        SuspendLayout();
        AutoScaleDimensions=new SizeF(96F,96F);AutoScaleMode=AutoScaleMode.Dpi;
        Text="Customize keys";ClientSize=new Size(440,270);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;ShowIcon=false;
        StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",9);BackColor=Theme.Canvas;ForeColor=Theme.Text;
        Controls.Add(new Label{Text="Choose one key for each action. Your choice replaces its default shortcut. Use Restore defaults to reset all three.",Location=new Point(20,18),Size=new Size(400,34)});
        Controls.Add(new Label{Text="Action",Location=new Point(20,64),AutoSize=true,ForeColor=Theme.Muted});
        Controls.Add(new Label{Text="Default",Location=new Point(220,64),AutoSize=true,ForeColor=Theme.Muted});
        Controls.Add(new Label{Text="Active key",Location=new Point(300,64),AutoSize=true,ForeColor=Theme.Muted});
        Controls.Add(new Panel{BackColor=Theme.Line,Location=new Point(20,86),Size=new Size(400,1)});
        record=Row("Record / finish",98,current.Record,Keys.F8);play=Row("Play / stop playback",134,current.Play,Keys.F9);stop=Row("Emergency stop",170,current.Stop,Keys.F12);
        var footer=Theme.Footer(new Rectangle(0,212,440,58));Controls.Add(footer);
        var reset=new ToolButton{Text="Restore defaults",Location=new Point(20,14),Size=new Size(136,30)};reset.Click+=(s,e)=>{record.SelectedIndex=0;play.SelectedIndex=0;stop.SelectedIndex=0;};footer.Controls.Add(reset);
        var cancel=new ToolButton{Text="Cancel",DialogResult=DialogResult.Cancel,Location=new Point(236,14),Size=new Size(88,30)};footer.Controls.Add(cancel);CancelButton=cancel;
        var save=new ToolButton{Text="Save",Style=ButtonStyle.Primary,Location=new Point(332,14),Size=new Size(88,30)};footer.Controls.Add(save);AcceptButton=save;
        save.Click+=(s,e)=> {var selected=new HotkeySettings{Record=(int)(Keys)record.SelectedItem,Play=(int)(Keys)play.SelectedItem,Stop=(int)(Keys)stop.SelectedItem};try{selected.Validate();Selection=selected;DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(this,ex.Message,"Choose different keys",MessageBoxButtons.OK,MessageBoxIcon.Information);}};
        ResumeLayout(false);
    }
    // Show the factory default for reference; only the selected key is active.
    ComboBox Row(string text,int y,int key,Keys fallback) {
        Controls.Add(new Label{Text=text,Location=new Point(20,y+4),AutoSize=true});
        Controls.Add(new KeyCap(HotkeySettings.Name((int)fallback)){Location=new Point(220,y+1),Size=new Size(48,22)});
        var box=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(300,y),Width=120,FormattingEnabled=true,MaxDropDownItems=12};
        box.Items.Add(fallback);
        foreach(var item in HotkeySettings.Choices())if(item!=fallback)box.Items.Add(item);
        box.Format+=(s,e)=>e.Value=HotkeySettings.Name((int)(Keys)e.ListItem);
        box.SelectedItem=(Keys)key;Controls.Add(box);return box;
    }
}

internal class AboutDialog : Form {
    public AboutDialog(Icon appIcon) {
        SuspendLayout();
        AutoScaleDimensions=new SizeF(96F,96F);AutoScaleMode=AutoScaleMode.Dpi;
        Text="About myTaskTiny";ClientSize=new Size(340,164);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;ShowIcon=false;
        StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",9);BackColor=Theme.Canvas;ForeColor=Theme.Text;
        if(appIcon!=null)try{Controls.Add(new PictureBox{Image=new Icon(appIcon,new Size(48,48)).ToBitmap(),SizeMode=PictureBoxSizeMode.Zoom,Location=new Point(20,22),Size=new Size(48,48)});}catch{}
        Controls.Add(new Label{Text="myTaskTiny",Font=new Font("Segoe UI Semibold",12f),AutoSize=true,Location=new Point(84,20)});
        Controls.Add(new Label{Text="Version "+AppVersion.Current,ForeColor=Theme.Muted,AutoSize=true,Location=new Point(85,47)});
        Controls.Add(new Label{Text="Developed by Craby",AutoSize=true,Location=new Point(85,70)});
        var footer=Theme.Footer(new Rectangle(0,108,340,56));Controls.Add(footer);
        var ok=new ToolButton{Text="OK",Style=ButtonStyle.Primary,DialogResult=DialogResult.OK,Location=new Point(232,13),Size=new Size(88,30)};footer.Controls.Add(ok);AcceptButton=ok;CancelButton=ok;
        ResumeLayout(false);
    }
}

public class MacroEvent {
    public long Time {get;set;}
    public int Message {get;set;}
    public int X {get;set;}
    public int Y {get;set;}
    public uint Data {get;set;}
    public uint Scan {get;set;}
    public uint Flags {get;set;}
}
public class Recording {
    public const int MaximumJsonLength=64000000;
    public int Version {get;set;}
    public long Duration {get;set;}
    public List<MacroEvent> Events {get;set;}
    public Recording() { Version=1; Events=new List<MacroEvent>(); }
    public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength=MaximumJsonLength }; }
    public void Validate() {
        if(Version!=1 || Events==null || Events.Count>500000 || Duration<0 || Duration>86400000) throw new Exception("Unsupported or oversized recording.");
        long previous=0;
        foreach(var e in Events) {
            if(e==null || e.Time<previous || e.Time>Duration) throw new Exception("Invalid event timing.");
            previous=e.Time;
            if(e.Message==0x100 || e.Message==0x101 || e.Message==0x104 || e.Message==0x105) {
                if(e.Data>255 || e.Scan>65535) throw new Exception("Invalid key.");
            } else if(e.Message!=0x200 && e.Message!=0x201 && e.Message!=0x202 && e.Message!=0x204 && e.Message!=0x205 && e.Message!=0x207 && e.Message!=0x208 && e.Message!=0x20A && e.Message!=0x20B && e.Message!=0x20C && e.Message!=0x20E) throw new Exception("Unknown input event.");
        }
    }
}
internal static class Native {
    public delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseHook { public Point Point; public uint Data,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyHook { public uint Vk,Scan,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseInput { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyInput { public ushort Vk,Scan; public uint Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] public struct Union { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] public struct Input { public uint Type; public Union Value; }
    [DllImport("user32.dll",SetLastError=true)] public static extern IntPtr SetWindowsHookEx(int id, Hook proc, IntPtr module,uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)] public static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll",SetLastError=true)] public static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
    public static Input Convert(MacroEvent e) {
        Input i=new Input();
        if(e.Message<0x200) {
            i.Type=1;
            i.Value.Key.Vk=(ushort)(e.Scan==0 ? e.Data : 0);
            i.Value.Key.Scan=(ushort)e.Scan;
            i.Value.Key.Flags=(e.Scan==0 ? 0u : 8u) | (e.Flags&1) | ((e.Message==0x101 || e.Message==0x105)?2u:0u);
        } else {
            Rectangle r=SystemInformation.VirtualScreen;
            i.Value.Mouse.X=(int)Math.Max(0,Math.Min(65535,((long)e.X-r.Left)*65535/Math.Max(1,r.Width-1)));
            i.Value.Mouse.Y=(int)Math.Max(0,Math.Min(65535,((long)e.Y-r.Top)*65535/Math.Max(1,r.Height-1)));
            uint f=0;
            switch(e.Message) { case 0x201:f=2;break;case 0x202:f=4;break;case 0x204:f=8;break;case 0x205:f=16;break;case 0x207:f=32;break;case 0x208:f=64;break;case 0x20B:f=128;break;case 0x20C:f=256;break;case 0x20A:f=0x800;break;case 0x20E:f=0x1000;break; }
            i.Value.Mouse.Flags=0xC001|f;
            i.Value.Mouse.Data=e.Data;
        }
        return i;
    }
    public static void Send(MacroEvent e) {
        if(SendInput(1,new Input[]{Convert(e)},Marshal.SizeOf(typeof(Input)))!=1) throw new Exception("Windows blocked playback. Run at the same permission level as the target app.");
    }
}
internal class RecordingLibrary {
    readonly string indexPath,folder;
    readonly List<string> recent=new List<string>();
    public RecordingLibrary(string indexPath,string folder) {
        this.indexPath=indexPath;this.folder=folder;
        if(File.Exists(indexPath))try {var items=Recording.Serializer().Deserialize<List<string>>(File.ReadAllText(indexPath));if(items!=null)foreach(var item in items){if(string.IsNullOrWhiteSpace(item))continue;try{string path=Path.GetFullPath(item);if(!recent.Exists(x=>string.Equals(x,path,StringComparison.OrdinalIgnoreCase)))recent.Add(path);}catch{}if(recent.Count>=50)break;}}catch{}
    }
    public void Remember(string path) {
        path=Path.GetFullPath(path);recent.RemoveAll(x=>string.Equals(x,path,StringComparison.OrdinalIgnoreCase));recent.Insert(0,path);if(recent.Count>50)recent.RemoveRange(50,recent.Count-50);
        string temp=indexPath+".tmp";
        try{File.WriteAllText(temp,Recording.Serializer().Serialize(recent));if(File.Exists(indexPath))File.Replace(temp,indexPath,null);else File.Move(temp,indexPath);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public List<string> Available() {
        var result=new List<string>();
        foreach(var path in recent)if(File.Exists(path))result.Add(path);
        foreach(var directory in new string[]{folder,Path.Combine(folder,"Recordings")}) {
            if(!Directory.Exists(directory))continue;
            try{var files=Directory.GetFiles(directory,"*.mtt");Array.Sort(files,StringComparer.OrdinalIgnoreCase);foreach(var path in files)if(!result.Exists(x=>string.Equals(x,path,StringComparison.OrdinalIgnoreCase)))result.Add(path);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return result;
    }
}

public class RemovalFile {
    public string Path {get;set;}
    public string Hash {get;set;}
    public override string ToString() {return Path;}
}
public class RemovalPlan {
    public List<RemovalFile> Files {get;set;}
    public List<string> EmptyDirectories {get;set;}
    public RemovalPlan(){Files=new List<RemovalFile>();EmptyDirectories=new List<string>();}
}
internal class CreatedFiles {
    readonly string index;
    public CreatedFiles(string executable){index=System.IO.Path.ChangeExtension(executable,"created-files.json");}
    public List<string> Read() {
        if(!File.Exists(index))return new List<string>();
        try {return Recording.Serializer().Deserialize<List<string>>(File.ReadAllText(index))??new List<string>();}
        catch {throw new IOException("The created-files list could not be read. Your files have not been changed.");}
    }
    public void Remember(string path) {
        var paths=Read();path=System.IO.Path.GetFullPath(path);
        if(!paths.Exists(p=>string.Equals(p,path,StringComparison.OrdinalIgnoreCase)))paths.Add(path);
        string temp=index+".tmp";
        try{File.WriteAllText(temp,Recording.Serializer().Serialize(paths));if(File.Exists(index))File.Replace(temp,index,null);else File.Move(temp,index);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
internal static class UninstallService {
    public static string Hash(string path) {using(var stream=File.OpenRead(path))using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","");}
    public static void Add(List<RemovalFile> files,string path) {
        if(string.IsNullOrWhiteSpace(path))return;path=Path.GetFullPath(path);
        if(!File.Exists(path)||files.Exists(f=>string.Equals(f.Path,path,StringComparison.OrdinalIgnoreCase)))return;
        files.Add(new RemovalFile{Path=path,Hash=Hash(path)});
    }
    public static List<RemovalFile> CoreFiles(string exe,string updateSettings) {
        var files=new List<RemovalFile>();Add(files,exe);
        foreach(var suffix in new string[]{"settings.json","recordings.json","created-files.json"})Add(files,Path.ChangeExtension(exe,suffix));
        Add(files,updateSettings);
        if(string.Equals(Path.GetFileNameWithoutExtension(exe),AppIdentity.Name,StringComparison.OrdinalIgnoreCase)) {
            string oldExe=Path.Combine(Path.GetDirectoryName(exe),AppIdentity.LegacyName+".exe");
            foreach(var suffix in new string[]{"settings.json","recordings.json","created-files.json"})Add(files,Path.ChangeExtension(oldExe,suffix));
            if(updateSettings!=null)Add(files,Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(updateSettings)),AppIdentity.LegacyName,"updates.json"));
        }
        return files;
    }
    public static List<RemovalFile> DataFiles(string exe,IEnumerable<string> candidates) {
        var files=new List<RemovalFile>();var queue=new Queue<string>(candidates);var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while(queue.Count>0) {
            var path=queue.Dequeue();if(string.IsNullOrWhiteSpace(path))continue;
            string full;try{full=Path.GetFullPath(path);}catch{continue;}
            if(string.Equals(full,exe,StringComparison.OrdinalIgnoreCase)||!visited.Add(full))continue;
            var ext=Path.GetExtension(full);
            if(ext.Equals(".mtt",StringComparison.OrdinalIgnoreCase))Add(files,full);
            else if(ext.Equals(".exe",StringComparison.OrdinalIgnoreCase)) {
                Add(files,full);
                foreach(var suffix in new string[]{"settings.json","recordings.json","created-files.json"})Add(files,Path.ChangeExtension(full,suffix));
                // Include outputs created by tracked exports; cycles are deduplicated by path.
                foreach(var output in new CreatedFiles(full).Read())queue.Enqueue(output);
            }
        }
        return files;
    }
    public static Process Start(RemovalPlan plan,int parent,bool quiet) {
        string basePath=Path.Combine(Path.GetTempPath(),"myTaskTiny-remove-"+Guid.NewGuid().ToString("N"));
        string script=basePath+".ps1",json=basePath+".json";
        try {
            File.WriteAllText(script,WorkerScript,System.Text.Encoding.UTF8);File.WriteAllText(json,Recording.Serializer().Serialize(plan),System.Text.Encoding.UTF8);
            string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
            return Process.Start(new ProcessStartInfo(powershell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\" -PlanPath \""+json+"\" -ParentId "+parent+(quiet?" -Quiet":"")){UseShellExecute=false,CreateNoWindow=true});
        }catch{if(File.Exists(script))File.Delete(script);if(File.Exists(json))File.Delete(json);throw;}
    }
    public static void Test() {
        string root=Path.Combine(Path.GetTempPath(),"myTaskTiny-removal-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            string exe=Path.Combine(root,"myTaskTiny.exe"),recording=Path.Combine(root,"owned.mtt"),unrelated=Path.Combine(root,"keep.txt"),changed=Path.Combine(root,"changed.mtt"),export=Path.Combine(root,"macro.exe");
            foreach(var path in new string[]{exe,recording,unrelated,changed,export})File.WriteAllText(path,"fixture");
            var tracker=new CreatedFiles(exe);tracker.Remember(recording);tracker.Remember(recording);tracker.Remember(export);
            if(new CreatedFiles(exe).Read().Count!=2)throw new Exception("Created-file tracking failed.");
            var core=CoreFiles(exe,null);if(core.Exists(f=>f.Path==recording))throw new Exception("Recordings removed by default.");
            var data=DataFiles(exe,new string[]{recording,recording,unrelated,exe,export});if(data.Count!=2)throw new Exception("Removal file filtering failed.");
            using(var dialog=new UninstallDialog(core,data)) {
                if(!dialog.DataUnchecked||dialog.Plan!=null)throw new Exception("Uninstall default/cancel state failed.");
                dialog.Text="Uninstall layout test";dialog.Show();Application.DoEvents();
                using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"uninstall-preview.png"));}
                dialog.Close();if(dialog.Plan!=null)throw new Exception("Closing uninstall selected a deletion plan.");
            }
            string exportSettings=Path.ChangeExtension(export,"settings.json");File.WriteAllText(exportSettings,"{}");
            new CreatedFiles(export).Remember(changed);new CreatedFiles(export).Remember(export);
            var nested=DataFiles(exe,new string[]{export});if(!nested.Exists(f=>f.Path==changed)||!nested.Exists(f=>f.Path==exportSettings)||nested.Count!=4)throw new Exception("Export output/sidecar discovery failed.");
            var plan=new RemovalPlan();plan.Files=core;Add(plan.Files,recording);Add(plan.Files,changed);File.WriteAllText(changed,"modified after review");plan.EmptyDirectories.Add(root);
            using(var process=Start(plan,-1,true)){if(!process.WaitForExit(15000)||process.ExitCode!=1)throw new Exception("Changed-file protection failed.");}
            if(File.Exists(exe)||File.Exists(recording)||!File.Exists(unrelated)||!File.Exists(export)||!File.Exists(changed)||!Directory.Exists(root))throw new Exception("Uninstall selected-file scope failed.");
            var clean=new RemovalPlan();Add(clean.Files,export);using(var process=Start(clean,-1,true)){if(!process.WaitForExit(15000)||process.ExitCode!=0)throw new Exception("Removal worker failed.");}
            if(File.Exists(export))throw new Exception("Selected export not removed.");
            string waitingFile=Path.Combine(root,"wait-for-exit.mtt");File.WriteAllText(waitingFile,"fixture");
            string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
            using(var parent=Process.Start(new ProcessStartInfo(powershell,"-NoProfile -NonInteractive -Command Start-Sleep -Seconds 3"){UseShellExecute=false,CreateNoWindow=true})) {
                var waiting=new RemovalPlan();Add(waiting.Files,waitingFile);
                using(var worker=Start(waiting,parent.Id,true)) {
                    System.Threading.Thread.Sleep(300);
                    if(!parent.HasExited&&!File.Exists(waitingFile))throw new Exception("Uninstall deleted before parent exit.");
                    if(!worker.WaitForExit(15000)||worker.ExitCode!=0||File.Exists(waitingFile))throw new Exception("Wait-for-exit deletion failed.");
                }
            }
        }finally{Directory.Delete(root,true);}
    }
    public const string WorkerScript=@"param([Parameter(Mandatory=$true)][string]$PlanPath,[int]$ParentId,[switch]$Quiet)
$ErrorActionPreference = 'Stop'
$failures = New-Object 'System.Collections.Generic.List[string]'
try {
    $plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
    $parent = $null
    try { $parent = [Diagnostics.Process]::GetProcessById($ParentId) } catch [ArgumentException] {}
    if ($parent -and -not $parent.WaitForExit(30000)) { throw 'The app did not close. No files were removed.' }
    foreach ($file in $plan.Files) {
        try {
            if (-not (Test-Path -LiteralPath $file.Path -PathType Leaf)) { continue }
            $hash = (Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash
            if ($hash -ne $file.Hash) { throw 'File changed after confirmation; left in place.' }
            Remove-Item -LiteralPath $file.Path -Force
        } catch { $failures.Add($file.Path + ': ' + $_.Exception.Message) }
    }
    foreach ($directory in $plan.EmptyDirectories) {
        try { if ([IO.Directory]::Exists($directory) -and [IO.Directory]::GetFileSystemEntries($directory).Length -eq 0) { [IO.Directory]::Delete($directory,$false) } } catch {}
    }
} catch { $failures.Add($_.Exception.Message) }
finally {
    Remove-Item -LiteralPath $PlanPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
}
if (-not $Quiet) {
    Add-Type -AssemblyName System.Windows.Forms
    $message = if ($failures.Count -eq 0) { 'myTaskTiny was removed. Files you chose to keep were left in place.' } else { ""Some files could not be removed:`r`n`r`n"" + ($failures -join ""`r`n"") }
    [void][Windows.Forms.MessageBox]::Show($message,'myTaskTiny uninstall')
}
if ($failures.Count -gt 0) { if ($Quiet) { $failures | Write-Output }; exit 1 }
";
}
internal class UninstallDialog : Form {
    readonly List<RemovalFile> core;
    readonly CheckedListBox data;
    public RemovalPlan Plan {get;private set;}
    internal bool DataUnchecked {get{return data.CheckedItems.Count==0;}}
    public UninstallDialog(List<RemovalFile> appFiles,List<RemovalFile> dataFiles) {
        core=appFiles;SuspendLayout();AutoScaleDimensions=new SizeF(96F,96F);AutoScaleMode=AutoScaleMode.Dpi;Text="Uninstall myTaskTiny";ClientSize=new Size(560,400);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;ShowIcon=false;Font=new Font("Segoe UI",9);BackColor=Theme.Canvas;ForeColor=Theme.Text;
        Controls.Add(new Label{Text="The app and these settings files will be permanently removed:",Location=new Point(20,16),AutoSize=true});
        var appList=new ListBox{Location=new Point(20,40),Size=new Size(520,68),HorizontalScrollbar=true,BorderStyle=BorderStyle.FixedSingle};foreach(var file in core)appList.Items.Add(file.Path);Controls.Add(appList);
        Controls.Add(new Label{Text="Optional: select recordings or exported macros to delete. Unchecked files are kept.",Location=new Point(20,118),AutoSize=true});
        data=new CheckedListBox{Location=new Point(20,140),Size=new Size(520,122),HorizontalScrollbar=true,CheckOnClick=true,BorderStyle=BorderStyle.FixedSingle};foreach(var file in dataFiles)data.Items.Add(file,false);Controls.Add(data);
        var all=new CheckBox{Text="Select all listed recordings and exports",Location=new Point(20,273),AutoSize=true};all.CheckedChanged+=(s,e)=>{for(int i=0;i<data.Items.Count;i++)data.SetItemChecked(i,all.Checked);};Controls.Add(all);
        var browse=new ToolButton{Text="Add files…",Location=new Point(428,268),Size=new Size(112,28)};browse.Click+=(s,e)=>{using(var dialog=new OpenFileDialog{Filter="Recordings and exports (*.mtt;*.exe)|*.mtt;*.exe",Multiselect=true}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;try{foreach(var file in UninstallService.DataFiles(Assembly.GetExecutingAssembly().Location,dialog.FileNames)){bool exists=false;foreach(RemovalFile item in data.Items)if(string.Equals(item.Path,file.Path,StringComparison.OrdinalIgnoreCase))exists=true;if(!exists)data.Items.Add(file,true);}}catch(Exception ex){MessageBox.Show(this,ex.Message,"Cannot add file");}}};Controls.Add(browse);
        Controls.Add(new Label{Text="Older exports or moved files may not be listed. Add them manually if needed.\nProject source, unrelated files, and nonempty folders are kept. This cannot be undone.",Location=new Point(20,304),Size=new Size(520,34),ForeColor=Theme.Muted});
        var footer=Theme.Footer(new Rectangle(0,344,560,56));Controls.Add(footer);
        var cancel=new ToolButton{Text="Cancel",DialogResult=DialogResult.Cancel,Location=new Point(320,13),Size=new Size(100,30)};footer.Controls.Add(cancel);CancelButton=cancel;
        var remove=new ToolButton{Text="Uninstall",Style=ButtonStyle.Danger,Location=new Point(428,13),Size=new Size(112,30)};footer.Controls.Add(remove);
        remove.Click+=(s,e)=>{
            var plan=new RemovalPlan();plan.Files.AddRange(core);
            foreach(RemovalFile item in data.CheckedItems)if(!plan.Files.Exists(f=>string.Equals(f.Path,item.Path,StringComparison.OrdinalIgnoreCase)))plan.Files.Add(item);
            int selectedCount=plan.Files.Count;
            if(MessageBox.Show(this,"Permanently delete "+selectedCount+" selected files and close myTaskTiny?","Confirm uninstall",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            Plan=plan;DialogResult=DialogResult.OK;
        };
        ResumeLayout(false);
    }
}

public class MainForm : Form {
    Recording macro=new Recording();
    bool recording,playing,pending,dirty,editingHotkeys;
    HotkeySettings hotkeys=new HotkeySettings();
    HashSet<uint> shortcutHeld=new HashSet<uint>();
    string settingsPath=Path.ChangeExtension(Assembly.GetExecutingAssembly().Location,"settings.json");
    bool updateChecking;
    ReleaseInfo availableUpdate;
    UpdatePreferences updatePreferences;
    ToolStripMenuItem updateItem,automaticUpdatesItem;
    readonly string updatePreferencesPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"myTaskTiny","updates.json");
    CreatedFiles createdFiles;
    bool exitApproved;
    ToolStripMenuItem uninstallItem;
    RecordingLibrary library;
    string selectedPath;
    int position;
    long iteration;
    long lastMove=-10;
    double playbackSpeed=1;
    int repeatCount=1;
    bool forever,waitingForNext;
    double runInterval;
    string fileName="Untitled";
    Stopwatch watch=new Stopwatch();
    Timer timer=new Timer();
    Native.Hook keyProc,mouseProc;
    IntPtr keyHook,mouseHook;
    Dictionary<string,MacroEvent> held=new Dictionary<string,MacroEvent>();
    ToolButton record,play,stop,menuButton;
    ToolStripMenuItem openItem,saveItem,exportItem,keysItem,topItem,savedItem;
    ContextMenuStrip menu;
    ToolTip tips=new ToolTip();
    StatusLine card;
    Label completedLoops;
    NumericUpDown repeats;
    ComboBox speed;
    CheckBox loop;
    RadioButton speedMode,intervalEnabled;
    NumericUpDown intervalValue;
    ComboBox intervalUnit;
    internal bool ReadyForUpdate {get{return !IsDisposed&&Visible&&keyHook!=IntPtr.Zero&&mouseHook!=IntPtr.Zero;}}
    public MainForm() : this(false) {}
    internal MainForm(bool testMode) {
        createdFiles=new CreatedFiles(Assembly.GetExecutingAssembly().Location);
        string settingsWarning=null;
        if(!testMode&&Assembly.GetExecutingAssembly().GetManifestResourceInfo("macro.mtt")==null)try{AppIdentity.ImportSettings(Assembly.GetExecutingAssembly().Location,Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));}catch{settingsWarning="Some previous settings could not be imported.";}
        library=new RecordingLibrary(Path.ChangeExtension(Assembly.GetExecutingAssembly().Location,"recordings.json"),AppDomain.CurrentDomain.BaseDirectory);
        if(!testMode)try{hotkeys=HotkeySettings.Load(settingsPath);}catch{settingsWarning="Shortcut settings could not be read. Default keys are active.";}
        SuspendLayout();
        AutoScaleDimensions=new SizeF(96F,96F);AutoScaleMode=AutoScaleMode.Dpi;
        Text="myTaskTiny v"+AppVersion.Current; ClientSize=new Size(400,128); FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false;
        StartPosition=FormStartPosition.CenterScreen; Font=new Font("Segoe UI",9); BackColor=Theme.Canvas; ForeColor=Theme.Text;
        using(var embeddedIcon=Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))if(embeddedIcon!=null)try{Icon=new Icon(embeddedIcon);}catch{}
        var transport=new Font("Segoe UI Semibold",9.5f);
        record=ToolAt("Record",10,8,104,32,()=>ToggleRecord(),ButtonStyle.Primary,transport);
        play=ToolAt("Play",120,8,104,32,()=>StartPlayback(),ButtonStyle.Secondary,transport);
        stop=ToolAt("Stop",230,8,104,32,()=>Stop(),ButtonStyle.Secondary,transport);
        menuButton=ToolAt("Menu",340,8,50,32,()=>ToggleMenu(),ButtonStyle.Secondary,Font);
        menu=new ContextMenuStrip{Font=Font,Renderer=new ToolStripProfessionalRenderer(new FlatColors()){RoundedEdges=false},ShowImageMargin=false,ShowCheckMargin=true};
        // Do not let outside-click dismissal close and immediately reopen the menu
        // before the Menu button's Click handler gets the same mouse gesture.
        menu.Closing+=(s,e)=>{if(e.CloseReason==ToolStripDropDownCloseReason.AppClicked && menuButton.RectangleToScreen(menuButton.ClientRectangle).Contains(Cursor.Position))e.Cancel=true;};
        openItem=MenuItem("Open…",()=>OpenMacro());saveItem=MenuItem("Save…",()=>SaveMacro());exportItem=MenuItem("Export EXE…",()=>ExportExe());
        savedItem=new ToolStripMenuItem("Saved recordings");menu.Items.Insert(0,savedItem);
        var savedMenu=(ToolStripDropDownMenu)savedItem.DropDown;savedMenu.Font=Font;savedMenu.Renderer=menu.Renderer;savedMenu.ShowImageMargin=false;savedMenu.ShowCheckMargin=true;
        savedItem.DropDownOpening+=(s,e)=>RefreshSavedMenu();
        RefreshSavedMenu();
        menu.Items.Add(new ToolStripSeparator());
        topItem=new ToolStripMenuItem("Always on top"){CheckOnClick=true};topItem.CheckedChanged+=(s,e)=>TopMost=topItem.Checked;menu.Items.Add(topItem);
        // Exported macros are standalone tasks, not installations to replace with the recorder.
        bool recorderBuild=!testMode && Assembly.GetExecutingAssembly().GetManifestResourceInfo("macro.mtt")==null;
        if(recorderBuild) {
            updatePreferences=UpdatePreferences.Load(updatePreferencesPath);
            automaticUpdatesItem=new ToolStripMenuItem("Check for updates on startup"){CheckOnClick=true,Checked=updatePreferences.CheckOnStartup};menu.Items.Add(automaticUpdatesItem);
            automaticUpdatesItem.CheckedChanged+=(s,e)=>Guard(()=>{updatePreferences.CheckOnStartup=automaticUpdatesItem.Checked;if(!automaticUpdatesItem.Checked)availableUpdate=null;updatePreferences.Save(updatePreferencesPath);});
            Shown+=(s,e)=>{if(updatePreferences.CheckOnStartup)CheckUpdates(false);};
        }
        keysItem=MenuItem("Customize keys…",()=>CustomizeKeys());
        menu.Items.Add(new ToolStripSeparator());
        if(recorderBuild)updateItem=MenuItem("Check for updates…",()=>CheckUpdates(true));
        MenuItem("About myTaskTiny…",()=>ShowAbout());
        menu.Items.Add(new ToolStripSeparator());
        uninstallItem=MenuItem("Uninstall…",()=>Uninstall());
        card=new StatusLine{Location=new Point(10,44),Size=new Size(380,20),TabStop=false};Controls.Add(card);
        speedMode=new RadioButton{Text="Speed",AutoSize=true,Checked=true,Location=new Point(10,71)};Controls.Add(speedMode);
        speed=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(96,69),Width=62}; speed.Items.AddRange(new object[]{"0.25x","0.5x","1x","2x","4x","8x"});speed.SelectedIndex=2;Controls.Add(speed);
        Controls.Add(new Label{Text="Repeat",AutoSize=true,Location=new Point(164,73)});
        repeats=new NumericUpDown{Minimum=1,Maximum=100000,Value=1,Location=new Point(210,69),Width=58};Controls.Add(repeats);
        loop=new CheckBox{Text="Loop until stopped",AutoSize=true,Location=new Point(274,71)};loop.CheckedChanged+=(s,e)=>UpdateControls();Controls.Add(loop);
        intervalEnabled=new RadioButton{Text="Run every",AutoSize=true,Location=new Point(10,99)};Controls.Add(intervalEnabled);
        intervalValue=new NumericUpDown{Minimum=1,Maximum=86400,Value=5,Location=new Point(96,97),Width=62};Controls.Add(intervalValue);
        intervalUnit=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(164,97),Width=70};intervalUnit.Items.AddRange(new object[]{"seconds","minutes","hours"});intervalUnit.SelectedIndex=1;Controls.Add(intervalUnit);
        intervalEnabled.CheckedChanged+=(s,e)=>UpdateControls();
        speedMode.CheckedChanged+=(s,e)=>UpdateControls();
        tips.SetToolTip(intervalEnabled,"Replay the whole recording at this start-to-start interval. Use Repeat or Loop. Longer recordings finish before restarting.");
        completedLoops=new Label{Text="Completed loops: 0",Location=new Point(238,97),Size=new Size(152,23),AutoEllipsis=true,Font=new Font("Segoe UI",8.5f),TextAlign=ContentAlignment.MiddleRight,ForeColor=Theme.Muted};Controls.Add(completedLoops);
        ResumeLayout(false);
        timer.Interval=5;timer.Tick+=(s,e)=>Tick();timer.Start();
        keyProc=Keyboard;mouseProc=Mouse;
        Shown+=(s,e)=> { keyHook=Native.SetWindowsHookEx(13,keyProc,Native.GetModuleHandle(null),0);mouseHook=Native.SetWindowsHookEx(14,mouseProc,Native.GetModuleHandle(null),0);if(keyHook==IntPtr.Zero || mouseHook==IntPtr.Zero){MessageBox.Show("Cannot install Windows input hooks. Close and reopen the app.");Close();} };
        FormClosing+=(s,e)=> { Stop(); if(!exitApproved&&!ConfirmDiscard()) {e.Cancel=true;return;} timer.Stop();Native.UnhookWindowsHookEx(keyHook);Native.UnhookWindowsHookEx(mouseHook); };
        using(var embedded=Assembly.GetExecutingAssembly().GetManifestResourceStream("macro.mtt")) {if(embedded!=null)using(var reader=new StreamReader(embedded)){macro=Recording.Serializer().Deserialize<Recording>(reader.ReadToEnd());macro.Validate();fileName="Embedded recording";}}
        UpdateControls();
        SetStatus("Ready",Tone.Ready,Summary());
        if(settingsWarning!=null)SetStatus("Ready",Tone.Warning,settingsWarning);
    }
    void ToggleMenu() {
        if(menu.Visible)menu.Close(ToolStripDropDownCloseReason.CloseCalled);
        else menu.Show(menuButton,new Point(menuButton.Width,menuButton.Height),ToolStripDropDownDirection.BelowLeft);
    }
    internal void TestMenu(bool mouseInput) {
        ToggleMenu();Application.DoEvents();if(!menu.Visible)throw new Exception("Menu did not open.");
        ToggleMenu();Application.DoEvents();if(menu.Visible)throw new Exception("Menu did not close.");
        ToggleMenu();menu.Close(ToolStripDropDownCloseReason.Keyboard);Application.DoEvents();if(menu.Visible)throw new Exception("Menu keyboard dismissal failed.");
        if(!mouseInput)return;
        TopMost=true;BringToFront();Activate();Application.DoEvents();
        Point saved=Cursor.Position;
        try {
            // An initial click on the title bar activates the test window without invoking an action.
            int titleX=Left+Width/2,titleY=Top+12;
            if(Native.WindowFromPoint(new Native.Point{X=titleX,Y=titleY})!=Handle)throw new Exception("Menu test title bar is obscured.");
            Native.Send(new MacroEvent{Message=0x201,X=titleX,Y=titleY});
            Native.Send(new MacroEvent{Message=0x202,X=titleX,Y=titleY});PumpMessages(120);
            Point point=menuButton.PointToScreen(new Point(menuButton.Width/2,menuButton.Height/2));
            if(Native.WindowFromPoint(new Native.Point{X=point.X,Y=point.Y})!=menuButton.Handle)throw new Exception("Menu test button is obscured.");
            for(int i=0;i<4;i++) {
                Native.Send(new MacroEvent{Message=0x201,X=point.X,Y=point.Y});PumpMessages(70);
                Native.Send(new MacroEvent{Message=0x202,X=point.X,Y=point.Y});PumpMessages(70);
                if(menu.Visible!=(i%2==0))throw new Exception("Mouse menu toggle failed at click "+(i+1));
            }
        }finally{menu.Close();TopMost=false;Cursor.Position=saved;}
    }
    static void PumpMessages(int milliseconds) {var elapsed=Stopwatch.StartNew();while(elapsed.ElapsedMilliseconds<milliseconds){Application.DoEvents();System.Threading.Thread.Sleep(1);}}
    ToolButton ToolAt(string text,int x,int y,int w,int h,Action action,ButtonStyle style,Font font) { var b=new ToolButton{Text=text,Style=style,Font=font,Location=new Point(x,y),Size=new Size(w,h)};b.Click+=(s,e)=>Guard(action);Controls.Add(b);return b; }
    ToolStripMenuItem MenuItem(string text,Action action) { var item=new ToolStripMenuItem(text);item.Click+=(s,e)=>Guard(action);menu.Items.Add(item);return item; }
    void SetStatus(string headline,Tone tone,string info) { card.Headline=headline;card.Tone=tone;card.Info=info; }
    static string Count(int n,string noun) { return n+" "+noun+(n==1?"":"s"); }
    string Summary() {
        if(macro.Events.Count==0)return "No recording yet";
        return Count(macro.Events.Count,"event")+"  ·  "+(macro.Duration/1000.0).ToString("0.00")+" s";
    }
    void Guard(Action action) {try { action(); } catch(Exception ex){Stop();MessageBox.Show(this,ex.Message,"myTaskTiny",MessageBoxButtons.OK,MessageBoxIcon.Error);} }
    bool ConfirmDiscard() { if(!dirty)return true; var result=MessageBox.Show(this,"Save this recording before continuing?","Unsaved recording",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);if(result==DialogResult.Cancel)return false;if(result==DialogResult.Yes){SaveMacro();return !dirty;}return true; }
    void UpdateControls() {
        bool busy=recording||playing||pending;
        if(uninstallItem!=null)uninstallItem.Enabled=!busy;
        if(updateItem!=null)updateItem.Enabled=!busy&&!updateChecking;
        savedItem.Enabled=openItem.Enabled=saveItem.Enabled=exportItem.Enabled=keysItem.Enabled=loop.Enabled=!busy;repeats.Enabled=!busy&&!loop.Checked;
        speedMode.Enabled=intervalEnabled.Enabled=!busy;speed.Enabled=!busy&&speedMode.Checked;intervalValue.Enabled=intervalUnit.Enabled=!busy&&intervalEnabled.Checked;
        play.Enabled=!recording&&macro.Events.Count>0;record.Enabled=!playing&&!pending;stop.Enabled=busy;
        record.Text=recording?"Finish":"Record";record.Style=recording?ButtonStyle.Danger:ButtonStyle.Primary;
        record.Hint=HotkeySettings.Name(hotkeys.Record);play.Hint=HotkeySettings.Name(hotkeys.Play);stop.Hint=HotkeySettings.Name(hotkeys.Stop);
        tips.SetToolTip(record,(recording?"Finish recording (":"Record (")+HotkeySettings.Name(hotkeys.Record)+")");tips.SetToolTip(play,"Play / stop playback ("+HotkeySettings.Name(hotkeys.Play)+")");tips.SetToolTip(stop,"Emergency stop ("+HotkeySettings.Name(hotkeys.Stop)+")");
        UpdateActivityIndicators();
    }
    void UpdateActivityIndicators() {
        bool busy=recording||playing||pending;
        play.Style=(pending||waitingForNext)?ButtonStyle.Waiting:playing?ButtonStyle.Playback:ButtonStyle.Secondary;
        stop.Style=busy?ButtonStyle.Danger:ButtonStyle.Secondary;
        stop.Enabled=busy;
        string state=recording?"Recording":pending?"Starting":waitingForNext?"Waiting":playing?"Playing":null;
        Text=(state==null?"":"["+state+"] ")+"myTaskTiny v"+AppVersion.Current+" — "+fileName+(dirty?" *":"");
        play.AccessibleDescription=pending?"Playback countdown. Press to cancel.":waitingForNext?"Waiting for the next run. Press to stop.":playing?"Recording is playing. Press to stop.":"Play the loaded recording.";
        stop.AccessibleDescription=busy?"Stop the current recording, playback, or countdown.":"Nothing is running.";
    }
    void ToggleRecord() {
        if(playing||pending)return;
        if(recording){Stop();return;}
        if(!ConfirmDiscard())return;
        macro=new Recording();selectedPath=null;fileName="Untitled";dirty=true;lastMove=-10;watch.Restart();recording=true;SetStatus("Recording",Tone.Recording,"Press "+HotkeySettings.Name(hotkeys.Record)+" to finish");UpdateControls();
    }
    void StartPlayback() {
        if(playing||pending){Stop();return;}
        if(recording||macro.Events.Count==0)return;
        macro.Validate();playbackSpeed=intervalEnabled.Checked?1:new double[]{.25,.5,1,2,4,8}[speed.SelectedIndex];repeatCount=(int)repeats.Value;forever=loop.Checked;
        runInterval=intervalEnabled.Checked?(double)intervalValue.Value*new double[]{1000,60000,3600000}[intervalUnit.SelectedIndex]:0;waitingForNext=false;
        position=0;iteration=0;UpdateLoopCount();pending=true;watch.Restart();SetStatus("Starting in 2 s",Tone.Waiting,"Switch to your target window");UpdateControls();
    }
    void Stop() {
        bool wasBusy=recording||playing||pending;
        if(recording){macro.Duration=Math.Min(86400000,watch.ElapsedMilliseconds);recording=false;}
        pending=playing=waitingForNext=false;watch.Stop();ReleaseHeld();SetStatus(wasBusy?"Stopped":"Ready",Tone.Ready,Summary());UpdateControls();
    }
    async void CheckUpdates(bool manual) {
        if(updateChecking)return;
        updateChecking=true;updateItem.Enabled=false;
        try {
            var release=await System.Threading.Tasks.Task.Factory.StartNew(()=>UpdateService.Fetch());
            if(IsDisposed||Disposing||(!manual&&!updatePreferences.CheckOnStartup))return;
            if(UpdateService.ShouldOffer(release,AppVersion.Current,updatePreferences.SkippedVersion,manual)) {
                availableUpdate=release;
                OfferUpdateIfIdle();
            } else if(manual)MessageBox.Show(this,release==null?"No downloadable stable release is available yet.":"You are up to date (v"+AppVersion.Current+").","myTaskTiny updates");
        }catch(Exception){if(manual&&!IsDisposed&&!Disposing)MessageBox.Show(this,"Could not check GitHub for updates. Check your internet connection and try again later.","myTaskTiny updates",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        finally{updateChecking=false;if(!IsDisposed&&!Disposing)UpdateControls();}
    }
    void OfferUpdateIfIdle() {
        if(availableUpdate==null||recording||playing||pending||ShortcutsBlocked||OwnedForms.Length>0||menu.Visible||Native.GetForegroundWindow()!=Handle)return;
        var release=availableUpdate;availableUpdate=null;editingHotkeys=true;
        try {using(var dialog=new UpdateDialog(release)) {
            var result=dialog.ShowDialog(this);
            if(result==DialogResult.Ignore){updatePreferences.SkippedVersion=release.Number.ToString();updatePreferences.Save(updatePreferencesPath);}
            else if(result==DialogResult.Retry)Process.Start(new ProcessStartInfo(release.Page){UseShellExecute=true});
            else if(result==DialogResult.Yes)InstallUpdate(release);
        }}finally{editingHotkeys=false;}
    }
    void InstallUpdate(ReleaseInfo release) {
        if(!ConfirmDiscard())return;
        using(var download=new InstallUpdateDialog(release,Assembly.GetExecutingAssembly().Location)) {
            if(download.ShowDialog(this)!=DialogResult.OK)return;
            bool handedOff=false;
            try {
                using(var helper=AutoUpdate.Start(download.Plan,Process.GetCurrentProcess().Id,false)){}
                handedOff=true;exitApproved=true;Close();
            }finally{if(!handedOff)AutoUpdate.Cleanup(download.Plan);}
        }
    }
    void Tick() {
        if(availableUpdate!=null)Guard(OfferUpdateIfIdle);
        if(recording) {card.Info=Count(macro.Events.Count,"event")+"  ·  "+(watch.ElapsedMilliseconds/1000.0).ToString("0.0")+" s";if(watch.ElapsedMilliseconds>=86400000 || macro.Events.Count>=500000)Stop();return;}
        if(pending)card.Headline="Starting in "+Math.Max(1,(int)Math.Ceiling((2000-watch.ElapsedMilliseconds)/1000.0))+" s";
        if(pending && watch.ElapsedMilliseconds>=2000) {pending=false;playing=true;watch.Restart();UpdateActivityIndicators();}
        if(!playing)return;
        Guard(()=>AdvancePlayback(watch.Elapsed.TotalMilliseconds,Native.Send));
    }
    void AdvancePlayback(double elapsed,Action<MacroEvent> send) {
        if(waitingForNext) {
            if(elapsed>=runInterval){waitingForNext=false;position=0;watch.Restart();UpdateActivityIndicators();SetStatus("Playing",Tone.Active,"Starting next run");}
            else {card.Headline="Waiting";card.Tone=Tone.Waiting;card.Info="Next run in "+FormatRemaining(runInterval-elapsed);}
            return;
        }
        UpdateActivityIndicators();
        int batch=0;
        while(position<macro.Events.Count && macro.Events[position].Time/playbackSpeed<=elapsed && batch++<100) {var e=macro.Events[position++];send(e);Track(e);}
        card.Headline="Playing";card.Tone=Tone.Active;card.Info="Pass "+(iteration+1)+(forever?" (looping)":" / "+repeatCount)+"  ·  "+position+" / "+macro.Events.Count+" events";
        if(position==macro.Events.Count && elapsed>=Math.Max(20,macro.Duration/playbackSpeed)) {
            ReleaseHeld();iteration++;UpdateLoopCount();
            if(!forever&&iteration>=repeatCount){Stop();SetStatus("Finished",Tone.Ready,Summary());}
            else if(runInterval>elapsed){waitingForNext=true;UpdateActivityIndicators();SetStatus("Waiting",Tone.Waiting,"Next run in "+FormatRemaining(runInterval-elapsed));}
            else {position=0;watch.Restart();}
        }
    }
    static string FormatRemaining(double milliseconds) {
        var remaining=TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0,milliseconds)/1000));
        return (remaining.Days>0?remaining.Days+"d ":"")+remaining.ToString(@"hh\:mm\:ss");
    }
    void UpdateLoopCount() {completedLoops.Text="Completed loops: "+iteration.ToString("N0");}
    internal void TestActivityIndicators() {
        timer.Stop();
        try {
            macro=new Recording{Duration=10000};macro.Events.Add(new MacroEvent{Time=0,Message=0x200});repeats.Value=2;
            intervalEnabled.Checked=true;intervalValue.Value=5;intervalUnit.SelectedIndex=1;
            using(var states=new Bitmap(Width,Height*4))using(var canvas=Graphics.FromImage(states)) {
                StartPlayback();
                if(play.Style!=ButtonStyle.Waiting||stop.Style!=ButtonStyle.Danger||!stop.Enabled||card.Tone!=Tone.Waiting)throw new Exception("Countdown indicators failed.");
                SnapshotState(canvas,0);
                pending=false;playing=true;AdvancePlayback(0,e=>{});
                if(play.Style!=ButtonStyle.Playback||stop.Style!=ButtonStyle.Danger||card.Headline!="Playing"||card.Tone!=Tone.Active||!Text.StartsWith("[Playing]"))throw new Exception("Playback indicators failed.");
                SnapshotState(canvas,1);
                AdvancePlayback(10000,e=>{});
                if(play.Style!=ButtonStyle.Waiting||stop.Style!=ButtonStyle.Danger||card.Headline!="Waiting"||card.Tone!=Tone.Waiting)throw new Exception("Interval waiting indicators failed.");
                SnapshotState(canvas,2);
                Stop();
                if(play.Style!=ButtonStyle.Secondary||stop.Enabled||stop.Style!=ButtonStyle.Secondary||card.Headline!="Stopped")throw new Exception("Stop indicators failed.");
                SnapshotState(canvas,3);
                states.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"activity-preview.png"));
            }
            repeats.Value=1;StartPlayback();pending=false;playing=true;AdvancePlayback(10000,e=>{});
            if(card.Headline!="Finished"||stop.Enabled||play.Style!=ButtonStyle.Secondary)throw new Exception("Finished indicators failed.");
            recording=true;UpdateControls();if(stop.Style!=ButtonStyle.Danger||!stop.Enabled)throw new Exception("Recording stop indicator failed.");recording=false;Stop();
        }finally{intervalEnabled.Checked=false;speedMode.Checked=true;repeats.Value=1;timer.Start();}
    }
    void SnapshotState(Graphics target,int row) {using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));target.DrawImageUnscaled(bitmap,0,row*Height);}}
    internal void TestIntervals() {
        if(FormatRemaining(90000000)!="1d 01:00:00"||FormatRemaining(1)!="00:00:01"||FormatRemaining(0)!="00:00:00")throw new Exception("Interval countdown formatting failed.");
        // Simulate elapsed time without waiting minutes or injecting desktop input.
        macro=new Recording{Duration=1000};macro.Events.Add(new MacroEvent{Time=0,Message=0x200});macro.Events.Add(new MacroEvent{Time=500,Message=0x200});
        speed.SelectedIndex=4;intervalEnabled.Checked=true;
        if(speed.Enabled||speedMode.Checked||!intervalValue.Enabled||!intervalUnit.Enabled)throw new Exception("Interval mode controls failed.");
        intervalValue.Value=5;intervalUnit.SelectedIndex=1;repeats.Value=2;StartPlayback();
        if(runInterval!=300000||playbackSpeed!=1)throw new Exception("Minute interval conversion failed.");
        pending=false;playing=true;int sent=0;Action<MacroEvent> send=e=>sent++;
        AdvancePlayback(0,send);AdvancePlayback(1000,send);
        if(sent!=2||!waitingForNext||iteration!=1||completedLoops.Text!="Completed loops: 1")throw new Exception("First scheduled pass failed.");
        AdvancePlayback(299999,send);if(sent!=2||!waitingForNext)throw new Exception("Interval fired early.");
        AdvancePlayback(300000,send);AdvancePlayback(0,send);AdvancePlayback(1000,send);
        if(sent!=4||playing||waitingForNext||completedLoops.Text!="Completed loops: 2")throw new Exception("Repeat count or interval timing failed.");
        loop.Checked=true;StartPlayback();if(completedLoops.Text!="Completed loops: 0")throw new Exception("Loop counter did not reset.");pending=false;playing=true;AdvancePlayback(1000,send);Stop();
        if(playing||waitingForNext||pending||completedLoops.Text!="Completed loops: 1")throw new Exception("Stop while waiting or retained loop count failed.");
        intervalUnit.SelectedIndex=0;intervalValue.Value=1;macro.Duration=2000;StartPlayback();pending=false;playing=true;AdvancePlayback(1000,send);
        if(iteration!=0)throw new Exception("Long recording overlapped.");
        AdvancePlayback(2000,send);if(iteration!=1||waitingForNext||position!=0)throw new Exception("Long recording completion failed.");Stop();
        intervalUnit.SelectedIndex=2;StartPlayback();if(runInterval!=3600000)throw new Exception("Hour conversion failed.");Stop();
        speedMode.Checked=true;
        if(!speed.Enabled||intervalEnabled.Checked||intervalValue.Enabled||intervalUnit.Enabled)throw new Exception("Speed mode controls failed.");
        loop.Checked=false;repeats.Value=1;StartPlayback();if(runInterval!=0||playbackSpeed!=4)throw new Exception("Normal playback interval failed.");Stop();
        speed.SelectedIndex=2;intervalValue.Value=5;intervalUnit.SelectedIndex=1;
    }
    void Track(MacroEvent e) {
        string id=null;bool up=false;
        if(e.Message<0x200){id="k"+e.Data;up=e.Message==0x101||e.Message==0x105;}
        else switch(e.Message){case 0x201:case 0x202:id="left";up=e.Message==0x202;break;case 0x204:case 0x205:id="right";up=e.Message==0x205;break;case 0x207:case 0x208:id="middle";up=e.Message==0x208;break;case 0x20B:case 0x20C:id="x"+e.Data;up=e.Message==0x20C;break;}
        if(id!=null){if(up)held.Remove(id);else held[id]=e;}
    }
    void ReleaseHeld() { foreach(var e in held.Values) {var up=new MacroEvent{Message=e.Message<0x200?0x101:e.Message+1,Data=e.Data,Scan=e.Scan,Flags=e.Flags,X=Cursor.Position.X,Y=Cursor.Position.Y};try{Native.Send(up);}catch{}}held.Clear(); }
    void ShowAbout() {
        editingHotkeys=true;
        try {using(var dialog=new AboutDialog(Icon))dialog.ShowDialog(this);}
        finally{editingHotkeys=false;}
    }
    void CustomizeKeys() {
        if(recording||playing||pending)return;
        editingHotkeys=true;
        try {using(var dialog=new HotkeyDialog(hotkeys)) {
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            dialog.Selection.Save(settingsPath);hotkeys=dialog.Selection;UpdateControls();SetStatus("Keys saved",Tone.Ready,"Custom shortcuts are active");
        }}finally{editingHotkeys=false;}
    }
    // Native file dialogs disable the owner without necessarily changing Control.Enabled.
    bool ShortcutsBlocked {get{return editingHotkeys||!Enabled||!Native.IsWindowEnabled(Handle);}}
    bool HandleShortcut(uint key,bool down) {
        // Always consume the release/repeat of a previously swallowed press, even if a dialog opened.
        if(shortcutHeld.Contains(key)){if(!down)shortcutHeld.Remove(key);return true;}
        if(ShortcutsBlocked || !down)return false;
        int action=hotkeys.ActionFor(key);if(action==0)return false;
        shortcutHeld.Add(key);
        BeginInvoke(new Action(()=>Guard(()=>{if(ShortcutsBlocked)return;if(action==1)ToggleRecord();else if(action==2)StartPlayback();else Stop();})));
        return true;
    }
    internal void TestHotkeys() {
        hotkeys=new HotkeySettings{Record=(int)Keys.F6,Play=(int)Keys.F7,Stop=(int)Keys.F10};UpdateControls();
        if(HandleShortcut((uint)Keys.F8,true)||HandleShortcut((uint)Keys.F9,true)||HandleShortcut((uint)Keys.F12,true))throw new Exception("Replaced default shortcut still active.");
        if(record.Hint!="F6"||play.Hint!="F7"||stop.Hint!="F10")throw new Exception("Custom shortcut labels failed.");
        HandleShortcut((uint)Keys.F6,true);Application.DoEvents();
        if(!recording)throw new Exception("Custom record shortcut failed.");
        HandleShortcut((uint)Keys.F6,true);Application.DoEvents();if(!recording)throw new Exception("Key repeat triggered twice.");
        HandleShortcut((uint)Keys.F6,false);HandleShortcut((uint)Keys.F6,true);Application.DoEvents();
        if(recording)throw new Exception("Custom finish shortcut failed.");HandleShortcut((uint)Keys.F6,false);dirty=false;
        macro=new Recording{Duration=40};macro.Events.Add(new MacroEvent{Message=0x100,Data=65,Scan=30});
        HandleShortcut((uint)Keys.F7,true);Application.DoEvents();if(!pending)throw new Exception("Custom play failed.");
        HandleShortcut((uint)Keys.F10,true);Application.DoEvents();if(pending||playing)throw new Exception("Independent stop while play key held failed.");
        HandleShortcut((uint)Keys.F7,false);HandleShortcut((uint)Keys.F10,false);
        editingHotkeys=true;if(HandleShortcut((uint)Keys.F6,true))throw new Exception("Shortcut fired in settings.");editingHotkeys=false;
        Enabled=false;
        try{if(HandleShortcut((uint)Keys.F6,true))throw new Exception("Shortcut fired while a modal dialog disabled the owner.");}finally{Enabled=true;}
        HandleShortcut((uint)Keys.F7,true);Enabled=false;Application.DoEvents();Enabled=true;HandleShortcut((uint)Keys.F7,false);
        if(pending||playing)throw new Exception("Queued shortcut fired after a modal dialog opened.");
        hotkeys=new HotkeySettings();UpdateControls();
        if(hotkeys.ActionFor((uint)Keys.F8)!=1||hotkeys.ActionFor((uint)Keys.F9)!=2||hotkeys.ActionFor((uint)Keys.F12)!=3||record.Hint!="F8"||play.Hint!="F9"||stop.Hint!="F12")throw new Exception("Restore default keys failed.");
    }
    // A second launch broadcasts this message so the running window comes to the front instead of opening another copy.
    internal static readonly uint ActivateMessage=Native.RegisterWindowMessage("myTaskTiny.Activate");
    internal static readonly uint LegacyActivateMessage=Native.RegisterWindowMessage(AppIdentity.LegacyName+".Activate");
    protected override void WndProc(ref Message m) {
        if(m.Msg==(int)ActivateMessage||m.Msg==(int)LegacyActivateMessage){if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;Activate();return;}
        base.WndProc(ref m);
    }
    IntPtr Keyboard(int code,IntPtr message,IntPtr data) {
        if(code>=0){var k=(Native.KeyHook)Marshal.PtrToStructure(data,typeof(Native.KeyHook));int m=message.ToInt32();
            if((k.Flags&0x10)==0) {
                bool down=m==0x100||m==0x104;
                if(HandleShortcut(k.Vk,down))return new IntPtr(1);
                if(recording && !ShortcutsBlocked && Native.GetForegroundWindow()!=Handle) CaptureEvent(new MacroEvent{Time=watch.ElapsedMilliseconds,Message=m,Data=k.Vk,Scan=k.Scan,Flags=k.Flags});
            }
        }
        return Native.CallNextHookEx(keyHook,code,message,data);
    }
    IntPtr Mouse(int code,IntPtr message,IntPtr data) {
        if(code>=0&&recording&&!ShortcutsBlocked){var m=(Native.MouseHook)Marshal.PtrToStructure(data,typeof(Native.MouseHook));int msg=message.ToInt32();long now=watch.ElapsedMilliseconds;
            if((m.Flags&1)==0 && (WindowState==FormWindowState.Minimized || !Bounds.Contains(m.Point.X,m.Point.Y)) && (msg!=0x200||now-lastMove>=8)) {
                if(msg==0x200)lastMove=now;
                uint value=(msg==0x20A||msg==0x20E)?unchecked((uint)(int)(short)(m.Data>>16)):(m.Data>>16);
                CaptureEvent(new MacroEvent{Time=now,Message=msg,X=m.Point.X,Y=m.Point.Y,Data=value});
            }
        }
        return Native.CallNextHookEx(mouseHook,code,message,data);
    }
    void CaptureEvent(MacroEvent input) {
        if(macro.Events.Count>=500000||input.Time>86400000){Stop();SetStatus("Stopped",Tone.Warning,"Recording limit reached; save your recording");return;}
        macro.Events.Add(input);
    }
    internal void TestRecordingLimits() {
        macro=new Recording();recording=true;watch.Restart();
        CaptureEvent(new MacroEvent{Time=86400001,Message=0x200});
        if(recording||macro.Events.Count!=0)throw new Exception("Recording exceeded its duration limit.");macro.Validate();
        macro=new Recording();recording=true;watch.Restart();
        for(int i=0;i<500000;i++)macro.Events.Add(new MacroEvent{Time=0,Message=0x200});
        CaptureEvent(new MacroEvent{Time=0,Message=0x200});
        if(recording||macro.Events.Count!=500000)throw new Exception("Recording exceeded its event limit.");macro.Validate();
        macro=new Recording();dirty=false;Stop();
    }
    void ExportExe() {
        if(macro.Events.Count==0)throw new Exception("Record or open a macro first.");
        macro.Validate();
        using(var d=new SaveFileDialog{Filter="Windows application (*.exe)|*.exe",FileName="MyMacro.exe"}) {
            if(d.ShowDialog(this)!=DialogResult.OK)return;
            if(string.Equals(Path.GetFullPath(d.FileName),Assembly.GetExecutingAssembly().Location,StringComparison.OrdinalIgnoreCase))throw new Exception("Choose a different name from the running app.");
            BuildExport(d.FileName);
            SetStatus("Exported",Tone.Ready,"Open the EXE and press Play");
            RememberCreated(d.FileName);
        }
    }
    internal void BuildExport(string outputPath) {
            string temp=Path.Combine(Path.GetTempPath(),"myTaskTiny-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            try {
                string source;
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Program.cs"))using(var reader=new StreamReader(stream)){source=reader.ReadToEnd();}
                string metadata;
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AssemblyInfo.cs"))using(var reader=new StreamReader(stream)){metadata=reader.ReadToEnd();}
                File.WriteAllText(Path.Combine(temp,"AssemblyInfo.cs"),metadata);
                File.WriteAllText(Path.Combine(temp,"Program.cs"),source);
                File.WriteAllText(Path.Combine(temp,"macro.mtt"),Recording.Serializer().Serialize(macro));
                string iconPath=Path.Combine(temp,"app.ico");
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")) {if(stream!=null)using(var output=File.Create(iconPath))stream.CopyTo(output);}
                using(var compiler=new CSharpCodeProvider()) {
                    var options=new CompilerParameters(new string[]{"System.dll","System.Core.dll","System.Windows.Forms.dll","System.Drawing.dll","System.Web.Extensions.dll","Microsoft.CSharp.dll"},Path.Combine(temp,"macro.exe"));
                    options.GenerateExecutable=true;options.CompilerOptions="/target:winexe /optimize+"+(File.Exists(iconPath)?" \"/win32icon:"+iconPath+"\"":"");
                    options.EmbeddedResources.Add(Path.Combine(temp,"AssemblyInfo.cs"));options.EmbeddedResources.Add(Path.Combine(temp,"Program.cs"));options.EmbeddedResources.Add(Path.Combine(temp,"macro.mtt"));if(File.Exists(iconPath))options.EmbeddedResources.Add(iconPath);
                    var result=compiler.CompileAssemblyFromSource(options,source,metadata);
                    if(result.Errors.HasErrors)throw new Exception("Export failed: "+result.Errors[0].ErrorText);
                    File.Copy(options.OutputAssembly,outputPath,true);
                }
            } finally {Directory.Delete(temp,true);}
    }
    internal void TestPlayback() {
        using(var target=new Form{Text="myTaskTiny playback test",Size=new Size(320,150),StartPosition=FormStartPosition.CenterScreen}) {
            var input=new TextBox{Dock=DockStyle.Fill,Multiline=true};target.Controls.Add(input);target.Show();target.Activate();Native.SetForegroundWindow(target.Handle);input.Focus();Application.DoEvents();
            if(Native.GetForegroundWindow()!=target.Handle)throw new Exception("Test window could not gain focus.");
            macro=new Recording{Duration=80};
            macro.Events.Add(new MacroEvent{Time=0,Message=0x100,Data=65,Scan=30});macro.Events.Add(new MacroEvent{Time=40,Message=0x101,Data=65,Scan=30});
            repeats.Value=2;StartPlayback();var timeout=Stopwatch.StartNew();
            while((playing||pending)&&timeout.ElapsedMilliseconds<5000){Application.DoEvents();System.Threading.Thread.Sleep(1);}
            if(playing||pending || input.Text.ToLowerInvariant()!="aa")throw new Exception("Live keyboard playback/repeat failed: "+input.Text);
            // A held key must be released even when stopped partway through a recording.
            Native.Send(macro.Events[0]);Track(macro.Events[0]);Stop();
            if(held.Count!=0)throw new Exception("Stop did not clear held input.");
            StartPlayback();Stop();if(pending||playing)throw new Exception("Countdown cancellation failed.");
            target.Close();
        }
    }
    void SaveMacro() {
        using(var d=new SaveFileDialog{Filter="myTaskTiny recording (*.mtt)|*.mtt",DefaultExt="mtt",FileName=fileName=="Untitled"?"My recording.mtt":fileName}) {
            if(d.ShowDialog(this)!=DialogResult.OK)return;macro.Validate();string temp=d.FileName+".tmp";File.WriteAllText(temp,Recording.Serializer().Serialize(macro));if(File.Exists(d.FileName))File.Replace(temp,d.FileName,null);else File.Move(temp,d.FileName);fileName=Path.GetFileName(d.FileName);selectedPath=Path.GetFullPath(d.FileName);dirty=false;UpdateControls();RememberRecording(selectedPath);RememberCreated(selectedPath);
        }
    }
    void RememberCreated(string path) {
        try{createdFiles.Remember(path);}catch{SetStatus("File saved",Tone.Warning,"Could not track this file for uninstall");}
    }
    void Uninstall() {
        if(recording||playing||pending)return;
        editingHotkeys=true;
        try {
            if(!ConfirmDiscard())return;
            string exe=Assembly.GetExecutingAssembly().Location;
            bool exported=Assembly.GetExecutingAssembly().GetManifestResourceInfo("macro.mtt")!=null;
            var core=UninstallService.CoreFiles(exe,exported?null:updatePreferencesPath);
            var candidates=createdFiles.Read();candidates.AddRange(library.Available());
            using(var dialog=new UninstallDialog(core,UninstallService.DataFiles(exe,candidates))) {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                dialog.Plan.EmptyDirectories.Add(Path.Combine(Path.GetDirectoryName(exe),"Recordings"));
                dialog.Plan.EmptyDirectories.Add(Path.GetDirectoryName(exe));
                if(!exported)dialog.Plan.EmptyDirectories.Add(Path.GetDirectoryName(updatePreferencesPath));
                using(var helper=UninstallService.Start(dialog.Plan,Process.GetCurrentProcess().Id,false)) {if(helper==null)throw new IOException("Could not start uninstall.");}
                exitApproved=true;Close();
            }
        } finally {editingHotkeys=false;}
    }
    void RememberRecording(string path) {
        try{library.Remember(path);}catch(Exception){SetStatus("Recording ready",Tone.Warning,"Could not save the recordings menu");}
    }
    void RefreshSavedMenu() {
        while(savedItem.DropDownItems.Count>0){var item=savedItem.DropDownItems[0];savedItem.DropDownItems.RemoveAt(0);item.Dispose();}
        var paths=library.Available();
        if(paths.Count==0)savedItem.DropDownItems.Add(new ToolStripMenuItem("No saved recordings yet"){Enabled=false});
        foreach(var path in paths) {
            string captured=path;
            string label=Path.GetFileName(path);
            if(paths.FindAll(x=>string.Equals(Path.GetFileName(x),label,StringComparison.OrdinalIgnoreCase)).Count>1)label+=" — "+Path.GetDirectoryName(path);
            var item=new ToolStripMenuItem(label.Replace("&","&&")){ToolTipText=path,Checked=string.Equals(path,selectedPath,StringComparison.OrdinalIgnoreCase)};
            item.Click+=(s,e)=>Guard(()=>LoadRecording(captured));savedItem.DropDownItems.Add(item);
        }
        savedItem.DropDownItems.Add(new ToolStripSeparator());
        var browse=new ToolStripMenuItem("Browse…");browse.Click+=(s,e)=>Guard(OpenMacro);savedItem.DropDownItems.Add(browse);
    }
    void LoadRecording(string path) {
        if(recording||playing||pending)return;
        if(!File.Exists(path))throw new Exception("This recording was moved or deleted. Use Browse to locate it.");
        if(new FileInfo(path).Length>Recording.MaximumJsonLength)throw new Exception("Recording is too large.");
        var loaded=Recording.Serializer().Deserialize<Recording>(File.ReadAllText(path));if(loaded==null)throw new Exception("Empty recording.");loaded.Validate();
        if(!ConfirmDiscard())return;
        macro=loaded;selectedPath=Path.GetFullPath(path);fileName=Path.GetFileName(path);dirty=false;Stop();RememberRecording(path);
    }
    void OpenMacro() {using(var d=new OpenFileDialog{Filter="myTaskTiny recording (*.mtt)|*.mtt"}){if(d.ShowDialog(this)==DialogResult.OK)LoadRecording(d.FileName);} }
    internal void TestLibrary() {
        string folder=Path.Combine(Path.GetTempPath(),"myTaskTiny-library-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var original=library;
        try {
            string first=Path.Combine(folder,"first.mtt"),second=Path.Combine(folder,"second.mtt"),index=Path.Combine(folder,"history.json");
            var sample=new Recording{Duration=10};sample.Events.Add(new MacroEvent{Message=0x100,Data=66,Scan=48});File.WriteAllText(first,Recording.Serializer().Serialize(sample));
            library=new RecordingLibrary(index,folder);if(library.Available().Count!=1)throw new Exception("Saved recording discovery failed.");
            library.Remember(first);library.Remember(first);library=new RecordingLibrary(index,folder);if(library.Available().Count!=1)throw new Exception("Library persistence/deduplication failed.");
            LoadRecording(first);if(fileName!="first.mtt"||macro.Events.Count!=1||playing||pending)throw new Exception("Saved menu loading failed.");
            RefreshSavedMenu();if(!((ToolStripMenuItem)savedItem.DropDownItems[0]).Checked)throw new Exception("Selected recording marker missing.");
            File.WriteAllText(second,"invalid recording");bool rejected=false;try{LoadRecording(second);}catch{rejected=true;}if(!rejected||fileName!="first.mtt")throw new Exception("Invalid recording replaced current macro.");
            File.Delete(first);File.Delete(second);if(library.Available().Count!=0)throw new Exception("Missing recording still listed.");
            RefreshSavedMenu();if(savedItem.DropDownItems[0].Enabled)throw new Exception("Empty menu state failed.");
        }finally{library=original;Directory.Delete(folder,true);}
    }

}
internal static class Program {
    [STAThread] static void Main(string[] args) {
        Native.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>0&&(args[0]=="--self-test"||args[0]=="--hotkey-test")) {
            Exception failure=null;
            using(var context=new ApplicationContext())using(var runner=new Timer{Interval=50}) {
                runner.Tick+=(sender,e)=> {runner.Stop();try{SelfTest(args[0]=="--self-test");}catch(Exception ex){failure=ex;}finally{context.ExitThread();}};
                runner.Start();Application.Run(context);
            }
            Environment.ExitCode=failure==null?0:1;
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),failure!=null?"FAIL: "+failure:(args[0]=="--self-test"?"PASS: full Windows regression suite, including live menu clicks and keyboard playback. ":"PASS: non-injecting regression suite. ")+"Uninstall tests: scoped deletion, changed-file protection, wait-for-exit, unchecked data, cancel, tracked exports and settings. Playback, countdown, waiting, stopped, and finished indicators passed. Name migration and legacy update compatibility passed. Automatic update download validation, cancellation, replacement, restart, rollback, startup confirmation, Windows attachment-policy integration, recording limits, and preservation of user files passed. Existing shortcut, interval, recording, export, update, and version checks passed.");
            return;
        }
        if(args.Length>0&&args[0]=="--check-update-test") {try{var release=UpdateService.Fetch();File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-test-results.txt"),release==null?"PASS: no published downloadable release": "PASS: GitHub release "+release.tag_name+"; offer="+UpdateService.ShouldOffer(release,AppVersion.Current,null,false));}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-test-results.txt"),"FAIL: "+ex.Message);Environment.ExitCode=1;}return;}
        // One recorder at a time: two copies would both react to the same global shortcuts.
        bool first,legacyFirst;
        using(var instance=new System.Threading.Mutex(true,"Local\\myTaskTiny.SingleInstance",out first))
        using(var legacy=new System.Threading.Mutex(true,"Local\\"+AppIdentity.LegacyName+".SingleInstance",out legacyFirst)) {
            if(!first||!legacyFirst){Native.AllowSetForegroundWindow(-1);Native.PostMessage((IntPtr)0xFFFF,MainForm.ActivateMessage,IntPtr.Zero,IntPtr.Zero);Native.PostMessage((IntPtr)0xFFFF,MainForm.LegacyActivateMessage,IntPtr.Zero,IntPtr.Zero);return;}
            using(var form=new MainForm()) {
                form.Shown+=(sender,e)=>{if(form.ReadyForUpdate)try{AutoUpdate.ConfirmStartup(args);}catch{};};
                Application.Run(form);
            }
        }
    }
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    static void SelfTest(bool livePlayback) {
        AppIdentity.Test();
        UpdateService.Test();
        AutoUpdate.Test();
        InstallUpdateDialog.Test();
        UninstallService.Test();
        using(var dialog=new UpdateDialog(new ReleaseInfo{tag_name="v2.0.0",body="Example release notes\n- New feature\n- Bug fix",assets=new List<ReleaseAsset>{new ReleaseAsset{name="myTaskTiny.exe",state="uploaded",size=100},new ReleaseAsset{name="SHA256SUMS.txt",state="uploaded",size=82}}})) {
            dialog.Text="Update dialog layout test";dialog.Show();Application.DoEvents();
            using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-preview.png"));}
        }
        Assert(AppVersion.Current!="development","Version metadata missing");
        Assert(FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion==AppVersion.Current,"Executable product version");
        string settingsFile=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".json");
        try {
            var keys=new HotkeySettings{Record=(int)Keys.F6,Play=(int)Keys.F7,Stop=(int)Keys.F10};keys.Save(settingsFile);
            var loaded=HotkeySettings.Load(settingsFile);Assert(loaded.Record==keys.Record&&loaded.Play==keys.Play&&loaded.Stop==keys.Stop,"Settings round trip");
            keys.Stop=(int)Keys.F11;keys.Save(settingsFile);Assert(HotkeySettings.Load(settingsFile).Stop==(int)Keys.F11,"Settings replacement");
            keys.Play=keys.Record;bool invalid=false;try{keys.Validate();}catch{invalid=true;}Assert(invalid,"Reject duplicate keys");
            keys.Play=(int)Keys.F8;keys.Validate();Assert(keys.ActionFor((uint)Keys.F8)==2,"Reassign unused default key");
            keys.Play=0;invalid=false;try{keys.Validate();}catch{invalid=true;}Assert(invalid,"Reject unsupported key");
        }finally{if(File.Exists(settingsFile))File.Delete(settingsFile);}

        var largestEvent=new MacroEvent{Time=86400000,Message=0x20E,X=int.MinValue,Y=int.MinValue,Data=uint.MaxValue,Scan=uint.MaxValue,Flags=uint.MaxValue};
        Assert((Recording.Serializer().Serialize(largestEvent).Length+1L)*500000+100<=Recording.MaximumJsonLength,"Maximum recording fits JSON size limit");
        var r=new Recording{Duration=100};r.Events.Add(new MacroEvent{Time=10,Message=0x100,Data=65,Scan=30});r.Events.Add(new MacroEvent{Time=90,Message=0x101,Data=65,Scan=30});r.Validate();
        var copy=Recording.Serializer().Deserialize<Recording>(Recording.Serializer().Serialize(r));copy.Validate();Assert(copy.Events.Count==2&&copy.Events[1].Time==90,"Round trip");
        copy.Events[1].Time=1;bool rejected=false;try{copy.Validate();}catch{rejected=true;}Assert(rejected,"Reject unordered events");copy.Events[1].Time=90;copy.Events[1].Data=256;rejected=false;try{copy.Validate();}catch{rejected=true;}Assert(rejected,"Reject invalid key");
        Assert(Marshal.SizeOf(typeof(Native.Input))==(IntPtr.Size==8?40:28),"Native INPUT ABI");var k=Native.Convert(r.Events[1]);Assert(k.Type==1&&k.Value.Key.Scan==30&&k.Value.Key.Flags==10,"Keyboard conversion");
        var m=Native.Convert(new MacroEvent{Message=0x20A,Data=unchecked((uint)-120)});Assert(m.Value.Mouse.Flags==0xC801&&m.Value.Mouse.Data==unchecked((uint)-120),"Wheel conversion");
        Native.Hook callback=(c,w,l)=>Native.CallNextHookEx(IntPtr.Zero,c,w,l);var kh=Native.SetWindowsHookEx(13,callback,Native.GetModuleHandle(null),0);var mh=Native.SetWindowsHookEx(14,callback,Native.GetModuleHandle(null),0);Assert(kh!=IntPtr.Zero&&mh!=IntPtr.Zero,"Install hooks");Native.UnhookWindowsHookEx(kh);Native.UnhookWindowsHookEx(mh);GC.KeepAlive(callback);
        using(var f=new MainForm(true)){f.Show();Application.DoEvents();Assert(f.Visible,"Window visible");f.TestHotkeys();f.TestMenu(livePlayback);f.TestLibrary();f.TestIntervals();f.TestRecordingLimits();f.TestActivityIndicators();if(livePlayback)f.TestPlayback();
            string exported=Path.Combine(Path.GetTempPath(),"myTaskTiny-test-"+Guid.NewGuid().ToString("N")+".exe");
            try {f.BuildExport(exported);Assert(new FileInfo(exported).Length>10000,"Export executable");Assert(FileVersionInfo.GetVersionInfo(exported).ProductVersion==AppVersion.Current,"Export version metadata");}finally{if(File.Exists(exported))File.Delete(exported);}
            using(var bitmap=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bitmap,new Rectangle(0,0,f.Width,f.Height));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview.png"));}
            using(var dialog=new HotkeyDialog(new HotkeySettings())){dialog.Show(f);Application.DoEvents();using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"keys-preview.png"));}dialog.Close();}
            f.Close();}
    }
}
