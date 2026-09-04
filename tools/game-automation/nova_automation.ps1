Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public static class NovaWin32 {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

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

    public static List<IntPtr> ChildHandles = new List<IntPtr>();
    public static bool CollectChild(IntPtr hWnd, IntPtr lParam) { ChildHandles.Add(hWnd); return true; }
}
"@

$MOUSEEVENTF_LEFTDOWN = 0x0002
$MOUSEEVENTF_LEFTUP = 0x0004
$SW_RESTORE = 9
$WM_LBUTTONDOWN = 0x0201
$WM_LBUTTONUP = 0x0202
$WM_SETTEXT = 0x000C
$WM_GETTEXT = 0x000D
$BM_CLICK = 0x00F5
$MK_LBUTTON = 0x0001

$Global:NovaOriginX = 100
$Global:NovaOriginY = 100
$Global:NovaProcessName = "Nova"

function Get-VisibleWindowsNova {
    [NovaWin32]::Results.Clear()
    [NovaWin32]::Pids.Clear()
    [NovaWin32]::EnumWindows([NovaWin32+EnumWindowsProc]{ param($h,$l) [NovaWin32]::Report($h,$l) }, [IntPtr]::Zero) | Out-Null
    $list = @()
    for ($i = 0; $i -lt [NovaWin32]::Results.Count; $i++) {
        $list += [PSCustomObject]@{ Handle = [NovaWin32]::Results[$i].Key; Title = [NovaWin32]::Results[$i].Value; Pid = [NovaWin32]::Pids[$i] }
    }
    return $list
}

function Get-NovaWindowHandle {
    $pids = (Get-Process -Name $Global:NovaProcessName -ErrorAction SilentlyContinue).Id
    if (-not $pids) { return [IntPtr]::Zero }
    $windows = Get-VisibleWindowsNova
    $match = $windows | Where-Object { $pids -contains $_.Pid } | Select-Object -First 1
    if ($match) { return $match.Handle }
    return [IntPtr]::Zero
}

function Focus-NovaWindow {
    $h = Get-NovaWindowHandle
    if ($h -eq [IntPtr]::Zero) { throw "Nova window not found (is Nova.exe running?)" }
    [NovaWin32]::ShowWindow($h, $SW_RESTORE) | Out-Null
    [NovaWin32]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 250
    return $h
}

