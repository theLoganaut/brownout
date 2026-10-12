# Blackout-style capture checklist

Sep 30, 2026 · @Logan

Capture work splits into three tiers: numbers a script can measure from gameplay footage, numbers only other creators' test videos can give, and feel we tune by hand against reference clips. Community stat tables are cross-checks only; our data files come from your footage.

## Tier 1: measure from gameplay footage

Fixed-length animations and UI timers can be measured from ordinary gameplay, because many noisy clips average out to a reliable median. Sample one or two weapons per class with distinct handling.

| Item | How it's measured | Clips needed | Status |
| --- | --- | --- | --- |
| Fire rate (RPM) | Audio onset detection on gunfire, per sampled weapon | 3+ sustained bursts per weapon | Not started |
| Reload times | Frame count start to end; tactical vs empty, with and without Fast Mags | 5+ per weapon and mag setup | Not started |
| Draw, swap, holster | Frame count at 60fps | 5+ per class | Not started |
| ADS in and out | Frame count from input to full zoom; with and without Quickdraw | 5+ per weapon and optic | Not started |
| Sprint-to-fire delay | Frame count from sprint end to first shot | 5+ per class | Not started |
| Healing and armor use | Progress-bar frame counts: first aid, med kit, trauma kit, armor equip | 3+ per item | Not started |
| Revive, pickup, inventory actions | Frame counts for revive, item pickup, attachment swap | 3+ each | Not started |
| Circle schedule | Timer readouts: gap before each collapse, shrink duration | 1-2 full matches | Not started |
| Circle damage | Health-bar drop per tick outside the circle, by stage | Each stage, from full matches | Not started |
| Hit feedback timing | Hit marker, kill marker, damage indicator, flinch; frame counts | 20+ hits across ranges | Not started |
| Drop and parachute | Jump to landing time; freefall, wingsuit and parachute phases | 3+ drops | Not started |
| Throwables | Cook time, fuse time, throw arc shape, blast visual | 3+ per equipment type | Not started |
| HUD states | Stills of ammo, health, armor, compass, minimap, kill feed, prompts | One clean 1080p still per state | Not started |
| Optics and weapon visuals | Stills of each optic's reticle and ADS view; muzzle flash, tracers, shell eject | One per optic and effect | Not started |

## Tier 2: find as test videos

Recoil, spread, damage falloff and absolute speeds need controlled tests that gameplay can't give, so the source is other creators' test videos. I haven't confirmed these exist for every weapon, so note gaps as you search.

| Item | Search for | Extract | Status |
| --- | --- | --- | --- |
| Recoil patterns | Wall tests and recoil-pattern videos per sampled weapon | Bullet-hole pattern shape, with and without recoil attachments | Not started |
| Hip-fire and ADS spread | Spread and bloom test videos | Spread size standing, moving, crouched | Not started |
| Damage falloff ranges | Damage range and TTK test videos | Distance where shots-to-kill increases, per weapon | Not started |
| Headshot multipliers | Headshot shots-to-kill tests | Multiplier per weapon class | Not started |
| Armor reduction by level | Armor test videos | Percent reduction per level and which levels protect headshots; sources disagree | Not started |
| Attachment effects | Attachment comparison videos | Change in recoil, ADS time, spread, reload per attachment | Not started |
| Absolute movement speeds | Movement speed tests over a known distance | Walk, sprint, crouch, prone, slide, ADS-walk in m/s | Not started |
| Fall damage | Fall damage tests | Height thresholds and damage dealt | Not started |
| Vehicle stats | Vehicle tests | Top speed, acceleration, health, turn radius, enter and exit time | Not started |
| Hitbox sizes | Model-scale and hitbox references | Standing, crouched, prone heights against known-size objects | Not started |
| Loot spawn rates | Community loot guides and map item counts | Items per building type and rarity mix | Not started |
| Sniper handling | Sniper handling tests | Breath-hold length, scope sway, bullet velocity and drop | Not started |

## Tier 3: tune by feel

Whatever the first two tiers can't pin down, we tune by running our build beside a reference clip and adjusting until the two match.

| Item | How we tune | Reference needed | Status |
| --- | --- | --- | --- |
| Recoil kick and recovery | Match the pattern and rhythm of a sustained burst | One sustained-fire clip per sampled weapon | Not started |
| Hip-fire bloom | Match reticle expansion and recovery while moving and standing | Hip-fire clips | Not started |
| ADS feel | Match zoom, FOV change, idle sway, scope-edge look | One ADS clip per optic | Not started |
| Movement feel | Match acceleration, slide, camera bob, sprint FOV | Walk, sprint and slide clips | Not started |
| Hit feedback and flinch | Match screen shake, damage indicator, hit marker timing | Clips of dealing and taking damage | Not started |
| Time to kill | Playtest duels at set ranges against TTK computed from measured RPM and shots-to-kill | Tier 1 and 2 numbers | Not started |
| Weapon audio mix | Match loudness and distance falloff using our own sounds | Clips with clear weapon audio | Not started |
| Vehicle handling | Match steering, braking, camera | Driving clips | Not started |
| Dive and parachute feel | Match dive speed, camera, deploy transition | Drop clips | Not started |
| Match pacing | Compare loot density and circle timing against full-match footage | One or two full matches | Not started |

## Recording rules

Measurements stay valid only if every clip is the same patch, a constant 60fps, and labeled.

- Use the final patch where you can. Stats changed across updates, so note the patch or date of each source.
- Prefer 1080p at a constant 60fps with the HUD on and no overlay, facecam or slow motion. Variable frame rate breaks timing measurements.
- Note the FOV. Recoil and sway look different at different FOVs; default is best.
- Label each clip: weapon, attachments, stance, and what it shows.
- For each class, pick one or two weapons with distinct handling and capture every item on them. Other weapons inherit their class values, with community numbers applied on top.
- Treat community stat tables as cross-checks. A launch-era multiplayer table exists ([GameGuideHQ](https://gameguidehq.com/black-ops-4-multiplayer-weapon-statistics-damage-time-to-kill-rate-of-fire-reload-time/)), but its TTK column doesn't match its own RPM and shots-to-kill columns, and it covers multiplayer, not Blackout.
