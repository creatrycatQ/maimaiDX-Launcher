using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameLauncher;

static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    const uint MB_TOPMOST = 0x00040000;
    const uint MB_SETFOREGROUND = 0x00010000;
    const uint MB_ICONWARNING = 0x00000030;

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 多开检测：同一台机器只允许运行一个实例
        using var mutex = new Mutex(true, @"Global\MaimaiDXLauncher_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            var existingHwnd = FindExistingWindow();
            MessageBox(existingHwnd,
                "maimaiDX Launcher 已经在运行中！\n请不要同时运行多个实例。\n(本来就占GPU你是想多开占满吗?)",
                "多开警告",
                MB_TOPMOST | MB_SETFOREGROUND | MB_ICONWARNING);
            return;
        }

        // 设置 DPI 感知模式为不感知，避免高缩放(200%)下窗口错位。
        // Set DPI unaware mode to prevent layout misalignment under high scaling (e.g. 200% on laptops).
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }

    /// <summary>查找已存在的启动器窗口句柄，用作 MessageBox 的 owner 以确保障碍置顶。</summary>
    private static IntPtr FindExistingWindow()
    {
        var current = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName(current.ProcessName);
        foreach (var p in processes)
        {
            if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                return p.MainWindowHandle;
        }
        return IntPtr.Zero;
    }
}
