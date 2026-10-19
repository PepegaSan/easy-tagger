using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;
using Forms = System.Windows.Forms;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace EasyTagger.App;

static class SelectedFiles
{
    const uint CfHdrop = 15;
    const uint CfUnicodeText = 13;
    const uint GaRoot = 2;
    const byte VkControl = 0x11;
    const byte VkC = 0x43;
    const uint KeyUp = 2;

    // Text, images and copied files. "Preferred DropEffect" keeps a cut in Explorer a cut.
    static readonly string[] KeptFormats =
    [
        Forms.DataFormats.UnicodeText,
        Forms.DataFormats.Text,
        Forms.DataFormats.Rtf,
        Forms.DataFormats.Html,
        Forms.DataFormats.CommaSeparatedValue,
        Forms.DataFormats.FileDrop,
        "Preferred DropEffect",
        Forms.DataFormats.Bitmap,
    ];

    public static string LastNote { get; private set; } = "";

    // Read while WM_HOTKEY is still handled, before BeginInvoke runs later code.
    public static IntPtr ForegroundNow() => GetForegroundWindow();

    public static List<string> Capture(IntPtr foregroundAtHotkey = default)
    {
        LastNote = "";
        var foreground = foregroundAtHotkey != IntPtr.Zero ? foregroundAtHotkey : GetForegroundWindow();
        var fromShell = FromExplorer(foreground);
        if (fromShell.Count > 0)
            return fromShell;
        FocusSpanFile(foreground);
        return FromClipboardCopy(foreground);
    }

