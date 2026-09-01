# Phase 9D — Narration Audio Publish / Rollback / LED Integrity

## 1. Scope and result

Phase 9D closes the production-content path established by Phase 9A–9C:

```text
Fresh Draft NarrationAudioBinding
  -> Server publish gate
  -> immutable PublishedContent snapshot
  -> complete LED manifest
  -> SHA/Size-verified LED cache
  -> PlaybackCoordinator
  -> LedPlayer narration audio playback
```

Rollback follows the same verified path:

```text
historical PublishedContent
  -> Server rollback validation
  -> new immutable published version
  -> historical binding and asset identity
  -> LED manifest/version resynchronization
  -> historical audio playback
```

The working-tree audit started from actual local HEAD
`087896f7304c81a6aee045a1f1480e903024b593`. This includes the accepted Phase 9C baseline and its later
AdminWeb clean-hosting fix. Local source was treated as the source of truth.

This phase does not add Azure, Edge, a Local production model, a real external Provider, asset garbage
collection, legacy batch migration, a new playback command, a new playback state machine, or TouchClient UI
changes.

## 2. Server-authoritative publish gate

`ContentPublishPolicy` is the single publish-readiness authority used by draft snapshots and by the final
Server publish validation. AdminWeb presents the result but does not calculate Fresh/Stale or asset validity.

For every new `NarrationAudioBinding`, publish revalidates:

- complete, non-empty `AssetId` and URL;
- `AssetKind.NarrationAudio`;
- supported formal playback media type and matching file extension;
- positive file size and exact on-disk size;
- valid SHA-256 and exact on-disk SHA-256;
- current fingerprint version;
- the binding's text/configuration fingerprints and configuration snapshot;
- current `NarrationText` fingerprint;
- current synthesis configuration fingerprint;
- stable immutable asset identity within the complete published content.

The formally accepted narration formats are currently:

| Media type | Extension | Source |
|---|---|---|
| `audio/wav` | `.wav` | Generated Candidate or manual upload |
| `audio/mpeg` | `.mp3` | Manual upload |

Phase 9B generated output remains validated PCM WAV only. This phase does not claim that a Provider can
generate MP3.

## 3. Node publication matrix

| Node state | Publish result | Reason |
|---|---|---|
| Video/animation, no narration text | Allow | Existing visual-only semantics |
| Video/animation + narration text, no audio | Warning, allow | Existing video playback remains valid; Admin is explicitly told there is no playable narration |
| Pure narration text, no video and no audio | Block | Cannot form an automatic narration node |
| Fresh new binding | Allow | Text, configuration and immutable asset all verified by Server |
| `StaleText` | Block | Prevents new text from playing old audio |
| `StaleSynthesisConfiguration` | Block | Prevents changed voice/rate/pitch from using old audio |
| `InvalidAsset` / `InvalidBinding` | Block | Formal integrity or binding identity is not trustworthy |
| Unchanged legacy `TtsAudioUrl` | Warning, allow | Read/play/rollback compatibility only; never presented as Fresh |
| Edited/new node attempting legacy-only audio | Block | Must be upgraded through Generate+Adopt or manual binding |

Warnings and blocking issues carry module ID/name and node ID/name so AdminWeb can identify the exact
location. HTTP errors and stack traces are not exposed as operator-facing content.

## 4. Immutable published snapshots

Publication still uses `SaveIfVersionAsync`; a successful publish creates a complete `PublishedContent`
snapshot containing the binding and immutable `ContentAsset` identity present at that moment.

After publication:

- editing Draft does not mutate current PublishedContent;
- later Candidate changes do not mutate PublishedContent;
- Provider configuration changes do not rewrite historical versions;
- a failed publish does not increment the content version;
- a failed publish does not reset Draft or alter the active formal version;
- an active playback session continues to use the content version it started with.

The publish API now requires both `BaseContentVersion` and `ExpectedDraftRevision`. Requests missing either
value fail with `publish_revision_required`; the old non-concurrent unconditional publish path is no longer
available to Admin.

## 5. Publish and rollback concurrency

Publish is serialized by `ContentDraftWorkflowService` and protected by:

```text
expected PublishedContent version + expected Draft revision
```

The following races have deterministic outcomes:

