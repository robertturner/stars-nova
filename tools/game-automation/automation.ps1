Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

$MOUSEEVENTF_LEFTDOWN = 0x0002
$MOUSEEVENTF_LEFTUP = 0x0004
$SW_RESTORE = 9

# Fixed screen position we always move the game window to, so click coordinates are stable.
$GameOriginX = 100
$GameOriginY = 100

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public static class WinEnum {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    public static List<KeyValuePair<IntPtr,string>> Results = new List<KeyValuePair<IntPtr,string>>();
    public static List<uint> Pids = new List<uint>();
    public static bool Report(IntPtr hWnd, IntPtr lParam) {
        if (IsWindowVisible(hWnd)) {
            int len = GetWindowTextLength(hWnd);
            if (len > 0) {
                StringBuilder sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                Results.Add(new KeyValuePair<IntPtr,string>(hWnd, sb.ToString()));
                Pids.Add(pid);
            }
        }
        return true;
    }
}
"@

function Get-VisibleWindows {
    [WinEnum]::Results.Clear()
    [WinEnum]::Pids.Clear()
    [WinEnum]::EnumWindows([WinEnum+EnumWindowsProc]{ param($h,$l) [WinEnum]::Report($h,$l) }, [IntPtr]::Zero) | Out-Null
    $list = @()
    for ($i = 0; $i -lt [WinEnum]::Results.Count; $i++) {
        $list += [PSCustomObject]@{ Handle = [WinEnum]::Results[$i].Key; Title = [WinEnum]::Results[$i].Value; Pid = [WinEnum]::Pids[$i] }
    }
    return $list
}

# The game's top-level window is always the FIRST match in EnumWindows order (which enumerates
# top-to-bottom in Z-order) belonging to the otvdm process — whichever dialog is currently frontmost
# (main menu, a wizard page, a modal popup), regardless of its title.
function Get-GameWindowHandle {
    $gamePids = (Get-Process -Name otvdm -ErrorAction SilentlyContinue).Id
    if (-not $gamePids) { return [IntPtr]::Zero }
    $windows = Get-VisibleWindows
    $match = $windows | Where-Object { $gamePids -contains $_.Pid } | Select-Object -First 1
    if ($match) { return $match.Handle }
    return [IntPtr]::Zero
}

function Focus-GameWindow {
    $h = Get-GameWindowHandle
    if ($h -eq [IntPtr]::Zero) { throw "Game window not found (is otvdm/stars.exe running?)" }
    [Win32]::ShowWindow($h, $SW_RESTORE) | Out-Null
    [Win32]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 300
    $fg = [Win32]::GetForegroundWindow()
    if ($fg -ne $h) { Write-Warning "Game window did not become foreground (fg=$fg, game=$h)" }
    return $h
}

function Pin-GameWindow {
    # Moves the game window to a fixed on-screen origin so absolute click coords stay valid across calls.
    $h = Focus-GameWindow
    $rect = New-Object Win32+RECT
    [Win32]::GetWindowRect($h, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $ht = $rect.Bottom - $rect.Top
    [Win32]::MoveWindow($h, $GameOriginX, $GameOriginY, $w, $ht, $true) | Out-Null
    Start-Sleep -Milliseconds 200
}

# x,y are relative to the game window's pinned origin (GameOriginX, GameOriginY), not absolute screen coords.
function Click-InGame($x, $y) {
    Focus-GameWindow | Out-Null
    $absX = $GameOriginX + $x
    $absY = $GameOriginY + $y
    [Win32]::SetCursorPos($absX, $absY)
    Start-Sleep -Milliseconds 120
    [Win32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [Win32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
}

# x,y are ABSOLUTE screen coordinates (no pinned-origin offset) — use for popup dialogs Windows
# places on its own (e.g. centered), which are not positioned relative to the pinned main window.
function Click-Absolute($x, $y) {
    Focus-GameWindow | Out-Null
    [Win32]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 120
    [Win32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [Win32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
}

function Type-InGame($text) {
    Focus-GameWindow | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait($text)
}

function Screenshot($path) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
