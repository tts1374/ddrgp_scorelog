using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JacketCatalogCollector;

public sealed class MainViewModel(
    IMasterUpdateService masterUpdateService,
    IProjectionService projectionService) : INotifyPropertyChanged
{
    private ReviewProjection? projection;
    private string selectedCoverageStatus = "all";
    private string selectedReason = "all";
    private string statusTitle = "準備完了";
    private string statusMessage = "master DB と jacket catalog を選択してください。";
    private bool isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProjectionSong> Songs { get; } = [];
    public ObservableCollection<ReviewReference> ReviewReferences { get; } = [];
    public ObservableCollection<string> CoverageStatusOptions { get; } =
        ["all", "referenced", "needs_review", "uncollected", "unresolved", "orphaned"];
    public ObservableCollection<string> ReasonOptions { get; } = ["all"];

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
            RebuildFilterOptions();
            ApplyFilters();
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

    private void NotifyProjectionProperties()
    {
        OnPropertyChanged(nameof(MasterVersion));
        OnPropertyChanged(nameof(MasterSourceHash));
        OnPropertyChanged(nameof(MasterCounts));
        OnPropertyChanged(nameof(CatalogIdentity));
        OnPropertyChanged(nameof(CatalogSchema));
        OnPropertyChanged(nameof(CoverageSummary));
        OnPropertyChanged(nameof(OrphanSummary));
    }

    private void ClearProjection()
    {
        projection = null;
        Songs.Clear();
        ReviewReferences.Clear();
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
