using System.Runtime.InteropServices;

namespace DynamicIsland;

/// <summary>CPU·메모리 사용량과 배터리 상태. 가벼운 시스템 호출 세 번이라 1초에 한 번씩 화면 스레드에서 읽어도 된다.</summary>
public static class SystemStats
{
    static ulong lastIdle, lastTotal;

    /// <summary>0~1. 직전에 읽은 뒤로 CPU가 일한 비율.</summary>
    public static double Cpu { get; private set; }
    /// <summary>0~1. 쓰고 있는 메모리 비율.</summary>
    public static double Memory { get; private set; }
    public static double MemoryUsedGb { get; private set; }
    public static double MemoryTotalGb { get; private set; }

    /// <summary>배터리가 있는 기기(노트북)인지.</summary>
    public static bool HasBattery { get; private set; }
    /// <summary>0~1.</summary>
    public static double Battery { get; private set; }
    /// <summary>전원 어댑터가 꽂혀 있는지.</summary>
    public static bool PluggedIn { get; private set; }

    public static void Update()
    {
        if (GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        {
            ulong total = kernel + user; // 커널 시간에는 쉬는 시간(idle)이 포함돼 있다
            if (lastTotal != 0 && total > lastTotal)
                Cpu = Math.Clamp(1.0 - (double)(idle - lastIdle) / (total - lastTotal), 0, 1);
            lastIdle = idle;
            lastTotal = total;
        }

        var mem = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref mem) && mem.TotalPhys > 0)
        {
            const double gb = 1024.0 * 1024 * 1024;
            MemoryTotalGb = mem.TotalPhys / gb;
            MemoryUsedGb = (mem.TotalPhys - mem.AvailPhys) / gb;
            Memory = Math.Clamp(MemoryUsedGb / MemoryTotalGb, 0, 1);
        }

        if (GetSystemPowerStatus(out var power))
        {
            const byte noBattery = 128, unknown = 255;
            HasBattery = (power.BatteryFlag & noBattery) == 0 && power.BatteryFlag != unknown && power.BatteryLifePercent != unknown;
            if (HasBattery) Battery = Math.Clamp(power.BatteryLifePercent / 100.0, 0, 1);
            PluggedIn = power.ACLineStatus == 1;
        }
    }

    [DllImport("kernel32.dll")]
    static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [StructLayout(LayoutKind.Sequential)]
    struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
