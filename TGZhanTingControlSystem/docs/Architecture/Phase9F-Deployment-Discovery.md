# Phase 9F — Windows Deployment Discovery

Baseline: `510a6070dc25a303aec48d40e36e376692747e35`

Scope: discovery and deployment design only. This document does not implement an installer, change product code,
change playback protocol, add Asset GC, or add a second TTS Provider.

## 1. Current State

The current product is deployable as four runtime applications plus shared data:

| Runtime | Current implementation | Current deployment maturity |
| --- | --- | --- |
| Server | ASP.NET Core 8 executable, hosts APIs, AdminWeb static files, content repository, playback coordination, TTS job system, MeloTTS provider supervision | Service-capable, but no installer/service registration script yet |
| AdminWeb | TypeScript/Vite static application copied into Server output as `AdminWeb` | Runtime-ready once built; no Node runtime needed on customer machine |
| TouchClient | Unity 2020.3.35f1c2 Windows x64 Player for 55-inch touch terminal | Runtime-ready Player output exists; lacks external site config |
| LedPlayer | Unity 2020.3.35f1c2 Windows x64 Player with AVPro/UniversalMediaPlayer/LibVLC dependencies | Runtime-ready Player output exists; native DLL layout is sensitive and must be copied intact |
| MeloTTS Worker | Loopback-only Python Worker launched by Server through `MeloTtsWorkerSupervisor` | Provider validated in Phase 9E; full offline bundle is generated artifact, not committed |

Current important facts:

- Current Git worktree was clean before this discovery document was added.
- Current HEAD matched the frozen Phase 9E baseline.
- No `artifacts/server-win-x64` publish directory was present during audit.
- Actual Server Release build output was inspected at `src/Server/TG.Control.Server/bin/Release/net8.0`.
- Phase 9E Windows Player outputs were inspected at `C:\Users\A\AppData\Local\Temp\TG-Phase9E-Final`.
- Current local ports `5080` and `5091` were not listening at audit time; port inventory below is based on source configuration.

## 2. Runtime Dependency Inventory

High-level runtime inventory:

| Area | Customer runtime dependency | Build/package dependency | Current status |
| --- | --- | --- | --- |
| Server | .NET 8 ASP.NET Core Runtime unless self-contained publish is chosen | .NET SDK | framework-dependent today |
| AdminWeb | browser only; files served by Server | Node/npm for `npm ci` and Vite build | production dist copied into Server output |
| TouchClient | bundled Unity Player runtime | Unity 2020.3.35f1c2 | Windows x64 Player output exists |
| LedPlayer | bundled Unity Player runtime plus AVPro/UniversalMediaPlayer/LibVLC native DLLs | Unity 2020.3.35f1c2 and licensed video plugins | Windows x64 Player output exists |
| MeloTTS Worker | bundled CPython 3.10.11 runtime, Python packages, MeloTTS source, models, NLTK data | internet-connected build machine, pip, Git patch step | validated in Phase 9E; final bundle not installed in repo |
| Site operation | Windows account/session, audio output, LAN access to Server | installer/launcher not implemented yet | pending Phase 9G |

### 2.1 Server Release actual output structure and dependencies

Current inspected Server Release output:

```text
src/Server/TG.Control.Server/bin/Release/net8.0/
  TG.Control.Server.exe
  TG.Control.Server.dll
  TG.Control.Server.deps.json
  TG.Control.Server.runtimeconfig.json
  TG.Control.Contracts.dll
  appsettings.json
  AdminWeb/
  TtsWorker/
  runtimes/
  Data/
    Media/
    TtsStaging/
```

Measured current build-output size:

| Path | Size |
| --- | ---: |
| `src/Server/TG.Control.Server/bin/Release/net8.0` | 1.94 MiB |
| `src/Server/TG.Control.Server/bin/Release/net8.0/AdminWeb` | 0.07 MiB |
| `src/Server/TG.Control.Server/bin/Release/net8.0/TtsWorker` | 0.03 MiB |

The small Server size is because current output is framework-dependent and does not include the generated MeloTTS
offline runtime/model bundle.

Current Server publish script:

```powershell
dotnet publish src\Server\TG.Control.Server\TG.Control.Server.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained false `
  --output artifacts\server-win-x64
