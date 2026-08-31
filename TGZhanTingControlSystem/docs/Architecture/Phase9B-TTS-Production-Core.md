# Phase 9B — TTS Production Core

## 1. Scope and result

Phase 9B adds the provider-neutral Server production pipeline approved after Phase 9A:

```text
Request -> persistent Job -> ITtsProvider -> PCM WAV validation
        -> existing immutable AssetStorage -> Candidate
```

The safety boundary is intentional:

```text
Generate != Adopt != Publish
```

A successful Job creates a persisted `NarrationAudioCandidate`. It does not change a
`NarrationAudioBinding`, save a content draft, create a `PublishedContent` version, modify the
LED manifest, or send a playback command. A failed, timed-out, cancelled, corrupt, or interrupted
attempt changes only TTS production state.

No Azure, Edge, Local model, paid API, external TTS SDK, Admin generation UI, preview UI, adoption
API, playback protocol, playback state machine, TouchClient audio path, or real provider is included.

## 2. Provider abstraction

`ITtsProvider` exposes only provider-neutral concepts:

- stable `ProviderId`;
- an asynchronous descriptor containing availability, voices and capabilities;
- a synthesis request containing normalized text, the Phase 9A text fingerprint, the synthesis
  configuration snapshot, and its fingerprint;
- an audio stream result with declared media type and optional provider request ID;
- `CancellationToken` on capability and synthesis calls;
- `TtsProviderException` with `Transient` or `Permanent` failure kind and a stable error code.

`TtsProviderRegistry` resolves registered adapters case-insensitively and rejects duplicate or empty
IDs. Production orchestration never branches on a vendor name. A future Local, Azure, or other
adapter is added through DI and the same interface; credentials and SDK types stay inside that adapter.

`DeterministicTestTtsProvider` is explicitly development/test-only. The same text/configuration pair
produces the same short PCM WAV test tone. It is not speech synthesis and is not presented as a
production provider. Server registration requires both:

1. the ASP.NET environment is `Development`;
2. `TtsProduction:EnableDeterministicTestProvider` is explicitly `true`.

The checked-in default is `false`, so no provider is silently enabled in production.

## 3. Job model and state machine

`TtsProductionJob` persists:

- Job, module and node identity;
- requesting administrator;
- normalized narration text and Server-computed text fingerprint;
- complete synthesis configuration snapshot and Server-computed configuration fingerprint;
- idempotency key, provider and voice;
- `Queued / Running / Succeeded / Failed / Cancelled` state;
- created, started and completed timestamps;
- retry count and immutable attempt history;
- structured error category, code and diagnostic message;
- optional Candidate ID.

State transitions are:

```text
Queued -> Running -> Succeeded
                  -> Failed
                  -> Cancelled
Queued ----------------> Cancelled
```

Every completed attempt records its number, start/end time, success, and structured failure when
applicable. Error categories distinguish transient provider failure, permanent input/provider failure,
invalid media, cancellation, server interruption, and an internal Server failure.

## 4. Candidate model

`NarrationAudioCandidate` links:

- one immutable `ContentAsset`;
- the authoritative text and synthesis fingerprints;
- the complete synthesis configuration snapshot;
- provider and voice audit information;
- source Job and optional provider request ID;
- creation time;
- Server media validation result and extracted duration.

Candidate means “validated audio available for preview/adoption.” It does not mean “currently bound,”
“published,” or “playable by the active content version.” Phase 9C must perform an explicit adoption
operation before a candidate can become a draft `NarrationAudioBinding`.

## 5. Idempotency and concurrency

The idempotency key is Server-owned SHA-256 over the versioned domain
`tg:tts-production-idempotency:v1`, the narration text fingerprint, and the synthesis configuration
fingerprint. Module/node IDs are audit location, not audio identity.

Repository creation is serialized under one gate. For the same fingerprint pair:

- an existing Queued, Running, or Succeeded Job is returned;
- an existing Failed or Cancelled Job is returned unless `RetryFailed=true`;
- explicit retry creates exactly one new Queued Job; concurrent retry requests then reuse it.

This protects the Server even when multiple Admin requests arrive concurrently. UI button disabling is
not the idempotency mechanism. The implementation does not physically deduplicate files beyond this Job
policy and does not rewrite the existing asset repository.

## 6. Persistence and restart recovery

`TtsProductionRepository` stores Jobs and Candidates together in:

```text
Data/tts-production.json
```

Every mutation is written to a temporary file and atomically replaces the previous JSON state. Job
success and Candidate creation are committed in one repository write, so restart cannot preserve a
successful Job while losing its Candidate.

Startup recovery is deterministic:

- Succeeded, Failed and Cancelled Jobs remain historical records;
- Candidates remain available;
- Queued Jobs are scheduled by `TtsProductionService`;
- a Job persisted as Running is converted to Failed with category `Interrupted` and code
  `server_interrupted`, including a completed interrupted attempt record.

Phase 9B does not attempt to resume an arbitrary provider stream after process loss. An operator may
explicitly retry the interrupted fingerprint pair.

## 7. Media validation and immutable asset ingestion

Phase 9B deliberately supports one verifiable generation format: uncompressed 16-bit PCM WAV.

The Server does not trust provider SHA, size, duration, MIME, extension, or stream content. It:

1. copies the provider stream to a bounded temporary staging file;
2. rejects empty, undersized, oversized, or non-WAV declared media;
3. parses RIFF/WAVE, `fmt ` and `data` chunks;
4. requires PCM codec, mono/stereo, 8–48 kHz, 16-bit samples, consistent byte rate/block alignment,
   non-empty audio data, and a valid duration;