- two concurrent publishes: one succeeds and one receives `content_version_conflict`;
- Adopt racing Publish: one mutation succeeds and the stale operation receives a revision/version conflict;
- stale Draft publish: `draft_revision_conflict`;
- Generate still Running: it does not enter Draft or PublishedContent;
- Candidate created but not Adopted: it does not enter PublishedContent or the manifest.

Rollback now accepts `RollbackContentRequest(ExpectedContentVersion, ExpectedDraftRevision)`. The Server
checks current published version and current Draft revision, validates the historical target, performs an
optimistic repository rollback, creates a new version, and resets Draft to that result. A rollback never
overwrites history in place.

## 6. LED manifest integrity

For a new narration binding, `ContentManifestBuilder` emits:

```text
AssetId + URL + SHA256 + SizeBytes + MediaType
```

The additive `AssetId` and `MediaType` fields are mirrored in `UnityContracts`. `PlaybackCommand` is
unchanged.

Manifest deduplication uses stable `AssetId` for immutable assets and URL only for legacy assets. Multiple
nodes referencing the same immutable audio create one download. If one AssetId maps to conflicting
URL/SHA/Size/MediaType values, publication/manifest generation fails instead of silently selecting one.

Legacy `TtsAudioUrl` remains represented with empty SHA and zero Size to make its unverified status explicit;
the Server never fabricates integrity metadata.

## 7. LED cache behavior

The existing `LedContentCache` behavior was exercised without changing the LedPlayer state machine:

1. the manifest registers expected Size and SHA per normalized URL;
2. an existing cache file is checked before reuse;
3. Size mismatch or SHA mismatch deletes the invalid local cache;
4. the asset is downloaded again;
5. the partial download is verified before atomic promotion to the final cache path;
6. a valid cache survives LedPlayer restart and is reused.

The Phase 9D change is the complete manifest identity supplied to this already-existing validation path.

## 8. Asset reference protection and future GC rule

`AssetReferenceProtectionService` prevents direct deletion of a stored media file while it is referenced by:

- current Draft bindings or content assets;
- current PublishedContent;
- any historical version still available for rollback;
- a media/cover asset in those documents;
- a valid `NarrationAudioCandidate`.

The delete API returns `409 asset_is_referenced` with user-readable references instead of removing the file.

Future asset GC must use the same union of roots. An asset is collectable only when it has no reference from
Draft, current PublishedContent, any retained rollback version, or any valid Candidate. Candidate expiry and
history-retention policy must be defined before automatic GC is implemented.

## 9. Legacy compatibility

Legacy JSON remains readable and normalizes without creating a fake new binding. Unchanged legacy audio can
still be published with a warning, played through the existing legacy URL fallback, and restored by rollback.

Legacy rules are deliberately asymmetric:

- historical read/play/rollback: supported;
- unchanged legacy republish: supported with warning;
- legacy value following changed text or a new node: blocked;
- legacy status: always `LegacyUnverified`, never `Fresh`;
- batch migration: not included in Phase 9D.

## 10. Admin publish experience

`ContentDraftSnapshot` now includes optional `ContentPublishReadiness`. AdminWeb shows the Server result:

- valid narration audio count;
- missing audio count;
- stale text/configuration count;
- legacy count;
- invalid asset/binding count;
- exact blocking issues and warnings by module/node.

After a local edit, readiness is cleared and Publish is disabled until the new Draft revision has been saved
and checked by Server. The summary is then refreshed in place. Publish remains disabled unless the content is
dirty, conflict-free, synchronized, and Server reports `CanPublish=true`.

Rollback sends the current published version and Draft revision for optimistic concurrency.

## 11. Automated test evidence

All commands ran in Release configuration on 2026-09-01.

| Suite | Status | Result |
|---|---|---|
| Phase 9A binding/fingerprint/legacy/manifest regression | PASS | 20/20 |
| Phase 9B Provider/Job/Candidate/persistence regression | PASS | 23/23 |
| Phase 9C Draft/Candidate/Adopt/concurrency regression | PASS | 11/11 |
| Phase 9D publish/rollback/manifest/reference protection | PASS | 25/25 |
| AdminWeb TTS workflow tests | PASS | 8/8 |
| Failed tests | PASS | 0 failures |

Phase 9D's 25 cases cover:

- Fresh publish;
- StaleText and StaleConfiguration rejection;
- empty AssetId, missing file, wrong SHA, wrong Size, unsupported media and invalid binding rejection;
- Candidate/Running Job isolation;
- immutable published snapshot;
- complete and deduplicated manifest;
- conflicting immutable identity rejection;
- V1/V2 rollback binding and manifest restoration;
- failed-publish atomicity;
- stale revision and concurrent publish;
- Adopt/Publish race;
- legacy read/publish/play/rollback;
- video-only warning and pure-narration blocking;
- Draft/current/history/Candidate asset reference protection;
- optimistic rollback revision check.

## 12. Build evidence

| Target | Status | Evidence |
|---|---|---|
| Server Release | PASS | 0 warnings, 0 errors; AdminWeb production assets embedded |
| AdminWeb production | PASS | TypeScript + Vite build completed |
| TouchClient Windows x64 | PASS | Unity 2020.3.35f1c2 `DisplayProgressNotification: Build Successful` |
| LedPlayer Windows x64 | PASS | Unity 2020.3.35f1c2 `DisplayProgressNotification: Build Successful` |

Temporary build logs used for verification:

```text
C:\Users\A\AppData\Local\Temp\TG-Phase9D-Final\TouchClient-build.log
C:\Users\A\AppData\Local\Temp\TG-Phase9D-Final\LedPlayer-build.log
```

## 13. Real V1 -> V2 -> Rollback V1 evidence

The four applications were run against an isolated Development data directory with the explicitly enabled
`DeterministicTestTtsProvider`. It is a test tone Provider, not a production voice.

### V1 / Audio A

```text
NarrationText: Phase 9D 验收文案 A。欢迎参观智慧展厅。
AssetId: c7890c7c2e2f4604b21ad96d0b385b75
URL: /media/ccc026a83826440f9cf0d4700eaf1558.wav
SHA256: a55e2633afccce0451b2599fe8d81a9c1b073f71e40e795569ea502dd6591cfc
Size: 13412
MediaType: audio/wav
```

Admin browser evidence showed Job waiting, Generate success, Candidate match, and the preview action changing
from `播放试听` to `暂停试听`. Adopt changed the Draft summary from one blocking Missing item to one Fresh item;
Publish produced V1 without changing the workflow into automatic publish.

With real TouchClient and LedPlayer registered, Server reported both online/ready at content V1. Playback
session `3dae3ccfdeef4adb85fe41669858d0bf` reported:

```text
LED Received -> Ready -> Playing -> Completed
start drift: 0ms
completed position: 0.2784999907016754s
```

### V2 / Audio B

```text
NarrationText: Phase 9D 验收文案 B。数智创新引领绿色发展。
AssetId: 476405291a604f05a1c70f1524c32c22
URL: /media/6f3ecebc601d47378d4ea96b9037eddd.wav
SHA256: 2ed89e27ccdf18d69ae5b9533527f5dc21dda3a211d1b4abd7fe3a25c368195c
Size: 12076
MediaType: audio/wav
```

Changing text A to B immediately produced Server `StaleText` and blocked Publish. Generate B, browser preview,
explicit Adopt and explicit Publish produced V2. LedPlayer reached content V2/Ready and cached Audio B with
the exact manifest SHA/Size. Playback session `d971fed3a78947c9a43a6732dac36ed1` reported:

```text
LED Received -> Ready -> Playing -> Completed
start drift: 0ms
completed position: 0.25066667795181274s
```

### Rollback V1 -> new V3

Admin rollback selected historical V1 and created immutable current V3:

```text
PublishedBy: admin（回滚自 V1）
NarrationText: restored A
AssetId/SHA/Size: restored Audio A exactly
LED manifest version: V3
LED status: online, Ready, content V3
```

Playback session `2ba765ded72244e19db6d0dd91188517` again reported Audio A:

```text
LED Received -> Ready -> Playing -> Completed
start drift: 0ms
completed position: 0.2784999907016754s
```

Audio A and B both remained in the LED cache with different URL-derived cache names and their exact SHA/Size.

## 14. Restart and corruption recovery evidence

### Valid cache restart reuse

Status: **PASS**

Before restart, Audio A cache was 13412 bytes with SHA
`a55e2633afccce0451b2599fe8d81a9c1b073f71e40e795569ea502dd6591cfc`. After restarting LedPlayer:

- the cache `LastWriteTimeUtc` remained older than the new process start;
- Size/SHA remained unchanged;
- the restart log contained no cache validation failure;
- LedPlayer reported online, Ready, content V3;
- sync completed `V3, 1/1` using the existing cache.

