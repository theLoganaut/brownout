# Project Brownout data files

This folder lives in the repo at `References/` so the coding conversation can read it. The reference docs
(`weapon data.md`, `HUD & gameplay feel.md`, `Blackout-style capture checklist.md`) and their `images/` are here too.
The Notion hub ("Project Brownout") is the source of truth for rules and decisions; these
files carry numbers that are too bulky for Notion.

| File | What it is |
|---|---|
| `brownout_guns.json` | Decided values for the 17 starting guns: damage, mag, fire timing, reload and ammo-in, ADS, caliber, bullet speed, swap times, allowed optics and attachments, measured recoil. Start here. |
| `weapons_brownout.json` / `.csv` | Raw measurements for all 43 Blackout guns in the reference video (fire rate, reload, ADS, zoom, recoil) plus community stats. Source for the file above and for guns added later. |
| `hud_rects.json` | Position of every HUD element, normalized to the screen. |
| `gameplay_timings.json` | Measured timings from Blackout gameplay: revive, healing, drop, pickups, swaps, banners. |

Notes
- Gun names are placeholders; `based_on` is the Blackout gun the numbers come from.
- Bullet speeds are our own starting values. No measured Blackout speeds exist.
- Recoil values are on-screen measurements from footage; see the `meta` block in `weapons_brownout.json`.
