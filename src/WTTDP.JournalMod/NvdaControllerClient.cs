using System;
using System.Runtime.InteropServices;

namespace WTTDP.JournalMod;

internal static class NvdaControllerClient
{
    private static DateTime _nextProbeUtc = DateTime.MinValue;
    private static bool _isAvailable;

    public static bool IsAvailable()
    {
        if (DateTime.UtcNow < _nextProbeUtc)
        {
            return _isAvailable;
        }

        _nextProbeUtc = DateTime.UtcNow.AddSeconds(2);
        _isAvailable = Probe();
        return _isAvailable;
    }

    public static bool TrySpeak(string? text, bool interrupt)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsAvailable())
        {
            return false;
        }

        try
        {
            if (interrupt)
            {
                CancelCore();
            }

            return SpeakCore(text!) == 0;
        }
        catch (DllNotFoundException)
        {
            _isAvailable = false;
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            _isAvailable = false;
            return false;
        }
    }

    public static void TryCancel()
    {
        if (!IsAvailable())
        {
            return;
        }

        try
        {
            CancelCore();
        }
        catch (DllNotFoundException)
        {
            _isAvailable = false;
        }
        catch (EntryPointNotFoundException)
        {
            _isAvailable = false;
        }
    }

    private static bool Probe()
    {
        try
        {
            return TestIfRunningCore() == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static int SpeakCore(string text)
    {
        return IntPtr.Size == 8 ? SpeakText64(text) : SpeakText32(text);
    }

    private static int CancelCore()
    {
        return IntPtr.Size == 8 ? CancelSpeech64() : CancelSpeech32();
    }

    private static int TestIfRunningCore()
    {
        return IntPtr.Size == 8 ? TestIfRunning64() : TestIfRunning32();
    }

    [DllImport("nvdaControllerClient32.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_speakText")]
    private static extern int SpeakText32(string text);

    [DllImport("nvdaControllerClient64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_speakText")]
    private static extern int SpeakText64(string text);

    [DllImport("nvdaControllerClient32.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_cancelSpeech")]
    private static extern int CancelSpeech32();

    [DllImport("nvdaControllerClient64.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_cancelSpeech")]
    private static extern int CancelSpeech64();

    [DllImport("nvdaControllerClient32.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_testIfRunning")]
    private static extern int TestIfRunning32();

    [DllImport("nvdaControllerClient64.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_testIfRunning")]
    private static extern int TestIfRunning64();
}
