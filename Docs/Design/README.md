# UI design (Claude Design canvas)

Live canvas: https://claude.ai/artifact/AXoh2R41DV9cHDmSFb57K4 (private to the owner).
These files are the canvas source (`.dc.html` = HTML with inline styles), the single source of truth for the Unity UI.

- Artboards are 540×960 = half of the Unity reference canvas 1080×1920 → **every px value ×2 = Unity units**.
- `Main.dc.html` — tokens: colors, Fredoka type scale, spacing (8-grid), radii, button styles, gameplay zones.
- `Game.dc.html` — gameplay HUD (popups and Drill mode import it as background).
- `Menu`, `Drill`, `Win`, `Lose`, `Shop`, `Pause`, `Unlock` — the other screens.
