using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

public sealed class SpriteSet
{
    public ImageSource[] Frames = Array.Empty<ImageSource>();
    /// <summary>스프라이트 단위 크기(도트 수). 이미지 캐릭터는 높이 16 기준으로 가로가 달라진다.</summary>
    public double W = 16, H = 16;
    public int TopPad, BottomPad;
}

/// <summary>기본 도트 캐릭터와, 캐릭터 정의를 실제 비트맵으로 만드는 곳.</summary>
public static class Sprites
{
    public const int N = 16;
    public const string IndexChars = "0123456789abcdef";

    public static readonly string[] Kinds = { "Cat", "Slime", "Ghost", "Chick", "Robot" };

    public static string KindName(string kind) => kind switch
    {
        "Cat" => "고양이",
        "Slime" => "슬라임",
        "Ghost" => "유령",
        "Chick" => "병아리",
        "Robot" => "로봇",
        "Custom" => "도트 만들기",
        "Image" => "이미지",
        _ => kind,
    };

    public static bool IsBuiltIn(string kind) => Art.ContainsKey(kind);

    public static (string c1, string c2) DefaultColors(string kind) => kind switch
    {
        "Slime" => ("#6EE7A8", "#3FBF7F"),
        "Ghost" => ("#F4F2FF", "#CFC8F5"),
        "Chick" => ("#FFD84D", "#FF9F1C"),
        "Robot" => ("#AFC1D4", "#5EEAD4"),
        _ => ("#F4A259", "#FBD3A6"),
    };

    // 글자: . 투명 / k 외곽선 / 1 몸 색 / 2 포인트 색 / w 흰색 / e 눈 / p 분홍
    static readonly Dictionary<string, string[][]> Art = new()
    {
        ["Cat"] = new[]
        {
            new[]
            {
                "..k.......k.....",
                ".k1k.....k1k....",
                ".k11kkkkk11k....",
                ".k111111111k....",
                ".k11e111e11k....",
                ".k11e111e11k..k.",
                ".k2111p1112k.k1k",
                "..k1111111k..k1k",
                "..k1111111k.k1k.",
                ".k111111111k1k..",
                ".k11111111111k..",
                ".k1111111111k...",
                ".k1111111111k...",
                ".k11k111k11k....",
                ".k1k.k1k.k1k....",
                "..k...k...k.....",
            },
            new[]
            {
                "..k.......k.....",
                ".k1k.....k1k....",
                ".k11kkkkk11k....",
                ".k111111111k....",
                ".k11e111e11k....",
                ".k11e111e11k...k",
                ".k2111p1112k..k1",
                "..k1111111k...k1",
                "..k1111111k..k1k",
                ".k111111111kk1k.",
                ".k11111111111k..",
                ".k1111111111k...",
                ".k1111111111k...",
                ".k1k111k111k....",
                "..k.k1k.k1k.....",
                ".....k...k......",
            },
        },
        ["Slime"] = new[]
        {
            new[]
            {
                "................",
                "................",
                "................",
                "................",
                "................",
                ".....kkkkkk.....",
                "...kk111111kk...",
                "..k1w11111111k..",
                ".k1w1111111111k.",
                ".k111111111111k.",
                "k111e111111e111k",
                "k111e111111e111k",
                "k111111kk111111k",
                "k12111111111121k",
                ".k222222222222k.",
                "..kkkkkkkkkkkk..",
            },
            new[]
            {
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "....kkkkkkkk....",
                "..kk11111111kk..",
                ".k1w1111111111k.",
                "k1w111111111111k",
                "k111e111111e111k",
                "k111e111111e111k",
                "k111111kk111111k",
                "k12111111111121k",
                ".k222222222222k.",
                "..kkkkkkkkkkkk..",
            },
        },
        ["Ghost"] = new[]
        {
            new[]
            {
                "................",
                "....kkkkkkk.....",
                "..kk1111111kk...",
                ".k11111111111k..",
                ".k11111111111k..",
                "k1111e1111e111k.",
                "k1111e1111e111k.",
                "k1111111111111k.",
                "k111111ee11111k.",
                "k1111111111111k.",
                "k2111111111112k.",
                "k2111111111112k.",
                "k1111111111111k.",
                "k11k111k111k11k.",
                ".kk.kkk.kkk.kk..",
                "................",
            },
            new[]
            {
                "................",
                "....kkkkkkk.....",
                "..kk1111111kk...",
                ".k11111111111k..",
                ".k11111111111k..",
                "k1111e1111e111k.",
                "k1111e1111e111k.",
                "k1111111111111k.",
                "k111111ee11111k.",
                "k1111111111111k.",
                "k2111111111112k.",
                "k2111111111112k.",
                "k1111111111111k.",
                "k1k111k111k111k.",
                ".k.kkk.kkk.kkk..",
                "................",
            },
        },
        ["Chick"] = new[]
        {
            new[]
            {
                "................",
                "................",
                "......kkkk......",
                "....kk1111kk....",
                "...k11111111k...",
                "..k1111111e11k..",
                "..k11111111122k.",
                "..k1111111111k..",
                ".k111111111111k.",
                ".k112221111111k.",
                ".k111221111111k.",
                ".k111111111111k.",
                "..k1111111111k..",
                "...kkkkkkkkkk...",
                ".....2....2.....",
                "....22...22.....",
            },
            new[]
            {
                "................",
                "................",
                "......kkkk......",
                "....kk1111kk....",
                "...k11111111k...",
                "..k1111111e11k..",
                "..k11111111122k.",
                "..k1111111111k..",
                ".k111111111111k.",
                ".k122221111111k.",
                ".k112211111111k.",
                ".k111111111111k.",
                "..k1111111111k..",
                "...kkkkkkkkkk...",
                "......2..2......",
                ".....22.22......",
            },
        },
        ["Robot"] = new[]
        {
            new[]
            {
                ".......2........",
                ".......k........",
                "...kkkkkkkkk....",
                "..k111111111k...",
                "..k1eeeeeee1k...",
                "..k1e2eee2e1k...",
                "..k1eeeeeee1k...",
                "..k111111111k...",
                "...kkkkkkkkk....",
                "..k122222221k...",
                ".kk121111121kk..",
                "k1k122222221k1k.",
                "kkk111111111kkk.",
                "...k11k.k11k....",
                "...k11k.k11k....",
                "...kkkk.kkkk....",
            },
            new[]
            {
                ".......w........",
                ".......k........",
                "...kkkkkkkkk....",
                "..k111111111k...",
                "..k1eeeeeee1k...",
                "..k1e2eee2e1k...",
                "..k1eeeeeee1k...",
                "..k111111111k...",
                "...kkkkkkkkk....",
                "..k122222221k...",
                ".kk121111121kk..",
                "k1k122222221k1k.",
                "kkk111111111kkk.",
                "...k11k.k11k....",
                "...kkkk.k11k....",
                "........kkkk....",
            },
        },
    };

