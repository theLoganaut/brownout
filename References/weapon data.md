# Project Brownout — weapon data

Oct 1, 2026 · @Logan

Measured from the all-weapons showcase video (1080p, 60fps, 43 guns), with damage and magazine sizes from online sources. Names match the video labels for now.

## How to read the numbers

Full-auto fire rates are the most reliable numbers here; everything else is either a timing from the animation or a value borrowed from BO4 multiplayer.

- **Damage**: max-range-bracket damage from a BO4 multiplayer table (launch era). Blackout values may differ. Blank means I found no source. Your headshot multiplier (1.1x, 1.5x for snipers) and armor numbers (30/40/50%) apply on top.
- **Mag**: Blackout magazine size where NamuWiki lists it, otherwise BO4 multiplayer, otherwise counted from the video.
- **RPM**: full-auto rates come from audio autocorrelation and match the hip and ADS strings within 1%. Burst weapons show the in-burst rate plus the fastest burst-to-burst cycle seen. Semi, bolt and pump weapons show how fast the shooter actually fired, so treat those as a floor, not the max.
- **Reload**: full animation until the gun is back at idle, from the last shot. The game adds ammo earlier, which is why community reload values are smaller. Use the community value as the ammo-in point and ours as the animation length.
- **ADS**: time for the gun to center, about 0.05-0.1 s shorter than community ADS values. Zoom is how much the scene scales from hip to ADS.
- **Recoil**: how far the camera moved while firing in ADS, in degrees, assuming an 80° horizontal hip FOV. Mean is the typical offset during a spray; max is the peak. It may include the shooter pulling down. The full per-shot patterns are in the JSON file.
- Weapon swaps in the video are hard cuts, so there are no draw or holster timings here.

## Pistols

The KAP 45 is the only full-auto pistol, at about 793 RPM. The Strife and Mozu numbers are how fast the shooter tapped, not the cap.

| Weapon | Fire mode | Mag | Damage (MP) | RPM | Reload, s (ours / MP) | ADS, s (ours / MP) | Zoom | ADS recoil up, ° (mean / max) | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Strife | semi | 12 | 46 | ≥300 (MP 491) | 2.08 / 1.08 | — / 0.1 |  |  |  |
| RK 7 Garrison | burst3 | 15 | 32 | 1200 in-burst, 360 ms cycle (MP 830) | 2.27 / 1.08 | — / 0.13 |  |  |  |
| Mozu | semi (revolver) | 6 | 62 | ≥128 (MP 470) | 2.42 / 1.25 | 0.13 / 0.1 | 1.12 |  | likely per-round or partial reload in video; treat as rough |
| KAP 45 | auto | 20 |  | 793 | 1.93 |  | 1.12 | 1.11 / 1.94 |  |

Left: hip idle. Right: ADS sight picture.

&#91;image: `images/pistol_hip_and_ads_reference_frames.png`\]

## Shotguns

Shotguns are the weakest data in this set: I found no pellet damage, and the reloads look per-shell, so the reload numbers are rough. The pump cycles are the useful part: the MOG 12 cycles in about 1.0 s and the Argus in about 0.98 s.

| Weapon | Fire mode | Mag | Damage (MP) | RPM | Reload, s (ours / MP) | ADS, s (ours / MP) | Zoom | ADS recoil up, ° (mean / max) | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| MOG 12 | pump | 4 |  | ≥60 (MP 80) | 2.97 / 0.75 | 0.15 / 0.27 | 1.11 |  | likely per-round or partial reload in video; treat as rough |
| SG12 | semi | 6 |  | ≥146 (MP 245) | 2.15 / 1.78 | — / 0.23 | 1.11 |  | likely per-round or partial reload in video; treat as rough |
| Rampage | semi (uncertain) | 8 |  |  | 3.23 | 0.18 | 1.11 |  | fire mode unclear from audio; check footage |
| Argus | pump/lever (uncertain) |  |  | ≥62 | 3.48 | 0.15 | 1.1 |  | likely per-round or partial reload in video; treat as rough |

&#91;image: `images/shotgun_hip_and_ads_reference_frames.png`\]

## SMGs

SMG fire rates land 1-3% under the multiplayer table, and the Spitfire and Cordite kick hardest at about 3.1-3.4° peak. The GKS is the steadiest full-auto here at under 1°.

