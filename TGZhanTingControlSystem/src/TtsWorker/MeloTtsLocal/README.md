# MeloTTS Local Worker

This directory contains only the source-controlled Worker adapter. The Python runtime, patched upstream source,
models, and NLTK data are generated deployment dependencies and must not be committed.

Build a customer-deployable offline bundle on an internet-connected build machine:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-MeloTtsWindowsBundle.ps1 `
  -DestinationRoot C:\TGDeployment\Server\TtsWorker\MeloTtsLocal
```

After the command succeeds, the destination is self-contained. Copy it beside the published Server executable at
`TtsWorker\MeloTtsLocal`. The customer computer does not install Python, run pip, or download a model.

Runtime endpoints are loopback-only:

- `GET /health`
- `GET /voices`
- `POST /synthesize`
- `POST /requests/{requestId}/cancel`

The frozen model and source hashes are verified both while building the bundle and while the Worker starts. The
Server advertises the Provider as unavailable when runtime files are absent or the Worker health check fails; manual
audio upload remains independent.