### Corrupt cache re-download

Status: **PASS**

Only the confirmed Audio A file under `DefaultCompany/LedPlayer/Content` was modified. It was reduced to five
bytes with SHA `74f81fe167d99b4cb41d6d0ccda82278caee9f3e2f25d5e5a3936ff3dcec60d0`.

LedPlayer logged:

```text
LED缓存文件校验失败，将重新下载：缓存文件大小不一致：预期 13412 字节，实际 5 字节。
LED内容同步完成：版本 V3，1/1 个素材已缓存。
```

The final cache returned to 13412 bytes and SHA
`a55e2633afccce0451b2599fe8d81a9c1b073f71e40e795569ea502dd6591cfc`; LedPlayer remained online/Ready at V3.

## 15. Acceptance status ledger

| Item | Status | Note |
|---|---|---|
| Server-side publish revalidation | PASS | Fresh/Stale/invalid/missing/legacy matrix verified |
| PublishedContent snapshot immutability | PASS | Automated and V1/V2 real evidence |
| Publish failure atomicity/version stability | PASS | Automated |
| Optimistic publish/rollback concurrency | PASS | Automated |
| Complete narration manifest identity and deduplication | PASS | Automated and real manifests |
| LED download, Size/SHA validation and local cache | PASS | Real Player and files |
| LED restart cache reuse | PASS | Real process restart and unchanged cache timestamp/hash |
| LED corrupted-cache recovery | PASS | Real 5-byte corruption and verified redownload |
| V1 Audio A -> V2 Audio B -> rollback Audio A | PASS | Real Admin, Server, LED and playback statuses |
| Admin Generate/Listen/Adopt/Publish | PASS | Real browser workflow |
| Server + AdminWeb + TouchClient + LedPlayer concurrently running | PASS | Both Unity clients registered online/ready during V1 chain |
| TouchClient physical UI click automation | BLOCKED | Unity window capture returned `SetIsBorderRequired ... 0x80004002`; no guessed-coordinate input was used. The same authenticated operator start API and real Touch/LED registrations were exercised. |
| Formal Azure/Edge/Local production Provider | NOT RUN | Explicitly out of Phase 9D scope |
| Asset GC and legacy batch migration | NOT RUN | Explicitly out of Phase 9D scope |
| Product-code or automated-test failures | PASS | No remaining failure |

The one BLOCKED item is a test-infrastructure limitation of automating a Unity window, not a detected
Publish/Manifest/LED/Playback product failure. No Phase 9D feature was hidden behind a client-side fake state.

## 16. Changed files

### Contracts

- `src/Shared/TG.Control.Contracts/ContentDraftContracts.cs`
- `src/Shared/TG.Control.Contracts/PlaybackContracts.cs`
- `src/Shared/UnityContracts/Runtime/Contracts.cs`

### Server

- `src/Server/TG.Control.Server/ContentPublishPolicy.cs`
- `src/Server/TG.Control.Server/AssetReferenceProtectionService.cs`
- `src/Server/TG.Control.Server/ContentDraftRepository.cs`
- `src/Server/TG.Control.Server/ContentDraftWorkflowService.cs`
- `src/Server/TG.Control.Server/ContentManifestBuilder.cs`
- `src/Server/TG.Control.Server/ContentRepository.cs`
- `src/Server/TG.Control.Server/ContentValidator.cs`
- `src/Server/TG.Control.Server/NarrationAudioBindingInspector.cs`
- `src/Server/TG.Control.Server/TtsProductionRepository.cs`
- `src/Server/TG.Control.Server/Program.cs`

### AdminWeb

- `src/AdminWeb/src/api.ts`
- `src/AdminWeb/src/main.ts`
- `src/AdminWeb/src/style.css`

### Tests and documentation

- `tests/TG.Control.Phase9C.Tests/Program.cs`
- `tests/TG.Control.Phase9D.Tests/TG.Control.Phase9D.Tests.csproj`
- `tests/TG.Control.Phase9D.Tests/Program.cs`
- `docs/Architecture/Phase9D-TTS-Publish-Rollback.md`

## 17. Phase 9E boundary

Phase 9D stops here. Possible future work—not implemented by this commit—includes a real production Provider,
credential/configuration operations, Provider-specific observability, legacy upgrade tooling, asset retention
and garbage collection, and any separately approved playback evolution.
