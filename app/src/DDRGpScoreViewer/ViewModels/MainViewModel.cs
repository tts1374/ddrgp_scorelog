using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.Updates;

namespace DDRGpScoreViewer.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const int ChartBestPageSize = 50;
    private const string AllBestFilterValue = "すべて";
    private const string BestSortScoreDescending = "スコア（高い順）";
    private const string BestSortScoreAscending = "スコア（低い順）";
    private const string BestSortTitleAscending = "曲名（昇順）";
    private const string BestSortLevelAscending = "レベル（昇順）";
    private const string BestSortLastPlayedDescending = "最終プレー（新しい順）";
    private const string BestSortPlayCountDescending = "プレー回数（多い順）";
    private const string ChartDetailAllPlaysMode = "全プレー";
    private const string ChartDetailBestProgressionMode = "自己ベスト推移";
    private static readonly string[] BestVersionOrder =
    [
        "DDR GRAND PRIX",
        "DDR WORLD",
        "DDR A3",
        "DDR A20 PLUS",
        "DDR A20",
        "DDR A",
        "DDR (2014)",
        "DDR (2013)",
        "X3 VS 2ndMIX",
        "X2",
        "X",
        "SuperNOVA 2",
        "SuperNOVA",
        "EXTREME",
        "DDRMAX2",
        "DDRMAX",
        "5thMIX",
        "4thMIX",
        "3rdMIX",
        "2ndMIX",
        "1st",
    ];

    private readonly ScoreViewerRepository repository;
    private readonly IPersonalScoreDbWorkflowRunner? workflowRunner;
    private readonly ISingleFrameCaptureService? captureService;
    private readonly IContinuousCaptureService? continuousCaptureService;
    private readonly ILiveMonitoringCaptureService? liveMonitoringService;
    private readonly ICaptureSaveWorkflowRunner? captureSaveWorkflowRunner;
    private readonly IViewerPathStore? pathStore;
    private readonly IUserSettingsStore userSettingsStore;
    private readonly ViewerDatabasePaths defaultDatabasePaths;
    private readonly IScoreDatabaseInitializer scoreDatabaseInitializer;
    private readonly IDdrGpWindowEnumerator ddrGpWindowEnumerator;
    private readonly AutomaticMonitoringOptions automaticMonitoringOptions;
    private readonly SynchronizationContext? uiSynchronizationContext;
    private PlayHistoryItem? selectedPlay;
    private ChartBestItem? selectedChartBest;
    private HomePlayItem? homeLatestPlay;
    private HomePlayItem? chartDetailLatestPlay;
    private HomePlayItem? chartDetailScoreBestPlay;
    private HomePlayItem? chartDetailExScoreBestPlay;
    private IReadOnlyList<HomePlayItem> chartDetailAllPlayPoints = [];
    private IReadOnlyList<HomePlayItem> chartDetailBestPlayPoints = [];
    private string chartDetailGraphMode = ChartDetailAllPlaysMode;
    private IReadOnlyList<ChartBestItem> allChartBests = [];
    private IReadOnlyList<string> bestVersionOptions = [AllBestFilterValue];
    private string bestPlayStyleFilter = "SINGLE";
    private string bestDifficultyFilter = AllBestFilterValue;
    private string bestLevelFilter = AllBestFilterValue;
    private string bestSongQuery = "";
    private string bestVersionFilter = AllBestFilterValue;
    private string bestPlayStatusFilter = AllBestFilterValue;
    private string bestRankFilter = AllBestFilterValue;
    private string bestClearFilter = AllBestFilterValue;
    private string bestSortFilter = BestSortScoreDescending;
    private int chartBestDisplayedCount;
    private int chartBestTotalCount;
    private bool suppressBestFilterRefresh;
    private int homeTodayPlayCount;
    private int homeTodayScoreUpdateCount;
    private int homeTodayExScoreUpdateCount;
    private int homeTodayFullComboCount;
    private string homeTodayDateDisplay =
        DateTimeOffset.Now.ToString("yyyy/MM/dd", CultureInfo.CurrentCulture);
    private string statusTitle = "既定のDBを確認しています";
    private string statusMessage =
        "現在の環境に対応する既定pathのDBを検証して、履歴と自己ベストを表示します。";
    private bool hasData;
    private string masterVersion = "—";
    private string saveStatusTitle = "";
    private string saveStatusMessage = "";
    private bool hasSaveStatus;
    private bool isSaving;
    private string captureStatusTitle = "";
    private string captureStatusMessage = "";
    private bool hasCaptureStatus;
    private readonly HashSet<string> unresolvedCaptureNotificationEventIds =
        new(StringComparer.Ordinal);
    private string unresolvedNotificationTitle = "";
    private string unresolvedNotificationMessage = "";
    private bool hasUnresolvedNotification;
    private bool isCapturing;
    private bool isContinuousCapturing;
    private bool isStoppingCapture;
    private TaskCompletionSource? continuousCaptureFinished;
#if DEBUG
    private TaskCompletionSource? singleCaptureFinished;
    private TaskCompletionSource? manualSaveFinished;
