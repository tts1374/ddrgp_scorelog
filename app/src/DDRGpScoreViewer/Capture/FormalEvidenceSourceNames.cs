namespace DDRGpScoreViewer.Capture;

/// <summary>
/// Requirement-level source IDs shared by producers and the formal save bridge.
/// </summary>
internal static class FormalEvidenceSourceNames
{
    public const string MasterMetadata = "master_metadata";
    public const string ResultIdentityVisualEvidence = "result_identity_visual_evidence";
    public const string ResultNumericVisualEvidence = "result_numeric_visual_evidence";
    public const string ResultRankVisualEvidence = "result_rank_visual_evidence";
    public const string ResultClearTypeVisualEvidence = "result_clear_type_visual_evidence";
    public const string ResultFlareRankVisualEvidence = "result_flare_rank_visual_evidence";
    public const string CaptureEventV1 = "capture_event_v1";
    public const string CaptureUtc = "capture_utc";
}
