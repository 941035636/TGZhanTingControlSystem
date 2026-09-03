# Phase 9G — Windows Production Deployment

Date: 2026-09-03

Frozen input baseline: `510a6070dc25a303aec48d40e36e376692747e35`

Target: Windows 10/11 x64, offline exhibition deployment

Status: implementation and build complete; clean-machine/elevated installation acceptance remains `BLOCKED`

## 1. Scope and outcome

Phase 9G turns the Phase 9E application set into a repeatable Windows production package without changing the
frozen playback, TTS lifecycle or client business semantics.

Implemented:

- self-contained Windows Server release with embedded AdminWeb;
- Windows Service registration, automatic start and recovery policy;
- Server-supervised MeloTTS local Worker;
- interactive Runtime Launcher for TouchClient and LedPlayer;
- external site configuration for Server, TouchClient, LedPlayer and Launcher;
- immutable `Program Files` / mutable `ProgramData` layout;
- complete offline MeloTTS runtime/model bundle;
- complete Unity Windows client directories, including native playback dependencies;
- reproducible production package and SHA-256 manifest;
- Inno Setup x64 offline installer;
- upgrade backup and data-retaining uninstall behavior;
- private-profile Windows Firewall rule limited to the Server API.

Not implemented in this phase:

- a second TTS Provider;
- Asset GC or legacy bulk migration;
- playback seek or protocol changes;
- device protocols or new UI/business features;
- installer code signing;
- remote multi-machine orchestration.

## 2. Final process topology

```text
Windows Service Control Manager
  └─ TG Exhibition Control Server (self-contained .NET 8)
       ├─ Server API / AdminWeb / Media : TCP 5080, LAN
       └─ MeloTTS Worker supervisor
            └─ Python Worker : TCP 5091, loopback only

Interactive Windows user session
  └─ TG Runtime Launcher
       ├─ waits for Server /api/health
       ├─ starts and monitors TouchClient
       └─ starts and monitors LedPlayer
```

The Launcher does not supervise the Server or MeloTTS Worker. The Server remains owned by Windows Service Control
Manager, while the MeloTTS Worker remains owned by the existing `MeloTtsWorkerSupervisor`. This preserves the Phase
9E Provider boundary and avoids Session 0 UI problems.

## 3. Directory model

### 3.1 Immutable application files

```text
C:\Program Files\TG Exhibition\
  Server\
    TG.Control.Server.exe
    AdminWeb\
    runtimes\
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
  TtsWorker\MeloTtsLocal\
    runtime\
    vendor\MeloTTS\
    models\MeloTTS-Chinese\
    models\bert-base-multilingual-uncased\
    worker.py
    bundle-manifest.json
  Launcher\
    TG.Control.Launcher.exe
  Tools\
  ThirdParty\
  package-manifest.json
```

Runtime code does not intentionally write into this tree. Server and Launcher are published self-contained, so a
customer machine does not need a separately installed .NET runtime.

### 3.2 Mutable customer/runtime data

```text
C:\ProgramData\TG Exhibition\
  Config\
    server.site.json
    touch-client.json
    led-player.json
    launcher.json
    initial-credentials.txt       # first install only; Administrators/SYSTEM only
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
  Media\                         # reserved top-level deployment directory
  Cache\LedPlayer\Content\
  Logs\
    Server\
    Launcher\
    TouchClient\Player.log
    LedPlayer\Player.log
  Backups\
  Runtime\
```

The current Server repository owns Media and TTS staging below `Storage:DataDirectory`, therefore the active media
location is `ProgramData\TG Exhibition\Data\Media`. The top-level `Media` directory is reserved and not claimed to be
an active second media store.

## 4. Configuration strategy

Product defaults remain suitable for development. Production site values are external and have higher precedence.

### Server

Configuration order:

1. existing .NET product defaults (`appsettings.json`);
2. optional `%ProgramData%\TG Exhibition\Config\server.site.json`;
3. explicit `TG_SERVER_SITE_CONFIG` file when supplied;
4. environment variables;
5. command-line arguments.

An explicitly requested site config must exist; a missing explicit path fails fast. The installed site config moves
Server data and logs to ProgramData and supplies absolute MeloTTS bundle paths.

### TouchClient and LedPlayer

The Unity runtime bootstraps load:

1. `TG_TOUCH_CLIENT_CONFIG` / `TG_LED_PLAYER_CONFIG` when present;
2. the matching ProgramData JSON file;
3. an executable-adjacent JSON file as a portable fallback;
4. serialized development defaults when no file exists.