#endif
    private MonitoringState monitoringState = MonitoringState.Idle;
    private string monitoringTarget = "未選択";
    private string monitoringTargetSize = "—";
    private int monitoringFrameCount;
    private int monitoringSampledFrameCount;
    private int monitoringResultFrameCount;
    private int monitoringConfirmedCandidateCount;
    private int monitoringDiscardedFrameCount;
    private int monitoringPendingCandidateCount;
    private int monitoringCandidateQueueDropCount;
    private DateTimeOffset? monitoringStartedAtUtc;
    private DateTimeOffset? monitoringLatestEventAtUtc;
    private string monitoringReason = "—";
    private MonitoringResultSummary monitoringResults = MonitoringResultSummary.Empty;
    private bool isMonitoringStartPending;
    private bool applicationExitRequested;
    private string scoreDatabasePath = "—";
    private string masterDatabasePath = "—";
    private string catalogDatabasePath = "—";
    private MasterDatabaseInspection masterDatabaseInspection =
        MasterDatabaseInspection.Missing(
            string.Empty,
            "master DBがまだ検証されていません。現在の環境の既定pathを確認してください。");
    private JacketCatalogInspection jacketCatalogInspection =
        JacketCatalogInspection.Missing(
            string.Empty,
            "jacket参照catalogがまだ検証されていません。現在の環境の既定pathを確認してください。");
    private long monitoringSessionSequence;
    private long activeMonitoringSession;
    private CancellationTokenSource? monitoringCancellation;
    private CancellationTokenSource? monitoringStartCancellation;
    private TaskCompletionSource? monitoringStartFinished;
    private nint? activeMonitoringTargetHandle;
    private ILiveMonitoringCaptureService? activeLiveMonitoringService;
    private int monitoringOperationReserved;
    private CancellationTokenSource? automaticMonitoringCancellation;
    private Task? automaticMonitoringTask;
    private bool automaticMonitoringManuallyStopped;
    private bool automaticMonitoringBlocked;
    private string automaticMonitoringBlockReason = "—";
    private bool automaticMonitoringRequiresWindowGap;
    private bool automaticMonitoringWindowLossStopInProgress;
    private bool automaticMonitoringWaitingForUpdate;
    private bool referenceDataUpdateInProgress;
    private bool isSettingsPage;
    private bool startMonitoringOnLaunch = UserSettings.Defaults.StartMonitoringOnLaunch;
    private bool notifyUnresolvedResults = UserSettings.Defaults.NotifyUnresolvedResults;
    private string defaultPlayStyle = UserSettings.Defaults.DefaultPlayStyle;
    private string startupPage = UserSettings.Defaults.StartupPage;
    private bool appliedStartMonitoringOnLaunch = UserSettings.Defaults.StartMonitoringOnLaunch;
    private bool appliedNotifyUnresolvedResults = UserSettings.Defaults.NotifyUnresolvedResults;
    private string appliedDefaultPlayStyle = UserSettings.Defaults.DefaultPlayStyle;
    private string appliedStartupPage = UserSettings.Defaults.StartupPage;
    private string settingsStatusMessage = "変更内容は保存時に反映されます";
    private readonly IApplicationUpdateService? applicationUpdateService;
    private string applicationUpdateStatusTitle = "アプリ更新";
    private string applicationUpdateStatusMessage = "起動後にGitHub Releasesを確認します。";
    private string applicationUpdateVersion = "";
    private int applicationUpdateProgress;
    private bool hasApplicationUpdateStatus;
    private bool applicationUpdateAvailable;
    private bool applicationUpdateDownloaded;
    private int applicationUpdateOperationReserved;

    public MainViewModel(
        ScoreViewerRepository repository,
        IPersonalScoreDbWorkflowRunner? workflowRunner = null,
        ISingleFrameCaptureService? captureService = null,
        IContinuousCaptureService? continuousCaptureService = null,
        ICaptureSaveWorkflowRunner? captureSaveWorkflowRunner = null,
        IViewerPathStore? pathStore = null,
        ViewerDatabasePaths? defaultDatabasePaths = null,
        IScoreDatabaseInitializer? scoreDatabaseInitializer = null,
        IDdrGpWindowEnumerator? ddrGpWindowEnumerator = null,
        ILiveMonitoringCaptureService? liveMonitoringService = null,
        IApplicationUpdateService? applicationUpdateService = null,
        AutomaticMonitoringOptions? automaticMonitoringOptions = null,
        IUserSettingsStore? userSettingsStore = null)
    {
        this.repository = repository;
        this.workflowRunner = workflowRunner;
        this.captureService = captureService;
        this.continuousCaptureService = continuousCaptureService;
        this.liveMonitoringService = liveMonitoringService;
        this.captureSaveWorkflowRunner = captureSaveWorkflowRunner;
        this.pathStore = pathStore;
        this.defaultDatabasePaths = defaultDatabasePaths ?? ViewerDatabasePaths.ResolveDefault();
        this.userSettingsStore = userSettingsStore ??
            new LocalUserSettingsStore(this.defaultDatabasePaths.UserSettingsPath);
        this.scoreDatabaseInitializer = scoreDatabaseInitializer ?? new PersonalScoreDbInitializer();
        this.ddrGpWindowEnumerator = ddrGpWindowEnumerator ?? new DdrGpWindowEnumerator();
        this.automaticMonitoringOptions = automaticMonitoringOptions ?? new AutomaticMonitoringOptions();
        this.automaticMonitoringOptions.Validate();
        this.applicationUpdateService = applicationUpdateService;
        uiSynchronizationContext = SynchronizationContext.Current;
        scoreDatabasePath = this.defaultDatabasePaths.ScoreDatabasePath;
        masterDatabasePath = this.defaultDatabasePaths.MasterDatabasePath;
        catalogDatabasePath = this.defaultDatabasePaths.JacketCatalogDatabasePath;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<ChartBestItem>? ChartBestSelectionRequested;
    public event EventHandler? ChartBestListReset;
    public event EventHandler? ChartDetailUpdated;
    public event Action<UnresolvedCaptureNotification>? UnresolvedCaptureNotificationRequested;
    public event Action<UnresolvedCaptureNotification>? UnresolvedCaptureDiagnosticRecorded;

    public ObservableCollection<PlayHistoryItem> Plays { get; } = [];
    public ObservableCollection<ChartBestItem> ChartBests { get; } = [];
    public ObservableCollection<HomePlayItem> HomeRecentPlays { get; } = [];
    public ObservableCollection<HomePlayItem> HomeBestUpdates { get; } = [];
    public ObservableCollection<string> BestActiveFilterChips { get; } = [];
    public ObservableCollection<HomePlayItem> ChartDetailHistory { get; } = [];

    public IReadOnlyList<string> BestDifficultyOptions { get; } =
    [
        AllBestFilterValue,
        "BEGINNER",
        "BASIC",
        "DIFFICULT",
        "EXPERT",
        "CHALLENGE",
    ];

    public IReadOnlyList<string> BestLevelOptions { get; } =
        [AllBestFilterValue, .. Enumerable.Range(1, 19).Select(level => $"Lv.{level}")];

    public IReadOnlyList<string> BestPlayStatusOptions { get; } =
        [AllBestFilterValue, "プレー済み", "未プレー"];

    public IReadOnlyList<string> BestRankOptions { get; } =
        [AllBestFilterValue, "AAA以上", "AA", "A以下"];

    public IReadOnlyList<string> BestClearOptions { get; } =
        [AllBestFilterValue, "PFC", "GFC", "FC", "CLEAR", "未CLEAR"];

    public IReadOnlyList<string> BestSortOptions { get; } =
    [
        BestSortScoreDescending,
        BestSortScoreAscending,
        BestSortTitleAscending,
        BestSortLevelAscending,
        BestSortLastPlayedDescending,
        BestSortPlayCountDescending,
    ];

    public IReadOnlyList<string> StartupPageOptions { get; } =
        [UserSettings.HomeStartupPage, UserSettings.BestStartupPage, UserSettings.HistoryStartupPage];

    public bool StartMonitoringOnLaunch
    {
        get => startMonitoringOnLaunch;
        set => SetProperty(ref startMonitoringOnLaunch, value);
    }

    public bool NotifyUnresolvedResults
    {
        get => notifyUnresolvedResults;
        set => SetProperty(ref notifyUnresolvedResults, value);
    }

    public string DefaultPlayStyle
    {
        get => defaultPlayStyle;
        set
        {
            if (UserSettings.IsValidPlayStyle(value))
            {
                SetProperty(ref defaultPlayStyle, value);
            }
        }
    }

    public string StartupPage
    {
        get => startupPage;
        set
        {
            if (UserSettings.IsValidStartupPage(value))
            {
                SetProperty(ref startupPage, value);
            }
        }
    }

    public string SettingsStatusMessage
    {
        get => settingsStatusMessage;
        private set => SetProperty(ref settingsStatusMessage, value);
    }

    public IReadOnlyList<string> BestVersionOptions
    {
        get => bestVersionOptions;
        private set => SetProperty(ref bestVersionOptions, value);
    }

    public PlayHistoryItem? SelectedPlay
    {
        get => selectedPlay;
        set => SetProperty(ref selectedPlay, value);
    }

    public ChartBestItem? SelectedChartBest
    {
        get => selectedChartBest;
        private set
        {
            if (!SetProperty(ref selectedChartBest, value))
            {
                return;
            }
            RefreshChartDetail();
        }
    }

    public string ChartDetailGraphMode
    {
        get => chartDetailGraphMode;
        private set => SetProperty(ref chartDetailGraphMode, value);
    }

    public IReadOnlyList<HomePlayItem> ChartDetailGraphPlays =>
        ChartDetailGraphMode == ChartDetailBestProgressionMode
            ? chartDetailBestPlayPoints
            : chartDetailAllPlayPoints;

    public IReadOnlyList<HomePlayItem> ChartDetailAllPlayPoints => chartDetailAllPlayPoints;

    public IReadOnlyList<HomePlayItem> ChartDetailBestPlayPoints => chartDetailBestPlayPoints;

    public HomePlayItem? ChartDetailLatestPlay => chartDetailLatestPlay;

    public string ChartDetailSongTitle => selectedChartBest?.SongTitle ?? "—";

    public string ChartDetailPlayStyleDisplay => selectedChartBest?.PlayStyleDisplay ?? "—";

    public string ChartDetailDifficultyDisplay => selectedChartBest?.DifficultyDisplay ?? "—";

    public string ChartDetailLevelDisplay => selectedChartBest?.LevelDisplay ?? "—";

    public string ChartDetailBestScoreDisplay => selectedChartBest?.BestScoreDisplay ?? "—";

    public string ChartDetailBestExScoreDisplay => selectedChartBest?.BestExScoreDisplay ?? "—";

    public string ChartDetailRankDisplay => selectedChartBest is { IsPlayed: true }
        ? selectedChartBest.RankDisplay
        : "—";

    public System.Windows.Visibility ChartDetailRankBadgeVisibility =>
        selectedChartBest is { IsPlayed: true, HasRank: true }
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility ChartDetailRankPlaceholderVisibility =>
        selectedChartBest is { IsPlayed: true, HasRank: true }
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public string ChartDetailClearDisplay => selectedChartBest is { IsPlayed: true }
        ? selectedChartBest.ClearDisplay
        : "—";

    public System.Windows.Visibility ChartDetailClearBadgeVisibility =>
        selectedChartBest is { IsPlayed: true, HasClear: true }
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility ChartDetailClearPlaceholderVisibility =>
        selectedChartBest is { IsPlayed: true, HasClear: true }
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public string ChartDetailFlareRankDisplay => selectedChartBest is { IsPlayed: true }
        ? selectedChartBest.FlareRankDisplay
        : "—";

    public System.Windows.Visibility ChartDetailFlareBadgeVisibility =>
        selectedChartBest is { IsPlayed: true, FlareBadgeGroup: not "None" }
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility ChartDetailFlarePlaceholderVisibility =>
        selectedChartBest is { IsPlayed: true, FlareBadgeGroup: not "None" }
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public string ChartDetailRankBadgeGroup => selectedChartBest is { IsPlayed: true }
        ? selectedChartBest.RankBadgeGroup
        : "Neutral";

    public string ChartDetailClearBadgeGroup => selectedChartBest is { IsPlayed: true }
        ? selectedChartBest.ClearBadgeGroup
        : "Neutral";

    public string ChartDetailFlareBadgeGroup => selectedChartBest is { IsPlayed: true }
        ? selectedChartBest.FlareBadgeGroup
        : "None";

    public string ChartDetailScoreBestAtDisplay => chartDetailScoreBestPlay?.Play.PlayedAtDisplay ?? "—";

    public string ChartDetailExScoreBestAtDisplay => chartDetailExScoreBestPlay?.Play.PlayedAtDisplay ?? "—";

    public string ChartDetailPlayCountDisplay => $"{ChartDetailHistory.Count:N0}回";

    public string ChartDetailHistoryCountDisplay => $"{ChartDetailHistory.Count:N0}件";

    public string ChartDetailFullComboCountDisplay =>
        $"{ChartDetailHistory.Count(IsFullCombo):N0}回";

    public System.Windows.Visibility ChartDetailPlayVisibility =>
        ChartDetailHistory.Count == 0
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public System.Windows.Visibility ChartDetailEmptyVisibility =>
        ChartDetailHistory.Count == 0
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public string BestPlayStyleFilter
    {
        get => bestPlayStyleFilter;
        set
        {
            if (!SetProperty(ref bestPlayStyleFilter, value))
            {
                return;
            }
            UpdateBestVersionOptions();
            OnBestFilterChanged();
        }
    }

    public string BestDifficultyFilter
    {
        get => bestDifficultyFilter;
        set
        {
            if (!SetProperty(ref bestDifficultyFilter, value))
            {
                return;
            }
            OnBestFilterChanged();
        }
    }

    public string BestLevelFilter
    {
        get => bestLevelFilter;
        set
        {
            if (!SetProperty(ref bestLevelFilter, value))
            {
                return;
            }
            OnBestFilterChanged();
        }
    }

    public string BestSongQuery
    {
        get => bestSongQuery;
        set
        {
            if (!SetProperty(ref bestSongQuery, value ?? ""))
            {
                return;
            }
            OnBestFilterChanged();
        }
    }

    public string BestVersionFilter
    {
        get => bestVersionFilter;
        set
        {
            if (!SetProperty(ref bestVersionFilter, value))
            {
                return;
            }
            OnBestFilterChanged();
        }
    }

    public string BestPlayStatusFilter
    {
        get => bestPlayStatusFilter;
        set
        {
            if (!SetProperty(ref bestPlayStatusFilter, value))
            {
                return;
            }
            OnBestFilterChanged();
        }
    }

    public string BestRankFilter
    {
        get => bestRankFilter;
        set
        {
            if (!SetProperty(ref bestRankFilter, value))
            {
                return;
            }
            OnBestFilterChanged();
        }
    }

    public string BestClearFilter
    {
        get => bestClearFilter;
        set
        {
            if (!SetProperty(ref bestClearFilter, value))
            {
                return;
            }
            OnBestFilterChanged();
        }
    }

    public string BestSortFilter
    {
        get => bestSortFilter;
        set
        {
            if (!SetProperty(ref bestSortFilter, value))
            {
                return;
            }
            OnBestFilterChanged();
        }
    }

    public int ChartBestDisplayedCount
    {
        get => chartBestDisplayedCount;
        private set => SetProperty(ref chartBestDisplayedCount, value);
    }

    public int ChartBestTotalCount
    {
        get => chartBestTotalCount;
        private set => SetProperty(ref chartBestTotalCount, value);
    }

    public bool CanLoadMoreChartBests => ChartBestDisplayedCount < ChartBestTotalCount;

    public string ChartBestRangeDisplay => ChartBestTotalCount == 0
        ? "表示 0譜面 / 全0譜面"
        : $"表示 1〜{ChartBestDisplayedCount:N0} / 全{ChartBestTotalCount:N0}譜面";

    public string ChartBestLoadMoreHintDisplay => CanLoadMoreChartBests
        ? "下端までスクロールすると次の50譜面を表示"
        : $"全{ChartBestTotalCount:N0}譜面を表示中";

    public string BestActiveFilterSummary => BestActiveFilterChips.Count == 0
        ? "適用中: なし"
        : $"適用中: {string.Join(" / ", BestActiveFilterChips)}";

    public HomePlayItem? HomeLatestPlay
    {
        get => homeLatestPlay;
        private set => SetProperty(ref homeLatestPlay, value);
    }

    public int HomeTodayPlayCount
    {
        get => homeTodayPlayCount;
        private set => SetProperty(ref homeTodayPlayCount, value);
    }

    public int HomeTodayScoreUpdateCount
    {
        get => homeTodayScoreUpdateCount;
        private set => SetProperty(ref homeTodayScoreUpdateCount, value);
    }

    public int HomeTodayExScoreUpdateCount
    {
        get => homeTodayExScoreUpdateCount;
        private set => SetProperty(ref homeTodayExScoreUpdateCount, value);
    }

    public int HomeTodayFullComboCount
    {
        get => homeTodayFullComboCount;
        private set => SetProperty(ref homeTodayFullComboCount, value);
    }

    public string HomeTodayDateDisplay
    {
        get => homeTodayDateDisplay;
        private set => SetProperty(ref homeTodayDateDisplay, value);
    }

    public bool HasHomeBestUpdates => HomeBestUpdates.Count > 0;

    public string HomeBestUpdateSummaryDisplay => HomeBestUpdates.Count switch
    {
        0 => "自己ベスト更新はまだありません",
        var count => $"直近の自己ベスト更新を{count}件表示",
    };

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

    public bool IsSettingsPage => isSettingsPage;

    public System.Windows.Visibility StatusVisibility =>
        HasData || IsSettingsPage
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
    public System.Windows.Visibility DataVisibility =>
        HasData || IsSettingsPage
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public string MasterVersion
    {
        get => masterVersion;
        private set => SetProperty(ref masterVersion, value);
    }

    public string ScoreDatabasePath
    {
        get => scoreDatabasePath;
        private set => SetProperty(ref scoreDatabasePath, value);
    }

    public string MasterDatabasePath
    {
        get => masterDatabasePath;
        private set => SetProperty(ref masterDatabasePath, value);
    }

    public string CatalogDatabasePath
    {
        get => catalogDatabasePath;
        private set => SetProperty(ref catalogDatabasePath, value);
    }

    public string DatabaseEnvironmentDisplay => defaultDatabasePaths.Environment switch
    {
        ViewerDatabaseEnvironment.Development => "development（development root）",
        ViewerDatabaseEnvironment.Production => "production（LocalAppData）",
        _ => "unknown",
    };

    public MasterDatabaseStatus MasterDatabaseStatus => masterDatabaseInspection.Status;

    public string MasterDatabaseStatusDisplay => MasterDatabaseStatus switch
    {
        MasterDatabaseStatus.Missing => "missing（既定pathを確認）",
        MasterDatabaseStatus.Unreadable => "read不可（既定pathを確認）",
        MasterDatabaseStatus.Incompatible => "schema incompatible（既定pathを確認）",
        MasterDatabaseStatus.Compatible => "compatible",
        _ => MasterDatabaseStatus.ToString(),
    };

    public string MasterDatabaseReason => masterDatabaseInspection.Message;

    public MasterDatabaseStatus CatalogDatabaseStatus => jacketCatalogInspection.Status;

    public string CatalogDatabaseStatusDisplay => CatalogDatabaseStatus switch
    {
        MasterDatabaseStatus.Missing => "missing（既定pathを確認）",
        MasterDatabaseStatus.Unreadable => "read不可（既定pathを確認）",
        MasterDatabaseStatus.Incompatible => "schema incompatible（既定pathを確認）",
        MasterDatabaseStatus.Compatible => "compatible",
        _ => CatalogDatabaseStatus.ToString(),
    };

    public string CatalogDatabaseReason => jacketCatalogInspection.Message;

    public string SaveStatusTitle
    {
        get => saveStatusTitle;
        private set => SetProperty(ref saveStatusTitle, value);
    }

    public string SaveStatusMessage
    {
        get => saveStatusMessage;
        private set => SetProperty(ref saveStatusMessage, value);
    }

    public bool HasSaveStatus
    {
        get => hasSaveStatus;
        private set
        {
            if (SetProperty(ref hasSaveStatus, value))
            {
                OnPropertyChanged(nameof(SaveStatusVisibility));
            }
        }
    }

    public bool IsSaving
    {
        get => isSaving;
        private set
        {
            if (SetProperty(ref isSaving, value))
            {
                OnPropertyChanged(nameof(CanStartMonitoring));
                OnPropertyChanged(nameof(CanRunDeveloperOperations));
            }
        }
    }

    public System.Windows.Visibility SaveStatusVisibility =>
        HasSaveStatus ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public string UnresolvedNotificationTitle
    {
        get => unresolvedNotificationTitle;
        private set => SetProperty(ref unresolvedNotificationTitle, value);
    }

    public string UnresolvedNotificationMessage
    {
        get => unresolvedNotificationMessage;
        private set => SetProperty(ref unresolvedNotificationMessage, value);
    }

    public bool HasUnresolvedNotification
    {
        get => hasUnresolvedNotification;
        private set
        {
            if (SetProperty(ref hasUnresolvedNotification, value))
            {
                OnPropertyChanged(nameof(UnresolvedNotificationVisibility));
            }
        }
    }

    public System.Windows.Visibility UnresolvedNotificationVisibility =>
        HasUnresolvedNotification
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public string CaptureStatusTitle
    {
        get => captureStatusTitle;
        private set => SetProperty(ref captureStatusTitle, value);
    }

    public string CaptureStatusMessage
    {
        get => captureStatusMessage;
        private set => SetProperty(ref captureStatusMessage, value);
    }

    public bool HasCaptureStatus
    {
        get => hasCaptureStatus;
        private set
        {
            if (SetProperty(ref hasCaptureStatus, value))
            {
                OnPropertyChanged(nameof(CaptureStatusVisibility));
            }
        }
    }

    public bool IsCapturing
    {
        get => isCapturing;
        private set
        {
            if (SetProperty(ref isCapturing, value))
            {
                OnPropertyChanged(nameof(CanStartMonitoring));
                OnPropertyChanged(nameof(CanRunDeveloperOperations));
            }
        }
    }

    public bool IsContinuousCapturing
    {
        get => isContinuousCapturing;
        private set
        {
            if (SetProperty(ref isContinuousCapturing, value))
            {
                OnPropertyChanged(nameof(CanStartMonitoring));
                OnPropertyChanged(nameof(CanStopMonitoring));
                OnPropertyChanged(nameof(CanRunDeveloperOperations));
            }
        }
    }

    public bool IsStoppingCapture
    {
        get => isStoppingCapture;
        private set => SetProperty(ref isStoppingCapture, value);
    }

    public System.Windows.Visibility CaptureStatusVisibility =>
        HasCaptureStatus ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public string ApplicationUpdateStatusTitle
    {
        get => applicationUpdateStatusTitle;
        private set => SetProperty(ref applicationUpdateStatusTitle, value);
    }

    public string ApplicationUpdateStatusMessage
    {
        get => applicationUpdateStatusMessage;
        private set => SetProperty(ref applicationUpdateStatusMessage, value);
    }

    public string ApplicationUpdateVersion
    {
        get => applicationUpdateVersion;
        private set => SetProperty(ref applicationUpdateVersion, value);
    }

    public int ApplicationUpdateProgress
    {
        get => applicationUpdateProgress;
        private set => SetProperty(ref applicationUpdateProgress, value);
    }

    public bool HasApplicationUpdateStatus
    {
        get => hasApplicationUpdateStatus;
        private set => SetProperty(ref hasApplicationUpdateStatus, value);
    }

    public System.Windows.Visibility ApplicationUpdateStatusVisibility =>
        applicationUpdateService is null
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public bool IsApplicationUpdateBusy =>
        Volatile.Read(ref applicationUpdateOperationReserved) != 0;

    public bool IsUpdateProcessing =>
        IsApplicationUpdateBusy || referenceDataUpdateInProgress;

    public bool CanCheckForApplicationUpdate =>
        applicationUpdateService is not null &&
        !IsApplicationUpdateBusy &&
        !applicationExitRequested;

    public bool CanDownloadAndApplyApplicationUpdate =>
        applicationUpdateService is not null &&
        (applicationUpdateAvailable || applicationUpdateDownloaded) &&
        !IsApplicationUpdateBusy &&
        !applicationExitRequested;

    public MonitoringState CurrentMonitoringState
    {
        get => monitoringState;
        private set
        {
            if (SetProperty(ref monitoringState, value))
            {
                OnPropertyChanged(nameof(MonitoringStateDisplay));
                OnPropertyChanged(nameof(MonitoringTargetStatus));
                OnPropertyChanged(nameof(CanStartMonitoring));
                OnPropertyChanged(nameof(CanStopMonitoring));
                OnPropertyChanged(nameof(CanRunDeveloperOperations));
            }
        }
    }

    public string MonitoringStateDisplay => CurrentMonitoringState switch
    {
        MonitoringState.Idle => "待機中",
        MonitoringState.Starting => "監視開始中",
        MonitoringState.WaitingForGame => "ゲーム待機中",
        MonitoringState.SelectingTarget => "対象windowを選択中",
        MonitoringState.Monitoring => "監視中",
        MonitoringState.Stopping => "停止処理中",
        MonitoringState.Stopped => "停止済み",
        MonitoringState.ManuallyStopped => "手動停止済み",
        MonitoringState.Blocked => "監視開始不可",
        MonitoringState.ShuttingDown => "終了処理中",
        MonitoringState.TargetClosed => "対象window終了",
        MonitoringState.Resized => "対象windowのサイズ変更",
        MonitoringState.DeviceLost => "GPU device lost",
        MonitoringState.CaptureFailed => "capture失敗",
        MonitoringState.WorkflowFailed => "workflow失敗",
        _ => CurrentMonitoringState.ToString(),
    };

    public string MonitoringTarget
    {
        get => monitoringTarget;
        private set
        {
            if (SetProperty(ref monitoringTarget, value))
            {
                OnPropertyChanged(nameof(MonitoringTargetStatus));
            }
        }
    }

    public string MonitoringTargetSize
    {
        get => monitoringTargetSize;
        private set => SetProperty(ref monitoringTargetSize, value);
    }

    public string MonitoringTargetStatus => CurrentMonitoringState switch
    {
        MonitoringState.Starting => "検出済み・開始中",
        MonitoringState.WaitingForGame => "待機中",
        MonitoringState.SelectingTarget => "選択待ち",
        MonitoringState.Monitoring or MonitoringState.Stopping => "選択済み",
        MonitoringState.ManuallyStopped => "手動停止",
        MonitoringState.Blocked => "開始不可",
        MonitoringState.ShuttingDown => "終了処理中",
        MonitoringState.TargetClosed => "閉鎖",
        MonitoringState.Resized => "resize検出",
        MonitoringState.DeviceLost => "device lost",
        _ when MonitoringTarget != "未選択" => "停止済み",
        _ => "未選択",
    };

    public int MonitoringFrameCount
    {
        get => monitoringFrameCount;
        private set => SetProperty(ref monitoringFrameCount, value);
    }

    public int MonitoringSampledFrameCount
    {
        get => monitoringSampledFrameCount;
        private set => SetProperty(ref monitoringSampledFrameCount, value);
    }

    public int MonitoringResultFrameCount
    {
        get => monitoringResultFrameCount;
        private set => SetProperty(ref monitoringResultFrameCount, value);
    }

    public int MonitoringConfirmedCandidateCount
    {
        get => monitoringConfirmedCandidateCount;
        private set => SetProperty(ref monitoringConfirmedCandidateCount, value);
    }

    public int MonitoringDiscardedFrameCount
    {
        get => monitoringDiscardedFrameCount;
        private set => SetProperty(ref monitoringDiscardedFrameCount, value);
    }

    public int MonitoringPendingCandidateCount
    {
        get => monitoringPendingCandidateCount;
        private set => SetProperty(ref monitoringPendingCandidateCount, value);
    }

    public int MonitoringCandidateQueueDropCount
    {
        get => monitoringCandidateQueueDropCount;
        private set => SetProperty(ref monitoringCandidateQueueDropCount, value);
    }

    public string MonitoringStartedAtDisplay => FormatMonitoringTime(monitoringStartedAtUtc);
    public string MonitoringLatestEventAtDisplay => FormatMonitoringTime(monitoringLatestEventAtUtc);

    public string MonitoringReason
    {
        get => monitoringReason;
        private set => SetProperty(ref monitoringReason, value);
    }

    public MonitoringResultSummary MonitoringResults
    {
        get => monitoringResults;
        private set
        {
            if (SetProperty(ref monitoringResults, value))
            {
                OnPropertyChanged(nameof(MonitoringResultsDisplay));
                OnPropertyChanged(nameof(MonitoringResultAtDisplay));
            }
        }
    }

    public string MonitoringResultsDisplay =>
        $"saved={MonitoringResults.Saved}, duplicate={MonitoringResults.Duplicate}, " +
        $"excluded={MonitoringResults.Excluded}, unresolved={MonitoringResults.Unresolved}, " +
        $"analysis_failed={MonitoringResults.AnalysisFailed}, db_rejected={MonitoringResults.DbRejected}, " +
        $"workflow_failed={MonitoringResults.WorkflowFailed}, sampled={MonitoringSampledFrameCount}, " +
        $"result={MonitoringResultFrameCount}, candidate={MonitoringConfirmedCandidateCount}, " +
        $"discarded={MonitoringDiscardedFrameCount}, pending={MonitoringPendingCandidateCount}, " +
        $"queue_dropped={MonitoringCandidateQueueDropCount}";

    public string MonitoringResultAtDisplay =>
        MonitoringResults.RecordedAtUtc == DateTimeOffset.MinValue
            ? "—"
            : FormatMonitoringTime(MonitoringResults.RecordedAtUtc);

    public bool CanStartMonitoring =>
        TrayMenuState.FromMonitoringState(CurrentMonitoringState).CanStart &&
        !IsSaving && !IsCapturing && !IsContinuousCapturing &&
        !isMonitoringStartPending && !IsMonitoringStartInProgress &&
        !IsUpdateProcessing && !applicationExitRequested;

    public bool CanRunDeveloperOperations =>
        !applicationExitRequested &&
        !isMonitoringStartPending &&
        !IsMonitoringStartInProgress &&
        Volatile.Read(ref monitoringOperationReserved) == 0 &&
        !IsSaving &&
        !IsCapturing &&
        !IsContinuousCapturing &&
        CurrentMonitoringState is not (
            MonitoringState.SelectingTarget or
            MonitoringState.Monitoring or
            MonitoringState.Stopping);

    public bool CanStopMonitoring =>
        (IsContinuousCapturing || IsMonitoringStartInProgress) &&
        !applicationExitRequested &&
        (TrayMenuState.FromMonitoringState(CurrentMonitoringState).CanStop ||
            IsStoppingCapture || CurrentMonitoringState == MonitoringState.CaptureFailed);

    public bool IsApplicationExitRequested => applicationExitRequested;

    public bool IsAutomaticMonitoringEnabled =>
        automaticMonitoringOptions.Enabled && appliedStartMonitoringOnLaunch;

    internal string AppliedStartupPage => appliedStartupPage;

    internal void SetSettingsPage(bool value)
    {
        if (isSettingsPage == value)
        {
            return;
        }

        isSettingsPage = value;
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(StatusVisibility));
        OnPropertyChanged(nameof(DataVisibility));
    }

    internal void RestoreUserSettings()
    {
        try
        {
            ApplyUserSettings(userSettingsStore.Load());
            SettingsStatusMessage = "変更内容は保存時に反映されます";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or InvalidOperationException)
        {
            ApplyUserSettings(null);
            SettingsStatusMessage =
                $"保存済み設定を読み込めなかったため、初期値を使用しています。{exception.Message}";
        }
    }

    internal bool SaveUserSettings()
    {
        var settings = new UserSettings(
            StartMonitoringOnLaunch,
            NotifyUnresolvedResults,
            DefaultPlayStyle,
            StartupPage);
        if (!settings.IsValid)
        {
            SettingsStatusMessage = "設定値を確認してから保存してください。";
            return false;
        }

        try
        {
            userSettingsStore.Save(settings);
            ApplyUserSettings(settings);
            SettingsStatusMessage = "設定を保存しました。";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException)
        {
            SettingsStatusMessage = $"設定を保存できませんでした。{exception.Message}";
            return false;
        }
    }

    internal void ResetUserSettings()
    {
        StartMonitoringOnLaunch = UserSettings.Defaults.StartMonitoringOnLaunch;
        NotifyUnresolvedResults = UserSettings.Defaults.NotifyUnresolvedResults;
        DefaultPlayStyle = UserSettings.Defaults.DefaultPlayStyle;
        StartupPage = UserSettings.Defaults.StartupPage;
        SettingsStatusMessage = "初期値に戻しました。保存すると反映されます。";
    }

    private void ApplyUserSettings(UserSettings? settings)
    {
        var effective = settings?.IsValid == true ? settings : UserSettings.Defaults;
        appliedStartMonitoringOnLaunch = effective.StartMonitoringOnLaunch;
        appliedNotifyUnresolvedResults = effective.NotifyUnresolvedResults;
        appliedDefaultPlayStyle = effective.DefaultPlayStyle;
        appliedStartupPage = effective.StartupPage;
        StartMonitoringOnLaunch = effective.StartMonitoringOnLaunch;
        NotifyUnresolvedResults = effective.NotifyUnresolvedResults;
        DefaultPlayStyle = effective.DefaultPlayStyle;
        StartupPage = effective.StartupPage;
        BestPlayStyleFilter = appliedDefaultPlayStyle;
        OnPropertyChanged(nameof(IsAutomaticMonitoringEnabled));

        if (!appliedNotifyUnresolvedResults)
        {
            HasUnresolvedNotification = false;
            UnresolvedNotificationTitle = "";
            UnresolvedNotificationMessage = "";
        }
    }

    private bool IsMonitoringStartInProgress =>
        monitoringStartFinished is not null;

    public void RequestApplicationExit()
    {
        if (applicationExitRequested)
        {
            return;
        }
        applicationExitRequested = true;
        OnPropertyChanged(nameof(IsApplicationExitRequested));
        OnPropertyChanged(nameof(CanStartMonitoring));
        OnPropertyChanged(nameof(CanRunDeveloperOperations));
        OnPropertyChanged(nameof(CanCheckForApplicationUpdate));
        OnPropertyChanged(nameof(CanDownloadAndApplyApplicationUpdate));
        OnPropertyChanged(nameof(IsUpdateProcessing));
        SetMonitoringState(MonitoringState.ShuttingDown, "終了処理中です。新しい監視を開始しません。");
        monitoringCancellation?.Cancel();
        monitoringStartCancellation?.Cancel();
        automaticMonitoringCancellation?.Cancel();
    }

    public async Task CheckForApplicationUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryReserveApplicationUpdateOperation())
        {
            return;
        }

        try
        {
            SetApplicationUpdateStatus(
                "アプリ更新を確認しています",
                "GitHub Releasesへ接続しています。通信に失敗しても現在のversionで通常利用を続けられます。",
                progress: 0);
            var result = await applicationUpdateService!.CheckForUpdatesAsync(cancellationToken);
            ApplyApplicationUpdateResult(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!applicationExitRequested)
            {
                SetApplicationUpdateStatus(
                    "アプリ更新の確認をcancelしました",
                    "現在のversionで通常利用を続けられます。",
                    progress: 0);
            }
        }
        catch (Exception exception)
        {
            ApplyApplicationUpdateResult(
                new ApplicationUpdateResult(
                    ApplicationUpdateStatus.Failed,
                    $"アプリ更新の確認に失敗しました。現在のversionで通常利用を続けられます。 {exception.Message}"));
        }
        finally
        {
            ReleaseApplicationUpdateOperation();
        }
    }

    public async Task DownloadAndApplyApplicationUpdateAsync(
        Func<Task> prepareExit,
        Func<Task> completeExit,
        Action forceExit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepareExit);
        ArgumentNullException.ThrowIfNull(completeExit);
        ArgumentNullException.ThrowIfNull(forceExit);
        if (!TryReserveApplicationUpdateOperation())
        {
            return;
        }

        try
        {
            SetApplicationUpdateStatus(
                "アプリ更新をdownloadしています",
                "完了するまで現在のversionを使い続けます。",
                progress: 0);
            var downloaded = await applicationUpdateService!.DownloadAsync(
                progress => SetApplicationUpdateProgress(progress),
                cancellationToken);
            ApplyApplicationUpdateResult(downloaded);
            if (downloaded.Status != ApplicationUpdateStatus.Downloaded)
            {
                return;
            }

            SetApplicationUpdateStatus(
                "アプリ更新を適用しています",
                "監視、進行中の保存、capture runtimeを完全に終了してから再起動します。",
                progress: 100);
            var applied = await applicationUpdateService.ApplyAndRestartAsync(
                prepareExit,
                completeExit,
                forceExit,
                cancellationToken);
            ApplyApplicationUpdateResult(applied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!applicationExitRequested)
            {
                SetApplicationUpdateStatus(
                    "アプリ更新をcancelしました",
                    "現在のversionで通常利用を続けられます。",
                    progress: 0);
            }
        }
        catch (Exception exception)
        {
            ApplyApplicationUpdateResult(
                new ApplicationUpdateResult(
                    ApplicationUpdateStatus.Failed,
                    $"アプリ更新の適用に失敗しました。現在のversionで通常利用を続けられます。 {exception.Message}"));
        }
        finally
        {
            ReleaseApplicationUpdateOperation();
        }
    }

    public async Task WaitForOperationsAsync()
    {
#if DEBUG
        Task[] operations =
        [
            .. new[]
            {
                singleCaptureFinished?.Task,
                manualSaveFinished?.Task,
                continuousCaptureFinished?.Task,
                automaticMonitoringTask,
                monitoringStartFinished?.Task,
            }.OfType<Task>(),
        ];
#else
        Task[] operations =
        [
            .. new[]
            {
                continuousCaptureFinished?.Task,
                automaticMonitoringTask,
                monitoringStartFinished?.Task,
            }.OfType<Task>(),
        ];
#endif
        await Task.WhenAll(operations);
    }

    internal void SetMonitoringStartPending(bool value)
    {
        if (isMonitoringStartPending != value)
        {
            isMonitoringStartPending = value;
            OnPropertyChanged(nameof(CanStartMonitoring));
            OnPropertyChanged(nameof(CanStopMonitoring));
            OnPropertyChanged(nameof(CanRunDeveloperOperations));
        }
    }

    internal void SetReferenceDataUpdateInProgress(bool value)
    {
        if (referenceDataUpdateInProgress == value)
        {
            return;
        }

        referenceDataUpdateInProgress = value;
        OnPropertyChanged(nameof(IsUpdateProcessing));
        OnPropertyChanged(nameof(CanStartMonitoring));
    }

    internal void StartAutomaticMonitoring(nint ownerWindowHandle)
    {
        if (!IsAutomaticMonitoringEnabled ||
            applicationExitRequested ||
            automaticMonitoringTask is not null)
        {
            return;
        }

        automaticMonitoringCancellation = new CancellationTokenSource();
        automaticMonitoringTask = RunAutomaticMonitoringAsync(
            ownerWindowHandle,
            automaticMonitoringCancellation.Token);
    }

    private async Task RunAutomaticMonitoringAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken)
    {
        var consecutiveDetections = 0;
        var consecutiveMisses = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested && !applicationExitRequested)
            {
                if (IsUpdateProcessing)
                {
                    consecutiveDetections = 0;
                    consecutiveMisses = 0;
                    automaticMonitoringWaitingForUpdate = true;
                    if (!IsContinuousCapturing && !IsMonitoringStartInProgress)
                    {
                        SetAutomaticBlocked(
                            "更新処理中のため、自動監視を開始せず完了を待っています。",
                            temporary: true);
                    }
                    await DelayAutomaticMonitoringAsync(cancellationToken);
                    continue;
                }

                if (automaticMonitoringWaitingForUpdate)
                {
                    automaticMonitoringWaitingForUpdate = false;
                    if (automaticMonitoringBlocked)
                    {
                        SetAutomaticBlocked(automaticMonitoringBlockReason);
                    }
                    else if (!automaticMonitoringManuallyStopped &&
                             !IsContinuousCapturing &&
                             !IsMonitoringStartInProgress)
                    {
                        SetAutomaticWaiting(
                            "更新処理が完了しました。DDR GRAND PRIX windowを待っています。");
                    }
                }

                if (automaticMonitoringManuallyStopped)
                {
                    consecutiveDetections = 0;
                    consecutiveMisses = 0;
                    if (!IsContinuousCapturing && !IsMonitoringStartInProgress)
                    {
                        SetMonitoringState(
                            MonitoringState.ManuallyStopped,
                            "手動停止済みです。このアプリセッション中は自動再開しません。明示的に監視開始できます。");
                    }
                    await DelayAutomaticMonitoringAsync(cancellationToken);
                    continue;
                }

                if (automaticMonitoringBlocked)
                {
                    consecutiveDetections = 0;
                    consecutiveMisses = 0;
                    if (!IsContinuousCapturing && !IsMonitoringStartInProgress)
                    {
                        SetAutomaticBlocked(automaticMonitoringBlockReason);
                    }
                    await DelayAutomaticMonitoringAsync(cancellationToken);
                    continue;
                }

                if (!IsContinuousCapturing && IsMonitoringStartInProgress)
                {
                    consecutiveDetections = 0;
                    consecutiveMisses = 0;
                    await DelayAutomaticMonitoringAsync(cancellationToken);
                    continue;
                }

                IReadOnlyList<DdrGpWindowCandidate> windows;
                string? detectionFailureReason = null;
                try
                {
                    windows = await ddrGpWindowEnumerator.EnumerateAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    windows = [];
                    detectionFailureReason =
                        $"DDR GRAND PRIX windowの探索に一時的に失敗しました。次回の探索で再確認します。{exception.Message}";
                }

                if (detectionFailureReason is not null)
                {
                    consecutiveDetections = 0;
                    if (IsContinuousCapturing)
                    {
                        consecutiveMisses++;
                    }
                    else
                    {
                        consecutiveMisses = 0;
                    }

                    if (!IsContinuousCapturing)
                    {
                        SetAutomaticWaiting(detectionFailureReason);
                    }
                    await DelayAutomaticMonitoringAsync(cancellationToken);
                    if (consecutiveMisses < automaticMonitoringOptions.RequiredConsecutiveMisses ||
                        !IsContinuousCapturing ||
                        IsStoppingCapture)
                    {
                        continue;
                    }
                }

                var candidates = windows
                    .Where(DdrGpWindowEnumerator.IsDdrGpTarget)
                    .ToList();
                if (IsContinuousCapturing)
                {
                    consecutiveDetections = 0;
                    if (automaticMonitoringManuallyStopped ||
                        activeMonitoringTargetHandle is not { } activeTargetHandle)
                    {
                        consecutiveMisses = 0;
                    }
                    else if (candidates.Any(candidate => candidate.Handle == activeTargetHandle))
                    {
                        consecutiveMisses = 0;
                    }
                    else
                    {
                        consecutiveMisses++;
                        if (consecutiveMisses >= automaticMonitoringOptions.RequiredConsecutiveMisses &&
                            !IsStoppingCapture)
                        {
                            consecutiveMisses = 0;
                            automaticMonitoringWindowLossStopInProgress = true;
                            try
                            {
                                await StopContinuousCaptureAsync(manualStop: false);
                                if (!applicationExitRequested &&
                                    !automaticMonitoringManuallyStopped &&
                                    !automaticMonitoringBlocked)
                                {
                                    SetAutomaticWaiting(
                                        "対象windowが消失したため安全に停止しました。再出現を待っています。");
                                }
                            }
                            catch (Exception exception)
                            {
                                SetAutomaticBlocked(
                                    $"対象window消失後の自動停止に失敗したため、自動監視を停止しました。{exception.Message}");
                            }
                        }
                    }

                    await DelayAutomaticMonitoringAsync(cancellationToken);
                    continue;
                }

                if (IsSaving || IsCapturing || IsStoppingCapture)
                {
                    consecutiveDetections = 0;
                    consecutiveMisses = 0;
                    await DelayAutomaticMonitoringAsync(cancellationToken);
                    continue;
                }

                if (!AreAutomaticMonitoringDatabasesReady(out var databaseReason))
                {
                    SetAutomaticBlocked(databaseReason);
                    await DelayAutomaticMonitoringAsync(cancellationToken);
                    continue;
                }

                if (automaticMonitoringRequiresWindowGap)
                {
                    consecutiveDetections = 0;
                    if (candidates.Count == 0)
                    {
                        automaticMonitoringRequiresWindowGap = false;
                        SetAutomaticWaiting(
                            "対象windowの消失を確認しました。再出現を待っています。");
                    }
                    else
                    {
                        SetAutomaticWaiting(
                            "対象windowの再出現を待っています。現在のwindowを推測して再接続しません。");
                    }
                    await DelayAutomaticMonitoringAsync(cancellationToken);
                    continue;
                }

                if (candidates.Count == 1)
                {
                    consecutiveDetections++;
                    consecutiveMisses = 0;
                    if (consecutiveDetections >=
                        automaticMonitoringOptions.RequiredConsecutiveDetections)
                    {
                        consecutiveDetections = 0;
                        BeginAutomaticMonitoringStart(
                            ownerWindowHandle,
                            candidates[0],
                            cancellationToken);
                    }
                }
                else
                {
                    consecutiveDetections = 0;
                    consecutiveMisses = 0;
                    SetAutomaticWaiting(
                        candidates.Count == 0
                            ? "DDR GRAND PRIX windowを待っています。"
                            : $"条件に一致するDDR GRAND PRIX windowが{candidates.Count}件あるため、推測で選択せず待機しています。");
                }

                await DelayAutomaticMonitoringAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetAutomaticBlocked(
                $"自動監視workerで予期しない失敗が発生したため、自動開始を停止しました。{exception.Message}");
        }
        finally
        {
            var cancellation = automaticMonitoringCancellation;
            automaticMonitoringCancellation = null;
            cancellation?.Dispose();
        }
    }

    private void BeginAutomaticMonitoringStart(
        nint ownerWindowHandle,
        DdrGpWindowCandidate detectedTarget,
        CancellationToken automaticMonitoringCancellationToken)
    {
        if (applicationExitRequested ||
            IsUpdateProcessing ||
            automaticMonitoringManuallyStopped ||
            automaticMonitoringBlocked ||
            IsSaving || IsCapturing || IsContinuousCapturing || IsStoppingCapture ||
            IsMonitoringStartInProgress)
        {
            return;
        }

        SetMonitoringState(
            MonitoringState.Starting,
            "安定して検出したDDR GRAND PRIX windowへ監視を接続しています。");
        HasCaptureStatus = true;
        CaptureStatusTitle = "自動監視を開始しています";
        CaptureStatusMessage =
            $"{detectedTarget.DisplayName}を{automaticMonitoringOptions.RequiredConsecutiveDetections}回連続で確認しました。";
        _ = RunAutomaticMonitoringStartAsync(
            ownerWindowHandle,
            detectedTarget,
            automaticMonitoringCancellationToken);
    }

    private async Task RunAutomaticMonitoringStartAsync(
        nint ownerWindowHandle,
        DdrGpWindowCandidate detectedTarget,
        CancellationToken automaticMonitoringCancellationToken)
    {
        try
        {
            await StartContinuousCaptureAndSaveCoreAsync(
                ownerWindowHandle,
                defaultDatabasePaths.ScoreDatabasePath,
                defaultDatabasePaths.MasterDatabasePath,
                defaultDatabasePaths.JacketCatalogDatabasePath,
                automaticMonitoringCancellationToken,
                automaticWindowDetection: true,
                automaticStart: true,
                detectedTarget: detectedTarget);
        }
        catch (Exception exception)
        {
            if (!applicationExitRequested)
            {
                SetAutomaticBlocked(
                    $"自動監視の開始に失敗したため、自動再試行を停止しました。{exception.Message}");
            }
        }
        finally
        {
            if (!applicationExitRequested)
            {
                if (automaticMonitoringManuallyStopped)
                {
                    SetMonitoringState(
                        MonitoringState.ManuallyStopped,
                        "手動停止済みです。このアプリセッション中は自動再開しません。明示的に監視開始できます。");
                }
                else if (CurrentMonitoringState is MonitoringState.CaptureFailed or
                         MonitoringState.DeviceLost or MonitoringState.WorkflowFailed or
                         MonitoringState.Blocked)
                {
                    SetAutomaticBlocked(MonitoringReason);
                }
                else if (CurrentMonitoringState is MonitoringState.TargetClosed or
                         MonitoringState.Resized or MonitoringState.Stopped)
                {
                    if (!automaticMonitoringWindowLossStopInProgress)
                    {
                        automaticMonitoringRequiresWindowGap = true;
                        SetAutomaticWaiting(
                            "監視sessionが終了しました。対象windowの消失と再出現を確認してから復帰します。");
                    }
                }
            }
            automaticMonitoringWindowLossStopInProgress = false;
        }
    }

    private async Task DelayAutomaticMonitoringAsync(CancellationToken cancellationToken) =>
        await Task.Delay(automaticMonitoringOptions.PollInterval, cancellationToken);

    private bool AreAutomaticMonitoringDatabasesReady(out string reason)
    {
        var masterInspection = repository.InspectMasterDatabase(
            defaultDatabasePaths.MasterDatabasePath);
        var catalogInspection = repository.InspectJacketCatalogDatabase(
            defaultDatabasePaths.JacketCatalogDatabasePath);
        ApplyMasterDatabaseInspection(masterInspection);
        ApplyJacketCatalogInspection(catalogInspection);
        if (masterInspection.IsCompatible && catalogInspection.IsCompatible)
        {
            reason = string.Empty;
            return true;
        }

        reason = BuildMasterDatabaseBlockMessage(
            masterInspection,
            catalogInspection,
            "自動監視を開始しません。DBを修復または再配置してからアプリを再起動してください。");
        return false;
    }

    private void SetAutomaticWaiting(string reason)
    {
        if (applicationExitRequested ||
            automaticMonitoringManuallyStopped ||
            automaticMonitoringBlocked ||
            IsUpdateProcessing ||
            IsContinuousCapturing ||
            IsMonitoringStartInProgress)
        {
            return;
        }

        HasCaptureStatus = true;
        CaptureStatusTitle = "自動監視は待機中です";
        CaptureStatusMessage = reason;
        SetMonitoringState(MonitoringState.WaitingForGame, reason);
    }

    private void SetAutomaticBlocked(string reason, bool temporary = false)
    {
        if (applicationExitRequested || IsContinuousCapturing)
        {
            return;
        }

        if (!temporary)
        {
            automaticMonitoringBlocked = true;
            automaticMonitoringBlockReason = string.IsNullOrWhiteSpace(reason) ? "—" : reason;
        }
        HasCaptureStatus = true;
        CaptureStatusTitle = "自動監視を開始できません";
        CaptureStatusMessage = reason;
        SetMonitoringState(MonitoringState.Blocked, reason);
    }

    private bool TryReserveDeveloperOperation()
    {
        if (!CanRunDeveloperOperations ||
            Interlocked.CompareExchange(ref monitoringOperationReserved, 1, 0) != 0)
        {
            return false;
        }

        OnPropertyChanged(nameof(CanRunDeveloperOperations));
        return true;
    }

    private bool TryReserveApplicationUpdateOperation()
    {
        if (!CanCheckForApplicationUpdate ||
            Interlocked.CompareExchange(ref applicationUpdateOperationReserved, 1, 0) != 0)
        {
            return false;
        }

        OnPropertyChanged(nameof(IsApplicationUpdateBusy));
        OnPropertyChanged(nameof(IsUpdateProcessing));
        OnPropertyChanged(nameof(CanCheckForApplicationUpdate));
        OnPropertyChanged(nameof(CanDownloadAndApplyApplicationUpdate));
        OnPropertyChanged(nameof(CanStartMonitoring));
        return true;
    }

    private void ReleaseApplicationUpdateOperation()
    {
        Interlocked.Exchange(ref applicationUpdateOperationReserved, 0);
        OnPropertyChanged(nameof(IsApplicationUpdateBusy));
        OnPropertyChanged(nameof(IsUpdateProcessing));
        OnPropertyChanged(nameof(CanCheckForApplicationUpdate));
        OnPropertyChanged(nameof(CanDownloadAndApplyApplicationUpdate));
        OnPropertyChanged(nameof(CanStartMonitoring));
    }

    private void ApplyApplicationUpdateResult(ApplicationUpdateResult result)
    {
        applicationUpdateAvailable = result.Status is
            ApplicationUpdateStatus.Available or
            ApplicationUpdateStatus.Downloaded or
            ApplicationUpdateStatus.ReadyToRestart;
        applicationUpdateDownloaded = result.Status is
            ApplicationUpdateStatus.Downloaded or
            ApplicationUpdateStatus.ReadyToRestart;
        ApplicationUpdateVersion = result.Version ?? "";
        SetApplicationUpdateStatus(
            result.Status switch
            {
                ApplicationUpdateStatus.Unsupported => "アプリ更新は利用できません",
                ApplicationUpdateStatus.NoUpdate => "アプリ更新はありません",
                ApplicationUpdateStatus.Available => "アプリ更新があります",
                ApplicationUpdateStatus.Downloaded => "アプリ更新を準備しました",
                ApplicationUpdateStatus.ReadyToRestart => "アプリ更新を適用しました",
                _ => "アプリ更新に失敗しました",
            },
            result.Message,
            result.Status is ApplicationUpdateStatus.Available or ApplicationUpdateStatus.Downloaded
                ? applicationUpdateProgress
                : result.Status == ApplicationUpdateStatus.ReadyToRestart ? 100 : 0);
        OnPropertyChanged(nameof(CanDownloadAndApplyApplicationUpdate));
    }

    private void SetApplicationUpdateStatus(
        string title,
        string message,
        int progress)
    {
        ApplicationUpdateStatusTitle = title;
        ApplicationUpdateStatusMessage = message;
        ApplicationUpdateProgress = Math.Clamp(progress, 0, 100);
        HasApplicationUpdateStatus = true;
    }

    private void SetApplicationUpdateProgress(int progress)
    {
        if (uiSynchronizationContext is null ||
            ReferenceEquals(SynchronizationContext.Current, uiSynchronizationContext))
        {
            ApplicationUpdateProgress = Math.Clamp(progress, 0, 100);
            return;
        }

        uiSynchronizationContext.Post(
            _ => ApplicationUpdateProgress = Math.Clamp(progress, 0, 100),
            null);
    }

    private bool TryReserveMonitoringOperation()
    {
        if (Interlocked.CompareExchange(ref monitoringOperationReserved, 1, 0) != 0)
        {
            return false;
        }

        OnPropertyChanged(nameof(CanRunDeveloperOperations));
        return true;
    }

    private void ReleaseOperationReservation()
    {
        Interlocked.Exchange(ref monitoringOperationReserved, 0);
        OnPropertyChanged(nameof(CanRunDeveloperOperations));
    }

    public void RestoreSavedPaths() =>
        RestoreSavedPathsAsync().GetAwaiter().GetResult();

    public async Task RestoreSavedPathsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            defaultDatabasePaths.EnsureDefaultDirectories();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            HasSaveStatus = true;
            SaveStatusTitle = "既定の保存先を準備できませんでした";
            SaveStatusMessage =
                $"保存先のdirectoryを作成できません。表示された既定pathを確認してください。{exception.Message}";
            return;
        }

        RestoreUserSettings();

        ViewerPathSelection? selection = null;
        if (pathStore is not null)
        {
            try
            {
                selection = pathStore.Load();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                HasSaveStatus = true;
                SaveStatusTitle = "保存済みpathを読み込めませんでした";
                SaveStatusMessage = $"保存済みpathを使用せず、既定pathを使います。{exception.Message}";
            }
        }

        ScoreDatabasePath = SafeFullPath(defaultDatabasePaths.ScoreDatabasePath);
        MasterDatabasePath = SafeFullPath(defaultDatabasePaths.MasterDatabasePath);
        var masterInspection = repository.InspectMasterDatabase(
            defaultDatabasePaths.MasterDatabasePath);
        var catalogInspection = repository.InspectJacketCatalogDatabase(
            defaultDatabasePaths.JacketCatalogDatabasePath);
        ApplyMasterDatabaseInspection(masterInspection);
        ApplyJacketCatalogInspection(catalogInspection);
        if (!masterInspection.IsCompatible || !catalogInspection.IsCompatible)
        {
            ClearLoadedData();
            HasSaveStatus = true;
            SaveStatusTitle = "master DBを使用できません";
            SaveStatusMessage = BuildMasterDatabaseBlockMessage(
                masterInspection,
                catalogInspection,
                "起動時のscore DB初期化、解析、正式保存を開始しません。");
            if (selection is not null && !MatchesDefaultDatabasePaths(selection))
            {
                SaveStatusMessage +=
                    $" 保存済みpathは使用せず、現在の{defaultDatabasePaths.Environment}既定DBだけを使用しました。";
            }
            return;
        }

        ScoreDatabaseInitializationResult initialization;
        try
        {
            initialization = await scoreDatabaseInitializer.InitializeIfMissingAsync(
                defaultDatabasePaths.ScoreDatabasePath,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            initialization = new ScoreDatabaseInitializationResult(
                false,
                false,
                $"score DBの初期化処理に失敗しました。{exception.Message}");
        }
        if (!initialization.Succeeded)
        {
            ClearLoadedData();
            HasSaveStatus = true;
            SaveStatusTitle = "score DBを準備できませんでした";
            SaveStatusMessage =
                $"{initialization.Message} 起動時の解析・正式保存を開始しません。";
            if (selection is not null && !MatchesDefaultDatabasePaths(selection))
            {
                SaveStatusMessage +=
                    $" 保存済みpathは使用せず、現在の{defaultDatabasePaths.Environment}既定DBだけを使用しました。";
            }
            return;
        }

        Load(
            defaultDatabasePaths.ScoreDatabasePath,
            defaultDatabasePaths.MasterDatabasePath,
            defaultDatabasePaths.JacketCatalogDatabasePath,
            persist: true);

        if (selection is not null && !MatchesDefaultDatabasePaths(selection))
        {
            HasSaveStatus = true;
            SaveStatusTitle = "保存済みpathは使用しませんでした";
            SaveStatusMessage =
                $"保存済みpathが現在の{defaultDatabasePaths.Environment}既定pathと一致しないため、現在の環境の既定DBだけを使用しました。";
        }
    }

    public void ApplyReferenceDataSetUpdateResult(ReferenceDataSetUpdateResult result)
    {
        HasSaveStatus = true;
        SaveStatusTitle = result.Status switch
        {
            ReferenceDataSetUpdateStatus.Installed => "reference DBを初回配置しました",
            ReferenceDataSetUpdateStatus.Updated => "reference DBを更新しました",
            ReferenceDataSetUpdateStatus.Unchanged => "reference DBの更新を確認しました",
            ReferenceDataSetUpdateStatus.DowngradeRejected => "reference DB更新を拒否しました",
            ReferenceDataSetUpdateStatus.Failed => "reference DBを更新できませんでした",
            _ => "reference DBの状態を確認しました",
        };
        SaveStatusMessage =
            $"{result.Message} 正式個人スコアDBとsettingsは変更していません。";
    }

    public Task StartConfiguredContinuousCaptureAndSaveAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default) =>
        StartContinuousCaptureAndSaveCoreAsync(
            ownerWindowHandle,
            defaultDatabasePaths.ScoreDatabasePath,
            defaultDatabasePaths.MasterDatabasePath,
            defaultDatabasePaths.JacketCatalogDatabasePath,
            cancellationToken,
            automaticWindowDetection: true);

