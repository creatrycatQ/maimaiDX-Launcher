using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace GameLauncher;

public partial class Form1 : Form
{
    // ---- 配置文件 ----
    const string RGP = "GamePath", RES = "EnglishSubtitle", RLB = "LargeBgPath";
    static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
    Dictionary<string, string> _config = new();

    WebView2 _webView = null!;
    string _bgFolder = null!;
    Process? _sinmaiProcess;

    // Win32: 文件对话框需要窗口句柄
    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    public Form1()
    {
        InitializeComponent();
        LoadConfig();
        _bgFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "backgrounds");
        Directory.CreateDirectory(_bgFolder);
        BuildUI();
    }

    // ---- 任务栏最小化修复：补齐 WS_SYSMENU | WS_MINIMIZEBOX ----
    const int WS_SYSMENU = 0x00080000;
    const int WS_MINIMIZEBOX = 0x00020000;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= WS_SYSMENU | WS_MINIMIZEBOX;
            return cp;
        }
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
                _ = OpenSinmaiEditorAsync();
                break;
            case "openOptFolder":
                OpenOptFolder();
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
        var gp = GetConfig(RGP) ?? "";
        var es = GetConfig(RES) ?? "MaimaiDX Launcher";
        var lbg = GetConfig(RLB) ?? "";
        var hasPath = !string.IsNullOrWhiteSpace(gp) && File.Exists(gp);
        var optFolderExists = false;
        if (hasPath)
        {
            var dir = Path.GetDirectoryName(gp);
            if (!string.IsNullOrWhiteSpace(dir))
                optFolderExists = Directory.Exists(Path.Combine(dir, "Sinmai_Data", "StreamingAssets"));
        }
        var js = $"setState('{EscapeJs(gp)}','{EscapeJs(es)}','{EscapeJs(lbg)}',{hasPath.ToString().ToLower()},{optFolderExists.ToString().ToLower()})";
        await _webView.CoreWebView2.ExecuteScriptAsync(js);
    }

    async void DoLaunch()
    {
        var p = GetConfig(RGP);
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
            var es = GetConfig(RES) ?? "";
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

    async Task OpenSinmaiEditorAsync()
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
            // 如果编辑器文件不存在，自动从 GitHub 下载
            if (!File.Exists(scriptPath))
            {
                _ = _webView.CoreWebView2.ExecuteScriptAsync("showStatus('未找到编辑器文件，正在从GitHub下载...','success')");
                var downloaded = await DownloadSinmaiAssistAsync(editorDir);
                if (!downloaded)
                {
                    _ = _webView.CoreWebView2.ExecuteScriptAsync("showStatus('下载失败，请检查网络连接后重试','error')");
                    return;
                }
                _ = _webView.CoreWebView2.ExecuteScriptAsync("showStatus('下载完成，正在启动编辑器...','success')");
            }

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

    /// <summary>使用 GitHub 加速从仓库下载 Sinmai-Assist 工具文件夹。</summary>
    async Task<bool> DownloadSinmaiAssistAsync(string targetDir)
    {
        try
        {
            var repoOwner = "creatrycatQ";
            var repoName = "maimaiDX-Launcher";
            var branch = "master";

            // GitHub 下载加速代理（ghproxy.com）
            var zipUrl = $"https://ghproxy.com/https://github.com/{repoOwner}/{repoName}/archive/refs/heads/{branch}.zip";

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(2);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MaimaiDX-Launcher/1.0");

            var zipPath = Path.Combine(Path.GetTempPath(), $"sinmai_assist_{Guid.NewGuid():N}.zip");
            var extractRoot = Path.Combine(Path.GetTempPath(), $"sinmai_assist_extract_{Guid.NewGuid():N}");

            try
            {
                // 下载 zip
                var response = await http.GetAsync(zipUrl);
                response.EnsureSuccessStatusCode();
                using var fs = File.Create(zipPath);
                await response.Content.CopyToAsync(fs);
                fs.Close();

                // 解压
                ZipFile.ExtractToDirectory(zipPath, extractRoot);

                // 找到解压后的根目录（maimaiDX-Launcher-master）
                var srcDir = Directory.GetDirectories(extractRoot).FirstOrDefault()
                    ?? extractRoot;

                // 查找 Sinmai-Assist 子目录
                var sinmaiAssistSrc = Path.Combine(srcDir, "Sinmai-Assist");
                if (!Directory.Exists(sinmaiAssistSrc))
                    sinmaiAssistSrc = Directory.GetDirectories(srcDir, "Sinmai-Assist", SearchOption.AllDirectories).FirstOrDefault()
                        ?? srcDir;

                // 复制文件到目标目录
                Directory.CreateDirectory(targetDir);
                foreach (var file in Directory.GetFiles(sinmaiAssistSrc))
                {
                    var dest = Path.Combine(targetDir, Path.GetFileName(file));
                    File.Copy(file, dest, true);
                }
                // 递归复制子目录
                foreach (var subDir in Directory.GetDirectories(sinmaiAssistSrc))
                {
                    var destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
                    CopyDirectoryRecursive(subDir, destSubDir);
                }

                return true;
            }
            finally
            {
                // 清理临时文件
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                try { if (Directory.Exists(extractRoot)) Directory.Delete(extractRoot, true); } catch { }
            }
        }
        catch
        {
            return false;
        }
    }

    static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        foreach (var subDir in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursive(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
    }

    /// <summary>打开 opt 文件夹：游戏路径目录下的 Sinmai_Data\StreamingAssets。</summary>
    void OpenOptFolder()
    {
        var gp = GetConfig(RGP);
        if (string.IsNullOrWhiteSpace(gp) || !File.Exists(gp))
        {
            _ = _webView.CoreWebView2.ExecuteScriptAsync("showStatus('请先设置游戏启动路径','error')");
            return;
        }

        var dir = Path.GetDirectoryName(gp);
        if (string.IsNullOrWhiteSpace(dir))
        {
            _ = _webView.CoreWebView2.ExecuteScriptAsync("showStatus('无法获取游戏目录','error')");
            return;
        }

        var optDir = Path.Combine(dir, "Sinmai_Data", "StreamingAssets");
        if (!Directory.Exists(optDir))
        {
            // 创建目录
            Directory.CreateDirectory(optDir);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{optDir}\"",
            UseShellExecute = false
        });
    }

    async void ResetGamePath()
    {
        SaveConfig(RGP, "");
        await SendState();
    }

    void BrowseGamePath()
    {
        var d = new OpenFileDialog { Title = "选择启动程序", Filter = "可执行文件|*.exe;*.bat|所有文件|*.*", RestoreDirectory = true };
        var cur = GetConfig(RGP);
        if (!string.IsNullOrWhiteSpace(cur) && File.Exists(cur)) d.InitialDirectory = Path.GetDirectoryName(cur);
        if (d.ShowDialog() == DialogResult.OK)
        {
            SaveConfig(RGP, d.FileName);
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

        SaveConfig(RLB, localPath);
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"setBgFile('{EscapeJs(localPath)}','{EscapeJs(ext)}')");
        _ = _webView.CoreWebView2.ExecuteScriptAsync("showStatus('背景已更新','success')");
    }

    void SaveRegFromJS(string payload)
    {
        // payload: "key=value"
        var kv = payload.Split('=', 2);
        if (kv.Length == 2) SaveConfig(kv[0], kv[1]);
    }

    string? GetConfig(string name) { _config.TryGetValue(name, out var v); return v; }
    void SaveConfig(string name, string val) { _config[name] = val; FlushConfig(); }

    void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                _config = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
        }
        catch { _config = new(); }
    }

    void FlushConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
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
#settingsPanel{position:fixed;top:0;left:0;width:100%;height:100%;z-index:10;background:rgba(10,10,30,0.50);display:none;flex-direction:column;padding:60px 100px;overflow-y:auto;backdrop-filter:blur(12px);-webkit-backdrop-filter:blur(12px)}
#settingsPanel.show{display:flex}
#settingsTitle{font-size:22px;font-weight:bold;color:#fff;margin-bottom:30px}