This allows Server address, terminal identity and terminal key changes without rebuilding Unity. LedPlayer also loads
its content cache directory from site config.

### Launcher

The Launcher loads `TG_LAUNCHER_CONFIG`, then ProgramData `launcher.json`, then an executable-adjacent fallback. It
passes the Unity config paths through environment variables and directs both Unity `Player.log` files to ProgramData.

### Secret handling

- no customer IP, production password or production terminal key is committed;
- source defaults are explicitly labelled `TG-DEVELOPMENT-ONLY`;
- the installer generates independent cryptographically random Admin and terminal secrets on first install;
- existing site secrets are retained on reinstall/upgrade;
- the initial Admin credential file is ACL-restricted to Administrators and SYSTEM;
- ordinary kiosk users receive read access to terminal configuration only because the clients require the terminal
  key locally;
- secrets are not written by the new Server/Launcher logs.

## 5. Server Windows Service

Service name: `TG Exhibition Control Server`

Installation behavior:

- validates elevation and required binaries;
- registers the self-contained Server executable directly, without a third-party service wrapper;
- uses automatic startup;
- configures recovery delays of 5 seconds, 10 seconds and 30 seconds;
- enables failure actions for non-crash failures;
- starts the service and waits for `Running`;
- preserves the existing ProgramData configuration;
- creates a timestamped pre-install configuration backup on upgrade.

The service may start with a Windows system working directory, but production paths do not depend on it:

- AdminWeb is resolved from `AppContext.BaseDirectory`;
- site configuration is resolved from ProgramData or an explicit absolute path;
- storage, logs and MeloTTS paths are absolute in `server.site.json`.

The daily file logger writes Server diagnostics to `ProgramData\TG Exhibition\Logs\Server` without introducing a
service wrapper or a new logging framework.

## 6. Runtime Launcher

`TG.Control.Launcher` is a self-contained Windows Forms executable. Its responsibilities are deliberately narrow:

- acquire one global mutex and prevent duplicate Launcher instances;
- wait for the real Server health endpoint;
- start TouchClient and LedPlayer only after Server is healthy;
- detect unexpected client exits and restart after a bounded delay when configured;
- avoid starting a second instance of the same executable;
- allow an operator to start or stop either client;
- keep a client stopped after an explicit operator stop until it is explicitly started again;
- open AdminWeb;
- write Launcher and Unity client logs to ProgramData.

It does not duplicate Server business logic and does not supervise the MeloTTS Worker.

## 7. Network and firewall

| Endpoint | Owner | Scope | Firewall behavior |
| --- | --- | --- | --- |
| `0.0.0.0:5080` | Server API/AdminWeb/media | LAN | one inbound TCP rule, private profile, restricted to Server executable |
| `127.0.0.1:5091` | MeloTTS Worker | localhost only | no inbound firewall rule |
| outbound HTTP to Server | TouchClient/LedPlayer/Admin browser | local/LAN | no extra inbound rule |

Uninstall removes only the named firewall rule created by this product. The generated all-in-one configuration uses
loopback for both clients. A split-host site changes the external client `serverBaseUrl` to the Server LAN address;
Unity does not need to be rebuilt.

## 8. MeloTTS offline production bundle

The bundle builder pins and verifies:

- Python embedded runtime `3.10.11`;
- MeloTTS `v0.1.2`, commit `b633f243412169b999526e19eb6fcac0974b5d30`;
- MeloTTS Chinese model revision `082ca057e44f1e52ec47e1622a30286019e8a3ef`;
- multilingual BERT revision `7cbf9a625e29989f6b9c6c2fa68234c304f7e38f`;
- `pip 24.3.1`, `setuptools 70.3.0`, `wheel 0.45.1`;
- Python archive, source archive, model and dictionary SHA-256 values.

The formal build uses a short temporary directory to avoid Windows path-length failures, applies the frozen Windows
offline patch at the Worker/deployment boundary and removes only Torch C/C++ development headers that are not needed
at runtime. Model/runtime files are generated artifacts and are not committed to Git.

Final bundle inventory:

| Metric | Value |
| --- | ---: |
| files | 18,125 |
| bytes | 2,441,179,365 |
| approximate size | 2.27 GiB |

