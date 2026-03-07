using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;

namespace PolarisManager;

public partial class QrWindow : Window
{
    public QrWindow(string deviceName, string url)
    {
        InitializeComponent();
        TxtTitle.Text = $"📱  {deviceName}";
        TxtUrl.Text   = url;

        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        var bytes = qr.GetGraphic(6);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new System.IO.MemoryStream(bytes);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        QrImage.Source = bmp;
    }

    private void BtnClose_Click(object s, RoutedEventArgs e) => Close();
}
