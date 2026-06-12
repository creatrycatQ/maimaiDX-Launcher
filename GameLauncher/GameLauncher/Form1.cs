using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace GameLauncher;

public partial class Form1 : Form
{
    // ---- 注册表 ----
    const string RK = @"SOFTWARE\GameLauncher";
    const string RGP = "GamePath", RES = "EnglishSubtitle", RLB = "LargeBgPath";

    WebView2 _webView = null!;
    string _bgFolder = null!;
    Process? _sinmaiProcess;

    // Win32: 文件对话框需要窗口句柄
    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    public Form1()
    {
        InitializeComponent();
        _bgFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "backgrounds");
        Directory.CreateDirectory(_bgFolder);
        BuildUI();
    }

    async void BuildUI()
    {
        Size = new Size(1280, 720);
        MinimumSize = MaximumSize = new Size(1280, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        Text = "MaimaiDX Launcher";

        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);

        await _webView.EnsureCoreWebView2Async(null);

        // 虚拟主机：背景文件
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "bg.local", _bgFolder, CoreWebView2HostResourceAccessKind.Allow);

        // 注册 JS → C# 桥接
        _webView.CoreWebView2.WebMessageReceived += OnWebMessage;

        // 加载 HTML UI
        _webView.CoreWebView2.NavigateToString(GetHtml());
    }
    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = e.TryGetWebMessageAsString();
        if (string.IsNullOrWhiteSpace(msg)) return;

        // 简单协议: "action|payload"
        var parts = msg.Split('|', 2);
        var action = parts[0];
        var payload = parts.Length > 1 ? parts[1] : "";

        switch (action)
        {
            case "launch":
                DoLaunch();
                break;
            case "browseGame":
                BrowseGamePath();
                break;
            case "browseBg":
                BrowseBgFile();
                break;
            case "saveReg":
                SaveRegFromJS(payload);
                break;
            case "getState":
                _ = SendState();
                break;
            case "setWindowTitle":
                Text = string.IsNullOrWhiteSpace(payload) ? "MaimaiDX Launcher" : "MaimaiDX - " + payload;
                break;
            case "openSinmaiEditor":
                OpenSinmaiEditor();
                break;
            case "resetGamePath":
                ResetGamePath();
                break;
            case "minimize":
                WindowState = FormWindowState.Minimized;
                break;
            case "close":
                Close();
                break;
            case "drag":
                DragWindow();
                break;
        }
    }

    async Task SendState()
    {
        var gp = GetReg(RGP) ?? "";
        var es = GetReg(RES) ?? "MaimaiDX Launcher";
        var lbg = GetReg(RLB) ?? "";
        var hasPath = !string.IsNullOrWhiteSpace(gp) && File.Exists(gp);
        var js = $"setState('{EscapeJs(gp)}','{EscapeJs(es)}','{EscapeJs(lbg)}',{hasPath.ToString().ToLower()})";
        await _webView.CoreWebView2.ExecuteScriptAsync(js);
    }

    async void DoLaunch()
    {
        var p = GetReg(RGP);
        if (string.IsNullOrWhiteSpace(p) || !File.Exists(p))
        {
            _ = _webView.CoreWebView2.ExecuteScriptAsync("showStatus('请先设置游戏路径','error')");
            _ = _webView.CoreWebView2.ExecuteScriptAsync("setLaunchReady()");
            return;
        }

        ProgressOverlay? overlay = null;
        try
        {
            // 禁用启动按钮
            _ = _webView.CoreWebView2.ExecuteScriptAsync("setLaunching()");

            // 1. 延迟 3 秒
            await Task.Delay(3000);
            if (IsDisposed || Disposing) return;

            // 2. 桌面右下角显示进度条，跑 3 秒
            var es = GetReg(RES) ?? "";
            overlay = new ProgressOverlay(es);
            await overlay.RunProgressAsync(3000);
            if (IsDisposed || Disposing) { overlay.Close(); return; }

            // 3. 进度条跑完后停留 1 秒
            await Task.Delay(1000);
            if (IsDisposed || Disposing) { overlay.Close(); return; }

            // 4. 启动游戏
            Process.Start(new ProcessStartInfo(p)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(p) ?? ""
            });

            // 5. 进度条淡出，然后关闭启动器窗口
            await overlay.FadeOutAsync();
            overlay.Close();
            Close();
        }
        catch (Exception ex)
        {
            overlay?.Close();
            _ = _webView.CoreWebView2.ExecuteScriptAsync(
                "showStatus('启动失败: " + EscapeJs(ex.Message) + "','error')");
            _ = _webView.CoreWebView2.ExecuteScriptAsync("setLaunchReady()");
        }
    }

    void OpenSinmaiEditor()
    {
        // 已有实例在运行则不再启动
        if (_sinmaiProcess != null && !_sinmaiProcess.HasExited)
        {
            _ = _webView.CoreWebView2.ExecuteScriptAsync("showStatus('Sinmai-Assist编辑器已在运行中','success')");
            return;
        }

        var editorDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "Sinmai-Assist");
        var scriptPath = Path.Combine(editorDir, "config_editor_gui.py");
        try
        {
            if (File.Exists(scriptPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pythonw",
                    Arguments = $"\"{scriptPath}\"",
                    WorkingDirectory = editorDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                _sinmaiProcess = Process.Start(psi);
                _ = _webView.CoreWebView2.ExecuteScriptAsync("showStatus('Sinmai-Assist编辑器已启动','success')");
            }
            else
            {
                _ = _webView.CoreWebView2.ExecuteScriptAsync("showStatus('未找到Sinmai-Assist编辑器','error')");
            }
        }
        catch (Exception ex)
        {
            _ = _webView.CoreWebView2.ExecuteScriptAsync("showStatus('启动失败: " + EscapeJs(ex.Message) + "','error')");
        }
    }

    async void ResetGamePath()
    {
        SaveReg(RGP, "");
        await SendState();
    }

    void BrowseGamePath()
    {
        var d = new OpenFileDialog { Title = "选择启动程序", Filter = "可执行文件|*.exe;*.bat|所有文件|*.*", RestoreDirectory = true };
        var cur = GetReg(RGP);
        if (!string.IsNullOrWhiteSpace(cur) && File.Exists(cur)) d.InitialDirectory = Path.GetDirectoryName(cur);
        if (d.ShowDialog() == DialogResult.OK)
        {
            SaveReg(RGP, d.FileName);
            _ = SendState();
            _ = _webView.CoreWebView2.ExecuteScriptAsync("showStatus('路径已保存','success')");
        }
    }

    async void BrowseBgFile()
    {
        var d = new OpenFileDialog
        {
            Title = "选择背景图片或视频",
            Filter = "图片和视频|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4;*.webm;*.avi|图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|视频|*.mp4;*.webm;*.avi|所有文件|*.*",
            RestoreDirectory = true
        };
        if (d.ShowDialog() != DialogResult.OK) return;

        string src = d.FileName;
        string ext = Path.GetExtension(src).ToLower();
        string localPath = Path.Combine(_bgFolder, "bg_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ext);
        try { File.Copy(src, localPath, true); } catch { localPath = src; }

        SaveReg(RLB, localPath);
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"setBgFile('{EscapeJs(localPath)}','{EscapeJs(ext)}')");
        _ = _webView.CoreWebView2.ExecuteScriptAsync("showStatus('背景已更新','success')");
    }

    void SaveRegFromJS(string payload)
    {
        // payload: "key=value"
        var kv = payload.Split('=', 2);
        if (kv.Length == 2) SaveReg(kv[0], kv[1]);
    }

    static string? GetReg(string name) { try { using var k = Registry.CurrentUser.OpenSubKey(RK); return k?.GetValue(name) as string; } catch { return null; } }
    static void SaveReg(string name, string val) { try { using var k = Registry.CurrentUser.CreateSubKey(RK); k.SetValue(name, val); } catch { } }
    static string EscapeJs(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "");

    static string GetHtml() => @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=1280,height=720"">