Offline validation used `HF_HUB_OFFLINE`, `TRANSFORMERS_OFFLINE`, `HF_DATASETS_OFFLINE` and dead HTTP/HTTPS proxies.
The current process could not create an outbound firewall rule because it was not elevated.

| Offline synthesis result | Value |
| --- | --- |
| status | `PASS` |
| isolation | offline environment plus dead proxy |
| synthesis elapsed | 15.51 s |
| WAV duration | 7.19 s |
| format | mono, 44.1 kHz, 16-bit PCM WAV |
| bytes | 634,528 |
| SHA-256 | `69858396ce361d8395e9778fefac5c090c179b7698fd80a43dba6fcd61aca3e6` |

Hard firewall isolation or a physically disconnected clean machine remains `BLOCKED`; the successful test must not be
expanded into that stronger claim.

## 9. Packaging pipeline

`scripts/Build-ProductionPackage.ps1` performs the following:

1. resets only the validated repository `artifacts` target;
2. publishes Server plus embedded AdminWeb as self-contained `win-x64`;
3. publishes Launcher as a self-contained single file;
4. builds Unity or consumes explicit validated TouchClient/LedPlayer build trees;
5. copies complete Unity directories rather than only their executables;
6. builds or consumes a verified MeloTTS offline bundle;
7. copies deployment tools and third-party notices;
8. checks critical native/runtime/model files;
9. produces a long-path-safe per-file size/SHA-256 manifest;
10. invokes Inno Setup when installer generation is enabled.

Server and Launcher PDBs are excluded from the production package. This avoids shipping unnecessary debug symbols and
source-path metadata.

The final pipeline was run again into an independent empty output. All 19,010 payload entries matched the installer
source package by path, size and SHA-256.

## 10. Installer

Technology: Inno Setup `7.1.0` x64.

Selection reasons:

- mature Windows installer and uninstaller support;
- x64 and Windows 10/11 support;
- single offline Setup executable;
- service, registry, shortcut and scripted deployment integration;
- extended-length path support needed by the Python/Torch bundle;
- no runtime installer framework is required on the customer machine.

Installer actions:

- requires administrator elevation;
- installs binaries beneath Program Files;
- creates ProgramData directories;
- stops an existing Server service before replacing immutable binaries;
- creates or preserves site configuration;
- establishes ACLs;
- registers service startup/recovery;
- creates only the Server private-network firewall rule;
- registers Launcher at Windows logon;
- creates Start Menu and optional desktop shortcuts;
- keeps ProgramData by default on uninstall;
- offers permanent ProgramData removal only through an explicit confirmation with default `No`.

Inno Setup's license text is included in `ThirdParty/InnoSetup-LICENSE.txt`. The upstream project asks commercial users
to purchase a commercial license. Commercial procurement/legal review remains a release gate.

## 11. Upgrade and uninstall

### Reinstall/update

- Installer waits for/requests Launcher closure through the application mutex.
- The existing Server service is stopped before files are replaced.
- Program Files binaries are replaced.
- current Config files are copied to a timestamped `Backups\preinstall-*` directory;
- existing config files and generated secrets are not overwritten;
- Data, Media, TTS assets, published versions, routes, logs and LED cache remain in ProgramData;
- service configuration and recovery settings are refreshed;
- the service is restarted.

### Uninstall

Default behavior:

- stop/delete the Server service;
- remove the product firewall rule;
- remove the Launcher logon entry;
- remove Program Files application files;
- retain ProgramData customer/runtime data.

Full data removal is performed only when the operator explicitly confirms it. The script validates that the deletion
target is exactly `%ProgramData%\TG Exhibition` before recursive removal.

## 12. Build and regression evidence

### Builds

| Item | Result | Evidence |
| --- | --- | --- |
| Server Release | `PASS` | zero warnings/errors; embedded AdminWeb production build completed |
| AdminWeb production | `PASS` | TypeScript + Vite production output copied into Server |
| TouchClient Windows | `PASS` | Unity 2020.3.35f1c2 batch build exited 0 |
| LedPlayer Windows | `PASS` | Unity batch build exited 0; `libvlc.dll` present in full build tree |
| Launcher | `PASS` | self-contained `win-x64` single-file publish |
| MeloTTS bundle | `PASS` | repeatable bundle created; standalone offline synthesis passed |
| Production Package | `PASS` | 19,010 entries, complete SHA-256 verification |
| Package reproducibility | `PASS` | independent clean output matched all 19,010 payload entries |
| Inno compiler | `PASS` | Setup executable generated without compile error |

