using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AssistantTouch;

/// <summary>
/// 磨砂玻璃球体的雾面噪点画刷：程序生成的小尺寸灰度噪声 WriteableBitmap，铺成可平铺的 TileBrush。
/// 纯 WPF 实现（不使用 DWM / SetWindowCompositionAttribute），静态画刷、无每帧计算。
/// </summary>
internal static class FrostedGlassBrush
{
    /// <summary>噪声贴图边长（设备无关单位，96 DPI）。</summary>
    private const int NoiseSize = 48;

    private static ImageBrush? _noiseBrush;

    /// <summary>进程内共享的雾面噪点画刷；首次访问时生成并冻结。</summary>
    public static ImageBrush NoiseBrush => _noiseBrush ??= CreateNoiseBrush();

    private static ImageBrush CreateNoiseBrush()
    {
        var bitmap = new WriteableBitmap(NoiseSize, NoiseSize, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[NoiseSize * NoiseSize * 4];

        // 固定种子：每次启动的纹理一致，避免观感随机跳动。
        var random = new Random(0x5EED);

        for (int i = 0; i < NoiseSize * NoiseSize; i++)
        {
            byte value = (byte)random.Next(0, 256);
            int offset = i * 4;
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }

        bitmap.WritePixels(
            new Int32Rect(0, 0, NoiseSize, NoiseSize),
            pixels,
            NoiseSize * 4,
            0);
        bitmap.Freeze();

        var brush = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, NoiseSize, NoiseSize),
            Stretch = Stretch.Fill
        };
        brush.Freeze();

        return brush;
    }
}