```

Deployment implication:

- Current publish mode requires .NET 8 runtime on the customer machine.
- `TG.Control.Server.runtimeconfig.json` requires:
  - `Microsoft.NETCore.App` 8.0.x
  - `Microsoft.AspNetCore.App` 8.0.x
- Current development machine has .NET 8.0.29 runtime, but the customer machine cannot be assumed to have it.
- Phase 9G should choose one formal strategy:
  - install .NET 8 Hosting Bundle / ASP.NET Core Runtime as prerequisite; or
  - publish Server self-contained `win-x64` and accept larger binary size.

## 3. Server Windows Service support

Server currently references `Microsoft.Extensions.Hosting.WindowsServices` and calls:

```csharp
builder.Host.UseWindowsService(options => options.ServiceName = "TG Exhibition Control Server");
```

This means the executable can run correctly under Windows Service Control Manager after service registration.

What is not implemented yet:

- no `New-Service` / `sc.exe create` script;
- no service uninstall script;
- no service recovery policy;
- no service account/ACL setup;
- no upgrade-safe stop/start workflow;
- no EventLog source provisioning script;
- no formal health check after service start.

Conclusion: Server is service-capable, not installer-ready.

## 4. AdminWeb production build and Server hosting

AdminWeb is currently integrated into the Server build.

`src/Server/TG.Control.Server/TG.Control.Server.csproj` runs, unless `SkipAdminWebBuild=true`:

```text
npm ci --no-audit --no-fund
npm run build
copy src/AdminWeb/dist/** -> ServerOutput/AdminWeb
copy src/AdminWeb/dist/** -> PublishDir/AdminWeb
```

`src/AdminWeb/package.json`:

```json
{
  "scripts": {
    "dev": "vite --host 0.0.0.0",
    "build": "tsc && vite build",
    "test": "node --test tests/tts-workflow.test.ts",
    "preview": "vite preview --host 0.0.0.0"
  }
}
```

Server startup sets:

```csharp
WebRootPath = Path.Combine(AppContext.BaseDirectory, "AdminWeb")
```

and serves:

- static files from `AdminWeb`;
- SPA fallback to `index.html`;
- `/media` from the mutable media store.

AdminWeb API base:

- production default is `window.location.origin`;
- optional build-time override is `VITE_API_BASE_URL`.

Deployment implication:

- customer machine does not need Node/npm when using a prebuilt Server publish output;
- build machine currently needs Node/npm because Server build invokes AdminWeb build;
- AdminWeb must be versioned together with Server because it is copied into Server output.

## 5. TouchClient Windows Build structure and dependencies

Phase 9E inspected TouchClient Player output:

```text
C:\Users\A\AppData\Local\Temp\TG-Phase9E-Final\TouchClient/
  TouchClient.exe
  UnityPlayer.dll
  UnityCrashHandler64.exe
  TouchClient_Data/
    Managed/
    Resources/
    level0
    globalgamemanagers
  MonoBleedingEdge/
```

Measured size: 58.94 MiB.

Unity project facts:

- Unity version: `2020.3.35f1c2`.
- Product name: `TouchClient`.
- Company name: `DefaultCompany`.
- Default resolution: `1920x1080`.
- Runtime bootstrap sets `Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow)`.
- Runtime bootstrap creates `TouchApiClient`, `TouchControlFacade`, and `TouchOperatorUi`.

Current runtime configuration limitation:

- `TouchApiClient` defaults are serialized in code/component:
  - `serverBaseUrl = "http://127.0.0.1:5080"`
  - `clientId = "touch-main"`
  - `terminalApiKey = "TG-TERMINAL-2026"`
- No external runtime config file was found for changing Server IP/key/client ID after build.

Deployment implication:

- If TouchClient runs on the same Windows host as Server, the default `127.0.0.1:5080` works.
- If TouchClient runs on a separate touch terminal, current build requires preconfiguring the Unity serialized fields
  before build or adding a Phase 9G runtime config mechanism.
- The current `DefaultCompany` value also affects Unity persistent/log paths and should be corrected before formal
  installer acceptance.

## 6. LedPlayer Windows Build structure and native dependencies

Phase 9E inspected LedPlayer Player output:

```text
C:\Users\A\AppData\Local\Temp\TG-Phase9E-Final\LedPlayer/
  LedPlayer.exe
  UnityPlayer.dll
  UnityCrashHandler64.exe
  LedPlayer_Data/
    Managed/
    Resources/
    Plugins/
      libvlc.dll
      libvlccore.dll
      x86_64/
        AVProVideo.dll
        UniversalMediaPlayer.dll
        libvlc.dll
        libvlccore.dll
      plugins/
        access/
        audio_filter/
        audio_mixer/
        audio_output/
        codec/
        d3d11/
        d3d9/
        demux/
        ...
  MonoBleedingEdge/
```

Measured size: 160.99 MiB.

The inspected `LedPlayer_Data/Plugins` tree contained 280 DLL files. The root-level copies of `libvlc.dll` and
`libvlccore.dll` are intentionally created by `src/LedPlayer/Assets/Editor/WindowsPlayerBuilder.cs` after build,
because runtime lookup previously failed when those DLLs existed only below `Plugins/x86_64`.

Unity project facts:

- Unity version: `2020.3.35f1c2`.
- Product name: `LedPlayer`.
- Company name: `DefaultCompany`.
- Default resolution: `1920x1080`.
- Runtime bootstrap sets `Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow)`.
- Runtime bootstrap creates `LedApiClient`, `UniversalMediaPlayer`, `UniversalMediaPlaybackAdapter`,
  `AudioSource`, `LedPlaybackController`, and `LedStatusOverlay`.

Current runtime configuration limitation:

- `LedApiClient` defaults are serialized in code/component:
  - `serverBaseUrl = "http://127.0.0.1:5080"`
  - `clientId = "led-main"`
  - `terminalApiKey = "TG-TERMINAL-2026"`
- No external runtime config file was found for changing Server IP/key/client ID after build.

Deployment implication:

- LedPlayer must be copied as a complete directory, not as a single executable.
- AVPro/UniversalMediaPlayer/LibVLC native dependencies are part of the LedPlayer runtime package.
- Clean-machine testing must include H.264/MP4 playback, audio device output, and the exact deployed DLL layout.

## 7. MeloTTS Worker runtime dependency inventory

MeloTTS is integrated as one `ITtsProvider` implementation:

```text
AdminWeb
  -> Server TTS Job API
  -> TtsProductionService
  -> TtsProviderRegistry
  -> MeloTtsLocalProvider
  -> loopback-only Python Worker
  -> PCM WAV
  -> TtsMediaValidator
  -> ContentAsset / Candidate
```

MeloTTS does not change Job/Candidate/Publish/Playback/LedPlayer domain lifecycle.

Source-controlled Worker files:

```text
src/TtsWorker/MeloTtsLocal/
  worker.py
  test_worker.py
  melotts-v0.1.2-windows-offline.patch
  requirements-windows-cpu.txt
  README.md
```

Generated bundle files are intentionally ignored by Git:

```text
src/TtsWorker/MeloTtsLocal/runtime/
src/TtsWorker/MeloTtsLocal/models/
src/TtsWorker/MeloTtsLocal/vendor/
src/TtsWorker/MeloTtsLocal/bundle-manifest.json
```

Formal source/model versions frozen in Phase 9E:

| Item | Version / revision |
| --- | --- |
| CPython embedded runtime | 3.10.11 x64 |
| MeloTTS | v0.1.2 |
| MeloTTS commit | `b633f243412169b999526e19eb6fcac0974b5d30` |
| MeloTTS Chinese acoustic model revision | `082ca057e44f1e52ec47e1622a30286019e8a3ef` |
| multilingual BERT revision | `7cbf9a625e29989f6b9c6c2fa68234c304f7e38f` |
| Formal provider ID | `melo-local` |
| Formal voice ID | `zh-standard` |
| Actual upstream speaker | `ZH` |
| Output format | mono 44.1 kHz 16-bit PCM WAV |

Direct pinned Python dependencies include:

- `torch==1.13.1+cpu`
- `torchaudio==0.13.1+cpu`
- `numpy==1.23.5`
- `scipy==1.15.3`
- `numba==0.67.0`
- `llvmlite==0.49.0`
- `librosa==0.9.1`
- `soundfile==0.14.0`
- `transformers==4.27.4`
- `tokenizers==0.13.3`
- `huggingface-hub==0.36.2`
- `pypinyin==0.50.0`
- `cn2an==0.5.22`
- `jieba==0.42.1`
- `g2p-en==2.1.0`
- `eng-to-ipa==0.0.2`
- `nltk==3.8.1`
- `num2words==0.5.12`
- `inflect==7.0.0`
- `Unidecode==1.3.7`
- `anyascii==0.3.2`
- `langid==1.1.6`
- `txtsplit==1.0.0`
- `tqdm==4.70.0`
- `regex==2023.12.25`

Observed key transitive/runtime dependencies in the Phase 9E Spike runtime included:

- `cached_path==1.8.10`
- `mecab-python3==1.0.5`
- `fugashi==1.3.0`
- `unidic-lite==1.0.8`
- `requests==2.34.2`
- `urllib3==2.7.0`
- `certifi==2026.7.22`
- `cffi==2.1.1`
- `uvicorn==0.52.4`
- `fastapi==0.141.1`
- `gradio==6.17.3`
- `boto3==1.43.85`

Some of these are transitive and not directly used by the local Worker API, but they are present because they are
installed through the MeloTTS dependency set. Phase 9G packaging must retain required runtime metadata and license
notices, and can later optimize only after a clean regression cycle.

## 8. MeloTTS complete offline runtime size

The full source-controlled repository does not contain the generated offline runtime/model bundle. This is intentional.

Measured current source/build facts:

| Path | Status | Size |
| --- | --- | ---: |
| `src/TtsWorker/MeloTtsLocal` | source-controlled adapter only | 0.05 MiB |
| `src/Server/TG.Control.Server/bin/Release/net8.0/TtsWorker/MeloTtsLocal` | copied adapter only | 0.03 MiB |
| `C:\Users\A\AppData\Local\Temp\TG-Phase9E-Melo-Spike` | Phase 9E Spike runtime/evidence directory | 3200.47 MiB |
| `C:\Users\A\AppData\Local\Temp\TG-Phase9E-Melo-Spike\Python310Embed` | CPython embedded runtime plus installed Python packages | 2264.55 MiB |
| `C:\Users\A\AppData\Local\Temp\TG-Phase9E-Melo-Spike\MeloTTS-0.1.2-patched` | patched upstream source | 21.60 MiB |
| `C:\Users\A\AppData\Local\Temp\TG-Phase9E-Melo-Spike\nltk_data` | NLTK data | 12.77 MiB |
| `C:\Users\A\AppData\Local\Temp\TG-Phase9E-Melo-Spike\hf-cache\hub` | Hugging Face model cache captured during Spike | 843.88 MiB |

Important limitation:

- The Spike runtime directory is temporary evidence, not a formal install layout.
- The formal bundle expected by Server is `TtsWorker/MeloTtsLocal` beside the Server executable, with
  `runtime/`, `vendor/`, `models/`, `nltk_data`, and `bundle-manifest.json`.
- Phase 9G must produce a stable bundle directory, measure it again, and run clean-machine/offline acceptance against
  that directory.

Additional packaging risk found during discovery:

- The Spike Hugging Face cache contained not only the approved Chinese acoustic model and multilingual BERT, but also
  several upstream language-model cache directories such as `bert-base-uncased`, Japanese, French, Spanish, and Korean
  BERT caches.
- This likely came from upstream MeloTTS import/dependency behavior during compatibility work.
- Phase 9G must verify that the final patched Worker does not depend on developer-machine Hugging Face cache contents.
  If it still does, the installer must either vendor every actually required snapshot with hashes or the Worker patch
  must be tightened at the Worker/deployment-adapter boundary.

## 9. Current working-directory and relative-path dependencies

Server:

| Area | Current rule | Deployment implication |
| --- | --- | --- |
| AdminWeb static files | `AppContext.BaseDirectory/AdminWeb` | independent of process working directory |
| MeloTTS Worker paths | resolved against `AppContext.BaseDirectory` | suitable for Program Files immutable bundle |
| Storage `DataDirectory` | `Path.GetFullPath(value, environment.ContentRootPath)` | dangerous if default `Data` points inside install/output directory |
| Manual console start | content root can follow current working directory | must set explicit DataDirectory in formal deployment |
| Windows Service start | `UseWindowsService` normally uses application base directory as content root | still must not use relative `Data` under Program Files |

TouchClient:

- Runtime configuration is embedded/serialized, not externalized.
- No product script writes customer data except Unity's normal Player logs.
- Runtime logs are expected under Unity's `Application.persistentDataPath` / LocalLow path because company/product
  names are currently `DefaultCompany/TouchClient`.

LedPlayer:

- Runtime configuration is embedded/serialized, not externalized.
- Media cache writes to:

```text
Application.persistentDataPath/Content
```

With current project settings this is expected under:

```text
%USERPROFILE%\AppData\LocalLow\DefaultCompany\LedPlayer\Content
```

Deployment implication:

- This is writable for the interactive user and survives application binary upgrades.
- It is not ideal for formal kiosk deployment because it is tied to Windows user profile and current `DefaultCompany`
  value.
- Phase 9G should decide whether to keep this behavior for V1 or add a supported external cache directory setting,
  preferably under ProgramData.

MeloTTS Worker:

- Server starts Worker with `WorkingDirectory` set to the Worker script directory.
- Server passes explicit model/source/NLTK paths as arguments.
- Server sets offline flags:
  - `HF_HUB_OFFLINE=1`
  - `TRANSFORMERS_OFFLINE=1`
  - `HF_DATASETS_OFFLINE=1`
  - `NO_PROXY=127.0.0.1,localhost`
  - `PYTHONUTF8=1`
- Worker itself does not intentionally write runtime data, but Python/upstream libraries may attempt:
  - `__pycache__` writes beside Python source files;
  - Hugging Face cache lookups/writes if any upstream model ID is not fully converted to an explicit local path;
  - tokenizer/cache writes depending on upstream English/mixed-text path.

Deployment implication:

- Installing Worker source/vendor under Program Files is acceptable only if Phase 9G proves no runtime write is attempted
  there, or starts Python with write-bytecode/caches disabled/redirected.

## 10. Mutable Data Inventory

Current Server mutable data under `Storage:DataDirectory`:

| File/folder | Owner | Purpose | Preserve on upgrade | Preserve on uninstall by default |
| --- | --- | --- | --- | --- |
| `published-content.json` | Server | current immutable published content snapshot | yes | yes |
| `ContentVersions/content-v########.json` | Server | rollback history | yes | yes |
| `content-draft.json` | Server/AdminWeb | editable draft and revision state | yes | yes |
| `narration-routes.json` | Server/Touch/AdminWeb | common narration routes | yes | yes |
| `ui-experience.json` | Server/AdminWeb/Touch/Led | current configurable UI titles/backgrounds/idle media | yes | yes |
| `tts-production.json` | Server TTS production | Jobs and Candidates | yes | yes |
| `active-playback-session.json` | Server playback | active session recovery | yes during upgrade; normally removable only when idle | no after confirmed uninstall |
| `operational-events.jsonl` | Server operations | audit/diagnostic operation log | yes | optional, recommend preserve/export |
| `Media/` | Server assets | uploaded videos/images/audio and generated TTS WAV assets | yes | yes |
| `TtsStaging/` | Server TTS validation | temporary WAV validation files | no, can clean when Server stopped | no |

Current AdminWeb browser-side mutable data:

| Storage | Key | Purpose |
| --- | --- | --- |
| `sessionStorage` | `tg-admin-token` | current login token |
| `localStorage` | `tg-content-draft-v1` | browser crash-refresh draft copy for same revision |

Current LedPlayer mutable data:

| Path | Purpose | Preserve on upgrade | Preserve on uninstall by default |
| --- | --- | --- | --- |
| `%USERPROFILE%\AppData\LocalLow\DefaultCompany\LedPlayer\Content` | video/audio/image cache downloaded from Server manifest | yes, recommended because videos are large | optional; preserve unless user requests full removal |
| `%USERPROFILE%\AppData\LocalLow\DefaultCompany\LedPlayer\Player.log` | Unity runtime log | optional | no |

Current TouchClient mutable data:

| Path | Purpose | Preserve on upgrade | Preserve on uninstall by default |
| --- | --- | --- | --- |
| `%USERPROFILE%\AppData\LocalLow\DefaultCompany\TouchClient\Player.log` | Unity runtime log | optional | no |

Current runtime data written into application output:

- Because `Storage:DataDirectory` defaults to `Data`, local execution from build output already created:

```text
src/Server/TG.Control.Server/bin/Release/net8.0/Data/Media
src/Server/TG.Control.Server/bin/Release/net8.0/Data/TtsStaging
```

This is acceptable for development but not for Program Files deployment. Formal deployment must override Server storage
to ProgramData.

## 11. Process Topology

Current startup behavior:

```text
TG.Control.Server.exe
  - starts ASP.NET Core APIs/AdminWeb on 5080
  - loads repositories from Storage:DataDirectory
  - starts TtsProductionService
  - if MeloTtsLocal.Enabled and AutoStartWorker:
      starts runtime/python.exe worker.py on 127.0.0.1:5091

TouchClient.exe
  - registers as touch-main
  - polls/long-polls Server
  - starts narration routes manually
  - controls playback session operations

LedPlayer.exe
  - registers as led-main
  - syncs /api/content/manifest
  - downloads media into local cache
  - plays video through UniversalMediaPlayer/LibVLC
  - plays narration audio through Unity AudioSource
```

Current crash/exit behavior:

| Process | Current behavior |
| --- | --- |
| Server manual process exits/crashes | no repository-level data deletion; no built-in external watchdog unless installed as service with recovery |
| Server as Windows Service | application supports SCM lifetime, but recovery policy is not configured yet |
| TTS Worker idle crash | `MeloTtsWorkerSupervisor` records unavailable reason and restarts after configured delay |
| TTS Worker crash during synthesis | current Job fails; Candidate/PublishedContent are not corrupted; Worker restarts |
| TouchClient crash | no current watchdog; after manual restart it registers and reads active session/readiness through existing APIs |
| LedPlayer crash | no current watchdog; after manual restart it registers, validates cache, syncs content, and can recover active playback state |

## 12. Port/Network Inventory

| Port/address | Owner | Current source default | Scope | Firewall need |
| --- | --- | --- | --- | --- |
| `http://0.0.0.0:5080` | Server/Admin/API/media | `appsettings.json` `Urls` | LAN reachable from all interfaces | inbound TCP 5080 required when Touch/Led/Admin are remote |
| `http://127.0.0.1:5091` | MeloTTS Worker | `MeloTtsLocal:BaseAddress` | loopback only | no external inbound rule; should not be opened to LAN |
| outbound HTTP to Server 5080 | TouchClient/LedPlayer/Admin browser | serialized client defaults and browser origin | local or LAN | outbound usually allowed; locked-down kiosks may need allow rules |

Security observations:

- Terminal APIs use `X-TG-Terminal-Key`.
- Admin APIs use session bearer token after `/api/auth/login`.
- Default credentials and terminal key are development defaults and must become site configuration.
- Current Server CORS policy allows any origin. Because AdminWeb is normally same-origin, Phase 9G should decide whether
  this remains acceptable for an isolated exhibition LAN or should be tightened for formal deployment.

## 13. Product defaults vs customer site configuration

Should remain product defaults:

- Server API route structure.
- AdminWeb served from Server `AdminWeb`.
- Default media validation rules.
- Playback lead/sync default values unless site testing requires tuning:
  - `PrepareLeadMilliseconds=1500`
  - `SyncToleranceMilliseconds=500`
  - `LongPollSeconds=20`
- MeloTTS formal provider ID and voice ID:
  - `melo-local`
  - `zh-standard`
- MeloTTS fixed output:
  - `audio/wav`
  - 44.1 kHz
  - mono
- TTS max attempts/retry policy defaults.

Must be customer/site configuration:

- Server listen URL / LAN IP / host name.
- Admin username/password.
- Terminal API key.
- TouchClient server URL.
- LedPlayer server URL.
- Touch/Led client IDs if more than one terminal exists.
- Server data directory.
- Media/cache/log paths if moved to ProgramData.
- MeloTTS bundle directory if installed outside Server base directory.
- Firewall rule enablement.
- Windows service name/display name/recovery policy if customer has naming standards.
- Startup mode for Touch and Led clients.

## 14. Deployment Risks

| Risk | Severity | Evidence | Recommendation |
| --- | --- | --- | --- |
| Runtime data may be written under install directory | high | `Storage:DataDirectory` default is relative `Data`; Release output already contains `Data/Media` and `Data/TtsStaging` | Phase 9G must set DataDirectory to ProgramData and migrate existing data |
| Unity clients lack external config | high | Touch/Led default Server URL/key/client ID are serialized defaults | add runtime config file or launcher-injected config before formal deployment |
| Full MeloTTS bundle absent from current output | high | Server output contains only Worker adapter files | installer/build pipeline must generate/copy full `runtime/vendor/models/nltk_data` bundle |
| MeloTTS may depend on developer HF cache | high | Spike HF cache contained multiple upstream model snapshots beyond intended files | clean-machine offline test must validate final bundle with no user HF cache |
| Python/upstream imports may write beside vendor source | medium/high | Python default can create `__pycache__`; upstream code may cache dictionaries/tokenizers | disable/redirect runtime caches or precompile into writable location |
| No formal service registration/recovery | high | `UseWindowsService` exists, but no install script/policy | Phase 9G installer must create service, ACLs, recovery, health validation |
| Touch/Led no watchdog/autostart | high for现场 | no Launcher/service/task scheduler currently | add interactive Launcher or startup task in Phase 9G |
| Framework-dependent Server requires .NET runtime | medium | publish script uses `--self-contained false` | decide prerequisite vs self-contained publish |
| AVPro/LibVLC DLL layout is fragile | medium | post-build script copies LibVLC DLLs to root plugin lookup path | installer must preserve complete LedPlayer directory and verify DLL presence |
| Current company name is `DefaultCompany` | medium | Unity ProjectSettings | change before formal packaging or account for LocalLow paths |
| Default passwords/terminal key in appsettings | high | `appsettings.json` contains `admin/TG@2026` and `TG-TERMINAL-2026` | installer first-run/site config must force change |
| CORS allows any origin | medium | `AllowAnyOrigin` | acceptable only on isolated LAN; otherwise tighten in deployment hardening |
| No backup/restore automation | medium | JSON repositories and Media are mutable | installer must back up ProgramData before upgrades |

## 15. Recommended Directory Layout

The Program Files / ProgramData split is appropriate for this architecture, but the current defaults must be adjusted
in Phase 9G.

Recommended immutable binaries:

```text
C:\Program Files\TG Exhibition Control\
  Server\
    TG.Control.Server.exe
    TG.Control.Server.dll
    TG.Control.Server.deps.json
    TG.Control.Server.runtimeconfig.json
    appsettings.json
    AdminWeb\
    runtimes\
    TtsWorker\
      MeloTtsLocal\
        worker.py
        requirements-windows-cpu.txt
        melotts-v0.1.2-windows-offline.patch
        README.md
        bundle-manifest.json
        runtime\
          python.exe
          ...
        vendor\
          MeloTTS\
        models\
          MeloTTS-Chinese\
          bert-base-multilingual-uncased\
        nltk_data\
  TouchClient\
    TouchClient.exe
    TouchClient_Data\
    MonoBleedingEdge\
    UnityPlayer.dll
  LedPlayer\
    LedPlayer.exe
    LedPlayer_Data\
    MonoBleedingEdge\
    UnityPlayer.dll
```

Recommended mutable data:

```text
C:\ProgramData\TG Exhibition Control\
  Config\
    server.site.json
    terminals.site.json
  Server\
    Data\
      published-content.json
      content-draft.json
      narration-routes.json
      ui-experience.json
      tts-production.json
      active-playback-session.json
      operational-events.jsonl
      ContentVersions\
      Media\
      TtsStaging\
  LedPlayer\
    Cache\
      Content\
    Logs\
  TouchClient\
    Logs\
  Backups\
```

Recommended rule:

- Program Files is immutable after installation.
- ProgramData contains all customer content, generated assets, drafts, rollback history, logs, caches, site config, and
  installer backups.
- Upgrade replaces Program Files but never deletes ProgramData unless the user explicitly chooses full data removal.

## 16. Recommended Startup Model

Recommended model:

```text
Windows Service:
  TG Exhibition Control Server
    - TG.Control.Server.exe
    - owns MeloTTS Worker child process
    - exposes Admin/API/media

Interactive Launcher:
  TG Runtime Launcher
    - starts TouchClient in the logged-in touch-terminal session when installed on that machine
    - starts LedPlayer in the logged-in LED-player session when installed on that machine
    - shows service/client status
    - can restart Touch/Led after crash
```

Reasoning:

- Server is a background service and already supports Windows Service lifetime.
- MeloTTS Worker is not independently useful to operators; it should remain a child process supervised by Server.
- TouchClient and LedPlayer are graphical applications and must run in an interactive Windows user session.
- A Windows Service should not directly display or own UI processes because Session 0 isolation makes that model fragile.
- A single all-powerful service Supervisor managing both background service and UI would complicate desktop-session
  behavior. If a Supervisor is built, it should be an interactive Launcher/watchdog for UI clients, not a replacement
  for Windows Service Control Manager.

Alternative models:

| Model | Recommendation | Reason |
| --- | --- | --- |
| Server as Windows Service + independent Launcher | recommended | best separation of background reliability and UI session control |
| One TG Runtime Supervisor managing Server/TTS/Touch/Led | not recommended as sole model | service-to-UI interaction is fragile; duplicates SCM responsibilities |
| Windows Service for backend + Launcher for interactive clients | recommended | matches current code boundaries and Windows deployment constraints |

Startup order:

1. Windows starts `TG Exhibition Control Server` service.
2. Server loads ProgramData repositories.
3. Server starts MeloTTS Worker if enabled and bundle is valid.
4. Launcher starts LedPlayer on LED host.
5. Launcher starts TouchClient on touch-terminal host.
6. LedPlayer registers and syncs manifest/cache.
7. TouchClient registers and displays readiness.
8. Operator starts narration manually.

## 17. Upgrade Strategy

Recommended upgrade process:

1. Detect existing installation and ProgramData root.
2. Stop TouchClient/LedPlayer through Launcher if present.
3. Stop Server service.
4. Confirm no active playback session, or warn operator and preserve `active-playback-session.json`.
5. Back up ProgramData:
   - JSON repositories;
   - `Media`;
   - `ContentVersions`;
   - `tts-production.json`;
   - logs if required by customer.
6. Replace Program Files binaries atomically.
7. Preserve site configuration and do not overwrite customer passwords/API keys.
8. Validate MeloTTS bundle manifest and model hashes.
9. Start Server service.
10. Wait for `/api/health`.
11. Wait for `/api/tts/providers` to report MeloTTS available or a clear unavailable reason.
12. Start LedPlayer and TouchClient through Launcher.
13. Verify readiness and content version.

Data compatibility:

- Content JSON model has additive TTS fields and legacy compatibility rules from Phase 9A-9D.
- Published versions must remain immutable.
- Rollback history must not be deleted during upgrade.
- Media assets referenced by current draft, published versions, rollback versions, and Candidates must be preserved.

## 18. Uninstall and Data Retention Strategy

Recommended uninstall default:

- remove Program Files binaries;
- stop and delete Windows Service;
- remove Launcher/autostart entries;
- keep ProgramData by default.

Data to keep unless user explicitly selects full removal:

- uploaded media and generated narration audio;
- published content and rollback history;
- content draft;
- route presets;
- UI experience config;
- TTS Jobs/Candidates;
- operational event logs;
- site configuration;
- LedPlayer cache if disk space allows.

Data safe to remove on full uninstall:

- Server temporary `TtsStaging`;
- Unity Player logs;
- Launcher logs;
- LedPlayer cache after user confirms media can be redownloaded.

## 19. Windows 10/11 x64 compatibility

Expected compatibility:

- Server targets .NET 8 on Windows x64.
- TouchClient and LedPlayer are StandaloneWindows64 Unity Players.
- MeloTTS Worker uses CPython 3.10.11 embedded x64 and CPU PyTorch 1.13.1.
- AVPro/UniversalMediaPlayer/LibVLC native dependencies are Windows x64.

Must still be validated on clean Windows 10/11 x64:

- Microsoft Visual C++ runtime availability for LibVLC/PyTorch/native wheels.
- Windows Defender/SmartScreen behavior for unsigned executables and Python Worker.
- Service account access to ProgramData.
- Kiosk/autologin/session startup for Touch/Led clients.
- Audio device selection on LED host.
- Hardware video decoding stability on the LED host GPU/driver.

## 20. Are runtime tools required on the customer machine?

| Tool | Current runtime need | Build/package need |
| --- | --- | --- |
| Visual Studio | no | no, unless developing |
| .NET SDK | no | yes if building from source |
| .NET Runtime / ASP.NET Core Runtime | yes under current framework-dependent publish | no if Phase 9G chooses self-contained publish |
| Node/npm | no after AdminWeb is built into Server output | yes on build machine because Server project runs AdminWeb build |
| Unity Editor | no after Player build | yes on build machine for Touch/Led Player builds |
| Python | no external install if MeloTTS bundle is present | embedded runtime is generated on build machine |
| pip | no runtime | used only when building the offline MeloTTS bundle |
| Git | no runtime | current bundle script uses `git apply` to patch MeloTTS source |

Target customer experience:

- customer machine should not install Visual Studio, Unity, Node/npm, Python, pip, or Git;
- if framework-dependent Server publish is retained, customer machine needs the .NET 8 Hosting Bundle/runtime installed
  by the installer or documented as a prerequisite;
- preferred final installer behavior is to include/verify all runtime dependencies automatically.

## 21. Clean Machine Acceptance Plan

Phase 9G installer acceptance should run on a machine or VM without:

- Visual Studio;
- Unity Editor;
- Node/npm;
- Python/pip;
- Git;
- developer Hugging Face cache;
- existing TG ProgramData.

Minimum acceptance cases:

| Scenario | Expected result |
| --- | --- |
| Install Server package | Program Files binaries installed; ProgramData created; service registered |
| Start service | `/api/health` returns ok on configured port |
| Admin local access | `http://127.0.0.1:5080/` opens AdminWeb |
| Admin LAN access | remote browser opens Server LAN IP if firewall rule enabled |
| MeloTTS provider health | `/api/tts/providers` shows `melo-local` available with `zh-standard` |
| Offline TTS | Generate Candidate succeeds with network disconnected or blocked |
| No developer cache dependency | generation succeeds with empty `%USERPROFILE%\.cache\huggingface` |
| Preview/Adopt/Publish | Admin can Generate, preview in browser, Adopt, Publish |
| LED sync | LedPlayer downloads media/audio, validates Size/SHA, caches locally |
| Touch start | TouchClient starts the published route manually |
| Playback | LedPlayer plays video/narration audio correctly |
| V1/V2/Rollback | published text/audio/manifest/cache return to V1 after rollback |
| Server restart | active session and repositories recover |
| Worker crash | Provider becomes unavailable then recovers; published content unaffected |
| LedPlayer restart | valid cache is reused; corrupt cache is redownloaded |
| Upgrade | Program Files replaced; ProgramData preserved; content still available |
| Uninstall keep data | binaries/service removed; ProgramData retained |
| Reinstall with existing data | service starts using retained ProgramData |

Acceptance status language must remain explicit:

- `PASS`
- `FAIL`
- `BLOCKED`
- `NOT RUN`

Do not mark a clean-machine scenario as PASS based only on the Phase 9E Spike.

## 22. Phase 9F Implementation Scope

If Phase 9F receives a later implementation approval before full installer work, the safe minimum scope would be:

1. Add explicit deployment documentation for manual copy/install.
2. Add a sample production configuration template.
3. Add non-invasive validation scripts that inspect a prepared deployment folder.
4. Add no product-code changes unless specifically approved.

Because the user explicitly requested Discovery only in this round, none of the above was implemented here.

## 23. Deferred to Phase 9G Installer

Phase 9G should own:

- real installer technology selection;
- Program Files / ProgramData directory creation;
- ACLs for service account and interactive clients;
- Server publish mode decision: framework-dependent prerequisite vs self-contained;
- Windows Service install/uninstall/recovery;
- Launcher/autostart/watchdog for TouchClient and LedPlayer;
- external runtime config for TouchClient and LedPlayer;
- first-run/site configuration flow for:
  - Server URL/IP;
  - Admin password;
  - Terminal API key;
  - client IDs;
  - data/cache directories;
- firewall rule creation/removal;
- full MeloTTS offline bundle generation/copy/verification;
- .NET/VC++ runtime prerequisite handling;
- upgrade backup/restore;
- uninstall data-retention UI;
- clean Windows 10/11 x64 acceptance run;
- installer signing and SmartScreen strategy if required.

Phase 9G should not change:

- PlaybackCommand protocol;
- PlaybackCoordinator core semantics;
- TouchClient playback architecture;
- LedPlayer playback/cache semantics;
- TTS Generate -> Candidate -> Adopt -> Publish lifecycle;
- Asset GC policy unless separately approved.

## 24. Discovery conclusion

The Program Files / ProgramData split is the correct formal deployment direction for the current architecture.

Current code is close to deployment-ready in runtime behavior, but not installer-ready:

- Server can run as a Windows Service, but there is no service registration/recovery installer.
- AdminWeb is correctly hosted by Server after build.
- TouchClient and LedPlayer Windows Players include their Unity runtime dependencies, but site configuration is not
  externalized.
- LedPlayer native VLC/AVPro dependencies must be deployed as an intact directory.
- MeloTTS is architecturally isolated as a Provider, but the full offline runtime/model bundle must be generated and
  clean-machine validated before customer delivery.
- Mutable data must move out of application/output directories into ProgramData before Program Files installation.

Recommended next phase:

Phase 9G should build the formal installer/deployment package around the existing architecture rather than redesigning
the product runtime.