### Automated regression

| Suite | Result |
| --- | --- |
| Phase 9A | `PASS` — 20/20 |
| Phase 9B | `PASS` — 23/23 |
| Phase 9C Server | `PASS` — 11/11 |
| Phase 9D | `PASS` — 25/25 |
| Phase 9E Provider | `PASS` — 6/6 |
| AdminWeb TTS | `PASS` — 9/9 |
| Worker normalization/segmentation | `PASS` — 3/3 |

### Runtime checks on the development machine

| Check | Result |
| --- | --- |
| self-contained Server executable starts without `dotnet` host | `PASS` |
| explicit external site config is loaded | `PASS` |
| external ProgramData-style storage path is active | `PASS` |
| `/api/health` returns `ok` | `PASS` |
| embedded AdminWeb returns HTTP 200 | `PASS` |
| external daily Server log is created | `PASS` |
| package contains no Server/Launcher PDB | `PASS` |
| package text config/scripts contain no tested development absolute paths | `PASS` |

## 13. Artifact inventory

Final installer source package:

- files: `19,010`;
- bytes: `2,936,462,676` (approximately 2.74 GiB);
- manifest SHA-256: `f58df784ab7cf1a9326c405ea39d62cc8e4bd42e1431133c819d318a015b13d3`.

Component sizes:

| Component | Files | Bytes |
| --- | ---: | ---: |
| Server | 340 | approximately 103 MB |
| Launcher | 1 | approximately 162 MB |
| TouchClient | 128 | approximately 62 MB |
| LedPlayer | 410 | approximately 169 MB |
| TtsWorker | 18,125 | 2,441,179,365 |
| Tools | 3 | approximately 12 KB |
| ThirdParty | 3 | approximately 5 KB |

Final Setup candidate:

- file: `artifacts/Phase9G/Installer/TG智慧展厅智能中控系统_Setup.exe`;
- bytes: `1,178,372,199` (approximately 1.10 GiB);
- SHA-256: `984b52fc09918fc67370a3ae0768a717431db597f0a5dacd24e7aba4a0c74f57`;
- Authenticode: `NotSigned`.

Artifacts are intentionally ignored by Git. Runtime/model binaries remain release artifacts, not source-controlled
files.

## 14. Acceptance classification

The classifications below are intentionally conservative.

| Scenario | Result | Reason/evidence |
| --- | --- | --- |
| production package generation | `PASS` | complete clean rebuild and manifest verification |
| installer compilation | `PASS` | final Setup generated by Inno Setup 7.1.0 x64 |
| installer Authenticode signing | `BLOCKED` | no production code-signing certificate is available |
| actual elevated install on this development machine | `NOT RUN` | avoiding mutation of the active development host |
| clean Windows 10/11 x64 install | `BLOCKED` | no clean VM/physical acceptance machine is available |
| no VS/Unity/Python/Node/Git prerequisites | `BLOCKED` for clean-machine claim | package is self-contained by inventory, but clean-host proof is unavailable |
| Server Service register/start/stop/restart | `BLOCKED` | requires elevated clean-machine installation |
| service recovery after abnormal exit | `BLOCKED` | SCM policy is configured; destructive runtime test not performed here |
| Windows reboot/logon autostart | `BLOCKED` | requires a rebootable acceptance machine |
| upgrade/reinstall data retention | `BLOCKED` | scripts preserve/backup data; real V1-to-update install not executed |
| uninstall retains ProgramData | `BLOCKED` | implemented but not executed on a clean installation |
| explicit full data removal | `BLOCKED` | implemented with guarded exact path; destructive acceptance not executed |
| Worker runtime missing reports unavailable | `PASS` | Phase 9E automated Provider test |
| actual Worker crash/recovery | `NOT RUN` | requires a controlled installed runtime fault test |
| actual model missing behavior | `NOT RUN` | required-file/package checks pass; installed fault injection not performed |
| TouchClient/LedPlayer crash restart | `NOT RUN` | Launcher code/build present; interactive crash injection not performed |
| duplicate Launcher instance | `NOT RUN` | global mutex implemented; interactive two-process acceptance not performed |
| temporary LAN loss and recovery | `NOT RUN` | no isolated multi-host acceptance environment |
| offline Worker synthesis | `PASS` | offline environment + dead proxy test produced valid PCM WAV |
| physical network disconnect/firewall-isolated synthesis | `BLOCKED` | no elevated firewall/isolated clean host |
| end-to-end installed Admin→Melo→LED→Touch playback | `BLOCKED` | Phase 9E application chain passed previously; installed clean-machine chain not available |
| Inno commercial license procurement | `BLOCKED` release gate | legal/procurement action is outside the repository |