| Weapon | Fire mode | Mag | Damage (MP) | RPM | Reload, s (ours / MP) | ADS, s (ours / MP) | Zoom | ADS recoil up, ° (mean / max) | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| MX9 | auto | 35 | 28 | 722 (MP 741) | 2.53 / 1.37 | 0.13 / 0.27 | 1.26 | 1.22 / 2.00 |  |
| GKS | auto | 38 | 28 | 651 (MP 661) | 2.42 / 1.32 | 0.13 / 0.2 | 1.26 | 0.53 / 0.96 |  |
| Spitfire | auto | 34 | 27 | 1036 (MP 1051) | 2.33 / 1.55 | 0.15 / 0.25 | 1.26 | 1.66 / 3.12 |  |
| Cordite | auto | 60 | 28 | 801 (MP 812) | 2.73 / 1.97 | 0.15 / 0.25 |  | 1.65 / 3.42 |  |
| Saug 9mm | auto | 36 | 28 | 909 (MP 926) | 2.33 / 1.32 | 0.13 / 0.22 |  | 1.44 / 2.85 |  |
| Daemon 3XB | burst3 | 36 |  | 833 in-burst, 310 ms cycle | 2.53 | 0.15 | 1.26 | 0.24 / 0.78 |  |
| Switchblade X9 | auto | 30 |  | 845 | 2.13 | 0.12 | 1.26 | 0.79 / 1.93 |  |
| VMP | auto | 40 (video count) |  | 919 | 2.37 | 0.17 |  | 0.72 / 1.32 |  |
| MicroMG 9mm | auto (two-stage rate) |  |  | \~1270 then \~660 | 2.9 | 0.18 | 1.25 | 1.33 / 2.22 | \~47 ms per shot for the first \~17 rounds, then \~91 ms |

&#91;image: `images/smg_hip_and_ads_reference_frames.png`\]

## Assault rifles

Fandom backs our ICR-7 (\~600) and KN-57 (550) rates over the multiplayer table, so I'd trust the measured RPM for ARs. Iron-sight zoom is about 1.43x across the class.

| Weapon | Fire mode | Mag | Damage (MP) | RPM | Reload, s (ours / MP) | ADS, s (ours / MP) | Zoom | ADS recoil up, ° (mean / max) | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| KN-57 | auto | 35 | 40 | 556 (MP 570; Fandom 550) | 2.92 / 1.58 | 0.2 / 0.27 |  | 0.71 / 1.37 | Fandom reload 1.62 |
| Grav | auto | 35 |  | 556 | 2.82 | 0.23 |  | 0.87 / 1.46 |  |
| AN-94 | auto (fast first rounds) |  |  | \~555 sustained | 2.88 | 0.08 | 1.43 | 0.67 / 1.32 | first \~2 rounds at \~50-70 ms, then \~108 ms |
| Peacekeeper | auto | 32 |  | 697 | 2.68 | 0.13 |  | 0.39 / 0.76 |  |
| ICR-7 | auto | 35 | 37 | 601 (MP 615; Fandom \~600) | 2.63 / 1.18 | 0.2 / 0.27 | 1.43 | 0.71 / 1.37 | Fandom: 5 shots to kill up close |
| Rampart 17 | auto | 30 | 46 | 517 (MP 529) | 3.23 / 1.72 | 0.22 / 0.27 | 1.51 | 1.05 / 1.67 |  |
| VAPR-XKG | auto | 35 | 37 | 653 (MP 667) | 2.73 / 1.47 | 0.17 / 0.27 | 1.43 | 0.33 / 1.76 |  |
| Maddox RFB | auto | 40 | 32 | 723 (MP 676) | 2.77 / 1.77 | 0.22 / 0.27 |  | 0.98 / 1.91 |  |
| SWAT RFT | auto | 35 |  | 624 | 2.92 | 0.23 |  | 0.56 / 1.11 |  |
| Doublecross | auto (irregular cadence) |  |  | \~625 (median) |  |  |  | 0.50 / 1.16 | intervals vary 75-125 ms |

&#91;image: `images/assault_rifle_hip_and_ads_reference_frames.png`\]

## Tactical rifles

Burst sizes match NamuWiki (ABR 3, Swordfish 4, Stingray 2, M16 3), but our in-burst rates are lower than the multiplayer table for the ABR and Swordfish. The S6 Stingray's two shots land too close together to separate in the audio.

| Weapon | Fire mode | Mag | Damage (MP) | RPM | Reload, s (ours / MP) | ADS, s (ours / MP) | Zoom | ADS recoil up, ° (mean / max) | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Auger DMR | semi | 20 | 62 | ≥316 (MP 327) | 2.8 / 1.72 | 0.17 / 0.27 |  | 0.01 / 0.10 |  |
| ABR 223 | burst3 | 36 | 38 | 732 in-burst, 600 ms cycle (MP 1197) | 2.95 / 1.67 | — / 0.27 |  | 0.14 / 0.64 | in-burst rate uncertain: echoes overlap the burst |
| Swordfish | burst4 | 36 | 34 | 857 in-burst, 390 ms cycle (MP 1124) | 3.18 / 1.92 | 0.18 / 0.27 |  | 0.08 / 0.22 | 4 shots clearly \~72 ms apart |
| S6 Stingray | burst2 | 26 |  | burst every ≥272 ms | 2.95 | 0.12 |  | 0.29 / 0.62 |  |
| M16 | burst3 |  |  | 1463 in-burst, 400 ms cycle | 2.9 | 0.18 | 1.43 | 0.39 / 0.84 |  |