/* 标签栏 */
#tabNav{display:flex;gap:0;margin-bottom:36px;border-bottom:1px solid rgba(255,255,255,0.1)}
.tabBtn{padding:10px 28px;border:none;background:transparent;color:rgba(255,255,255,0.5);font-size:14px;cursor:pointer;transition:all 0.2s;font-family:inherit;border-bottom:2px solid transparent;position:relative;bottom:-1px}
.tabBtn:hover{color:rgba(255,255,255,0.8)}
.tabBtn.active{color:#ffb84d;border-bottom-color:#ffb84d}

/* 标签内容 */
.tabContent{display:none;flex-direction:column;gap:0}
.tabContent.active{display:flex}

.section{margin-bottom:28px}
.section label{display:block;font-size:11px;color:rgba(220,220,230,0.85);margin-bottom:6px}
.section .row{display:flex;gap:10px;align-items:center}
.section input[type=""text""]{flex:1;max-width:600px;height:36px;background:#22223a;border:none;color:#fff;padding:0 14px;font-size:11px;border-radius:4px;outline:none;font-family:inherit}
.section input[type=""text""]:focus{outline:1px solid rgba(255,184,77,0.5)}
.section input[type=""text""]:read-only{color:rgba(200,200,220,0.7)}
.btnGold{padding:0 22px;height:36px;border-radius:18px;border:none;background:linear-gradient(180deg,#ffb84d,#ff8c28);color:#fff;font-size:11px;font-weight:bold;cursor:pointer;transition:all 0.2s;white-space:nowrap;font-family:inherit}
.btnGold:hover{background:linear-gradient(180deg,#ffc664,#ff9b37);box-shadow:0 2px 12px rgba(255,150,30,0.35)}
.btnGray{padding:0 22px;height:36px;border-radius:18px;border:none;background:rgba(100,100,120,0.5);color:rgba(200,200,210,0.5);font-size:11px;font-weight:bold;cursor:default;white-space:nowrap;font-family:inherit;transition:all 0.2s}
.btnBack{padding:0 28px;height:38px;border-radius:19px;border:none;background:rgba(55,55,78,0.9);color:#fff;font-size:11px;cursor:pointer;transition:all 0.2s;position:absolute;bottom:25px;left:30px;font-family:inherit}
.btnBack:hover{background:rgba(75,75,100,0.9)}
#copyright{position:absolute;bottom:25px;right:32px;font-size:10px;color:rgba(255,255,255,0.35);text-align:right;line-height:1.5}

/* 工具区 opt 提示 */
.optHint{font-size:10px;color:rgba(255,180,80,0.7);margin-top:6px}

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
#dragHandle{position:fixed;top:0;left:0;right:100px;height:45px;z-index:20;cursor:default}

/* 转场动画层 */
#transitionOverlay{position:fixed;top:0;left:0;width:100%;height:100%;z-index:12;display:none;pointer-events:none}
#transitionCanvas{width:100vw;height:100vh;position:fixed;top:0;left:0}
#transitionVideo{display:none}
</style>
</head>
<body>

<div id=""bg"">
  <video id=""bgVideo"" autoplay loop muted playsinline style=""display:none""></video>
  <img id=""bgImage"" style=""display:none"">
</div>
<div id=""overlay""></div>
<div id=""dragHandle"" onmousedown=""dragWindow()""></div>
<div id=""main"">
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

  <!-- 标签导航 -->
  <div id=""tabNav"">
    <button class=""tabBtn active"" id=""tabPathBtn"" onclick=""switchTab('path')"">路径</button>
    <button class=""tabBtn"" id=""tabToolsBtn"" onclick=""switchTab('tools')"">工具</button>
  </div>

  <!-- 路径 Tab -->
  <div class=""tabContent active"" id=""tabPath"">
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
  </div>

  <!-- 工具 Tab -->
  <div class=""tabContent"" id=""tabTools"">
    <div class=""section"">
      <label>Sinmai-Assist 配置工具:</label>
      <div class=""row"">
        <button class=""btnGold"" onclick=""openSinmaiEditor()"">🔧 启动配置编辑器</button>
      </div>
    </div>
    <div class=""section"" style=""margin-top:-12px"">
      <label>Opt 文件夹:</label>
      <div class=""row"">
        <button id=""btnOpenOpt"" class=""btnGold"" onclick=""openOptFolder()"">📂 打开文件夹</button>
        <span id=""optHint"" class=""optHint"" style=""display:none"">请设置游戏启动路径</span>
      </div>
    </div>
  </div>

  <button class=""btnBack"" onclick=""closeSettings()"">← 返回</button>
  <div id=""copyright"">©2026 CreatyCatQ<br>©DeepSeek</div>
</div>
<div id=""toast""></div>

<!-- 转场动画层（绿幕抠除） -->
<div id=""transitionOverlay"">
  <canvas id=""transitionCanvas"" width=""1280"" height=""720""></canvas>
</div>
<video id=""transitionVideo"" muted playsinline preload=""auto"">
  <source src=""https://bg.local/Transition-animation.mp4"" type=""video/mp4"">
</video>

<script>
// C# 桥接
function callCS(action,payload){if(payload===undefined)payload='';window.chrome.webview.postMessage(action+'|'+payload)}

// 标签切换
function switchTab(tab){
  document.querySelectorAll('.tabBtn').forEach(function(b){b.classList.remove('active')});
  document.querySelectorAll('.tabContent').forEach(function(c){c.classList.remove('active')});
  if(tab==='path'){
    document.getElementById('tabPathBtn').classList.add('active');
    document.getElementById('tabPath').classList.add('active');
  }else{
    document.getElementById('tabToolsBtn').classList.add('active');
    document.getElementById('tabTools').classList.add('active');
    updateOptButton();
  }
}

// 更新 opt 按钮状态
function updateOptButton(){
  var btn=document.getElementById('btnOpenOpt');
  var hint=document.getElementById('optHint');
  var gp=document.getElementById('tbPath').value;
  if(gp){
    btn.className='btnGold';
    btn.style.cursor='pointer';
    hint.style.display='none';
  }else{
    btn.className='btnGray';
    btn.style.cursor='default';
    hint.style.display='inline';
  }
}

// 打开 opt 文件夹
function openOptFolder(){
  var gp=document.getElementById('tbPath').value;
  if(!gp){return;}
  callCS('openOptFolder');
}

// 打开设置（转场动画前半段覆盖主界面，50% 时设置界面浮现到动画下方）
function openSettings(){
  var es=document.getElementById('tbEngSub').value;
  if(!es||es==='MaimaiDX Launcher')document.getElementById('tbEngSub').value='';
  playTransitionVideo(function(){
    // 动画播放到 50% 时回调：设置界面从动画下方浮现
    document.getElementById('settingsPanel').classList.add('show');
    switchTab('path');
    updateOptButton();
  });
}
// 关闭设置（返回主界面，同样播放转场动画）
function closeSettings(){
  var es=document.getElementById('tbEngSub').value;
  if(es)callCS('saveReg','EnglishSubtitle='+es);
  playTransitionVideo(function(){
    // 动画播放到 50% 时：隐藏设置界面，主界面浮现到动画下方
    document.getElementById('settingsPanel').classList.remove('show');
    callCS('getState');
  });
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
function setState(gp,es,lbg,hasPath,optFolderExists){
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

  // 更新 opt 按钮状态
  updateOptButton();

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

// 绿幕转场动画（WebGL GPU 着色器抠除纯绿背景 —— 流畅无卡顿）
var _transitionGl=null;
var _transitionCallback=null;
var _transitionVideoObj=null;
var _transitionAnimId=null;
var _transitionHalfDone=false;

function playTransitionVideo(callback){
  var video=document.getElementById('transitionVideo');
  var canvas=document.getElementById('transitionCanvas');
  var overlay=document.getElementById('transitionOverlay');
  _transitionCallback=callback;
  _transitionVideoObj=video;

  // 转场层叠加在上方
  overlay.style.display='block';

  // 初始化 WebGL（仅首次）
  if(!_transitionGl){
    _transitionGl=canvas.getContext('webgl',{premultipliedAlpha:false});
    var gl=_transitionGl;

    // 顶点着色器
    var vs=gl.createShader(gl.VERTEX_SHADER);
    gl.shaderSource(vs,'attribute vec2 aPos;varying vec2 vUV;void main(){vUV=(aPos+1.0)/2.0;gl_Position=vec4(aPos,0,1);}');
    gl.compileShader(vs);

    // 片段着色器 —— 绿幕抠除核心
    var fs=gl.createShader(gl.FRAGMENT_SHADER);
    gl.shaderSource(fs,'precision mediump float;varying vec2 vUV;uniform sampler2D uTex;void main(){vec4 c=texture2D(uTex,vUV);if(c.g>0.35&&c.g>c.r*1.3&&c.g>c.b*1.3)discard;gl_FragColor=c;}');
    gl.compileShader(fs);

    var prog=gl.createProgram();
    gl.attachShader(prog,vs);gl.attachShader(prog,fs);
    gl.linkProgram(prog);gl.useProgram(prog);

    // 全屏四边形
    var buf=gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER,buf);
    gl.bufferData(gl.ARRAY_BUFFER,new Float32Array([-1,-1, 1,-1, -1,1, 1,1]),gl.STATIC_DRAW);
    var aPos=gl.getAttribLocation(prog,'aPos');
    gl.enableVertexAttribArray(aPos);
    gl.vertexAttribPointer(aPos,2,gl.FLOAT,false,0,0);

    // 纹理
    var tex=gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D,tex);
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.LINEAR);
    // 视频帧上传时自动翻转 Y 轴（视频解码和 WebGL 纹理坐标系相反）
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL,true);
  }

  video.currentTime=0;
  _transitionHalfDone=false;
  video.play();
}

function _processTransitionFrameGL(){
  var video=_transitionVideoObj;
  var gl=_transitionGl;
  if(video.paused || video.ended){
    if(gl){gl.clear(gl.COLOR_BUFFER_BIT);}
    document.getElementById('transitionOverlay').style.display='none';
    if(_transitionCallback){var cb=_transitionCallback;_transitionCallback=null;cb();}
    _transitionHalfDone=false;
    return;
  }
  // 动画播放到 50% 时：设置界面浮现到动画下方
  if(!_transitionHalfDone && video.duration && video.currentTime >= video.duration*0.5){
    _transitionHalfDone=true;
    if(_transitionCallback){var cb=_transitionCallback;_transitionCallback=null;cb();}
  }
  // 上传当前视频帧到 GPU 纹理
  gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,gl.RGBA,gl.UNSIGNED_BYTE,video);
  gl.drawArrays(gl.TRIANGLE_STRIP,0,4);
  _transitionAnimId=requestAnimationFrame(_processTransitionFrameGL);
}

document.getElementById('transitionVideo').addEventListener('playing',function(){
  _transitionAnimId=requestAnimationFrame(_processTransitionFrameGL);
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