#if DEBUG
    public async Task CaptureOneFrameAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default)
    {
        if (!TryReserveDeveloperOperation())
        {
            return;
        }

        try
        {
            if (captureService is null)
            {
                HasCaptureStatus = true;
                CaptureStatusTitle = "画面キャプチャを利用できません";
                CaptureStatusMessage = "capture serviceが構成されていません。";
                return;
            }

            IsCapturing = true;
            var captureFinished = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            singleCaptureFinished = captureFinished;
            HasCaptureStatus = true;
            CaptureStatusTitle = "対象windowを選択してください";
            CaptureStatusMessage = "選択したwindowから1フレームだけ取得します。解析やDB保存は実行しません。";
            try
            {
                var result = await captureService.CaptureAsync(ownerWindowHandle, cancellationToken);
                CaptureStatusTitle = result.Status switch
                {
                    CaptureOperationStatus.Saved => "1フレームを保存しました",
                    CaptureOperationStatus.Cancelled => "画面キャプチャをキャンセルしました",
                    CaptureOperationStatus.Unsupported => "画面キャプチャを利用できません",
                    CaptureOperationStatus.AccessDenied => "画面キャプチャが拒否されました",
                    CaptureOperationStatus.TargetClosed => "対象windowが終了しました",
                    CaptureOperationStatus.InvalidSize => "対象windowを取得できません",
                    CaptureOperationStatus.Resized => "対象windowのサイズが変わりました",
                    CaptureOperationStatus.DeviceLost => "GPU deviceが失われました",
                    CaptureOperationStatus.WriteFailed => "キャプチャ出力に失敗しました",
                    _ => "1フレーム取得に失敗しました",
                };
                CaptureStatusMessage = result.UserMessage;
            }
            finally
            {
                IsCapturing = false;
                if (ReferenceEquals(singleCaptureFinished, captureFinished))
                {
                    singleCaptureFinished = null;
                }
                captureFinished.TrySetResult();
            }
        }
        finally
        {
            ReleaseOperationReservation();
        }
    }

    public async Task StartContinuousCaptureAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default)
    {
        if (applicationExitRequested || cancellationToken.IsCancellationRequested ||
            !TryReserveDeveloperOperation())
        {
            return;
        }

        try
        {
            await StartContinuousCaptureCoreAsync(
                ownerWindowHandle,
                null,
                null,
                null,
                cancellationToken,
                automaticWindowDetection: false,
                automaticStart: false,
                suppliedDetectedTarget: null);
        }
        finally
        {
            ReleaseOperationReservation();
        }
    }
