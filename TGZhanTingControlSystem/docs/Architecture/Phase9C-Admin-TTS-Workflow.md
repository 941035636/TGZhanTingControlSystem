# Phase 9C — Admin TTS generation, preview and explicit adoption

## 1. Scope and result

Phase 9C is implemented on baseline `b931099560daf5224c667e3ae7939c8088f60e2a` without replacing the existing
AdminWeb technology stack or content workspace. The delivered workflow is:

```text
Edit NarrationText
  -> save Server draft
  -> choose a dynamically registered Provider / Voice / parameters
  -> create or reuse a persistent Job
  -> bounded Job polling
  -> validated immutable Candidate
  -> browser preview
  -> explicit Server-side Adopt
  -> Fresh draft NarrationAudioBinding
  -> separate explicit Publish
```

The safety boundary remains:

```text
Generate != Adopt != Publish
```

No Azure, Edge, Local production model, external TTS API, paid SDK, playback protocol change, TouchClient
audio path, LedPlayer state-machine change, automatic publication, asset garbage collection, or legacy batch
migration is included.

## 2. Existing Admin audit and integration point

The existing AdminWeb is a dependency-light TypeScript/Vite application whose page state and DOM are built
in `src/AdminWeb/src/main.ts`. Before Phase 9C:

- `NarrationText` was edited in the existing module/node modal;
- node/module changes lived in a browser `localStorage` draft;
- manual narration upload already used the Phase 9A binding endpoint;
- the old Generate button called the legacy synchronous `/api/tts/synthesize` route and wrote a URL directly;
- publication posted the entire module list;
- all HTTP calls were centralized in `src/AdminWeb/src/api.ts`.

Phase 9C keeps that workspace and API-client organization. It replaces only the node-level narration voice
area and introduces a small `TtsWorkflowController` for Provider loading, Job polling, Candidate preview and
explicit adoption. It does not introduce a frontend framework or a second content editor.

## 3. Admin interaction model

Each node with a narration text now exposes a **讲解语音** area containing:

- the current Server-authoritative binding state;
- the currently adopted or published audio, its source and immutable asset metadata;
- Provider, Voice, Language, Rate and Pitch;
- Generate and Cancel;
- persistent Job state (`等待生成 / 正在生成 / 生成成功 / 生成失败 / 已取消`);
- a separate Candidate card with Provider, Voice, generated time, duration and Server match checks;
- Play/Pause/Replay preview;
- explicit Adopt and local Abandon actions;
- the existing manual narration-audio upload entry.

The current binding and Candidate are deliberately separate visual cards. A successful adoption is labelled
`当前草稿已采用语音 · 等待发布`; it does not imply that the published version changed.

Development-only Providers and voices are labelled `开发测试`, with an explicit warning that they are not
commercial speech. When the Server reports no available Provider/Voice, the page displays
`当前未配置语音合成服务`, disables Generate, and keeps manual upload available. Provider and Voice values
are never hard-coded in AdminWeb.

## 4. Server-authoritative binding states

AdminWeb consumes `ContentDraftSnapshot.NarrationAudioStatuses`. It does not calculate a text or synthesis
fingerprint and does not recreate Phase 9A freshness rules.

| Server state | Administrator text |
|---|---|
| `Missing` | 尚未生成 |
| `Fresh` | 语音有效 |
| `StaleText` | 讲解词已修改，请重新生成 |
| `StaleSynthesisConfiguration` | 音色配置已变化，请重新生成 |
| `InvalidAsset` | 音频资产异常 |
| `InvalidBinding` | 语音绑定异常 |
| `LegacyUnverified` | 旧版语音待升级 |

While an edit is waiting for Server draft persistence, the UI reports that the Server is checking the draft
and prevents Candidate adoption. It does not temporarily claim a client-computed Fresh/Stale result.

## 5. Persisted draft and optimistic concurrency

Phase 9C adds one authoritative Admin content draft stored at:

```text
Data/content-draft.json
```

The document contains:

```text
BaseContentVersion + Revision + UpdatedAtUtc + UpdatedBy + Modules
```

Concurrency rules:

1. `GET /api/content/draft` returns the current persisted draft and Server binding evaluations.
2. `PUT /api/content/draft` requires the exact base PublishedContent version and expected draft revision.
3. Every successful replacement atomically increments the revision.
4. Adopt requires the exact base version and draft revision.
5. Publish requires the exact base version and revision, verifies that the posted modules equal the persisted
   draft, then uses `IContentRepository.SaveIfVersionAsync` so a concurrent publication cannot be overwritten.
6. Publish and rollback reset the draft to revision zero on the new published base.

AdminWeb retains `localStorage` only as an accidental-refresh recovery copy. It may replay the local modules
only when its recorded Server revision still equals the authoritative revision. It cannot silently overwrite a
newer Server draft. Conflicts display `当前内容已发生变化，请刷新后重新确认` and disable publication.

This is intentionally a minimal shared-draft optimistic lock, not a collaborative document editor.

## 6. Candidate evaluation and Adopt API

New API surface:

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/content/draft` | Read persisted draft and Server binding states |
| `PUT` | `/api/content/draft` | Replace a draft at an exact revision |
| `GET` | `/api/tts/candidates/{candidateId}/evaluation` | Read Server candidate freshness/adoptability |
| `POST` | `/api/tts/candidates/{candidateId}/adopt` | Explicitly adopt into the current draft |

The Phase 9B Provider, Job and Candidate routes remain unchanged. The existing publish route accepts optional
`baseContentVersion` and `expectedDraftRevision`; legacy callers without those fields retain the previous
compatibility path.

`POST /api/tts/candidates/{candidateId}/adopt` revalidates under one Server workflow gate:

1. current PublishedContent version and draft revision;
2. Candidate existence;
3. successful source Job and exact Candidate/Job association;
4. target module and node identity;
5. current Server-computed NarrationText fingerprint;
6. current Server-computed synthesis-configuration fingerprint;
7. Candidate validation result;
8. immutable asset identity, actual file existence, size and SHA-256.

Only after all checks pass does the Server construct a Generated `NarrationAudioBinding`, update the draft,
and verify that the resulting binding evaluates as `Fresh`. The operation appends a structured TTS adoption
event. AdminWeb never constructs a binding from Candidate fields.

Candidate mismatch results use stable error codes such as `candidate_text_stale`,
`candidate_configuration_stale`, `candidate_location_changed` and `candidate_asset_invalid`. AdminWeb maps
these to administrator-friendly Chinese messages; technical details remain in Server state/logging.

## 7. Job polling, preview and abandonment

`TtsWorkflowController` uses the Phase 9B persistent Job API and a one-second polling interval. Polling is
bounded to 600 requests, stops on every terminal state, and is cancelled when the node editor is closed or a
different node is selected. The Generate action is disabled while a request/job is active, while Server-side
fingerprint idempotency remains the final duplicate protection.

Candidate preview uses the existing immutable `ContentAsset.Url`; it does not copy or upload a second audio
file. The controller supports Play, Pause and Replay and stops/reset the browser audio object when the node
changes or the editor is disposed.

`放弃候选` clears the current Admin presentation and stops polling/preview. The persisted Candidate remains
an immutable production/audit record; Phase 9C does not delete Candidate assets or implement asset GC.

## 8. Manual upload and legacy compatibility

Manual upload remains a first-class alternative to TTS:

1. the existing streaming asset endpoint creates a complete narration `ContentAsset`;
2. the Phase 9A binding endpoint verifies the file, SHA and size and returns a complete manual binding;
3. AdminWeb stores `NarrationAudio`, `TtsConfiguration` and the compatibility URL together;
4. the updated node is persisted into the same Server draft and receives a Server Fresh/Stale state.

Legacy URL-only narration remains visible as `旧版语音待升级`. Neither Candidate generation nor adoption
deletes historical assets, so published history and rollback semantics remain intact.

## 9. Automated verification

The following suites passed on 2026-08-31:

| Suite | Result | Coverage relevant to Phase 9C |
|---|---:|---|
| Phase 9A binding suite | 20/20 | fingerprints, Fresh/Stale, manual binding, legacy JSON, manifest integrity |
| Phase 9B production suite | 23/23 | Provider validation, Job lifecycle, idempotency/concurrency, retry/cancel, Candidate persistence and failure safety |
| Phase 9C Server suite | 11/11 | draft status, evaluation, Fresh Adopt, no Publish, text/config expiry, invalid asset, draft conflict, manual/legacy, optimistic Publish |
| AdminWeb workflow suite | 8/8 | provider/no-provider, success/failure, duplicate Generate, bounded polling disposal, preview disposal, explicit Adopt/no Publish |

Specific dangerous cases are covered at the Server authority boundary:

- text A -> Candidate A -> text B -> Adopt A is rejected;
- Voice/config A -> Candidate A -> config B -> Adopt A is rejected;
- an invalid Candidate asset is rejected;
- a stale draft revision is rejected;
- Generate or Adopt never changes `PublishedContent`;
- manual upload still produces a Fresh complete binding;
- legacy audio still loads and is visibly unverified.

## 10. Browser acceptance

An authenticated Development run used only the explicitly enabled `DeterministicTestTtsProvider` and an
isolated data directory under:

```text
C:/Users/A/AppData/Local/Temp/TG-Phase9C-Acceptance/Data
```

The real AdminWeb flow completed:

1. login and open the existing module/node editor;
2. edit narration text A;
3. select the dynamically returned development Provider/Voice;
4. click Generate and observe `等待生成`;
5. receive a succeeded Candidate with validated 0.3-second WAV metadata;
6. play the Candidate in the browser and observe the control enter `暂停试听`;
7. explicitly Adopt;
8. observe `语音有效` and `当前草稿已采用语音 · 等待发布`;
9. verify PublishedContent remained V0 with zero published nodes;
10. change text to B and observe `讲解词已修改，请重新生成`;
11. call Adopt for the old Candidate at the current revision and receive HTTP 409 with
    `candidate_text_stale`;
12. verify the draft remained stale and PublishedContent remained unchanged.

The browser console contained no warning or error entries during this acceptance run.

## 11. Build verification

| Target | Result |
|---|---|
| Contracts + Server Release | PASS — 0 warnings, 0 errors |
| AdminWeb production | PASS — TypeScript + Vite |
| TouchClient Windows Player | PASS — Unity 2020.3.35f1c2, StandaloneWindows64 |
| LedPlayer Windows Player | PASS — Unity 2020.3.35f1c2, StandaloneWindows64; runtime LibVLC DLL copies present |

Unity build evidence is outside the repository at:

```text
C:/Users/A/AppData/Local/Temp/TG-Phase9C-Builds/
```

TouchClient, LedPlayer, `PlaybackCommand`, `PlaybackCoordinator`, `NarrationAudioPlayer`, TTS playback
ownership and the synchronization protocol were not modified.

## 12. Files changed

```text
README.md
docs/Architecture/Phase9C-Admin-TTS-Workflow.md
src/Shared/TG.Control.Contracts/ContentDraftContracts.cs
src/Server/TG.Control.Server/ContentRepository.cs
src/Server/TG.Control.Server/ContentDraftRepository.cs
src/Server/TG.Control.Server/ContentDraftWorkflowService.cs
src/Server/TG.Control.Server/Program.cs
src/AdminWeb/package.json
src/AdminWeb/src/api.ts
src/AdminWeb/src/main.ts
src/AdminWeb/src/style.css
src/AdminWeb/src/tts-workflow.ts
src/AdminWeb/tests/tts-workflow.test.ts
tests/TG.Control.Phase9C.Tests/TG.Control.Phase9C.Tests.csproj
tests/TG.Control.Phase9C.Tests/Program.cs
```

## 13. Phase 9D boundary

Phase 9D remains unimplemented and requires separate approval. Its expected boundary is limited to one or
more real `ITtsProvider` adapters and production configuration/secrets, formal voice catalog mapping, real
provider error mapping, supported output-format field verification and field audio-quality acceptance.

Phase 9D must reuse the Phase 9A binding, Phase 9B Job/Candidate production core and Phase 9C Admin workflow.
It must not bypass Candidate validation/adoption, auto-publish, move audio playback to TouchClient, or encode a
vendor branch into business services.

Phase 9C stops here.
