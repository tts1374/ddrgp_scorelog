using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;

namespace DDRGpScoreViewer.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ScoreViewerRepository repository;
    private PlayHistoryItem? selectedPlay;
    private string statusTitle = "プレーデータを選択してください";
    private string statusMessage =
        "正式なプレーデータと生成済みの楽曲データを選ぶと、履歴と自己ベストを表示します。";
    private bool hasData;
    private string masterVersion = "—";

    public MainViewModel(ScoreViewerRepository repository) => this.repository = repository;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PlayHistoryItem> Plays { get; } = [];
    public ObservableCollection<ChartBestItem> ChartBests { get; } = [];

    public PlayHistoryItem? SelectedPlay
    {
        get => selectedPlay;
        set => SetProperty(ref selectedPlay, value);
    }

    public string StatusTitle
    {
        get => statusTitle;
        private set => SetProperty(ref statusTitle, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public bool HasData
    {
        get => hasData;
        private set
        {
            if (SetProperty(ref hasData, value))
            {
                OnPropertyChanged(nameof(StatusVisibility));
                OnPropertyChanged(nameof(DataVisibility));
            }
        }
    }

    public System.Windows.Visibility StatusVisibility =>
        HasData ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public System.Windows.Visibility DataVisibility =>
        HasData ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public string MasterVersion
    {
        get => masterVersion;
        private set => SetProperty(ref masterVersion, value);
    }

    public void Load(string scoreDatabasePath, string masterDatabasePath)
    {
        try
        {
            var data = repository.Load(scoreDatabasePath, masterDatabasePath);
            Replace(Plays, data.Plays);
            Replace(ChartBests, data.ChartBests);
            MasterVersion = data.MasterVersion;
            SelectedPlay = Plays.FirstOrDefault();
            if (Plays.Count == 0)
            {
                HasData = false;
                StatusTitle = "まだプレーデータがありません";
                StatusMessage =
                    "DDR GRAND PRIXをプレーするか、データを読み込むとここに表示されます。";
                return;
            }
            HasData = true;
        }
        catch (ViewerDatabaseException exception)
        {
            Plays.Clear();
            ChartBests.Clear();
            SelectedPlay = null;
            MasterVersion = "—";
            HasData = false;
            StatusTitle = "データを読み込めませんでした";
            StatusMessage = exception.UserMessage;
        }
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
