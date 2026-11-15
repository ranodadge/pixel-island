using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland;

public record IslandNotification(string App, string Title, string Body, ImageSource? Icon);

/// <summary>윈도우 알림 센터에 새로 들어온 토스트를 읽어 온다. 사용자가 알림 접근을 허용해야 동작한다.</summary>
public sealed class NotificationService
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1.2) };
    readonly HashSet<uint> seen = new();
    UserNotificationListener? listener;
    bool busy, seeded;

    public event Action<IslandNotification>? Received;
    public event Action? StatusChanged;

    public bool Allowed { get; private set; }
    public string Status { get; private set; } = "확인 중…";

    public NotificationService() => timer.Tick += (_, _) => Poll();

    public async Task StartAsync()
    {
        timer.Stop();
        Allowed = false;
        try
        {
            listener = UserNotificationListener.Current;
            var access = await listener.RequestAccessAsync();
            Allowed = access == UserNotificationListenerAccessStatus.Allowed;
            Status = Allowed
                ? "허용됨 — 새 알림이 아일랜드에 표시돼요"
                : "권한 없음 — 윈도우 설정에서 알림 접근을 허용해 주세요";
        }
        catch (Exception ex)
        {
            Status = "이 PC에서는 알림을 읽을 수 없어요 (" + ex.Message.Trim() + ")";
            App.Log(ex);
        }
        StatusChanged?.Invoke();
        if (Allowed) timer.Start();
    }

    async void Poll()
    {
        if (busy || listener == null) return;
        busy = true;
        try
        {
            var list = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var ids = new HashSet<uint>();
            foreach (var n in list)
            {
                ids.Add(n.Id);
                if (!seen.Add(n.Id) || !seeded) continue; // 앱을 켜기 전부터 있던 알림은 건너뛴다
                var item = await Convert(n);
                if (item != null) Received?.Invoke(item);
            }
            seen.IntersectWith(ids);
            seeded = true;
        }
        catch { }
        finally { busy = false; }
    }

    static async Task<IslandNotification?> Convert(UserNotification n)
    {
        string app = "";
        try { app = n.AppInfo?.DisplayInfo?.DisplayName ?? ""; } catch { }

        var texts = new List<string>();
        try
        {
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding != null)
            {
                foreach (var t in binding.GetTextElements())
                    if (!string.IsNullOrWhiteSpace(t.Text)) texts.Add(t.Text.Trim());
            }
        }
        catch { }
        if (texts.Count == 0 && app.Length == 0) return null;

        ImageSource? icon = null;
        try
        {
            var logo = n.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(48, 48));
            if (logo != null)
            {
                using var ras = await logo.OpenReadAsync();
                using var stream = ras.AsStreamForRead();
                var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                icon = bmp;
            }
        }
        catch { }

        string title = texts.Count > 0 ? texts[0] : app;
        string body = texts.Count > 1 ? string.Join(" ", texts.Skip(1)) : "";
        return new IslandNotification(app, title, body, icon);
    }
}