    static List<string> FromExplorer(IntPtr foreground)
    {
        var paths = new List<string>();
        if (foreground == IntPtr.Zero)
            return paths;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null)
                return paths;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();
            int count = windows.Count;
            for (var index = 0; index < count; index++)
            {
                dynamic window = windows.Item(index);
                try
                {
                    var handle = new IntPtr(Convert.ToInt64(window.HWND));
                    if (!OwnsShellWindow(handle, foreground))
                        continue;
                    dynamic items = window.Document.SelectedItems();
                    int selected = items.Count;
                    for (var itemIndex = 0; itemIndex < selected; itemIndex++)
                        paths.Add((string)items.Item(itemIndex).Path);
                    if (paths.Count > 0)
                        break;
                }
                catch (Exception ex) when (ex is COMException or RuntimeBinderException)
                {
                    // This shell window is not a folder view, for example the Control Panel.
                }
            }
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException)
        {
            return [];
        }

        return ExistingFiles(paths);
    }

    static List<string> FromClipboardCopy(IntPtr foreground)
    {
        var backup = SnapshotClipboard();
        if (!TryEmptyClipboard())
        {
            LastNote = UiText.Get("clipboard-busy");
            return [];
        }

        if (foreground != IntPtr.Zero)
        {
            SetForegroundWindow(foreground);
            Thread.Sleep(80);
        }

        WaitUntilModifiersReleased();
        var sequence = GetClipboardSequenceNumber();
        TapCopy();

        // Erst nach Strg+C merken. Eine Änderung durch das Leeren darf nicht
        // als fertige Dateiliste gelten. SPAN Finder schreibt den Pfad erst
        // nach dem Loslassen der Tasten.
        var paths = new List<string>();
        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < 2500)
        {
            if (GetClipboardSequenceNumber() == sequence)
            {
                Thread.Sleep(40);
                continue;
            }

            Thread.Sleep(100);
            paths = ReadClipboardFiles();
            if (paths.Count > 0)
                break;
            sequence = GetClipboardSequenceNumber();
        }

        if (paths.Count == 0)
        {
            paths = ReadClipboardFiles();
            if (paths.Count == 0)
                LastNote = string.Format(UiText.Get("clipboard-empty"), DescribeFormats());
        }

        try
        {
            if (backup != null)
                Forms.Clipboard.SetDataObject(backup, copy: true);
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            // The previous clipboard contents could not be restored.
        }

        return ExistingFiles(paths);
    }

    // The object GetDataObject returns reads the clipboard live, so after Ctrl+C it would hand back
    // the file paths, and putting it back fails. The data is copied out while it is still there.
    static Forms.DataObject? SnapshotClipboard()
    {
        Forms.IDataObject? live;
        try
        {
            live = Forms.Clipboard.GetDataObject();
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            return null;
        }
        if (live == null)
            return null;

        var copy = new Forms.DataObject();
        var kept = false;
        foreach (var format in KeptFormats)
        {
            try
            {
                if (!live.GetDataPresent(format, autoConvert: false))
                    continue;
                var data = live.GetData(format, autoConvert: false);
                if (data == null)
                    continue;
                copy.SetData(format, autoConvert: false, data);
                kept = true;
            }
            catch (Exception ex) when (ex is COMException or ExternalException or ArgumentException or OutOfMemoryException)
            {
                // This format cannot be read. The others are still worth keeping.
            }
        }
        return kept ? copy : null;
    }

    static void TapCopy()
    {
        var scanControl = (byte)MapVirtualKey(VkControl, 0);
        var scanC = (byte)MapVirtualKey(VkC, 0);
        keybd_event(VkControl, scanControl, 0, UIntPtr.Zero);
        keybd_event(VkC, scanC, 0, UIntPtr.Zero);
        keybd_event(VkC, scanC, KeyUp, UIntPtr.Zero);
        keybd_event(VkControl, scanControl, KeyUp, UIntPtr.Zero);
    }

    static bool TryEmptyClipboard()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    return EmptyClipboard();
                }
                finally
                {
                    CloseClipboard();
                }
            }

            Thread.Sleep(30);
        }

        return false;
    }

    static List<string> ReadClipboardFiles()
    {
        try
        {
            if (Forms.Clipboard.ContainsFileDropList())
            {
                var dropped = new List<string>();
                foreach (string? path in Forms.Clipboard.GetFileDropList())
                {
                    if (!string.IsNullOrWhiteSpace(path))
                        dropped.Add(path);
                }
                if (dropped.Count > 0)
                    return dropped;
            }
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            // The WinRT clipboard is not ready for the managed reader yet.
        }

        var native = ReadFileDrop();
        if (native.Count > 0)
            return native;
        return ReadUnicodeText()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Trim('"'))
            .Where(line => line.Length > 0)
            .ToList();
    }

    static string ReadUnicodeText()
    {
        if (!IsClipboardFormatAvailable(CfUnicodeText) || !OpenClipboard(IntPtr.Zero))
            return "";
        try
        {
            var handle = GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero)
                return "";
            var locked = GlobalLock(handle);
            if (locked == IntPtr.Zero)
                return "";
            try
            {
                return Marshal.PtrToStringUni(locked) ?? "";
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    static List<string> ReadFileDrop()
    {
        var paths = new List<string>();
        if (!IsClipboardFormatAvailable(CfHdrop) || !OpenClipboard(IntPtr.Zero))
            return paths;
        try
        {
            var handle = GetClipboardData(CfHdrop);
            if (handle == IntPtr.Zero)
                return paths;
            var locked = GlobalLock(handle);
            try
            {
                var count = DragQueryFile(handle, 0xFFFFFFFF, null, 0);
                var drop = handle;
                if (count == 0 && locked != IntPtr.Zero)
                {
                    drop = locked;
                    count = DragQueryFile(drop, 0xFFFFFFFF, null, 0);
                }
                if (count > 4096)
                    return paths;
                for (uint index = 0; index < count; index++)
                {
                    var length = DragQueryFile(drop, index, null, 0);
                    if (length == 0 || length > 32768)
                        continue;
                    var buffer = new StringBuilder((int)length + 1);
                    DragQueryFile(drop, index, buffer, length + 1);
                    if (buffer.Length > 0)
                        paths.Add(buffer.ToString());
                }
            }
            finally
            {
                if (locked != IntPtr.Zero)
                    GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
        return paths;
    }

    static List<string> ExistingFiles(IEnumerable<string> paths)
    {
        var files = new List<string>();
        var rejected = new List<string>();
        foreach (var raw in paths)
        {
            var path = raw.Trim().Trim('"').Trim('\0');
            if (path.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                path = Uri.UnescapeDataString(path[8..]).Replace('/', '\\');
            if (File.Exists(path))
                files.Add(Path.GetFullPath(path));
            else if (path.Length > 0)
                rejected.Add(path);
        }

        if (files.Count == 0 && rejected.Count > 0)
            LastNote = string.Format(UiText.Get("clipboard-rejected"), string.Join(" | ", rejected.Take(3)));
        return files;
    }

    static string DescribeFormats()
    {
        if (!OpenClipboard(IntPtr.Zero))
            return UiText.Get("unreadable");
        try
        {
            var names = new List<string>();
            var format = EnumClipboardFormats(0);
            while (format != 0 && names.Count < 8)
            {
                names.Add(format.ToString());
                format = EnumClipboardFormats(format);
            }

            return names.Count == 0 ? "keine" : string.Join(",", names);
        }
        finally
        {
            CloseClipboard();
        }
    }

    // Explorer focus can sit on an inner list view; the shell window is an ancestor of it.
    static bool OwnsShellWindow(IntPtr shellHwnd, IntPtr active)
    {
        if (shellHwnd == IntPtr.Zero || active == IntPtr.Zero)
            return false;
        if (shellHwnd == active)
            return true;
        for (var walk = active; walk != IntPtr.Zero; walk = GetParent(walk))
        {
            if (walk == shellHwnd)
                return true;
        }
        return GetAncestor(shellHwnd, GaRoot) == GetAncestor(active, GaRoot);
    }

    static void FocusSpanFile(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero || !IsSpanFinder(foreground))
            return;
        try
        {
            var window = AutomationElement.FromHandle(foreground);
            if (window == null)
                return;
            var selectedFile = new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, "Span.ViewModels.FileViewModel"),
                new PropertyCondition(SelectionItemPattern.IsSelectedProperty, true));
            var file = window.FindFirst(TreeScope.Descendants, selectedFile);
            if (file == null)
                return;
            SetForegroundWindow(foreground);
            file.SetFocus();
            Thread.Sleep(150);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            // The file row disappeared while the hotkey was read.
        }
    }

    static bool IsSpanFinder(IntPtr hwnd)
    {
        var title = new StringBuilder(512);
        GetWindowText(hwnd, title, title.Capacity);
        if (title.ToString().Contains("SPAN Finder", StringComparison.OrdinalIgnoreCase))
            return true;
        GetWindowThreadProcessId(hwnd, out var processId);
        try
        {
            return Process.GetProcessById((int)processId).ProcessName.Equals("Span", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static void WaitUntilModifiersReleased()
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (!AnyHotkeyHeld())
                return;
            Thread.Sleep(40);
        }
    }

    static bool AnyHotkeyHeld()
    {
        if ((GetAsyncKeyState(VkControl) & 0x8000) != 0)
            return true;
        if ((GetAsyncKeyState(0x12) & 0x8000) != 0)
            return true;
        if ((GetAsyncKeyState(0x10) & 0x8000) != 0)
            return true;
        for (var vk = 0x70; vk <= 0x87; vk++)
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                return true;
        }
        return false;
    }

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte virtualKey, byte scan, uint flags, UIntPtr extra);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    static extern bool OpenClipboard(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll")]
    static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    static extern uint GetClipboardSequenceNumber();

    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("user32.dll")]
    static extern IntPtr GetClipboardData(uint format);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? buffer, uint length);
}
