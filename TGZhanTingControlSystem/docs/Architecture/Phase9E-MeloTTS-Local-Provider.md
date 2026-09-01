# Phase 9E — MeloTTS Local Formal Provider

## 1. Scope and result

Phase 9E adds the first formal, offline TTS provider without changing the Phase 9A–9D domain lifecycle:

```text
AdminWeb
  -> existing TTS Job API
  -> TtsProductionService
  -> ITtsProvider Registry
  -> MeloTtsLocalProvider
  -> loopback-only Python Worker
  -> MeloTTS Chinese on CPU
  -> PCM WAV
  -> existing media validation / immutable ContentAsset / Candidate
  -> Preview -> Adopt -> Publish -> LED cache -> Playback
```

The work continued from frozen baseline
`082ae849178d915557b8950d42561429d99d009b`. No Azure, Edge TTS, CosyVoice, other model, playback protocol,
PlaybackCoordinator state-machine, TouchClient audio ownership, `NarrationAudioPlayer`, asset GC, or legacy batch
migration was introduced.

Overall Phase 9E result: **PASS with two explicitly BLOCKED environment-only acceptance items**. The formal MeloTTS
provider, Admin workflow, immutable asset path, publish/rollback path, real LedPlayer cache and playback, automated
regressions, and four product builds passed. A machine-wide physical-network disconnect could not be performed
without administrator rights, and physical TouchClient click automation remains blocked by the same Unity-window
capture limitation recorded in Phase 9D. Neither item is reported as PASS.

## 2. Frozen upstream and license

| Dependency | Frozen identity | Integrity / license |
|---|---|---|
| MeloTTS | v0.1.2, commit `b633f243412169b999526e19eb6fcac0974b5d30` | MIT; full license in `ThirdParty/MeloTTS-LICENSE.txt` |
| MeloTTS Chinese acoustic model | revision `082ca057e44f1e52ec47e1622a30286019e8a3ef` | `config.json` SHA `d58b5acd...c8bb9`; `checkpoint.pth` SHA `a74e9ead...7093` |
| multilingual BERT dependency | revision `7cbf9a625e29989f6b9c6c2fa68234c304f7e38f` | config/model/vocab SHA values frozen in Worker and `ThirdParty/NOTICE.md` |
| Python runtime | Windows embeddable CPython 3.10.11 x64 | archive SHA frozen by deployment script |

The deployment bundle retains Python package metadata. A future final installer must aggregate all runtime package
notices; Phase 9E intentionally does not build the final MSI.

## 3. Windows compatibility spike

The approved compatibility spike was retained as evidence and not rerun during the resumed work.

| Check | Status | Evidence |
|---|---|---|
| Windows isolated runtime | PASS | CPython 3.10.11 embeddable x64 |
| CPU inference | PASS | Actual MeloTTS v0.1.2 inference on i5-12400F, no NVIDIA requirement |
| Chinese + English + digits + punctuation | PASS | Real mixed-text WAV generated |
| Output format | PASS | Mono, 44.1 kHz, 16-bit PCM WAV |
| Actual voice catalog | PASS | Upstream Chinese model exposes exactly `ZH`; no invented speakers |
| Initial timing | PASS | 7.96-second audio generated in 7.17 seconds |
| Patch repeatability | PASS | `melotts-v0.1.2-windows-offline.patch` passed `git apply --check` on pristine v0.1.2 and patched Python compiled |

The minimal patch is confined to the Worker/deployment boundary. It:

- removes the obsolete `pip.req` setup dependency;
- accepts explicit local acoustic model paths instead of using the unavailable upstream S3 path;
- lazily imports unrelated language modules so Chinese startup does not load online-only models;
- loads the Chinese mixed-language BERT from a frozen local directory;
- lazily initializes the unrelated English tokenizer, avoiding an unnecessary online download;
- removes a Windows-incompatible dependency on the Japanese module from English text helpers.

No Phase 9A–9D Job, Candidate, Binding, Publish, manifest, or playback lifecycle code contains MeloTTS-specific
branches.

## 4. Provider contract and capabilities

`MeloTtsLocalProvider` implements the existing `ITtsProvider` and is resolved by `TtsProviderRegistry` under the
stable ID `melo-local`. AdminWeb discovers it through the existing `GET /api/tts/providers` API.