    /// <summary>기본 캐릭터 팔레트: 0 외곽선, 1 몸, 2 포인트, 3 흰색, 4 눈, 5 분홍 (+ 여분 2색)</summary>
    public static List<string> BuiltInPalette(string c1, string c2) =>
        new() { "#22202A", c1, c2, "#FFFFFF", "#1A1A22", "#FF8FB1", "#FFD84D", "#6FD3FF" };

    public static List<string> BuiltInFrame(string kind, int frame)
    {
        var art = Art.TryGetValue(kind, out var a) ? a : Art["Cat"];
        var rows = new List<string>(N);
        foreach (var row in art[Math.Clamp(frame, 0, art.Length - 1)])
        {
            var chars = new char[N];
            for (int x = 0; x < N; x++)
            {
                char c = x < row.Length ? row[x] : '.';
                chars[x] = c switch { 'k' => '0', '1' => '1', '2' => '2', 'w' => '3', 'e' => '4', 'p' => '5', _ => '.' };
            }
            rows.Add(new string(chars));
        }
        return rows;
    }

    public static int IndexOf(char c) => c == '.' ? -1 : IndexChars.IndexOf(char.ToLowerInvariant(c));

    public static SpriteSet Build(CharacterDef def)
    {
        if (def.Kind == "Image" && !string.IsNullOrEmpty(def.ImagePath) && File.Exists(def.ImagePath))
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri(def.ImagePath);
                img.DecodePixelHeight = 128;
                img.EndInit();
                img.Freeze();
                double aspect = img.PixelHeight > 0 ? (double)img.PixelWidth / img.PixelHeight : 1;
                return new SpriteSet { Frames = new ImageSource[] { img, img }, W = N * aspect, H = N };
            }
            catch { }
        }

        List<string> pal, a, b;
        if (def.Kind == "Custom" && def.FrameA.Count > 0)
        {
            pal = def.Palette;
            a = def.FrameA;
            b = def.FrameB.Count > 0 ? def.FrameB : def.FrameA;
        }
        else
        {
            var kind = IsBuiltIn(def.Kind) ? def.Kind : "Cat";
            pal = BuiltInPalette(def.Color1, def.Color2);
            a = BuiltInFrame(kind, 0);
            b = BuiltInFrame(kind, 1);
        }

        var colors = pal.Select(p => Util.ParseColor(p, Colors.Magenta)).ToArray();
        var set = new SpriteSet { Frames = new ImageSource[] { Render(a, colors), Render(b, colors) } };

        int first = -1, last = -1;
        for (int y = 0; y < N; y++)
        {
            bool any = y < a.Count && a[y].Any(c => IndexOf(c) >= 0);
            if (!any) continue;
            if (first < 0) first = y;
            last = y;
        }
        if (first >= 0)
        {
            set.TopPad = first;
            set.BottomPad = N - 1 - last;
        }
        return set;
    }

    static BitmapSource Render(IList<string> rows, Color[] palette)
    {
        var px = new byte[N * N * 4];
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                char c = y < rows.Count && x < rows[y].Length ? rows[y][x] : '.';
                int idx = IndexOf(c);
                if (idx < 0 || idx >= palette.Length) continue;
                var col = palette[idx];
                int o = (y * N + x) * 4;
                px[o] = col.B; px[o + 1] = col.G; px[o + 2] = col.R; px[o + 3] = col.A;
            }
        }
        var bmp = BitmapSource.Create(N, N, 96, 96, PixelFormats.Bgra32, null, px, N * 4);
        bmp.Freeze();
        return bmp;
    }
}
