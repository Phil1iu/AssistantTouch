using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>磨砂噪点画刷的尺寸、冻结状态与纹理质量（细腻、无缝）测试。</summary>
public class FrostedGlassBrushTests
{
    [Fact]
    public void NoiseBrush_IsFrozen128Tile_AndCached()
    {
        ImageBrush brush = FrostedGlassBrush.NoiseBrush;

        Assert.True(brush.IsFrozen);
        Assert.Equal(TileMode.Tile, brush.TileMode);
        Assert.Equal(128, brush.Viewport.Width);
        Assert.Equal(128, brush.Viewport.Height);

        var bitmap = Assert.IsType<WriteableBitmap>(brush.ImageSource);
        Assert.Equal(128, bitmap.PixelWidth);
        Assert.Equal(128, bitmap.PixelHeight);

        Assert.Same(brush, FrostedGlassBrush.NoiseBrush);
    }

    [Fact]
    public void NoiseTexture_IsFineAndSeamless()
    {
        var bitmap = (WriteableBitmap)FrostedGlassBrush.NoiseBrush.ImageSource;
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[height * stride];
        bitmap.CopyPixels(pixels, stride, 0);

        double sum = 0;
        double sumSquares = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Bgra32；三个颜色通道相同，取 B 通道即可。
                byte value = pixels[y * stride + x * 4];
                sum += value;
                sumSquares += value * value;
            }
        }

        double mean = sum / (width * height);
        double std = Math.Sqrt(sumSquares / (width * height) - mean * mean);

        // 纹理要“有细节但不能回到粗颗粒”：经验范围 5..60 灰阶标准差。
        Assert.InRange(std, 5, 60);

        // 无缝：首尾列差异应与内部相邻列差异同量级（环绕模糊 + 周期扰动）。
        double seamDifference = 0;
        for (int y = 0; y < height; y++)
        {
            seamDifference += Math.Abs(pixels[y * stride] - pixels[y * stride + (width - 1) * 4]);
        }

        seamDifference /= height;

        double interiorDifference = 0;
        int interiorCount = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                interiorDifference += Math.Abs(
                    pixels[y * stride + x * 4] - pixels[y * stride + (x + 1) * 4]);
                interiorCount++;
            }
        }

        interiorDifference /= interiorCount;

        Assert.True(
            seamDifference <= interiorDifference * 3 + 3,
            $"seam={seamDifference:0.##}, interior={interiorDifference:0.##}");
    }
}