| Capability | Formal value |
|---|---|
| Display name | MeloTTS 本地中文 |
| Voice ID | `zh-standard` |
| Voice display name | 中文标准讲解 |
| Language | `zh-CN` (Chinese with supported English mixing) |
| Maximum request text | 5000 characters |
| Rate | 0.75–1.25 |
| Pitch | Not supported; Admin disables it and Server rejects non-zero values |
| Synthesis volume | Not supported; Server rejects values other than 1 |
| Media | `audio/wav` |
| Sample rate | Default and fixed at 44100 Hz |
| Channels | Default and fixed at mono |
| Development-only | No |

The additive Provider capabilities distinguish defaults from fixed output constraints. This prevents the formal
MeloTTS format from being applied to Providers such as the deterministic regression Provider that legitimately
support a range of PCM sample rates/channels.

The Provider serializes the request to a fixed UTF-8 byte body with `Content-Length`; this avoids Python
`BaseHTTPRequestHandler` treating .NET chunked transfer as an empty request. It streams the resulting WAV into the
existing Server validator. Worker-reported SHA, Size, and duration are not asset-integrity authority: the existing
media pipeline parses the WAV and recomputes final SHA-256 and Size before Candidate creation.

## 5. Local Worker

The source-controlled Worker is a small standard-library HTTP process. It binds only to a loopback address and
exposes:

- `GET /health`;
- `GET /voices`;
- `POST /synthesize`;
- `POST /requests/{requestId}/cancel`.

At startup it validates the exact model files and SHA-256 values, configures Hugging Face/Transformers/Datasets
offline modes, loads the frozen acoustic model and BERT directory, confirms the only speaker is `ZH`, and confirms
44.1 kHz output. Until this completes, `/health` reports unavailable and `/voices` exposes no usable voice.

Requests are validated for voice, language, rate, pitch, volume, media type, sample rate, channels, unique request
ID, empty text, and maximum length. Synthesis is serialized because the CPU model is a shared non-thread-safe
resource. Cancellation is observed between text chunks and is also propagated from Server by a best-effort local
cancel request.

Long narration is split first at Chinese sentence punctuation, then at phrase punctuation, and only then at token
boundaries. English words, version strings, decimal numbers, and abbreviations are kept intact when they fit the
chunk. Segments are joined into one final PCM WAV with 5 ms edge fades; no second asset is created per segment.

## 6. Worker supervision and failure recovery

`MeloTtsWorkerSupervisor` owns only the optional Python process. It does not own or duplicate TTS Jobs.

- It validates loopback configuration and required runtime/model files before startup.
- It starts the Worker hidden with offline environment flags.
- Standard output/error is captured by Server logging.
- Missing runtime/model files make the Provider unavailable; manual upload remains available.
- Unexpected Worker exit records an unavailable reason and schedules a bounded-delay restart.
- Server shutdown terminates the owned process tree.
- Server restart reloads persisted Jobs/Candidates/PublishedContent through the existing Phase 9B repositories.

Real failure tests:

| Scenario | Status | Result |
|---|---|---|
| Kill idle Worker | PASS | Provider became unavailable, supervisor started a new PID, model returned Ready |
| Kill Worker during a long synthesis | PASS | Request failed rather than producing a Candidate; supervisor recovered; current V3/Audio A remained unchanged |
| Missing runtime layout | PASS | Provider reported unavailable and exposed zero fake voices |
| Non-loopback URL | PASS | Rejected by Provider/supervisor boundary |
| Server restart | PASS | persisted formal Jobs/Candidates and Published V3 remained readable; Worker restarted |

Provider errors remain categorized through the existing finite retry policy. There is no infinite Worker or Job
retry, and a failed generation cannot alter the current Binding or PublishedContent.

## 7. Windows offline bundle

`scripts/Build-MeloTtsWindowsBundle.ps1` creates the deployable Worker directory on an internet-connected build
machine. It downloads only frozen artifacts, verifies SHA-256 before use, applies the repeatable compatibility patch,
installs pinned Windows CPU dependencies into the embeddable runtime, installs model/NLTK data, and writes a bundle
manifest.

Deployment layout beside the published Server:

