using System.Windows;

namespace DDRGpScoreViewer;

public partial class App : Application
{
    public App() => SQLitePCL.Batteries_V2.Init();
}