<style>
*{margin:0;padding:0;box-sizing:border-box}
body{font-family:'Microsoft YaHei UI','Segoe UI',sans-serif;width:1280px;height:720px;overflow:hidden;background:#000;user-select:none;-webkit-user-select:none}

/* 视频背景 */
#bg{position:fixed;top:0;left:0;width:100%;height:100%;z-index:0;pointer-events:none}
#bgVideo{width:100vw;height:100vh;object-fit:cover;position:fixed;top:0;left:0}
#bgImage{width:100vw;height:100vh;object-fit:cover;position:fixed;top:0;left:0}

/* 遮罩层 */
#overlay{position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(10,10,30,0.35);z-index:1;pointer-events:none}

/* 主界面 */
#main{position:fixed;top:0;left:0;width:100%;height:100%;z-index:2}
#title{position:absolute;top:30px;left:30px;font-size:36px;font-weight:bold;color:#fff;text-shadow:0 2px 12px rgba(0,0,0,0.6)}
#subtitle{position:absolute;top:78px;left:34px;font-size:14px;font-style:italic;color:rgba(255,255,255,0.72);text-shadow:0 1px 4px rgba(0,0,0,0.5)}

/* 按钮 */
#btnArea{position:absolute;bottom:40px;right:40px;display:flex;align-items:center;gap:16px}
#statusMsg{position:absolute;bottom:125px;right:56px;font-size:10px;color:#fff;text-shadow:0 1px 4px rgba(0,0,0,0.6);max-width:300px;text-align:right;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
#subMsg{position:absolute;bottom:142px;right:56px;font-size:14px;font-style:italic;color:rgba(255,255,255,0.72);text-shadow:0 1px 4px rgba(0,0,0,0.5);max-width:300px;text-align:right}

.btn{display:flex;align-items:center;justify-content:center;cursor:pointer;transition:all 0.2s ease;position:relative;overflow:hidden}
.btn:active{transform:scale(0.96)}

/* 设置按钮 - 圆形 */
#btnSettings{width:48px;height:48px;border-radius:50%;background:rgba(30,30,36,0.85);border:none;color:#fff;font-size:20px;backdrop-filter:blur(8px);-webkit-backdrop-filter:blur(8px)}
#btnSettings:hover{background:rgba(60,60,70,0.9)}

/* 启动按钮 - 胶囊形 */
#btnLaunch{width:240px;height:72px;border-radius:36px;font-size:18px;font-weight:bold;color:#fff;border:none;text-shadow:0 1px 3px rgba(0,0,0,0.3)}
#btnLaunch.enabled{background:linear-gradient(180deg,#ffb84d,#ff8c28);box-shadow:0 4px 20px rgba(255,150,30,0.4)}
#btnLaunch.enabled:hover{background:linear-gradient(180deg,#ffc664,#ff9b37);box-shadow:0 6px 24px rgba(255,150,30,0.5)}
#btnLaunch.disabled{background:transparent;border:2px solid rgba(180,180,200,0.45);color:rgba(180,180,200,0.6);cursor:default}
#btnLaunch.disabled:hover{background:transparent}

/* 设置面板 */
#settingsPanel{position:fixed;top:0;left:0;width:100%;height:100%;z-index:10;background:rgba(14,14,32,0.96);display:none;flex-direction:column;padding:60px 100px;overflow-y:auto;backdrop-filter:blur(12px);-webkit-backdrop-filter:blur(12px)}
#settingsPanel.show{display:flex}
#settingsTitle{font-size:22px;font-weight:bold;color:#fff;margin-bottom:40px}
.section{margin-bottom:28px}
.section label{display:block;font-size:11px;color:rgba(220,220,230,0.85);margin-bottom:6px}
.section .row{display:flex;gap:10px;align-items:center}
.section input[type=""text""]{flex:1;max-width:600px;height:36px;background:#22223a;border:none;color:#fff;padding:0 14px;font-size:11px;border-radius:4px;outline:none;font-family:inherit}
.section input[type=""text""]:focus{outline:1px solid rgba(255,184,77,0.5)}
.section input[type=""text""]:read-only{color:rgba(200,200,220,0.7)}
.btnGold{padding:0 22px;height:36px;border-radius:18px;border:none;background:linear-gradient(180deg,#ffb84d,#ff8c28);color:#fff;font-size:11px;font-weight:bold;cursor:pointer;transition:all 0.2s;white-space:nowrap;font-family:inherit}
.btnGold:hover{background:linear-gradient(180deg,#ffc664,#ff9b37);box-shadow:0 2px 12px rgba(255,150,30,0.35)}
.btnBack{padding:0 28px;height:38px;border-radius:19px;border:none;background:rgba(55,55,78,0.9);color:#fff;font-size:11px;cursor:pointer;transition:all 0.2s;position:absolute;bottom:25px;left:30px;font-family:inherit}
.btnBack:hover{background:rgba(75,75,100,0.9)}
#copyright{position:absolute;bottom:25px;right:32px;font-size:10px;color:rgba(255,255,255,0.35);text-align:right;line-height:1.5}

/* 状态提示 */
#toast{position:fixed;bottom:30px;left:50%;transform:translateX(-50%);z-index:20;padding:10px 24px;border-radius:20px;font-size:11px;color:#fff;opacity:0;transition:opacity 0.3s;pointer-events:none}
#toast.error{background:rgba(255,60,60,0.85)}
#toast.success{background:rgba(40,200,100,0.85)}

/* 窗口控制按钮 */
#winControls{position:fixed;top:0;right:0;z-index:15;display:flex;height:32px}
.winBtn{width:46px;height:32px;display:flex;align-items:center;justify-content:center;cursor:pointer;background:transparent;border:none;color:rgba(255,255,255,0.8);font-size:16px;transition:all 0.2s;font-family:'Microsoft YaHei UI','Segoe UI',sans-serif}
.winBtn:hover{background:rgba(255,255,255,0.1)}
#btnClose:hover{background:#e81123;color:#fff}
/* 拖拽区域 */
#dragHandle{position:fixed;top:0;left:0;width:100%;height:45px;z-index:8;cursor:default}
</style>
</head>
<body>

<div id=""bg"">
  <video id=""bgVideo"" autoplay loop muted playsinline style=""display:none""></video>
  <img id=""bgImage"" style=""display:none"">
</div>
<div id=""overlay""></div>
<div id=""main"">
  <div id=""dragHandle"" onmousedown=""dragWindow()""></div>
  <div id=""winControls"">
    <button class=""winBtn"" onclick=""minimizeWindow()"" title=""最小化"">&#x2014;</button>
    <button class=""winBtn"" id=""btnClose"" onclick=""closeWindow()"" title=""关闭"">&#x2715;</button>
  </div>
  <div id=""title"" onmousedown=""dragWindow()"">MaimaiDX 启动器</div>
  <div id=""subMsg"">MaimaiDX Launcher</div>
  <div id=""statusMsg"">请先设置游戏路径</div>
  <div id=""btnArea"">
    <button class=""btn"" id=""btnSettings"" onclick=""openSettings()"">&#9881;</button>
    <button class=""btn disabled"" id=""btnLaunch"" onclick=""launchGame()"">开始游戏</button>
  </div>
</div>
<div id=""settingsPanel"">
  <div id=""settingsTitle"">设置</div>
  <div class=""section"">
    <label>游戏启动路径:</label>
    <div class=""row"">
      <input type=""text"" id=""tbPath"" readonly placeholder=""请选择游戏启动程序..."">
      <button class=""btnGold"" onclick=""browseGame()"">📂 选择路径</button>
      <button class=""btnGold"" onclick=""resetGamePath()"" style=""background:rgba(120,120,140,0.7);font-weight:normal"">↺ 重置</button>
    </div>
  </div>
  <div class=""section"">
    <label>英文副标题:</label>
    <div class=""row"">
      <input type=""text"" id=""tbEngSub"" placeholder=""MaimaiDX Launcher"">
    </div>
  </div>
  <div class=""section"">
    <label>背景图/视频 (图片或MP4):</label>
    <div class=""row"">
      <input type=""text"" id=""tbBg"" readonly placeholder=""选择图片或MP4视频..."">
      <button class=""btnGold"" onclick=""browseBg()"">📂 选择文件</button>
    </div>
  </div>
  <div class=""section"">
    <label>Sinmai-Assist 配置工具:</label>
    <div class=""row"">
      <button class=""btnGold"" onclick=""openSinmaiEditor()"">🔧 启动配置编辑器</button>
    </div>
  </div>
  <button class=""btnBack"" onclick=""closeSettings()"">← 返回</button>
  <div id=""copyright"">©2026 CreatyCatQ<br>©DeepSeek</div>
</div>
<div id=""toast""></div>

<script>
// C# 桥接
function callCS(action,payload){if(payload===undefined)payload='';window.chrome.webview.postMessage(action+'|'+payload)}

// 打开/关闭设置
function openSettings(){
  var es=document.getElementById('tbEngSub').value;
  if(!es||es==='MaimaiDX Launcher')document.getElementById('tbEngSub').value='';
  document.getElementById('settingsPanel').classList.add('show');
}
function closeSettings(){
  var es=document.getElementById('tbEngSub').value;
  if(es)callCS('saveReg','EnglishSubtitle='+es);
  document.getElementById('settingsPanel').classList.remove('show');
  callCS('getState');
}

// C# 调用的函数
function browseGame(){callCS('browseGame')}
function browseBg(){callCS('browseBg')}
function launchGame(){
  var btn=document.getElementById('btnLaunch');
  if(btn.className.indexOf('disabled')!==-1)return;
  btn.className='btn disabled';
  btn.textContent='正在启动...';
  document.getElementById('statusMsg').textContent='正在准备启动游戏...';
  callCS('launch');
}

// C# 调用：进入启动流程时禁用按钮
function setLaunching(){
  var btn=document.getElementById('btnLaunch');
  btn.className='btn disabled';
  btn.textContent='正在启动...';
  document.getElementById('statusMsg').textContent='正在准备启动游戏...';
}

// C# 调用：启动失败时恢复按钮
function setLaunchReady(){
  var btn=document.getElementById('btnLaunch');
  btn.className='btn enabled';
  btn.textContent='开始游戏';
  document.getElementById('statusMsg').textContent='已选择游戏路径';
}
function openSinmaiEditor(){callCS('openSinmaiEditor')}
function resetGamePath(){callCS('resetGamePath')}
function minimizeWindow(){callCS('minimize')}
function closeWindow(){callCS('close')}
function dragWindow(){callCS('drag')}

// 从 C# 接收状态
function setState(gp,es,lbg,hasPath){
  document.getElementById('tbPath').value=gp||'';
  if(es)document.getElementById('tbEngSub').value=es;
  document.getElementById('subMsg').textContent=es||'MaimaiDX Launcher';
  document.getElementById('tbBg').value=lbg||'';

  var btn=document.getElementById('btnLaunch');
  if(hasPath){
    btn.className='btn enabled';
    btn.textContent='开始游戏';
    document.getElementById('statusMsg').textContent='已选择游戏路径';
  }else{
    btn.className='btn disabled';
    btn.textContent='开始游戏';
    document.getElementById('statusMsg').textContent=gp?'游戏路径不存在，请重新设置':'请先设置游戏路径';
  }

  // 加载背景
  if(lbg){setBgFile(lbg,lbg.split('.').pop().toLowerCase())}
}

// 设置背景文件
function setBgFile(path,ext){
  var isVideo=ext==='mp4'||ext==='webm'||ext==='avi';
  var vi=document.getElementById('bgVideo');
  var im=document.getElementById('bgImage');
  if(isVideo){
    im.style.display='none';
    vi.style.display='block';
    var fn=path.split('/').pop().split('\\').pop();
    vi.innerHTML='<source src=""https://bg.local/'+encodeURIComponent(fn)+'"" type=""video/mp4"">';
    vi.load();vi.play();
  }else{
    vi.style.display='none';
    im.style.display='block';
    im.src='https://bg.local/'+encodeURIComponent(path.split('/').pop().split('\\').pop());
  }
  document.getElementById('tbBg').value=path;
}

// Toast 提示
function showStatus(msg,type){
  var t=document.getElementById('toast');
  t.textContent=msg;t.className=type||'success';t.style.opacity=1;
  clearTimeout(t._timer);
  t._timer=setTimeout(function(){t.style.opacity=0},2000);
}

// 副标题实时更新
document.getElementById('tbEngSub').addEventListener('input',function(){
  var v=this.value||'MaimaiDX Launcher';
  document.getElementById('subMsg').textContent=v;
  callCS('setWindowTitle',v);
});

// 初始化：向 C# 请求当前状态
callCS('getState');
</script>
</body></html>";

    // Win32: 无边框窗口拖动
    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    const int WM_NCLBUTTONDOWN = 0x00A1;
    const int HT_CAPTION = 0x0002;

    void DragWindow()
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
    }

    const int WM_NCHITTEST = 0x0084;
    const int HTCAPTION = 2;
    const int HTCLIENT = 1;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            // 将鼠标屏幕坐标转为客户端坐标
            int x = m.LParam.ToInt32() & 0xFFFF;
            int y = m.LParam.ToInt32() >> 16;
            var clientPos = PointToClient(new Point(x, y));

            // 顶部 45px 且不在右侧按钮区域（右100px）时，视为标题栏可拖拽
            if (clientPos.Y >= 0 && clientPos.Y <= 45 &&
                clientPos.X >= 0 && clientPos.X <= Width - 100)
            {
                m.Result = (IntPtr)HTCAPTION;
                return;
            }
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_sinmaiProcess != null && !_sinmaiProcess.HasExited)
        {
            try { _sinmaiProcess.Kill(); } catch { }
        }
        base.OnFormClosing(e);
    }
}