```text
TtsWorker/MeloTtsLocal/
  worker.py
  bundle-manifest.json
  runtime/python.exe
  runtime/nltk_data/...
  vendor/MeloTTS/...
  models/MeloTTS-Chinese/{config.json,checkpoint.pth}
  models/bert-base-multilingual-uncased/{config.json,pytorch_model.bin,vocab.txt}
```

The customer machine does not install Python, run pip, download a model, or set environment variables. Runtime,
model, vendor, and generated manifest directories are intentionally ignored by Git. The source files needed to
describe/build the Worker are copied with Server build/publish output; the multi-gigabyte generated bundle is a
deployment artifact and is not committed.

| Deployment verification | Status | Note |
|---|---|---|
| PowerShell parser / frozen hashes | PASS | Script parsed; downloaded Spike artifacts matched frozen hashes |
| Patch on pristine source | PASS | `git apply --check`, apply, and Python compile succeeded |
| Self-contained runtime layout | PASS | Real isolated Python/model/NLTK layout started and synthesized without package installation at runtime |
| Final MSI | NOT RUN | Explicitly outside Phase 9E |

## 8. Offline verification

The Worker was started with all supported offline flags and `HTTP_PROXY`, `HTTPS_PROXY`, and `ALL_PROXY` pointed to
an unreachable loopback endpoint. Only loopback was allowed through `NO_PROXY`.

Status: **PASS (process-enforced offline)**

```text
audio duration: 7.027596 seconds
synthesis time: 7.179533 seconds
size: 619878 bytes
SHA256: 6b4ad...f73d
external TCP connections observed for Worker PID: 0
```

Evidence WAV:
`C:\Users\A\AppData\Local\Temp\TG-Phase9E-Formal\Evidence\offline-verification.wav`.

Status: **BLOCKED (machine-wide physical disconnect/firewall rule)**

Creating a Windows outbound firewall rule was denied because the current process did not have administrator rights.
The physical adapter was therefore not disabled and this stronger test is not reported as PASS. The process-enforced
test and connection inspection prove the synthesis itself used the bundled local dependencies, but the final
installer/site acceptance should repeat the complete Admin workflow with the Windows host physically disconnected.

## 9. CPU performance

Measured on Windows/i5-12400F, 6 cores/12 threads, CPU inference, frozen Chinese model. RTF is synthesis time divided
by produced audio duration; no target was invented.

| Sample | Text length | Audio duration | Synthesis time | RTF | Observed Worker memory |
|---|---:|---:|---:|---:|---:|
| About 10 seconds | 47 chars | 8.726 s | 4.416 s | 0.506 | 1855.4 MiB working set |
| About 30 seconds | 152 chars | 26.616 s | 13.192 s | 0.496 | 1993.6 MiB working set |
| About 60 seconds | 370 chars | 65.797 s | 32.693 s | 0.497 | 2010.7 MiB working set |
| Real exhibit long narration | 545 chars | 98.050 s | 49.189 s | 0.502 | 1997.1 MiB working set |

The process historical peak during the run was about 2550.4 MiB. This is a deployment sizing fact, not a guaranteed
limit. The Server TTS attempt timeout is now 300 seconds so real long CPU narration is not killed by the former
30-second test-oriented timeout; cancellation and finite retry behavior remain unchanged.

## 10. Formal Admin workflow

The actual AdminWeb was opened in a browser against the formal Server with the development test Provider disabled.

- The dynamic Provider list displayed `MeloTTS 本地中文` and `中文标准讲解`.
- Pitch was disabled with an explicit unsupported message.
- Generate showed real Job state, then Candidate state.
- Browser playback changed `播放试听` to `暂停试听`, confirming actual Candidate URL playback.
- Adopt made the Draft Binding Fresh and did not publish.
- Explicit Publish created the next immutable version.
- Changing NarrationText immediately produced `StaleText` and blocked Publish until a new Candidate was adopted.

The Admin controller also now transparently re-enters the existing bounded Server retry path when a page reload first
returns an idempotent Failed/Cancelled Job. This fixes a formal-provider integration issue where the operator
previously needed to click Generate twice after a reload; it does not change Server retry limits.

## 11. Real MeloTTS V1 -> V2 -> rollback V1

All audio below was produced by real MeloTTS, not `DeterministicTestTtsProvider`.

### V1 / Audio A

