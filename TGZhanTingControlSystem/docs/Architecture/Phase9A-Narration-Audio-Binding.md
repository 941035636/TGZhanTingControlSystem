# Phase 9A — Narration Audio Binding Foundation

## 1. Scope

Phase 9A implements only the domain and safety foundation approved after
`Phase9-TTS-Discovery.md`:

- immutable narration audio asset metadata;
- Server-authoritative narration text and synthesis configuration fingerprints;
- Server-authoritative binding freshness evaluation;
- complete manual-upload binding;
- legacy JSON/read/playback compatibility;
- real narration audio SHA-256 and size in the LED content manifest.

It does not add a real TTS provider, provider SDK, generation job, voice UI, preview UI,
or a second playback path. `PlaybackCommand`, `PlaybackCoordinator`, Touch playback ownership,
and `NarrationAudioPlayer` are unchanged.

## 2. Final domain model

`ContentAsset` keeps the existing immutable identity and integrity fields and adds optional
`MediaType`:

```text
Id + Url + Sha256 + SizeBytes + Kind + Name + DurationSeconds + MediaType
```

`TtsSynthesisConfiguration` is provider-neutral and contains only parameters that describe
the expected audio result:

```text
ProviderKey + Voice + Language + Rate + Pitch + Volume
+ OutputMediaType + SampleRateHz + Channels
```

No Azure, Local engine, SDK, credential, region, endpoint, or vendor DTO is present in the
core model.

`NarrationAudioBinding` contains:

```text
ContentAsset Asset
NarrationTextFingerprint
SynthesisConfigurationFingerprint
TtsSynthesisConfiguration snapshot
Origin (Generated / ManualUpload / Legacy)
BoundAtUtc
FingerprintVersion
optional ProviderRequestId
```

`NarrationNode` retains nullable `TtsAudioUrl` and adds nullable `TtsConfiguration` and
`NarrationAudio`. The new properties are trailing optional record parameters, so historical
JSON without them remains deserializable.

## 3. Fingerprint specification

### 3.1 Narration text

Algorithm/domain: `tg:narration-text:v1`.

Normalization is performed only on the Server:

1. null becomes an empty string;
2. CRLF and CR become LF;
3. Unicode is normalized to NFC;
4. leading/trailing whitespace is trimmed;
5. internal text, punctuation, whitespace, case and line breaks are preserved;
6. SHA-256 is computed over UTF-8 bytes of the domain prefix, LF, and normalized text;
7. output is 64 lowercase hexadecimal characters.

This makes equivalent line endings and canonically equivalent Unicode stable while ensuring
that meaningful wording, punctuation, or internal formatting changes produce a new hash.

### 3.2 Synthesis configuration

Algorithm/domain: `tg:tts-synthesis-configuration:v1`.

The Server serializes a fixed-order canonical JSON object. Provider key, language and media
type are trimmed, NFC-normalized and lowercased; voice is trimmed and NFC-normalized without
case folding. Numeric values are emitted by `Utf8JsonWriter` in a culture-independent form.
The property order received from a client therefore cannot change the result.

The fingerprint includes provider identity, voice, language, rate, pitch, volume, output
media type, sample rate and channel count. A change to any audio-affecting field makes the
binding stale.

`FingerprintVersion = tts-binding-v1` makes future normalization changes explicit rather
than silently reinterpreting old hashes.

## 4. Server-authoritative status model

`NarrationAudioBindingInspector` evaluates in this order:

1. `Missing`: neither a new binding nor a legacy URL exists.
2. `LegacyUnverified`: only `TtsAudioUrl` exists; it remains playable but is never Fresh.
3. `InvalidAsset`: missing asset ID/URL, wrong kind, missing/invalid SHA, non-positive size,
   missing audio media type, missing file, size mismatch or actual SHA mismatch.
4. `InvalidBinding`: unsupported/missing fingerprint version, malformed fingerprints,
   invalid configuration, or a configuration snapshot whose stored fingerprint does not match.
