using System.Runtime.InteropServices;

namespace VoxPad.CableCheck;

/// <summary>
/// Sets the Windows default audio endpoint via the (undocumented but long-stable) IPolicyConfig interface.
/// Used by "voxpad-cablecheck setdefault" to undo VB-CABLE's habit of making itself the default device.
/// </summary>
internal static class PolicyConfig
{
    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat(string pszDeviceName, IntPtr ppFormat);
        int GetDeviceFormat(string pszDeviceName, int bDefault, IntPtr ppFormat);
        int ResetDeviceFormat(string pszDeviceName);
        int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
        int GetProcessingPeriod(string pszDeviceName, int bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
        int GetShareMode(string pszDeviceName, IntPtr pMode);
        int SetShareMode(string pszDeviceName, IntPtr mode);
        int GetPropertyValue(string pszDeviceName, int bFxStore, IntPtr key, IntPtr pv);
        int SetPropertyValue(string pszDeviceName, int bFxStore, IntPtr key, IntPtr pv);
        int SetDefaultEndpoint(string pszDeviceName, ERole role);
        int SetEndpointVisibility(string pszDeviceName, int bVisible);
    }

    public static void SetDefault(string deviceId, bool communicationsToo)
    {
        var pc = (IPolicyConfig)new PolicyConfigClient();
        Marshal.ThrowExceptionForHR(pc.SetDefaultEndpoint(deviceId, ERole.Console));
        Marshal.ThrowExceptionForHR(pc.SetDefaultEndpoint(deviceId, ERole.Multimedia));
        if (communicationsToo) Marshal.ThrowExceptionForHR(pc.SetDefaultEndpoint(deviceId, ERole.Communications));
    }
}
