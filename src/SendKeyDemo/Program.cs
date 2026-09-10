using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SendKeyDemo;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    const int SW_RESTORE = 9;

    [STAThread]
    static void Main()
    {
        // Chỉ cho chạy 1 bản: bản thứ hai không đăng ký được hotkey toàn cục (bản đầu đang giữ)
        // và sẽ chết lặng lẽ — thay vì thế, đưa cửa sổ bản đang chạy lên rồi thoát.
        using var single = new Mutex(true, @"Local\SendKeyDemo.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            FocusRunningInstance();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    static void FocusRunningInstance()
    {
        var me = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("SendKeyDemo"))
        {
            if (p.Id == me || p.MainWindowHandle == IntPtr.Zero) continue;
            ShowWindow(p.MainWindowHandle, SW_RESTORE);
            SetForegroundWindow(p.MainWindowHandle);
            return;
        }

        // Bản kia đang thu về tray (không có cửa sổ) -> báo cho biết, đừng im lặng không phản hồi.
        MessageBox.Show(
            "SendKey Evidence đang chạy sẵn ở khay hệ thống (góc dưới-phải, có thể phải bấm mũi tên \"^\").\n\n" +
            "Double-click icon đó để mở cửa sổ.",
            "Đã chạy rồi", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
