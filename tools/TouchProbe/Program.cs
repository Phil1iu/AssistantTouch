using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using static TouchProbe.NativeMethods;

namespace TouchProbe;

/// <summary>
/// 触摸注入探针：先用 Marshal 尺寸/偏移自检 POINTER_TOUCH_INFO 布局，再按需注入。
/// 不启动、不附加 AssistantTouch；坐标使用物理屏幕像素。默认（无参数）只打印信息，不注入。
/// </summary>
internal static class Program
{
    private const uint DownFlags = POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT;
    private const uint MoveFlags = POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT;
    private const uint UpFlags = POINTER_FLAG_UP | POINTER_FLAG_INRANGE;

    private static readonly (string Field, int Offset64, int Offset32)[] PointerInfoOffsetTable =
    {
        ("pointerType", 0, 0),
        ("pointerId", 4, 4),
        ("frameId", 8, 8),
        ("pointerFlags", 12, 12),
        ("sourceDevice", 16, 16),
        ("hwndTarget", 24, 20),
        ("ptPixelLocation", 32, 24),
        ("ptHimetricLocation", 40, 32),
        ("ptPixelLocationRaw", 48, 40),
        ("ptHimetricLocationRaw", 56, 48),
        ("dwTime", 64, 56),
        ("historyCount", 68, 60),
        ("InputData", 72, 64),
        ("dwKeyStates", 76, 68),
        ("PerformanceCount", 80, 72),
        ("ButtonChangeType", 88, 80)
    };

    private static readonly (string Field, int Offset64, int Offset32)[] TouchInfoOffsetTable =
    {
        ("pointerInfo", 0, 0),
        ("touchFlags", 96, 88),
        ("touchMask", 100, 92),
        ("rcContact", 104, 96),
        ("rcContactRaw", 120, 112),
        ("orientation", 136, 128),
        ("pressure", 140, 132)
    };

    private static int Main(string[] args)
    {
        // 让重定向/跨终端捕获的日志保持 UTF-8（部分 Windows 控制台默认 GBK）。
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // 无控制台或句柄不可用时忽略，不影响自检与注入。
        }

