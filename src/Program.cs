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

public class ReleaseAsset { public string name {get;set;} public string state {get;set;} public long size {get;set;} }
public class ReleaseInfo {
    public string tag_name {get;set;}
    public string body {get;set;}
    public bool draft {get;set;}
    public bool prerelease {get;set;}
    public List<ReleaseAsset> assets {get;set;}
    public Version Number {get {return UpdateService.ParseVersion(tag_name);}}
    public string Page {get {return "https://github.com/Crabyy/myTinyTask/releases/tag/"+Uri.EscapeDataString(tag_name);}}
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
    public const string Endpoint="https://api.github.com/repos/Crabyy/myTinyTask/releases/latest";
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
        if(release.assets==null||!release.assets.Exists(a=>a!=null&&a.state=="uploaded"&&a.size>0&&(a.name=="myTinyTask.exe"||a.name=="myTinyTask-"+release.Number+".zip")))return null;
        return release;
    }
    public static bool ShouldOffer(ReleaseInfo release,string current,string skipped,bool manual) {
        var number=ParseVersion(current);
        return release!=null&&number!=null&&release.Number>number&&(manual||!string.Equals(release.Number.ToString(),skipped,StringComparison.Ordinal));
    }
    public static ReleaseInfo Fetch() {
        System.Net.ServicePointManager.SecurityProtocol|=System.Net.SecurityProtocolType.Tls12;
        var request=(System.Net.HttpWebRequest)System.Net.WebRequest.Create(Endpoint);
        request.UserAgent="myTinyTask/"+AppVersion.Current;
        request.Accept="application/vnd.github+json";request.Headers["X-GitHub-Api-Version"]="2022-11-28";
        request.Timeout=15000;request.ReadWriteTimeout=15000;
        try {
            using(var response=(System.Net.HttpWebResponse)request.GetResponse())
            using(var reader=new StreamReader(response.GetResponseStream())) {
                var content=new System.Text.StringBuilder();var buffer=new char[4096];int count;
                while((count=reader.Read(buffer,0,buffer.Length))>0){content.Append(buffer,0,count);if(content.Length>2000000)throw new IOException("Release response is too large.");}
                return ParseRelease(content.ToString());
            }
        }catch(System.Net.WebException ex){var response=ex.Response as System.Net.HttpWebResponse;if(response!=null){bool missing=response.StatusCode==System.Net.HttpStatusCode.NotFound;response.Dispose();if(missing)return null;}throw;}
    }
    public static void Test() {
        if(ParseVersion("v1.10.0")<=ParseVersion("1.9.9")||ParseVersion("1.0.0-beta")!=null||ParseVersion("../1.2.3")!=null)throw new Exception("Update version parsing failed.");
        var release=new ReleaseInfo{tag_name="v2.0.0",body="Changes",assets=new List<ReleaseAsset>{new ReleaseAsset{name="myTinyTask.exe",state="uploaded",size=100}}};
        var parsed=ParseRelease(Recording.Serializer().Serialize(release));
        if(!ShouldOffer(parsed,"1.9.0",null,false)||ShouldOffer(parsed,"2.0.0",null,false)||ShouldOffer(parsed,"3.0.0",null,false)||ShouldOffer(parsed,"1.9.0","2.0.0",false)||!ShouldOffer(parsed,"1.9.0","2.0.0",true))throw new Exception("Update selection failed.");
        if(parsed.Page!="https://github.com/Crabyy/myTinyTask/releases/tag/v2.0.0")throw new Exception("Update URL failed.");
        release.prerelease=true;if(ParseRelease(Recording.Serializer().Serialize(release))!=null)throw new Exception("Prerelease offered.");release.prerelease=false;
        release.draft=true;if(ParseRelease(Recording.Serializer().Serialize(release))!=null)throw new Exception("Draft offered.");release.draft=false;
        release.assets.Clear();if(ParseRelease(Recording.Serializer().Serialize(release))!=null)throw new Exception("Source-only release offered.");
        string dir=Path.Combine(Path.GetTempPath(),"myTinyTask-updates-"+Guid.NewGuid().ToString("N")),path=Path.Combine(dir,"updates.json");
        try{var prefs=new UpdatePreferences{CheckOnStartup=false,SkippedVersion="2.0.0"};prefs.Save(path);var loaded=UpdatePreferences.Load(path);if(loaded.CheckOnStartup||loaded.SkippedVersion!="2.0.0")throw new Exception("Update preferences failed.");prefs.CheckOnStartup=true;prefs.Save(path);if(!UpdatePreferences.Load(path).CheckOnStartup)throw new Exception("Update preferences replacement failed.");}finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
internal class UpdateDialog : Form {
    public UpdateDialog(ReleaseInfo release) {
        Text="Update available";AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(540,395);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;Font=new Font("Segoe UI",9);BackColor=Theme.Canvas;
        Controls.Add(new Label{Text="myTinyTask "+release.Number+" is available",Location=new Point(18,16),AutoSize=true,Font=new Font("Segoe UI",13,FontStyle.Bold)});
        Controls.Add(new Label{Text="Installed: "+AppVersion.Current+"   •   What's new",Location=new Point(18,49),AutoSize=true});
        Controls.Add(new TextBox{Text=string.IsNullOrWhiteSpace(release.body)?"No release notes provided.":release.body.Replace("\r\n","\n").Replace("\n",Environment.NewLine),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Location=new Point(18,76),Size=new Size(504,229),BackColor=Color.White});
        Controls.Add(new Label{Text="Download opens GitHub. Save your work and close the app before\nreplacing the EXE. Keep your recordings and settings files.",Location=new Point(18,314),Size=new Size(504,34)});
        var skip=new Button{Text="Skip this version",Location=new Point(18,355),Size=new Size(126,28),DialogResult=DialogResult.Ignore};Controls.Add(skip);
        var later=new Button{Text="Later",Location=new Point(290,355),Size=new Size(80,28),DialogResult=DialogResult.Cancel};Controls.Add(later);CancelButton=later;
        var download=new Button{Text="Download update",Location=new Point(381,355),Size=new Size(141,28),DialogResult=DialogResult.Yes};Controls.Add(download);AcceptButton=download;
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
internal enum Tone { Ready, Recording, Active, Warning }
internal enum ButtonStyle { Secondary, Primary, Danger }

internal static class Theme {
    public static readonly Color Canvas=Color.FromArgb(248,248,248),FooterFill=Color.FromArgb(240,240,240),Card=Color.White,Line=Color.FromArgb(224,224,224),Border=Color.FromArgb(196,196,196),
        Text=Color.FromArgb(28,28,28),Muted=Color.FromArgb(94,94,94),Disabled=Color.FromArgb(150,150,150),
        Accent=Color.FromArgb(0,95,184),AccentHover=Color.FromArgb(0,82,160),AccentDown=Color.FromArgb(0,68,135),
        Danger=Color.FromArgb(196,43,28),DangerHover=Color.FromArgb(172,36,23),DangerDown=Color.FromArgb(148,30,20),
        Ready=Color.FromArgb(16,124,16),Warning=Color.FromArgb(157,93,0);
    static readonly float scale=DetectScale();
    static float DetectScale() { using(var g=Graphics.FromHwnd(IntPtr.Zero))return g.DpiX/96f; }
    public static int S(int value) { return (int)Math.Round(value*scale); }
    public static Color ToneColor(Tone tone) { return tone==Tone.Recording?Danger:tone==Tone.Active?Accent:tone==Tone.Warning?Warning:Ready; }
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
            bool danger=style==ButtonStyle.Danger;
            fill=down?(danger?Theme.DangerDown:Theme.AccentDown):hot?(danger?Theme.DangerHover:Theme.AccentHover):(danger?Theme.Danger:Theme.Accent);
            border=fill;fore=Color.White;keyFill=Color.FromArgb(38,255,255,255);keyBorder=Color.FromArgb(120,255,255,255);keyFore=Color.White;
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
        var g=e.Graphics;g.Clear(Parent==null?Theme.Canvas:Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        int dot=Theme.S(8);
        using(var brush=new SolidBrush(Theme.ToneColor(tone)))g.FillEllipse(brush,Theme.S(2),(Height-dot)/2,dot,dot);
        int x=dot+Theme.S(10);
        int headWidth=TextRenderer.MeasureText(headline,headlineFont,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine).Width+Theme.S(2);
        TextRenderer.DrawText(g,headline,headlineFont,new Rectangle(x,0,Math.Min(headWidth,Width-x),Height),Theme.Text,Theme.Line1);
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
        Text="About myTinyTask";ClientSize=new Size(340,164);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;ShowIcon=false;
        StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",9);BackColor=Theme.Canvas;ForeColor=Theme.Text;
        if(appIcon!=null)try{Controls.Add(new PictureBox{Image=new Icon(appIcon,new Size(48,48)).ToBitmap(),SizeMode=PictureBoxSizeMode.Zoom,Location=new Point(20,22),Size=new Size(48,48)});}catch{}
        Controls.Add(new Label{Text="myTinyTask",Font=new Font("Segoe UI Semibold",12f),AutoSize=true,Location=new Point(84,20)});
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
    public int Version {get;set;}
    public long Duration {get;set;}
    public List<MacroEvent> Events {get;set;}
    public Recording() { Version=1; Events=new List<MacroEvent>(); }
    public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength=32000000 }; }
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
    readonly string updatePreferencesPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"myTinyTask","updates.json");
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
    public MainForm() : this(false) {}
    internal MainForm(bool testMode) {
        library=new RecordingLibrary(Path.ChangeExtension(Assembly.GetExecutingAssembly().Location,"recordings.json"),AppDomain.CurrentDomain.BaseDirectory);
        string settingsWarning=null;
        if(!testMode)try{hotkeys=HotkeySettings.Load(settingsPath);}catch{settingsWarning="Shortcut settings could not be read. Default keys are active.";}
        SuspendLayout();
        AutoScaleDimensions=new SizeF(96F,96F);AutoScaleMode=AutoScaleMode.Dpi;
        Text="myTinyTask v"+AppVersion.Current; ClientSize=new Size(400,128); FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false;
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
        keysItem=MenuItem("Customize keys…",()=>CustomizeKeys());
        menu.Items.Add(new ToolStripSeparator());
        MenuItem("About myTinyTask…",()=>ShowAbout());
        // Exported macros are standalone tasks, not installations to replace with the recorder.
        if(!testMode && Assembly.GetExecutingAssembly().GetManifestResourceInfo("macro.mtt")==null) {
            updatePreferences=UpdatePreferences.Load(updatePreferencesPath);
            updateItem=MenuItem("Check for updates…",()=>CheckUpdates(true));
            automaticUpdatesItem=new ToolStripMenuItem("Check for updates on startup"){CheckOnClick=true,Checked=updatePreferences.CheckOnStartup};menu.Items.Add(automaticUpdatesItem);
            automaticUpdatesItem.CheckedChanged+=(s,e)=>Guard(()=>{updatePreferences.CheckOnStartup=automaticUpdatesItem.Checked;if(!automaticUpdatesItem.Checked)availableUpdate=null;updatePreferences.Save(updatePreferencesPath);});
            Shown+=(s,e)=>{if(updatePreferences.CheckOnStartup)CheckUpdates(false);};
        }
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
        FormClosing+=(s,e)=> { Stop(); if(!ConfirmDiscard()) {e.Cancel=true;return;} timer.Stop();Native.UnhookWindowsHookEx(keyHook);Native.UnhookWindowsHookEx(mouseHook); };
        using(var embedded=Assembly.GetExecutingAssembly().GetManifestResourceStream("macro.mtt")) {if(embedded!=null)using(var reader=new StreamReader(embedded)){macro=Recording.Serializer().Deserialize<Recording>(reader.ReadToEnd());macro.Validate();fileName="Embedded recording";}}
        UpdateControls();
        SetStatus("Ready",Tone.Ready,Summary());
        if(settingsWarning!=null)SetStatus("Ready",Tone.Warning,"Settings unreadable; default keys active");
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
            Native.Send(new MacroEvent{Message=0x201,X=titleX,Y=titleY});PumpMessages(80);
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
    void Guard(Action action) {try { action(); } catch(Exception ex){Stop();MessageBox.Show(this,ex.Message,"myTinyTask",MessageBoxButtons.OK,MessageBoxIcon.Error);} }
    bool ConfirmDiscard() { if(!dirty)return true; var result=MessageBox.Show(this,"Save this recording before continuing?","Unsaved recording",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);if(result==DialogResult.Cancel)return false;if(result==DialogResult.Yes){SaveMacro();return !dirty;}return true; }
    void UpdateControls() {
        bool busy=recording||playing||pending;
        if(updateItem!=null)updateItem.Enabled=!busy&&!updateChecking;
        savedItem.Enabled=openItem.Enabled=saveItem.Enabled=exportItem.Enabled=keysItem.Enabled=loop.Enabled=!busy;repeats.Enabled=!busy&&!loop.Checked;
        speedMode.Enabled=intervalEnabled.Enabled=!busy;speed.Enabled=!busy&&speedMode.Checked;intervalValue.Enabled=intervalUnit.Enabled=!busy&&intervalEnabled.Checked;
        play.Enabled=!recording&&macro.Events.Count>0;record.Enabled=!playing&&!pending;stop.Enabled=busy;
        record.Text=recording?"Finish":"Record";record.Style=recording?ButtonStyle.Danger:ButtonStyle.Primary;
        record.Hint=HotkeySettings.Name(hotkeys.Record);play.Hint=HotkeySettings.Name(hotkeys.Play);stop.Hint=HotkeySettings.Name(hotkeys.Stop);
        tips.SetToolTip(record,(recording?"Finish recording (":"Record (")+HotkeySettings.Name(hotkeys.Record)+")");tips.SetToolTip(play,"Play / stop playback ("+HotkeySettings.Name(hotkeys.Play)+")");tips.SetToolTip(stop,"Emergency stop ("+HotkeySettings.Name(hotkeys.Stop)+")");
        Text="myTinyTask v"+AppVersion.Current+" — "+fileName+(dirty?" *":"");
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
        position=0;iteration=0;UpdateLoopCount();pending=true;watch.Restart();SetStatus("Starting in 2 s",Tone.Active,"Switch to your target window");UpdateControls();
    }
    void Stop() {
        if(recording){macro.Duration=Math.Min(86400000,watch.ElapsedMilliseconds);recording=false;}
        pending=playing=waitingForNext=false;watch.Stop();ReleaseHeld();SetStatus("Ready",Tone.Ready,Summary());UpdateControls();
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
            } else if(manual)MessageBox.Show(this,release==null?"No downloadable stable release is available yet.":"You are up to date (v"+AppVersion.Current+").","myTinyTask updates");
        }catch(Exception){if(manual&&!IsDisposed&&!Disposing)MessageBox.Show(this,"Could not check GitHub for updates. Check your internet connection and try again later.","myTinyTask updates",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        finally{updateChecking=false;if(!IsDisposed&&!Disposing)UpdateControls();}
    }
    void OfferUpdateIfIdle() {
        if(availableUpdate==null||recording||playing||pending||editingHotkeys||OwnedForms.Length>0||menu.Visible||Native.GetForegroundWindow()!=Handle)return;
        var release=availableUpdate;availableUpdate=null;editingHotkeys=true;
        try {using(var dialog=new UpdateDialog(release)) {
            var result=dialog.ShowDialog(this);
            if(result==DialogResult.Ignore){updatePreferences.SkippedVersion=release.Number.ToString();updatePreferences.Save(updatePreferencesPath);}
            else if(result==DialogResult.Yes)Process.Start(new ProcessStartInfo(release.Page){UseShellExecute=true});
        }}finally{editingHotkeys=false;}
    }
    void Tick() {
        if(availableUpdate!=null)Guard(OfferUpdateIfIdle);
        if(recording) {card.Info=Count(macro.Events.Count,"event")+"  ·  "+(watch.ElapsedMilliseconds/1000.0).ToString("0.0")+" s";if(watch.ElapsedMilliseconds>=86400000 || macro.Events.Count>=500000)Stop();return;}
        if(pending)card.Headline="Starting in "+Math.Max(1,(int)Math.Ceiling((2000-watch.ElapsedMilliseconds)/1000.0))+" s";
        if(pending && watch.ElapsedMilliseconds>=2000) {pending=false;playing=true;watch.Restart();}
        if(!playing)return;
        Guard(()=>AdvancePlayback(watch.Elapsed.TotalMilliseconds,Native.Send));
    }
    void AdvancePlayback(double elapsed,Action<MacroEvent> send) {
        if(waitingForNext) {
            if(elapsed>=runInterval){waitingForNext=false;position=0;watch.Restart();}
            else {card.Headline="Waiting";card.Tone=Tone.Active;card.Info="Next run in "+TimeSpan.FromMilliseconds(runInterval-elapsed).ToString(@"hh\:mm\:ss");}
            return;
        }
        int batch=0;
        while(position<macro.Events.Count && macro.Events[position].Time/playbackSpeed<=elapsed && batch++<100) {var e=macro.Events[position++];send(e);Track(e);}
        card.Headline="Playing";card.Tone=Tone.Active;card.Info="Pass "+(iteration+1)+(forever?" (looping)":" / "+repeatCount)+"  ·  "+position+" / "+macro.Events.Count+" events";
        if(position==macro.Events.Count && elapsed>=Math.Max(20,macro.Duration/playbackSpeed)) {
            ReleaseHeld();iteration++;UpdateLoopCount();
            if(!forever&&iteration>=repeatCount)Stop();
            else if(runInterval>elapsed){waitingForNext=true;}
            else {position=0;watch.Restart();}
        }
    }
    void UpdateLoopCount() {completedLoops.Text="Completed loops: "+iteration.ToString("N0");}
    internal void TestIntervals() {
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
    bool HandleShortcut(uint key,bool down) {
        // Always consume the release/repeat of a previously swallowed press, even if a dialog opened.
        if(shortcutHeld.Contains(key)){if(!down)shortcutHeld.Remove(key);return true;}
        if(editingHotkeys || !down)return false;
        int action=hotkeys.ActionFor(key);if(action==0)return false;
        shortcutHeld.Add(key);
        BeginInvoke(new Action(()=>Guard(()=>{if(editingHotkeys)return;if(action==1)ToggleRecord();else if(action==2)StartPlayback();else Stop();})));
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
        hotkeys=new HotkeySettings();UpdateControls();
        if(hotkeys.ActionFor((uint)Keys.F8)!=1||hotkeys.ActionFor((uint)Keys.F9)!=2||hotkeys.ActionFor((uint)Keys.F12)!=3||record.Hint!="F8"||play.Hint!="F9"||stop.Hint!="F12")throw new Exception("Restore default keys failed.");
    }
    // A second launch broadcasts this message so the running window comes to the front instead of opening another copy.
    internal static readonly uint ActivateMessage=Native.RegisterWindowMessage("myTinyTask.Activate");
    protected override void WndProc(ref Message m) {
        if(m.Msg==(int)ActivateMessage){if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;Activate();return;}
        base.WndProc(ref m);
    }
    IntPtr Keyboard(int code,IntPtr message,IntPtr data) {
        if(code>=0){var k=(Native.KeyHook)Marshal.PtrToStructure(data,typeof(Native.KeyHook));int m=message.ToInt32();
            if((k.Flags&0x10)==0) {
                bool down=m==0x100||m==0x104;
                if(HandleShortcut(k.Vk,down))return new IntPtr(1);
                if(recording && Native.GetForegroundWindow()!=Handle) macro.Events.Add(new MacroEvent{Time=watch.ElapsedMilliseconds,Message=m,Data=k.Vk,Scan=k.Scan,Flags=k.Flags});
            }
        }
        return Native.CallNextHookEx(keyHook,code,message,data);
    }
    IntPtr Mouse(int code,IntPtr message,IntPtr data) {
        if(code>=0&&recording){var m=(Native.MouseHook)Marshal.PtrToStructure(data,typeof(Native.MouseHook));int msg=message.ToInt32();long now=watch.ElapsedMilliseconds;
            if((m.Flags&1)==0 && (WindowState==FormWindowState.Minimized || !Bounds.Contains(m.Point.X,m.Point.Y)) && (msg!=0x200||now-lastMove>=8)) {
                if(msg==0x200)lastMove=now;
                uint value=(msg==0x20A||msg==0x20E)?unchecked((uint)(int)(short)(m.Data>>16)):(m.Data>>16);
                macro.Events.Add(new MacroEvent{Time=now,Message=msg,X=m.Point.X,Y=m.Point.Y,Data=value});
            }
        }
        return Native.CallNextHookEx(mouseHook,code,message,data);
    }
    void ExportExe() {
        if(macro.Events.Count==0)throw new Exception("Record or open a macro first.");
        macro.Validate();
        using(var d=new SaveFileDialog{Filter="Windows application (*.exe)|*.exe",FileName="MyMacro.exe"}) {
            if(d.ShowDialog(this)!=DialogResult.OK)return;
            if(string.Equals(Path.GetFullPath(d.FileName),Assembly.GetExecutingAssembly().Location,StringComparison.OrdinalIgnoreCase))throw new Exception("Choose a different name from the running app.");
            BuildExport(d.FileName);
            SetStatus("Exported",Tone.Ready,"Open the EXE and press Play");
        }
    }
    internal void BuildExport(string outputPath) {
            string temp=Path.Combine(Path.GetTempPath(),"myTinyTask-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
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
        using(var target=new Form{Text="myTinyTask playback test",Size=new Size(320,150),StartPosition=FormStartPosition.CenterScreen}) {
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
        using(var d=new SaveFileDialog{Filter="myTinyTask recording (*.mtt)|*.mtt",DefaultExt="mtt",FileName=fileName=="Untitled"?"My recording.mtt":fileName}) {
            if(d.ShowDialog(this)!=DialogResult.OK)return;macro.Validate();string temp=d.FileName+".tmp";File.WriteAllText(temp,Recording.Serializer().Serialize(macro));if(File.Exists(d.FileName))File.Replace(temp,d.FileName,null);else File.Move(temp,d.FileName);fileName=Path.GetFileName(d.FileName);selectedPath=Path.GetFullPath(d.FileName);dirty=false;UpdateControls();RememberRecording(selectedPath);
        }
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
        if(new FileInfo(path).Length>32000000)throw new Exception("Recording is too large.");
        var loaded=Recording.Serializer().Deserialize<Recording>(File.ReadAllText(path));if(loaded==null)throw new Exception("Empty recording.");loaded.Validate();
        if(!ConfirmDiscard())return;
        macro=loaded;selectedPath=Path.GetFullPath(path);fileName=Path.GetFileName(path);dirty=false;Stop();RememberRecording(path);
    }
    void OpenMacro() {using(var d=new OpenFileDialog{Filter="myTinyTask recording (*.mtt)|*.mtt"}){if(d.ShowDialog(this)==DialogResult.OK)LoadRecording(d.FileName);} }
    internal void TestLibrary() {
        string folder=Path.Combine(Path.GetTempPath(),"myTinyTask-library-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
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
        if(args.Length>0&&(args[0]=="--self-test"||args[0]=="--hotkey-test")) {try {SelfTest(args[0]=="--self-test");File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),(args[0]=="--hotkey-test"?"PASS (shortcut tests; live playback not run): ":"PASS: ")+"update versions, skip/manual override, prerelease filtering and update preferences, exclusive playback modes, completed-loop display, reset and retention, interval timing, repeat completion, long recording non-overlap, stop while waiting, saved recordings discovery, persistence, deduplication, selection, missing and invalid files, serialization, timing validation, invalid key rejection, replacement shortcuts, restored defaults, active key labels, menu toggle, held-key repeat suppression, independent emergency stop, settings persistence, duplicate shortcut validation, input ABI, key conversion, mouse conversion, hook install/uninstall, UI lifecycle, standalone EXE compilation, app and exported EXE version metadata.");Environment.ExitCode=0;}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),"FAIL: "+ex);Environment.ExitCode=1;}return;}
        if(args.Length>0&&args[0]=="--check-update-test") {try{var release=UpdateService.Fetch();File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-test-results.txt"),release==null?"PASS: no published downloadable release": "PASS: GitHub release "+release.tag_name+"; offer="+UpdateService.ShouldOffer(release,AppVersion.Current,null,false));}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-test-results.txt"),"FAIL: "+ex.Message);Environment.ExitCode=1;}return;}
        // One recorder at a time: two copies would both react to the same global shortcuts.
        bool first;
        using(var instance=new System.Threading.Mutex(true,"Local\\myTinyTask.SingleInstance",out first)) {
            if(!first){Native.AllowSetForegroundWindow(-1);Native.PostMessage((IntPtr)0xFFFF,MainForm.ActivateMessage,IntPtr.Zero,IntPtr.Zero);return;}
            Application.Run(new MainForm());
        }
    }
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    static void SelfTest(bool livePlayback) {
        UpdateService.Test();
        using(var dialog=new UpdateDialog(new ReleaseInfo{tag_name="v2.0.0",body="Example release notes\n- New feature\n- Bug fix"})) {
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

        var r=new Recording{Duration=100};r.Events.Add(new MacroEvent{Time=10,Message=0x100,Data=65,Scan=30});r.Events.Add(new MacroEvent{Time=90,Message=0x101,Data=65,Scan=30});r.Validate();
        var copy=Recording.Serializer().Deserialize<Recording>(Recording.Serializer().Serialize(r));copy.Validate();Assert(copy.Events.Count==2&&copy.Events[1].Time==90,"Round trip");
        copy.Events[1].Time=1;bool rejected=false;try{copy.Validate();}catch{rejected=true;}Assert(rejected,"Reject unordered events");copy.Events[1].Time=90;copy.Events[1].Data=256;rejected=false;try{copy.Validate();}catch{rejected=true;}Assert(rejected,"Reject invalid key");
        Assert(Marshal.SizeOf(typeof(Native.Input))==(IntPtr.Size==8?40:28),"Native INPUT ABI");var k=Native.Convert(r.Events[1]);Assert(k.Type==1&&k.Value.Key.Scan==30&&k.Value.Key.Flags==10,"Keyboard conversion");
        var m=Native.Convert(new MacroEvent{Message=0x20A,Data=unchecked((uint)-120)});Assert(m.Value.Mouse.Flags==0xC801&&m.Value.Mouse.Data==unchecked((uint)-120),"Wheel conversion");
        Native.Hook callback=(c,w,l)=>Native.CallNextHookEx(IntPtr.Zero,c,w,l);var kh=Native.SetWindowsHookEx(13,callback,Native.GetModuleHandle(null),0);var mh=Native.SetWindowsHookEx(14,callback,Native.GetModuleHandle(null),0);Assert(kh!=IntPtr.Zero&&mh!=IntPtr.Zero,"Install hooks");Native.UnhookWindowsHookEx(kh);Native.UnhookWindowsHookEx(mh);GC.KeepAlive(callback);
        using(var f=new MainForm(true)){f.Show();Application.DoEvents();Assert(f.Visible,"Window visible");f.TestHotkeys();f.TestMenu(livePlayback);f.TestLibrary();f.TestIntervals();if(livePlayback)f.TestPlayback();
            string exported=Path.Combine(Path.GetTempPath(),"myTinyTask-test-"+Guid.NewGuid().ToString("N")+".exe");
            try {f.BuildExport(exported);Assert(new FileInfo(exported).Length>10000,"Export executable");Assert(FileVersionInfo.GetVersionInfo(exported).ProductVersion==AppVersion.Current,"Export version metadata");}finally{if(File.Exists(exported))File.Delete(exported);}
            using(var bitmap=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bitmap,new Rectangle(0,0,f.Width,f.Height));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview.png"));}
            using(var dialog=new HotkeyDialog(new HotkeySettings())){dialog.Show(f);Application.DoEvents();using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"keys-preview.png"));}dialog.Close();}
            f.Close();}
    }
}