5. `StaleText`: the current Server-computed text fingerprint differs from the binding.
6. `StaleSynthesisConfiguration`: the current node configuration fingerprint differs from
   the binding snapshot.
7. `Fresh`: asset, text and configuration are all valid and equal.

Only `Fresh` is accepted as a valid new binding by `ContentValidator`. AdminWeb may eventually
display this result, but it must not reimplement the rules.

## 5. Legacy compatibility and priority

Compatibility rules are explicit:

- existing JSON containing only `TtsAudioUrl` still deserializes;
- existing legacy content still reaches LedPlayer through the unchanged playback command;
- if a new binding exists, `binding.Asset.Url` is authoritative and the repository normalizes
  the compatibility `TtsAudioUrl` to that URL before persistence/runtime use;
- the LED manifest emits a legacy URL with empty SHA and size zero only as an explicit
  `LegacyUnverified` fallback; it does not invent integrity data;
- an unchanged legacy node may be republished during migration;
- a new naked legacy URL, a downgrade from a new binding to a naked URL, or an existing legacy
  URL paired with changed narration text is rejected on normal publish;
- historical rollback uses an explicit compatibility mode so an old version remains readable
  and restorable, but it is not reclassified as Fresh.

These rules prevent `text A + audio A -> text B + audio A considered valid` without deleting
the old file needed by history or rollback.

## 6. Manual upload chain

The existing generic upload still returns a complete `ContentAsset`. Upload now also records
the media type. AdminWeb then calls the authenticated Server binding endpoint with the returned
asset and current narration text.

The Server:

1. requires `AssetKind.NarrationAudio`;
2. requires Asset ID, URL, SHA, positive size and audio media type;
3. verifies the actual Server file, size and SHA;
4. creates a provider-neutral manual-upload configuration;
5. computes both fingerprints;
6. returns a complete `NarrationAudioBinding`.

AdminWeb stores the binding, its configuration and the compatibility URL together. It no longer
reduces a narration upload to URL-only data. This is a foundation endpoint, not the Phase 9B
generation/preview/adopt workflow.

## 7. LED manifest

`ContentManifestBuilder` now uses this priority:

1. regular node assets with their existing SHA/size;
2. new narration binding asset with its real SHA/size;
3. legacy `TtsAudioUrl` with explicit empty integrity metadata.

The `ContentSyncAsset` contract and LedPlayer cache/playback implementation are unchanged.
Newly bound narration audio therefore enters the existing background download, resume and
integrity validation path without a playback protocol change.

## 8. Automated verification

Run:

```powershell
dotnet run --project tests/TG.Control.Phase9A.Tests/TG.Control.Phase9A.Tests.csproj -c Release
```

The dependency-free executable test suite covers:

- stable text normalization/fingerprint and changed text;
- configuration property-order stability;
- provider/voice/language/rate/pitch changes;
- complete manual binding and Fresh evaluation;
- StaleText and StaleSynthesisConfiguration;
- missing/wrong SHA and missing size;
- legacy JSON deserialization and new JSON round-trip;
- repository rollback of legacy content;
- new and legacy LED manifest integrity behavior;
- legacy narration text change rejection;
- binding URL priority over legacy URL.

Phase 9A result: 20/20 passed.

## 9. Deferred to Phase 9B and later

- `ITtsProvider` registry and any real Local/Azure/other provider adapter;
- generation candidate/job persistence, idempotency, retry and audit lifecycle;
- provider capability/status that reflects the actual registered adapter;
- generated stream ingestion and audio decode/duration validation;
- Admin generation, progress, preview, adopt/discard and voice selection UI;
- a public read model/API for Admin to display Server freshness status before publish;
- migration tooling for upgrading legacy audio to verified bindings;
- reference-aware deletion/garbage collection across current content, history and candidates;
- full long-text, real audio format, LED download/playback and field sound acceptance.

Phase 9A stops at the binding safety foundation.
