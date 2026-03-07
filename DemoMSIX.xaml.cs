using System.Windows;

namespace PolarisManager;

public partial class DemoMSIX : Window
{
    public DemoMSIX()
    {
        InitializeComponent();
        GridPkg.ItemsSource = new[]
        {
            new MsixPkg(true,  "&#x1F98A;", "Mozilla Firefox",       "Mozilla.Firefox",              "133.0.2"),
            new MsixPkg(true,  "&#x1F3A5;", "VLC Media Player",      "VideoLAN.VLC",                 "3.0.21"),
            new MsixPkg(true,  "&#x1F4E6;", "7-Zip",                 "7zip.7zip",                    "24.09"),
            new MsixPkg(true,  "&#x1F4DD;", "Notepad++",             "Notepad++.Notepad++",          "8.7.4"),
            new MsixPkg(false, "&#x1F4C4;", "Adobe Acrobat Reader",  "Adobe.Acrobat.Reader.64-bit",  "24.005"),
            new MsixPkg(false, "&#x1F4BB;", "Visual Studio Code",    "Microsoft.VisualStudioCode",   "1.96.4"),
            new MsixPkg(false, "&#x1F4E7;", "Microsoft Teams",       "Microsoft.Teams",              "24.12.2"),
            new MsixPkg(false, "&#x1F4CA;", "OnlyOffice Desktop",    "ONLYOFFICE.DesktopEditors",    "8.2.2"),
        };
    }
}

record MsixPkg(bool Selected, string Icon, string Name, string WingetId, string Version);
