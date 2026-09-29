using System.Runtime.InteropServices;

namespace EasyTagger.App;

static class SingleInstance
{
    const string MutexName = "Local\\EasyTagger.SingleInstance";
    static Mutex? _mutex;

    public static uint ShowMessage { get; } = RegisterWindowMessage("EasyTagger.Show");

    public static bool TryOwn(bool showExisting)
    {
        _mutex = new Mutex(false, MutexName);
        try
        {
            if (_mutex.WaitOne(0))
                return true;
        }
        catch (AbandonedMutexException)
        {
            return true;
        }

        _mutex.Dispose();
        _mutex = null;
        if (showExisting && ShowMessage != 0)
            PostMessage((IntPtr)0xFFFF, ShowMessage, IntPtr.Zero, IntPtr.Zero);
        return false;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
