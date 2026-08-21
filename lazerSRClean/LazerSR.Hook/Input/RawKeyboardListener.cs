using System;
using System.Runtime.InteropServices;
using System.Threading;
using osuTK.Input;

namespace LazerSR.Hook.Input;

/// <summary>
/// 창 포커스와 무관하게 <b>하드웨어 키 이벤트</b>를 받는다 (Raw Input + <c>RIDEV_INPUTSINK</c>).
/// <para>
/// 패턴 복제 모드에서는 대상 게임이 포커스를 쥐고 있어야 하므로(그래야 그 게임의 핵 방지에 걸릴 여지가
/// 없다) osu!는 포커스를 못 받는다. <c>INPUTSINK</c>는 <b>비포그라운드 창에도 입력을 배달</b>해주는
/// 플래그라, 대상 게임의 입력 경로를 전혀 건드리지 않고 같은 키를 병렬로 받아올 수 있다.
/// </para>
/// <para>
/// 합성 입력(<c>SendInput</c> 등)은 어디에도 쓰지 않는다 — 사용자가 실제로 누른 키를 <b>전달만</b> 한다.
/// </para>
/// </summary>
internal sealed class RawKeyboardListener : IDisposable
{
    /// <summary>키 상태 변화. <b>전용 메시지 스레드에서 호출된다</b> — 수신 측이 스레드 안전해야 한다.</summary>
    public event Action<Key, bool>? KeyChanged;

    private Thread? thread;
    private IntPtr hwnd;
    private uint threadId;

    // 델리게이트가 GC되면 네이티브 쪽에서 죽은 포인터를 호출한다 — 반드시 필드로 붙잡아 둔다.
    private WndProc? wndProcHolder;

    private readonly ManualResetEventSlim ready = new(false);
    private volatile bool disposed;

    public bool Start()
    {
        if (thread != null) return true;

        thread = new Thread(run) { IsBackground = true, Name = "LazerSR RawInput" };
        thread.Start();

        // 창 생성과 장치 등록이 끝날 때까지 기다린다 — 실패 여부를 호출부가 알아야 한다.
        ready.Wait(3000);

        return hwnd != IntPtr.Zero;
    }

    private void run()
    {
        try
        {
            if (!createMessageWindow() || !registerDevice())
            {
                ready.Set();
                return;
            }

            ready.Set();

            // 메시지 전용 창이라 이 루프가 WM_INPUT만 받는다.
            while (!disposed && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] RawKeyboardListener thread failed: {ex}");
        }
        finally
        {
            ready.Set();
            cleanup();
        }
    }

    private const string window_class = "LazerSRRawInputSink";

    private bool createMessageWindow()
    {
        threadId = GetCurrentThreadId();
        wndProcHolder = wndProc;

        IntPtr module = GetModuleHandle(null);

        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProcHolder),
            hInstance = module,
            lpszClassName = window_class,
        };

        // 이미 등록돼 있으면(재진입) 실패하지만 CreateWindowEx는 정상 동작하므로 무시한다.
        RegisterClassEx(ref wc);

        hwnd = CreateWindowEx(0, window_class, string.Empty, 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, module, IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
            HookLog.Write($"[LazerSR] RawKeyboardListener: CreateWindowEx failed ({Marshal.GetLastWin32Error()}).");

        return hwnd != IntPtr.Zero;
    }

    private bool registerDevice()
    {
        var device = new RAWINPUTDEVICE
        {
            UsagePage = 0x01,      // Generic Desktop
            Usage = 0x06,          // Keyboard
            Flags = RIDEV_INPUTSINK,
            Target = hwnd,
        };

        if (RegisterRawInputDevices(new[] { device }, 1, Marshal.SizeOf<RAWINPUTDEVICE>()))
            return true;

        HookLog.Write($"[LazerSR] RawKeyboardListener: RegisterRawInputDevices failed ({Marshal.GetLastWin32Error()}).");
        return false;
    }

    private IntPtr wndProc(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT)
        {
            try
            {
                handleRawInput(lParam);
            }
            catch (Exception ex)
            {
                HookLog.Write($"[LazerSR] RawKeyboardListener.handleRawInput failed: {ex}");
            }
        }

        return DefWindowProc(h, msg, wParam, lParam);
    }

    private void handleRawInput(IntPtr handle)
    {
        int headerSize = Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = (uint)Marshal.SizeOf<RAWINPUTKEYBOARD>();

        IntPtr buffer = Marshal.AllocHGlobal((int)size);

        try
        {
            if (GetRawInputData(handle, RID_INPUT, buffer, ref size, (uint)headerSize) == unchecked((uint)-1))
                return;

            var raw = Marshal.PtrToStructure<RAWINPUTKEYBOARD>(buffer);

            if (raw.Header.Type != RIM_TYPEKEYBOARD)
                return;

            // 키보드가 만들어내는 잡음 값. 무시하지 않으면 엉뚱한 키로 매핑된다.
            if (raw.Keyboard.VKey >= 0xFF)
                return;

            if (RawKeyMap.FromVirtualKey(raw.Keyboard.VKey) is not Key key)
                return;

            bool pressed = (raw.Keyboard.Flags & RI_KEY_BREAK) == 0;

            KeyChanged?.Invoke(key, pressed);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void cleanup()
    {
        if (hwnd != IntPtr.Zero)
        {
            DestroyWindow(hwnd);
            hwnd = IntPtr.Zero;
        }

        wndProcHolder = null;
    }

    public void Dispose()
    {
        if (disposed) return;

        disposed = true;
        KeyChanged = null;

        // 메시지 루프를 깨워서 스스로 빠져나가게 한다.
        if (threadId != 0)
            PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

        thread?.Join(1000);
        thread = null;
        ready.Dispose();
    }

    // ---- Win32 ----

    private const int WM_INPUT = 0x00FF;
    private const int WM_QUIT = 0x0012;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const ushort RI_KEY_BREAK = 0x01;

    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTKEYBOARD
    {
        public RAWINPUTHEADER Header;
        public RAWKEYBOARD Keyboard;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