5. requires actual channel/sample-rate values to match nonzero requested values;
6. imports the validated stream through the existing `AssetStorage` media directory;
7. recomputes SHA-256 and byte size during the final immutable write;
8. creates a normal `ContentAsset` with `AssetKind.NarrationAudio`, URL, SHA, size, duration and MIME.

Temporary staging is not a second asset repository. A failure after immutable import but before Candidate
commit can leave an unreferenced immutable file; reference-aware asset garbage collection remains a
separate future phase and must protect current content, history, bindings and candidates.

## 8. Retry, timeout and cancellation

Defaults are configurable under `TtsProduction`:

- maximum 3 attempts;
- 30-second timeout per attempt;
- 250 ms bounded retry delay;
- 5,000 normalized characters;
- 45-byte minimum and 100 MiB maximum generated stream.

Only provider failures explicitly classified `Transient` and provider timeouts retry. Permanent
input/provider errors, media validation failures, and internal failures do not retry automatically.
The configured attempt count is clamped to 1–10, so no path retries indefinitely.

Cancellation propagates to the provider and validation/import pipeline. Queued Jobs can become
Cancelled immediately. Running Jobs persist a cancelled attempt and cannot create a Candidate after the
cancellation boundary. A provider that honors the required token stops promptly.

## 9. Minimal Admin API foundation

All new routes require the existing Admin session authentication:

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/tts/providers` | List actually registered provider/voice capabilities |
| `POST` | `/api/tts/jobs` | Validate input and create or reuse an idempotent Job |
| `GET` | `/api/tts/jobs/{jobId}` | Read persisted Job state and attempts |
| `POST` | `/api/tts/jobs/{jobId}/cancel` | Cancel a Queued/Running Job |
| `GET` | `/api/tts/candidates/{candidateId}` | Read one validated Candidate |

There is intentionally no adopt, discard, preview orchestration, draft update, or publish endpoint in
Phase 9B. The pre-existing legacy `/api/tts/synthesize` and `UnconfiguredTtsService` remain unchanged for
compatibility; the new production core does not route through them.

## 10. Failure safety and compatibility

`TtsProductionService` has no dependency on `IContentRepository`, `PlaybackCoordinator`, command broker,
TouchClient, or LedPlayer. Therefore generation cannot mutate current or historical published content.
Automated tests prove both a failed Job and a successful unadopted Candidate leave an existing
`NarrationAudioBinding` and PublishedContent version unchanged.

Contracts changes are additive production DTOs in a new file. Phase 9A binding fields and legacy
`TtsAudioUrl` compatibility remain unchanged. `PlaybackCommand`, `PlaybackCoordinator`,
`NarrationAudioPlayer`, TouchClient and LedPlayer source were not modified.

## 11. Automated verification

Run:

```powershell
dotnet run --project tests/TG.Control.Phase9B.Tests/TG.Control.Phase9B.Tests.csproj -c Release
```

Result: **23/23 passed**. Coverage includes:

- deterministic byte output from the development-only test provider;
- normal generation, timestamps, attempt transition, Candidate creation;
- actual ContentAsset SHA, size, MIME and parsed duration;
- sequential and 20-request concurrent idempotency;
- explicit failed-Job retry behavior;
- permanent failure, bounded transient retry and bounded timeout retry;
- empty and corrupt media;
- invalid provider, voice, rate, pitch, volume, PCM sample rate/channel count, empty text and overlong text;
- running cancellation;
- successful Job/Candidate and failed Job persistence across restart;
- deterministic Running-to-Interrupted recovery;
- failure does not change an existing binding or PublishedContent;
- Candidate creation does not adopt or publish.

Phase 9A compatibility suite also remains **20/20 passed**.

An authenticated Development API smoke test also registered exactly one deterministic test provider,
created a Job through `POST /api/tts/jobs`, observed one successful attempt, read the persisted Candidate,
and verified a 64-character SHA-256, nonzero size, valid PCM WAV media and `validation.valid=true`.

## 12. Build verification

| Target | Result |
|---|---|
| Contracts + Server Release | PASS — 0 warnings, 0 errors |
| AdminWeb production build | PASS — TypeScript and Vite production build |
| TouchClient Windows Player | PASS — Unity 2020.3.35f1c2, StandaloneWindows64 |
| LedPlayer Windows Player | PASS — Unity 2020.3.35f1c2, StandaloneWindows64; LibVLC runtime DLLs present |

Build evidence is outside the repository at:

```text
C:/Users/A/AppData/Local/Temp/TG-Phase9B-Builds/
```

## 13. Phase 9C boundary

Phase 9C still needs to design and implement, after explicit approval:

- Admin provider/voice selection and generation request UX;
- persisted Job polling and failure/cancellation presentation;
- Candidate audio preview and explicit adopt/discard UX;
- an authenticated adopt API that revalidates Candidate fingerprints and current draft text/config;
- construction of a Generated `NarrationAudioBinding` from the adopted immutable asset;
- draft ownership/concurrency semantics, because current Admin drafts are browser-local;
- publish UX using the existing Phase 9A Fresh/Stale authority;
- clear handling when text/config changes after Candidate generation;
- operational audit events for Generate/Adopt/Discard;
- eventual reference-aware asset cleanup policy.

Phase 9C must not make Generate imply Adopt or Publish. Selection of the first real provider remains a
separate approved phase and must reuse `ITtsProvider` without vendor branches in production orchestration.

Phase 9B stops here.
