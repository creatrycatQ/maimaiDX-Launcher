namespace GameLauncher;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 设置 DPI 感知模式为不感知，避免高缩放(200%)下窗口错位。
        // Set DPI unaware mode to prevent layout misalignment under high scaling (e.g. 200% on laptops).
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }    
}