using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KnightsCleaner.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var window = new MainWindow();
        window.Show();
        window.UpdateLayout();
        if (window.Title != "Knights Cleaner") throw new Exception("Window title mismatch.");
        var drives = (WrapPanel)window.FindName("Drives");
        var system = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        foreach (CheckBox drive in drives.Children)
            if (!string.Equals((string)drive.Tag, system, StringComparison.OrdinalIgnoreCase) && drive.IsChecked == true)
                throw new Exception("Non-system drive selected by default.");
        if (((ListBox)window.FindName("Activity")).ActualHeight < 150)
            throw new Exception("Activity log is too small.");
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        Directory.CreateDirectory("artifacts");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create("artifacts/KnightsCleaner-preview.png")) encoder.Save(stream);
        window.Close();
        Console.WriteLine("PASS: WPF window loads, drive defaults, activity log layout. No cleanup invoked.");
    }
}
