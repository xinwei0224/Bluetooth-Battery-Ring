using System;
using System.IO;
using System.Text;
using System.Diagnostics;

// Chrome Native Messaging host for the Shokz battery extension.
//
// Chrome launches this exe, writes a 4-byte little-endian length followed by a
// UTF-8 JSON message on stdin, and expects a reply framed the same way on stdout.
// We ignore the request payload, run status.ps1 (which reads the Windows PnP
// property DEVPKEY_Bluetooth_Battery), and return its JSON line.
// Nothing must be written to stdout except the framed reply.
class NativeHost
{
    static int Main()
    {
        try
        {
            Stream stdin = Console.OpenStandardInput();
            Stream stdout = Console.OpenStandardOutput();

            byte[] lenBuf = new byte[4];
            int read = 0;
            while (read < 4)
            {
                int n = stdin.Read(lenBuf, read, 4 - read);
                if (n <= 0) return 0;
                read += n;
            }
            int len = BitConverter.ToInt32(lenBuf, 0);
            if (len > 0 && len < 1048576)
            {
                byte[] msg = new byte[len];
                int r = 0;
                while (r < len)
                {
                    int n = stdin.Read(msg, r, len - r);
                    if (n <= 0) break;
                    r += n;
                }
            }

            string json = RunStatus();
            if (string.IsNullOrEmpty(json))
                json = "{\"ok\":false,\"connected\":false,\"percent\":null,\"note\":\"status.ps1 produced no output\"}";

            byte[] outBytes = Encoding.UTF8.GetBytes(json);
            byte[] outLen = BitConverter.GetBytes(outBytes.Length);
            stdout.Write(outLen, 0, 4);
            stdout.Write(outBytes, 0, outBytes.Length);
            stdout.Flush();
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    static string RunStatus()
    {
        try
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string script = Path.Combine(exeDir, "status.ps1");
            if (!File.Exists(script)) return null;

            var psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;

            using (var proc = Process.Start(psi))
            {
                string outp = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(20000);

                string best = null;
                foreach (string line in outp.Split('\n'))
                {
                    string s = line.Trim();
                    if (s.StartsWith("{")) best = s;
                }
                return best;
            }
        }
        catch
        {
            return null;
        }
    }
}
