using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JacketCatalogCollector;

public sealed class JacketObservationViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly WindowCaptureViewModel capture;
    private readonly JacketObservationSession session;
    private readonly ICaptureDispatcher dispatcher;
    private readonly InformationTitleLineDetector informationDetector;
    private readonly object frameSync = new();
    private Task frameTail = Task.CompletedTask;
    private RawCaptureFrame? pendingFrame;
    private bool frameWorkerRunning;
    private long observationDroppedFrameCount;
    private long captureDroppedFrameCount;
    private long persistedDroppedFrameCount;
    private JacketObservationCandidate? stableCandidate;
    private JacketDetectionResult detection = new(
        JacketDetectionState.NoFrame, null, "session is not started", 0, 0, 0);
    private InformationTitleLineDetectionResult informationDetection =
        InformationTitleLineDetectionResult.Empty("session is not started");
    private string statusTitle = "観測session未開始";
    private string statusMessage = "capture開始前に明示的に観測sessionを作成します。";
    private string? sessionId;
    private string resumeSessionId = "";
    private string? lastCatalogReceipt;
    private bool captureEnded = true;
    private bool autoSaveEnabled;
    private readonly HashSet<string> catalogSavedIdentityKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> autoSaveAttemptedIdentityKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ObservationSavePreflightDisposition> savePreflightByIdentity =
        new(StringComparer.Ordinal);

    public JacketObservationViewModel(
        WindowCaptureViewModel capture,
        JacketObservationSession session,
        ICaptureDispatcher dispatcher,
        InformationTitleLineDetector? informationDetector = null)
    {
        this.capture = capture;
        this.session = session;
        this.dispatcher = dispatcher;
        this.informationDetector = informationDetector ?? new InformationTitleLineDetector();
        capture.FrameReceived += OnFrameReceived;
        capture.LifecycleChanged += OnLifecycleChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public JacketDetectionResult Detection
    {
        get => detection;
        private set
        {
            if (SetField(ref detection, value))
            {
                OnPropertyChanged(nameof(DetectorState));
                OnPropertyChanged(nameof(DetectorProgress));
                OnPropertyChanged(nameof(StableCandidate));
                OnPropertyChanged(nameof(CanAdopt));
                OnPropertyChanged(nameof(CollectionStateTitle));
                OnPropertyChanged(nameof(CollectionStateMessage));
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

    public string SessionId => sessionId ?? "未開始";
    public string ResumeSessionId
    {
        get => resumeSessionId;
        set
        {
            if (SetField(ref resumeSessionId, value))
            {
                OnPropertyChanged(nameof(CanResume));
            }
        }
    }
    public string DetectorState => Detection.State.ToString();
    public string DetectorProgress =>
        $"frames={Detection.ProcessedFrameCount} / invalid={Detection.InvalidFrameCount} "
        + $"/ duplicate={Detection.DuplicatePreviewCount} / observation_drop={observationDroppedFrameCount}";
    public InformationTitleLineDetectionResult InformationDetection
    {
        get => informationDetection;
        private set
        {
            if (SetField(ref informationDetection, value))
            {
                OnPropertyChanged(nameof(InformationPanelDisplay));
                OnPropertyChanged(nameof(InformationTitleLineStability));
                OnPropertyChanged(nameof(InformationTitleLineHash));
                OnPropertyChanged(nameof(InformationDiagnostic));
            }
        }
    }
    public string InformationPanelDisplay => InformationDetection.IsDisplayed ? "表示あり" : "表示なし";
    public string InformationTitleLineStability => InformationDetection.IsStable
        ? "安定"
        : InformationDetection.IsDisplayed ? "確認中" : "—";
    public string InformationTitleLineHash => InformationDetection.TitleLineHash ?? "—";
    public string InformationDiagnostic =>
        $"{InformationDetection.DetectorVersion} / {InformationDetection.FeatureVersion}: "
        + InformationDetection.Diagnostic;
    public string StableCandidate => stableCandidate is null
        ? "—"
        : $"jacket={stableCandidate.FeatureHash}, title={stableCandidate.TitleLineFeature?.FeatureHash ?? "—"}, "
            + $"composite={stableCandidate.CompositeIdentity?.IdentityHash ?? "—"}, "
            + $"stable={stableCandidate.StableFrameCount} frames / {stableCandidate.StableDuration.TotalMilliseconds:0}ms";
    public string LastCatalogReceipt => lastCatalogReceipt ?? "未投入";
    public bool IsActive => session.IsActive;
    public bool AutoSaveEnabled
    {
        get => autoSaveEnabled;
        set
        {
            if (SetField(ref autoSaveEnabled, value))
            {
                OnPropertyChanged(nameof(CollectionStateMessage));
            }
        }
    }
    public bool CanConfigureAutoSave => !captureEnded && session.IsActive;
    public bool CanAdopt => !captureEnded
        && session.HasAdoptableCandidate
        && stableCandidate is not null
        && Detection.State is JacketDetectionState.StableCandidate
            or JacketDetectionState.DuplicatePreview
        && (!session.RequiresCompositeIdentity || stableCandidate.CompositeIdentity is not null)
        && !IsSaved(CandidateIdentityKey(stableCandidate));
    public bool CanResume => !session.IsActive && ResumeSessionId.Trim().Length > 0;
    public string CollectionStateTitle
    {
        get
        {
            if (captureEnded)
            {
                return "収集は停止中";
            }
            if (Detection.State == JacketDetectionState.ChangeCandidate)
            {
                return "ジャケットを確認中";
            }
            if (stableCandidate is not null
                && IsSaved(CandidateIdentityKey(stableCandidate)))
            {
                return "このジャケットは保存済み";
            }
            if (stableCandidate is not null
                && session.RequiresCompositeIdentity
                && stableCandidate.CompositeIdentity is null)
            {
                return "曲名行を確認中";
            }
            if (stableCandidate is not null)
            {
                return "新しいジャケットを検出";
            }
            return "DDR GPの曲選択画面を待っています";
        }
    }

    public string CollectionStateMessage
    {
        get
        {
            if (captureEnded)
            {
                return "DDR GPのウィンドウを選び、収集を開始してください。";
            }
            if (Detection.State == JacketDetectionState.ChangeCandidate)
            {
                return "同じ曲をそのまま表示してください。安定すると保存できるようになります。";
            }
            if (stableCandidate is not null
                && IsSaved(CandidateIdentityKey(stableCandidate)))
            {
                return "DDR GPで別の曲へ移動してください。新しいジャケットを自動で確認します。";
            }
            if (stableCandidate is not null
                && session.RequiresCompositeIdentity
                && stableCandidate.CompositeIdentity is null)
            {
                return "同じframeの安定したINFORMATION曲名行が確認できるまで保存できません。";
            }
            if (stableCandidate is not null)
            {
                if (AutoSaveEnabled)
                {
                    return "current checkpointとcatalog identity集合を再照合して自動保存します。";
                }
                return "内容を確認して「このジャケットを保存」を押してください。";
            }
            return "DDR GPで曲選択画面を表示してください。";
        }
    }

    public async Task StartSessionAsync(
        ProjectionMaster master,
        ProjectionCatalog catalog,
        WindowCandidate candidate,
        string masterPath,
        string catalogPath,
        CancellationToken cancellationToken = default)
    {
        if (catalog.CatalogIdentity != "ddrgp-local-jacket-reference-catalog")
        {
            throw new InvalidOperationException("catalog identity is unsupported for observation session");
        }
        var identity = new ObservationSessionIdentity(
            Guid.NewGuid().ToString("N"),
            master.MasterVersion,
            master.SourceHash,
            catalog.CatalogIdentity,
            catalog.SchemaVersion,
            catalog.CurrentFeatureExtractorVersion,
            JacketObservationVersions.Detector,
            JacketObservationVersions.Roi,
            candidate.Identity,
            DateTimeOffset.UtcNow,
            JacketObservationVersions.FrameClock,
            catalog.CreatedAt);
        await session.StartAsync(
            identity,
            masterPath,
            catalogPath,
            cancellationToken,
            requireCompositeIdentity: true);
        ResetFrameQueueForStart();
        Detection = session.LastDetection;
        sessionId = identity.SessionId;
        lastCatalogReceipt = null;
        ResetSavePreflightState();
        StatusTitle = "観測session開始";
        StatusMessage =
            "自動保存は既定OFFです。明示的に有効化したsessionだけ保存前照合後に自動保存します。";
        OnPropertyChanged(nameof(SessionId));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CanAdopt));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanConfigureAutoSave));
    }

    public async Task ResumeSessionAsync(
        ProjectionMaster master,
        ProjectionCatalog catalog,
        WindowCandidate candidate,
        string masterPath,
        string catalogPath,
        CancellationToken cancellationToken = default)
    {
        var requestedSessionId = ResumeSessionId.Trim();
        if (requestedSessionId.Length == 0)
        {
            throw new InvalidOperationException("再開するsession IDを入力してください。");
        }
        var result = await session.ResumeAsync(
            new ObservationResumeRequest(
                requestedSessionId,
                master.MasterVersion,
                master.SourceHash,
                catalog.CatalogIdentity,
                catalog.SchemaVersion,
                catalog.CurrentFeatureExtractorVersion,
                JacketObservationVersions.Detector,
                JacketObservationVersions.Roi,
                candidate.Identity,
                JacketObservationVersions.FrameClock,
                catalog.CreatedAt),
            masterPath,
            catalogPath,
            cancellationToken);
        if (!result.Compatible || result.Checkpoint is null)
        {
            throw new InvalidOperationException(result.Message);
        }
        ResetFrameQueueForStart(result.Checkpoint.DroppedFrameCount);
        Detection = session.LastDetection;
        sessionId = result.Checkpoint.Session.SessionId;
        lastCatalogReceipt = null;
        ResetSavePreflightState();
        StatusTitle = "観測session再開";
        StatusMessage = result.Message;
        OnPropertyChanged(nameof(SessionId));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CanAdopt));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanConfigureAutoSave));
    }

    public async Task<ObservationAdoptionResult> AdoptAsync(
        CancellationToken cancellationToken = default)
    {
        if (captureEnded)
        {
            throw new InvalidOperationException("capture終了後の候補は採用できません。");
        }
        try
        {
            var result = await session.AdoptLastStableAsync(cancellationToken);
            ApplyAdoptionResult(result, automatic: false);
            return result;
        }
        catch (ObservationAlreadyCatalogedException exception)
        {
            MarkCatalogExisting(exception.IdentityHash);
            throw;
        }
    }

    public async Task<IReadOnlyList<ObservationAdoptionResult>> RetryCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        var results = await session.RetryPendingCatalogAsync(cancellationToken);
        lastCatalogReceipt = results.Count == 0
            ? "retry対象なし"
            : string.Join("; ", results.Select(result => result.Catalog.Message));
        StatusTitle = "catalog retry完了";
        StatusMessage = lastCatalogReceipt;
        OnPropertyChanged(nameof(LastCatalogReceipt));
        return results;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task pendingFrames;
        lock (frameSync)
        {
            captureEnded = true;
            pendingFrames = frameTail;
        }
        AutoSaveEnabled = false;
        try
        {
            await pendingFrames.WaitAsync(cancellationToken);
            long droppedFrameCount;
            lock (frameSync)
            {
                droppedFrameCount = checked(
                    persistedDroppedFrameCount
                    + captureDroppedFrameCount
                    + observationDroppedFrameCount);
            }
            await session.UpdateDroppedFrameCountAsync(droppedFrameCount, cancellationToken);
            await session.StopAsync(cancellationToken);
            StatusTitle = "観測session停止";
            StatusMessage = "停止後frameはdetector/artifact/catalogへ渡しません。";
        }
        finally
        {
            var hadStableCandidate = stableCandidate is not null;
            stableCandidate = null;
            informationDetector.Reset();
            InformationDetection = InformationTitleLineDetectionResult.Empty("capture is stopped");
            Detection = session.LastDetection;
            if (hadStableCandidate)
            {
                OnPropertyChanged(nameof(StableCandidate));
            }
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(CanAdopt));
            OnPropertyChanged(nameof(CanResume));
            OnPropertyChanged(nameof(CanConfigureAutoSave));
            OnPropertyChanged(nameof(CollectionStateTitle));
            OnPropertyChanged(nameof(CollectionStateMessage));
        }
    }

    public ValueTask DisposeAsync() => session.DisposeAsync();

    private void OnFrameReceived(object? sender, RawCaptureFrame frame)
    {
        lock (frameSync)
        {
            if (captureEnded || !session.IsActive)
            {
                return;
            }
            if (pendingFrame is not null)
            {
                observationDroppedFrameCount++;
            }
            pendingFrame = frame;
            if (!frameWorkerRunning)
            {
                frameWorkerRunning = true;
                frameTail = Task.Run(ProcessFrameLoopAsync);
            }
        }
    }

    private async Task ProcessFrameLoopAsync()
    {
        while (true)
        {
            RawCaptureFrame? frame;
            lock (frameSync)
            {
                frame = pendingFrame;
                pendingFrame = null;
                if (frame is null)
                {
                    frameWorkerRunning = false;
                    return;
                }
            }
            await ProcessFrameAsync(frame);
        }
    }

    private async Task ProcessFrameAsync(RawCaptureFrame frame)
    {
        InformationTitleLineDetectionResult? information = null;
        try
        {
            information = informationDetector.Observe(frame);
            var value = await session.ObserveFrameAsync(frame, information: information);
            await dispatcher.InvokeAsync(() =>
            {
                if (value.HasStableCandidate && value.Candidate is not null)
                {
                    stableCandidate = value.Candidate;
                }
                InformationDetection = information;
                Detection = value;
                StatusTitle = value.HasStableCandidate
                    ? "安定候補（明示採用待ち）"
                    : "jacket detector";
                StatusMessage = value.Diagnostic;
            });
            if (value.HasStableCandidate && value.Candidate?.CompositeIdentity is not null)
            {
                await InspectAndMaybeAutoSaveAsync(value.Candidate);
            }
        }
        catch (Exception exception)
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (information is not null)
                {
                    InformationDetection = information;
                }
                StatusTitle = "detector失敗";
                StatusMessage = exception.Message;
            });
        }
    }

    private async void OnLifecycleChanged(object? sender, CaptureLifecycleSnapshot value)
    {
        var stopping = value.State == CaptureLifecycleState.Stopping;
        var terminal = value.State is CaptureLifecycleState.Stopped
            or CaptureLifecycleState.Failed;
        lock (frameSync)
        {
            captureDroppedFrameCount = Math.Max(captureDroppedFrameCount, value.DroppedCount);
        }
        if (stopping || terminal)
        {
            lock (frameSync)
            {
                captureEnded = true;
            }
            AutoSaveEnabled = false;
            OnPropertyChanged(nameof(CanAdopt));
            OnPropertyChanged(nameof(CanConfigureAutoSave));
            OnPropertyChanged(nameof(CollectionStateTitle));
            OnPropertyChanged(nameof(CollectionStateMessage));
        }
        try
        {
            if (session.IsActive)
            {
                long droppedFrameCount;
                lock (frameSync)
                {
                    droppedFrameCount = checked(
                        persistedDroppedFrameCount + captureDroppedFrameCount);
                }
                await session.UpdateDroppedFrameCountAsync(droppedFrameCount);
            }
            if (terminal)
            {
                await StopAsync();
            }
        }
        catch (Exception exception)
        {
            StatusTitle = "観測session停止失敗";
            StatusMessage = exception.Message;
        }
    }

    private void ResetFrameQueueForStart(long persistedDropCount = 0)
    {
        lock (frameSync)
        {
            if (frameWorkerRunning || pendingFrame is not null)
            {
                throw new InvalidOperationException("previous observation frame queue is not drained");
            }
            captureEnded = false;
            observationDroppedFrameCount = 0;
            captureDroppedFrameCount = 0;
            persistedDroppedFrameCount = persistedDropCount;
            stableCandidate = null;
            informationDetector.Reset();
        }
        InformationDetection = InformationTitleLineDetectionResult.Empty("waiting for capture frame");
        OnPropertyChanged(nameof(DetectorProgress));
        OnPropertyChanged(nameof(StableCandidate));
        OnPropertyChanged(nameof(CollectionStateTitle));
        OnPropertyChanged(nameof(CollectionStateMessage));
        OnPropertyChanged(nameof(CanConfigureAutoSave));
    }

    private bool IsSaved(string compositeIdentityHash) =>
        catalogSavedIdentityKeys.Contains(compositeIdentityHash)
        || session.Checkpoint?.Observations.Any(
            observation => (session.RequiresCompositeIdentity
                    ? observation.CompositeIdentityHash
                    : observation.FeatureHash) == compositeIdentityHash) == true;

    private async Task InspectAndMaybeAutoSaveAsync(JacketObservationCandidate candidate)
    {
        var candidateKey = CandidateIdentityKey(candidate);
        ObservationSavePreflight preflight;
        if (savePreflightByIdentity.TryGetValue(candidateKey, out var cachedDisposition))
        {
            preflight = new ObservationSavePreflight(cachedDisposition, candidateKey);
        }
        else
        {
            try
            {
                preflight = await session.InspectLastStableSavePreflightAsync();
                savePreflightByIdentity[candidateKey] = preflight.Disposition;
            }
            catch (Exception exception)
            {
                await dispatcher.InvokeAsync(() =>
                {
                    StatusTitle = "保存前照合失敗";
                    StatusMessage = exception.Message;
                });
                return;
            }
        }
        if (preflight.CompositeIdentityHash != candidateKey)
        {
            return;
        }
        if (preflight.Disposition == ObservationSavePreflightDisposition.CatalogExisting)
        {
            await dispatcher.InvokeAsync(() => MarkCatalogExisting(candidateKey));
            return;
        }
        if (preflight.Disposition != ObservationSavePreflightDisposition.Eligible
            || IsCaptureEnded()
            || !AutoSaveEnabled
            || !autoSaveAttemptedIdentityKeys.Add(candidateKey))
        {
            return;
        }
        try
        {
            var result = await session.AdoptLastStableAsync();
            await dispatcher.InvokeAsync(() => ApplyAdoptionResult(result, automatic: true));
        }
        catch (ObservationAlreadyCatalogedException exception)
        {
            await dispatcher.InvokeAsync(() => MarkCatalogExisting(exception.IdentityHash));
        }
        catch (Exception exception)
        {
            await dispatcher.InvokeAsync(() =>
            {
                StatusTitle = "自動保存失敗";
                StatusMessage = $"自動再試行はしません。明示保存またはcatalog retryを使用してください: {exception.Message}";
            });
        }
    }

    private void ApplyAdoptionResult(ObservationAdoptionResult result, bool automatic)
    {
        lastCatalogReceipt = result.Catalog.Message;
        StatusTitle = result.Catalog.Disposition switch
        {
            CatalogIngestDisposition.Created or CatalogIngestDisposition.Existing => automatic
                ? "自動保存・catalog投入完了"
                : "観測採用・catalog投入完了",
            CatalogIngestDisposition.DeferredUnsupportedSchema => automatic
                ? "自動保存・catalog投入保留"
                : "観測採用・catalog投入保留",
            _ => automatic ? "自動保存・catalog retry待ち" : "観測採用・catalog retry待ち",
        };
        StatusMessage = result.Catalog.Message;
        OnPropertyChanged(nameof(LastCatalogReceipt));
        NotifySaveStateChanged();
    }

    private void MarkCatalogExisting(string identityHash)
    {
        catalogSavedIdentityKeys.Add(identityHash);
        savePreflightByIdentity[identityHash] = ObservationSavePreflightDisposition.CatalogExisting;
        StatusTitle = "catalog保存済み";
        StatusMessage = "current catalogに同じcomposite identityがあるため、artifact/checkpointは作成しません。";
        NotifySaveStateChanged();
    }

    private void NotifySaveStateChanged()
    {
        OnPropertyChanged(nameof(CanAdopt));
        OnPropertyChanged(nameof(CollectionStateTitle));
        OnPropertyChanged(nameof(CollectionStateMessage));
    }

    private void ResetSavePreflightState()
    {
        AutoSaveEnabled = false;
        catalogSavedIdentityKeys.Clear();
        autoSaveAttemptedIdentityKeys.Clear();
        savePreflightByIdentity.Clear();
    }

    private string CandidateIdentityKey(JacketObservationCandidate candidate) =>
        session.RequiresCompositeIdentity
            ? candidate.CompositeIdentity?.IdentityHash ?? ""
            : candidate.FeatureHash;

    private bool IsCaptureEnded()
    {
        lock (frameSync)
        {
            return captureEnded;
        }
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? name = null)
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
