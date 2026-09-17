# UI Customization — Phase 1

## Scope

This phase adds a backward-compatible, declarative UI customization boundary for the TouchClient and LedPlayer.
It does not change content, playback, TTS, synchronization, or device-control behavior.

## Configuration model

`UiExperienceConfig` retains all legacy flat fields and adds optional:

- `layout`: bounded Touch and LED templates plus visibility switches;
- `touchElements` / `ledElements`: whitelisted text and asset overrides;
- asset identity fields (`assetId`, `assetUrl`, `assetSha256`, `assetSizeBytes`, `assetMediaType`).

Supported keys are deliberately finite. Runtime clients never execute remote markup, scripts, shaders or prefabs.

## Server authority

`UiExperiencePolicy` normalizes templates, trims text, validates colors and rejects unknown keys or incomplete asset
identity. `/api/ui/publish` validates every configured element asset through the existing `AssetStorage` before writing
the versioned snapshot. Legacy JSON without the new properties is read successfully and receives default layout values.

## Runtime behavior

- TouchClient applies Home hero title/subtitle/logo, hero visibility, status-panel visibility and quick-action visibility.
- LedPlayer applies idle title/subtitle/logo and combines legacy and layout branding/status switches.
- Missing or failed images fall back to the existing built-in visual; no business state is changed.
- Existing `/api/ui/current` polling and version semantics remain unchanged.

## Admin behavior

The existing terminal appearance editor now exposes bounded templates, visibility switches, hero/idle text overrides and
Logo uploads. Uploads use the existing asset endpoint and retain full asset identity in the UI element override. Publish
remains an explicit action and is independent from the content publish version.

## Follow-up phases

The next phase can extend the same schema with additional whitelisted slots (page labels, empty/error illustrations and
LED playback-return text), then add preview, UI history and rollback. Arbitrary drag/drop or executable custom UI remains
out of scope for the safe runtime model.
