using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace DynamicIsland;

/// <summary>윈도우의 "지금 재생 중" 세션(스포티파이, 브라우저, 유튜브 뮤직 등)을 읽고 제어한다.</summary>
public sealed class MediaService
{
    readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
    GlobalSystemMediaTransportControlsSessionManager? manager;
    GlobalSystemMediaTransportControlsSession? session;
    int version;

    public event Action? Changed;

    public bool HasSession { get; private set; }
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public bool IsPlaying { get; private set; }
    public TimeSpan Position { get; private set; }
    public TimeSpan Duration { get; private set; }
    public DateTimeOffset LastUpdated { get; private set; }
    public double Rate { get; private set; } = 1;
    public ImageSource? Art { get; private set; }
    public Color? ArtColor { get; private set; }

    /// <summary>마지막 보고 시점부터 흐른 시간을 더한 현재 재생 위치.</summary>
    public TimeSpan CurrentPosition
    {
        get
        {
            var p = Position;
            if (IsPlaying && LastUpdated.Year > 2000) p += (DateTimeOffset.Now - LastUpdated) * Rate;
            if (p < TimeSpan.Zero) p = TimeSpan.Zero;
            if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
            return p;
        }
    }

    public async Task StartAsync()
    {
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            manager.CurrentSessionChanged += (_, _) => ui.BeginInvoke(Attach);
            manager.SessionsChanged += (_, _) => ui.BeginInvoke(Attach);
            Attach();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    GlobalSystemMediaTransportControlsSession? Pick()
    {
        if (manager == null) return null;
        try
        {
            foreach (var s in manager.GetSessions())
            {
                if (s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    return s;
            }
            return manager.GetCurrentSession();
        }
        catch { return null; }
    }

    void Attach()
    {
        if (session != null)
        {
            try
            {
                session.MediaPropertiesChanged -= OnProps;
                session.PlaybackInfoChanged -= OnPlayback;
                session.TimelinePropertiesChanged -= OnTimeline;
            }
            catch { }
        }

        session = Pick();
        if (session == null)
        {
            version++;
            HasSession = false;
            IsPlaying = false;
            Title = Artist = "";
            Art = null;
            ArtColor = null;
            Changed?.Invoke();
            return;
        }

        session.MediaPropertiesChanged += OnProps;
        session.PlaybackInfoChanged += OnPlayback;
        session.TimelinePropertiesChanged += OnTimeline;
        HasSession = true;
        RefreshPlayback();
        RefreshTimeline();
        _ = RefreshPropsAsync();
    }

    void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) =>
        ui.BeginInvoke(() => { _ = RefreshPropsAsync(); });

    void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) =>
        ui.BeginInvoke(() =>
        {
            RefreshPlayback();
            RefreshTimeline();
            Changed?.Invoke();
            if (!IsPlaying) Attach(); // 다른 앱이 재생 중이면 그쪽으로 옮겨 간다
        });

    void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) =>
        ui.BeginInvoke(() => { RefreshTimeline(); Changed?.Invoke(); });

    void RefreshPlayback()
    {
        try
        {
            var info = session?.GetPlaybackInfo();
            if (info == null) return;
            IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            Rate = info.PlaybackRate ?? 1;
        }
        catch { }
    }

    void RefreshTimeline()
    {
        try
        {
            var t = session?.GetTimelineProperties();
            if (t == null) return;
            Position = t.Position - t.StartTime;
            Duration = t.EndTime - t.StartTime;
            LastUpdated = t.LastUpdatedTime;
        }
        catch { }
    }

    async Task RefreshPropsAsync()
    {
        var s = session;
        if (s == null) return;
        int v = ++version;
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if (v != version || p == null) return;
            Title = p.Title ?? "";
            Artist = p.Artist ?? "";
            Changed?.Invoke();

            ImageSource? art = null;
            Color? color = null;
            if (p.Thumbnail != null)
            {
                using var ras = await p.Thumbnail.OpenReadAsync();
                using var stream = ras.AsStreamForRead();
                var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 160;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                art = bmp;
                color = VividAverage(bmp);
            }
            if (v != version) return;
            Art = art;
            ArtColor = color;
            Changed?.Invoke();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    /// <summary>채도가 높은 픽셀에 가중치를 준 평균색 — 앨범 아트의 "대표색".</summary>
    static Color? VividAverage(BitmapSource src)
    {
        try
        {
            var fb = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = fb.PixelWidth, h = fb.PixelHeight;
            if (w == 0 || h == 0) return null;
            var buf = new byte[w * h * 4];
            fb.CopyPixels(buf, w * 4, 0);
            double r = 0, g = 0, b = 0, total = 0;
            for (int i = 0; i < buf.Length; i += 4 * 5)
            {
                double pb = buf[i], pg = buf[i + 1], pr = buf[i + 2];
                double max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
                double weight = (max == 0 ? 0 : (max - min) / max) + 0.05;
                r += pr * weight; g += pg * weight; b += pb * weight; total += weight;
            }
            if (total <= 0) return null;
            return Color.FromRgb((byte)(r / total), (byte)(g / total), (byte)(b / total));
        }
        catch { return null; }
    }

    public async void Toggle() { try { if (session != null) await session.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next() { try { if (session != null) await session.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (session != null) await session.TrySkipPreviousAsync(); } catch { } }

    public async void Seek(TimeSpan position)
    {
        try
        {
            if (session == null) return;
            if (await session.TryChangePlaybackPositionAsync(position.Ticks))
            {
                Position = position;
                LastUpdated = DateTimeOffset.Now;
                Changed?.Invoke();
            }
        }
        catch { }
    }
}
