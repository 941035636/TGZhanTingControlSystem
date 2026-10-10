TG TouchClient Dark UI Kit

Reference/Approved_Visual_Reference.png: APPROVED COMPOSITE MOCKUP; never use as runtime background.
Reference/Asset_Style_Guide.png: CONCEPT ASSET SHEET; do not crop its tiny elements for production.
Backgrounds/Home_Dark_Abstract_1920x1080.png: standalone clean abstract dark-blue 16:9 background, NO cards or text. This is a practical fallback, NOT a pixel-perfect reconstruction of the approved exhibition-hall photo.
Icons/nav_*.png: transparent 128px navigation icons. SVG source included.

Implementation: Existing Unity UGUI should draw all text, cards, buttons, statuses. Keep original 12 card assets, state and commands. Use reference for visual comparison. If exact architecture/hall photo background is required, request a separately generated clean 1920x1080 scene asset; do not extract from composite.
