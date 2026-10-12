# Project Brownout — HUD & gameplay feel

Oct 1, 2026 · @Logan

Frame-level reference from the Blackout gameplay video (1080p, 60fps, FOV 120). Times are video timestamps in seconds.

## HUD layout

The HUD hugs the four corners and the top edge, leaving the middle clear except for the crosshair. Positions below are normalized (0-1) so they drop straight into Unity anchors; exact pixel boxes are in `hud_rects.json`.

&#91;image: `images/numbered_hud_elements_1080p.png`\]

| # | Element | Box (x0, y0, x1, y1) | Behavior seen |
| --- | --- | --- | --- |
| 1 | Minimap | 0.043, 0.061, 0.183, 0.315 | Square, player-up. Shows squad pins, pings, flight path in the helicopter, dashed route to the zone when outside it |
| 2 | Zone timer | 0.039, 0.335, 0.117, 0.372 | Circle icon + mm:ss. Outside the zone it becomes a distance bar with a running-man icon. Near 1:00 a copy pops up near upper-center |
| 3 | Compass | 0.313, 0.046, 0.657, 0.111 | Degree ticks every 15°, cardinal letters, heading in a box above center, squad/ping pins on the strip |
| 4 | Counters | 0.858, 0.057, 0.932, 0.093 | Players alive, kills, and spectators (eye icon, appears once someone spectates you) |
| 5 | Kill feed | 0.781, 0.097, 0.932, 0.130 | Rows stack downward, newest on top. Squad names yellow, enemies red, weapon or skull icon between |
| 6 | Squad list | 0.056, 0.671, 0.167, 0.750 | Colored number, name, mic icon, ping pin, segmented health bar (armor shows as a yellow tail). Downed adds a white cross and red bleed-out bar |
| 7 | Portrait + health | 0.041, 0.850, 0.120, 0.917 | Character face and current HP (150 normal, 200 seen later after a boost) |
| 8 | Health item | 0.121, 0.813, 0.157, 0.917 | Current healing item + count, key hint (X) above |
| 9 | Armor | 0.158, 0.850, 0.192, 0.917 | Vest icon; it darkened as the armor broke under fire |
| 10 | Empty quick slot | 0.716, 0.847, 0.750, 0.903 | Dark until filled |
| 11 | Equipment | 0.750, 0.808, 0.784, 0.907 | Equipped tool/grenade + count, key hint (E) above |
| 12 | Ammo | 0.784, 0.850, 0.814, 0.903 | Mag count large, reserve small underneath |
| 13 | Ammo type | 0.815, 0.856, 0.880, 0.903 | Caliber label plus pips: one tall bar for the loaded mag, short bars for spare mags |
| 14 | Weapon | 0.880, 0.856, 0.932, 0.912 | Weapon silhouette and name |

Also on screen: a 4-line hip crosshair (only once a gun is out), teammate name tags over their heads, a yellow system/chat feed on the left ("has come online", "joined your party"), and a red directional damage arc at top-center when hit.

&#91;image: `images/each_hud_component_full_resolution.png`\]

## Combat feel (0:10-0:25)

From sprint to a settled ADS took about 0.37 s, and the player went from 150 HP to downed in 1.2 s of incoming fire. That fight is the clearest picture of how fast Blackout gunfights resolve.

| Time (s) | What happens | Detail |
| --- | --- | --- |
| 10.0-11.25 | Sprint | Gun tucked low and angled down; HUD unchanged |
| 11.28 | Sprint ends | Gun swings up to the hip in \~0.1 s |
| 11.42-11.65 | ADS raise | \~0.23 s; the scene visibly magnifies when the sight settles |
| 12.0-13.6 | Firing bursts (Swordfish) | Hit markers almost continuous from 12.33; muzzle flash fills the lower sight |
| 12.10 | Armor hit on enemy | Small yellow shield icon over the enemy at the impact |
| 12.55, 12.83, 12.93, 13.10 | Player hit | 150 → 127 → 104 → 81 → 58: 23 per hit with armor on |
| 13.18 | Player hit | 58 → 29: 29 per hit after the armor icon darkened |
| 13.75 | Downed | See next section |

**Hit markers:** a white X of four short diagonal ticks with a gap around the crosshair, about 45 px across at 1080p. Each hit refreshes it and it clears about 0.1-0.2 s after the last hit. Hitting armor adds a small vest icon under the X; the hit that downs someone turns the X red.

**Taking damage:** each hit flashes a red tint over the whole screen for a few frames, a red arc at top-center points toward the shooter, and blood splatter collects at the screen edges as health drops.

&#91;image: `images/combat_timeline_frames.png`\]

Health readout every \~83 ms while taking fire. The armor vest icon on the right darkens partway through:

&#91;image: `images/health_dropping_under_fire.png`\]

## Downed, revive and healing

A teammate revive took 3.1 s and brought the player back at 50 HP; a first-aid use took 1.0 s. Both use the same centered progress bar.

