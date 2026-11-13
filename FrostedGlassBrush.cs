using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AssistantTouch;

/// <summary>
/// 磨砂玻璃球体的雾面噪点画刷：程序生成的 128×128 灰度噪声 WriteableBitmap。
/// 采用「每像素 4 次采样平均 + 环绕模糊 + 低频周期扰动」，纹理细腻且可无缝平铺；
/// 静态冻结画刷、无每帧计算。
/// </summary>
internal static class FrostedGlassBrush
{
    /// <summary>噪声贴图边长（设备无关单位，96 DPI）；铺满 68px 球体时不会出现可见重复周期。</summary>
    private const int NoiseSize = 128;

    private static ImageBrush? _noiseBrush;

    /// <summary>进程内共享的雾面噪点画刷；首次访问时生成并冻结。</summary>
    public static ImageBrush NoiseBrush => _noiseBrush ??= CreateNoiseBrush();

    private static ImageBrush CreateNoiseBrush()
    {
        var random = new Random(0x5EED);
        var values = new double[NoiseSize * NoiseSize];

        // 4 次采样平均：压低粗颗粒方差，得到细腻底噪。
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (random.NextDouble() + random.NextDouble() +
                         random.NextDouble() + random.NextDouble()) / 4.0;
        }

        // 环绕式 3×3 模糊：让四边自然衔接，平铺时无接缝。
        values = BlurWrap(values, NoiseSize);

        var pixels = new byte[NoiseSize * NoiseSize * 4];

        for (int y = 0; y < NoiseSize; y++)
        {
            for (int x = 0; x < NoiseSize; x++)
            {
                // 低频周期扰动（整数周期 -> 平铺无缝），叠在细粒上形成轻微云雾感。
                double lowFrequency =
                    0.06 * Math.Sin(2 * Math.PI * x / NoiseSize + 0.7) *
                           Math.Cos(2 * Math.PI * y / NoiseSize + 1.3);

                double value = 0.5 + (values[y * NoiseSize + x] - 0.5) * 2.0 + lowFrequency;
                value = Math.Clamp(value, 0.0, 1.0);

                byte gray = (byte)Math.Round(value * 255);
                int offset = (y * NoiseSize + x) * 4;
                pixels[offset] = gray;
                pixels[offset + 1] = gray;
                pixels[offset + 2] = gray;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = new WriteableBitmap(NoiseSize, NoiseSize, 96, 96, PixelFormats.Bgra32, null);
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

    /// <summary>环绕式 3×3 均值模糊（边界取模），保证纹理首尾无缝。</summary>
    private static double[] BlurWrap(double[] source, int size)
    {
        var destination = new double[source.Length];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double sum = 0;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int sourceY = (y + dy + size) % size;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int sourceX = (x + dx + size) % size;
                        sum += source[sourceY * size + sourceX];
                    }
                }

                destination[y * size + x] = sum / 9.0;
            }
        }

        return destination;
    }
}
