using System.Runtime.InteropServices;

namespace TouchProbe;

/// <summary>
/// WinUser.h 中触摸注入相关结构与常量的精确镜像（x64/x86 均按平台自然对齐）。
/// 字段顺序、类型与 Windows SDK 一致；切勿随意增删字段。
/// </summary>
internal static class NativeMethods
{
    // POINTER_FEEDBACK_* (WinUser.h)
    public const uint POINTER_FEEDBACK_DEFAULT = 0x00000001;

    // TOUCH_MASK_*
    public const uint TOUCH_MASK_CONTACTAREA = 0x00000001;

    // POINTER_FLAG_*
    public const uint POINTER_FLAG_NEW = 0x00000001;
    public const uint POINTER_FLAG_INRANGE = 0x00000002;
    public const uint POINTER_FLAG_INCONTACT = 0x00000004;
    public const uint POINTER_FLAG_DOWN = 0x00010000;
    public const uint POINTER_FLAG_UPDATE = 0x00020000;
    public const uint POINTER_FLAG_UP = 0x00040000;

    // POINTER_INPUT_TYPE
    public const int PT_TOUCH = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// typedef struct tagPOINTER_INFO（WinUser.h）。
    /// x64 期望大小 96；x86 期望大小 88。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_INFO
    {
        public int pointerType;          // POINTER_INPUT_TYPE
        public uint pointerId;
        public uint frameId;
        public uint pointerFlags;        // POINTER_FLAGS
        public IntPtr sourceDevice;
        public IntPtr hwndTarget;
        public POINT ptPixelLocation;
        public POINT ptHimetricLocation;
        public POINT ptPixelLocationRaw;
        public POINT ptHimetricLocationRaw;
        public uint dwTime;
        public uint historyCount;
        public int InputData;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;     // POINTER_BUTTON_CHANGE_TYPE
    }

    /// <summary>
    /// typedef struct tagPOINTER_TOUCH_INFO（WinUser.h）。
    /// x64 期望大小 144；x86 期望大小 136。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint touchFlags;          // TOUCH_FLAGS
        public uint touchMask;           // TOUCH_MASK
        public RECT rcContact;
        public RECT rcContactRaw;
        public uint orientation;
        public uint pressure;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InitializeTouchInjection(uint maxCount, uint dwMode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InjectTouchInput(uint count, [In] POINTER_TOUCH_INFO[] contacts);

    /// <summary>构造一个 4×4 接触面积的触摸点（坐标为物理屏幕像素）。</summary>
    public static POINTER_TOUCH_INFO CreateContact(int x, int y, uint flags)
    {
        var contact = new RECT
        {
            Left = x - 2,
            Top = y - 2,
            Right = x + 2,
            Bottom = y + 2
        };

        return new POINTER_TOUCH_INFO
        {
            pointerInfo = new POINTER_INFO
            {
                pointerType = PT_TOUCH,
                pointerId = 1,
                frameId = 0,
                pointerFlags = flags,
                sourceDevice = IntPtr.Zero,
                hwndTarget = IntPtr.Zero,
                ptPixelLocation = new POINT { X = x, Y = y }
            },
            touchFlags = 0,
            touchMask = TOUCH_MASK_CONTACTAREA,
            rcContact = contact,
            rcContactRaw = contact,
            orientation = 0,
            pressure = 0
        };
    }
}
