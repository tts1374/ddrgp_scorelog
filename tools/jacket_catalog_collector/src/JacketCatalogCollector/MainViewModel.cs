using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace JacketCatalogCollector;

public sealed class MainViewModel(
    IMasterUpdateService masterUpdateService,
    IProjectionService projectionService,
    IReviewWorkflowService? reviewWorkflowService = null) : INotifyPropertyChanged
{
    private ReviewProjection? projection;
    private string selectedCoverageStatus = "all";
    private string selectedReason = "all";
    private string statusTitle = "準備完了";
    private string statusMessage = "master DB と jacket catalog を選択してください。";
    private bool isBusy;
    private string? masterPath;
    private string? catalogPath;
    private ReviewReference? selectedReference;
    private ProjectionSong? selectedSong;
    private string songSearch = "";
    private string reviewReason = "";
    private string reviewNote = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProjectionSong> Songs { get; } = [];
    public ObservableCollection<ReviewReference> ReviewReferences { get; } = [];
    public ObservableCollection<string> CoverageStatusOptions { get; } =
        ["all", "referenced", "needs_review", "uncollected", "unresolved", "orphaned"];
    public ObservableCollection<string> ReasonOptions { get; } = ["all"];
    public ObservableCollection<ProjectionSong> SongChoices { get; } = [];

    public string MasterVersion => projection?.Master.MasterVersion ?? "未選択";
    public string MasterSourceHash => projection?.Master.SourceHash ?? "—";
    public string MasterCounts => projection is null
        ? "—"
        : $"songs: {projection.Master.SongCount} / charts: {projection.Master.ChartCount} / GP: {projection.Master.GrandPrixSongCount}";
    public string CatalogIdentity => projection?.Catalog.CatalogIdentity ?? "未選択";
    public string CatalogSchema => projection is null ? "—" : $"v{projection.Catalog.SchemaVersion}";
    public string CoverageSummary => projection is null
        ? "—"
        : string.Join(
            " / ",
            new[] { "referenced", "needs_review", "uncollected", "unresolved" }.Select(
                status => $"{status}: {projection.Coverage.StatusCounts.GetValueOrDefault(status)}"));
    public string OrphanSummary => projection is null
        ? "—"
        : $"orphan: {projection.Coverage.OrphanedReferenceCount}, 未割当 unresolved: {projection.Coverage.UnassignedUnresolvedObservationCount}";
    public string MutationCapability => projection?.Catalog.MutationCapability
        ?? (projection?.ProjectionSchemaVersion == 1 ? "read_only" : "未選択");
    public bool MigrationRequired => projection?.Catalog.MigrationRequired == true;

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
        private set => SetField(ref isBusy, value);
    }

    public async Task LoadProjectionAsync(
        string masterPath,
        string catalogPath,
        CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusTitle = "読込中";
        StatusMessage = "master と catalog を read-only で検証しています。";
        try
        {
            var loadedProjection = await projectionService.LoadAsync(
                masterPath,
                catalogPath,
                cancellationToken);
            projection = loadedProjection;
            this.masterPath = Path.GetFullPath(masterPath);
            this.catalogPath = Path.GetFullPath(catalogPath);
            RebuildFilterOptions();
            ApplyFilters();
            ApplySongSearch();
            NotifyProjectionProperties();
            StatusTitle = "読込完了";
            StatusMessage = $"GP対象 {projection.Coverage.GrandPrixSongCount} 曲を表示しました。";
        }
        catch (OperationCanceledException)
        {
            ClearProjection();
            StatusTitle = "取消";
            StatusMessage = "読込を取り消しました。DBは変更していません。";
            throw;
        }
        catch (Exception exception)
        {
            ClearProjection();
            StatusTitle = "読込失敗";
            StatusMessage = exception.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task MigrateCatalogAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        if (reviewWorkflowService is null || catalogPath is null || masterPath is null)
        {
            throw new InvalidOperationException("移行元catalogとmasterを先に読み込んでください。");
        }
        if (!MigrationRequired)
        {
            throw new InvalidOperationException("選択中catalogはv1移行対象ではありません。");
        }
        IsBusy = true;
        StatusTitle = "catalog移行中";
        StatusMessage = "v1を保持したままv2 stagingを検証しています。";
        try
        {
            await reviewWorkflowService.MigrateAsync(catalogPath, targetPath, cancellationToken);
            await LoadProjectionCoreAsync(masterPath, targetPath, cancellationToken);
            StatusTitle = "catalog移行完了";
            StatusMessage = "v1を変更せずv2 catalogを公開し、v2を読み込みました。";
        }
        catch (Exception exception)
        {
            StatusTitle = exception is OperationCanceledException ? "catalog移行取消" : "catalog移行失敗";
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
        if (reviewWorkflowService is null || projection is null || masterPath is null || catalogPath is null)
        {
            throw new InvalidOperationException("v2 catalogを先に読み込んでください。");
        }
        if (projection.Catalog.MutationCapability != "manual_review_v2"
            || SelectedReference?.Revision is null
            || SelectedReference.StoredStatus is null)
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
            SelectedReference.Revision.Value,
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
                masterPath, catalogPath, mutation, cancellationToken);
            await LoadProjectionCoreAsync(masterPath, catalogPath, cancellationToken);
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

    private async Task LoadProjectionCoreAsync(
        string masterPathValue,
        string catalogPathValue,
        CancellationToken cancellationToken)
    {
        projection = await projectionService.LoadAsync(
            masterPathValue, catalogPathValue, cancellationToken);
        masterPath = Path.GetFullPath(masterPathValue);
        catalogPath = Path.GetFullPath(catalogPathValue);
        RebuildFilterOptions();
        ApplyFilters();
        ApplySongSearch();
        NotifyProjectionProperties();
    }

    public async Task UpdateMasterAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusTitle = "master更新中";
        StatusMessage = "staging生成とinspectionを実行しています。";
        try
        {
            var result = await masterUpdateService.UpdateAsync(targetPath, cancellationToken);
            StatusTitle = "master更新完了";
            StatusMessage = result.Before is null
                ? $"新規 master {FormatSummary(result.After)} を公開しました。"
                : $"before [{FormatSummary(result.Before)}] → after [{FormatSummary(result.After)}]";
        }
        catch (OperationCanceledException)
        {
            StatusTitle = "master更新取消";
            StatusMessage = "更新を取り消しました。既存masterは変更していません。";
            throw;
        }
        catch (Exception exception)
        {
            StatusTitle = "master更新失敗";
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
                         && (SelectedReason == "all" || reference.Reason == SelectedReason)))
        {
            ReviewReferences.Add(reference);
        }
    }

    private void ApplySongSearch()
    {
        SongChoices.Clear();
        if (projection is null)
        {
            return;
        }
        foreach (var song in projection.Songs.Where(song =>
                     string.IsNullOrWhiteSpace(SongSearch)
                     || song.SongId.Contains(SongSearch, StringComparison.OrdinalIgnoreCase)
                     || song.Title.Contains(SongSearch, StringComparison.OrdinalIgnoreCase)
                     || song.Artist.Contains(SongSearch, StringComparison.OrdinalIgnoreCase)))
        {
            SongChoices.Add(song);
        }
    }

    private void NotifyProjectionProperties()
    {
        OnPropertyChanged(nameof(MasterVersion));
        OnPropertyChanged(nameof(MasterSourceHash));
        OnPropertyChanged(nameof(MasterCounts));
        OnPropertyChanged(nameof(CatalogIdentity));
        OnPropertyChanged(nameof(CatalogSchema));
        OnPropertyChanged(nameof(CoverageSummary));
        OnPropertyChanged(nameof(OrphanSummary));
        OnPropertyChanged(nameof(MutationCapability));
        OnPropertyChanged(nameof(MigrationRequired));
    }

    private void ClearProjection()
    {
        projection = null;
        Songs.Clear();
        ReviewReferences.Clear();
        SongChoices.Clear();
        SelectedReference = null;
        SelectedSong = null;
        masterPath = null;
        catalogPath = null;
        ReasonOptions.Clear();
        ReasonOptions.Add("all");
        SelectedCoverageStatus = "all";
        SelectedReason = "all";
        NotifyProjectionProperties();
    }

    private static string FormatSummary(MasterSummary summary) =>
        $"version={summary.MasterVersion}, hash={summary.SourceHash}, songs={summary.SongCount}, charts={summary.ChartCount}, GP={summary.GrandPrixSongCount}";

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