&#91;image: `images/tactical_rifle_hip_and_ads_reference_frames.png`\]

## Snipers

Bolt cycles are the solid numbers here: about 0.98 s for the Paladin, 0.68 s for the Outlaw and 1.22 s for the Koshka and Locus. Our ADS times are too short for scoped weapons because the gun centers before the scope view settles, so use the multiplayer ADS values.

| Weapon | Fire mode | Mag | Damage | RPM | Reload, s (ours / MP) | ADS, s (ours / MP) | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Paladin HB50 | bolt | 5 | \~90 body (Blackout, NamuWiki) | ≥61 (MP 52) | 4.3 / 2.07 | 0.13 / 0.4 | reload may be per-round; rough |
| Outlaw | bolt | 9 |  | ≥88 (MP 89) | 4.17 / 1.77 | 0.13 / 0.4 | reload may be per-round; rough |
| SDM | semi | 12 |  | ≥240 (MP 283) | 3.87 / 2.1 | — / 0.42 |  |
| Koshka | bolt | 8 |  | ≥49 (MP 54) | 4 / 2.02 | 0.12 / 0.35 | reload may be per-round; rough |
| Vendetta | semi | 20 | 3 shots to kill (in-game text) | ≥353 | 3.17 | 0.13 |  |
| Locus | bolt |  |  | ≥49 | 3.78 | 0.17 | reload may be per-round; rough |
| Havelina AA50 | semi |  |  | ≥171 | 6.57 | 0.12 | long reload; check footage |

&#91;image: `images/sniper_hip_frames_and_scope_views.png`\]

## LMGs

LMG reload animations run 4.9-7.2 s, about 1.5-2 s longer than the multiplayer ammo-in times. The shooter released the trigger early on the Titan and Hades, so those mags weren't fully emptied.

| Weapon | Fire mode | Mag | Damage (MP) | RPM | Reload, s (ours / MP) | ADS, s (ours / MP) | ADS recoil up, ° (mean / max) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Titan | auto | 75 | 46 | 480 (MP 485) | 6.05 / 4.55 | 0.22 / 0.33 | 0.41 / 0.85 |
| Hades | auto | 65 | 36 | 651 (MP 657) | 4.92 / 3.07 | 0.15 / 0.32 | 0.91 / 1.62 |
| VKM 750 | auto | 50 | 62 | 360 (MP 368) | 7.15 / 5.22 | — / 0.32 |  |
| Tigershark | auto | 48 |  | 500 | 3.73 | 0.23 | 0.20 / 0.65 |

&#91;image: `images/lmg_hip_and_ads_reference_frames.png`\]

## Gaps

Damage is the biggest hole: 22 of 43 guns have no damage source, mostly DLC weapons, all shotguns and most snipers. The fallback is to give each a value that hits the shots-to-kill of a known gun in its class, then tune by feel.

- **Not in this video:** draw and holster (swaps are hard cuts), hip-fire spread (no impacts to measure), damage falloff, sprint-to-fire, and any HUD.
- **Shaky in this video:** shotgun and sniper reloads (likely per-round), ABR 223 in-burst rate, Rampage fire mode, Doublecross cadence.
- **Worth covering in the next videos:** a gun swap or two for draw timing, a full empty reload on a shotgun and a sniper, and any footage with hit markers at known distances for damage falloff.
- Launchers, the Ballistic Knife, Reaver C86 and specialist weapons were skipped.

## Sources

- Video: Call of Duty: Black Ops 4 — All Weapons Showcase (YouTube 8VZ35Wbpiiw), supplied by Logan
- [GameGuideHQ: BO4 multiplayer weapon statistics](https://gameguidehq.com/black-ops-4-multiplayer-weapon-statistics-damage-time-to-kill-rate-of-fire-reload-time/) — MP damage, RPM, ADS, reload, mag
- [NamuWiki: Blackout weapons](https://en.namu.wiki/w/%EC%BD%9C%20%EC%98%A4%EB%B8%8C%20%EB%93%80%ED%8B%B0:%20%EB%B8%94%EB%9E%99%20%EC%98%B5%EC%8A%A4%204/%EB%B8%94%EB%9E%99%EC%95%84%EC%9B%83/%EB%AC%B4%EA%B8%B0) — Blackout mags, fire modes, Paladin damage
- [Call of Duty Wiki: ICR-7](https://callofduty.fandom.com/wiki/ICR-7), [KN-57](https://callofduty.fandom.com/wiki/KN-57), [Vendetta](<https://callofduty.fandom.com/wiki/Vendetta_(weapon)>) — RPM, reload, shots-to-kill
