using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

/// <summary>
/// 앱 아이콘을 코드로 그린다: 보랏빛 배경 위의 검은 섬, 그 위에 올라앉은 도트 고양이.
/// 개발용 스위치 --make-icon 으로 Assets\app.ico 를 다시 만들 수 있다.
/// </summary>
public static class AppIcon
{
    static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    static Color C(string hex) => Util.ParseColor(hex, Colors.Black);

    public static BitmapSource Render(int s)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = new LinearGradientBrush(new GradientStopCollection
            {
                new(C("#5B6CFF"), 0),
                new(C("#A77BFF"), 0.55),
                new(C("#FF9EC7"), 1),
            }, new Point(0.1, 0), new Point(0.9, 1));
            double corner = s * 0.225;
            dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, s, s), corner, corner);

            // 작은 크기에서는 고양이를 빼고 섬만 크게 그려야 알아볼 수 있다
            bool cat = s >= 32;
            double pw = Math.Round(s * (cat ? 0.76 : 0.80));
            double ph = Math.Round(s * (cat ? 0.25 : 0.36));
            double px = Math.Round((s - pw) / 2);
            double py = cat ? Math.Round(s * (s < 64 ? 0.62 : 0.56)) : Math.Round((s - ph) / 2);

            double drop = Math.Max(1, s * 0.025);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(70, 20, 10, 60)), null,
                new Rect(px, py + drop, pw, ph), ph / 2, ph / 2);
            dc.DrawRoundedRectangle(new SolidColorBrush(C("#0B0B10")), null,
                new Rect(px, py, pw, ph), ph / 2, ph / 2);

            if (s >= 48)
            {
                // 섬 안: 왼쪽에 앨범 점, 오른쪽에 음악 막대
                double cy = py + ph / 2;
                double dot = ph * 0.27;
                dc.DrawEllipse(new SolidColorBrush(C("#FF9EC7")), null, new Point(px + ph * 0.55, cy), dot, dot);
                var bar = new SolidColorBrush(C("#7CF2B0"));
                double bw = Math.Max(1.5, ph * 0.11), gap = bw * 0.9;
                double[] heights = { 0.30, 0.56, 0.40 };
                double right = px + pw - ph * 0.42;
                for (int i = 0; i < heights.Length; i++)
                {
                    double h = ph * heights[i];
                    double x = right - (heights.Length - i) * (bw + gap);
                    dc.DrawRoundedRectangle(bar, null, new Rect(x, cy - h / 2, bw, h), bw / 2, bw / 2);
                }
            }

            if (cat) DrawCat(dc, s, py);
        }
        var bmp = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>기본 고양이 도트를 섬 위에 발이 닿게 그린다. 도트가 흐려지지 않도록 칸 크기는 정수로 맞춘다.</summary>
    static void DrawCat(DrawingContext dc, int s, double groundY)
    {
        var (c1, c2) = Sprites.DefaultColors("Cat");
        var palette = Sprites.BuiltInPalette(c1, c2).Select(hex => (Brush)Util.Brush(hex)).ToArray();
        var rows = Sprites.BuiltInFrame("Cat", 0);

        int top = int.MaxValue, bottom = -1, left = int.MaxValue, right = -1;
        for (int y = 0; y < rows.Count; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (Sprites.IndexOf(rows[y][x]) >= 0)
                {
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                    left = Math.Min(left, x); right = Math.Max(right, x);
                }
        if (bottom < 0) return;
        int h = bottom - top + 1, w = right - left + 1;

        int cell = Math.Max(1, (int)Math.Round(s * 0.44 / h));
        while (cell > 1 && groundY - h * cell < s * 0.03) cell--;
        double ox = Math.Round((s - w * cell) / 2.0), oy = groundY - h * cell;

        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                int index = Sprites.IndexOf(rows[y][x]);
                if (index < 0 || index >= palette.Length) continue;
                dc.DrawRectangle(palette[index], null, new Rect(ox + (x - left) * cell, oy + (y - top) * cell, cell, cell));
            }
    }

    /// <summary>app.ico(여러 크기)와 눈으로 확인할 미리보기 PNG를 폴더에 저장한다.</summary>
    public static void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        var images = Sizes.Select(Render).ToArray();
        WriteIco(Path.Combine(folder, "app.ico"), images);

        // 미리보기: 모든 크기를 크게 늘려 한 장에 나란히
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        double x = 12, height = 0;
        using (var dc = visual.RenderOpen())
        {
            foreach (var img in images)
                height = Math.Max(height, img.PixelWidth * Math.Max(1, 96 / img.PixelWidth));
            double total = 12 + images.Sum(i => i.PixelWidth * Math.Max(1, 96 / i.PixelWidth) + 12);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(238, 238, 242)), null, new Rect(0, 0, total / 2, height + 24));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(32, 32, 38)), null, new Rect(total / 2, 0, total / 2, height + 24));
            foreach (var img in images)
            {
                double size = img.PixelWidth * Math.Max(1, 96 / img.PixelWidth);
                dc.DrawImage(img, new Rect(x, 12, size, size));
                x += size + 12;
            }
        }
        var sheet = new RenderTargetBitmap((int)x, (int)height + 24, 96, 96, PixelFormats.Pbgra32);
        sheet.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(sheet));
        using var file = File.Create(Path.Combine(folder, "app-preview.png"));
        png.Save(file);
    }

    static void WriteIco(string path, BitmapSource[] images)
    {
        var blobs = images.Select(Encode).ToArray();
        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0); w.Write((short)1); w.Write((short)images.Length);
        int offset = 6 + 16 * images.Length;
        for (int i = 0; i < images.Length; i++)
        {
            int s = images[i].PixelWidth;
            w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(blobs[i].Length); w.Write(offset);
            offset += blobs[i].Length;
        }
        foreach (var blob in blobs) w.Write(blob);
    }

    /// <summary>256은 PNG로, 그보다 작은 크기는 어디서나 읽히는 32비트 비트맵으로 넣는다.</summary>
    static byte[] Encode(BitmapSource image)
    {
        int s = image.PixelWidth;
        using var ms = new MemoryStream();
        if (s >= 256)
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(image));
            png.Save(ms);
            return ms.ToArray();
        }
        var straight = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[s * s * 4];
        straight.CopyPixels(pixels, s * 4, 0);
        int maskStride = (s + 31) / 32 * 4;
        using var w = new BinaryWriter(ms);
        w.Write(40); w.Write(s); w.Write(s * 2);
        w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(s * s * 4 + maskStride * s);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = s - 1; y >= 0; y--) w.Write(pixels, y * s * 4, s * 4);
        w.Write(new byte[maskStride * s]);
        w.Flush();
        return ms.ToArray();
    }
}