function Pin-NovaWindow {
    $h = Focus-NovaWindow
    $rect = New-Object NovaWin32+RECT
    [NovaWin32]::GetWindowRect($h, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $ht = $rect.Bottom - $rect.Top
    [NovaWin32]::MoveWindow($h, $Global:NovaOriginX, $Global:NovaOriginY, $w, $ht, $true) | Out-Null
    Start-Sleep -Milliseconds 200
}

# Cursor-based click. Needs a healthy, attached display session (CopyFromScreen must work -
# check first, e.g. via Screenshot-Nova's PrintWindow path vs. a raw CopyFromScreen test). If the
# console/RDP session's display has detached (seen after a multi-hour idle period on 2026-09-04),
# this silently does nothing - use the message-based functions below instead, which work
# regardless of desktop/input attach state.
function Click-Nova($x, $y) {
    Focus-NovaWindow | Out-Null
    $absX = $Global:NovaOriginX + $x
    $absY = $Global:NovaOriginY + $y
    [NovaWin32]::SetCursorPos($absX, $absY) | Out-Null
    Start-Sleep -Milliseconds 120
    [NovaWin32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [NovaWin32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
}

function Screenshot-Nova($path) {
    # PrintWindow captures a specific window's content directly and works regardless of desktop
    # composition/attach state - more reliable than CopyFromScreen in this environment. Pass a
    # specific handle for a dialog/popup other than the main window (see PrintWindow-Handle).
    $h = Get-NovaWindowHandle
    if ($h -eq [IntPtr]::Zero) { throw "Nova window not found (is Nova.exe running?)" }
    PrintWindow-Handle $h $path
}

function PrintWindow-Handle($handle, $path) {
    $rect = New-Object NovaWin32+RECT
    [NovaWin32]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $ht = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [NovaWin32]::PrintWindow($handle, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

# ---- Message-based interaction (robust, works regardless of display/input attach state) ----
#
# Lessons learned driving a real WinForms app this way (2026-09-04 session):
#  - BM_CLICK on a native BUTTON HWND (buttons, checkboxes, radios) is reliable. Use
#    Invoke-NovaButtonClick / Click-NovaChildByText for these.
#  - Do NOT send WM_LBUTTONDOWN then WM_LBUTTONUP to a SysListView32 via synchronous SendMessage
#    in two separate sequential calls from the same script - ListView's internal mouse-tracking
#    logic can block the down-message's SendMessage call waiting for a real button-up, which
#    deadlocks against your own not-yet-sent up-message. Use PostMessage (async) for both instead,
#    as Click-NovaListItem does. If you do get stuck, PostMessage a WM_LBUTTONUP from a separate
#    call/process to break the deadlock - the original blocked call will then return.
#  - Never synthesize WM_LBUTTONDBLCLK directly into a SysListView32 - this crashed Nova.exe with
#    an AccessViolationException in comctl32.dll during this session (unclear whether it's a real
#    app bug or an artifact of the synthetic message lacking real prior mouse-move/hover state;
#    treat as unverified either way and avoid it). Prefer a single click plus a dedicated
#    button/keypress where the UI offers one.
#  - Menus: PostMessage a click on the MenuStrip HWND at the item's pixel position to open a
#    dropdown, find the resulting popup via EnumWindows (class contains "Window.20808" for a
#    ToolStripDropDown), PrintWindow it to read positions, then PostMessage a click on the desired
#    item. Get pixel math right first (zoom into a capture) - a slightly-off click can select the
#    wrong item without any error.
#  - NumericUpDown: WM_SETTEXT + WM_KILLFOCUS on the inner Edit does NOT reliably commit a new
#    value (reverted to the old value in testing). Its spinner buttons also ignore synthetic mouse
#    messages entirely. Real cursor click + focus + SendKeys (Ctrl+A, type, Tab) works correctly.
#  - A modal ShowDialog() or an open menu blocks the click call that triggered it until the
#    dialog/menu closes - this is normal, not a hang. Run that click in the background and poll
#    EnumWindows for the new window from a separate call.
#  - A MessageBoxOptions.DefaultDesktopOnly fatal-error dialog can be genuinely on-screen while
#    still being missed by an EnumWindows-based check if you don't also look for it directly -
#    take an actual screenshot before concluding a WinForms process has "hung" (near-zero CPU +
#    Wait/LpcReply thread state looks identical to a real hang from the outside).

function Get-NovaChildren($parentHandle) {
    if (-not $parentHandle) { $parentHandle = Get-NovaWindowHandle }
    $topRect = New-Object NovaWin32+RECT
    [NovaWin32]::GetWindowRect($parentHandle, [ref]$topRect) | Out-Null
    [NovaWin32]::ChildHandles.Clear()
    [NovaWin32]::EnumChildWindows($parentHandle, [NovaWin32+EnumWindowsProc]{ param($h,$l) [NovaWin32]::CollectChild($h,$l) }, [IntPtr]::Zero) | Out-Null
    $list = @()
    foreach ($h in [NovaWin32]::ChildHandles) {
        $cls = New-Object System.Text.StringBuilder 256
        [NovaWin32]::GetClassName($h, $cls, 256) | Out-Null
        $len = [NovaWin32]::GetWindowTextLength($h)
        $txt = ""
        if ($len -gt 0) {
            $sb = New-Object System.Text.StringBuilder ($len + 1)
            [NovaWin32]::GetWindowText($h, $sb, $sb.Capacity) | Out-Null
            $txt = $sb.ToString()
        }
        $r = New-Object NovaWin32+RECT
        [NovaWin32]::GetWindowRect($h, [ref]$r) | Out-Null
        $list += [PSCustomObject]@{
            Handle = $h
            Class  = $cls.ToString()
            Text   = $txt
            RelLeft = $r.Left - $topRect.Left
            RelTop  = $r.Top - $topRect.Top
            RelRight = $r.Right - $topRect.Left
            RelBottom = $r.Bottom - $topRect.Top
            Width = $r.Right - $r.Left
            Height = $r.Bottom - $r.Top
        }
    }
    return $list
}

# Async click at client-relative (x,y) on any HWND - safe for list views (see notes above).
function Click-NovaListItem($handle, $x, $y) {
    $lParam = [IntPtr]((([int]$y -band 0xFFFF) -shl 16) -bor ([int]$x -band 0xFFFF))
    [NovaWin32]::PostMessage($handle, $WM_LBUTTONDOWN, [IntPtr]$MK_LBUTTON, $lParam) | Out-Null
    Start-Sleep -Milliseconds 80
    [NovaWin32]::PostMessage($handle, $WM_LBUTTONUP, [IntPtr]0, $lParam) | Out-Null
}

# Simplest/most reliable way to "press" a native BUTTON control (button, checkbox, radio).
# NOTE: if the button's handler opens a modal dialog (ShowDialog), this call will not return
# until that dialog is closed - see notes above.
function Invoke-NovaButtonClick($handle) {
    [NovaWin32]::SendMessage($handle, $BM_CLICK, [IntPtr]0, [IntPtr]0) | Out-Null
}

# Find a child by class-name substring and/or exact text, then BM_CLICK it.
function Click-NovaChildByText($text, $classContains = $null, $parentHandle = $null) {
    $children = Get-NovaChildren $parentHandle
    $match = $children | Where-Object { $_.Text -eq $text -and ($null -eq $classContains -or $_.Class -like "*$classContains*") } | Select-Object -First 1
    if (-not $match) { throw "No child window found with text '$text'" }
    Invoke-NovaButtonClick $match.Handle
    return $match
}

function Set-NovaText($handle, $text) {
    [NovaWin32]::SendMessage($handle, $WM_SETTEXT, [IntPtr]0, $text) | Out-Null
}

function Get-NovaText($handle) {
    $len = [NovaWin32]::GetWindowTextLength($handle)
    if ($len -le 0) { return "" }
    $sb = New-Object System.Text.StringBuilder ($len + 1)
    [NovaWin32]::GetWindowText($handle, $sb, $sb.Capacity) | Out-Null
    return $sb.ToString()
}
