using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace DynamicIsland;

/// <summary>
/// 앱을 윈도우에 "설치된 앱"으로 등록한다.
///  · 실행 파일을 %LocalAppData%\Programs\Dynamic Island 로 복사 (개발 폴더를 지워도 계속 동작)
///  · 시작 메뉴 바로가기 → 윈도우 검색에 나온다
///  · 바탕 화면 바로가기
///  · 설정 > 앱 > 설치된 앱 목록에 등록 (거기서 제거 가능)
/// 관리자 권한 없이 현재 사용자에게만 설치한다. 테마·설정(%AppData%\DynamicIsland)은 건드리지 않는다.
/// </summary>
public static class Installer
{
    public const string AppName = "Dynamic Island";
    public const string ShowSignal = "DynamicIsland.Show", QuitSignal = "DynamicIsland.Quit";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DynamicIsland";

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
    public static string InstalledExe => Path.Combine(InstallDir, "DynamicIsland.exe");
    public static string StartMenuLink =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
    public static string DesktopLink =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

    public static bool IsInstalled => File.Exists(InstalledExe) && File.Exists(StartMenuLink);
    public static bool RunningInstalled => SamePath(AppContext.BaseDirectory, InstallDir);
    public static bool HasDesktopShortcut => File.Exists(DesktopLink);

    static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    static string Version => typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>지금 실행 파일을 새 프로세스로 띄워 설치·제거를 맡긴다 (그 프로세스가 이 앱을 끄고 파일을 바꾼다).</summary>
    public static void Launch(string exe, string argument)
    {
        try { Process.Start(new ProcessStartInfo(exe, argument) { UseShellExecute = false }); }
        catch (Exception ex) { App.Log(ex); }
    }

    /// <summary>
    /// 런타임까지 한 파일에 담아 배포한 실행 파일인지 (설치파일 DynamicIslandSetup.exe 가 그렇다).
    /// 이때는 옆에 DynamicIsland.dll 이 없고, 실행 파일 하나만 복사하면 설치가 끝난다.
    /// </summary>
    static bool SingleFile => !File.Exists(Path.Combine(AppContext.BaseDirectory, "DynamicIsland.dll"));

