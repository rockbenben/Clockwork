using System.Runtime.InteropServices;

namespace Clockwork.Native;

// 系统音量控制（Core Audio API）。
// 占位方法 f0.. 用来对齐 COM vtable 偏移；只声明真正调用的 SetMasterVolumeLevelScalar/SetMute/GetMute。

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int f0();  // EnumAudioEndpoints
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate([In] ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
}

[Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    int f0(); int f1(); int f2();                 // Register/Unregister/GetChannelCount
    int f3();                                     // SetMasterVolumeLevel
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, [In] ref Guid ctx);
    int f5(); int f6(); int f7(); int f8(); int f9(); int f10();  // Get*/SetChannel*/GetChannel*
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, [In] ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumerator { }

public static class AudioController
{
    // 无可用输出设备（RDP 未重定向 / 无声卡 / 输出全禁用）时 GetDefaultAudioEndpoint 返回 E_NOTFOUND、dev 为 null——
    // 必须查 HRESULT 并判空，否则解引用 null 抛 NRE、音量步骤崩了还会中止整个动作组。
    // 返回默认输出端点；中间 COM 对象（枚举器、设备）在此就地释放，只把端点交出去。
    private static IAudioEndpointVolume? Endpoint()
    {
        var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (en.GetDefaultAudioEndpoint(0, 1, out IMMDevice dev) < 0 || dev == null) return null;   // eRender, eMultimedia
            try
            {
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                if (dev.Activate(ref iid, 1, IntPtr.Zero, out object o) < 0 || o == null) return null;  // CLSCTX_INPROC_SERVER
                return (IAudioEndpointVolume)o;
            }
            finally { Marshal.FinalReleaseComObject(dev); }
        }
        finally { Marshal.FinalReleaseComObject(en); }
    }

    // level: 0.0–1.0。无音频设备 → 静默跳过，不崩、不中止动作组。端点用完即释放（否则每次音量步骤泄漏 COM）。
    public static void SetVolume(float level)
    {
        var ep = Endpoint();
        if (ep == null) return;
        try
        {
            Guid ctx = Guid.Empty;
            int hr = ep.SetMasterVolumeLevelScalar(level, ref ctx);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }
        finally { Marshal.FinalReleaseComObject(ep); }
    }

    public static void SetMute(bool mute)
    {
        var ep = Endpoint();
        if (ep == null) return;
        try
        {
            Guid ctx = Guid.Empty;
            int hr = ep.SetMute(mute, ref ctx);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }
        finally { Marshal.FinalReleaseComObject(ep); }
    }

    // 百分比夹 0–100。
    public static void SetVolumePercent(int percent)
    {
        if (percent < 0) percent = 0;
        if (percent > 100) percent = 100;
        SetVolume(percent / 100.0f);
    }

    // 静音开关。
    public static void Mute(bool mute) => SetMute(mute);
}
