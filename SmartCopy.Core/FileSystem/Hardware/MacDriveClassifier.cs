using System.Diagnostics;
using System.Xml.Linq;

namespace SmartCopy.Core.FileSystem.Hardware;

internal sealed class MacDriveClassifier : IDriveClassifier
{
    public DriveClassification Classify(string rootPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "diskutil",
            Arguments = $"info -plist \"{rootPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null) return DriveClassification.Unknown;

            string xmlOutput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(xmlOutput))
            {
                return DriveClassification.Unknown;
            }

            var doc = XDocument.Parse(xmlOutput);
            var dict = doc.Element("plist")?.Element("dict");
            if (dict == null) return DriveClassification.Unknown;

            DriveMediaType mediaType = DriveMediaType.Unknown;
            DriveInterfaceType interfaceType = DriveInterfaceType.Unknown;

            var elements = dict.Elements();
            string? currentKey = null;

            foreach (var element in elements)
            {
                if (element.Name == "key")
                {
                    currentKey = element.Value;
                }
                else if (currentKey != null)
                {
                    if (currentKey == "SolidState")
                    {
                        mediaType = element.Name.LocalName == "true" ? DriveMediaType.SSD : DriveMediaType.HDD;
                    }
                    else if (currentKey == "BusProtocol")
                    {
                        string val = element.Value;
                        if (val.Contains("PCI", StringComparison.OrdinalIgnoreCase))
                            interfaceType = DriveInterfaceType.NVMe;
                        else if (val.Contains("SATA", StringComparison.OrdinalIgnoreCase))
                            interfaceType = DriveInterfaceType.SATA;
                        else if (val.Contains("USB", StringComparison.OrdinalIgnoreCase))
                            interfaceType = DriveInterfaceType.USB;
                        else if (val.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                            interfaceType = DriveInterfaceType.Virtual;
                    }
                    currentKey = null;
                }
            }

            return new DriveClassification(mediaType, interfaceType);
        }
        catch
        {
            return DriveClassification.Unknown;
        }
    }
}
