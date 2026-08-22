# Changelog

## 1.0.17

- Fixed implausibly high `/top time` and `/mtop time` values (players showing hundreds or thousands of hours). Root cause: a player is added to the game's connected-players list (which `OnAutoSaveWorld` and `OnPlayerDisconnected` both read) as soon as their connection starts, but `ActivityTracker` only reset that player's session-start timestamp once the connection fully finished loading (`OnPlayerConnectedLate`, which can fire noticeably later, or never if the connection drops mid-load). Any autosave tick, or a dropped connection, landing in that gap computed "now minus their *previous* session's last-seen timestamp" - potentially days or weeks - and added the whole thing as playtime.
- Fixed by resetting the session-start timestamp as early as possible (`OnPlayerConnectedEarly`, which fires the moment the player is added to the connected list, closing the gap), and by discarding (with a console warning) any single tracked gap over 24 hours as a safety net, since a real gap between two tracked points is always far shorter than that.
- This only prevents new bad data going forward - an already-corrupted total in an existing `playeractivity.json` needs a manual edit.

## 1.0.16

- Fixed `{rolename:top}`/`{rolename:bottom}`/`{rolename:<group>}` double-wrapping a group's `prefix` in `[brackets]`. A `prefix` that already includes its own markup (e.g. `"<b>[Owner]</b>"`, common when embedding icons/colors directly) came out as `[<b>[Owner]</b>]`. Brackets are no longer added automatically - put them in the `prefix` value itself (e.g. `"[God]"`) if you want them. Updated `chatcolors.example.json`'s prefixes to include their own brackets so its default look is unchanged.

## 1.0.15

- Fixed `chatcolors.json`'s `Format` being silently ignored - reset right back to the default - if it was added to an *existing flat file* without also moving the per-group entries under a `Groups` key. The loader required an explicit `Groups` wrapper to recognize `Format` at all; without one, it fell back to treating the *entire* file (including the `Format` string itself) as the group map, which doesn't parse as a group and just get dropped. `Format` is now recognized on its own regardless of whether a `Groups` wrapper is present - if it's absent, every other top-level key is treated as a group automatically, so adding `Format` to an existing file just works.

## 1.0.14

- Fixed `/ban {playername} ...` failing with "player not found" for offline players. `PlayerHelper.TryGetPlayer`'s name lookup skips offline players unless explicitly told not to (`includeOffline: true`), and `BanCommand.cs` wasn't passing that flag - so only banning by raw SteamID64 worked while the target was disconnected, not by nickname. Verified `Chat.Send`/`Players.Disconnect` are already safe no-ops for an offline target, so this was the only blocker.

## 1.0.13

- Fixed unquoted multi-word player names (e.g. `/groups Aldric Stormvale`) being cut off at the first space and failing with "player not found". The 1.0.12 whitespace-insensitive matching fix only helped once a name actually reached `PlayerHelper` - but each command's own argument regex was already truncating it to the first word before that, so it never got the chance to help. Fixed the regex in `/groups`, `/kick`, `/list`, `/lastseen`, `/killplayer`, `/killnpcs`, `/unmute`, `/tpa` and `/antigrief` (name is the only/last argument, always safe), and in `/ban`, `/mute`, `/rpban` and `/jail` for the case where a numeric time value is given (used to unambiguously locate where the name ends, verified against several edge cases including a reason that itself starts with digits). `/warp`, `/trade` and `/whisper` still require quotes for multi-word names - there's no numeric anchor to disambiguate against, and guessing wrong there risks warping/trading/messaging the wrong player.
- Fixed `ColonyNameGuard` never re-checking a colony's *current* name against the blacklist if that name hadn't changed since it was last accepted as fine. This meant a colony that already existed (with its name unchanged) before a word was added to `blacklistedwords.json` - or before `/bcreload` - stayed unflagged forever, since the guard only re-validated a name when it *changed*, ignoring the freshly-computed blacklist result entirely in that case.

## 1.0.12

- `PlayerHelper.TryGetPlayer` (used by every command that takes a playername argument) now falls back to a whitespace-insensitive comparison when there's no exact match: a splitscreen profile named e.g. `Kleator 1` can now be targeted by typing `Kleator1` (one word, no quotes needed) - previously the chat command parser split unquoted arguments on whitespace, truncating the name at the space, so only `/command 'Kleator 1'` (quoted) worked. An exact match (e.g. plain `Kleator` for the main account) is still always tried first, so the two are never confused. This also tolerates extra/missing spacing anywhere in a typed name generally.

## 1.0.11

