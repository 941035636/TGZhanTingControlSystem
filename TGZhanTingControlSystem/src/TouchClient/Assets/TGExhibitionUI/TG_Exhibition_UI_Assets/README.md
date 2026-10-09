# TG Exhibition UI Assets — extracted from supplied concept art

## Important distinction
The three supplied PNGs are flattened concept images. Their text, imagery, buttons, shadows and highlights cannot be losslessly separated. **REFERENCE_ONLY** and **Composite** images must not be treated as clean production backgrounds or clickable controls.

## Files
- References/Reference_4.png: welcome storyboard and standby concept (1672x941)
- References/Reference_5.png: immersive LED environment photograph (1920x1280), reference only, not necessarily licensed as a production asset
- References/Reference_6.png: module-home design concept (1672x941)
- Standby/Standby_Composite_REFERENCE_ONLY.png: upper standby design, with baked-in text
- Welcome/*: four storyboard panels, reference only
- ModuleCards/Extracted/*: twelve cards, including baked-in Chinese text/icons and gradients; use as temporary preview ONLY
- ModuleCards/PhotoRegions/*: cropped upper photo portions, contain possible baked-in numbers and are lower-resolution; NOT clean final covers
- UICommon/Card_BottomGradient_Transparent.png: usable gradient overlay for real module images
- UICommon/Card_SelectedOutline_Transparent.png: optional overlay, or preferably draw selection border in Unity
- UICommon/SoftBlueAmbientOverlay_Transparent.png: optional subtle tint
- Guides/module_manifest.json: card labels, coordinates, paths

## Implementation guidance
1. Recreate texts, selection checkboxes, status, buttons and navigation as actual Unity UI controls.
2. Do NOT put a flattened full-page reference image over interactive Unity controls.
3. Use real standalone cover images when available; if not, use temporary extracted composites visibly tagged as placeholders.
4. Do NOT upscale small photo regions and call them 4K-ready.
5. Welcome text must be driven by state; LED audio is authoritative for completion.
6. Prefer Texture Type Sprite (2D and UI), Sprite Mode Single, no NPOT rescale, Clamp, Bilinear for UI PNGs; review memory/compression per target platform.
7. This asset package is a temporary design extraction, not an approval of image rights or a source for original clean photography.