```text
NarrationText: 欢迎来到智慧展厅。这是MeloTTS正式本地语音第一版，系统将为您介绍企业发展与科技创新成果。
AssetId: 0a9672fbf0eb422d9f10b53fe6e4c098
URL: /media/2731885479424aeb804a1f4ee408043a.wav
SHA256: 36829a1152ba275a3624489304f57ee770851d32bc6dd177346976fb534ab011
Size: 745120
Duration: 8.447573696 seconds
MediaType: audio/wav
```

LedPlayer reached V1/Ready, downloaded the exact asset, and playback session
`4cc7c6fca0b7452884491496e9fc3dd9` reported `Received -> Ready -> Playing -> Completed`, 0 ms start drift, and
8.44757366 s completed position.

### V2 / Audio B

```text
NarrationText: 欢迎来到智慧展厅。这是MeloTTS正式本地语音第二版，数智创新正在引领企业绿色低碳发展。
AssetId: a55855b9c2214a049cde4cfbaa52c189
URL: /media/7ce8a79b054f4a7ca2409c612ebb1ddc.wav
SHA256: be176a411dc3c8ffd1ddcf79eba4711d85aeae037611833e8edb70542cf9ea44
Size: 685728
MediaType: audio/wav
```

Text A -> B first produced a Server-authoritative stale state. Generate, actual browser preview, explicit Adopt, and
explicit Publish created V2. Playback session `eb2d7b7ba864404fb35f85714e46a75e` completed Audio B through the real
LedPlayer.

### Rollback V1 -> immutable V3

Admin selected historical V1 and produced V3 with `PublishedBy: admin（回滚自 V1）`. PublishedContent restored text A
and the exact Audio A AssetId/URL/SHA/Size. LedPlayer reached V3/Ready and session
`331b04de467c49ab97c8d1768ae46344` again reported `Received -> Ready -> Playing -> Completed`, 0 ms drift, and
8.4475736618042 s completed position. Audio B and its Candidate remained intact for history/audit.

The formal data snapshots remain under
`C:\Users\A\AppData\Local\Temp\TG-Phase9E-Formal\Data`; player logs are under its `Evidence` directory.

## 12. LED cache integrity

| Scenario | Status | Evidence |
|---|---|---|
| Initial Audio A download | PASS | Manifest Size/SHA matched stored cache file |
| Audio B download after V2 | PASS | Distinct immutable asset cached and played |
| Rollback manifest restores Audio A | PASS | V3 Ready and playback completed Audio A |
| Restart reuses valid Audio A cache | PASS | Size/SHA/timestamp unchanged; V3 sync completed 1/1 without redownload |
| Corrupt cache recovery | PASS | Exact cache reduced to five bytes; after LedPlayer restart it redownloaded to 745120 bytes and exact Audio A SHA |

LedPlayer validates on content synchronization/startup. It does not continuously background-rehash a cache file while
the same version remains Ready; this existing behavior was not changed in Phase 9E.

## 13. Automated regression

All final suites ran in Release configuration on 2026-09-01.

| Suite | Status | Result |
|---|---|---|
| Phase 9A fingerprint/binding/legacy/manifest | PASS | 20/20 |
| Phase 9B Provider/Job/Candidate/persistence | PASS | 23/23 |
| Phase 9C Draft/Adopt/concurrency | PASS | 11/11 |
| Phase 9D publish/rollback/manifest/cache policy | PASS | 25/25 |
| Phase 9E Provider boundary | PASS | 6/6 |
| MeloTTS Worker text normalization/splitting | PASS | 3/3 |
| AdminWeb TTS workflow | PASS | 9/9 |
| Product-code test failures | PASS | 0 failures |

## 14. Build result

| Target | Status | Evidence |
|---|---|---|
| Server Release + embedded AdminWeb | PASS | 0 warnings, 0 errors |
| AdminWeb production | PASS | TypeScript and Vite production build |
| TouchClient Windows x64 | PASS | Unity 2020.3.35f1c2 `Build Successful` |
| LedPlayer Windows x64 | PASS | Unity 2020.3.35f1c2 `Build Successful`; LibVLC post-build validation passed |

The visible TouchClient editor was not forcibly closed. Its current source plus the relative UnityContracts package
was copied to a temporary build-only project for verification. Final logs:

