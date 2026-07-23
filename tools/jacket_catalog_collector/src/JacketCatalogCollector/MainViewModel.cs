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
    CollectorDatabasePaths? databasePaths = null,
    ICatalogInitializationService? catalogInitializationService = null,
    IManualReviewDraftStore? manualReviewDraftStore = null) : INotifyPropertyChanged
{
    private readonly CollectorDatabasePaths fixedDatabasePaths =
        databasePaths ?? CollectorDatabasePaths.Resolve();
    private readonly ICatalogInitializationService catalogInitializer =
        catalogInitializationService ?? CreateCatalogInitializer(databasePaths);
    private ReviewProjection? projection;
    private readonly Dictionary<string, ManualReviewDraft> manualReviewDrafts =
        new(StringComparer.Ordinal);
    private string selectedCoverageStatus = "all";
    private string selectedReason = "all";
    private string statusTitle = "準備完了";
    private string statusMessage = "固定pathの曲情報DBとジャケット情報DBを確認します。";
    private bool isBusy;
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
    public string? CurrentMasterPath => projection is null ? null : fixedDatabasePaths.MasterPath;
    public string? CurrentCatalogPath => projection is null ? null : fixedDatabasePaths.CatalogPath;
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

    public async Task InitializeDatabasesAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("別の処理を実行中です。");
        }

        IsBusy = true;
        StatusTitle = "DB確認中";
        StatusMessage = "固定pathの曲情報DBとジャケット情報DBをread-onlyで検証しています。";
        try
        {
            await InitializeDatabasesCoreAsync(cancellationToken);
            if (projection is not null)
            {
                StatusTitle = "読込完了";
                StatusMessage =
                    $"固定DBからGP対象 {projection.Coverage.GrandPrixSongCount} 曲を表示しました。";
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
        StatusTitle = "読込中";
        StatusMessage = "固定pathのmasterとcatalogをread-onlyで検証しています。";
        try
        {
            await LoadProjectionCoreAsync(cancellationToken);
            StatusTitle = "読込完了";
            StatusMessage = $"固定DBからGP対象 {projection!.Coverage.GrandPrixSongCount} 曲を表示しました。";
        }
        catch (OperationCanceledException)
        {
            ClearProjection();
            StatusTitle = "読込取消";
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

    private Task LoadProjectionCoreAsync(CancellationToken cancellationToken) =>
        LoadProjectionCoreAsync(
            fixedDatabasePaths.MasterPath,
            fixedDatabasePaths.CatalogPath,
            cancellationToken);

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

        try
        {
            await masterUpdateService.InspectAsync(
                fixedDatabasePaths.MasterPath,
                cancellationToken);
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
                StatusTitle = "ジャケット情報初期化中";
                StatusMessage = "current schemaの空catalogを固定pathへ作成しています。";
                await catalogInitializer.EnsureCreatedAsync(cancellationToken);
                catalogCreated = true;
            }

            StatusTitle = "ジャケット情報確認中";
            StatusMessage = "固定pathのcatalogをstrict read-only projectionで検証しています。";
            await LoadProjectionCoreAsync(cancellationToken);
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
        IsBusy = true;
        StatusTitle = "master更新中";
        StatusMessage = "staging生成とinspectionを実行しています。";
        var projectionReloadFailed = false;
        try
        {
            var result = await masterUpdateService.UpdateAsync(
                fixedDatabasePaths.MasterPath,
                cancellationToken);
            try
            {
                await InitializeDatabasesCoreAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ClearProjection();
                projectionReloadFailed = true;
                StatusTitle = "master更新後の再読込取消";
                StatusMessage = "masterは更新済みですが、projection再読込を取り消しました。";
                throw;
            }
            catch (Exception exception)
            {
                ClearProjection();
                projectionReloadFailed = true;
                StatusTitle = "master更新後の再読込失敗";
                StatusMessage =
                    $"masterは更新済みですが、catalog/projectionを再読込できません: {exception.Message}";
                throw;
            }
            StatusTitle = "master更新完了";
            StatusMessage = result.Before is null
                ? $"新規 master {FormatSummary(result.After)} を公開しました。"
                : $"before [{FormatSummary(result.Before)}] → after [{FormatSummary(result.After)}]";
        }
        catch (OperationCanceledException)
        {
            if (projectionReloadFailed)
            {
                throw;
            }
            StatusTitle = "master更新取消";
            StatusMessage = "更新を取り消しました。既存masterは変更していません。";
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
        if (Observation is null || projection is null)
        {
            throw new InvalidOperationException(
                "master/catalogを先に読み込み、window候補を明示選択してください。");
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
                "master/catalogを先に読み込み、window候補を明示選択してください。");
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
            if (stopFailure is not null)
            {
                StatusTitle = "収集終了・停止処理失敗";
                StatusMessage =
                    $"停止/checkpoint処理: {stopFailure.Message} / projection再読込: "
                    + projectionReloadMessage;
            }
            else if (summary?.IsRejected == true)
            {
                StatusTitle = projectionReloaded
                    ? "収集終了・catalog retry拒否"
                    : "収集終了・catalog retry拒否/projection再読込失敗";
                StatusMessage =
                    $"catalog retry: {summary.DisplayMessage} / projection再読込: "
                    + projectionReloadMessage;
            }
            else
            {
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
                await LoadProjectionCoreAsync(cancellationToken);
                StatusTitle = "catalog retry・projection再読込完了";
                StatusMessage =
                    $"catalog retry: {summary.DisplayMessage} / projection再読込: 成功";
            }
            catch (OperationCanceledException exception)
            {
                ClearProjection();
                StatusTitle = "catalog retry・projection再読込取消";
                StatusMessage =
                    $"catalog retry: {summary.DisplayMessage} / projection再読込: 取消: {exception.Message}";
                throw;
            }
            catch (Exception exception)
            {
                ClearProjection();
                StatusTitle = "catalog retry・projection再読込失敗";
                StatusMessage =
                    $"catalog retry: {summary.DisplayMessage} / projection再読込: 失敗: {exception.Message}";
                throw;
            }
        }
        catch (Exception exception) when (StatusTitle == "catalog retry中")
        {
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
        OnPropertyChanged(nameof(CurrentMasterPath));
        OnPropertyChanged(nameof(CurrentCatalogPath));
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