        if (!PrintLayoutReport())
        {
            Console.Error.WriteLine("LAYOUT MISMATCH: 结构体尺寸/偏移与 Windows SDK 不符，拒绝注入。");
            return 5;
        }

        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "--help" or "-h" or "help" => PrintHelp(),
            "sizes" => 0,
            "probe" => RunProbe(),
            "tap" => RunTap(args),
            "drag" => RunDrag(args),
            _ => UnknownCommand(args[0])
        };
    }

    /// <summary>打印进程位数、结构体大小与关键字段偏移；返回是否与预期布局一致。</summary>
    private static bool PrintLayoutReport()
    {
        bool is64 = Environment.Is64BitProcess;
        int expectedPointerInfo = is64 ? 96 : 88;
        int expectedTouchInfo = is64 ? 144 : 136;

        int actualPointerInfo = Marshal.SizeOf<POINTER_INFO>();
        int actualTouchInfo = Marshal.SizeOf<POINTER_TOUCH_INFO>();

        Console.WriteLine("TouchProbe — 触摸注入布局自检（不会自动注入）");
        Console.WriteLine($"OS           : {RuntimeInformation.OSDescription}");
        Console.WriteLine($"Process arch : {RuntimeInformation.ProcessArchitecture} (64-bit={is64})");
        Console.WriteLine(
            $"POINTER_INFO       actual={actualPointerInfo,4} expected={expectedPointerInfo,4} " +
            $"{(actualPointerInfo == expectedPointerInfo ? "OK" : "MISMATCH")}");
        Console.WriteLine(
            $"POINTER_TOUCH_INFO actual={actualTouchInfo,4} expected={expectedTouchInfo,4} " +
            $"{(actualTouchInfo == expectedTouchInfo ? "OK" : "MISMATCH")}");

        bool offsetsOk = true;

        Console.WriteLine("POINTER_INFO offsets:");
        foreach ((string field, int offset64, int offset32) in PointerInfoOffsetTable)
        {
            offsetsOk &= PrintOffset<POINTER_INFO>(field, is64 ? offset64 : offset32);
        }

        Console.WriteLine("POINTER_TOUCH_INFO offsets:");
        foreach ((string field, int offset64, int offset32) in TouchInfoOffsetTable)
        {
            offsetsOk &= PrintOffset<POINTER_TOUCH_INFO>(field, is64 ? offset64 : offset32);
        }

        bool ok = actualPointerInfo == expectedPointerInfo &&
                  actualTouchInfo == expectedTouchInfo &&
                  offsetsOk;

        Console.WriteLine(ok ? "LAYOUT OK" : "LAYOUT MISMATCH");
        return ok;
    }

    private static bool PrintOffset<T>(string field, int expected) where T : struct
    {
        int actual = (int)Marshal.OffsetOf<T>(field);
        bool ok = actual == expected;
        Console.WriteLine($"  {field,-22} actual={actual,4} expected={expected,4} {(ok ? "OK" : "MISMATCH")}");
        return ok;
    }

    private static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("用法（默认只自检，不注入）：");
        Console.WriteLine("  TouchProbe                 打印尺寸/偏移与用法（不注入）");
        Console.WriteLine("  TouchProbe sizes           同上（不注入）");
        Console.WriteLine("  TouchProbe probe           初始化 + 向 (100,100) 注入一次 down/up");
        Console.WriteLine("  TouchProbe tap x y         一次点击（down → 60ms → up）");
        Console.WriteLine("  TouchProbe drag x1 y1 x2 y2 [steps]");
        Console.WriteLine("                             按下、逐步移动（默认 10 步）、抬起");
        Console.WriteLine();
        Console.WriteLine("坐标：物理屏幕像素（虚拟桌面坐标系）；多显示器可用负坐标。");
    }

    private static int PrintHelp()
    {
        PrintUsage();
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"未知命令：{command}");
        PrintUsage();
        return 2;
    }

    private static int RunProbe()
    {
        if (!TryInitializeTouchInjection())
        {
            return 3;
        }

        Console.WriteLine("probe: 向 (100,100) 发送 down/up");
        bool downSent = false;
        try
        {
            if (!Inject(CreateContact(100, 100, DownFlags), "DOWN (100,100)"))
            {
                return 4;
            }

            downSent = true;
            Thread.Sleep(60);

            if (!Inject(CreateContact(100, 100, UpFlags), "UP (100,100)"))
            {
                return 4;
            }

            downSent = false;
            Console.WriteLine("probe: 完成");
            return 0;
        }
        finally
        {
            if (downSent)
            {
                Console.WriteLine("probe: 清理未抬起的触点");
                Inject(CreateContact(100, 100, UpFlags), "UP(cleanup) (100,100)");
            }
        }
    }

    private static int RunTap(string[] args)
    {
        if (args.Length != 3 || !TryParsePoint(args[1], args[2], out int x, out int y))
        {
            Console.Error.WriteLine("用法：TouchProbe tap x y");
            return 2;
        }

        if (!TryInitializeTouchInjection())
        {
            return 3;
        }

        Console.WriteLine($"tap: ({x},{y})");
        bool downSent = false;
        try
        {
            if (!Inject(CreateContact(x, y, DownFlags), $"DOWN ({x},{y})"))
            {
                return 4;
            }

            downSent = true;
            Thread.Sleep(60);

            if (!Inject(CreateContact(x, y, UpFlags), $"UP ({x},{y})"))
            {
                return 4;
            }

            downSent = false;
            Console.WriteLine("tap: 完成");
            return 0;
        }
        finally
        {
            if (downSent)
            {
                Console.WriteLine("tap: 清理未抬起的触点");
                Inject(CreateContact(x, y, UpFlags), "UP(cleanup)");
            }
        }
    }

    private static int RunDrag(string[] args)
    {
        if (args.Length is < 5 or > 6 ||
            !TryParsePoint(args[1], args[2], out int x1, out int y1) ||
            !TryParsePoint(args[3], args[4], out int x2, out int y2))
        {
            Console.Error.WriteLine("用法：TouchProbe drag x1 y1 x2 y2 [steps]");
            return 2;
        }

        int steps = 10;
        if (args.Length == 6 &&
            (!int.TryParse(args[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out steps) || steps < 1))
        {
            Console.Error.WriteLine("steps 必须是 >=1 的整数");
            return 2;
        }

        steps = Math.Clamp(steps, 1, 200);

        if (!TryInitializeTouchInjection())
        {
            return 3;
        }

        Console.WriteLine($"drag: ({x1},{y1}) -> ({x2},{y2})，{steps} 步");
        bool downSent = false;
        try
        {
            if (!Inject(CreateContact(x1, y1, DownFlags), $"DOWN ({x1},{y1})"))
            {
                return 4;
            }

            downSent = true;

            for (int step = 1; step <= steps; step++)
            {
                double t = (double)step / steps;
                int x = (int)Math.Round(x1 + (x2 - x1) * t, MidpointRounding.AwayFromZero);
                int y = (int)Math.Round(y1 + (y2 - y1) * t, MidpointRounding.AwayFromZero);

                if (!Inject(CreateContact(x, y, MoveFlags), $"MOVE {step}/{steps} ({x},{y})"))
                {
                    return 4;
                }

                Thread.Sleep(10);
            }

            if (!Inject(CreateContact(x2, y2, UpFlags), $"UP ({x2},{y2})"))
            {
                return 4;
            }

            downSent = false;
            Console.WriteLine("drag: 完成");
            return 0;
        }
        finally
        {
            if (downSent)
            {
                Console.WriteLine("drag: 清理未抬起的触点");
                Inject(CreateContact(x2, y2, UpFlags), $"UP(cleanup) ({x2},{y2})");
            }
        }
    }

    private static bool TryParsePoint(string xs, string ys, out int x, out int y)
    {
        bool okX = int.TryParse(xs, NumberStyles.Integer, CultureInfo.InvariantCulture, out x);
        bool okY = int.TryParse(ys, NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
        return okX && okY;
    }

    /// <summary>
    /// 初始化触摸注入。按 D51：首选 (10, POINTER_FEEDBACK_DEFAULT)；失败再试 (1,0)、(10,0)，
    /// 每次失败都打印 GetLastError；全部失败则不注入。不绕过系统策略。
    /// </summary>
    private static bool TryInitializeTouchInjection()
    {
        (uint Count, uint Mode, string Label)[] attempts =
        {
            (10, POINTER_FEEDBACK_DEFAULT, "10, POINTER_FEEDBACK_DEFAULT(1)"),
            (1, 0, "1, 0 (no feedback)"),
            (10, 0, "10, 0 (no feedback)")
        };

        foreach ((uint count, uint mode, string label) in attempts)
        {
            if (InitializeTouchInjection(count, mode))
            {
                Console.WriteLine($"InitializeTouchInjection({label}) -> TRUE");
                return true;
            }

            int error = Marshal.GetLastWin32Error();
            Console.WriteLine(
                $"InitializeTouchInjection({label}) -> FALSE, GetLastError={error} ({DescribeError(error)})");
        }

        Console.Error.WriteLine("所有 InitializeTouchInjection 尝试均失败；未注入。");
        return false;
    }

    private static bool Inject(POINTER_TOUCH_INFO contact, string label)
    {
        bool ok = InjectTouchInput(1, new[] { contact });
        int error = ok ? 0 : Marshal.GetLastWin32Error();

        POINT point = contact.pointerInfo.ptPixelLocation;
        string result = ok
            ? "TRUE"
            : $"FALSE, GetLastError={error} ({DescribeError(error)})";

        Console.WriteLine(
            $"InjectTouchInput({label}) flags=0x{contact.pointerInfo.pointerFlags:X5} " +
            $"pt=({point.X},{point.Y}) rc=({contact.rcContact.Left},{contact.rcContact.Top}," +
            $"{contact.rcContact.Right},{contact.rcContact.Bottom}) -> {result}");

        return ok;
    }

    private static string DescribeError(int error)
    {
        if (error == 0)
        {
            return "no error";
        }

        try
        {
            return new Win32Exception(error).Message;
        }
        catch (Exception)
        {
            return "unknown error";
        }
    }
}
