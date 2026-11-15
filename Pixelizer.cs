using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

/// <summary>
/// 캐릭터 사진/그림을 16x16 도트 스프라이트로 바꾼다.
/// 배경 지우기 → 캐릭터만 잘라 내기 → 16칸으로 줄이기 → 색 수 줄이기 → (선택) 외곽선 → 걷는 프레임 만들기.
/// </summary>
public static class Pixelizer
{
    const int N = Sprites.N;
    public const string OutlineColor = "#22202A";

    public sealed record Result(List<string> Palette, List<string> FrameA, List<string> FrameB);

    public static Result Convert(string path, int colors, bool removeBackground, bool outline)
    {
        // 1. 읽기 (너무 큰 사진은 줄여서)
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.UriSource = new Uri(path);
        img.DecodePixelWidth = 320;
        img.EndInit();
        var src = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight;
        var px = new byte[w * h * 4];
        src.CopyPixels(px, w * 4, 0);

        // 2. 어디가 캐릭터인지
        var fg = Foreground(px, w, h, removeBackground);

        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!fg[y * w + x]) continue;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
            }
        if (x1 < 0) { x0 = y0 = 0; x1 = w - 1; y1 = h - 1; Array.Fill(fg, true); }

        // 3. 캐릭터가 꽉 차도록 격자에 맞춘다 (외곽선을 두르려면 한 칸씩 여백)
        int margin = outline ? 1 : 0, size = N - margin * 2;
        double bw = x1 - x0 + 1, bh = y1 - y0 + 1, cell = Math.Max(bw, bh) / size;
        int gw = Math.Clamp((int)Math.Ceiling(bw / cell - 0.01), 1, size);
        int gh = Math.Clamp((int)Math.Ceiling(bh / cell - 0.01), 1, size);
        int offX = margin + (size - gw) / 2, offY = margin + (size - gh); // 가로는 가운데, 세로는 바닥에 붙인다

        // 4. 캐릭터 전체에서 대표색을 먼저 뽑는다 (픽셀이 많으면 띄엄띄엄 본다)
        int fgCount = fg.Count(v => v), step = Math.Max(1, fgCount / 6000), seen = 0;
        var sample = new List<double[]>();
        for (int i = 0; i < fg.Length; i++)
        {
            if (!fg[i] || seen++ % step != 0) continue;
            sample.Add(new double[] { px[i * 4 + 2], px[i * 4 + 1], px[i * 4] });
        }
        var centers = Quantize(sample, Math.Clamp(colors, 1, 15));
        int k = centers.Count;

        // 몸 색과 많이 다른 색(눈, 볼, 무늬)은 작아도 살아남도록 표에 가중치를 준다
        var mean = new double[3];
        foreach (var p in sample) for (int i = 0; i < 3; i++) mean[i] += p[i] / sample.Count;
        var weight = new double[k];
        double far = 1;
        for (int i = 0; i < k; i++) far = Math.Max(far, Math.Sqrt(Dist(centers[i], mean)));
        for (int i = 0; i < k; i++) weight[i] = 1 + 1.6 * Math.Sqrt(Dist(centers[i], mean)) / far;

        // 5. 각 칸은 평균색이 아니라 "그 칸에서 가장 우세한 대표색"으로 — 그래야 또렷한 도트가 된다
        var cellIndex = new int[N, N];
        var used = new bool[k];
        var votes = new double[k];
        var probe = new double[3];
        for (int gy = 0; gy < N; gy++) for (int gx = 0; gx < N; gx++) cellIndex[gy, gx] = -1;
        for (int gy = 0; gy < gh; gy++)
        {
            for (int gx = 0; gx < gw; gx++)
            {
                int sx0 = x0 + (int)(gx * cell), sx1 = Math.Min(x1 + 1, Math.Max(sx0 + 1, x0 + (int)((gx + 1) * cell)));
                int sy0 = y0 + (int)(gy * cell), sy1 = Math.Min(y1 + 1, Math.Max(sy0 + 1, y0 + (int)((gy + 1) * cell)));
                Array.Clear(votes);
                int count = 0, total = 0;
                for (int y = sy0; y < sy1; y++)
                    for (int x = sx0; x < sx1; x++)
                    {
                        total++;
                        int i = y * w + x;
                        if (!fg[i]) continue;
                        probe[0] = px[i * 4 + 2]; probe[1] = px[i * 4 + 1]; probe[2] = px[i * 4];
                        int c = Nearest(centers, probe);
                        votes[c] += weight[c];
                        count++;
                    }
                if (total == 0 || count < total * 0.4 || k == 0) continue;
                int best = 0;
                for (int i = 1; i < k; i++) if (votes[i] > votes[best]) best = i;
                cellIndex[offY + gy, offX + gx] = best;
                used[best] = true;
            }
        }

        // 실제로 쓰인 색만 팔레트에 담는다 (0번은 외곽선 색)
        var palette = new List<string> { OutlineColor };
        var remap = new int[k];
        for (int i = 0; i < k; i++)
        {
            if (!used[i]) continue;
            remap[i] = palette.Count;
            var c = centers[i];
            palette.Add(Util.ToHex(Color.FromRgb((byte)Math.Round(c[0]), (byte)Math.Round(c[1]), (byte)Math.Round(c[2]))));
        }

        var grid = new char[N, N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                grid[y, x] = cellIndex[y, x] >= 0 ? Sprites.IndexChars[remap[cellIndex[y, x]]] : '.';

        // 6. 외곽선: 캐릭터와 맞닿은 빈칸을 어두운 색으로
        if (outline)
        {
            var edge = new List<(int x, int y)>();
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    if (grid[y, x] != '.') continue;
                    if ((x > 0 && grid[y, x - 1] != '.') || (x < N - 1 && grid[y, x + 1] != '.') ||
                        (y > 0 && grid[y - 1, x] != '.') || (y < N - 1 && grid[y + 1, x] != '.'))
                        edge.Add((x, y));
                }
            foreach (var (x, y) in edge) grid[y, x] = '0';
        }

        var a = ToRows(grid);
        return new Result(palette, a, Bob(a));
    }

    /// <summary>투명 배경이 있으면 그대로 쓰고, 없으면 테두리에서 이어진 배경색 영역을 지운다.</summary>
    static bool[] Foreground(byte[] px, int w, int h, bool removeBackground)
    {
        int n = w * h;
        var fg = new bool[n];
        int transparent = 0;
        for (int i = 0; i < n; i++) if (px[i * 4 + 3] < 128) transparent++;
        if (transparent > n * 0.02)
        {
            for (int i = 0; i < n; i++) fg[i] = px[i * 4 + 3] >= 128;
            return fg;
        }
        if (!removeBackground)
        {
            Array.Fill(fg, true);
            return fg;
        }

        // 테두리에서 가장 흔한 색 = 배경색
        var buckets = new Dictionary<int, (long r, long g, long b, int n)>();
        void Sample(int x, int y)
        {
            int i = (y * w + x) * 4;
            int key = (px[i + 2] >> 4) << 8 | (px[i + 1] >> 4) << 4 | px[i] >> 4;
            buckets.TryGetValue(key, out var v);
            buckets[key] = (v.r + px[i + 2], v.g + px[i + 1], v.b + px[i], v.n + 1);
        }
        for (int x = 0; x < w; x++) { Sample(x, 0); Sample(x, h - 1); }
        for (int y = 0; y < h; y++) { Sample(0, y); Sample(w - 1, y); }
        var best = buckets.Values.MaxBy(v => v.n);
        double br = (double)best.r / best.n, bg = (double)best.g / best.n, bb = (double)best.b / best.n;

        const double tolerance = 46;
        bool IsBackground(int i)
        {
            double dr = px[i * 4 + 2] - br, dg = px[i * 4 + 1] - bg, db = px[i * 4] - bb;
            return dr * dr + dg * dg + db * db < tolerance * tolerance;
        }

        var seen = new bool[n];
        var stack = new Stack<int>();
        void Push(int x, int y)
        {
            int i = y * w + x;
            if (seen[i] || !IsBackground(i)) return;
            seen[i] = true;
            stack.Push(i);
        }
        for (int x = 0; x < w; x++) { Push(x, 0); Push(x, h - 1); }
        for (int y = 0; y < h; y++) { Push(0, y); Push(w - 1, y); }
        while (stack.Count > 0)
        {
            int i = stack.Pop(), x = i % w, y = i / w;
            if (x > 0) Push(x - 1, y);
            if (x < w - 1) Push(x + 1, y);
            if (y > 0) Push(x, y - 1);
            if (y < h - 1) Push(x, y + 1);
        }

        int kept = 0;
        for (int i = 0; i < n; i++) if (fg[i] = !seen[i]) kept++;
        if (kept < n * 0.01) Array.Fill(fg, true); // 전부 배경으로 보였다면 지우지 않는다
        return fg;
    }

    /// <summary>k-평균으로 대표색 k개를 고른다. 시작점은 서로 가장 먼 색들.</summary>
    static List<double[]> Quantize(List<double[]> points, int k)
    {
        var centers = new List<double[]>();
        if (points.Count == 0) return centers;
        k = Math.Min(k, points.Count);

        var mean = new double[3];
        foreach (var p in points) for (int i = 0; i < 3; i++) mean[i] += p[i] / points.Count;
        centers.Add((double[])points[Nearest(points, mean)].Clone());
        while (centers.Count < k)
        {
            double far = -1;
            double[]? pick = null;
            foreach (var p in points)
            {
                double d = Dist(p, centers[Nearest(centers, p)]);
                if (d > far) { far = d; pick = p; }
            }
            if (pick == null || far < 1) break; // 더 이상 다른 색이 없다
            centers.Add((double[])pick.Clone());
        }

        for (int iter = 0; iter < 14; iter++)
        {
            var sum = new double[centers.Count, 3];
            var count = new int[centers.Count];
            foreach (var p in points)
            {
                int c = Nearest(centers, p);
                for (int i = 0; i < 3; i++) sum[c, i] += p[i];
                count[c]++;
            }
            for (int c = 0; c < centers.Count; c++)
                if (count[c] > 0)
                    for (int i = 0; i < 3; i++) centers[c][i] = sum[c, i] / count[c];
        }
        return centers;
    }

    static double Dist(double[] a, double[] b)
    {
        double dr = a[0] - b[0], dg = a[1] - b[1], db = a[2] - b[2];
        return dr * dr * 0.9 + dg * dg * 1.2 + db * db * 0.7; // 눈은 초록 차이에 더 민감하다
    }

    static int Nearest(List<double[]> list, double[] p)
    {
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            double d = Dist(list[i], p);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    static List<string> ToRows(char[,] grid)
    {
        var rows = new List<string>(N);
        var buf = new char[N];
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++) buf[x] = grid[y, x];
            rows.Add(new string(buf));
        }
        return rows;
    }

    /// <summary>걷는 느낌이 나도록, 발(맨 아래 세 줄)은 그대로 두고 몸만 한 칸 내려앉힌 두 번째 프레임.</summary>
    public static List<string> Bob(List<string> a)
    {
        string empty = new('.', N);
        int last = a.FindLastIndex(r => r.Any(c => c != '.'));
        int first = a.FindIndex(r => r.Any(c => c != '.'));
        var b = new List<string>(a);
        if (last < 0 || last - first < 6) return b;
        for (int y = last - 3; y >= 1; y--) b[y] = a[y - 1];
        b[0] = empty;
        return b;
    }

    /// <summary>변환 결과를 크게 확대한 미리보기 PNG로 저장한다 (확인·공유용).</summary>
    public static void SavePreview(Result result, string path, int scale = 16)
    {
        var colors = result.Palette.Select(p => Util.ParseColor(p, Colors.Magenta)).ToArray();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            for (int f = 0; f < 2; f++)
            {
                var rows = f == 0 ? result.FrameA : result.FrameB;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        int idx = Sprites.IndexOf(rows[y][x]);
                        var brush = idx >= 0 && idx < colors.Length
                            ? Util.Brush(colors[idx])
                            : Util.Brush((x + y) % 2 == 0 ? "#4B4B5A" : "#3F3F4C");
                        dc.DrawRectangle(brush, null, new Rect((f * (N + 1) + x) * scale, y * scale, scale, scale));
                    }
            }
        }
        var bmp = new RenderTargetBitmap((N * 2 + 1) * scale, N * scale, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
