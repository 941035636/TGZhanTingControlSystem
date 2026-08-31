using System.Text.Json;
using TG.Control.Contracts;

namespace TG.Control.Server;

public enum ContentDraftWorkflowFailure
{
    NotFound,
    Conflict,
    CandidateExpired,
    InvalidAsset
}

public sealed class ContentDraftWorkflowException(
    ContentDraftWorkflowFailure failure,
    string errorCode,
    string message) : InvalidOperationException(message)
{
    public ContentDraftWorkflowFailure Failure { get; } = failure;
    public string ErrorCode { get; } = errorCode;
}

public sealed class ContentDraftValidationException(Dictionary<string, string[]> errors)
    : InvalidOperationException("Content draft validation failed.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ContentDraftWorkflowService(
    IContentRepository publishedRepository,
    ContentDraftRepository draftRepository,
    TtsProductionService ttsProduction,
    AssetStorage assetStorage,
    OperationalEventRepository events)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions comparisonJson = new(JsonSerializerDefaults.Web);

    public async Task<ContentDraftSnapshot> GetAsync(HostString requestHost, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var published = await publishedRepository.GetAsync(cancellationToken);
            var draft = await draftRepository.GetOrCreateAsync(published, cancellationToken);
            return BuildSnapshot(draft, requestHost);
        }
        finally { gate.Release(); }
    }

    public async Task<ContentDraftSnapshot> SaveAsync(SaveContentDraftRequest request, string actor,
        HostString requestHost, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Modules);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var published = await publishedRepository.GetAsync(cancellationToken);
            if (published.Version != request.BaseContentVersion)
                throw Conflict("content_version_conflict");
            await draftRepository.GetOrCreateAsync(published, cancellationToken);
            var draft = await draftRepository.ReplaceAsync(request.BaseContentVersion, request.ExpectedRevision,
                request.Modules, actor, cancellationToken);
            return BuildSnapshot(draft, requestHost);
        }
        catch (ContentDraftConflictException)
        {
            throw Conflict("draft_revision_conflict");
        }
        finally { gate.Release(); }
    }

    public async Task<NarrationAudioCandidateEvaluation> EvaluateCandidateAsync(string candidateId,
        HostString requestHost, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var published = await publishedRepository.GetAsync(cancellationToken);
            var draft = await draftRepository.GetOrCreateAsync(published, cancellationToken);
            return await EvaluateCandidateCoreAsync(candidateId, draft, requestHost, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<AdoptNarrationAudioCandidateResponse> AdoptAsync(string candidateId,
        AdoptNarrationAudioCandidateRequest request, string actor, HostString requestHost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var published = await publishedRepository.GetAsync(cancellationToken);
            if (published.Version != request.BaseContentVersion)
                throw Conflict("content_version_conflict");
            var draft = await draftRepository.GetOrCreateAsync(published, cancellationToken);
            if (draft.Revision != request.ExpectedDraftRevision)
                throw Conflict("draft_revision_conflict");

            var evaluation = await EvaluateCandidateCoreAsync(candidateId, draft, requestHost, cancellationToken);
            if (!evaluation.CandidateExists)
                throw new ContentDraftWorkflowException(ContentDraftWorkflowFailure.NotFound,
                    "candidate_not_found", "Candidate does not exist.");
            if (!evaluation.LocationMatches)
                throw Expired("candidate_location_changed", "Candidate target module or node no longer matches the draft.");
            if (!evaluation.NarrationTextMatches)
                throw Expired("candidate_text_stale", "Narration text changed after this candidate was generated.");
            if (!evaluation.SynthesisConfigurationMatches)
                throw Expired("candidate_configuration_stale", "Voice or synthesis settings changed after generation.");
            if (!evaluation.AssetValid)
                throw new ContentDraftWorkflowException(ContentDraftWorkflowFailure.InvalidAsset,
                    "candidate_asset_invalid", "Candidate audio asset is missing or invalid.");

            var candidate = await ttsProduction.GetCandidateAsync(candidateId, cancellationToken)
                            ?? throw new ContentDraftWorkflowException(ContentDraftWorkflowFailure.NotFound,
                                "candidate_not_found", "Candidate does not exist.");
            var job = await ttsProduction.GetJobAsync(candidate.JobId, cancellationToken)
                      ?? throw new ContentDraftWorkflowException(ContentDraftWorkflowFailure.NotFound,
                          "candidate_job_not_found", "Candidate source job does not exist.");
            var modules = draft.Modules.ToArray();
            var moduleIndex = Array.FindIndex(modules, item =>
                string.Equals(item.Id, job.ModuleId, StringComparison.OrdinalIgnoreCase));
            if (moduleIndex < 0) throw Expired("candidate_location_changed", "Candidate module no longer exists.");
            var nodes = modules[moduleIndex].Nodes.ToArray();
            var nodeIndex = Array.FindIndex(nodes, item =>
                string.Equals(item.Id, job.NodeId, StringComparison.OrdinalIgnoreCase));
            if (nodeIndex < 0) throw Expired("candidate_location_changed", "Candidate node no longer exists.");

            var now = DateTimeOffset.UtcNow;
            var binding = new NarrationAudioBinding(candidate.Asset, candidate.NarrationTextFingerprint,
                candidate.SynthesisConfigurationFingerprint, candidate.SynthesisConfiguration,
                NarrationAudioOrigin.Generated, now, NarrationAudioFingerprint.Version,
                candidate.ProviderRequestId);
            nodes[nodeIndex] = nodes[nodeIndex] with
            {
                TtsAudioUrl = candidate.Asset.Url,
                TtsConfiguration = candidate.SynthesisConfiguration,
                NarrationAudio = binding
            };
            modules[moduleIndex] = modules[moduleIndex] with { Nodes = nodes };
            var updated = await draftRepository.ReplaceAsync(draft.BaseContentVersion, draft.Revision,
                modules, actor, cancellationToken);
            var snapshot = BuildSnapshot(updated, requestHost);
            var adoptedStatus = snapshot.NarrationAudioStatuses.First(item =>
                string.Equals(item.ModuleId, job.ModuleId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.NodeId, job.NodeId, StringComparison.OrdinalIgnoreCase));
            if (adoptedStatus.Status != NarrationAudioBindingStatus.Fresh)
                throw new InvalidOperationException("Adopted narration binding did not evaluate as Fresh.");

            await events.AppendAsync("Information", "TTS", "AdoptCandidate",
                $"{actor} adopted a generated narration audio candidate.", detail: candidateId,
                nodeId: job.NodeId, cancellationToken: cancellationToken);
            return new AdoptNarrationAudioCandidateResponse(snapshot, binding);
        }
        catch (ContentDraftConflictException)
        {
            throw Conflict("draft_revision_conflict");
        }
        finally { gate.Release(); }
    }

    public async Task<PublishedContent> PublishAsync(SaveContentDraftRequest request, string actor,
        HostString requestHost, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var published = await publishedRepository.GetAsync(cancellationToken);
            if (published.Version != request.BaseContentVersion)
                throw Conflict("content_version_conflict");
            var draft = await draftRepository.GetOrCreateAsync(published, cancellationToken);
            if (draft.Revision != request.ExpectedRevision)
                throw Conflict("draft_revision_conflict");
            var requestedModules = NarrationAudioCompatibility.NormalizeModules(request.Modules);
            if (!JsonSerializer.SerializeToUtf8Bytes(draft.Modules, comparisonJson)
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(requestedModules, comparisonJson)))
                throw Conflict("draft_content_conflict");

            var validation = ContentValidator.Validate(requestedModules, assetStorage, requestHost, published);
            if (validation.Count > 0) throw new ContentDraftValidationException(validation);
            PublishedContent result;
            try
            {
                result = await publishedRepository.SaveIfVersionAsync(requestedModules, published.Version,
                    actor, cancellationToken);
            }
            catch (ContentVersionConflictException)
            {
                throw Conflict("content_version_conflict");
            }
            await draftRepository.ResetAsync(result, cancellationToken);
            return result;
        }
        finally { gate.Release(); }
    }

    public async Task ResetAsync(PublishedContent published, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { await draftRepository.ResetAsync(published, cancellationToken); }
        finally { gate.Release(); }
    }

    private async Task<NarrationAudioCandidateEvaluation> EvaluateCandidateCoreAsync(string candidateId,
        ContentDraftDocument draft, HostString requestHost, CancellationToken cancellationToken)
    {
        var candidate = await ttsProduction.GetCandidateAsync(candidateId, cancellationToken);
        if (candidate is null)
            return Evaluation(candidateId, draft, false, false, false, false, false,
                "Candidate does not exist.");
        var job = await ttsProduction.GetJobAsync(candidate.JobId, cancellationToken);
        if (job is null || job.Status != TtsProductionJobStatus.Succeeded ||
            !string.Equals(job.CandidateId, candidateId, StringComparison.Ordinal))
            return Evaluation(candidateId, draft, true, false, false, false, false,
                "Candidate source job is invalid.");

        var matchingModules = draft.Modules.Where(item =>
            string.Equals(item.Id, job.ModuleId, StringComparison.OrdinalIgnoreCase)).ToArray();
        var matchingNodes = matchingModules.SelectMany(item => item.Nodes).Where(item =>
            string.Equals(item.Id, job.NodeId, StringComparison.OrdinalIgnoreCase)).ToArray();
        var locationMatches = matchingModules.Length == 1 && matchingNodes.Length == 1;
        var node = locationMatches ? matchingNodes[0] : null;
        var textMatches = node is not null && string.Equals(candidate.NarrationTextFingerprint,
            NarrationAudioFingerprint.ComputeText(node.NarrationText), StringComparison.OrdinalIgnoreCase);
        var configurationMatches = node?.TtsConfiguration is not null &&
                                   NarrationAudioFingerprint.IsSynthesisConfigurationValid(node.TtsConfiguration) &&
                                   string.Equals(candidate.SynthesisConfigurationFingerprint,
                                       NarrationAudioFingerprint.ComputeSynthesisConfiguration(node.TtsConfiguration),
                                       StringComparison.OrdinalIgnoreCase);
        var assetValid = candidate.Validation.Valid &&
                         NarrationAudioBindingInspector.HasCompleteAssetIdentity(candidate.Asset, out _) &&
                         assetStorage.ValidatePublishedReference(candidate.Asset.Url, candidate.Asset.SizeBytes,
                             requestHost, candidate.Asset.Sha256) is null;
        var adoptable = locationMatches && textMatches && configurationMatches && assetValid;
        var message = adoptable ? "Candidate matches the current draft and can be adopted."
            : !locationMatches ? "Candidate target module or node changed."
            : !textMatches ? "Narration text changed after generation."
            : !configurationMatches ? "Voice or synthesis settings changed after generation."
            : "Candidate audio asset is invalid.";
        return new NarrationAudioCandidateEvaluation(candidateId, draft.BaseContentVersion, draft.Revision, true,
            locationMatches, textMatches, configurationMatches, assetValid, adoptable, message);
    }

    private ContentDraftSnapshot BuildSnapshot(ContentDraftDocument draft, HostString requestHost)
    {
        var statuses = draft.Modules.SelectMany(module => module.Nodes.Select(node =>
        {
            var evaluation = NarrationAudioBindingInspector.Evaluate(node, assetStorage, requestHost);
            return new NarrationAudioDraftStatus(module.Id, node.Id, evaluation.Status, evaluation.Message);
        })).ToArray();
        return new ContentDraftSnapshot(draft.BaseContentVersion, draft.Revision, draft.UpdatedAtUtc,
            draft.UpdatedBy, draft.Modules, statuses);
    }

    private static NarrationAudioCandidateEvaluation Evaluation(string candidateId, ContentDraftDocument draft,
        bool exists, bool location, bool text, bool configuration, bool asset, string message) =>
        new(candidateId, draft.BaseContentVersion, draft.Revision, exists, location, text, configuration, asset,
            location && text && configuration && asset, message);

    private static ContentDraftWorkflowException Conflict(string code) =>
        new(ContentDraftWorkflowFailure.Conflict, code,
            "Content changed; refresh and confirm the latest draft before continuing.");

    private static ContentDraftWorkflowException Expired(string code, string message) =>
        new(ContentDraftWorkflowFailure.CandidateExpired, code, message);
}
