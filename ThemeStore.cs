using System.IO;
using System.Text.Json;

namespace DynamicIsland;

/// <summary>테마와 앱 설정을 %AppData%\DynamicIsland 에 JSON으로 보관한다.</summary>
public static class ThemeStore
{
    public static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");
    static string ThemesDir => Path.Combine(Root, "themes");
    public static string AssetsDir => Path.Combine(Root, "assets");
    static string SettingsPath => Path.Combine(Root, "settings.json");

    public static List<Theme> Themes { get; } = new();
    public static AppSettings Settings { get; private set; } = new();
    public static bool FirstRun { get; private set; }

    public static Theme Active =>
        Themes.FirstOrDefault(t => t.Id == Settings.ActiveThemeId) ?? Themes[0];

    public static void Load()
    {
        Directory.CreateDirectory(ThemesDir);
        Directory.CreateDirectory(AssetsDir);

        FirstRun = !File.Exists(SettingsPath);
        if (!FirstRun)
        {
            try { Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Json.Options) ?? new(); }
            catch { Settings = new(); }
        }

        Themes.Clear();
        foreach (var file in Directory.GetFiles(ThemesDir, "*.json"))
        {
            try
            {
                var t = JsonSerializer.Deserialize<Theme>(File.ReadAllText(file), Json.Options);
                if (t != null) Themes.Add(t);
            }
            catch { }
        }

        if (Themes.Count == 0)
        {
            foreach (var t in BuiltIn())
            {
                Themes.Add(t);
                Save(t);
            }
        }
        Sort();
        if (FirstRun) SaveSettings();
    }

    static void Sort() => Themes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));

    public static void Save(Theme t)
    {
        try { File.WriteAllText(Path.Combine(ThemesDir, t.Id + ".json"), JsonSerializer.Serialize(t, Json.Options)); }
        catch { }
    }

    public static void Delete(Theme t)
    {
        Themes.Remove(t);
        try { File.Delete(Path.Combine(ThemesDir, t.Id + ".json")); } catch { }
    }

    public static void SaveSettings()
    {
        try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, Json.Options)); }
        catch { }
    }

    /// <summary>기본 테마를 처음 상태로 되돌린다(지웠으면 다시 만든다). 직접 만든 테마는 건드리지 않는다.</summary>
    public static void RestoreBuiltIns()
    {
        foreach (var b in BuiltIn())
        {
            Themes.RemoveAll(t => t.Id == b.Id);
            Themes.Add(b);
            Save(b);
        }
        Sort();
    }

    static CharacterDef C(string kind, string c1, string c2, double size = 2, double speed = 1, double energy = 0.5) =>
        new() { Kind = kind, Color1 = c1, Color2 = c2, PixelSize = size, Speed = speed, Energy = energy, Floats = kind == "Ghost" };

    static List<Theme> BuiltIn() => new()
    {
        new Theme
        {
            Id = "builtin-midnight", Name = "미드나잇",
            Background = "#000000", Text = "#FFFFFF", Accent = "#FF7AA8", UseAlbumColor = true,
            Stage = "Inside", Visualizer = "Bars",
            Characters = { C("Cat", "#F4A259", "#FBD3A6") },
        },
        new Theme
        {
            Id = "builtin-sakura", Name = "벚꽃 고양이",
            Background = "#2B1B2E", UseGradient = true, Background2 = "#4A2545",
            Text = "#FFF1F7", Accent = "#FF9EC7",
            Border = "#FF9EC7", BorderThickness = 1, Glow = true, GlowColor = "#FF7AB8",
            Particles = "Petals", ParticleColor = "#FFB7D5",
            Stage = "OnTop", Ball = true, BallColor = "#FF7AA8",
            Characters = { C("Cat", "#FFF6F0", "#FFC9DE", energy: 0.7), C("Chick", "#FFD84D", "#FF9F1C", 1.5, 1.2, 0.8) },
        },
        new Theme
        {
            Id = "builtin-neon", Name = "네온 슬라임",
            Background = "#05080A", Text = "#E9FFF5", Accent = "#5CFFB0",
            Border = "#5CFFB0", BorderThickness = 1.5, Glow = true, GlowColor = "#5CFFB0",
            Particles = "Bubbles", ParticleColor = "#5CFFB0", Visualizer = "Dots",
            Stage = "OnTop",
            Characters = { C("Slime", "#5CFFB0", "#2FBF85", energy: 0.8), C("Slime", "#6FD3FF", "#3A9BD9", 1.5, 1.3, 0.6) },
        },
        new Theme
        {
            Id = "builtin-ghost", Name = "유령의 밤",
            Background = "#1A1338", UseGradient = true, Background2 = "#2F2166",
            Text = "#EEEAFF", Accent = "#B7A6FF", Glow = true, GlowColor = "#7C66FF",
            Particles = "Stars", ParticleColor = "#FFF4B8", Visualizer = "Wave",
            Stage = "OnTop",
            Characters = { C("Ghost", "#F4F2FF", "#CFC8F5", energy: 0.4), C("Ghost", "#C9F2FF", "#9ADDF5", 1.5, 0.8, 0.6) },
        },
        new Theme
        {
            Id = "builtin-farm", Name = "병아리 농장",
            Background = "#FFF3C4", Text = "#4A3B14", Accent = "#FF8A1C",
            Border = "#F2C94C", BorderThickness = 2,
            Stage = "OnTop", Ball = true, BallColor = "#FF6B5E",
            Characters = { C("Chick", "#FFD84D", "#FF9F1C", energy: 0.9), C("Chick", "#FFF1B0", "#FF9F1C", 1.5, 1.3, 0.7), C("Chick", "#FFE27A", "#FF7A1C", 1.5, 0.9, 0.5) },
        },
        new Theme
        {
            Id = "builtin-robot", Name = "로봇 연구소",
            Background = "#0E1621", UseGradient = true, Background2 = "#14283A",
            Text = "#DFF7F3", Accent = "#5EEAD4", Border = "#2C4A5E", BorderThickness = 1,
            Stage = "Inside", Visualizer = "Bars",
            Characters = { C("Robot", "#AFC1D4", "#5EEAD4", speed: 0.8, energy: 0.4) },
        },
    };
}