```text
C:\Users\A\AppData\Local\Temp\TG-Phase9E-Final\TouchClient-build.log
C:\Users\A\AppData\Local\Temp\TG-Phase9E-Final\LedPlayer-build.log
```

## 15. Acceptance ledger

| Item | Status | Note |
|---|---|---|
| Formal MeloTTS Provider through `ITtsProvider` | PASS | No supplier branch in Job/Candidate/Publish/playback lifecycle |
| Dynamic Provider/Voice discovery | PASS | One real Chinese voice only |
| Capability-aware Admin controls | PASS | Unsupported Pitch/Volume are not presented as functional |
| PCM WAV validation and immutable Candidate asset | PASS | Server recomputed duration/SHA/Size |
| Worker lifecycle and crash recovery | PASS | Idle and in-synthesis crash tested |
| Process-enforced offline synthesis | PASS | Offline flags, dead proxies, zero external Worker connections |
| Machine-wide physically disconnected Admin workflow | BLOCKED | Windows firewall/adapter change required administrator rights |
| Admin Generate/Preview/Adopt/Publish | PASS | Actual browser workflow using formal MeloTTS |
| V1 -> V2 -> rollback V1 | PASS | Real formal audio assets and playback sessions |
| Server + AdminWeb + TouchClient + LedPlayer concurrent chain | PASS | Real processes registered; LED played both versions and rollback |
| TouchClient physical UI click automation | BLOCKED | Existing Unity window-capture limitation; authenticated operator start path and real clients were exercised |
| Manual narration upload and legacy compatibility | PASS | 9A–9D regression suites remain green |
| Development Test Provider isolation | PASS | Still Development-only and requires explicit enable flag |
| Final MSI / model GC / Azure | NOT RUN | Outside approved Phase 9E |

## 16. Changed files

### Contracts and Server

- `src/Shared/TG.Control.Contracts/TtsProductionContracts.cs`
- `src/Server/TG.Control.Server/Options.cs`
- `src/Server/TG.Control.Server/Program.cs`
- `src/Server/TG.Control.Server/MeloTtsLocalProvider.cs`
- `src/Server/TG.Control.Server/MeloTtsWorkerSupervisor.cs`
- `src/Server/TG.Control.Server/TtsProductionService.cs`
- `src/Server/TG.Control.Server/DeterministicTestTtsProvider.cs`
- `src/Server/TG.Control.Server/TG.Control.Server.csproj`
- `src/Server/TG.Control.Server/appsettings.json`

### Worker and deployment

- `.gitignore`
- `src/TtsWorker/MeloTtsLocal/worker.py`
- `src/TtsWorker/MeloTtsLocal/test_worker.py`
- `src/TtsWorker/MeloTtsLocal/melotts-v0.1.2-windows-offline.patch`
- `src/TtsWorker/MeloTtsLocal/requirements-windows-cpu.txt`
- `src/TtsWorker/MeloTtsLocal/README.md`
- `scripts/Build-MeloTtsWindowsBundle.ps1`
- `ThirdParty/NOTICE.md`
- `ThirdParty/MeloTTS-LICENSE.txt`

### AdminWeb, tests, and documentation

- `src/AdminWeb/src/api.ts`
- `src/AdminWeb/src/main.ts`
- `src/AdminWeb/src/style.css`
- `src/AdminWeb/src/tts-workflow.ts`
- `src/AdminWeb/tests/tts-workflow.test.ts`
- `tests/TG.Control.Phase9E.Tests/TG.Control.Phase9E.Tests.csproj`
- `tests/TG.Control.Phase9E.Tests/Program.cs`
- `docs/Architecture/Phase9E-MeloTTS-Local-Provider.md`

## 17. Remaining product/deployment considerations

- CPU Worker memory is approximately 2.0 GiB during long synthesis, with a measured historical peak around 2.55 GiB.
- Only one actual Chinese speaker exists in the frozen model; no male/female catalog is fabricated.
- Pitch and synthesis-volume controls are unsupported.
- Final customer deployment still needs installer packaging and aggregated dependency notices.
- Site acceptance should repeat the end-to-end workflow with the physical network disconnected and with an operator
  pressing the real TouchClient control.
- LedPlayer cache integrity is checked during synchronization/startup, not by continuous background scanning.

Phase 9E stops here. Phase 9F, Azure, other Providers, asset GC, and playback evolution are not implemented.
