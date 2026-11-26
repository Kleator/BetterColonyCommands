# Changelog

## 1.2.0

### New: audio sources
- Admins place music and sounds in the world (`/audio`, window also in `/bcconfig` → *Audio sources*, permission `mods.scarabol.commands.audio`) - lobby music at the spawn, bells, ambience. Tracks are the Ogg Vorbis files in the world folder `bcc-audio/` (registered at server start; every player downloads them once when joining).
- Per source: **Positional** (3D, smooth fade with distance) or **Area** (same volume inside an inner radius, fading out towards the range), range (up to 4096 blocks - every positional source's exact range is prepared at server start, a range changed while running uses the nearest of the steps up to 1024 until the next restart), volume, loop with adjustable **crossfade** between end and start, **fade in / fade out**, synced ("radio") or from the start for each player, **triggers** (in range, enter a radius, right-click the block, join, interval, daybreak, nightfall, time of day, manual), who hears a triggered start (everyone in range / only the triggering player), duration, cooldown, day/night condition, and **channels with priority** for blending (only the best source of a channel is heard, the others fade out).
- **Sync with**: a source follows another source's timeline (same point in the music, same loop rounds, triggered starts join there) while keeping its own trigger, track, volume, range and fades - for seamless hand-overs between areas (same channel). Loops are detected, renaming follows, deleting frees it.
- Only players in range get anything - a source at the spawn is never sent to anyone elsewhere.
- The game only plays Ogg Vorbis: mp3, wav, m4a, flac, ... files in `bcc-audio/` are listed in the window, in `/audio tracks` and in the server log with how to convert them (Audacity or ffmpeg; "already converted" if the .ogg is there; MIDI needs to be exported as audio first).
- `/audio show` toggles markers (label + range ring) for yourself, default off. Chat commands for everything: `/audio add|remove|move|tp|play|stop|on|off|set|info|edit|tracks|near`.
- Global settings in `bcc-settings.json` → `Audio` (range steps, volume steps, fade step time, markers, ...). `/bcreload` also reloads `bcc-audio.json`.
- BccApi (API version 2): `GetAudioSourceNames`, `PlayAudioSource`, `StopAudioSource`, `SetAudioSourceEnabled`.
- Self test: `!audio sim|fire|stop|status`, audio windows in `!ui`.

## 1.1.1

### Fixes
- The role tag (`{rolename:top}` in the chat line and the tag next to player names in every window) shows the player's real highest role again. It used to pick the first matching group of `chatcolors.json`, so a base role that every time role inherits - like a default role at the bottom of the top strand - was shown for almost everyone. Now the role tree decides: tree order from the top, strand by strand as in the role window, and a role that another of the player's roles includes only counts if the player has nothing above it. Example: Explorer + Gatherer + Lumberjack + Farmer shows Farmer, Explorer + Scientist + Overseer shows Overseer, Mod + Farmer shows Mod. Role editor changes apply right away; `chatcolors.json` needs no change.

## 1.1.0

Big feature update: windows (UI) for everything, an integration API for other mods, an economy module and new commands.

### New commands
- `/roletime` - your online time, the next role you will reach and the time left, and every role still ahead of you (highest first; roles you already have are not listed). Role times come from Pytonia Automation's time based roles (`pytonia-automation.json`, read from disk, so it also works without that mod loaded) or from `bcc-settings.json` → `RoleTime.RequiredHours`.
- `/achievements` - whether Steam achievements are still on for you, your colonies and the whole world (the game has no world switch: cheats like `/disableachievements`, `/loot`, `/teleport`, `/setflight` or `/debug` switch them off per player and colony group, and joining such a colony spreads it). Admins (`mods.scarabol.commands.achievements.enable`) see who is affected and switch them back on with `/achievements enable {player|all}` - the player, their colony groups and the co-owners (or the game would switch them off again); the client is told right away, saved with the world. Flight mode is left alone.
- `/roles` - all roles (permission groups) with their chat formatting and required online time.
- `/rtp` - random teleport to a safe spot `Rtp.MinDistance`..`Rtp.MaxDistance` blocks around you, with a server-side cooldown (`Rtp.CooldownSeconds`, bypass permission `mods.scarabol.commands.rtp.nocooldown`). Never lands in water, in the spawn/custom protection areas or in foreign banner areas (configurable); uses the terrain generator's height where the chunk isn't loaded.
- `/modlist [page|name]` - all installed mods with preview image, version and description; details page per mod (author, website, game versions, long description, newest patch notes). See `05_Modinfo-Standard` in the README for the fields a mod can provide.
- `/permissions [filter|page|unused|used|open]` - every permission that exists in the game (registered by any mod or found in the permission files), its mod, description, which groups/players have it and which are unused. Only for `mods.scarabol.commands.permissions` (groups with the all-permission `""`, e.g. god, have it).
- `/bcconfig` - admin configuration window for every setting (`mods.scarabol.commands.config`): antigrief-config.json, bcc-settings.json (UI, /rtp, economy, mod list, role times), jail-config.json and chatcolors.json (format and every group's prefix/colors).
- `/bcui {exclude|include|list|on|off|reset} [command]` and `/bcsettings` - choose which commands show their output in a window.
- Economy: `/money [player]`, `/pay {player} {amount}`, `/baltop`, `/exchange {tomoney|tocoins} {amount}` (THE TRADER coins), admin `/addmoney`, `/removemoney`, `/setmoney` (`mods.scarabol.commands.economy.admin`).

### Windows (UI)
- `/help` opens a command window (only commands you may use), with filter, paging and a details page per command where the parameters can be filled in and the command executed. Commands you have no permission for are not listed, and their details page shows neither input fields nor an execute button; permissions are re-checked on the server for every click.
- Info commands typed in chat (`/top`, `/mtop`, `/online`, `/lastseen`, `/roles`, `/roletime`, `/groups`, `/banlog`, `/jailrec`, `/inactive`, `/list`, `/money`, `/baltop` … - `bcc-settings.json` → `Ui.WindowCommands`) show their output in a window with paging.
- Every window has a Back button to the previous page and paging where lists continue.
- Players exclude commands from windows with `/bcui exclude <command>` or in `/bcsettings`; the server list `Ui.ExcludedCommands` (and `Ui.Enabled`) always overrides the player setting.
- Rich text is rendered in all windows; `<size>` tags are removed from player-defined names (colonies, banners, players) everywhere in chat and UI, so a name can never show up oversized.

### Integration API for other mods
- `ColonyCommands.BccApi` (reflection friendly): `RegisterCommand`/`RegisterCommandEx` (description, usage, parameters, aliases), `RegisterPermission`, `CanUseCommand`, economy (`GetBalance`, `AddBalance`, `TryRemoveBalance`, `SetBalance`, `TryTransfer`, …), `ShowOutputWindow`, text helpers.
- Commands of other mods are discovered automatically from any `*CommandRegistry` class with an `All` collection (all Pytonia mods) - with descriptions - and listed below Better Colony Commands' own in `/help`. The old `HelpCommand.commandPermissionList` reflection hook keeps working unchanged.
- Mods without such a registry (e.g. MapMod's `/map`) are found too: Better Colony Commands reads the `/command` texts their chat command handler compares with, and takes the description from the sentence of the mod's own description that names the command.
- Commands without a permission get an auto-created one (`bcc.cmd.<command>`, `<modslug>.cmd.<command>` for other mods) that is open to everyone until an admin assigns it to a group or player - then it is enforced for that command, also for other mods' commands.

### Economy module
- Based on Pytonia Automation's economy (balances, starting balance, passive income, HUD counter), can be disabled server-side (`Economy.Enabled`).
- With Pytonia Automation installed, its economy stays in charge and Better Colony Commands mirrors every balance into `bcc-economy.json`; a three-way merge at startup keeps changes made in either mod while the other one was missing, so removing (or re-adding) Pytonia Automation never loses money.
- THE TRADER (Trader Coins are stockpile items): coin stocks are snapshotted; if THE TRADER is removed, the coins are converted into money for the colony owners (`TraderCoinValue`, owners are told on their next join) and the now unusable placeholder coins are taken out of the stockpile, so nothing is counted twice.

### Role management (window)
- `/bcconfig` → **Roles, permissions & role formatting**: all roles (permission groups) as a tree - a role inherits every permission of the roles below it, multi-level. Create roles (optionally placed under one role and inheriting from another), delete roles (with confirmation; the default role can't be deleted) and set the default role for new players.
- Every role has its own page: rename (updated everywhere: other roles, `permissionusers.json`, `betterchat.<role>`/`permissions.setgroup.<role>` permissions, `chatcolors.json`, `jail-config.json`, Pytonia Automation and home limits), chat formatting (tag, tag color, name color, message color - the role automatically gets the `betterchat.<role>` permission that applies it, chat priority follows the tree), inheritance in both directions with loop protection, own permissions (add/remove, picker with every known permission and its mod), Pytonia Automation values (online hours, time based exclusion, homes, max colonies) or the home limit.
- Loaded from the existing `permissiongroups.json` (the game's default file is copied into the world folder on the first change); the game's permissions are reloaded after every change; backups as `.bak`.
- **Protection against game updates**: depending on the server setup a game update puts the game's default `gamedata/settings/permissiongroups.json` back. Better Colony Commands keeps a copy of the last state with its game version in the world folder (`bcc-permissiongroups-backup.json`). If the file changed **and** the game version changed since then, the last state is put back as the world's own `permissiongroups.json` (which the game prefers from then on), the permissions are reloaded before anyone joins, the reset file is kept in `bcc-permission-backups/`, and admins are told when they join. Changes with the same game version (role editor, edits + `/reloadpermission`) renew the backup; earlier versions are kept (`Permissions.KeepBackups`, default 10). Can be switched off: `/bcconfig` → Permission groups backup.

### Home points
- `/sethome [name]`, `/home [name]`, `/homes`, `/delhome [name]` taken over from Pytonia Automation, plus a **My homes** window (teleport, set, delete). Limits per role (`bcc-settings.json` → `Homes.RoleLimits`, `DefaultLimit`, initially imported from Pytonia Automation's roles).
- Home points are now a Better Colony Commands feature: its own data (`bcc-homes.json`), its own limits per role and its own commands, which always answer first - also if an older Pytonia Automation with its own home module is still installed. At every start, homes found in `pytonia-automation.json` that Better Colony Commands doesn't know yet are imported (homes deleted in Better Colony Commands stay deleted); nothing is written back to Pytonia Automation. The home limits of the roles are taken from Pytonia Automation once on the first start.

- Pytonia Automation's home permissions are migrated once: whoever had `pytonia.automation.home` (directly or through a parent like `pytonia.automation`), `.sethome` or `.delhome` - roles and players with own permissions - gets `bcc.cmd.home` + `bcc.cmd.homes`, `bcc.cmd.sethome` or `bcc.cmd.delhome`. A Pytonia permission nobody had goes to the all-permission roles, so the commands stay as restricted as before. Logged in the server log; done once per world.

### Trust on claims ("Manage Permission")
- New button **Manage Permission** right next to "Manage Owners" in the game's Colony tab (only for the colony's owners), also `/trust` (window of your active colony) and from the `/help` window: who may build on this claim (the colony's banner area). Trusted players are listed at the top with **Remove**, a **Guards: friendly/hostile** toggle when Angry Guards is installed ("friendly" light green, "hostile" red), and **Player**; below, players can be added - online players first, offline ones by searching their name. Opened from the Colony tab, "< Back" returns to the Colony tab.
- **Player** opens that player with all of **your** claims: trust or remove per claim, **Trust on all my claims** / **Remove from all**, and the Angry Guards friend toggle for all your guards. `/trust {player}` opens it directly; `/trust add|remove {player}` and `/trust list` work in chat.
- A trusted player may now really **build** in the banner area (Anti-Grief treats them like an owner; stockpile and jobs stay with "Manage Owners"). `/antigrief banner {player}` (all your claims) works the same way now - before it only allowed placing a banner close to your colony. Trusted players get a chat message.
- Fixed: the per-colony Anti-Grief permission was written without leading zeros (`...banner.colony.5`) and, because the game matches permissions by prefix, would also have covered colonies 50-59, 500-599, ... - it is `...banner.colony.0000000005` now.

### Protected areas
- Colonists or job blocks that try to change protected blocks no longer spam the chat: the colony's owners get **one** entry in the game's notification list per session (position and reason); marking it read or deleting it is the acknowledgement.

### Player moderation (window)
- `/bcconfig` → **Player moderation**, the **Players** button in `/help` or `/players [search]` (permission `mods.scarabol.commands.playermoderation`): every player of the server, last online first, searchable, with filters (online, with incidents, banned) and their status at a glance (BANNED, JAILED, muted, rp-banned, number of incidents).
- Each player has a page: status, SteamID, position, colonies; actions Teleport to player, Kick, Mute/Unmute, Jail/Release, Ban (with confirmation - like `/ban` it deletes colonies the player owns alone)/Unban; online time; money (set/add/remove); roles (add/remove); own permissions (add, remove, picker); colonies & banners (rename, teleport, remove the player as owner, delete with confirmation) and - with Pytonia Automation - the colony limit override; homes (teleport, delete); the complete moderation history plus notes and warnings.
- Every action needs its own permission as well (kick, mute, jail, ban, economy.admin, config, setcolonyname, purgebanner, warp...); buttons without it are disabled. All actions are written to the server log and to the player's history.
- A **warning** is shown to the player as a popup ("Official warning") - on the next join if they are offline - until they confirm it with OK; the confirmation is recorded in the history.
- Only moderators see the "Players" button, the "Player moderation" row and can use `/players`; the "Announcements" row is shown only to those allowed to edit announcements.
- Singleplayer, LAN and hosted games: an orange note on every role/permission/moderation page and in `/roles`, `/groups`, `/permissions` explains that in singleplayer and LAN games the game gives every player every permission and can't store roles (they only work on a server or a world hosted for friends), and that the host of a game hosted for friends always has every permission.
- Players without SteamID (singleplayer and LAN games): the game can't store roles or own permissions for them (and gives them every permission) - the player window now says so instead of reporting a success that didn't happen.
- New `bcc-moderation-log.json`: kicks, mutes, unmutes, jail releases, unbans, roleplay bans, blocked colony names, killed colonists, warnings, notes and admin changes; bans and jail sentences keep their own logs and are merged into the history.

### Roles
- Each role page lists the players that have the role, with "Remove" and an "Add player" field (name or SteamID, also offline players). Player counts come from the game's live data.
- Roles are recognized the way the game grants them, so the role pages, the player page, `/groups`, `/roles`, `/roletime` and home limits match the chat role tag: roles in the player's entry, the **default role** for players without an entry **and** for players whose entry the game created as a copy of the default role (it copies the role's permissions, e.g. `betterchat.<role>`, instead of its name - most players hold the default role this way), and roles held **through a higher role** (shown as "through ...", without Add/Remove - change the higher role instead). Read at server start and live from the game's data afterwards.
- A role can't be added twice any more ("Add" only where the player really doesn't have the role, "Add player" refuses players who already have it); "Remove" takes away every copy, and for the default role also the permissions the game copied. Duplicate roles that are already in `permissionusers.json` (the game's own add doesn't check) are removed at start and when a player joins; the role test never restores a role twice.
- **Test a role**: a "Test" button on every role in the role list and on each role page (and `/roletest {role}`) - your roles are saved, you get only that role for 2 minutes, then your roles come back automatically. A chat message tells you right after the switch (with `/roletest end` to stop early; anyone in a test may use it, also when the tested role can't open the role pages). The saved roles are written to `bcc-roletests.json` before anything changes, so the roles also come back after a disconnect, when the server stops and - after a crash - at the next start. Roles given to the player by others during the test are kept; BCC's own role tools (role pages, player moderation, `/promote`, jail, war) end the test first.

### Game commands in /help
- The game's own commands (`/time`, `/tps`, `/save`, `/backup`, `/teleport`, `/loot`, `/setflight`, `/colony addowner`, `/addgroup`, `/setgroup`, `/whitelist add`, `/debug ...` and more) are listed under "Colony Survival" with description and the permission the game really checks (taken from the game's server code). Commands the game doesn't check at all (e.g. `/debug clearinventory`, `/debug clearstockpile`) get an auto-created `colonysurvival.cmd.<command>` permission that can restrict them.
- Sub commands are matched individually (`/debug clearstockpile` has its own permission entry).
- Auto-created permissions can also be restricted through a parent: assigning `colonysurvival.cmd.debug` to a role restricts all unchecked `/debug` commands at once (`bcc.cmd` all of BCC's open commands, etc.). The all-permission `""` doesn't count, so a god role alone doesn't restrict anything.

### Announcements window
- `/announcements` without arguments opens the window (for admins and players with `mods.scarabol.commands.announcements`); `/announcements list|add|...` work in chat as before.
- `/bcconfig` → **Announcements**: welcome message, interval in minutes, every rotating message with On/Off, Edit (multi-line with preview), Up/Down, Send now and Delete, plus new messages - all by buttons.

### Fixes and improvements
- New defaults: `Ui.WindowWidth` 1000, `Ui.WindowCommands` /help, /modlist, /roles, /roletime, /permissions, /top, /mtop, /groups, /banlog, /jailrec, /inactive, /list, /baltop, /bcsettings, /bcconfig; default chat line format `{name}{rolename:top}{rp}: {message}` (existing settings files keep their values).
- `/ban` stored the banned player instead of the admin as "banned by"; banning a player who was the only owner of a colony ran its colony deletion loop the wrong way - both fixed.
- Row buttons did nothing: "Edit" in the role list, "Remove" on a role page and "Teleport"/"Delete" in "My homes" looked up the wrong row list - fixed.
- `/rtp` never drops players from the sky any more: where the chunk isn't loaded yet the generator height (max. 2 blocks above ground) is used and the player is put exactly on the ground as soon as the chunk has loaded; in addition fall damage is cancelled until the first landing (parachute, max. 30 s).
- Every command executed from a window is written to the server log with player, the full command line and its result (`[BCC UI] ... executed: ...` / `result ...`).
- Small Better Colony Commands logo (`ui-logo.png`) centered under the window title on every page.
- Lists scroll on one page; they are only split into pages beyond `Ui.MaxLinesPerPage` lines (default 500, replaces `PageSize`/`OutputLinesPerPage`).
- Mod and section headings in windows are dark bars with a gold accent and gold text, commands use a lighter blue - contrast checked against an in-game screenshot.
- Window title "Better Colony Commands" with a color gradient instead of "Server Popup"; buttons and input fields keep a margin to the scrollbar; alternate list rows were partly white with invisible white text (a transparent row color is turned into white by the game) - fixed everywhere.
- `/help` and `/modlist` are one scrollable page (no paging). The mod list is sorted by "recently updated on this server" (newest file in the mod folder) with an A-Z option (`/modlist az`), the details page shows the complete version history (modInfo `patchnotes`, or the mod's `CHANGELOG.md`).
- Commands with their own window (`/help`, `/bcsettings`, `/modlist`, `/permissions`, `/bcconfig`, `/homes`) open that window when executed from the help window (with Back) instead of a plain output window.
- Every setting in `/bcconfig` has an explanation (what `0`/`-1` mean).
- Time based role settings belong to Pytonia Automation: `/roletime` and the "Time based roles" config section only exist while it is installed (BCC's own role time settings were removed); "roles still ahead" are listed next role first.
- `/warpbanner` without a name warps to the closest banner of all your colonies and outposts again (not only the main colony); a colony without banner no longer fails with "Unknown command".
- `/bannername` also shows when each owner was last online.
- A space between player name and role tag wherever both are shown (`/mtop`, `/groups`, ...); `/baltop` shows names with their role formatting.
- `/promote` confirms the new role.
- `/warpbanner <name>` (and every other colony lookup) ignores formatting and decorative symbols: `Highrise-City3` finds `<b><size=25><color=#EB34C9>✮Highrise-</color><color=#FF4FB9>City✮3</color></size></b>`.
- Roles are shown with their chatcolors.json formatting wherever they are mentioned (`/roles`, `/groups`, `/roletime`, `/permissions`).
- `/help` redesigned like `/pytoniahelp`: colored command plus description, grouped by mod, paging (`/help chat [page]`, `/help <filter>`, `/help <command>`).
- Fixed `antigrief-config.example.json` using the key `"ShowJoinPopup "` (trailing space), which was silently ignored.
- War mode (`/war start`) gives players their roles back again: the old code compared role objects that never match, so nothing was saved and players stayed in the war role (players without an own entry even caused an error); saving the roles to disk was not implemented at all. Now the roles are written to `war-permissions-to-restore.json` before they change and restored exactly at the end of the war or at the next start after a crash.
- Where banner areas of two colonies overlap, a block there needs the permission (owner or trusted) of every colony in range - before, the first colony found decided.
- Home permissions of Pytonia Automation are only migrated if Pytonia Automation really had homes; on servers with a Pytonia Automation without home module the home commands stay open for everyone.
- Renaming or deleting a role ends role tests that use it first (the tested role could otherwise stay with the tester).
- The permission groups backup also restores a reset to the game's default without a game update (hosts that put the game files back on every start).
- Removing the default role from a player removes copied permissions only if the game really copied them; permissions given to the player individually stay.
- `/achievements` shows the number of colonies and players only to admins.
- Declared compatibility with Colony Survival `0.18.0.10` in `modInfo.json` (built and tested against it).
- New `ShowJoinChatMessage` in `antigrief-config.json` (also in `/bcconfig` → Anti-Grief & server): unticked, no player gets the "Anti-Grief protection enabled" chat line any more (the join popup has its own setting).
- New settings file `bcc-settings.json`, player window settings in `bcc-playersettings.json`; both reloaded by `/bcreload`.
- Built-in self test for server admins/developers (`bcc-selftest.txt` in the world folder, see README).

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
