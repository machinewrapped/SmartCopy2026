using SmartCopy.UI.ViewModels.Dialogs;

namespace SmartCopy.Tests.UI;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class MtpDevicePickerViewModelTests
{
    [Theory]
    [InlineData("MTP:USB", null, true)]
    [InlineData(null, @"\\?\usb#vid_22b8&pid_2e82#zy22lc9bn2#{6ac27878-a6fa-4155-ba85-f98f491d4f33}", true)]
    [InlineData("", @"\\?\swd#wpdbusenum#{2cc013b6-aa7b-11ef-912f-6245b509bec6}#0000000000100000#{6ac27878-a6fa-4155-ba85-f98f491d4f33}", false)]
    [InlineData(null, null, false)]
    public void IsMtpDevice_AcceptsMtpProtocolOrPhysicalUsbWpdDevice(
        string? protocol, string? deviceId, bool expected)
    {
        Assert.Equal(expected, MtpDevicePickerViewModel.IsMtpDevice(protocol, deviceId));
    }
}
