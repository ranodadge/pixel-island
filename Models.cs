using System.Text.Encodings.Web;
using System.Text.Json;

namespace DynamicIsland;

/// <summary>섬에서 노는 캐릭터 하나의 정의.</summary>
public class CharacterDef
{
    /// <summary>Cat, Slime, Ghost, Chick, Robot, Custom(직접 그린 도트), Image(이미지 파일)</summary>
    public string Kind { get; set; } = "Cat";
    public string Color1 { get; set; } = "#F4A259";
    public string Color2 { get; set; } = "#FBD3A6";
    /// <summary>도트 한 칸의 화면 크기(px). 스프라이트는 16x16.</summary>
    public double PixelSize { get; set; } = 2;
    public double Speed { get; set; } = 1;
    /// <summary>0(느긋) ~ 1(활발)</summary>
    public double Energy { get; set; } = 0.5;
    public bool Floats { get; set; }

    // Custom 전용: 팔레트 + 16x16 프레임 두 장. 각 글자는 '.'(투명) 또는 팔레트 인덱스(16진수 한 자리)
    public List<string> Palette { get; set; } = new();
    public List<string> FrameA { get; set; } = new();
    public List<string> FrameB { get; set; } = new();

    // Image 전용
    public string? ImagePath { get; set; }
}

public class Theme
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "새 테마";

    public string Background { get; set; } = "#000000";
    public bool UseGradient { get; set; }
    public string Background2 { get; set; } = "#1E1E2E";
    public string Text { get; set; } = "#FFFFFF";
    public string Accent { get; set; } = "#FF7AA8";
    public bool UseAlbumColor { get; set; }
    public string Border { get; set; } = "#FFFFFF";
    public double BorderThickness { get; set; }
    public bool Glow { get; set; }
    public string GlowColor { get; set; } = "#FF7AA8";
    public double Roundness { get; set; } = 1;
    public double Opacity { get; set; } = 1;

    /// <summary>Bars, Wave, Dots</summary>
    public string Visualizer { get; set; } = "Bars";
    /// <summary>None, Stars, Snow, Petals, Hearts, Bubbles</summary>
    public string Particles { get; set; } = "None";
    public string ParticleColor { get; set; } = "#FFFFFF";

    /// <summary>OnTop(섬 위) 또는 Inside(섬 안)</summary>
    public string Stage { get; set; } = "OnTop";
    public bool Ball { get; set; }
    public string BallColor { get; set; } = "#FF7AA8";
    public List<CharacterDef> Characters { get; set; } = new();

    public Theme Clone()
    {
        var copy = JsonSerializer.Deserialize<Theme>(JsonSerializer.Serialize(this, Json.Options), Json.Options)!;
        copy.Id = Guid.NewGuid().ToString("N");
        return copy;
    }
}

public class AppSettings
{
    public string ActiveThemeId { get; set; } = "builtin-sakura";
    public bool ExpandOnHover { get; set; } = true;
    public bool HideOnFullscreen { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool AudioReactive { get; set; } = true;
    public double NotificationSeconds { get; set; } = 5;
    /// <summary>윈도우 잠금이 풀릴 때 섬에서 자물쇠가 열리는 연출을 보여 줄지.</summary>
    public bool UnlockAnimation { get; set; } = true;
    /// <summary>기본(접힌) 상태에서 CPU·메모리 사용량을 보여 줄지.</summary>
    public bool ShowStats { get; set; } = true;
    /// <summary>기본(접힌) 상태에서 배터리 잔량을 보여 줄지 (배터리가 있는 기기에서만).</summary>
    public bool ShowBattery { get; set; } = true;
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };
}
