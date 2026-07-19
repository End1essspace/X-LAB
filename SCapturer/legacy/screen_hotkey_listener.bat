
@echo off
REM --- one-time autostart install (Startup folder) ---
if /i "%~1"=="--install" goto :INSTALL
if /i "%~1"=="--run" goto :RUN

echo Installing autostart...
call "%~f0" --install
echo Done. Starting...
call "%~f0" --run
exit /b 0

:INSTALL
set "STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
set "LNK=%STARTUP%\Screen Hotkey Listener.lnk"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$s=(New-Object -ComObject WScript.Shell);" ^
  "$lnk=$s.CreateShortcut('%LNK%');" ^
  "$lnk.TargetPath='%~f0';" ^
  "$lnk.Arguments='--run';" ^
  "$lnk.WorkingDirectory='%~dp0';" ^
  "$lnk.WindowStyle=7;" ^
  "$lnk.Save()"
exit /b 0

:RUN
@echo off
setlocal EnableExtensions EnableDelayedExpansion
title Screen Hotkey Listener (DEBUG)

set "PS1=%TEMP%\screen_hotkey_listener.ps1"

REM --- find marker line number in THIS bat ---
for /f "delims=:" %%L in ('findstr /n /c:"__PS1_PAYLOAD_BELOW__" "%~f0"') do set "MARK=%%L"
if not defined MARK (
  echo ERROR: Marker not found.
  pause
  exit /b 1
)

set /a START=MARK
echo Writing PS1 to: "%PS1%"
echo Marker line: %MARK%
echo.

REM --- write everything after marker to PS1 ---
more +%START% "%~f0" > "%PS1%"

echo Done. Launching PowerShell (window will stay open)...
echo Hotkeys: Ctrl+Shift+G = screenshot to clipboard, Ctrl+Shift+Q = quit.
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -STA -File "%PS1%"
exit /b 0

echo.
echo PowerShell returned to CMD. Press any key to close.
pause >nul
exit /b 0

__PS1_PAYLOAD_BELOW__
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function Log([string]$s) {
  $ts = (Get-Date).ToString('HH:mm:ss')
  Write-Host ("[{0}] {1}" -f $ts, $s)
}

$code = @'
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;

public class HotkeyListenerForm : Form
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    [DllImport("user32.dll", SetLastError=true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError=true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError=true)]
    private static extern short GetKeyState(int nVirtKey);

    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT   = 0x10;
    private const int VK_G       = 0x47;
    private const int VK_Q       = 0x51;

    private readonly Timer pollTimer;

    public HotkeyListenerForm()
    {
        this.ShowInTaskbar = false;
        this.FormBorderStyle = FormBorderStyle.None;
        this.StartPosition = FormStartPosition.Manual;
        this.Location = new Point(-32000, -32000);
        this.Size = new Size(1, 1);
        this.Opacity = 0.01;

        pollTimer = new Timer();
        pollTimer.Interval = 50;
        pollTimer.Tick += PollTimer_Tick;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        pollTimer.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        uint mods = MOD_CTRL + MOD_SHIFT;

        if (!RegisterHotKey(this.Handle, 1, mods, (uint)Keys.G))
            throw new Exception("RegisterHotKey Ctrl+Shift+G failed. Win32=" + Marshal.GetLastWin32Error());

        if (!RegisterHotKey(this.Handle, 2, mods, (uint)Keys.Q))
            throw new Exception("RegisterHotKey Ctrl+Shift+Q failed. Win32=" + Marshal.GetLastWin32Error());
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { UnregisterHotKey(this.Handle, 1); } catch {}
        try { UnregisterHotKey(this.Handle, 2); } catch {}
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            Console.WriteLine("[HOTKEY] WM_HOTKEY received: id=" + id);

            if (id == 1) CaptureAllMonitorsToClipboard();
            else if (id == 2) Application.Exit();
        }
        base.WndProc(ref m);
    }

    private void PollTimer_Tick(object sender, EventArgs e)
    {
        bool ctrl  = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
        bool shift = (GetKeyState(VK_SHIFT)   & 0x8000) != 0;
        bool g     = (GetKeyState(VK_G)       & 0x8000) != 0;
        bool q     = (GetKeyState(VK_Q)       & 0x8000) != 0;

        if (ctrl && shift && g)
        {
            Console.WriteLine("[POLL] Ctrl+Shift+G detected");
            CaptureAllMonitorsToClipboard();
            System.Threading.Thread.Sleep(250);
        }
        else if (ctrl && shift && q)
        {
            Console.WriteLine("[POLL] Ctrl+Shift+Q detected");
            Application.Exit();
        }
    }

    private static void CaptureAllMonitorsToClipboard()
    {
        Rectangle b = SystemInformation.VirtualScreen;
        using (Bitmap bmp = new Bitmap(b.Width, b.Height))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(b.X, b.Y, 0, 0, bmp.Size);
            Clipboard.SetImage(bmp);
        }
        System.Media.SystemSounds.Beep.Play();
    }
}
'@

try {
  Log "Compiling C# listener..."
  Add-Type -TypeDefinition $code -ReferencedAssemblies 'System.Windows.Forms','System.Drawing'
  Log "OK. Listener running. Try Ctrl+Shift+G now."
  Log "If WM_HOTKEY is blocked, fallback POLL will still capture."
  [System.Windows.Forms.Application]::Run((New-Object HotkeyListenerForm))
  Log "Listener exited."
}
catch {
  Log ("ERROR: " + $_.Exception.Message)
  $_ | Format-List * | Out-String | Write-Host
  Read-Host "Press Enter"
}