#endif

    public async Task StartContinuousCaptureAndSaveAsync(
        nint ownerWindowHandle,
        string scoreDatabasePath,
        string masterDatabasePath,
        CancellationToken cancellationToken = default)
    {
        await StartContinuousCaptureAndSaveCoreAsync(
            ownerWindowHandle,
            scoreDatabasePath,
            masterDatabasePath,
            catalogDatabasePath: null,
            cancellationToken: cancellationToken,
            automaticWindowDetection: false);
    }

    public async Task StartContinuousCaptureAndSaveAsync(
        nint ownerWindowHandle,
        string scoreDatabasePath,
        string masterDatabasePath,
        string catalogDatabasePath,
        CancellationToken cancellationToken = default)
    {
        await StartContinuousCaptureAndSaveCoreAsync(
            ownerWindowHandle,
            scoreDatabasePath,
            masterDatabasePath,
            catalogDatabasePath,
            cancellationToken,
            automaticWindowDetection: false);
    }

    private async Task StartContinuousCaptureAndSaveCoreAsync(
        nint ownerWindowHandle,
        string scoreDatabasePath,
        string masterDatabasePath,
        string? catalogDatabasePath,
        CancellationToken cancellationToken,
        bool automaticWindowDetection,
        bool automaticStart = false,
        DdrGpWindowCandidate? detectedTarget = null)
    {
        if (applicationExitRequested || IsSaving || IsUpdateProcessing ||
            cancellationToken.IsCancellationRequested)
        {
            return;
        }
        if (!TryReserveMonitoringOperation())
        {
            if (automaticStart)
            {
                SetAutomaticWaiting(
                    "別のcaptureまたは保存処理が実行中のため、自動監視を開始せず待機しています。");
            }
            return;
        }
        if (!automaticStart)
        {
            automaticMonitoringBlocked = false;
            automaticMonitoringBlockReason = "—";
            automaticMonitoringRequiresWindowGap = false;
        }
        IsSaving = true;
        OnPropertyChanged(nameof(CanStartMonitoring));

        using var startCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var startFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        monitoringStartCancellation = startCancellation;
        monitoringStartFinished = startFinished;
        OnPropertyChanged(nameof(CanStartMonitoring));
        OnPropertyChanged(nameof(CanStopMonitoring));
        try
        {
            await StartContinuousCaptureCoreAsync(
                ownerWindowHandle,
                scoreDatabasePath,
                masterDatabasePath,
                catalogDatabasePath,
                startCancellation.Token,
                automaticWindowDetection,
                automaticStart,
                detectedTarget);
        }
        finally
        {
            if (ReferenceEquals(monitoringStartCancellation, startCancellation))
            {
                monitoringStartCancellation = null;
            }
            if (ReferenceEquals(monitoringStartFinished, startFinished))
            {
                monitoringStartFinished = null;
            }
            startFinished.TrySetResult();
            OnPropertyChanged(nameof(CanStartMonitoring));
            OnPropertyChanged(nameof(CanStopMonitoring));
            IsSaving = false;
            ReleaseOperationReservation();
        }
    }

    private async Task StartContinuousCaptureCoreAsync(
        nint ownerWindowHandle,
        string? scoreDatabasePath,
        string? masterDatabasePath,
        string? catalogDatabasePath,
        CancellationToken cancellationToken,
        bool automaticWindowDetection,
        bool automaticStart,
        DdrGpWindowCandidate? suppliedDetectedTarget)
    {
        if (applicationExitRequested || cancellationToken.IsCancellationRequested)
        {
            return;
        }
        if (IsUpdateProcessing)
        {
            if (automaticStart)
            {
                SetAutomaticBlocked(
                    "更新処理中のため、自動監視を開始しません。更新完了後に対象windowを再検出します。",
                    temporary: true);
            }
            return;
        }
        if (IsSaving && scoreDatabasePath is null)
        {
            HasCaptureStatus = true;
            CaptureStatusTitle = "保存処理中です";
            CaptureStatusMessage = "保存完了後に監視を開始してください。";
            return;
        }
        if (IsStoppingCapture)
        {
            HasCaptureStatus = true;
            CaptureStatusTitle = "連続キャプチャを停止しています";
            CaptureStatusMessage = "停止完了後にもう一度開始してください。";
            if (automaticStart)
            {
                SetAutomaticWaiting(CaptureStatusMessage);
            }
            return;
        }
        if (IsContinuousCapturing)
        {
            HasCaptureStatus = true;
            CaptureStatusTitle = "連続キャプチャは開始済みです";
            CaptureStatusMessage = "現在のsessionを停止してから再選択してください。";
            return;
        }
        if (IsCapturing)
        {
            HasCaptureStatus = true;
            CaptureStatusTitle = "1フレーム取得中です";
            CaptureStatusMessage = "取得完了後に連続キャプチャを開始してください。";
            return;
        }
        if (continuousCaptureService is null)
        {
            HasCaptureStatus = true;
            CaptureStatusTitle = "連続キャプチャを利用できません";
            CaptureStatusMessage = "continuous capture serviceが構成されていません。";
            if (automaticStart)
            {
                SetAutomaticBlocked(CaptureStatusMessage);
            }
            return;
        }

        ITargetedMonitoringContinuousCaptureService? targetedMonitoringService = null;
        var liveTargetedMonitoringService = liveMonitoringService;
        var useLiveMonitoring = automaticWindowDetection && liveTargetedMonitoringService is not null;
        DdrGpWindowCandidate? detectedTarget = suppliedDetectedTarget;
        if (automaticWindowDetection)
        {
            targetedMonitoringService = continuousCaptureService as
                ITargetedMonitoringContinuousCaptureService;
            if (!useLiveMonitoring && targetedMonitoringService is null)
            {
                HasCaptureStatus = true;
                CaptureStatusTitle = "監視を開始できません";
                CaptureStatusMessage =
                    "自動特定した対象windowへ接続できるcapture serviceが構成されていません。";
                SetMonitoringState(MonitoringState.Stopped, CaptureStatusMessage);
                return;
            }

            ResetMonitoringSession();
            SetMonitoringState(
                automaticStart
                    ? MonitoringState.Starting
                    : MonitoringState.SelectingTarget,
                automaticStart
                    ? "安定して検出したDDR GRAND PRIX windowへ監視を接続しています。"
                    : "DDR GRAND PRIX windowを自動検出しています。");
            HasCaptureStatus = true;
            CaptureStatusTitle = automaticStart
                ? "検出した対象windowへ接続しています"
                : "DDR GRAND PRIX windowを自動検出しています";
            CaptureStatusMessage = automaticStart
                ? "検出したwindowを監視へ接続します。"
                : "process=ddr-konaste、client=1280 x 720のtop-level windowを確認しています。";

            if (detectedTarget is null)
            {
                IReadOnlyList<DdrGpWindowCandidate> windows;
                try
                {
                    windows = await ddrGpWindowEnumerator.EnumerateAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    var reason = $"DDR GRAND PRIX windowを確認できませんでした。{exception.Message}";
                    HasCaptureStatus = true;
                    CaptureStatusTitle = "監視を開始できません";
                    CaptureStatusMessage = reason;
                    SetMonitoringState(
                        automaticStart ? MonitoringState.Blocked : MonitoringState.CaptureFailed,
                        reason);
                    if (automaticStart)
                    {
                        automaticMonitoringBlocked = true;
                        automaticMonitoringBlockReason = reason;
                    }
                    return;
                }

                var candidates = windows
                    .Where(DdrGpWindowEnumerator.IsDdrGpTarget)
                    .ToList();
                if (candidates.Count == 0)
                {
                    var reason =
                        "DDR GRAND PRIXの対象windowが見つかりません。ゲームの起動、process=ddr-konaste、client=1280 x 720、アクセス権を確認してから再度実行してください。";
                    HasCaptureStatus = true;
                    CaptureStatusTitle = "監視を開始できません";
                    CaptureStatusMessage = reason;
                    SetMonitoringState(
                        automaticStart ? MonitoringState.WaitingForGame : MonitoringState.Stopped,
                        reason);
                    return;
                }
                if (candidates.Count > 1)
                {
                    var reason =
                        $"条件に一致するDDR GRAND PRIX windowが{candidates.Count}件あるため、推測で選択せず監視を開始しません。不要なwindowを閉じてから再度実行してください。";
                    HasCaptureStatus = true;
                    CaptureStatusTitle = "監視を開始できません";
                    CaptureStatusMessage = reason;
                    SetMonitoringState(
                        automaticStart ? MonitoringState.WaitingForGame : MonitoringState.Stopped,
                        reason);
                    return;
                }

                detectedTarget = candidates[0];
            }

            MonitoringTarget = detectedTarget.DisplayName;
            MonitoringTargetSize =
                $"{detectedTarget.ClientWidth} x {detectedTarget.ClientHeight}";
        }

        if (scoreDatabasePath is not null &&
            (masterDatabasePath is null ||
             !ValidateMasterDatabasesForSave(masterDatabasePath, catalogDatabasePath)))
        {
            return;
        }

        if (!automaticWindowDetection)
        {
            ResetMonitoringSession();
            SetMonitoringState(MonitoringState.SelectingTarget, "対象windowの選択を待っています。");
        }
        else if (automaticStart)
        {
            SetMonitoringState(
                MonitoringState.Starting,
                "検出したDDR GRAND PRIX windowへ監視を接続しています。");
        }
        IsContinuousCapturing = true;
        continuousCaptureFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionId = Interlocked.Increment(ref monitoringSessionSequence);
        Volatile.Write(ref activeMonitoringSession, sessionId);
        var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        monitoringCancellation = sessionCancellation;
        activeMonitoringTargetHandle = detectedTarget?.Handle;
        activeLiveMonitoringService = useLiveMonitoring
            ? liveTargetedMonitoringService
            : null;
        HasCaptureStatus = true;
        CaptureStatusTitle = automaticWindowDetection
            ? "検出した対象windowへ接続しています"
            : "対象windowを選択してください";
        CaptureStatusMessage =
            useLiveMonitoring
                ? "1秒ごとにRESULTSとSCOREを確認し、SCOREが2回安定した候補だけを解析・正式保存します。画像は保管しません。"
                : automaticWindowDetection
                    ? "検出したwindowを明示停止まで取得し、完成manifestだけを解析・正式保存境界へ渡します。"
                : scoreDatabasePath is null
                    ? "選択したwindowを明示停止まで取得します。解析やDB保存は実行しません。"
                    : "選択したwindowを明示停止まで取得し、完成manifestだけを解析・正式保存境界へ渡します。";
        try
        {
            var progress = new CallbackProgress<CaptureSessionProgress>(
                value => ApplyMonitoringProgress(sessionId, value),
                uiSynchronizationContext);
            var result = useLiveMonitoring
                ? await liveTargetedMonitoringService!.RunAsync(
                    detectedTarget!.Handle,
                    detectedTarget.TargetInfo,
                    progress,
                    (frame, observation, token) => ProcessLiveCandidateAsync(
                        sessionId,
                        scoreDatabasePath!,
                        masterDatabasePath!,
                        catalogDatabasePath,
                        frame,
                        observation,
                        token),
                    sessionCancellation.Token)
                : automaticWindowDetection
                ? await targetedMonitoringService!.RunAsync(
                    detectedTarget!.Handle,
                    detectedTarget.TargetInfo,
                    progress,
                    sessionCancellation.Token)
                : continuousCaptureService is IMonitoringContinuousCaptureService monitoringService
                    ? await monitoringService.RunAsync(
                        ownerWindowHandle,
                        progress,
                        sessionCancellation.Token)
                    : await continuousCaptureService.RunAsync(
                        ownerWindowHandle,
                        sessionCancellation.Token);
            CaptureStatusTitle = result.Status switch
            {
                CaptureOperationStatus.Cancelled when useLiveMonitoring => "監視を停止しました",
                CaptureOperationStatus.Saved => "連続キャプチャを保存しました",
                CaptureOperationStatus.Cancelled => "連続キャプチャをキャンセルしました",
                CaptureOperationStatus.Unsupported => "画面キャプチャを利用できません",
                CaptureOperationStatus.AccessDenied => "画面キャプチャが拒否されました",
                CaptureOperationStatus.TargetClosed => "対象windowが終了しました",
                CaptureOperationStatus.InvalidSize => "対象windowを取得できません",
                CaptureOperationStatus.Resized => "対象windowのサイズが変わりました",
                CaptureOperationStatus.DeviceLost => "GPU deviceが失われました",
                CaptureOperationStatus.WriteFailed => "session outputに失敗しました",
                CaptureOperationStatus.AlreadyRunning => "連続キャプチャは開始済みです",
                _ => "連続キャプチャに失敗しました",
            };
            CaptureStatusMessage = result.UserMessage;
            if (!useLiveMonitoring &&
                result.Status == CaptureOperationStatus.Saved
                && result.Output is not null
                && scoreDatabasePath is not null
                && masterDatabasePath is not null)
            {
                if (!sessionCancellation.IsCancellationRequested &&
                    !applicationExitRequested)
                {
                    await RunCaptureSaveWorkflowAsync(
                        result.Output.ManifestPath,
                        scoreDatabasePath,
                        masterDatabasePath,
                        catalogDatabasePath,
                        sessionId,
                        sessionCancellation.Token);
                }
                else
                {
                    ApplyCaptureCompletion(sessionId, result);
                }
            }
            else
            {
                ApplyCaptureCompletion(sessionId, result);
            }
        }
        catch (OperationCanceledException) when (
            sessionCancellation.IsCancellationRequested || applicationExitRequested)
        {
            if (!applicationExitRequested)
            {
                ApplyCaptureCompletion(
                    sessionId,
                    new CaptureSessionOperationResult(
                        CaptureOperationStatus.Cancelled,
                        "監視を停止しました。新しい解析・保存は開始していません。"));
            }
        }
        catch (Exception exception)
        {
            if (!applicationExitRequested)
            {
                CaptureStatusTitle = "連続キャプチャに失敗しました";
                CaptureStatusMessage = exception.Message;
                SetMonitoringState(MonitoringState.CaptureFailed, exception.Message);
            }
        }
        finally
        {
            IsContinuousCapturing = false;
            IsStoppingCapture = false;
            if (Volatile.Read(ref activeMonitoringSession) == sessionId)
            {
                Volatile.Write(ref activeMonitoringSession, 0);
            }
            if (ReferenceEquals(monitoringCancellation, sessionCancellation))
            {
                monitoringCancellation = null;
            }
            if (ReferenceEquals(activeLiveMonitoringService, liveTargetedMonitoringService))
            {
                activeLiveMonitoringService = null;
            }
            activeMonitoringTargetHandle = null;
            sessionCancellation.Dispose();
            continuousCaptureFinished?.TrySetResult();
            continuousCaptureFinished = null;
        }
    }

    private async Task RunCaptureSaveWorkflowAsync(
        string manifestPath,
        string scoreDatabasePath,
        string masterDatabasePath,
        string? catalogDatabasePath,
        long sessionId,
        CancellationToken cancellationToken)
    {
        if (!CanRunMonitoringWork(sessionId, cancellationToken))
        {
            return;
        }

        HasSaveStatus = true;
        SaveStatusTitle = "キャプチャを解析しています";
        SaveStatusMessage = "confirmed eventを取得順に1件ずつ正式保存境界で処理しています。";
        try
        {
            if (!ValidateMasterDatabasesForSave(masterDatabasePath, catalogDatabasePath) ||
                !CanRunMonitoringWork(sessionId, cancellationToken))
            {
                return;
            }
            if (captureSaveWorkflowRunner is null)
            {
                SaveStatusTitle = "自動保存workflowを利用できません";
                SaveStatusMessage = "capture save workflow runnerが構成されていません。";
                RecordWorkflowFailure(SaveStatusMessage);
                return;
            }
            var result = await captureSaveWorkflowRunner.RunAsync(
                manifestPath,
                scoreDatabasePath,
                masterDatabasePath,
                cancellationToken);
            if (!CanRunMonitoringWork(sessionId, cancellationToken))
            {
                return;
            }
            if (result.Status is not ("completed" or "workflow_failed"))
            {
                SaveStatusTitle = "キャプチャ解析に失敗しました";
                SaveStatusMessage = result.Reasons.Count == 0
                    ? "解析結果を取得できませんでした。"
                    : string.Join(" / ", result.Reasons);
                RecordWorkflowResult(result, workflowFailed: true);
                return;
            }

            if (result.SavedPlayIds.Count > 0)
            {
                var data = catalogDatabasePath is null
                    ? repository.Load(scoreDatabasePath, masterDatabasePath)
                    : repository.Load(scoreDatabasePath, masterDatabasePath, catalogDatabasePath);
                if (result.SavedPlayIds.Any(id => data.Plays.All(play => play.PlayId != id)))
                {
                    SaveStatusTitle = "保存結果を確認できませんでした";
                    SaveStatusMessage = "transaction後のread-only再読込で保存済みplayを確認できませんでした。";
                    RecordWorkflowFailure(SaveStatusMessage);
                    return;
                }
                ApplyData(data);
                if (string.IsNullOrWhiteSpace(data.CatalogDatabasePath))
                {
                    PersistPathsIfConfigured(data.ScoreDatabasePath, data.MasterDatabasePath, persist: true);
                }
                else
                {
                    PersistPathsIfConfigured(
                        data.ScoreDatabasePath,
                        data.MasterDatabasePath,
                        data.CatalogDatabasePath,
                        persist: true);
                }
            }

            if (result.Status == "workflow_failed")
            {
                SaveStatusTitle = result.SavedPlayIds.Count > 0
                    ? $"{result.SavedPlayIds.Count}件を保存し、一部の保存処理に失敗しました"
                    : "保存workflowに失敗しました";
                var reasons = result.Reasons.Count == 0
                    ? "失敗理由を取得できませんでした。"
                    : string.Join(" / ", result.Reasons);
                SaveStatusMessage = $"{CaptureSaveStatusMessage(result)} {reasons}";
                RecordWorkflowResult(result, workflowFailed: true);
            }
            else
            {
                SaveStatusTitle = result.SavedPlayIds.Count > 0
                    ? $"{result.SavedPlayIds.Count}件のプレーを保存しました"
                    : "保存できるプレーはありませんでした";
                SaveStatusMessage = CaptureSaveStatusMessage(result);
                RecordWorkflowResult(result, workflowFailed: false);
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || applicationExitRequested)
        {
            if (!applicationExitRequested)
            {
                SaveStatusTitle = "監視を停止しました";
                SaveStatusMessage = "停止後の解析・保存は開始していません。";
                SetMonitoringState(MonitoringState.Stopped, SaveStatusMessage);
            }
        }
        catch (ViewerDatabaseException exception)
        {
            SaveStatusTitle = "保存後の再読込に失敗しました";
            SaveStatusMessage = exception.UserMessage;
            RecordWorkflowFailure(exception.UserMessage);
        }
        catch (Exception exception)
        {
            SaveStatusTitle = "保存workflowに失敗しました";
            SaveStatusMessage = exception.Message;
            RecordWorkflowFailure(exception.Message);
        }
    }

    private Task ProcessLiveCandidateAsync(
        long sessionId,
        string scoreDatabasePath,
        string masterDatabasePath,
        string? catalogDatabasePath,
        CapturedFrame frame,
        LiveResultObservation observation,
        CancellationToken cancellationToken)
    {
        if (uiSynchronizationContext is null ||
            ReferenceEquals(SynchronizationContext.Current, uiSynchronizationContext))
        {
            return ProcessLiveCandidateCoreAsync(
                sessionId,
                scoreDatabasePath,
                masterDatabasePath,
                catalogDatabasePath,
                frame,
                observation,
                cancellationToken);
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        uiSynchronizationContext.Post(
            async _ =>
            {
                try
                {
                    await ProcessLiveCandidateCoreAsync(
                        sessionId,
                        scoreDatabasePath,
                        masterDatabasePath,
                        catalogDatabasePath,
                        frame,
                        observation,
                        cancellationToken);
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            null);
        return completion.Task;
    }

    private async Task ProcessLiveCandidateCoreAsync(
        long sessionId,
        string scoreDatabasePath,
        string masterDatabasePath,
        string? catalogDatabasePath,
        CapturedFrame frame,
        LiveResultObservation observation,
        CancellationToken cancellationToken)
    {
        if (!CanRunMonitoringWork(sessionId, cancellationToken))
        {
            return;
        }

        HasSaveStatus = true;
        SaveStatusTitle = "RESULT候補を解析しています";
        SaveStatusMessage =
            $"SCORE={observation.Score}の候補を既存の正式保存境界で処理しています。";
        try
        {
            if (!ValidateMasterDatabasesForSave(masterDatabasePath, catalogDatabasePath) ||
                !CanRunMonitoringWork(sessionId, cancellationToken))
            {
                return;
            }
            if (captureSaveWorkflowRunner is not ILiveCaptureSaveWorkflowRunner liveRunner)
            {
                RecordLiveWorkflowFailure(
                    sessionId,
                    cancellationToken,
                    "live candidate workflow runnerが構成されていません。");
                return;
            }

            var result = await liveRunner.RunCandidateAsync(
                frame,
                observation,
                scoreDatabasePath,
                masterDatabasePath,
                catalogDatabasePath,
                cancellationToken);
            if (!CanRunMonitoringWork(sessionId, cancellationToken))
            {
                return;
            }
            if (result.SavedPlayIds.Count > 0 &&
                !ReloadSavedPlayData(
                    result,
                    scoreDatabasePath,
                    masterDatabasePath,
                    catalogDatabasePath))
            {
                RecordLiveWorkflowFailure(
                    sessionId,
                    cancellationToken,
                    "transaction後のread-only再読込で保存済みplayを確認できませんでした。");
                return;
            }

            RecordLiveWorkflowResult(result);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || applicationExitRequested)
        {
            // Stop and abnormal capture boundaries do not start a new candidate save.
        }
        catch (ViewerDatabaseException exception)
        {
            RecordLiveWorkflowFailure(sessionId, cancellationToken, exception.UserMessage);
        }
        catch (Exception exception)
        {
            RecordLiveWorkflowFailure(sessionId, cancellationToken, exception.Message);
        }
    }

    private bool ReloadSavedPlayData(
        CaptureSaveWorkflowResult result,
        string scoreDatabasePath,
        string masterDatabasePath,
        string? catalogDatabasePath)
    {
        var data = catalogDatabasePath is null
            ? repository.Load(scoreDatabasePath, masterDatabasePath)
            : repository.Load(scoreDatabasePath, masterDatabasePath, catalogDatabasePath);
        if (result.SavedPlayIds.Any(id => data.Plays.All(play => play.PlayId != id)))
        {
            return false;
        }

        ApplyData(data);
        if (string.IsNullOrWhiteSpace(data.CatalogDatabasePath))
        {
            PersistPathsIfConfigured(data.ScoreDatabasePath, data.MasterDatabasePath, persist: true);
        }
        else
        {
            PersistPathsIfConfigured(
                data.ScoreDatabasePath,
                data.MasterDatabasePath,
                data.CatalogDatabasePath,
                persist: true);
        }
        return true;
    }

    private void RecordLiveWorkflowResult(CaptureSaveWorkflowResult result)
    {
        PublishUnresolvedCaptureNotifications(result);
        var counts = new Dictionary<string, int>(result.StatusCounts);
        if (result.Status == "analysis_failed" && !counts.ContainsKey("analysis_failed"))
        {
            counts["analysis_failed"] = 1;
        }
        var failed = result.Status is not ("completed" or "workflow_failed");
        if (result.Status == "workflow_failed")
        {
            failed = true;
        }
        var incoming = MonitoringResultSummary.FromWorkflow(
            counts,
            failed,
            DateTimeOffset.UtcNow,
            result.Reasons);
        var reasons = new[]
        {
            MonitoringResults.Reason == "—" ? string.Empty : MonitoringResults.Reason,
            incoming.Reason == "—" ? string.Empty : incoming.Reason,
        }.Where(value => !string.IsNullOrWhiteSpace(value));
        var reason = string.Join(" / ", reasons);
        MonitoringResults = new MonitoringResultSummary(
            MonitoringResults.Saved + incoming.Saved,
            MonitoringResults.Duplicate + incoming.Duplicate,
            MonitoringResults.Excluded + incoming.Excluded,
            MonitoringResults.Unresolved + incoming.Unresolved,
            MonitoringResults.AnalysisFailed + incoming.AnalysisFailed,
            MonitoringResults.DbRejected + incoming.DbRejected,
            MonitoringResults.WorkflowFailed + incoming.WorkflowFailed,
            incoming.RecordedAtUtc,
            string.IsNullOrWhiteSpace(reason) ? "—" : reason);
        OnPropertyChanged(nameof(MonitoringResultsDisplay));
        SaveStatusTitle = result.SavedPlayIds.Count > 0
            ? $"{result.SavedPlayIds.Count}件のプレーを保存しました"
            : result.Status == "workflow_failed"
                ? "保存workflowに失敗しました"
                : result.Status == "completed"
                    ? "RESULT候補を処理しました"
                    : "RESULT候補の解析に失敗しました";
        SaveStatusMessage = CaptureSaveStatusMessage(result);
        if (CurrentMonitoringState == MonitoringState.Monitoring)
        {
            SetMonitoringState(MonitoringState.Monitoring, MonitoringResults.Reason);
        }
    }

    private void RecordLiveWorkflowFailure(
        long sessionId,
        CancellationToken cancellationToken,
        string reason)
    {
        if (!CanRunMonitoringWork(sessionId, cancellationToken))
        {
            return;
        }
        RecordLiveWorkflowResult(new CaptureSaveWorkflowResult(
            "process_failed",
            0,
            new Dictionary<string, int>(),
            [],
            [reason],
            null));
    }

    private static string CaptureSaveStatusMessage(CaptureSaveWorkflowResult result)
    {
        var counts = result.StatusCounts.Count == 0
            ? "対象eventなし"
            : string.Join(
                ", ",
                result.StatusCounts.OrderBy(item => item.Key)
                    .Select(item => $"{item.Key}={item.Value}"));
        var reasons = result.Reasons.Count == 0
            ? string.Empty
            : $" 理由: {string.Join(" / ", result.Reasons.Distinct(StringComparer.Ordinal))}";
        return $"event={result.EventCount}: {counts}。saved以外は成功保存として表示していません。{reasons}";
    }

    private void PublishUnresolvedCaptureNotifications(CaptureSaveWorkflowResult result)
    {
        var eventResults = result.EventResults is { Count: > 0 }
            ? result.EventResults
                .Where(item => item.Status is "unresolved" or "ambiguous")
                .ToArray()
            : BuildFallbackUnresolvedEventResults(result);
        foreach (var eventResult in eventResults)
        {
            var eventId = string.IsNullOrWhiteSpace(eventResult.EventId)
                ? $"capture-unresolved:{result.EventCount}:{eventResult.Status}"
                : eventResult.EventId;
            if (!unresolvedCaptureNotificationEventIds.Add(eventId))
            {
                continue;
            }

            var reasons = eventResult.Reasons.Count == 0
                ? "診断理由は監視結果を確認してください。"
                : string.Join(
                    " / ",
                    eventResult.Reasons.Distinct(StringComparer.Ordinal));
            var message =
                $"正式DBには保存されていません。理由: {reasons} 診断参照: {eventId}";
            var notification = new UnresolvedCaptureNotification(
                eventId,
                message,
                eventResult.Reasons);
            UnresolvedCaptureDiagnosticRecorded?.Invoke(notification);
            if (!appliedNotifyUnresolvedResults)
            {
                continue;
            }

            UnresolvedNotificationTitle = "自動保存できないプレーが発生しました";
            UnresolvedNotificationMessage = message;
            HasUnresolvedNotification = true;
            UnresolvedCaptureNotificationRequested?.Invoke(notification);
        }
    }

    private static IReadOnlyList<CaptureSaveEventResult> BuildFallbackUnresolvedEventResults(
        CaptureSaveWorkflowResult result)
    {
        if (!result.StatusCounts.TryGetValue("unresolved", out var count) || count <= 0)
        {
            return [];
        }

        return Enumerable.Range(0, count)
            .Select(index => new CaptureSaveEventResult(
                $"capture-unresolved:{result.EventCount}:{index}",
                "unresolved",
                result.Reasons))
            .ToArray();
    }

    public async Task StopContinuousCaptureAsync(bool manualStop = true)
    {
        if (manualStop && !applicationExitRequested)
        {
            automaticMonitoringManuallyStopped = true;
        }

        var liveStopService = activeLiveMonitoringService;
        var startFinished = monitoringStartFinished;
        if (!IsContinuousCapturing && !IsMonitoringStartInProgress)
        {
            if (manualStop && !applicationExitRequested)
            {
                SetMonitoringState(
                    MonitoringState.ManuallyStopped,
                    "手動停止済みです。このアプリセッション中は自動再開しません。明示的に監視開始できます。");
            }
            return;
        }

        if (!IsContinuousCapturing)
        {
            monitoringStartCancellation?.Cancel();
            if (startFinished is not null)
            {
                await startFinished.Task;
            }
            if (manualStop && !applicationExitRequested)
            {
                SetMonitoringState(
                    MonitoringState.ManuallyStopped,
                    "手動停止済みです。このアプリセッション中は自動再開しません。明示的に監視開始できます。");
            }
            return;
        }

        if (liveStopService is null && continuousCaptureService is null)
        {
            return;
        }

        var captureFinished = continuousCaptureFinished;
        if (!IsStoppingCapture)
        {
            IsStoppingCapture = true;
            SetMonitoringState(MonitoringState.Stopping, "停止とresource解放を待っています。");
            CaptureStatusTitle = "連続キャプチャを停止しています";
            CaptureStatusMessage = liveStopService is not null
                ? "現在のRESULT候補を完了して監視を停止します。新しい候補は開始しません。"
                : "取得済みフレームのmanifestを完成させて安全に公開します。";
            try
            {
                if (liveStopService is not null)
                {
                    await liveStopService.StopAsync();
                }
                else
                {
                    await continuousCaptureService!.StopAsync();
                }
            }
            catch (Exception exception)
            {
                IsStoppingCapture = false;
                CaptureStatusTitle = "監視停止に失敗しました";
                CaptureStatusMessage = exception.Message;
                SetMonitoringState(
                    MonitoringState.CaptureFailed,
                    $"監視停止に失敗しました。再度停止を実行してください。{exception.Message}");
                throw;
            }
        }
        if (captureFinished is not null)
        {
            await captureFinished.Task;
        }
        if (manualStop && !applicationExitRequested)
        {
            SetMonitoringState(
                MonitoringState.ManuallyStopped,
                "手動停止済みです。このアプリセッション中は自動再開しません。明示的に監視開始できます。");
        }
    }

    public void Load(
        string scoreDatabasePath,
        string masterDatabasePath,
        bool persist = true)
    {
        LoadCore(scoreDatabasePath, masterDatabasePath, catalogDatabasePath: null, persist);
    }

    public void Load(
        string scoreDatabasePath,
        string masterDatabasePath,
        string catalogDatabasePath,
        bool persist = true)
    {
        LoadCore(scoreDatabasePath, masterDatabasePath, catalogDatabasePath, persist);
    }

    private void LoadCore(
        string scoreDatabasePath,
        string masterDatabasePath,
        string? catalogDatabasePath,
        bool persist)
    {
        ScoreDatabasePath = SafeFullPath(scoreDatabasePath);
        MasterDatabasePath = SafeFullPath(masterDatabasePath);
        ApplyMasterDatabaseInspection(repository.InspectMasterDatabase(masterDatabasePath));
        if (catalogDatabasePath is null)
        {
            ApplyJacketCatalogInspection(
                JacketCatalogInspection.Missing(
                    string.Empty,
                    "jacket参照catalogはこの旧path入口では検査していません。現在の環境の固定pathを使用してください."));
        }
        else
        {
            CatalogDatabasePath = SafeFullPath(catalogDatabasePath);
            ApplyJacketCatalogInspection(
                repository.InspectJacketCatalogDatabase(catalogDatabasePath));
        }
        try
        {
            var data = catalogDatabasePath is null
                ? repository.Load(scoreDatabasePath, masterDatabasePath)
                : repository.Load(scoreDatabasePath, masterDatabasePath, catalogDatabasePath);
            ApplyData(data);
            if (string.IsNullOrWhiteSpace(data.CatalogDatabasePath))
            {
                PersistPathsIfConfigured(data.ScoreDatabasePath, data.MasterDatabasePath, persist);
            }
            else
            {
                PersistPathsIfConfigured(
                    data.ScoreDatabasePath,
                    data.MasterDatabasePath,
                    data.CatalogDatabasePath,
                    persist);
            }
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
            ClearLoadedData();
            StatusTitle = "データを読み込めませんでした";
            StatusMessage = exception.UserMessage;
        }
    }

#if DEBUG
    public Task SaveAndReloadConfiguredAsync(
        string workflowInputPath,
        CancellationToken cancellationToken = default) =>
        SaveAndReloadCoreAsync(
            workflowInputPath,
            defaultDatabasePaths.ScoreDatabasePath,
            defaultDatabasePaths.MasterDatabasePath,
            defaultDatabasePaths.JacketCatalogDatabasePath,
            cancellationToken);

    public async Task SaveAndReloadAsync(
        string workflowInputPath,
        string scoreDatabasePath,
        string masterDatabasePath,
        CancellationToken cancellationToken = default)
    {
        await SaveAndReloadCoreAsync(
            workflowInputPath,
            scoreDatabasePath,
            masterDatabasePath,
            catalogDatabasePath: null,
            cancellationToken);
    }

    public async Task SaveAndReloadAsync(
        string workflowInputPath,
        string scoreDatabasePath,
        string masterDatabasePath,
        string catalogDatabasePath,
        CancellationToken cancellationToken = default)
    {
        await SaveAndReloadCoreAsync(
            workflowInputPath,
            scoreDatabasePath,
            masterDatabasePath,
            catalogDatabasePath,
            cancellationToken);
    }

    private async Task SaveAndReloadCoreAsync(
        string workflowInputPath,
        string scoreDatabasePath,
        string masterDatabasePath,
        string? catalogDatabasePath,
        CancellationToken cancellationToken)
    {
        if (applicationExitRequested || IsSaving || IsContinuousCapturing ||
            cancellationToken.IsCancellationRequested)
        {
            return;
        }
        if (!TryReserveDeveloperOperation())
        {
            return;
        }
        var saveFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        manualSaveFinished = saveFinished;
        IsSaving = true;
        HasSaveStatus = true;
        SaveStatusTitle = "保存処理を実行しています";
        SaveStatusMessage = "選択したworkflow入力を既存の正式保存境界で1回だけ処理しています。";
        try
        {
            if (workflowRunner is null)
            {
                SaveStatusTitle = "保存workflowを利用できません";
                SaveStatusMessage = "manual save workflowが構成されていません。";
                return;
            }
            if (!ValidateMasterDatabasesForSave(masterDatabasePath, catalogDatabasePath))
            {
                return;
            }
            var result = await workflowRunner.RunAsync(
                workflowInputPath,
                scoreDatabasePath,
                cancellationToken);
            if (result.WorkflowStatus == "saved" && result.Written && result.PlayId is not null)
            {
                var data = catalogDatabasePath is null
                    ? repository.Load(scoreDatabasePath, masterDatabasePath)
                    : repository.Load(scoreDatabasePath, masterDatabasePath, catalogDatabasePath);
                if (!data.Plays.Any(play => play.PlayId == result.PlayId))
                {
                    SaveStatusTitle = "保存結果を確認できませんでした";
                    SaveStatusMessage = "DBへの保存結果をread-only再読込した履歴で確認できませんでした。";
                    return;
                }
                ApplyData(data);
                if (string.IsNullOrWhiteSpace(data.CatalogDatabasePath))
                {
                    PersistPathsIfConfigured(data.ScoreDatabasePath, data.MasterDatabasePath, persist: true);
                }
                else
                {
                    PersistPathsIfConfigured(
                        data.ScoreDatabasePath,
                        data.MasterDatabasePath,
                        data.CatalogDatabasePath,
                        persist: true);
                }
                SaveStatusTitle = "プレーを保存しました";
                SaveStatusMessage = "正式v1 DBをread-onlyで再読込し、履歴と自己ベストへ反映しました。";
                return;
            }
            (SaveStatusTitle, SaveStatusMessage) = Present(result);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || applicationExitRequested)
        {
            if (!applicationExitRequested)
            {
                SaveStatusTitle = "保存処理をキャンセルしました";
                SaveStatusMessage = "DBへ新しいplayは保存していません。";
            }
        }
        catch (ViewerDatabaseException exception)
        {
            SaveStatusTitle = "保存後の再読込に失敗しました";
            SaveStatusMessage = $"保存処理は完了しましたが、{exception.UserMessage}";
        }
        catch (Exception exception)
        {
            SaveStatusTitle = "保存workflowに失敗しました";
            SaveStatusMessage = exception.Message;
        }
        finally
        {
            IsSaving = false;
            if (ReferenceEquals(manualSaveFinished, saveFinished))
            {
                manualSaveFinished = null;
            }
            saveFinished.TrySetResult();
            ReleaseOperationReservation();
        }
    }
#endif

    private void ApplyData(ViewerData data)
    {
        (string SongId, string ChartId)? selectedChartKey = selectedChartBest is null
            ? null
            : (selectedChartBest.SongId, selectedChartBest.ChartId);
        int? preservedDisplayedCount = allChartBests.Count > 0
            ? ChartBestDisplayedCount
            : null;
        Replace(Plays, data.Plays);
        allChartBests = MergeChartBests(data.ChartBests, data.ChartCatalog);
        UpdateBestVersionOptions();
        RefreshChartBests(
            resetDisplayedCount: true,
            selectedChartKey: selectedChartKey,
            preservedDisplayedCount: preservedDisplayedCount);
        ApplyHomeData(data.Plays);
        MasterVersion = data.MasterVersion;
        ScoreDatabasePath = data.ScoreDatabasePath;
        MasterDatabasePath = data.MasterDatabasePath;
        if (!string.IsNullOrWhiteSpace(data.CatalogDatabasePath))
        {
            CatalogDatabasePath = data.CatalogDatabasePath;
        }
        ApplyMasterDatabaseInspection(
            new MasterDatabaseInspection(
                data.MasterDatabasePath,
                MasterDatabaseStatus.Compatible,
                $"master DBを読み込めます（schema compatible、version: {data.MasterVersion}）。",
                data.MasterVersion));
        if (!string.IsNullOrWhiteSpace(data.CatalogDatabasePath))
        {
            ApplyJacketCatalogInspection(
                new JacketCatalogInspection(
                    data.CatalogDatabasePath,
                    MasterDatabaseStatus.Compatible,
                    "jacket参照catalogをread-onlyで検証できます（schema compatible、version: 1）。",
                    "1"));
        }
        SelectedPlay = Plays.FirstOrDefault();
        HasData = Plays.Count > 0;
    }

    private void ClearLoadedData()
    {
        Plays.Clear();
        allChartBests = [];
        UpdateBestVersionOptions();
        RefreshChartBests(resetDisplayedCount: true);
        ClearHomeData();
        SelectedPlay = null;
        MasterVersion = "—";
        HasData = false;
    }

    public void LoadMoreChartBests()
    {
        if (!CanLoadMoreChartBests)
        {
            return;
        }

        ChartBestDisplayedCount = Math.Min(
            ChartBestDisplayedCount + ChartBestPageSize,
            ChartBestTotalCount);
        Replace(ChartBests, FilterChartBests().Take(ChartBestDisplayedCount));
        NotifyChartBestListState();
    }

    public void SelectChartBest(ChartBestItem? chartBest)
    {
        if (chartBest is null)
        {
            return;
        }

        SelectedChartBest = chartBest;
        ChartBestSelectionRequested?.Invoke(chartBest);
    }

    public void SetChartDetailGraphMode(string mode)
    {
        if (mode is not (ChartDetailAllPlaysMode or ChartDetailBestProgressionMode))
        {
            return;
        }

        if (!SetProperty(ref chartDetailGraphMode, mode, nameof(ChartDetailGraphMode)))
        {
            return;
        }

        OnPropertyChanged(nameof(ChartDetailGraphPlays));
        ChartDetailUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void ResetBestFilters()
    {
        suppressBestFilterRefresh = true;
        try
        {
            BestPlayStyleFilter = "SINGLE";
            BestDifficultyFilter = AllBestFilterValue;
            BestLevelFilter = AllBestFilterValue;
            BestSongQuery = "";
            BestVersionFilter = AllBestFilterValue;
            BestPlayStatusFilter = AllBestFilterValue;
            BestRankFilter = AllBestFilterValue;
            BestClearFilter = AllBestFilterValue;
            BestSortFilter = BestSortScoreDescending;
        }
        finally
        {
            suppressBestFilterRefresh = false;
        }

        RefreshChartBests(resetDisplayedCount: true);
    }

    private void OnBestFilterChanged()
    {
        if (!suppressBestFilterRefresh)
        {
            RefreshChartBests(resetDisplayedCount: true);
        }
    }

    private void RefreshChartBests(
        bool resetDisplayedCount,
        (string SongId, string ChartId)? selectedChartKey = null,
        int? preservedDisplayedCount = null)
    {
        var filtered = FilterChartBests().ToArray();
        ChartBestTotalCount = filtered.Length;
        if (resetDisplayedCount)
        {
            ChartBestDisplayedCount = Math.Min(
                preservedDisplayedCount ?? ChartBestPageSize,
                filtered.Length);
            var restoredSelection = selectedChartKey is { } key
                ? allChartBests.FirstOrDefault(item =>
                    item.SongId == key.SongId && item.ChartId == key.ChartId)
                : null;
            var selectionWasUnchanged =
                EqualityComparer<ChartBestItem?>.Default.Equals(
                    selectedChartBest,
                    restoredSelection);
            SelectedChartBest = restoredSelection;
            if (selectionWasUnchanged && restoredSelection is not null)
            {
                RefreshChartDetail();
            }
        }
        else
        {
            ChartBestDisplayedCount = Math.Min(ChartBestDisplayedCount, filtered.Length);
        }

        Replace(ChartBests, filtered.Take(ChartBestDisplayedCount));
        UpdateBestActiveFilterChips();
        NotifyChartBestListState();
        if (resetDisplayedCount)
        {
            ChartBestListReset?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RefreshChartDetail()
    {
        var selected = selectedChartBest;
        var chartPlays = selected is null
            ? Array.Empty<PlayHistoryItem>()
            : Plays
                .Where(play => play.SongId == selected.SongId && play.ChartId == selected.ChartId)
                .ToArray();
        var projected = BuildHomePlayItems(chartPlays);

        Replace(ChartDetailHistory, projected);
        chartDetailLatestPlay = ChartDetailHistory.FirstOrDefault();
        chartDetailScoreBestPlay = projected
            .OrderByDescending(play => play.Play.Score)
            .ThenByDescending(play => play.Play.ExScore)
            .ThenByDescending(play => ParseTimestamp(play.Play.PlayedAt))
            .ThenByDescending(play => play.Play.PlayId, StringComparer.Ordinal)
            .FirstOrDefault();
        chartDetailExScoreBestPlay = projected
            .OrderByDescending(play => play.Play.ExScore)
            .ThenByDescending(play => play.Play.Score)
            .ThenByDescending(play => ParseTimestamp(play.Play.PlayedAt))
            .ThenByDescending(play => play.Play.PlayId, StringComparer.Ordinal)
            .FirstOrDefault();
        chartDetailAllPlayPoints = projected
            .OrderBy(play => ParseTimestamp(play.Play.PlayedAt))
            .ThenBy(play => play.Play.PlayId, StringComparer.Ordinal)
            .ToArray();
        chartDetailBestPlayPoints = chartDetailAllPlayPoints
            .Where(play => play.PreviousScore is null || play.IsScoreBestUpdate)
            .ToArray();
        ChartDetailGraphMode = ChartDetailAllPlaysMode;

        NotifyChartDetailProperties();
        ChartDetailUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyChartDetailProperties()
    {
        foreach (var propertyName in new[]
                 {
                     nameof(ChartDetailGraphMode),
                     nameof(ChartDetailGraphPlays),
                     nameof(ChartDetailAllPlayPoints),
                     nameof(ChartDetailBestPlayPoints),
                     nameof(ChartDetailLatestPlay),
                     nameof(ChartDetailSongTitle),
                     nameof(ChartDetailPlayStyleDisplay),
                     nameof(ChartDetailDifficultyDisplay),
                     nameof(ChartDetailLevelDisplay),
                     nameof(ChartDetailBestScoreDisplay),
                     nameof(ChartDetailBestExScoreDisplay),
                      nameof(ChartDetailRankDisplay),
                      nameof(ChartDetailRankBadgeVisibility),
                      nameof(ChartDetailRankPlaceholderVisibility),
                      nameof(ChartDetailClearDisplay),
                      nameof(ChartDetailClearBadgeVisibility),
                      nameof(ChartDetailClearPlaceholderVisibility),
                      nameof(ChartDetailFlareRankDisplay),
                      nameof(ChartDetailFlareBadgeVisibility),
                      nameof(ChartDetailFlarePlaceholderVisibility),
                     nameof(ChartDetailRankBadgeGroup),
                     nameof(ChartDetailClearBadgeGroup),
                     nameof(ChartDetailFlareBadgeGroup),
                     nameof(ChartDetailScoreBestAtDisplay),
                     nameof(ChartDetailExScoreBestAtDisplay),
                     nameof(ChartDetailPlayCountDisplay),
                     nameof(ChartDetailHistoryCountDisplay),
                     nameof(ChartDetailFullComboCountDisplay),
                     nameof(ChartDetailPlayVisibility),
                     nameof(ChartDetailEmptyVisibility),
                 })
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void NotifyChartBestListState()
    {
        OnPropertyChanged(nameof(CanLoadMoreChartBests));
        OnPropertyChanged(nameof(ChartBestRangeDisplay));
        OnPropertyChanged(nameof(ChartBestLoadMoreHintDisplay));
    }

    private void UpdateBestVersionOptions()
    {
        var options = allChartBests
            .Where(item => item.PlayStyle == BestPlayStyleFilter)
            .Select(item => item.Version)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Select(GetBestVersionLabel)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(GetBestVersionOrder)
            .ThenBy(version => version, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        options.Insert(0, AllBestFilterValue);
        BestVersionOptions = options;
        if (!options.Contains(bestVersionFilter, StringComparer.CurrentCultureIgnoreCase))
        {
            bestVersionFilter = AllBestFilterValue;
            OnPropertyChanged(nameof(BestVersionFilter));
        }
    }

    private IEnumerable<ChartBestItem> FilterChartBests()
    {
        var songQuery = BestSongQuery.Trim();
        var filtered = allChartBests.Where(item =>
            item.PlayStyle == BestPlayStyleFilter &&
            (BestDifficultyFilter == AllBestFilterValue ||
             item.Difficulty == BestDifficultyFilter) &&
            (BestLevelFilter == AllBestFilterValue ||
             item.LevelDisplay == BestLevelFilter) &&
            (songQuery.Length == 0 ||
             item.SongTitle.Contains(songQuery, StringComparison.CurrentCultureIgnoreCase)) &&
            (BestVersionFilter == AllBestFilterValue ||
             string.Equals(
                 GetBestVersionLabel(item.Version),
                 BestVersionFilter,
                 StringComparison.CurrentCultureIgnoreCase)) &&
            MatchesPlayStatus(item, BestPlayStatusFilter) &&
            MatchesRank(item, BestRankFilter) &&
            MatchesClear(item, BestClearFilter));

        return BestSortFilter switch
        {
            BestSortScoreAscending => filtered
                .OrderBy(item => item.BestScore)
                .ThenBy(item => item.SongTitle, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.ChartId, StringComparer.Ordinal),
            BestSortTitleAscending => filtered
                .OrderBy(item => item.SongTitle, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Level ?? int.MaxValue)
                .ThenBy(item => item.ChartId, StringComparer.Ordinal),
            BestSortLevelAscending => filtered
                .OrderBy(item => item.Level ?? int.MaxValue)
                .ThenBy(item => item.SongTitle, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.ChartId, StringComparer.Ordinal),
            BestSortLastPlayedDescending => filtered
                .OrderByDescending(item => ParseTimestamp(item.LastPlayedAt))
                .ThenBy(item => item.SongTitle, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.ChartId, StringComparer.Ordinal),
            BestSortPlayCountDescending => filtered
                .OrderByDescending(item => item.PlayCount)
                .ThenBy(item => item.SongTitle, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.ChartId, StringComparer.Ordinal),
            _ => filtered
                .OrderByDescending(item => item.BestScore)
                .ThenBy(item => item.SongTitle, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.ChartId, StringComparer.Ordinal),
        };
    }

    private static bool MatchesPlayStatus(ChartBestItem item, string filter) => filter switch
    {
        "プレー済み" => item.IsPlayed,
        "未プレー" => !item.IsPlayed,
        _ => true,
    };

    private static bool MatchesRank(ChartBestItem item, string filter) => filter switch
    {
        "AAA以上" => item.Rank == "AAA",
        "AA" => item.Rank is "AA+" or "AA" or "AA-",
        "A以下" => item.Rank is "A+" or "A" or "A-" or
            "B+" or "B" or "B-" or "C+" or "C" or "C-" or "D+" or "D" or "E",
        _ => true,
    };

    private static bool MatchesClear(ChartBestItem item, string filter) => filter switch
    {
        "PFC" => item.ClearDisplay == "PFC",
        "GFC" => item.ClearDisplay == "GFC",
        "FC" => item.ClearDisplay == "FC",
        "CLEAR" => item.ClearDisplay == "CLEAR",
        "未CLEAR" => item.ClearDisplay is "—" or "FAILED",
        _ => true,
    };

    private static string GetBestVersionLabel(string version)
    {
        const string DanceDanceRevolutionPrefix = "DanceDanceRevolution ";
        var value = string.Join(
            " ",
            version.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (IsGrandPrixRelease(value))
        {
            return BestVersionOrder[0];
        }

        var aliases = new List<string> { value };
        if (value.StartsWith("DDR ", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add(value[4..]);
        }
        if (value.StartsWith(DanceDanceRevolutionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add(value[DanceDanceRevolutionPrefix.Length..]);
        }
        if (aliases.Contains("A20 PL US", StringComparer.OrdinalIgnoreCase))
        {
            aliases.Add("A20 PLUS");
        }

        foreach (var label in BestVersionOrder)
        {
            var labelAliases = new[]
            {
                label,
                label.StartsWith("DDR ", StringComparison.OrdinalIgnoreCase)
                    ? label[4..]
                    : label,
            };
            if (aliases.Any(alias => labelAliases.Contains(alias, StringComparer.OrdinalIgnoreCase)))
            {
                return label;
            }
        }

        return value;
    }

    private static bool IsGrandPrixRelease(string value) =>
        value.Contains("GRAND PRIX", StringComparison.OrdinalIgnoreCase) ||
        (value.Length >= 4 && value.StartsWith("2023", StringComparison.Ordinal) &&
         (value.Length == 4 || value[4] is '/' or '-' or '.'));

    private static int GetBestVersionOrder(string version)
    {
        var index = Array.FindIndex(
            BestVersionOrder,
            label => string.Equals(label, version, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? BestVersionOrder.Length : index;
    }

    private void UpdateBestActiveFilterChips()
    {
        var chips = new List<string>();
        if (BestDifficultyFilter != AllBestFilterValue)
        {
            chips.Add($"難易度: {BestDifficultyFilter}");
        }
        if (BestLevelFilter != AllBestFilterValue)
        {
            chips.Add($"レベル: {BestLevelFilter}");
        }
        if (!string.IsNullOrWhiteSpace(BestSongQuery))
        {
            chips.Add($"曲名: {BestSongQuery.Trim()}");
        }
        if (BestVersionFilter != AllBestFilterValue)
        {
            chips.Add($"バージョン: {BestVersionFilter}");
        }
        if (BestPlayStatusFilter != AllBestFilterValue)
        {
            chips.Add($"プレー状況: {BestPlayStatusFilter}");
        }
        if (BestRankFilter != AllBestFilterValue)
        {
            chips.Add($"ランク: {BestRankFilter}");
        }
        if (BestClearFilter != AllBestFilterValue)
        {
            chips.Add($"CLEAR: {BestClearFilter}");
        }

        Replace(BestActiveFilterChips, chips);
        OnPropertyChanged(nameof(BestActiveFilterSummary));
    }

    private static IReadOnlyList<ChartBestItem> MergeChartBests(
        IReadOnlyList<ChartBestItem> playedChartBests,
        IReadOnlyList<ChartBestItem> chartCatalog)
    {
        var merged = new Dictionary<(string SongId, string ChartId), ChartBestItem>();
        foreach (var item in chartCatalog)
        {
            merged[(item.SongId, item.ChartId)] = item;
        }
        foreach (var item in playedChartBests)
        {
            merged[(item.SongId, item.ChartId)] = item;
        }
        return merged.Values.ToArray();
    }

    private void ApplyHomeData(IReadOnlyList<PlayHistoryItem> plays)
    {
        var projectedPlays = BuildHomePlayItems(plays);
        HomeLatestPlay = projectedPlays.FirstOrDefault();
        Replace(HomeRecentPlays, projectedPlays.Skip(1).Take(5));
        Replace(
            HomeBestUpdates,
            projectedPlays
                .Where(play => play.IsScoreBestUpdate || play.IsExScoreBestUpdate)
                .Take(5));

        var today = DateTimeOffset.Now.Date;
        var todayPlays = projectedPlays
            .Where(play => IsLocalDate(play.Play.SavedAt, today))
            .ToArray();
        HomeTodayDateDisplay = today.ToString("yyyy/MM/dd", CultureInfo.CurrentCulture);
        HomeTodayPlayCount = todayPlays.Length;
        HomeTodayScoreUpdateCount = todayPlays.Count(play => play.IsScoreBestUpdate);
        HomeTodayExScoreUpdateCount = todayPlays.Count(play => play.IsExScoreBestUpdate);
        HomeTodayFullComboCount = todayPlays.Count(IsFullCombo);
        OnPropertyChanged(nameof(HasHomeBestUpdates));
        OnPropertyChanged(nameof(HomeBestUpdateSummaryDisplay));
    }

    private void ClearHomeData()
    {
        HomeLatestPlay = null;
        HomeRecentPlays.Clear();
        HomeBestUpdates.Clear();
        HomeTodayDateDisplay = DateTimeOffset.Now.ToString(
            "yyyy/MM/dd",
            CultureInfo.CurrentCulture);
        HomeTodayPlayCount = 0;
        HomeTodayScoreUpdateCount = 0;
        HomeTodayExScoreUpdateCount = 0;
        HomeTodayFullComboCount = 0;
        OnPropertyChanged(nameof(HasHomeBestUpdates));
        OnPropertyChanged(nameof(HomeBestUpdateSummaryDisplay));
    }

    private static IReadOnlyList<HomePlayItem> BuildHomePlayItems(
        IReadOnlyList<PlayHistoryItem> plays)
    {
        var bestByChart = new Dictionary<(string SongId, string ChartId), (int Score, int ExScore)>();
        var projectedByPlayId = new Dictionary<string, HomePlayItem>(StringComparer.Ordinal);
        var chronologicalPlays = plays
            .Select((play, index) => new { play, index })
            .OrderBy(item => ParseTimestamp(item.play.PlayedAt))
            .ThenByDescending(item => item.index)
            .ToArray();

        foreach (var entry in chronologicalPlays)
        {
            var key = (entry.play.SongId, entry.play.ChartId);
            var previous = bestByChart.TryGetValue(key, out var best)
                ? best
                : ((int Score, int ExScore)?)null;
            var projected = new HomePlayItem(
                entry.play,
                previous?.Score,
                previous?.ExScore);
            projectedByPlayId[entry.play.PlayId] = projected;

            bestByChart[key] = (
                previous is null ? entry.play.Score : Math.Max(previous.Value.Score, entry.play.Score),
                previous is null
                    ? entry.play.ExScore
                    : Math.Max(previous.Value.ExScore, entry.play.ExScore));
        }

        return plays
            .Where(play => projectedByPlayId.ContainsKey(play.PlayId))
            .Select(play => projectedByPlayId[play.PlayId])
            .ToArray();
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var timestamp)
            ? timestamp
            : DateTimeOffset.MinValue;

    private static bool IsLocalDate(string value, DateTime date)
    {
        var timestamp = ParseTimestamp(value);
        return timestamp != DateTimeOffset.MinValue && timestamp.ToLocalTime().Date == date;
    }

    private static bool IsFullCombo(HomePlayItem play) => play.ClearDisplay is
        "PFC" or "GFC" or "FC" or "MFC";

    private static string BuildMasterDatabaseBlockMessage(
        MasterDatabaseInspection masterInspection,
        JacketCatalogInspection catalogInspection,
        string action)
    {
        return
            $"master DB: {masterInspection.Message} / " +
            $"jacket参照catalog: {catalogInspection.Message} " +
            $"いずれかのmaster DBがmissing、read不可、またはschema incompatibleのため、{action}";
    }

    private bool ValidateMasterDatabasesForSave(string masterPath, string? catalogPath)
    {
        var masterInspection = repository.InspectMasterDatabase(masterPath);
        ApplyMasterDatabaseInspection(masterInspection);
        var catalogInspection = catalogPath is null
            ? null
            : repository.InspectJacketCatalogDatabase(catalogPath);
        if (catalogInspection is not null)
        {
            ApplyJacketCatalogInspection(catalogInspection);
        }

        if (masterInspection.IsCompatible &&
            (catalogInspection is null || catalogInspection.IsCompatible))
        {
            return true;
        }

        HasSaveStatus = true;
        SaveStatusTitle = "master DBを使用できません";
        var reasons = new List<string>
        {
            $"master DB: {masterInspection.Message}",
        };
        if (catalogInspection is not null)
        {
            reasons.Add($"jacket参照catalog: {catalogInspection.Message}");
        }
        SaveStatusMessage =
            $"{string.Join(" / ", reasons)} いずれかのmaster DBがmissing、read不可、またはschema incompatibleのため、解析・正式保存を開始しません。";
        RecordWorkflowFailure(SaveStatusMessage);
        return false;
    }

    private void ApplyMasterDatabaseInspection(MasterDatabaseInspection inspection)
    {
        masterDatabaseInspection = inspection;
        MasterDatabasePath = inspection.Path is { Length: > 0 } path ? path : "—";
        MasterVersion = inspection.Version ?? "—";
        OnPropertyChanged(nameof(MasterDatabaseStatus));
        OnPropertyChanged(nameof(MasterDatabaseStatusDisplay));
        OnPropertyChanged(nameof(MasterDatabaseReason));
    }

    private void ApplyJacketCatalogInspection(JacketCatalogInspection inspection)
    {
        jacketCatalogInspection = inspection;
        CatalogDatabasePath = inspection.Path is { Length: > 0 } path ? path : "—";
        OnPropertyChanged(nameof(CatalogDatabaseStatus));
        OnPropertyChanged(nameof(CatalogDatabaseStatusDisplay));
        OnPropertyChanged(nameof(CatalogDatabaseReason));
    }

    private void PersistPathsIfConfigured(
        string scorePath,
        string masterPath,
        bool persist)
    {
        if (!persist || pathStore is null)
        {
            return;
        }

        try
        {
            pathStore.Save(new ViewerPathSelection(scorePath, masterPath));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            HasSaveStatus = true;
            SaveStatusTitle = "DBは読み込みましたがpathを保存できませんでした";
            SaveStatusMessage =
                $"次回起動時は現在の環境の既定pathを使います。{exception.Message}";
        }
    }

    private void PersistPathsIfConfigured(
        string scorePath,
        string masterPath,
        string catalogPath,
        bool persist)
    {
        if (!persist || pathStore is null)
        {
            return;
        }

        if (!MatchesDefaultDatabasePaths(
                new ViewerPathSelection(
                    scorePath,
                    masterPath,
                    catalogPath,
                    defaultDatabasePaths.Environment)))
        {
            return;
        }

        try
        {
            pathStore.Save(
                new ViewerPathSelection(
                    scorePath,
                    masterPath,
                    catalogPath,
                    defaultDatabasePaths.Environment));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            HasSaveStatus = true;
            SaveStatusTitle = "DBは読み込みましたがpathを保存できませんでした";
            SaveStatusMessage =
                $"次回起動時は現在の環境の既定pathを使います。{exception.Message}";
        }
    }

    private bool MatchesDefaultDatabasePaths(ViewerPathSelection selection) =>
        selection.Environment == defaultDatabasePaths.Environment &&
        SamePath(selection.ScoreDatabasePath, defaultDatabasePaths.ScoreDatabasePath) &&
        SamePath(selection.MasterDatabasePath, defaultDatabasePaths.MasterDatabasePath) &&
        SamePath(selection.CatalogDatabasePath, defaultDatabasePaths.JacketCatalogDatabasePath);

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? "—" : Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private static (string Title, string Message) Present(PersonalScoreDbWorkflowResult result)
    {
        var reason = result.Reasons.Count == 0 ? "理由はありません。" : string.Join(" / ", result.Reasons);
        return result.WorkflowStatus switch
        {
            "excluded" => ("保存対象外です", $"play履歴には追加していません。{reason}"),
            "duplicate" => ("重複するプレーです", $"play履歴には追加していません。{reason}"),
            "unresolved" => ("正式保存値が未解決です", $"DBやartifactは変更していません。{reason}"),
            "invalid" => ("workflow入力が不正です", $"DBやartifactは変更していません。{reason}"),
            "db_rejected" => ("保存先DBを使用できません", $"DBは変更していません。{reason}"),
            "artifact_failed" or "artifact_conflict" =>
                ("解析artifactを保存できません", $"DBは変更していません。{reason}"),
            "artifact_created_db_failed" =>
                ("DB保存に失敗しました", $"解析artifactは作成済みですが、play保存は成功していません。{reason}"),
            _ => ("保存workflowに失敗しました", reason),
        };
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }

    private void ApplyMonitoringProgress(long sessionId, CaptureSessionProgress progress)
    {
        if (!CanApplyMonitoringCallback(sessionId))
        {
            return;
        }
        MonitoringTarget = progress.Target.DisplayName;
        MonitoringTargetSize = progress.Target.Width > 0 && progress.Target.Height > 0
            ? $"{progress.Target.Width} x {progress.Target.Height}"
            : "—";
        MonitoringFrameCount = progress.FrameCount;
        MonitoringSampledFrameCount = progress.SampledFrameCount;
        MonitoringResultFrameCount = progress.ResultFrameCount;
        MonitoringConfirmedCandidateCount = progress.ConfirmedCandidateCount;
        MonitoringDiscardedFrameCount = progress.DiscardedFrameCount;
        MonitoringPendingCandidateCount = progress.PendingCandidateCount;
        MonitoringCandidateQueueDropCount = progress.CandidateQueueDropCount;
        monitoringStartedAtUtc = progress.StartedAtUtc;
        monitoringLatestEventAtUtc = progress.LatestEventAtUtc;
        OnPropertyChanged(nameof(MonitoringStartedAtDisplay));
        OnPropertyChanged(nameof(MonitoringLatestEventAtDisplay));
        OnPropertyChanged(nameof(MonitoringResultsDisplay));
        SetMonitoringState(
            MonitoringState.Monitoring,
            string.IsNullOrWhiteSpace(progress.StatusMessage)
                ? "frameを取得しています。"
                : progress.StatusMessage);
        if (!string.IsNullOrWhiteSpace(progress.StatusMessage))
        {
            CaptureStatusMessage = progress.StatusMessage;
        }
    }

    private void ApplyCaptureCompletion(long sessionId, CaptureSessionOperationResult result)
    {
        if (Volatile.Read(ref activeMonitoringSession) != sessionId || applicationExitRequested)
        {
            return;
        }
        var state = result.Status switch
        {
            CaptureOperationStatus.Saved or CaptureOperationStatus.Cancelled => MonitoringState.Stopped,
            CaptureOperationStatus.TargetClosed => MonitoringState.TargetClosed,
            CaptureOperationStatus.Resized => MonitoringState.Resized,
            CaptureOperationStatus.DeviceLost => MonitoringState.DeviceLost,
            CaptureOperationStatus.AlreadyRunning => MonitoringState.Monitoring,
            _ => MonitoringState.CaptureFailed,
        };
        SetMonitoringState(state, result.UserMessage);
    }

    private bool CanApplyMonitoringCallback(long sessionId) =>
        Volatile.Read(ref activeMonitoringSession) == sessionId &&
        IsContinuousCapturing &&
        !applicationExitRequested &&
        CurrentMonitoringState is MonitoringState.Starting or
            MonitoringState.SelectingTarget or MonitoringState.Monitoring;

    private bool CanRunMonitoringWork(long sessionId, CancellationToken cancellationToken) =>
        Volatile.Read(ref activeMonitoringSession) == sessionId &&
        IsContinuousCapturing &&
        !applicationExitRequested &&
        !cancellationToken.IsCancellationRequested &&
        CurrentMonitoringState is MonitoringState.Starting or MonitoringState.SelectingTarget or
            MonitoringState.Monitoring or MonitoringState.Stopping;

    private void RecordWorkflowResult(CaptureSaveWorkflowResult result, bool workflowFailed)
    {
        PublishUnresolvedCaptureNotifications(result);
        var counts = new Dictionary<string, int>(result.StatusCounts);
        if (result.Status == "analysis_failed" && !counts.ContainsKey("analysis_failed"))
        {
            counts["analysis_failed"] = 1;
        }
        MonitoringResults = MonitoringResultSummary.FromWorkflow(
            counts,
            workflowFailed,
            DateTimeOffset.UtcNow,
            result.Reasons);
        SetMonitoringState(
            workflowFailed ? MonitoringState.WorkflowFailed : MonitoringState.Stopped,
            MonitoringResults.Reason);
    }

    private void RecordWorkflowFailure(string reason)
    {
        MonitoringResults = MonitoringResultSummary.FromWorkflow(
            new Dictionary<string, int>(),
            workflowFailed: true,
            DateTimeOffset.UtcNow,
            [reason]);
        SetMonitoringState(MonitoringState.WorkflowFailed, reason);
    }

    private void ResetMonitoringSession()
    {
        MonitoringTarget = "未選択";
        MonitoringTargetSize = "—";
        MonitoringFrameCount = 0;
        MonitoringSampledFrameCount = 0;
        MonitoringResultFrameCount = 0;
        MonitoringConfirmedCandidateCount = 0;
        MonitoringDiscardedFrameCount = 0;
        MonitoringPendingCandidateCount = 0;
        MonitoringCandidateQueueDropCount = 0;
        monitoringStartedAtUtc = null;
        monitoringLatestEventAtUtc = null;
        MonitoringResults = MonitoringResultSummary.Empty;
        unresolvedCaptureNotificationEventIds.Clear();
        HasUnresolvedNotification = false;
        UnresolvedNotificationTitle = "";
        UnresolvedNotificationMessage = "";
        OnPropertyChanged(nameof(MonitoringResultsDisplay));
        OnPropertyChanged(nameof(MonitoringStartedAtDisplay));
        OnPropertyChanged(nameof(MonitoringLatestEventAtDisplay));
    }

    private void SetMonitoringState(MonitoringState state, string reason)
    {
        MonitoringReason = string.IsNullOrWhiteSpace(reason) ? "—" : reason;
        CurrentMonitoringState = state;
    }

    private static string FormatMonitoringTime(DateTimeOffset? value) =>
        value is null ? "—" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private sealed class CallbackProgress<T>(
        Action<T> callback,
        SynchronizationContext? synchronizationContext) : IProgress<T>
    {
        public void Report(T value)
        {
            if (synchronizationContext is null ||
                ReferenceEquals(SynchronizationContext.Current, synchronizationContext))
            {
                callback(value);
                return;
            }
            synchronizationContext.Send(_ => callback(value), null);
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