Therefore Phase 9G produces a technically complete installer candidate, but it is not labelled release-ready until
clean-machine installation, upgrade/uninstall, reboot/recovery and code-signing gates are completed.

## 15. Clean-machine acceptance plan

Use a clean Windows 10 or Windows 11 x64 VM/physical machine with no Visual Studio, Unity, Python, pip, Node/npm, Git or
pre-existing TG ProgramData.

1. Verify Setup SHA-256 and Authenticode signature after the signing gate is complete.
2. Install Setup while offline.
3. Confirm Program Files and ProgramData layouts and ACLs.
4. Confirm the Server service is automatic and healthy after reboot.
5. Log in and confirm one Launcher, TouchClient and LedPlayer instance.
6. Open AdminWeb and authenticate with the generated initial credential.
7. Edit Chinese narration text.
8. Generate with the formal MeloTTS `zh-standard` voice while offline.
9. Preview, Adopt and Publish.
10. Confirm LED manifest download/cache and Touch-started LedPlayer playback.
11. Kill Worker, TouchClient and LedPlayer one at a time and observe the defined recovery behavior.
12. Kill the Server and verify SCM recovery.
13. Disconnect/reconnect the LAN and verify client recovery.
14. Reinstall/update and verify Config/Data/Media/TTS/version/route/log retention.
15. Uninstall with default data retention, reinstall, and verify recovery.
16. In a disposable snapshot only, select full data removal and verify the guarded removal behavior.

All results must continue to use `PASS`, `FAIL`, `BLOCKED` or `NOT RUN` without promoting static inspection to runtime
acceptance.

## 16. Modified/new source files

Deployment and packaging:

- `installer/TGExhibition.iss`
- `scripts/Build-ProductionPackage.ps1`
- `scripts/New-ProductionPackageManifest.ps1`
- `scripts/Test-ProductionPackage.ps1`
- `scripts/Test-ServerExternalConfiguration.ps1`
- `scripts/Test-MeloTtsOfflineBundle.ps1`
- `scripts/deployment/Install-TGExhibition.ps1`
- `scripts/deployment/Uninstall-TGExhibition.ps1`
- `scripts/deployment/Test-DeploymentHealth.ps1`
- `scripts/Build-All.ps1`
- `scripts/Build-MeloTtsWindowsBundle.ps1`
- `.gitignore`
- `README.md`

Launcher:

- `src/Launcher/TG.Control.Launcher/TG.Control.Launcher.csproj`
- `src/Launcher/TG.Control.Launcher/Program.cs`
- `src/Launcher/TG.Control.Launcher/LauncherConfiguration.cs`
- `src/Launcher/TG.Control.Launcher/RuntimeSupervisor.cs`
- `src/Launcher/TG.Control.Launcher/RuntimeLauncherForm.cs`

Runtime configuration/logging:

- `src/Server/TG.Control.Server/Program.cs`
- `src/Server/TG.Control.Server/Options.cs`
- `src/Server/TG.Control.Server/appsettings.json`
- `src/Server/TG.Control.Server/DailyFileLoggerProvider.cs`
- `src/TouchClient/Assets/Scripts/TouchRuntimeBootstrap.cs`
- `src/TouchClient/Assets/Scripts/TouchApiClient.cs`
- `src/LedPlayer/Assets/Scripts/LedRuntimeBootstrap.cs`
- `src/LedPlayer/Assets/Scripts/LedApiClient.cs`
- `src/LedPlayer/Assets/Scripts/LedContentCache.cs`

MeloTTS/third-party documentation:

- `src/TtsWorker/MeloTtsLocal/requirements-windows-cpu.txt`
- `ThirdParty/NOTICE.md`
- `ThirdParty/InnoSetup-LICENSE.txt`
- `docs/Architecture/Phase9F-Deployment-Discovery.md`
- `docs/Architecture/Phase9G-Windows-Production-Deployment.md`

## 17. Git record

Phase 9G is committed as one independent commit named:

```text
Phase 9G — Windows production deployment
```

The final SHA is reported at handoff. A commit cannot embed its own final SHA in content because changing the embedded
value changes that SHA.