    /// <summary>설치파일을 더블클릭했을 때 먼저 물어본다.</summary>
    public static bool ConfirmSetup() =>
        MessageBox.Show(
            (IsRegistered() ? "Dynamic Island가 이미 설치돼 있어요. 이 버전으로 다시 설치할까요?" : "Dynamic Island를 설치할까요?") +
            $"\n\n설치 위치: {InstallDir}\n시작 메뉴와 바탕 화면에 바로가기를 만들어요. 관리자 권한은 필요 없어요.",
            AppName + " 설치", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void RunInstall(bool quiet)
    {
        try
        {
            bool firstInstall = !IsRegistered();
            StopOthers();
            if (!RunningInstalled)
            {
                ClearInstallDir();
                if (SingleFile)
                {
                    // 내려받은 설치파일이 있는 폴더(다운로드 등)를 통째로 복사하면 안 되므로 실행 파일 하나만 옮긴다
                    Directory.CreateDirectory(InstallDir);
                    File.Copy(Environment.ProcessPath!, InstalledExe, true);
                    // "인터넷에서 받은 파일" 표시가 복사본에 따라오면 켤 때마다 경고가 뜰 수 있어서 설치본에서는 뗀다
                    try { File.Delete(InstalledExe + ":Zone.Identifier"); } catch { }
                }
                else CopyAll(AppContext.BaseDirectory, InstallDir);
            }

            CreateShortcut(StartMenuLink);
            // 바탕 화면 바로가기는 처음 설치할 때만 만든다 (지워 둔 사람에게 업데이트 때마다 되살리지 않도록)
            if (firstInstall) CreateShortcut(DesktopLink);
            Register();
            if (Startup.Get()) Startup.PointTo(InstalledExe);

            if (!quiet)
                MessageBox.Show("설치했어요.\n\n시작 메뉴나 윈도우 검색에서 'Dynamic Island'를 찾아 실행할 수 있고, 바탕 화면에도 바로가기를 만들었어요.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            Process.Start(new ProcessStartInfo(InstalledExe, quiet ? "" : "--editor") { UseShellExecute = false, WorkingDirectory = InstallDir });
        }
        catch (Exception ex)
        {
            App.Log(ex);
            if (!quiet) MessageBox.Show("설치하지 못했어요.\n\n" + ex.Message, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static void RunUninstall(bool quiet)
    {
        if (!quiet && MessageBox.Show("Dynamic Island를 제거할까요?\n\n만들어 둔 테마와 설정은 지우지 않고 남겨 둬요.",
                AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            StopOthers();
            TryDelete(StartMenuLink);
            TryDelete(DesktopLink);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            if (Startup.PointsInto(InstallDir)) Startup.Set(false);

            // 실행 중인 자기 자신은 지울 수 없으므로, 이 프로세스가 끝난 뒤 설치 폴더를 지우도록 맡긴다.
            // 지우는 대상은 이 앱이 만든 설치 폴더 하나뿐이다.
            if (File.Exists(InstalledExe) && InstallDir.EndsWith(@"\Programs\" + AppName, StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{InstallDir}\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath(),
                });
        }
        catch (Exception ex)
        {
            App.Log(ex);
            if (!quiet) MessageBox.Show("제거하지 못했어요.\n\n" + ex.Message, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static void SetDesktopShortcut(bool on)
    {
        try
        {
            if (on) CreateShortcut(DesktopLink);
            else TryDelete(DesktopLink);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    static bool IsRegistered()
    {
        using var k = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return k != null;
    }

    static void Register()
    {
        using var k = Registry.CurrentUser.CreateSubKey(UninstallKey);
        k.SetValue("DisplayName", AppName);
        k.SetValue("DisplayVersion", Version);
        k.SetValue("DisplayIcon", InstalledExe + ",0");
        k.SetValue("Publisher", AppName);
        k.SetValue("InstallLocation", InstallDir);
        k.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        k.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --quiet");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        long bytes = new DirectoryInfo(InstallDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        k.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
    }

    static void CreateShortcut(string linkPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("바로가기를 만들 수 없어요 (WScript.Shell 없음)");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic link = shell.CreateShortcut(linkPath);
        link.TargetPath = InstalledExe;
        link.WorkingDirectory = InstallDir;
        link.IconLocation = InstalledExe + ",0";
        link.Description = "화면 위의 다이내믹 아일랜드";
        link.Save();
    }

    /// <summary>실행 중인 다른 Dynamic Island를 끈다: 먼저 스스로 끝내 달라고 알리고, 안 끝나면 강제로 끈다.</summary>
    static void StopOthers()
    {
        bool asked = false;
        if (EventWaitHandle.TryOpenExisting(QuitSignal, out var quit))
            using (quit) { quit.Set(); asked = true; }

        int me = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("DynamicIsland"))
        {
            if (p.Id == me) continue;
            try
            {
                if (!asked || !p.WaitForExit(5000)) { p.Kill(); p.WaitForExit(5000); }
            }
            catch { }
        }
    }

    /// <summary>
    /// 예전 설치본의 파일을 비운다 (여러 파일짜리와 한 파일짜리 설치가 섞여 남지 않도록).
    /// 지우는 곳은 이 앱이 만든 설치 폴더 하나뿐이고, 테마·설정 폴더(%AppData%)는 다른 곳이라 영향이 없다.
    /// </summary>
    static void ClearInstallDir()
    {
        if (!Directory.Exists(InstallDir) || !InstallDir.EndsWith(@"\Programs\" + AppName, StringComparison.OrdinalIgnoreCase)) return;
        foreach (var file in Directory.GetFiles(InstallDir, "*", SearchOption.AllDirectories))
            for (int attempt = 0; ; attempt++)
            {
                // 방금 끈 앱이 파일을 아직 놓지 않았을 수 있어서 잠깐씩 다시 시도한다
                try { File.Delete(file); break; }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 8) { Thread.Sleep(400); }
            }
        foreach (var dir in Directory.GetDirectories(InstallDir))
            try { Directory.Delete(dir, true); } catch { }
    }

    static void CopyAll(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            // 방금 끈 앱이 파일을 아직 놓지 않았을 수 있어서 잠깐씩 다시 시도한다
            for (int attempt = 0; ; attempt++)
            {
                try { File.Copy(file, target, true); break; }
                catch (IOException) when (attempt < 8) { Thread.Sleep(400); }
            }
        }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
