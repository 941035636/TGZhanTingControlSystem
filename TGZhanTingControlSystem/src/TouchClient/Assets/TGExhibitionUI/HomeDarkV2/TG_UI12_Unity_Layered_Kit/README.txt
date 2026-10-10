TG Phase UI-12 Unity layered visual kit

IMPORTANT: The hall background is an upscaled CROP of a concept asset-sheet preview, not a native 1920x1080 independently generated scene. It may be soft at 4K and does not perfectly match the approved mockup. Treat as provisional visual-alignment asset, NOT final commercial high-res asset. Do not claim pixel-perfect fidelity.

Backgrounds/Home_Hall_1920x1080.png: independent scenery only, no UI/text; use full-screen background behind UI.
Backgrounds/Home_Hall_Dim_1920x1080.png: darker alternative.
Panels/*_9slice.png: transparent RGBA. Set Unity Texture Type Sprite (2D and UI), Mesh Type Full Rect, alpha transparency, Sprite Editor borders ~24px all sides; use Image Type Sliced. Adjust borders to actual geometry.
Icons/nav_*_normal.png and *_selected.png: PNG 128x128 transparent. SVG sources also provided.
Decor/Glow_Line.png: transparent horizontal glow, optional, Raycast Target false.
Reference/Approved_Visual_Reference.png: visual reference ONLY, NEVER as runtime background.

Do not use both hall background and duplicate hall decorations simultaneously. Preserve existing 12 module images, card layout, app logic, real service state. The background is mostly obscured by cards: expose top ceiling, left/right perimeter and bottom floor, and use translucent panels.
