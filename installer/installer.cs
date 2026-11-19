using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// Shokz Battery - one-click installer.
// The whole payload (Chrome extension + native host + tray) is embedded as a zip
// resource. On run it extracts to %LOCALAPPDATA%\ShokzBattery, registers the
// native messaging host, sets the tray icon to auto-start, launches it, and opens
// the Chrome extensions page with the folder to load.
class Setup
{
    const string ExtId = "pfggfahekjpdogphkccembookkehgjpi";
    const string HostName = "com.shokz.battery";

    [STAThread]
    static void Main(string[] args)
    {
        bool silent = args != null && (Array.IndexOf(args, "/S") >= 0 || Array.IndexOf(args, "/s") >= 0);
        try { DoInstall(silent); }
        catch (Exception ex)
        {
            MessageBox.Show("Install failed:\r\n\r\n" + ex.Message, "Bluetooth Battery",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void DoInstall(bool silent)
    {
        string target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShokzBattery");

        // Stop a running tray so its files are not locked.
        try
        {
            foreach (Process p in Process.GetProcessesByName("tray"))
            {
                try { p.Kill(); } catch { }
            }
        }
        catch { }
        Thread.Sleep(600);

        if (Directory.Exists(target))
        {
            try { Directory.Delete(target, true); } catch { }
        }
        Directory.CreateDirectory(target);

        // Extract the embedded payload.
        Assembly asm = Assembly.GetExecutingAssembly();
        using (Stream s = asm.GetManifestResourceStream("ShokzPayload.zip"))
        {
            if (s == null) throw new Exception("embedded payload not found");
            string tmp = Path.Combine(Path.GetTempPath(), "shokz_" + Guid.NewGuid().ToString("N") + ".zip");
            using (FileStream fs = File.Create(tmp)) s.CopyTo(fs);
            ZipFile.ExtractToDirectory(tmp, target);
            try { File.Delete(tmp); } catch { }
        }

        // Native messaging host manifest (absolute path to extracted host.exe).
        string hostExe = Path.Combine(target, "native-host", "host.exe");
        string nmPath = Path.Combine(target, "native-host", HostName + ".json");
        string escHost = hostExe.Replace("\\", "\\\\");
        string json = "{\"name\":\"" + HostName + "\",\"description\":\"Bluetooth battery reader\"," +
                      "\"path\":\"" + escHost + "\",\"type\":\"stdio\"," +
                      "\"allowed_origins\":[\"chrome-extension://" + ExtId + "/\"]}";
        File.WriteAllText(nmPath, json, new System.Text.UTF8Encoding(false));

        // Register for Chrome and Edge.
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(
            "Software\\Google\\Chrome\\NativeMessagingHosts\\" + HostName))
            k.SetValue(null, nmPath);
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(
            "Software\\Microsoft\\Edge\\NativeMessagingHosts\\" + HostName))
            k.SetValue(null, nmPath);

        // Tray auto-start.
        string trayExe = Path.Combine(target, "tray", "tray.exe");
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(
            "Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
            k.SetValue("ShokzBatteryTray", "\"" + trayExe + "\"");

        // Start the tray now.
        try { Process.Start(new ProcessStartInfo(trayExe) { UseShellExecute = true }); } catch { }

        // Open the Chrome extensions page.
        if (!silent) { try { Process.Start("chrome.exe", "chrome://extensions/"); } catch { } }

        string extPath = Path.Combine(target, "extension");
        if (silent) return;
        bool zh = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
        string msg =
            "安装完成！任务栏电量图标已在运行，并会随 Windows 启动。\r\n" +
            "Install complete. The taskbar battery icon is running and will start with Windows.\r\n\r\n" +
            "完成 Chrome 插件（仅需一次） / Finish the Chrome part (one time only):\r\n" +
            "1. 在刚打开的 chrome://extensions 开启「开发者模式」 / turn on Developer mode\r\n" +
            "2. 点「加载已解压的扩展程序」 / click \"Load unpacked\"\r\n" +
            "3. 选择这个文件夹 / select this folder:\r\n" + extPath + "\r\n\r\n" +
            "完成后鼠标悬停插件图标即可看电量 / Then hover the extension icon to see the battery %.";
        MessageBox.Show(msg, zh ? "蓝牙电量安装" : "Bluetooth Battery Setup",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
