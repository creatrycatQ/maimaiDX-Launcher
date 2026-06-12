using System.Runtime.InteropServices;

namespace GameLauncher;

public class ProgressOverlay : Form
{
    private Label _label = null!;
    private ProgressBar _progressBar = null!;

    // Win32 窗口动画 API
    [DllImport("user32.dll")]
    private static extern bool AnimateWindow(IntPtr hwnd, int dwTime, int dwFlags);

    private const int AW_BLEND = 0x00080000;   // 淡入淡出
    private const int AW_ACTIVATE = 0x00020000; // 激活窗口（配合 AW_BLEND 用于显示）
    private const int AW_HIDE = 0x00010000;     // 隐藏窗口（配合 AW_BLEND 用于淡出）

    public ProgressOverlay(string subtitle)
    {
        InitializeOverlay(subtitle);
    }

    private void InitializeOverlay(string subtitle)
    {
        // 窗口基本属性
        Width = 420;
        Height = 95;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(22, 22, 36);

        // 定位到主屏幕工作区右下角
        var workArea = Screen.PrimaryScreen!.WorkingArea;
        Left = workArea.Right - Width - 20;
        Top = workArea.Bottom - Height - 20;

        // 内边 Panel 做视觉边框
        var inner = new Panel
        {
            Width = Width - 4,
            Height = Height - 4,
            Location = new Point(2, 2),
            BackColor = Color.FromArgb(32, 32, 50)
        };
        Controls.Add(inner);

        // 标题文字
        string displayText = string.IsNullOrWhiteSpace(subtitle)
            ? "正在启动..."
            : $"正在启动 ({subtitle})";

        _label = new Label
        {
            Text = displayText,
            ForeColor = Color.FromArgb(225, 225, 242),
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular),
            AutoSize = false,
            Width = 390,
            Height = 26,
            Location = new Point(14, 12),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
        inner.Controls.Add(_label);

        // 进度条
        _progressBar = new ProgressBar
        {
            Style = ProgressBarStyle.Continuous,
            Width = 390,
            Height = 7,
            Location = new Point(14, 50),
            Maximum = 100,
            Minimum = 0,
            Value = 0
        };
        inner.Controls.Add(_progressBar);
    }

    /// <summary>
    /// 显示窗口并运行动画进度条（含淡入效果）
    /// </summary>
    /// <param name="durationMs">进度条总时长（毫秒）</param>
    /// <param name="ct">取消令牌</param>
    public async Task RunProgressAsync(int durationMs = 3000, CancellationToken ct = default)
    {
        // 淡入动画 (500ms)
        Show();
        AnimateWindow(Handle, 500, AW_BLEND | AW_ACTIVATE);

        _progressBar.Value = 0;

        int steps = 60;
        int stepDelay = Math.Max(1, durationMs / steps);

        for (int i = 0; i <= steps; i++)
        {
            if (ct.IsCancellationRequested) return;

            int value = Math.Min(100, (int)(i * 100.0 / steps));
            try { _progressBar.Value = value; } catch { /* 窗口可能已释放 */ }
            try { await Task.Delay(stepDelay, ct); }
            catch (TaskCanceledException) { return; }
        }
    }

    /// <summary>
    /// 淡出动画（不阻塞 UI），完成后隐藏窗口
    /// </summary>
    public async Task FadeOutAsync(int durationMs = 400)
    {
        // AnimateWindow 是同步的，用 Task.Run 避免阻塞调用线程
        await Task.Run(() => AnimateWindow(Handle, durationMs, AW_BLEND | AW_HIDE));
    }
}
