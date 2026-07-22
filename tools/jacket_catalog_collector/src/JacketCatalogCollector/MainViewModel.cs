using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace JacketCatalogCollector;

public sealed class MainViewModel(
    IMasterUpdateService masterUpdateService,
    IProjectionService projectionService,
    IReviewWorkflowService? reviewWorkflowService = null,
    WindowCaptureViewModel? windowCapture = null,
    JacketObservationViewModel? observation = null,
    ICollectorDatabasePathStore? databasePathStore = null,
    IManualReviewDraftStore? manualReviewDraftStore = null) : INotifyPropertyChanged
{
    private ReviewProjection? projection;
    private readonly Dictionary<string, ManualReviewDraft> manualReviewDrafts =
        new(StringComparer.Ordinal);
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
    private string selectedCandidateClassification = "all";
    private ManualReviewDraftRow? selectedManualReviewRow;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProjectionSong> Songs { get; } = [];
    public WindowCaptureViewModel? WindowCapture { get; } = windowCapture;
    public JacketObservationViewModel? Observation { get; } = observation;
    public ObservableCollection<ReviewReference> ReviewReferences { get; } = [];
    public ObservableCollection<ManualReviewDraftRow> ManualReviewRows { get; } = [];
    public ObservableCollection<string> CoverageStatusOptions { get; } =
        ["all", "referenced", "needs_review", "uncollected", "unresolved", "orphaned"];
    public ObservableCollection<string> ReasonOptions { get; } = ["all"];
    public ObservableCollection<ProjectionSong> SongChoices { get; } = [];
    public ObservableCollection<string> CandidateClassificationOptions { get; } = ["all"];
    public string? CurrentMasterPath => masterPath;
    public string? CurrentCatalogPath => catalogPath;
    public string ManualReviewSummary =>
        $"未反映 {ManualReviewRows.Count} 行 / 保存済み {ManualReviewRows.Count(row => row.IsSaved)} 件"
        + $" / 未保存 {ManualReviewRows.Count(row => !row.IsSaved)} 件";

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
            await LoadProjectionCoreAsync(masterPath, catalogPath, cancellationToken);
            StatusTitle = "読込完了";
            StatusMessage = $"GP対象 {projection!.Coverage.GrandPrixSongCount} 曲を表示しました。";
            if (databasePathStore is not null)
            {
                try
                {
                    await databasePathStore.SaveAsync(
                        new CollectorDatabasePaths(this.masterPath!, this.catalogPath!),
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    StatusTitle = "読込完了（path記憶失敗）";
                    StatusMessage =
                        $"DBはread-onlyで読込済みです。pathを記憶できませんでした: {exception.Message}";
                }
            }
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

    public async Task InitializeRememberedProjectionAsync(
        CancellationToken cancellationToken = default)
    {
        if (databasePathStore is null)
        {
            return;
        }
        if (IsBusy)
        {
            throw new InvalidOperationException("別の処理を実行中です。");
        }

        IsBusy = true;
        StatusTitle = "前回DBを確認中";
        StatusMessage = "記憶したmaster/catalogをread-onlyで再検証しています。";
        try
        {
            var remembered = await databasePathStore.LoadAsync(cancellationToken);
            if (remembered is null)
            {
                StatusTitle = "準備完了";
                StatusMessage = "master DB と jacket catalog を選択してください。";
                return;
            }
            await LoadProjectionCoreAsync(
                remembered.MasterPath,
                remembered.CatalogPath,
                cancellationToken);
            StatusTitle = "前回DBを自動読込";
            StatusMessage =
                $"記憶したmaster/catalogをread-onlyで再検証し、GP対象 {projection!.Coverage.GrandPrixSongCount} 曲を表示しました。";
        }
        catch (OperationCanceledException)
        {
            ClearProjection();
            StatusTitle = "自動読込取消";
            StatusMessage = "自動読込を取り消しました。DBと保存pathは変更していません。";
            throw;
        }
        catch (Exception exception)
        {
            ClearProjection();
            StatusTitle = "前回DBの自動読込失敗";
            StatusMessage =
                $"保存pathは保持しています。master/catalogを手動選択してください: {exception.Message}";
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
            OnPropertyChanged(nameof(ManualReviewSummary));
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

    private async Task LoadProjectionCoreAsync(
        string masterPathValue,
        string catalogPathValue,
        CancellationToken cancellationToken)
    {
        var loadedProjection = await projectionService.LoadAsync(
            masterPathValue, catalogPathValue, cancellationToken);
        var loadedDrafts = manualReviewDraftStore is null
            ? new Dictionary<string, ManualReviewDraft>(StringComparer.Ordinal)
            : (await manualReviewDraftStore.LoadAsync(cancellationToken))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        projection = loadedProjection;
        manualReviewDrafts.Clear();
        foreach (var draft in loadedDrafts)
        {
            manualReviewDrafts[draft.Key] = draft.Value;
        }
        masterPath = Path.GetFullPath(masterPathValue);
        catalogPath = Path.GetFullPath(catalogPathValue);
        RebuildFilterOptions();
        ApplyFilters();
        ApplyManualReviewRows();
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

    public Task StartObservationSessionAsync(
        WindowCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (Observation is null || projection is null || masterPath is null || catalogPath is null)
        {
            throw new InvalidOperationException(
                "master/catalogを先に読み込み、window候補を明示選択してください。");
        }
        return Observation.StartSessionAsync(
            projection.Master,
            projection.Catalog,
            candidate,
            masterPath,
            catalogPath,
            cancellationToken);
    }

    public Task ResumeObservationSessionAsync(
        WindowCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (Observation is null || projection is null || masterPath is null || catalogPath is null)
        {
            throw new InvalidOperationException(
                "master/catalogを先に読み込み、window候補を明示選択してください。");
        }
        return Observation.ResumeSessionAsync(
            projection.Master,
            projection.Catalog,
            candidate,
            masterPath,
            catalogPath,
            cancellationToken);
    }

    public Task StopObservationSessionAsync(CancellationToken cancellationToken = default) =>
        Observation?.StopAsync(cancellationToken) ?? Task.CompletedTask;

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
        SelectedManualReviewRow = null;
        if (projection is null)
        {
            OnPropertyChanged(nameof(ManualReviewSummary));
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
        OnPropertyChanged(nameof(ManualReviewSummary));
    }

    private static bool IsUnreflectedReviewTarget(ReviewReference reference) =>
        reference.StoredStatus is not ("auto_confirmed" or "manual_confirmed" or "rejected");

    private void ManualReviewRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ManualReviewDraftRow.IsSaved))
        {
            OnPropertyChanged(nameof(ManualReviewSummary));
        }
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
        UnsubscribeManualReviewRows();
        ManualReviewRows.Clear();
        SongChoices.Clear();
        SelectedReference = null;
        SelectedSong = null;
        SelectedManualReviewRow = null;
        manualReviewDrafts.Clear();
        masterPath = null;
        catalogPath = null;
        ReasonOptions.Clear();
        ReasonOptions.Add("all");
        CandidateClassificationOptions.Clear();
        CandidateClassificationOptions.Add("all");
        SelectedCoverageStatus = "all";
        SelectedReason = "all";
        SelectedCandidateClassification = "all";
        OnPropertyChanged(nameof(ManualReviewSummary));
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
