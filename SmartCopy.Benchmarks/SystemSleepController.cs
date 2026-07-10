using System;
using System.Runtime.InteropServices;

namespace SmartCopy.Benchmarks;

public static class SystemSleepController
{
    [Flags]
    private enum EXECUTION_STATE : uint
    {
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_CONTINUOUS = 0x80000000
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    public static void PreventSleep()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED);
            }
            catch
            {
                // Ignore errors gracefully
            }
        }
    }

    public static void AllowSleep()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
            }
            catch
            {
                // Ignore errors gracefully
            }
        }
    }
}
