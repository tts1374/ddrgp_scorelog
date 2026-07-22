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
    ICatalogInitializationService? catalogInitializationService = null) : INotifyPropertyChanged
{
    private readonly CollectorDatabasePaths fixedDatabasePaths =
        databasePaths ?? CollectorDatabasePaths.Resolve();
    private readonly ICatalogInitializationService catalogInitializer =
        catalogInitializationService ?? CreateCatalogInitializer(databasePaths);
    private ReviewProjection? projection;
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

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProjectionSong> Songs { get; } = [];
    public WindowCaptureViewModel? WindowCapture { get; } = windowCapture;
    public JacketObservationViewModel? Observation { get; } = observation;
    public ObservableCollection<ReviewReference> ReviewReferences { get; } = [];
    public ObservableCollection<string> CoverageStatusOptions { get; } =
        ["all", "referenced", "needs_review", "uncollected", "unresolved", "orphaned"];
    public ObservableCollection<string> ReasonOptions { get; } = ["all"];
    public ObservableCollection<ProjectionSong> SongChoices { get; } = [];
    public ObservableCollection<string> CandidateClassificationOptions { get; } = ["all"];
    public string? CurrentMasterPath => projection is null ? null : fixedDatabasePaths.MasterPath;
    public string? CurrentCatalogPath => projection is null ? null : fixedDatabasePaths.CatalogPath;

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

    private async Task LoadProjectionCoreAsync(CancellationToken cancellationToken)
    {
        projection = await projectionService.LoadAsync(
            fixedDatabasePaths.MasterPath,
            fixedDatabasePaths.CatalogPath,
            cancellationToken);
        RebuildFilterOptions();
        ApplyFilters();
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

        try
        {
            if (!File.Exists(fixedDatabasePaths.CatalogPath))
            {
                StatusTitle = "ジャケット情報初期化中";
                StatusMessage = "current schemaの空catalogを固定pathへ作成しています。";
                await catalogInitializer.EnsureCreatedAsync(cancellationToken);
            }

            StatusTitle = "ジャケット情報確認中";
            StatusMessage = "固定pathのcatalogをstrict read-only projectionで検証しています。";
            await LoadProjectionCoreAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
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
                projectionReloadFailed = true;
                StatusTitle = "master更新後の再読込取消";
                StatusMessage = "masterは更新済みですが、projection再読込を取り消しました。";
                throw;
            }
            catch (Exception exception)
            {
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
    }

    private void ClearProjection()
    {
        projection = null;
        Songs.Clear();
        ReviewReferences.Clear();
        SongChoices.Clear();
        SelectedReference = null;
        SelectedSong = null;
        ReasonOptions.Clear();
        ReasonOptions.Add("all");
        CandidateClassificationOptions.Clear();
        CandidateClassificationOptions.Add("all");
        SelectedCoverageStatus = "all";
        SelectedReason = "all";
        SelectedCandidateClassification = "all";
        NotifyProjectionProperties();
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