| Event | Start (s) | Duration | What the screen does |
| --- | --- | --- | --- |
| Downed | 13.75 | — | Heavy red overlay with blood splatter for \~0.3 s, camera drops low, bottom HUD (health, items, weapon) disappears |
| Switch to third person | \~14.2 | \~0.45 s after going down | Over-the-shoulder crawl camera; red fades |
| Crawling, waiting | 14.2-18.4 | 4.6 s | Squad list shows a white cross and a red bleed-out bar under your name |
| Being revived | 18.40 | 3.14 s | "Being Revived" label over a white bar at screen center-low (bar spans x 0.36-0.61, y 0.71) |
| Inventory while downed | 20.5-21.5 | — | Full inventory overlay opens on top of the revive; the revive keeps running |
| Back up | \~21.6 | — | Snaps back to first person at 50 HP with a different weapon out |
| First aid | 23.20 | 1.02 s | "Using First Aid" with the same bar; hands hold the bandage pack, gun lowered |

The bar fills at a constant rate, so in Unity it's a simple linear fill driven by the action timer.

## The drop (1:26-2:13)

Jump to landing took 28 s. Diving pushed speed from 43 to 69 m/s, and levelling out held about 67 m/s for 11 s, which matches your read: dive for speed, then glide to carry it.

The player rides in one of a fleet of helicopters (there's no separate plane in this footage); the others fly alongside and ahead.

| Phase | Time (s) | Speed (m/s) | What you see |
| --- | --- | --- | --- |
| Intro | 86-96.5 | — | No HUD, "Eliminate all other players" banner, camera on the fleet |
| In helicopter | 97-105 | — | HUD appears: minimap with flight path, progress bar under it, aboard count (70) on the right, "F WINGSUIT" prompt |
| Dive | 105.0-109.5 | 43 → 69 | Hard cut to third person below the helicopter, camera pointing down |
| Level glide | 110-128.5 | 63-68, mostly 67 | Camera to the horizon; W speed up / S slow down / mouse free-look hints shown |
| Parachute | 128.9-133.0 | 45 → 29, then 25-28 | Chute auto-deploys, camera pulls back above the canopy |
| Landing | 133.0 | — | Cuts straight to first person, unarmed |

**Altimeter:** a vertical bar on the right edge with a green ground marker at the bottom. The speed box rides on a white marker that slides down the bar as you lose height; chevrons under it show you're descending. There's no altitude number.

**Full map:** an 8×8 lettered grid. The flight path is a solid white line behind you and a dashed red/white line ahead, with an orange arrow for you. Controls panel: left-click places a waypoint, scroll zooms. The map also opens mid-glide.

&#91;image: `images/drop_sequence_frames.png`\]

&#91;image: `images/full_map_with_flight_path.png`\]

&#91;image: `images/wingsuit_hud_speed_box_altimeter.png`\]

Helicopter HUD pieces: system feed, flight-progress bar, aboard count and the wingsuit prompt.

&#91;image: `images/helicopter_hud_crops.png`\]

## Looting and pickups (2:13-2:19, 2:35-2:37)

Every pickup is a quick hand-sweep that doesn't block movement; the item leaves the ground about 0.4 s into a \~0.7 s sweep.

- **On the ground:** items lie as 3D models with a thin white outline so they read against any floor.
- **Hover:** looking at an item pops a floating icon of it above the model on a thin stem, plus a prompt bar: `[F] Equip | Spitfire`, `[F] Equip | Med Kit`, `[F] Pick up | <ammo>`.
- **Unarmed pickup:** the left hand sweeps down and back (134.75-135.45).
- **Gun pickup:** the gun comes up in a first-raise animation, held high then lowered to idle, about 1.1 s.
- **Ammo, med kits, bandages, armor:** same sweep; health items go straight into the health slot, armor into the Gear slot.
- **Attachments:** two choices, `[F] Equip | Stock` puts it on the current gun, `[G] Pick up` sends it to inventory. On equip the gun tilts to show the part being fitted, about 0.75 s, then returns to idle.

&#91;image: `images/looting_attachments_smoke_grapple_frames.png`\]

## Inventory, backpack and stash (3:07, 4:07-4:08, 4:36, 5:54, 7:45)

The inventory is a transparent overlay with a mouse cursor; the game keeps running behind it, so you can be shot, revived or keep moving while it's open.

**Layout (no backpack, 3:07):**

| Area | Position | Contents |
| --- | --- | --- |
| Ammo | Top-left, 2×4 grid | 9mm, .45 Cal, 5.56mm, 7.62mm, .338 Cal, .50 Cal, 12 Gauge, Rockets; empty types are dimmed |
| Weapon | Top-right | WEAPON 1 / WEAPON 2 tabs, "GKS / 9MM" name, weapon render |
| Attachments | Right, 3×2 grid | Optic, Barrel, Under Barrel, Magazine, Body, Stock (only slots the gun supports are lit) |
| Health, Gear | Bottom-left | Healing item stack, armor vest |
| Inventory | Bottom-center | 5 slots |
| Storage, Equipment | Bottom-right | Backpack slot, equipped tool or grenade |

As you noted, the Health, Gear and Equipment slots are separate, so equipped items don't use any of the 5 inventory slots.

**Backpack (4:36):** the label changes to BACKPACK and the 5 slots become 10 (two rows of 5); the backpack shows in Storage.

**Hover tooltip:** name in yellow, a one-line description, use time and duration, then mouse hints `USE` (left) and `DROP` (right); equipment shows `EQUIP` instead. The hovered slot gets corner brackets. Examples: Medic, "Heal faster and revive faster…", use time 2, duration 240; Skulker, "Move faster while crouched and prone", use time 3, duration 180.

**Dead players:** the body stays on the ground with a "KILLED \<name>" banner; at 5:54 the drop is a loot bag. Interacting opens a STASH panel across the top center, 8 items per row, beside your own inventory. Large stashes (7:45) scroll, with a vertical scrollbar on the right of the grid.

&#91;image: `images/inventory_without_backpack.png`\]

&#91;image: `images/backpack_inventory_hovered_gear.png`\]

&#91;image: `images/dead_player_stash.png`\]

&#91;image: `images/large_stash_scrollbar.png`\]

## Kills and notifications (3:52-3:55, 4:05, 7:18-7:21, 13:02)

Downing someone shows a center banner for about 1.65 s; the kill feed only logs full kills, as you said.

- **Downed banner (3:54):** two lines below the crosshair, `DOWNED <name>` with the name in white bold and `<n> DAMAGE DEALT` under it (this one read 57). It appeared at 234.33 and cleared at 235.98.
- **Killed banner:** same format, `KILLED <name>` with the name in red. The sniper headshot at 7:20 read `KILLED NickIsDankm8 / 150 DAMAGE DEALT`, a one-shot kill.
- **Kill feed (4:05):** top-right rows, newest on top: `attacker [weapon icon] victim`. Your squad in yellow, enemies in red, a skull-style icon for non-weapon deaths.
- **Outside the zone:** the minimap draws a dashed line to the safe zone and the timer box under it becomes a blue distance bar with a running-man icon.
- **Sniper shot (7:18-7:21):** the 3x scope draws a green bracket around the target before the shot. The shot kicks the scope up and out of view, the bolt cycles in \~0.7 s, and a high-pitched ding lands about 0.3 s after the shot. I'm treating that ding as the headshot sound, but that's an inference from the audio.
- **Victory (13:02):** the HUD clears except the system feed. A wide banner reads `#1 | Squad Placed | VICTORY` above center with an emblem below, and about 5 s later a "Match Ends in" countdown (0:55 at that point) appears bottom-right above a dark bottom bar with a Store button.

&#91;image: `images/kill_feed.png`\]

&#91;image: `images/victory_screen.png`\]

## Equipment and movement (5:19-5:26, 11:06-11:23, 12:13-12:16)

Throwing the smoke took 1.4 s from button to gun back up; the grapple took about 2.5 s end to end, most of it the 1.3 s pull.

| Action | Timeline (s) | Notes |
| --- | --- | --- |
| Swap equipment in inventory | 319.0-321.2 | Hovering equipment shows EQUIP; the Equipment slot icon changes to the smoke |
| Smoke throw | gun lowers 321.75, pin pull 322.08-322.33, release \~322.5, gun up 323.2 | Two-handed: grenade right, pin with left |
| Smoke cloud | pops \~324.3, full \~326.5 | About 1.8 s fuse after release, \~2.2 s to full size |
| Grapple | raised 734.1, fire \~734.45, pull 734.6-735.9, gun up 736.4 | One-handed in the left hand while the right still holds the gun; heavy motion blur during the pull |
| Weapon swap | e.g. 675.75-676.4 and 681.3-681.9 | About 0.5-0.7 s: \~0.25 s down, \~0.35 s up. A gun you haven't used yet plays the longer first-raise |

The 11:06-11:23 clip also shows health at 200 (boosted max), the zone timer popping near upper-center at 1:00 remaining, blue ping markers in the world, and back-to-back DOWNED and KILLED banners as the player clears a hillside.

## Keep vs change for Brownout, and gaps

Copy the layout, timings and flow; redraw every icon, font, map and emblem yourself so nothing is Activision's art.

- **Keep:** element positions, the timings above, the overlay-style inventory, the hover-icon-on-a-stem ground prompts, the dive-then-glide wingsuit with a speed box on an altimeter bar, and the banner wording pattern.
- **Change:** the map and its POI names, the victory emblem and its slogan, item and perk names, and the visual style of every icon. Your brownout POIs could also get a HUD indicator on the minimap or compass when power is out.
- **FOV:** all of this is at FOV 120, so the screen-space sizes (hit marker, bars) look smaller than they would at a default FOV. The HUD itself doesn't scale with FOV.
- **Gaps:** I couldn't pin a single hit marker's exact duration because the hits came back to back. Altitude has no number on screen, so drop height is unknown. The headshot sound is identified from audio only. Damage numbers on the player (23 then 29 per hit) depend on the enemy's gun and range, which I can't see.

Files: `hud_rects.json` (normalized box for each HUD element) and `gameplay_timings.json` (every timing in this doc).