- Added `/bcreload` (permission `mods.scarabol.commands.bcreload`) to reload `antigrief-config.json`, `chatcolors.json`, `travelpaths.json`, `blacklistedwords.json`, `jail-config.json` and `announcements.json` live, without a server restart.
- Deliberately scoped to genuine config, not runtime/history data (`playeractivity.json`, ban logs, jail log, roleplay bans, war status) - reloading those could discard in-memory progress or restart a background timer a second time. `JailManager` and `ColonyNameGuard` gained a `ReloadConfig()` that re-reads settings without re-running the startup-only jailed-player-snapshot restore or restarting their self-rescheduling loops (jail release checks / colony name scans).

## 1.0.10

- Fixed `ColonyNameGuard` permanently and silently keeping a blacklisted colony name in one specific case: the automatic `{OwnerName}'s Colony` fallback rename embeds the owner's own player name, which can itself contain a blacklisted word. The guard only re-checks the blacklist when a name *changes*, so it would accept that still-bad fallback as "known good" on the spot and never look at it again. It now re-checks its own replacement name and falls back further to a generic `Colony {id}` name (which can never match a word-based rule) if needed.
- Expanded `blacklistedwords.example.json` from 60 to 75 entries: 15 additional English words/slurs not already covered by existing rules, and every pattern hardened per-letter against letter-stretching (`"sscch"` now still matches `sch`) and fuller leetspeak substitution across every vowel/s/t/g/b occurrence, not just the first.

## 1.0.9

- Fixed `/setcolonyname`, `/groups`, `/tpa`, `/tpaccept` and `/tpadeny` returning "Unknown command": this mod registers chat commands through an explicit list in `AntiGrief.cs` rather than auto-discovery, and the five new 1.0.8 commands were never added to it.
- Renamed `blacklistednames.json` to `blacklistedwords.json`.
- `blacklistedwords.json` entries are now .NET regex patterns instead of plain substrings (a plain word still matches as a literal substring, so existing lists keep working). `blacklistedwords.example.json` ships with a starter list covering common profanity/slurs in English, German, French and Spanish, using character classes for basic leetspeak resilience.
- Every automatic colony name correction now logs a console warning with the rejected name, the corrected name, and the exact `blacklistedwords.json` rule that matched, instead of happening silently.

## 1.0.8

- Added `/setcolonyname {new name}` so admins can rename any colony (found via the closest banner), regardless of ownership.
- Added an optional `blacklistednames.json` (see `.example.json`): colony names are periodically checked and reverted if they're newly created or renamed to something blacklisted (normalized match, tolerant of spacing/punctuation/accents), with the owner warned in chat.
- Added `/tpa {playername}`, `/tpaccept`, `/tpadeny`: teleport requests with a 3s accept countdown (so the requester isn't teleported without warning) and a 60s request timeout.
- Added `/groups {playername}` to list a player's explicitly assigned permission group(s).
- `chatcolors.json` now controls the entire chat line layout via a `Format` template string with placeholders (`{name}`, `{rolename:top}`/`{rolename:bottom}`/`{rolename:<group>}`, `{role:<group>;string:"..."}`, `{rp}`, `{message}`) instead of a hardcoded layout - old-style files (no `Format`) still load unchanged. See README's "Chat Formatting" section.
- Player names printed by other commands (`/top`, `/mtop`, `/online`, jail, ban, kick, whisper, trade, war, roleplay ban, etc.) are now formatted the same way the live chat line is.
- Fixed `jail-config.json`'s `restoreGroupsOnRelease`: it relied on a reference-equality check that could never match, so it silently always fell back to `defaultGroup` instead of a released player's actual prior group(s).
- Added `jail-config.json` `rolesGrantedOnJail`/`rolesGrantedOnRelease` (lists of group names) to grant extra groups while jailed (auto-removed on release) or after release.

## 1.0.7

- Maintenance of Better Colony Commands (formerly Colony Commands) taken over by Kleator from this version onward. Credit to Xell for maintaining the mod through version 1.0.6, and to Adrenalynn as the original developer.
- Fixed players losing their `lastSeen`/playtime history in `playeractivity.json` after reconnecting. Game version `0.18.0.0` added a splitscreen "profile" field to player IDs, changing `Player.ID.ToStringReadable()` from `"<id> (<steamid>)"` to `"<id> (<steamid>, profile <n>)"` - the exact string `playeractivity.json` is keyed by. An existing entry silently became unreachable the first time its player reconnected, indistinguishable from their stats having reset.
- Added automatic migration of `playeractivity.json` on world load: existing keys are matched to their player's SteamID and renamed to the current key format (defaulting previously-known players to `profile 0`). Players never seen before are left untouched and migrate the next time they connect.
- Declared compatibility with Colony Survival `0.18.0.0` in `modInfo.json`.

## 1.0.6

- Rebuilt and cleaned for Colony Survival `0.15.1.0` and `0.15.1.1`.
- Added transport compatibility fallback for rail, elevator and glider interactions.
- Preserved smart quarter block placement by reserving clicks only for actual transport targets.
- Generates default anti-grief config in the savegame when missing.
- Added delayed anti-grief join notice and optional popup.
