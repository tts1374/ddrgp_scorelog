using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace JacketCatalogCollector;

public enum CollectorOperationState
{
    Ready,
    LoadingDatabases,
    InitializingCatalog,
    UpdatingMaster,
    UpdatingOfficialSnapshot,
    Collecting,
    FinalizingCollection,
    RetryingCatalog,
    ReloadingProjection,
    NoMaster,
    Failed,
}

public sealed record CoverageStatusOption(string Value, string Display);

public static class CollectionDisplayLabels
{
    public static string Status(string value) => value switch
    {
        "all" => "すべて",
        "referenced" => "収集済み",
        "needs_review" => "レビュー待ち",
        "uncollected" => "未収集",
        "unresolved" => "曲未特定",
        "orphan" or "orphaned" => "曲情報に存在しないデータ",
        _ => $"不明: {value}",
    };

    public static string Reason(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        if (value.StartsWith("observation_", StringComparison.Ordinal)
            || value == "title_match_artist_mismatch")
        {
            return "取得画面またはartifact不一致";
        }
        if (value is "missing_title_or_artist"
            or "identity_not_found"
            or "ambiguous_canonical_title_artist"
            or "ambiguous_alias_title_artist")
        {
            return "曲名を特定できない";
        }
        if (value.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || value.Contains("already", StringComparison.OrdinalIgnoreCase)
            || value.Contains("catalog_existing", StringComparison.OrdinalIgnoreCase))
        {
            return "重複または既登録";
        }
        if (value is "feature_extraction_failed" or "persisted_feature_invalid"
            || value.Contains("image", StringComparison.OrdinalIgnoreCase)
            || value.Contains("artifact", StringComparison.OrdinalIgnoreCase))
        {
            return "画像・artifact欠損";
        }
        if (value.Contains("drift", StringComparison.OrdinalIgnoreCase)
            || value.Contains("stale", StringComparison.OrdinalIgnoreCase)
            || value.Contains("changed", StringComparison.OrdinalIgnoreCase)
            || value.Contains("version", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("master_", StringComparison.Ordinal)
            || value == "song_not_grand_prix_available"
            || value == "master_song_missing")
        {
            return "曲情報、ジャケット情報、checkpointの更新差異";
        }

        return $"不明: {value}";
    }
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IMasterUpdateService masterUpdateService;
    private readonly IProjectionService projectionService;
    private readonly IReviewWorkflowService? reviewWorkflowService;
    private readonly CollectorDatabasePaths fixedDatabasePaths;
    private readonly ICatalogInitializationService catalogInitializer;
    private readonly IManualReviewDraftStore? manualReviewDraftStore;
    private readonly IManualReviewXlsxImportService? manualReviewXlsxImportService;
    private readonly IOfficialJacketSnapshotService officialJacketSnapshotService;
    private ReviewProjection? projection;
    private MasterSummary? masterSummary;
    private readonly Dictionary<string, ManualReviewDraft> manualReviewDrafts =
        new(StringComparer.Ordinal);
    private string selectedCoverageStatus = "all";
    private string selectedReason = "all";
    private string statusTitle = "準備完了";
    private string statusMessage = "固定pathの曲情報DBとジャケット情報DBを確認します。";
    private bool isBusy;
    private CollectorOperationState operationState = CollectorOperationState.Ready;
    private string lastOperationResult = "まだ処理結果がありません。";
    private string officialSnapshotLastResult = "公式ジャケット情報は未確認です。";
    private string collectionEndResult = "—";
    private OfficialJacketSnapshotMetadata? officialSnapshotMetadata;
    private OfficialJacketSnapshotProgress? officialSnapshotProgress;
    private OfficialSnapshotOperationOutcome officialSnapshotOutcome =
        OfficialSnapshotOperationOutcome.NotRun;
    private ReviewReference? selectedReference;
    private ProjectionSong? selectedSong;
    private string songSearch = "";
    private string reviewReason = "";
    private string reviewNote = "";
    private string selectedCandidateClassification = "all";
    private ManualReviewDraftRow? selectedManualReviewRow;
    private ReviewedManualReviewRow? selectedReviewedManualReviewRow;

    public MainViewModel(
        IMasterUpdateService masterUpdateService,
        IProjectionService projectionService,
        IReviewWorkflowService? reviewWorkflowService = null,
        WindowCaptureViewModel? windowCapture = null,
        JacketObservationViewModel? observation = null,
        CollectorDatabasePaths? databasePaths = null,
        ICatalogInitializationService? catalogInitializationService = null,
        IManualReviewDraftStore? manualReviewDraftStore = null,
        IManualReviewXlsxImportService? manualReviewXlsxImportService = null,
        IOfficialJacketSnapshotService? officialJacketSnapshotService = null)
    {
        this.masterUpdateService = masterUpdateService;
        this.projectionService = projectionService;
        this.reviewWorkflowService = reviewWorkflowService;
        this.manualReviewDraftStore = manualReviewDraftStore;
        this.manualReviewXlsxImportService = manualReviewXlsxImportService;
        fixedDatabasePaths = databasePaths ?? CollectorDatabasePaths.Resolve();
        this.officialJacketSnapshotService = officialJacketSnapshotService
            ?? new PythonOfficialJacketSnapshotService(
                new ProcessRunner(),
                fixedDatabasePaths.RepositoryRoot,
                fixedDatabasePaths.DdrWorldSnapshotRootPath);
        catalogInitializer = catalogInitializationService ?? CreateCatalogInitializer(databasePaths);
        WindowCapture = windowCapture;
        Observation = observation;
        if (WindowCapture is not null)
        {
            WindowCapture.PropertyChanged += ChildViewModel_PropertyChanged;
        }
        if (Observation is not null)
        {
            Observation.PropertyChanged += ChildViewModel_PropertyChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProjectionSong> Songs { get; } = [];
    public WindowCaptureViewModel? WindowCapture { get; }
    public JacketObservationViewModel? Observation { get; }
    public ObservableCollection<ReviewReference> ReviewReferences { get; } = [];
    public ObservableCollection<ManualReviewDraftRow> ManualReviewRows { get; } = [];
    public ObservableCollection<ReviewedManualReviewRow> ReviewedManualReviewRows { get; } = [];
    public ObservableCollection<string> CoverageStatusOptions { get; } =
        ["all", "referenced", "needs_review", "uncollected", "unresolved", "orphaned"];
    public ObservableCollection<CoverageStatusOption> CoverageFilterOptions { get; } =
    [
        new("all", "すべて"),
        new("referenced", "収集済み"),
        new("needs_review", "レビュー待ち"),
        new("uncollected", "未収集"),
        new("unresolved", "曲未特定"),
        new("orphaned", "曲情報に存在しないデータ"),
    ];
    public ObservableCollection<string> ReasonOptions { get; } = ["all"];
    public ObservableCollection<ProjectionSong> SongChoices { get; } = [];
    public ObservableCollection<string> CandidateClassificationOptions { get; } = ["all"];
    public string? CurrentMasterPath => projection is null ? null : fixedDatabasePaths.MasterPath;
    public string? CurrentCatalogPath => projection is null ? null : fixedDatabasePaths.CatalogPath;
    public int ManualReviewUnreviewedCount => CountManualReviewRows("unreviewed");
    public int ManualReviewConfirmedCount => CountManualReviewRows("confirmed");
    public int ManualReviewRejectedCount => CountManualReviewRows("rejected");
    public int ManualReviewHoldCount => CountManualReviewRows("hold");

    public CollectorOperationState OperationState
    {
        get => operationState;
        private set
        {
            if (SetField(ref operationState, value))
            {
                NotifyOperationDisplayProperties();
            }
        }
    }

    public string OperationStateDisplay => OperationState switch
    {
        CollectorOperationState.NoMaster => "未作成",
        CollectorOperationState.Collecting => "収集中",
        CollectorOperationState.UpdatingOfficialSnapshot => "公式ジャケット情報を更新中",
        CollectorOperationState.Failed => "更新失敗",
        CollectorOperationState.Ready => projection is null ? "処理中" : "利用可能",
        _ => "処理中",
    };

    public string MasterVersion => projection?.Master.MasterVersion ?? "未選択";
    public string MasterSourceHash => projection?.Master.SourceHash ?? "—";
    public string MasterCounts => projection is null
        ? "—"
        : $"songs: {projection.Master.SongCount} / charts: {projection.Master.ChartCount} / GP: {projection.Master.GrandPrixSongCount}";
    public string CatalogIdentity => projection?.Catalog.CatalogIdentity ?? "未選択";
    public string CatalogSchema => projection is null ? "—" : $"v{projection.Catalog.SchemaVersion}";
    public string MasterUpdatedAtDisplay => FormatLocalTimestamp(masterSummary?.GeneratedAt, "yyyy/MM/dd HH:mm");
    public string MasterUpdatedAtLongDisplay => FormatLocalTimestamp(masterSummary?.GeneratedAt, "yyyy/MM/dd HH:mm:ss");
    public string MasterHeaderDisplay => masterSummary is null
        ? "曲情報: 未作成"
        : $"曲情報: {MasterUpdatedAtDisplay} 更新";
    public string CatalogHeaderDisplay => projection is null
        ? "ジャケット情報: —"
        : $"ジャケット情報: v{projection.Catalog.SchemaVersion}";
    public string OfficialSnapshotHeaderDisplay => officialSnapshotMetadata is null
        ? "公式ジャケット: —"
        : $"公式ジャケット: {FormatLocalTimestamp(officialSnapshotMetadata.CompletedAt, "yyyy/MM/dd")}";
    public string OfficialSnapshotUpdatedAtDisplay =>
        FormatLocalTimestamp(officialSnapshotMetadata?.CompletedAt, "yyyy/MM/dd HH:mm:ss");
    public string OfficialSnapshotSongCountDisplay => officialSnapshotMetadata is null
        ? "—"
        : $"{officialSnapshotMetadata.SongCount:N0} 曲";
    public string OfficialSnapshotStoredImageCountDisplay => officialSnapshotMetadata is null
        ? "—"
        : $"{officialSnapshotMetadata.StoredImageCount:N0} 画像";
    public string OfficialSnapshotPathDisplay => "data/ddrworld_music_snapshot";
    public string OfficialSnapshotUserStatusDisplay => OperationState switch
    {
        CollectorOperationState.UpdatingOfficialSnapshot => "更新中…",
        _ when officialSnapshotOutcome == OfficialSnapshotOperationOutcome.Failed
            => "更新に失敗しました。",
        _ when officialSnapshotMetadata is null => "未作成",
        _ => "利用可能",
    };
    public string OfficialSnapshotProgressTitleDisplay => "公式ジャケット情報を取得中";
    public string OfficialSnapshotProgressPercentDisplay =>
        $"{OfficialSnapshotProgressPercent:0}%";
    public double OfficialSnapshotProgressPercent
    {
        get
        {
            if (officialSnapshotProgress is null || officialSnapshotProgress.Total <= 0)
            {
                return officialSnapshotProgress?.Phase == "jackets" ? 12 : 0;
            }
            return officialSnapshotProgress.Phase == "pages"
                ? officialSnapshotProgress.Completed * 12d / officialSnapshotProgress.Total
                : 12d + officialSnapshotProgress.Completed * 88d / officialSnapshotProgress.Total;
        }
    }
    public string OfficialSnapshotProgressDetailDisplay
    {
        get
        {
            if (officialSnapshotProgress is null)
            {
                return "公式ジャケット情報を取得中…";
            }
            return officialSnapshotProgress.Phase == "pages"
                ? $"曲一覧を取得中… {officialSnapshotProgress.Completed:N0} / "
                    + $"{officialSnapshotProgress.Total:N0}ページ"
                : $"ジャケットを取得中… {officialSnapshotProgress.Completed:N0} / "
                    + $"{officialSnapshotProgress.Total:N0}曲";
        }
    }
    public bool IsOfficialSnapshotUpdating =>
        OperationState == CollectorOperationState.UpdatingOfficialSnapshot;
    public bool CanCancelOfficialSnapshot => IsOfficialSnapshotUpdating && IsBusy;
    public string OfficialSnapshotLastResultDisplay => officialSnapshotLastResult;
    public string MasterUserStatusDisplay => OperationState switch
    {
        CollectorOperationState.LoadingDatabases
            or CollectorOperationState.ReloadingProjection => "読み込み中…",
        CollectorOperationState.UpdatingMaster => "曲情報を更新しています…",
        CollectorOperationState.InitializingCatalog => "曲情報を確認中…",
        CollectorOperationState.UpdatingOfficialSnapshot => "公式ジャケット情報を更新中…",
        CollectorOperationState.Failed when projection is not null => "更新に失敗しました。",
        CollectorOperationState.Failed when masterSummary is not null => "利用可能",
        CollectorOperationState.Failed => "更新に失敗しました。",
        CollectorOperationState.NoMaster => "曲情報がありません。",
        _ => projection is null ? "読み込み中…" : "利用可能",
    };
    public string CatalogUserStatusDisplay => OperationState switch
    {
        CollectorOperationState.LoadingDatabases
            or CollectorOperationState.ReloadingProjection => "読み込み中…",
        CollectorOperationState.InitializingCatalog => "初期情報を作成中…",
        CollectorOperationState.UpdatingOfficialSnapshot => "公式ジャケット情報を更新中…",
        CollectorOperationState.UpdatingMaster when projection is null => "読み込み中…",
        CollectorOperationState.Failed when projection is null => "更新に失敗しました。",
        _ => projection is null ? "読み込み中…" : "利用可能",
    };
    public string MasterSongCountDisplay => projection is null ? "—" : $"{projection.Master.SongCount:N0} 曲";
    public string MasterChartCountDisplay => projection is null ? "—" : $"{projection.Master.ChartCount:N0} 譜面";
    public string CollectedCountDisplay => $"{CountCoverage("referenced"):N0}";
    public string ReviewPendingSongCountDisplay => $"{CountCoverage("needs_review"):N0}";
    public string UncollectedCountDisplay => $"{CountCoverage("uncollected"):N0}";
    public string UnresolvedCountDisplay => $"{CountCoverage("unresolved"):N0}";
    public string OrphanedCountDisplay => $"{projection?.Coverage.OrphanedReferenceCount ?? 0:N0}";
    public string CatalogCoverageDisplay => projection is null
        ? "—"
        : $"{CountCoverage("referenced"):N0} / {projection.Coverage.GrandPrixSongCount:N0} 曲";
    public string ReviewPendingCountDisplay => $"{ManualReviewRows.Count:N0} 件";
    public string CollectionSummaryDisplay => projection is null
        ? "収集状況を読み込み中…"
        : $"収集済み: {CountCoverage("referenced"):N0} / レビュー待ち: {CountCoverage("needs_review"):N0}"
            + $" / 未収集: {CountCoverage("uncollected"):N0} / 曲未特定: {CountCoverage("unresolved"):N0}";
    public string CollectionOrphanSummaryDisplay => projection is null
        ? ""
        : $"曲情報に存在しないデータ: {projection.Coverage.OrphanedReferenceCount:N0}"
            + $" / 未割当: {projection.Coverage.UnassignedUnresolvedObservationCount:N0}";
    public string LastOperationResultDisplay => lastOperationResult;
    public string CollectionEndResultDisplay => collectionEndResult;
    public bool CanUpdateMaster => !IsBusy
        && OperationState is not (CollectorOperationState.LoadingDatabases
            or CollectorOperationState.InitializingCatalog
            or CollectorOperationState.UpdatingMaster
            or CollectorOperationState.UpdatingOfficialSnapshot
            or CollectorOperationState.Collecting
            or CollectorOperationState.FinalizingCollection
            or CollectorOperationState.RetryingCatalog
            or CollectorOperationState.ReloadingProjection)
        && WindowCapture?.IsDetecting != true
        && WindowCapture?.Lifecycle.State is not (CaptureLifecycleState.Starting
            or CaptureLifecycleState.Capturing
            or CaptureLifecycleState.Stopping)
        && Observation?.IsActive != true;
    public bool CanUpdateOfficialSnapshot => !IsBusy
        && OperationState is not (CollectorOperationState.LoadingDatabases
            or CollectorOperationState.InitializingCatalog
            or CollectorOperationState.UpdatingMaster
            or CollectorOperationState.UpdatingOfficialSnapshot
            or CollectorOperationState.Collecting
            or CollectorOperationState.FinalizingCollection
            or CollectorOperationState.RetryingCatalog
            or CollectorOperationState.ReloadingProjection)
        && WindowCapture?.IsDetecting != true
        && WindowCapture?.Lifecycle.State is not (CaptureLifecycleState.Starting
            or CaptureLifecycleState.Capturing
            or CaptureLifecycleState.Stopping)
        && Observation?.IsActive != true;
    public bool CanStartCollection => !IsBusy
        && OperationState is not (CollectorOperationState.LoadingDatabases
            or CollectorOperationState.InitializingCatalog
            or CollectorOperationState.UpdatingMaster
            or CollectorOperationState.UpdatingOfficialSnapshot
            or CollectorOperationState.Collecting
            or CollectorOperationState.FinalizingCollection
            or CollectorOperationState.RetryingCatalog
            or CollectorOperationState.ReloadingProjection)
        && projection is not null
        && WindowCapture?.IsDetecting != true
        && WindowCapture?.Lifecycle.State is not (CaptureLifecycleState.Starting
            or CaptureLifecycleState.Capturing
            or CaptureLifecycleState.Stopping)
        && Observation?.IsActive != true;
    public bool CanStopCollection => WindowCapture?.IsDetecting == true
        || Observation?.IsActive == true
        || OperationState == CollectorOperationState.Collecting
        || WindowCapture?.Lifecycle.State is CaptureLifecycleState.Starting
            or CaptureLifecycleState.Capturing
            or CaptureLifecycleState.Stopping;
    public string CoverageSummary => projection is null
        ? "—"
        : string.Join(
            " / ",
            new[] { "referenced", "needs_review", "uncollected", "unresolved" }.Select(
                status => $"{status}: {projection.Coverage.StatusCounts.GetValueOrDefault(status)}"));
    public string OrphanSummary => projection is null
        ? "—"
        : $"orphan: {projection.Coverage.OrphanedReferenceCount}, 未割当 unresolved: {projection.Coverage.UnassignedUnresolvedObservationCount}";

    public ReviewReference? SelectedReference
    {
        get => selectedReference;
        set => SetField(ref selectedReference, value);
    }

    public ProjectionSong? SelectedSong
    {
        get => selectedSong;
        set => SetField(ref selectedSong, value);
    }

    public ManualReviewDraftRow? SelectedManualReviewRow
    {
        get => selectedManualReviewRow;
        set
        {
            if (SetField(ref selectedManualReviewRow, value))
            {
                SelectedSong = value is null || projection is null
                    ? null
                    : projection.Songs.FirstOrDefault(
                        song => song.SongId == value.TruthSongId);
            }
        }
    }

    public ReviewedManualReviewRow? SelectedReviewedManualReviewRow
    {
        get => selectedReviewedManualReviewRow;
        set => SetField(ref selectedReviewedManualReviewRow, value);
    }

    public string SongSearch
    {
        get => songSearch;
        set
        {
            if (SetField(ref songSearch, value))
            {
                ApplySongSearch();
            }
        }
    }

    public string ReviewReason
    {
        get => reviewReason;
        set => SetField(ref reviewReason, value);
    }

    public string ReviewNote
    {
        get => reviewNote;
        set => SetField(ref reviewNote, value);
    }

    public string SelectedCoverageStatus
    {
        get => selectedCoverageStatus;
        set
        {
            if (SetField(ref selectedCoverageStatus, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedReason
    {
        get => selectedReason;
        set
        {
            if (SetField(ref selectedReason, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedCandidateClassification
    {
        get => selectedCandidateClassification;
        set
        {
            if (SetField(ref selectedCandidateClassification, value))
            {
                ApplyFilters();
            }
        }
    }

    public string StatusTitle
    {
        get => statusTitle;
        private set => SetField(ref statusTitle, value);
    }
    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                NotifyOperationDisplayProperties();
            }
        }
    }

    private async Task LoadOfficialSnapshotMetadataAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            officialSnapshotMetadata =
                await officialJacketSnapshotService.LoadAsync(cancellationToken);
            NotifyOfficialSnapshotProperties();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            officialSnapshotMetadata = null;
            officialSnapshotOutcome = OfficialSnapshotOperationOutcome.Failed;
            officialSnapshotLastResult =
                "公式ジャケット情報を読み込めませんでした。既存の情報は変更していません。";
            NotifyOfficialSnapshotProperties();
        }
    }

    public async Task InitializeDatabasesAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("別の処理を実行中です。");
        }

        IsBusy = true;
        OperationState = CollectorOperationState.LoadingDatabases;
        StatusTitle = "DB確認中";
        StatusMessage = "固定pathの曲情報DBとジャケット情報DBをread-onlyで検証しています。";
        try
        {
            await LoadOfficialSnapshotMetadataAsync(cancellationToken);
            await InitializeDatabasesCoreAsync(cancellationToken);
            if (projection is not null)
            {
                OperationState = CollectorOperationState.Ready;
                StatusTitle = "読込完了";
                StatusMessage =
                    $"固定DBからGP対象 {projection.Coverage.GrandPrixSongCount} 曲を表示しました。";
            }
            else
            {
                OperationState = CollectorOperationState.NoMaster;
            }
        }
        catch (OperationCanceledException)
        {
            ClearProjection();
            StatusTitle = "DB確認取消";
            StatusMessage = "DB確認を取り消しました。DBは変更していません。";
            throw;
        }
        catch (Exception exception)
        {
            ClearProjection();
            OperationState = CollectorOperationState.Failed;
            StatusTitle = "DB初期化/検証失敗";
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadProjectionAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("別の処理を実行中です。");
        }

        IsBusy = true;
        OperationState = CollectorOperationState.ReloadingProjection;
        StatusTitle = "読込中";
        StatusMessage = "固定pathのmasterとcatalogをread-onlyで検証しています。";
        try
        {
            await LoadOfficialSnapshotMetadataAsync(cancellationToken);
            await LoadProjectionCoreAsync(cancellationToken);
            OperationState = projection is null
                ? CollectorOperationState.NoMaster
                : CollectorOperationState.Ready;
            StatusTitle = "読込完了";
            StatusMessage = $"固定DBからGP対象 {projection!.Coverage.GrandPrixSongCount} 曲を表示しました。";
        }
        catch (OperationCanceledException)
        {
            ClearProjection();
            OperationState = CollectorOperationState.Failed;
            StatusTitle = "読込取消";
            StatusMessage = "読込を取り消しました。DBは変更していません。";
            throw;
        }
        catch (Exception exception)
        {
            ClearProjection();
            OperationState = CollectorOperationState.Failed;
            StatusTitle = "読込失敗";
            StatusMessage = exception.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyReviewAsync(
        string action,
        CancellationToken cancellationToken = default)
    {
        if (reviewWorkflowService is null || projection is null)
        {
            throw new InvalidOperationException("current catalogを先に読み込んでください。");
        }
        if (SelectedReference is null)
        {
            throw new InvalidOperationException("選択中projectionはmanual reviewに対応していません。");
        }
        var selectedSongId = action is "manual_confirm" or "reassign"
            ? SelectedSong?.SongId
                ?? throw new InvalidOperationException("GP対象songを明示選択してください。")
            : null;
        var mutation = new ReviewMutation(
            Guid.NewGuid().ToString("D"),
            SelectedReference.ReferenceId,
            action,
            SelectedReference.Revision,
            SelectedReference.StoredStatus,
            SelectedReference.AssignedSong?.SongId,
            selectedSongId,
            ReviewReason,
            ReviewNote);
        IsBusy = true;
        StatusTitle = "review更新中";
        StatusMessage = $"{action} をrevision precondition付きで実行しています。";
        try
        {
            var receipt = await reviewWorkflowService.ApplyAsync(
                fixedDatabasePaths.MasterPath,
                fixedDatabasePaths.CatalogPath,
                mutation,
                cancellationToken);
            await LoadProjectionCoreAsync(cancellationToken);
            SelectedReference = projection.ReviewReferences.FirstOrDefault(
                item => item.ReferenceId == receipt.ReferenceId);
            StatusTitle = "review更新完了";
            StatusMessage = $"{receipt.Action}: {receipt.Status}, revision={receipt.Revision}";
        }
        catch (Exception exception)
        {
            StatusTitle = exception is OperationCanceledException ? "review更新取消" : "review更新失敗/競合";
            StatusMessage = exception.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> SaveDraftsAsync(
        CancellationToken cancellationToken = default)
    {
        if (manualReviewDraftStore is null)
        {
            throw new InvalidOperationException("manual review draft store is not configured.");
        }
        if (projection is null)
        {
            StatusTitle = "下書き保存不可";
            StatusMessage = "current catalogを先に読み込んでください。";
            return false;
        }

        var dirtyRows = ManualReviewRows.Where(row => !row.IsSaved).ToList();
        if (dirtyRows.Count == 0)
        {
            StatusTitle = "下書き保存";
            StatusMessage = "未保存の変更はありません。catalog/historyは変更していません。";
            return true;
        }
        var validSongIds = projection.Songs
            .Select(song => song.SongId)
            .ToHashSet(StringComparer.Ordinal);
        var validationErrors = new List<(ManualReviewDraftRow Row, string Message)>();
        foreach (var row in dirtyRows)
        {
            var validationError = row.Validate(validSongIds);
            row.SetValidationError(validationError ?? "");
            if (validationError is not null)
            {
                validationErrors.Add((row, validationError));
            }
        }
        if (validationErrors.Count > 0)
        {
            var first = validationErrors[0];
            StatusTitle = "下書きvalidation error";
            StatusMessage =
                $"{validationErrors.Count}行に入力エラーがあります。"
                + $" observation={first.Row.ObservationId}: {first.Message}";
            return false;
        }

        IsBusy = true;
        StatusTitle = "下書き保存中";
        StatusMessage = $"{dirtyRows.Count}行の未保存下書きを保存しています。";
        try
        {
            var nextDrafts = new Dictionary<string, ManualReviewDraft>(
                manualReviewDrafts,
                StringComparer.Ordinal);
            foreach (var row in dirtyRows)
            {
                nextDrafts[row.ObservationId] = row.ToDraft();
            }
            await manualReviewDraftStore.SaveAsync(nextDrafts.Values, cancellationToken);
            manualReviewDrafts.Clear();
            foreach (var draft in nextDrafts)
            {
                manualReviewDrafts[draft.Key] = draft.Value;
            }
            foreach (var row in dirtyRows)
            {
                row.MarkSaved();
            }
            StatusTitle = "下書き保存完了";
            StatusMessage =
                $"{dirtyRows.Count}行を保存しました。catalog/historyは変更していません。";
            NotifyManualReviewCounts();
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusTitle = "下書き保存取消";
            StatusMessage = "下書き保存を取り消しました。catalog/historyは変更していません。";
            throw;
        }
        catch (Exception exception)
        {
            StatusTitle = "下書き保存失敗";
            StatusMessage = exception.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ImportManualReviewXlsxAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        if (manualReviewDraftStore is null)
        {
            throw new InvalidOperationException("manual review draft store is not configured.");
        }
        if (manualReviewXlsxImportService is null)
        {
            throw new InvalidOperationException("manual review XLSX import is not configured.");
        }
        if (projection is null)
        {
            StatusTitle = "XLSX import不可";
            StatusMessage = "current catalogを先に読み込んでください。";
            return false;
        }
        if (IsBusy)
        {
            throw new InvalidOperationException("別の処理を実行中です。");
        }

        IsBusy = true;
        StatusTitle = "XLSX import中";
        StatusMessage = "XLSX全行を検証しています。下書きはまだ変更していません。";
        try
        {
            var result = await manualReviewXlsxImportService.ImportManualReviewXlsxAsync(
                fixedDatabasePaths.MasterPath,
                fixedDatabasePaths.CatalogPath,
                inputPath,
                cancellationToken);
            if (result.Drafts is null)
            {
                throw new InvalidOperationException("Manual review XLSX import result has no drafts.");
            }

            var currentReferences = new Dictionary<string, ReviewReference>(StringComparer.Ordinal);
            foreach (var reference in projection.ReviewReferences)
            {
                var observationId = reference.CandidateEvaluation.ObservationId;
                if (!currentReferences.TryAdd(observationId, reference))
                {
                    throw new InvalidOperationException(
                        $"current projection has a duplicate observation ID: {observationId}");
                }
            }

            var importedObservationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var draft in result.Drafts)
            {
                if (draft is null
                    || string.IsNullOrWhiteSpace(draft.ObservationId)
                    || !importedObservationIds.Add(draft.ObservationId))
                {
                    throw new InvalidOperationException(
                        "Manual review XLSX import result has duplicate or empty observation IDs.");
                }
                if (!currentReferences.TryGetValue(draft.ObservationId, out var reference))
                {
                    throw new InvalidOperationException(
                        $"Manual review XLSX observation is not in the current projection: "
                        + draft.ObservationId);
                }
                if (reference.StoredStatus is "auto_confirmed" or "manual_confirmed" or "rejected")
                {
                    throw new InvalidOperationException(
                        $"Manual review XLSX observation is already reviewed: {draft.ObservationId}");
                }
            }

            var nextDrafts = new Dictionary<string, ManualReviewDraft>(
                manualReviewDrafts,
                StringComparer.Ordinal);
            foreach (var draft in result.Drafts)
            {
                nextDrafts[draft.ObservationId] = draft;
            }

            await manualReviewDraftStore.SaveAsync(nextDrafts.Values, cancellationToken);
            ReplaceManualReviewDrafts(nextDrafts);
            ApplyManualReviewRows();
            StatusTitle = "XLSX import完了";
            StatusMessage =
                $"{result.Drafts.Count}行を下書きへ反映しました。"
                + " catalog/history/確定状態は変更していません。";
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusTitle = "XLSX import取消";
            StatusMessage = "XLSX importを取り消しました。下書き・catalog/historyは変更していません。";
            throw;
        }
        catch (Exception exception)
        {
            StatusTitle = "XLSX import失敗";
            StatusMessage = exception.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ApplyDraftsAsync(
        CancellationToken cancellationToken = default)
    {
        if (manualReviewDraftStore is null)
        {
            throw new InvalidOperationException("manual review draft store is not configured.");
        }
        if (reviewWorkflowService is null || projection is null)
        {
            StatusTitle = "一括反映不可";
            StatusMessage = "current catalogを先に読み込んでください。";
            return false;
        }

        var validSongIds = projection.Songs
            .Select(song => song.SongId)
            .ToHashSet(StringComparer.Ordinal);
        var dirtyUnreviewedRows = ManualReviewRows
            .Where(row => !row.IsSaved)
            .ToList();
        var dirtyReviewedRows = ReviewedManualReviewRows
            .Where(row => !row.IsSaved)
            .ToList();
        var plannedUnreviewedRows = ManualReviewRows
            .Where(row => row.ShouldPersistDraft)
            .ToList();
        var plannedReviewedRows = ReviewedManualReviewRows
            .Where(row => row.ShouldPersistDraft)
            .ToList();

        var validationErrors = new List<(string ReferenceId, string ObservationId, string Message)>();
        foreach (var row in plannedUnreviewedRows)
        {
            var validationError = row.Validate(validSongIds);
            row.SetValidationError(validationError ?? "");
            if (validationError is not null)
            {
                validationErrors.Add((row.ReferenceId, row.ObservationId, validationError));
            }
        }
        foreach (var row in plannedReviewedRows)
        {
            var validationError = row.Validate(validSongIds);
            row.SetValidationError(validationError ?? "");
            if (validationError is not null)
            {
                validationErrors.Add((row.ReferenceId, row.ObservationId, validationError));
            }
        }
        if (validationErrors.Count > 0)
        {
            var first = validationErrors[0];
            StatusTitle = "一括反映validation error";
            StatusMessage =
                $"{validationErrors.Count}行に入力エラーがあります。"
                + $" reference={first.ReferenceId}, observation={first.ObservationId}: {first.Message}";
            return false;
        }

        var nextDrafts = new Dictionary<string, ManualReviewDraft>(
            manualReviewDrafts,
            StringComparer.Ordinal);
        foreach (var row in dirtyUnreviewedRows)
        {
            if (row.ShouldPersistDraft)
            {
                nextDrafts[row.ObservationId] = row.ToDraft();
            }
            else
            {
                nextDrafts.Remove(row.ObservationId);
            }
        }
        foreach (var row in dirtyReviewedRows)
        {
            if (row.ShouldPersistDraft)
            {
                nextDrafts[row.ObservationId] = row.ToDraft();
            }
            else
            {
                nextDrafts.Remove(row.ObservationId);
            }
        }

        var mutations = new List<ReviewMutation>();
        var cleanupObservationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in plannedUnreviewedRows)
        {
            var mutation = BuildUnreviewedMutation(row);
            if (mutation is not null)
            {
                mutations.Add(mutation);
                cleanupObservationIds.Add(row.ObservationId);
            }
        }
        foreach (var row in plannedReviewedRows)
        {
            var mutation = BuildReviewedMutation(row);
            if (mutation is not null)
            {
                mutations.Add(mutation);
            }
            if (row.DraftStatus is not "hold"
                && (mutation is not null || row.IsCurrentPlan))
            {
                cleanupObservationIds.Add(row.ObservationId);
            }
        }

        IsBusy = true;
        StatusTitle = "review一括反映中";
        StatusMessage = mutations.Count == 0
            ? "下書きを保存し、catalog変更が必要な行を確認しています。"
            : $"{mutations.Count}行を1 transactionで反映しています。";
        var catalogCommitted = false;
        try
        {
            if (dirtyUnreviewedRows.Count > 0 || dirtyReviewedRows.Count > 0)
            {
                await manualReviewDraftStore.SaveAsync(nextDrafts.Values, cancellationToken);
                ReplaceManualReviewDrafts(nextDrafts);
                foreach (var row in dirtyUnreviewedRows)
                {
                    if (row.ShouldPersistDraft)
                    {
                        row.MarkSaved();
                    }
                    else
                    {
                        row.MarkDraftRemoved();
                    }
                }
                foreach (var row in dirtyReviewedRows)
                {
                    if (row.ShouldPersistDraft)
                    {
                        row.MarkSaved();
                    }
                    else
                    {
                        row.MarkDraftRemoved();
                    }
                }
            }

            ReviewMutationBatchReceipt? receipt = null;
            if (mutations.Count > 0)
            {
                receipt = await reviewWorkflowService.ApplyBatchAsync(
                    fixedDatabasePaths.MasterPath,
                    fixedDatabasePaths.CatalogPath,
                    mutations,
                    cancellationToken);
                catalogCommitted = true;
                await LoadProjectionCoreAsync(cancellationToken);
            }

            if (cleanupObservationIds.Count > 0)
            {
                foreach (var observationId in cleanupObservationIds)
                {
                    manualReviewDrafts.Remove(observationId);
                }
                await manualReviewDraftStore.SaveAsync(
                    manualReviewDrafts.Values.ToList(),
                    cancellationToken);
            }
            if (mutations.Count > 0 || cleanupObservationIds.Count > 0)
            {
                await LoadProjectionCoreAsync(cancellationToken);
            }

            if (receipt is null)
            {
                StatusTitle = "下書き保存完了";
                StatusMessage =
                    "下書きを保存しました。未レビュー・保留はcatalogへ反映していません。";
            }
            else
            {
                StatusTitle = "review一括反映完了";
                StatusMessage =
                    $"requested={receipt.RequestedCount}, applied={receipt.AppliedCount}, "
                    + $"no-op={receipt.NoOpCount}。未レビューの反映対象を一覧から外しました。";
            }
            NotifyManualReviewCounts();
            return true;
        }
        catch (OperationCanceledException)
        {
            if (catalogCommitted)
            {
                StatusTitle = "review一括反映済み・後処理未完了";
                StatusMessage =
                    "catalog/historyは反映済みです。取消はDB反映を戻していません。"
                    + " projection再読込後、残っている下書きのcleanupを再試行してください。";
            }
            else
            {
                StatusTitle = "review一括反映取消";
                StatusMessage = "一括反映を取り消しました。catalog/historyは変更していません。";
            }
            throw;
        }
        catch (Exception exception)
        {
            if (exception is ReviewBatchPostCommitException)
            {
                catalogCommitted = true;
            }
            if (!catalogCommitted)
            {
                foreach (var row in plannedUnreviewedRows)
                {
                    if (exception.Message.Contains(row.ReferenceId, StringComparison.Ordinal)
                        || exception.Message.Contains(row.ObservationId, StringComparison.Ordinal))
                    {
                        row.SetValidationError(exception.Message);
                    }
                }
                foreach (var row in plannedReviewedRows)
                {
                    if (exception.Message.Contains(row.ReferenceId, StringComparison.Ordinal)
                        || exception.Message.Contains(row.ObservationId, StringComparison.Ordinal))
                    {
                        row.SetValidationError(exception.Message);
                    }
                }
            }
            if (catalogCommitted)
            {
                StatusTitle = "review一括反映済み・後処理未完了";
                StatusMessage =
                    "catalog/historyは反映済みです。projection再読込後、残っている下書きのcleanupを"
                    + $"再試行してください。詳細: {exception.Message}";
            }
            else
            {
                StatusTitle = "review一括反映失敗/ロールバック";
                StatusMessage = exception.Message;
            }
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static ReviewMutation? BuildUnreviewedMutation(ManualReviewDraftRow row) =>
        row.Status switch
        {
            "confirmed" => NewMutation(
                row.Reference,
                "manual_confirm",
                row.TruthSongId,
                row.Notes),
            "rejected" => NewMutation(row.Reference, "reject", null, row.Notes),
            _ => null,
        };

    private static ReviewMutation? BuildReviewedMutation(ReviewedManualReviewRow row)
    {
        if (row.DraftStatus == "hold")
        {
            return null;
        }
        if (row.DraftStatus == "unchanged" && row.Notes == row.Reference.Notes)
        {
            return null;
        }
        if (row.DraftStatus == "rejected")
        {
            return NewMutation(row.Reference, "reject", null, row.Notes);
        }
        if (row.DraftStatus == "confirmed")
        {
            var action = row.CurrentStatus == "rejected" ? "manual_confirm" : "reassign";
            return NewMutation(row.Reference, action, row.DraftSongId, row.Notes);
        }
        if (row.DraftStatus == "unchanged")
        {
            var action = row.CurrentStatus == "rejected" ? "reject" : "reassign";
            return NewMutation(row.Reference, action, row.CurrentSongId, row.Notes);
        }
        throw new InvalidOperationException($"unsupported reviewed draft status: {row.DraftStatus}");
    }

    private static ReviewMutation NewMutation(
        ReviewReference reference,
        string action,
        string? songId,
        string note) => new(
            Guid.NewGuid().ToString("D"),
            reference.ReferenceId,
            action,
            reference.Revision,
            reference.CurrentStatus,
            reference.CurrentSongId,
            songId,
            reference.Reason,
            note,
            reference.Notes);

    private void ReplaceManualReviewDrafts(
        IReadOnlyDictionary<string, ManualReviewDraft> drafts)
    {
        manualReviewDrafts.Clear();
        foreach (var draft in drafts)
        {
            manualReviewDrafts[draft.Key] = draft.Value;
        }
    }

    private Task LoadProjectionCoreAsync(CancellationToken cancellationToken) =>
        LoadProjectionCoreAsync(
            fixedDatabasePaths.MasterPath,
            fixedDatabasePaths.CatalogPath,
            cancellationToken,
            existingMasterSummary: null);

    private async Task LoadProjectionCoreAsync(
        string masterPathValue,
        string catalogPathValue,
        CancellationToken cancellationToken,
        MasterSummary? existingMasterSummary = null)
    {
        var loadedProjection = await projectionService.LoadAsync(
            masterPathValue, catalogPathValue, cancellationToken);
        var loadedMasterSummary = existingMasterSummary
            ?? await masterUpdateService.InspectAsync(masterPathValue, cancellationToken);
        var loadedDrafts = manualReviewDraftStore is null
            ? new Dictionary<string, ManualReviewDraft>(StringComparer.Ordinal)
            : (await manualReviewDraftStore.LoadAsync(cancellationToken))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        projection = loadedProjection;
        masterSummary = loadedMasterSummary;
        manualReviewDrafts.Clear();
        foreach (var draft in loadedDrafts)
        {
            manualReviewDrafts[draft.Key] = draft.Value;
        }
        RebuildFilterOptions();
        ApplyFilters();
        ApplyManualReviewRows();
        ApplySongSearch();
        NotifyProjectionProperties();
    }

    private async Task InitializeDatabasesCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(fixedDatabasePaths.MasterPath))
        {
            ClearProjection();
            StatusTitle = "曲情報がありません";
            StatusMessage = "曲情報を更新すると固定pathへmaster DBを作成できます。";
            return;
        }

        MasterSummary inspectedMaster;
        try
        {
            inspectedMaster = await masterUpdateService.InspectAsync(
                fixedDatabasePaths.MasterPath,
                cancellationToken);
            masterSummary = inspectedMaster;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"曲情報DBの検証に失敗しました。catalog作成と収集は開始しません: {exception.Message}",
                exception);
        }

        var catalogCreated = false;
        try
        {
            if (!File.Exists(fixedDatabasePaths.CatalogPath))
            {
                OperationState = CollectorOperationState.InitializingCatalog;
                StatusTitle = "ジャケット情報初期化中";
                StatusMessage = "current schemaの空catalogを固定pathへ作成しています。";
                await catalogInitializer.EnsureCreatedAsync(cancellationToken);
                catalogCreated = true;
            }

            StatusTitle = "ジャケット情報確認中";
            StatusMessage = "固定pathのcatalogをstrict read-only projectionで検証しています。";
            OperationState = CollectorOperationState.ReloadingProjection;
            await LoadProjectionCoreAsync(
                fixedDatabasePaths.MasterPath,
                fixedDatabasePaths.CatalogPath,
                cancellationToken,
                inspectedMaster);
        }
        catch (OperationCanceledException)
        {
            DeleteCreatedCatalog(catalogCreated);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DeleteCreatedCatalog(catalogCreated);
            throw new InvalidOperationException(
                $"ジャケット情報DBの初期化または検証に失敗しました。既存fileは置換していません: {exception.Message}",
                exception);
        }
    }

    public async Task UpdateMasterAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUpdateMaster)
        {
            throw new InvalidOperationException("曲情報を更新できない状態です。");
        }

        IsBusy = true;
        OperationState = CollectorOperationState.UpdatingMaster;
        StatusTitle = "master更新中";
        StatusMessage = "staging生成とinspectionを実行しています。";
        var projectionReloadFailed = false;
        try
        {
            var result = await masterUpdateService.UpdateAsync(
                fixedDatabasePaths.MasterPath,
                cancellationToken);
            masterSummary = result.After;
            NotifyProjectionProperties();
            try
            {
                await InitializeDatabasesCoreAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ClearProjection();
                masterSummary = result.After;
                NotifyProjectionProperties();
                OperationState = CollectorOperationState.Failed;
                projectionReloadFailed = true;
                StatusTitle = "master更新後の再読込取消";
                StatusMessage = "masterは更新済みですが、projection再読込を取り消しました。";
                lastOperationResult = "曲情報を更新しましたが、表示を更新できませんでした。ログを確認してください。";
                OnPropertyChanged(nameof(LastOperationResultDisplay));
                throw;
            }
            catch (Exception exception)
            {
                ClearProjection();
                masterSummary = result.After;
                NotifyProjectionProperties();
                OperationState = CollectorOperationState.Failed;
                projectionReloadFailed = true;
                StatusTitle = "master更新後の再読込失敗";
                StatusMessage =
                    $"masterは更新済みですが、catalog/projectionを再読込できません: {exception.Message}";
                lastOperationResult = "曲情報を更新しましたが、表示を更新できませんでした。ログを確認してください。";
                OnPropertyChanged(nameof(LastOperationResultDisplay));
                throw;
            }
            OperationState = projection is null
                ? CollectorOperationState.NoMaster
                : CollectorOperationState.Ready;
            StatusTitle = "master更新完了";
            StatusMessage = result.Before is null
                ? $"新規 master {FormatSummary(result.After)} を公開しました。"
                : $"before [{FormatSummary(result.Before)}] → after [{FormatSummary(result.After)}]";
            lastOperationResult =
                $"曲情報を更新しました。\n更新日時: {MasterUpdatedAtDisplay}";
            OnPropertyChanged(nameof(LastOperationResultDisplay));
        }
        catch (OperationCanceledException)
        {
            if (projectionReloadFailed)
            {
                throw;
            }
            StatusTitle = "master更新取消";
            StatusMessage = "更新を取り消しました。既存masterは変更していません。";
            OperationState = CollectorOperationState.Failed;
            lastOperationResult = "曲情報を更新できませんでした。ログを確認してください。";
            OnPropertyChanged(nameof(LastOperationResultDisplay));
            throw;
        }
        catch (Exception exception)
        {
            if (projectionReloadFailed)
            {
                throw;
            }
            StatusTitle = "master更新失敗";
            StatusMessage = exception.Message;
            OperationState = CollectorOperationState.Failed;
            lastOperationResult = "曲情報を更新できませんでした。ログを確認してください。";
            OnPropertyChanged(nameof(LastOperationResultDisplay));
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UpdateOfficialSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanUpdateOfficialSnapshot)
        {
            throw new InvalidOperationException(
                "公式ジャケット情報を更新できない状態です。");
        }

        IsBusy = true;
        OperationState = CollectorOperationState.UpdatingOfficialSnapshot;
        officialSnapshotOutcome = OfficialSnapshotOperationOutcome.NotRun;
        officialSnapshotProgress = null;
        StatusTitle = "公式ジャケット情報更新中";
        StatusMessage = "固定条件で曲一覧とjacketを取得・検証しています。";
        NotifyOfficialSnapshotProperties();
        var snapshotPublished = false;
        try
        {
            var progress = new Progress<OfficialJacketSnapshotProgress>(value =>
            {
                officialSnapshotProgress = value;
                OnPropertyChanged(nameof(OfficialSnapshotProgressPercent));
                OnPropertyChanged(nameof(OfficialSnapshotProgressPercentDisplay));
                OnPropertyChanged(nameof(OfficialSnapshotProgressDetailDisplay));
            });
            var result = await officialJacketSnapshotService.UpdateAsync(
                progress,
                cancellationToken);
            snapshotPublished = true;
            officialSnapshotMetadata = result.Metadata;
            officialSnapshotOutcome = OfficialSnapshotOperationOutcome.Succeeded;
            if (projection is not null)
            {
                try
                {
                    OperationState = CollectorOperationState.ReloadingProjection;
                    StatusTitle = "公式ジャケット情報更新後の再読込中";
                    StatusMessage = "固定snapshotと既存projectionを再読込しています。";
                    await LoadProjectionCoreAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    SetOfficialSnapshotPostPublishFailure(
                        "公式ジャケット情報更新完了・projection再読込取消",
                        "公式ジャケット情報は更新済みですが、表示の再読込を取り消しました。");
                    throw;
                }
                catch (Exception)
                {
                    SetOfficialSnapshotPostPublishFailure(
                        "公式ジャケット情報更新完了・projection再読込失敗",
                        "公式ジャケット情報は更新済みですが、表示を再読込できませんでした。");
                    throw;
                }
            }
            officialSnapshotLastResult =
                "公式ジャケット情報を更新しました。\n"
                + $"更新日時: {OfficialSnapshotUpdatedAtDisplay}\n"
                + $"{OfficialSnapshotSongCountDisplay} / "
                + $"{OfficialSnapshotStoredImageCountDisplay}\n"
                + $"固定配置先: {OfficialSnapshotPathDisplay}";
            lastOperationResult = officialSnapshotLastResult;
            StatusTitle = "公式ジャケット情報更新完了";
            StatusMessage =
                $"公式ジャケット情報 {OfficialSnapshotSongCountDisplay} / "
                + $"{OfficialSnapshotStoredImageCountDisplay} を再読込しました。";
            OperationState = projection is null
                ? CollectorOperationState.NoMaster
                : CollectorOperationState.Ready;
            OnPropertyChanged(nameof(LastOperationResultDisplay));
            NotifyOfficialSnapshotProperties();
        }
        catch (OperationCanceledException)
        {
            if (snapshotPublished)
            {
                throw;
            }
            officialSnapshotOutcome = OfficialSnapshotOperationOutcome.Canceled;
            officialSnapshotLastResult =
                "公式ジャケット情報の取得をキャンセルしました。\n"
                + "既存の公式ジャケット情報は維持されています。\n"
                + "途中データは完成済み情報として使用しません。";
            lastOperationResult = officialSnapshotLastResult;
            StatusTitle = "公式ジャケット情報更新取消";
            StatusMessage = "既存の公式ジャケット情報は変更していません。";
            OperationState = projection is null
                ? CollectorOperationState.NoMaster
                : CollectorOperationState.Ready;
            OnPropertyChanged(nameof(LastOperationResultDisplay));
            NotifyOfficialSnapshotProperties();
            throw;
        }
        catch (Exception exception)
        {
            if (snapshotPublished)
            {
                throw;
            }
            officialSnapshotOutcome = OfficialSnapshotOperationOutcome.Failed;
            var failureMessage = exception is OfficialJacketSnapshotUpdateException
                ? exception.Message
                : "取得処理を完了できませんでした。";
            officialSnapshotLastResult =
                "公式ジャケット情報の更新に失敗しました。\n"
                + failureMessage + "\n"
                + "既存の公式ジャケット情報は維持されています。";
            lastOperationResult = officialSnapshotLastResult;
            StatusTitle = "公式ジャケット情報更新失敗";
            StatusMessage = failureMessage;
            OperationState = CollectorOperationState.Failed;
            OnPropertyChanged(nameof(LastOperationResultDisplay));
            NotifyOfficialSnapshotProperties();
            throw;
        }
        finally
        {
            officialSnapshotProgress = null;
            IsBusy = false;
            NotifyOfficialSnapshotProperties();
        }
    }

    private void SetOfficialSnapshotPostPublishFailure(
        string statusTitle,
        string statusMessage)
    {
        ClearProjection();
        OperationState = CollectorOperationState.Failed;
        StatusTitle = statusTitle;
        StatusMessage = statusMessage;
        officialSnapshotLastResult =
            "公式ジャケット情報を更新しましたが、表示を再読込できませんでした。";
        lastOperationResult = officialSnapshotLastResult;
        OnPropertyChanged(nameof(LastOperationResultDisplay));
        NotifyOfficialSnapshotProperties();
    }

    public Task StartObservationSessionAsync(
        WindowCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (!CanStartCollection || Observation is null || projection is null)
        {
            throw new InvalidOperationException(
                "master/catalogを先に読み込み、DDR GPを検出してください。");
        }
        return Observation.StartSessionAsync(
            projection.Master,
            projection.Catalog,
            candidate,
            fixedDatabasePaths.MasterPath,
            fixedDatabasePaths.CatalogPath,
            cancellationToken);
    }

    public Task ResumeObservationSessionAsync(
        WindowCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (Observation is null || projection is null)
        {
            throw new InvalidOperationException(
                "master/catalogを先に読み込み、DDR GPを検出してください。");
        }
        return Observation.ResumeSessionAsync(
            projection.Master,
            projection.Catalog,
            candidate,
            fixedDatabasePaths.MasterPath,
            fixedDatabasePaths.CatalogPath,
            cancellationToken);
    }

    public Task StopObservationSessionAsync(CancellationToken cancellationToken = default) =>
        Observation?.StopAsync(cancellationToken) ?? Task.CompletedTask;

    public async Task FinalizeObservationSessionAsync(
        CancellationToken cancellationToken = default)
    {
        if (Observation is null)
        {
            return;
        }
        if (IsBusy)
        {
            throw new InvalidOperationException("別の処理を実行中です。");
        }
        IsBusy = true;
        OperationState = CollectorOperationState.FinalizingCollection;
        StatusTitle = "収集終了・catalog retry中";
        StatusMessage = "開始済みframe/保存処理をdrainしてpending observationをretryしています。";
        CatalogRetrySummary? summary = null;
        Exception? stopFailure = null;
        try
        {
            summary = await Observation.FinalizeCatalogAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            stopFailure = exception;
        }

        var projectionReloadMessage = "未実施";
        var projectionReloaded = false;
        if (stopFailure is OperationCanceledException stopCancellation)
        {
            ClearProjection();
            projectionReloadMessage = $"未実施（停止取消: {stopCancellation.Message}）";
        }
        else
        {
            try
            {
                OperationState = CollectorOperationState.ReloadingProjection;
                await LoadProjectionCoreAsync(cancellationToken);
                projectionReloaded = true;
                projectionReloadMessage = "成功";
            }
            catch (OperationCanceledException exception)
            {
                ClearProjection();
                projectionReloadMessage = $"取消: {exception.Message}";
            }
            catch (Exception exception)
            {
                ClearProjection();
                projectionReloadMessage = $"失敗: {exception.Message}";
            }
        }

        try
        {
            collectionEndResult = FormatCollectionEndResult(
                summary,
                stopFailure,
                projectionReloaded);
            lastOperationResult = collectionEndResult;
            OnPropertyChanged(nameof(CollectionEndResultDisplay));
            OnPropertyChanged(nameof(LastOperationResultDisplay));
            if (stopFailure is not null)
            {
                OperationState = CollectorOperationState.Failed;
                StatusTitle = "収集終了・停止処理失敗";
                StatusMessage =
                    $"停止/checkpoint処理: {stopFailure.Message} / projection再読込: "
                    + projectionReloadMessage;
            }
            else if (summary?.IsRejected == true)
            {
                OperationState = projectionReloaded
                    ? CollectorOperationState.Ready
                    : CollectorOperationState.Failed;
                StatusTitle = projectionReloaded
                    ? "収集終了・catalog retry拒否"
                    : "収集終了・catalog retry拒否/projection再読込失敗";
                StatusMessage =
                    $"catalog retry: {summary.DisplayMessage} / projection再読込: "
                    + projectionReloadMessage;
            }
            else
            {
                OperationState = projectionReloaded
                    ? CollectorOperationState.Ready
                    : CollectorOperationState.Failed;
                StatusTitle = projectionReloaded
                    ? "収集終了・projection再読込完了"
                    : "収集終了・projection再読込失敗";
                StatusMessage =
                    $"catalog retry: {summary?.DisplayMessage ?? "結果なし"} / projection再読込: "
                    + projectionReloadMessage;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RetryCatalogSessionAsync(CancellationToken cancellationToken = default)
    {
        if (Observation is null || projection is null)
        {
            throw new InvalidOperationException(
                "master/catalogとprojectionを先に読み込んでください。");
        }
        if (IsBusy)
        {
            throw new InvalidOperationException("別の処理を実行中です。");
        }
        IsBusy = true;
        OperationState = CollectorOperationState.RetryingCatalog;
        StatusTitle = "catalog retry中";
        StatusMessage = "指定sessionのcheckpoint/artifactを検証してcatalogへretryしています。";
        try
        {
            var summary = await Observation.RetryCatalogAsync(
                projection.Master,
                projection.Catalog,
                fixedDatabasePaths.MasterPath,
                fixedDatabasePaths.CatalogPath,
                cancellationToken);
            try
            {
                OperationState = CollectorOperationState.ReloadingProjection;
                await LoadProjectionCoreAsync(cancellationToken);
                OperationState = CollectorOperationState.Ready;
                StatusTitle = "catalog retry・projection再読込完了";
                StatusMessage =
                    $"catalog retry: {summary.DisplayMessage} / projection再読込: 成功";
            }
            catch (OperationCanceledException exception)
            {
                ClearProjection();
                OperationState = CollectorOperationState.Failed;
                StatusTitle = "catalog retry・projection再読込取消";
                StatusMessage =
                    $"catalog retry: {summary.DisplayMessage} / projection再読込: 取消: {exception.Message}";
                throw;
            }
            catch (Exception exception)
            {
                ClearProjection();
                OperationState = CollectorOperationState.Failed;
                StatusTitle = "catalog retry・projection再読込失敗";
                StatusMessage =
                    $"catalog retry: {summary.DisplayMessage} / projection再読込: 失敗: {exception.Message}";
                throw;
            }
        }
        catch (Exception exception) when (StatusTitle == "catalog retry中")
        {
            OperationState = CollectorOperationState.Failed;
            StatusTitle = "catalog retry失敗";
            StatusMessage = exception.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildFilterOptions()
    {
        ReasonOptions.Clear();
        ReasonOptions.Add("all");
        foreach (var reason in projection!.Songs.Select(song => song.Reason)
                     .Concat(projection.ReviewReferences.Select(reference => reference.Reason))
                     .Where(reason => !string.IsNullOrEmpty(reason))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            ReasonOptions.Add(reason);
        }
        SelectedCoverageStatus = "all";
        SelectedReason = "all";
        CandidateClassificationOptions.Clear();
        CandidateClassificationOptions.Add("all");
        foreach (var classification in projection.ReviewReferences
                     .Select(reference => reference.CandidateEvaluation.Classification)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            CandidateClassificationOptions.Add(classification);
        }
        SelectedCandidateClassification = "all";
    }

    private void ApplyFilters()
    {
        if (projection is null)
        {
            return;
        }
        Songs.Clear();
        foreach (var song in projection.Songs.Where(
                     song => (SelectedCoverageStatus == "all" || song.CoverageStatus == SelectedCoverageStatus)
                         && (SelectedReason == "all" || song.Reason == SelectedReason)))
        {
            Songs.Add(song);
        }
        ReviewReferences.Clear();
        foreach (var reference in projection.ReviewReferences.Where(
                     reference => (SelectedCoverageStatus == "all" || reference.ReviewStatus == SelectedCoverageStatus)
                         && (SelectedReason == "all" || reference.Reason == SelectedReason)
                         && (SelectedCandidateClassification == "all"
                             || reference.CandidateEvaluation.Classification == SelectedCandidateClassification))
                     .OrderBy(reference => reference.CandidateEvaluation.Classification, StringComparer.Ordinal)
                     .ThenBy(reference => reference.CandidateEvaluation.ObservationId, StringComparer.Ordinal))
        {
            ReviewReferences.Add(reference);
        }
    }

    private void ApplyManualReviewRows()
    {
        UnsubscribeManualReviewRows();
        ManualReviewRows.Clear();
        ReviewedManualReviewRows.Clear();
        SelectedManualReviewRow = null;
        SelectedReviewedManualReviewRow = null;
        if (projection is null)
        {
            NotifyManualReviewCounts();
            return;
        }

        var songsById = projection.Songs.ToDictionary(song => song.SongId, StringComparer.Ordinal);
        foreach (var reference in projection.ReviewReferences
                     .Where(IsUnreflectedReviewTarget)
                     .OrderBy(reference => reference.CandidateEvaluation.ObservationId, StringComparer.Ordinal)
                     .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal))
        {
            var observationId = reference.CandidateEvaluation.ObservationId;
            manualReviewDrafts.TryGetValue(observationId, out var draft);
            var row = new ManualReviewDraftRow(reference, draft, songsById);
            row.PropertyChanged += ManualReviewRow_PropertyChanged;
            ManualReviewRows.Add(row);
        }
        foreach (var reference in projection.ReviewReferences
                     .Where(IsReviewedManualReviewTarget)
                     .OrderBy(reference => reference.ProcessedAt, StringComparer.Ordinal)
                     .ThenBy(reference => reference.CandidateEvaluation.ObservationId, StringComparer.Ordinal)
                     .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal))
        {
            var observationId = reference.CandidateEvaluation.ObservationId;
            manualReviewDrafts.TryGetValue(observationId, out var draft);
            var row = new ReviewedManualReviewRow(reference, draft, songsById);
            ReviewedManualReviewRows.Add(row);
        }
        NotifyManualReviewCounts();
    }

    private static bool IsUnreflectedReviewTarget(ReviewReference reference) =>
        reference.StoredStatus is not ("auto_confirmed" or "manual_confirmed" or "rejected");

    private static bool IsReviewedManualReviewTarget(ReviewReference reference) =>
        reference.CurrentStatus is "auto_confirmed" or "manual_confirmed" or "rejected";

    private void ManualReviewRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ManualReviewDraftRow.IsSaved)
            or nameof(ManualReviewDraftRow.Status))
        {
            NotifyManualReviewCounts();
        }
    }

    private int CountManualReviewRows(string status) =>
        ManualReviewRows.Count(row => row.Status == status);

    private void NotifyManualReviewCounts()
    {
        OnPropertyChanged(nameof(ManualReviewUnreviewedCount));
        OnPropertyChanged(nameof(ManualReviewConfirmedCount));
        OnPropertyChanged(nameof(ManualReviewRejectedCount));
        OnPropertyChanged(nameof(ManualReviewHoldCount));
        OnPropertyChanged(nameof(ReviewPendingCountDisplay));
        OnPropertyChanged(nameof(ReviewPendingSongCountDisplay));
        OnPropertyChanged(nameof(CollectionSummaryDisplay));
    }

    private void UnsubscribeManualReviewRows()
    {
        foreach (var row in ManualReviewRows)
        {
            row.PropertyChanged -= ManualReviewRow_PropertyChanged;
        }
    }

    private void ApplySongSearch()
    {
        SongChoices.Clear();
        if (projection is null)
        {
            return;
        }
        var query = SongSearch.Trim();
        foreach (var song in projection.Songs
                     .Select(song => (Song: song, Rank: SongSearchRank(song, query)))
                     .Where(item => item.Rank is not null)
                     .OrderBy(item => item.Rank)
                     .ThenBy(item => item.Song.Title, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Song.SongId, StringComparer.Ordinal))
        {
            SongChoices.Add(song.Song);
        }
    }

    private static int? SongSearchRank(ProjectionSong song, string query)
    {
        if (query.Length == 0)
        {
            return 0;
        }
        if (string.Equals(song.Title, query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        if (song.Aliases.Any(alias =>
                string.Equals(alias, query, StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }
        if (song.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }
        if (song.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }
        if (song.Artist.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }
        if (song.SongId.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }
        return null;
    }

    private void NotifyProjectionProperties()
    {
        OnPropertyChanged(nameof(MasterVersion));
        OnPropertyChanged(nameof(CurrentMasterPath));
        OnPropertyChanged(nameof(CurrentCatalogPath));
        OnPropertyChanged(nameof(MasterSourceHash));
        OnPropertyChanged(nameof(MasterCounts));
        OnPropertyChanged(nameof(CatalogIdentity));
        OnPropertyChanged(nameof(CatalogSchema));
        OnPropertyChanged(nameof(CoverageSummary));
        OnPropertyChanged(nameof(OrphanSummary));
        OnPropertyChanged(nameof(MasterUpdatedAtDisplay));
        OnPropertyChanged(nameof(MasterUpdatedAtLongDisplay));
        OnPropertyChanged(nameof(MasterHeaderDisplay));
        OnPropertyChanged(nameof(CatalogHeaderDisplay));
        OnPropertyChanged(nameof(MasterSongCountDisplay));
        OnPropertyChanged(nameof(MasterChartCountDisplay));
        OnPropertyChanged(nameof(CollectedCountDisplay));
        OnPropertyChanged(nameof(ReviewPendingSongCountDisplay));
        OnPropertyChanged(nameof(UncollectedCountDisplay));
        OnPropertyChanged(nameof(UnresolvedCountDisplay));
        OnPropertyChanged(nameof(OrphanedCountDisplay));
        OnPropertyChanged(nameof(CatalogCoverageDisplay));
        OnPropertyChanged(nameof(CollectionSummaryDisplay));
        OnPropertyChanged(nameof(CollectionOrphanSummaryDisplay));
        NotifyOperationDisplayProperties();
    }

    private void NotifyOfficialSnapshotProperties()
    {
        OnPropertyChanged(nameof(OfficialSnapshotHeaderDisplay));
        OnPropertyChanged(nameof(OfficialSnapshotUpdatedAtDisplay));
        OnPropertyChanged(nameof(OfficialSnapshotSongCountDisplay));
        OnPropertyChanged(nameof(OfficialSnapshotStoredImageCountDisplay));
        OnPropertyChanged(nameof(OfficialSnapshotPathDisplay));
        OnPropertyChanged(nameof(OfficialSnapshotUserStatusDisplay));
        OnPropertyChanged(nameof(OfficialSnapshotProgressPercent));
        OnPropertyChanged(nameof(OfficialSnapshotProgressPercentDisplay));
        OnPropertyChanged(nameof(OfficialSnapshotProgressDetailDisplay));
        OnPropertyChanged(nameof(OfficialSnapshotLastResultDisplay));
        NotifyOperationDisplayProperties();
    }

    private void ClearProjection()
    {
        projection = null;
        masterSummary = null;
        Songs.Clear();
        ReviewReferences.Clear();
        UnsubscribeManualReviewRows();
        ManualReviewRows.Clear();
        ReviewedManualReviewRows.Clear();
        SongChoices.Clear();
        SelectedReference = null;
        SelectedSong = null;
        SelectedManualReviewRow = null;
        SelectedReviewedManualReviewRow = null;
        manualReviewDrafts.Clear();
        ReasonOptions.Clear();
        ReasonOptions.Add("all");
        CandidateClassificationOptions.Clear();
        CandidateClassificationOptions.Add("all");
        SelectedCoverageStatus = "all";
        SelectedReason = "all";
        SelectedCandidateClassification = "all";
        NotifyManualReviewCounts();
        NotifyProjectionProperties();
    }

    private void DeleteCreatedCatalog(bool catalogCreated)
    {
        if (catalogCreated && File.Exists(fixedDatabasePaths.CatalogPath))
        {
            File.Delete(fixedDatabasePaths.CatalogPath);
        }
    }

    private static ICatalogInitializationService CreateCatalogInitializer(
        CollectorDatabasePaths? paths)
    {
        var resolved = paths ?? CollectorDatabasePaths.Resolve();
        return new CatalogInitializationService(
            new ProcessRunner(),
            resolved.RepositoryRoot,
            resolved.CatalogPath);
    }

    private static string FormatSummary(MasterSummary summary) =>
        $"version={summary.MasterVersion}, hash={summary.SourceHash}, songs={summary.SongCount}, charts={summary.ChartCount}, GP={summary.GrandPrixSongCount}";

    private int CountCoverage(string status) => projection?.Coverage.StatusCounts.GetValueOrDefault(status) ?? 0;

    private static string FormatLocalTimestamp(string? value, string format)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return "—";
        }
        return parsed.ToLocalTime().ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FormatCollectionEndResult(
        CatalogRetrySummary? summary,
        Exception? stopFailure,
        bool projectionReloaded)
    {
        var display = summary is null
            ? "収集を終了できませんでした。結果を取得できませんでした。"
            : "収集を終了しました。\n"
                + $"保存済み: {summary.SavedObservationCount} / "
                + $"ジャケット新規登録: {summary.CatalogCreatedCount} / "
                + $"登録済み: {summary.CatalogExistingCount} / "
                + $"反映失敗: {summary.CatalogFailureCount} / "
                + $"保留中: {summary.PendingObservationCount}";

        if (stopFailure is not null || summary?.IsRejected == true)
        {
            display += "\n収集結果を確定できませんでした。ログを確認してください。";
        }
        if (!projectionReloaded)
        {
            display += "\n表示の更新に失敗しました。ログを確認してください。";
        }
        return display;
    }

    private void ChildViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var collectionStateChanged =
            (ReferenceEquals(sender, WindowCapture)
                && (e.PropertyName is nameof(WindowCaptureViewModel.IsDetecting)
                    or nameof(WindowCaptureViewModel.Lifecycle)))
            || (ReferenceEquals(sender, Observation)
                && e.PropertyName is nameof(JacketObservationViewModel.IsActive));
        if (!collectionStateChanged)
        {
            return;
        }

        if (!IsBusy)
        {
            if (Observation?.IsActive == true
                || WindowCapture?.Lifecycle.State is CaptureLifecycleState.Starting
                    or CaptureLifecycleState.Capturing)
            {
                OperationState = CollectorOperationState.Collecting;
            }
            else if (OperationState == CollectorOperationState.Collecting)
            {
                OperationState = projection is null
                    ? CollectorOperationState.NoMaster
                    : CollectorOperationState.Ready;
            }
        }
        NotifyOperationDisplayProperties();
    }

    private void NotifyOperationDisplayProperties()
    {
        OnPropertyChanged(nameof(OperationStateDisplay));
        OnPropertyChanged(nameof(MasterUserStatusDisplay));
        OnPropertyChanged(nameof(CatalogUserStatusDisplay));
        OnPropertyChanged(nameof(CanUpdateMaster));
        OnPropertyChanged(nameof(CanUpdateOfficialSnapshot));
        OnPropertyChanged(nameof(CanStartCollection));
        OnPropertyChanged(nameof(CanStopCollection));
        OnPropertyChanged(nameof(IsOfficialSnapshotUpdating));
        OnPropertyChanged(nameof(CanCancelOfficialSnapshot));
        OnPropertyChanged(nameof(OfficialSnapshotUserStatusDisplay));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
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
