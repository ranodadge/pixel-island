using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DynamicIsland;

/// <summary>캘린더의 날짜별 메모(이 PC에만 저장)와 한국 공휴일 계산.</summary>
public static class CalendarStore
{
    static string FilePath => Path.Combine(ThemeStore.Root, "calendar.json");
    static Dictionary<string, List<string>> memos = new();
    static readonly Dictionary<int, Dictionary<DateTime, string>> holidayCache = new();

    static string Key(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                memos = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(FilePath), Json.Options) ?? new();
        }
        catch { memos = new(); }
    }

    static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(memos, Json.Options)); }
        catch (Exception ex) { App.Log(ex); }
    }

    public static IReadOnlyList<string> Memos(DateTime date) =>
        memos.TryGetValue(Key(date), out var list) ? list : Array.Empty<string>();

    public static bool HasMemo(DateTime date) => memos.TryGetValue(Key(date), out var list) && list.Count > 0;

    public static void Add(DateTime date, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        if (!memos.TryGetValue(Key(date), out var list)) memos[Key(date)] = list = new List<string>();
        list.Add(text);
        Save();
    }

    public static void Remove(DateTime date, int index)
    {
        if (!memos.TryGetValue(Key(date), out var list) || index < 0 || index >= list.Count) return;
        list.RemoveAt(index);
        if (list.Count == 0) memos.Remove(Key(date));
        Save();
    }

    /// <summary>그 날이 공휴일이면 이름, 아니면 null. (대체공휴일·임시공휴일·선거일은 해마다 따로 정해져서 포함하지 않는다)</summary>
    public static string? Holiday(DateTime date)
    {
        if (!holidayCache.TryGetValue(date.Year, out var year)) holidayCache[date.Year] = year = BuildHolidays(date.Year);
        return year.TryGetValue(date.Date, out var name) ? name : null;
    }

    static Dictionary<DateTime, string> BuildHolidays(int year)
    {
        var map = new Dictionary<DateTime, string>();
        void Solar(int month, int day, string name) => map[new DateTime(year, month, day)] = name;
        Solar(1, 1, "신정");
        Solar(3, 1, "삼일절");
        Solar(5, 5, "어린이날");
        Solar(6, 6, "현충일");
        Solar(8, 15, "광복절");
        Solar(10, 3, "개천절");
        Solar(10, 9, "한글날");
        Solar(12, 25, "성탄절");

        // 음력 명절은 .NET의 한국 음력 달력으로 양력 날짜를 구한다
        try
        {
            var lunar = new KoreanLunisolarCalendar();
            DateTime FromLunar(int lunarYear, int month, int day)
            {
                // 윤달이 있는 해는 윤달 뒤의 달들이 한 칸씩 밀려 있다
                int leap = lunar.GetLeapMonth(lunarYear);
                int index = leap > 0 && month >= leap ? month + 1 : month;
                return lunar.ToDateTime(lunarYear, index, day, 0, 0, 0, 0);
            }
            void Add(DateTime d, string name) { if (d.Year == year) map.TryAdd(d.Date, name); }

            // 설날은 음력 1월 1일이라 양력으로는 그 해 1~2월 — 음력 연도와 양력 연도가 같다
            var seollal = FromLunar(year, 1, 1);
            Add(seollal.AddDays(-1), "설날 연휴");
            Add(seollal, "설날");
            Add(seollal.AddDays(1), "설날 연휴");
            Add(FromLunar(year, 4, 8), "부처님오신날");
            var chuseok = FromLunar(year, 8, 15);
            Add(chuseok.AddDays(-1), "추석 연휴");
            Add(chuseok, "추석");
            Add(chuseok.AddDays(1), "추석 연휴");
        }
        catch { }
        return map;
    }
}
