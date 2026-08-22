# Better Colony Commands

Server-side Colony Survival mod with anti-grief protection, moderation commands, admin utilities, jail and war systems, announcements, chat colors, activity tracking, trade, warp, travel and cleanup commands.

This is a maintained continuation of Colony Commands, originally by Adrenalynn and continued by Xell, rebuilt and cleaned for current Colony Survival releases. Maintained by Kleator since version 1.0.7.

## Compatibility

- Colony Survival `0.15.1.0`
- Colony Survival `0.15.1.1`
- Colony Survival `0.18.0.0`

## Credits

- Original mod: Colony Commands by Adrenalynn.
- Continued through version 1.0.6 by Xell.
- Maintained from version 1.0.7 onward by Kleator.
- Special thanks to Tjohei for most of the ideas and feature requests.
- Special thanks to Crone for the chat coloring system and score system.

## Current Maintenance Notes

- Rebuilt against the current Colony Survival server API and cleaned up for modern saves.
- Added a transport compatibility fallback so rail, elevator and glider interaction still works on current game versions.
- Kept smart quarter block placement intact by reserving clicks only for actual transport targets.
- Default anti-grief config is generated directly in the savegame if no config exists yet.
- The anti-grief join notice is delayed and also shown as a small popup because the game chat can remain collapsed on join.
- Set `ShowJoinPopup` to `false` in `antigrief-config.json` if you only want the chat line.

## Anti-Grief

This mods adds an anti-griefing mechanism to the server.
Regular players can't change 50 blocks around the spawn point and 50 blocks around other player banners.
Both values can be adjusted.
Furthermore players are auto-**jailed** if they kill more than 2 foreign colonists, auto-**kicked** if killing more than 5 and auto-**banned** if they kill more than 6 foreign colonists.
Those values can be configured in the config file, set them to 0 to disable the feature.

Players are also able to whitelist friends by writing **/antigrief banner [friendly-player-name]** in chat. 

Last but not least players with master permission **mods.scarabol.antigrief** are able to define custom protected areas.
Use **/antigrief area 10 10** to restrict regular players from changing blocks in an area 10 blocks around your current position.

To **disable anti-grief** in case you play with friends only, just set the protection ranges in the *antigrief-config.json* file in your savegame folder to *-1* each. The message on joining the server will still show up, but it has no effect then.

## Activity-Tracker

This mod adds an player activity tracker. It updates a last seen timestamp for each player, on each player login, logout or world auto-save. Furthermore the total time played is tracked.

Use **/lastseen [playername]** to view the timestamp for a player.

Use **/top time** to view a top ranking with the most time played.

If a tracked gap since a player's last recorded activity would add more than 24 hours of playtime in one go, it's discarded (logged as a warning) instead of being added - this guards against implausible totals from a connection that dropped mid-load or similar, rather than crediting a stale timestamp as played time. This only prevents *future* bad gaps; an already-corrupted total in `playeractivity.json` needs a manual edit (that file lives at `gamedata/savegames/<worldname>/playeractivity.json`).

## Announcements

This mods adds a welcome message and automatic announcements to the game.
See *announcements.example.json* and place it in your savegame folder, like */gamedata/savegames/mysavegamename/announcements.json* to activate them.

## Colonists Cap

This mod can set a maximum for each players colonists number. Each colonist beyond the limit will be killed. By default the limitation is disabled.

Players with **mods.scarabol.commands.colonycap** permission can use **/colonycap [max-number-of-colonists] [check-interval-delay]** to configure a limit or **/colonycap -1** to disable the limitation.

It is also possible to use different colonist limits based on player tier (group) and even colonist limits per colony and difficulty settings.
Add **ColonistCapacityTiers** to *antigrief-config.json* as an array with the different tier. The tiers have to be assigned to groups as a permission **mods.scarabol.commands.colonistcapacity.tier1** .tier2 and so on.

For limits per colony and tier add **ColonistLimitsColonyDifficultyTiers** to *antigrief-config.json*. It uses the same permission tiers as above but for each tier you can define an array of limits which is based on the colony difficulty settings, aka number of zombie spawns. For both day and night zombies they range from 'none' to 'very high' (1-5). Those are added together as difficulty so there are 10 possible difficulty settings.

## Fast Travel System

When a travel path is set up, a player approaching the spot gets automatically warped to the end point.

Creating travel paths requires permission **mods.scarabol.commands.travelpaths**. Edit *DefaultWarpRange* in *antigrief-config.json* to change the range for warping spots (how close a player has to be).

Use **/travel** to start a travel path and type it again to set the end point.

Use **/travel start** to reset the start point and **/travel remove** near a travel path to remove it.

## Whispering

Use **/whisper [playername] hello friend** or **/w [playername] hello friend** to send a chat message to a specific player instead of the complete server.

## Spam-Protect

Use **/mute [playername] [minutes]** to block a player from EVERY chatting for certain minutes.
After timeout the player is automatically unblocked or one can use **/unmute [playername]** to remove the blocking manually.

Muting and Unmuting players requires the **mods.scarabol.commands.mute** permission.

## Online Backup
Add **OnlineBackupIntervalHours** to *antigrief-config.json* to enable a regular online backup. Please note that a regular offline backup is still highly recommended!

## Jail System

The jail is an extension for the Anti-Grief system. Admin or staff members can throw players into jail as punishment. A jailed player can only move within the defined jail area. Should a player try to escape repeatedly his/her jail time will be automatically extended (configurable).
Getting jailed is also available as punishment option for killing another player's colonists.

Use **/setjail x y z** to define the server jail at your current position. x y z defines the dimensions of the jail. 
**Notice:** The jail will automatically create a protection area with its range. You might want to create a larger protection area instead, depending on your actual jail build.

Use **/setjail visitor** to define a visitor position for people wanting to visit the jail and its inmates. This position can be outside or inside the jail, visitors are always free to move.
Requires permission **mods.scarabol.commands.setjailposition**

Use **/jail {player} [time] {reason}** to send a player into jail. The default jail time is 3 minutes or you can specify the exact amount. Reason can be any text (including white spaces) but don't start it with a number (would be taken as time value instead).
Requires permission **mods.scarabol.commands.jail**

Use **/jailrelease {player}** to release a player from jail for good conduct. Players will be released automatically after their time is done.
Requires permission **mods.scarabol.commands.jail**

Visitors can use **/jailvisit** to visit the jail (if the visitor position was set). Visitors are completely free to move and can leave anytime.
For convenience there is also **/jailleave** which will warp a visitor to the position where he/she used /jailvisit. Just in case they have no others means to leave.
No permissions required for both commands

A jailed player can use **/jailtime** to check his/her remaining time in jail. Once the time is done the player will be automatically released and teleported to spawn.
No permission required

Use **/jailrec [player]** to check the jail history records. With a player name given it will show all jail records for that player. Without player name it will show the last 10 jail actions.
Requires permission **mods.scarabol.commands.jail**

## War system

The war system supports roleplay and player / colony war. Players that enable war are allowed to kill NPCs of other players that also have war enabled.
**/war start [ 120m | 2h | 4h ]** to enable war mode. The default duration is 2 hours (can be changed by config file) and can be extended. Up to the next server restart (war mode does not get saved).
A player needs to be at an active colony with colonists to start wars.
**/war** Lists all players that currently have war mode enabled and their remaining time
**/war end** Can be used by staff to end ongoing wars. Requires permission **mods.scarabol.commands.endwar**


<dl>
<dt>Configurable Settings</dt>
<dd>After the first start a new file jail-config.json will be generated at the world save directory (gamedate/savegames/&lt;worldname&gt;).</dd>
<dd>defaultJailTime: 5 Minutes, can be adjusted to any value</dd>
<dd>graceEscapeAttempts: 3. Number of escape attempts after which additional jail time is added. Set to 0 to disable.</dd>
<dd>protection-ranges.json contains a new entry NpcKillsJailThreshold, similar to kick and ban thresholds. If unwanted, set the value higher than the kick threshold to disable jailing as punishment option.</dd>
<dd><b>restoreGroupsOnRelease</b> (bool): if true, a released player is restored to whatever permission group(s) they actually had before being jailed, instead of always <b>defaultGroup</b>.</dd>
<dd><b>rolesGrantedOnJail</b> (list of group names): granted in addition to <b>prisonerGroup</b> while jailed, automatically removed again on release.</dd>
<dd><b>rolesGrantedOnRelease</b> (list of group names): granted in addition to whatever group(s) the player is restored/set to on release.</dd>
</dl>

## Colony Names

Use **/setcolonyname {new name}** to rename the colony whose banner is closest to you, regardless of ownership. Requires permission **mods.scarabol.commands.setcolonyname**.

If **blacklistedwords.json** exists in the world save directory (see blacklistedwords.example.json), colony names are checked every ~10 seconds against it. A newly placed banner with a blacklisted name is renamed to the game's own default (`{OwnerName}'s Colony`); an existing colony renamed to a blacklisted name is reverted to its previous name. The owner is warned in chat either way. This is a moderation net, not a hard block - repeatedly trying to dodge it with special characters risks the colony being purged by staff.

If the replacement name would itself still be blacklisted - e.g. `{OwnerName}'s Colony` when the player's own name contains a blacklisted word - a generic `Colony {id}` name (which can never match a word-based rule) is used instead, so a bad player name can't produce a permanently-stuck bad colony name.

Every time the mod has to correct a name this way, it logs a warning to the server console with the rejected name, the corrected name, and the exact rule from **blacklistedwords.json** that matched - e.g. `ColonyNameGuard: colony name 'ExampleBadWord' matched blacklistedwords.json rule 'f+u+c+k+', reset to 'Kleator's Colony'` - so staff can see what triggered it without having to guess which entry fired.

Each entry in **blacklistedwords.json** is a regex pattern (.NET regex syntax), matched case-insensitively against the colony name after it's been lowercased and stripped of accents and all non-alphanumeric characters (spaces, punctuation, symbols). A plain word (e.g. `"badword"`) is already a valid pattern matching that literal substring, so a simple word list works unchanged. Because spacing/punctuation is already stripped before matching, patterns generally don't need to account for it - `\b` word boundaries won't behave usefully since the normalized name has none left. Character classes are useful for leetspeak-style letter substitution, e.g. `"f+u+c+k+"` also catches repeated letters like "fuuuck", and `"[a4]+ss+h+[o0]+l+e+"` also catches "4ssh0le". blacklistedwords.example.json ships with a starter list (75 entries) covering common profanity/slurs in English, German, French, Spanish, Italian, Portuguese and Dutch - it's a starting point, not exhaustive, and worth extending for your community. An invalid regex entry is logged and skipped rather than breaking the whole list.

## Reloading Config

**/bcreload** reloads this mod's config files from disk without a server restart: `antigrief-config.json`, `chatcolors.json`, `travelpaths.json`, `blacklistedwords.json`, `jail-config.json` and `announcements.json`. Requires permission **mods.scarabol.commands.bcreload**.

It deliberately does *not* reload runtime/history data - `playeractivity.json`, `banned-players-log.json`, `jail-log.json`, `roleplay-banned.json` or war status - since those track live state rather than admin settings, and reloading them could discard in-memory progress (e.g. a player's playtime since the last autosave) or restart a background timer a second time (jail release checks, colony name scans, war status checks, announcement broadcasts all self-reschedule and are only started once, at world load).

## Teleport Requests

**/tpa {playername}** sends a teleport request to another player. Requires permission **mods.scarabol.commands.tpa.self**. The target has 60 seconds to respond with **/tpaccept** or **/tpadeny** (no permission required for either). On accept, both players are notified and the requester is teleported to the target after a 3 second countdown, so they're not teleported without warning.

## Writing Player Names

A lot of the commands require the name of a player as target. Names of players can sometimes be hard to type due to international character sets. For all commands the name can be specified in different ways:

**/command 'player name with blanks'** enclose the player name with single quotes. Always works, for any command.

**/command #12345678** use a 8 digit hash instead of the name. The hashes are printed out by the **/online id** command.

**/command Aldric Stormvale** for commands where the player name is the only (or last) argument - `/groups`, `/kick`, `/list`, `/lastseen`, `/killplayer`, `/killnpcs`, `/unmute`, `/tpa`, `/antigrief` - an unquoted multi-word name works without quotes too, since there's nothing after it the parser could confuse it with. The same applies to `/ban {name} {days} {reason}`, `/mute {name} {minutes}`, `/rpban {name} {duration} {reason}` and `/jail {name} {time} {reason}` **when a numeric time value is given** - it's used to unambiguously locate the end of the name. Without a time value, or for commands that take a player name *and* other free-form text with nothing to anchor on (`/warp`, `/trade`, `/whisper`), an unquoted multi-word name is still ambiguous and needs quotes.

**/command PlayerName1** splitscreen profiles show up with a name like `PlayerName 1` (the main account's name, a space, then the profile number). Player name matching ignores whitespace differences as a fallback, so typing the profile name as one word also resolves to the right profile, without being confused with the main account (an exact match, e.g. plain `PlayerName`, always wins first). This also means minor extra/missing spacing anywhere in a name is tolerated.

**/command &lt;steamid&gt;** if all of the above fails one can use the full steamid also

Use **/groups {playername}** to list the permission group(s) a player is explicitly assigned to. Requires permission **mods.scarabol.commands.groups**.

## Chat Formatting

**chatcolors.json** (see chatcolors.example.json) controls both the per-group chat coloring and the overall layout of every chat line, via a top-level **Format** string:

```json
{
	"Format": "{name}{rolename:top}{rp}> {message}",
	"Groups": {
		"god": { "prefix": "[God]", "prefixcolor": "red", "name": "yellow", "text": "#00ccff" }
	}
}
```

`Format` is recognized regardless of whether the `Groups` wrapper is present - if it's missing, every other top-level key is treated as a group automatically, so adding `Format` to an existing flat file (without moving the groups into `Groups`) also just works. Old-style files with neither key still load fine too and use the default `Format` shown above.

Placeholders available in **Format**:

- `{name}` - the player's name, colored by their highest-priority matching group.
- `{message}` - the chat text.
- `{rp}` - the `[RP]` roleplay marker, when the player has `/rp on`.
- `{rolename:top}` / `{rolename:bottom}` - the colored tag of the player's highest- or lowest-priority matching group (priority = declaration order in `Groups`), or nothing if none match. **`prefix` is used exactly as written, with no brackets added** - include your own, e.g. `"prefix": "[God]"` or `"prefix": "<b>[Owner]</b>"`, so nothing gets double-wrapped.
- `{rolename:<groupname>}` - that specific group's tag, only if the player currently matches it, else nothing.
- `{role:<groupname>;string:"literal text"}` - outputs the given literal text only if the player currently matches `<groupname>`, else nothing. Useful for building a custom sentence around a role, e.g. `{role:god;string:"This user is Owner: "}{rolename:god}`.

A role tag only shows up for a player who actually has the matching **`mods.scarabol.commands.betterchat.<groupname>`** permission granted (via your permission groups config) - adding an entry to `chatcolors.json`'s `Groups` only defines its look, it doesn't grant it to anyone. A player with none of the configured groups sees empty tag placeholders (just `{name}{rp}> {message}`).

## All Commands

*(none)* means no permission is required. Where a command has both a self and other-player variant, both permissions are listed.

### Anti-Grief & Moderation

| Command | Permission | Description |
|---|---|---|
| `/antigrief {grant\|revoke} {player}` | *(none)* | Grants or revokes a player's permission to build/dig in your banner area. |
| `/antigrief area {rangex rangez}` or `{rangexn rangexp rangezn rangezp}` | *(none)* | Sets a custom protection area independent of banners. |
| `/customarea` | *(none)* | Shows the protection area you're standing in, or the nearest one. |
| `/areashow` | `mods.scarabol.commands.areashow` | Toggles showing area job highlights from all players, not just your own. |
| `/graceperiod {minutes}` | `mods.scarabol.commands.antigrief.setgraceperiod` | Sets how long new joins are protected from grief punishment after server start. |
| `/colonycap [limit]` | *(none)* to view, `mods.scarabol.commands.colonycap` to change | Shows or sets the colonist-per-colony limit. |
| `/ban {player} {days} {reason}` | `mods.scarabol.commands.ban` | Bans a player - by SteamID or nickname, online or offline - and kicks them. |
| `/banlog` | `mods.scarabol.commands.ban` | Shows the ban history log. |
| `/kick {player}` | `mods.scarabol.commands.kick` | Kicks a player from the server. |
| `/mute {player} {minutes}` | `mods.scarabol.commands.mute` | Mutes a player's chat for a set duration. |
| `/unmute {player}` | `mods.scarabol.commands.mute` | Removes an active mute. |
| `/killplayer [player]` | `mods.scarabol.commands.killplayer` (`.self` to allow on yourself) | Kills the given player, or yourself if omitted. |
| `/killnpcs [player]` | `mods.scarabol.commands.killnpcs` (`.self` to allow on your own) | Kills all colonists (NPCs) belonging to the given player. |
| `/noflight {player}` | `mods.scarabol.commands.noflight` | Toggles disabling flight mode for a player. |
| `/purgebanner` | `mods.scarabol.purgebanner` | Removes the banner nearest to you; deletes the colony if it was the last one. |
| `/purgebanner colony` | `mods.scarabol.purgebanner` | Purges a whole colony at once. |
| `/purgebanner {player}` | `mods.scarabol.purgebanner` | Removes all banners/colony access for a player. |
| `/purgebanner all [range]` | `mods.scarabol.purgeallbanner` | Purges **all** banners within a range. Can be dangerous. |
| `/purgebanner days [age]` | `mods.scarabol.purgeallbanner` | Purges all banners of players inactive for at least `age` days. Can be dangerous. |
| `/inactive {days}` | `mods.scarabol.commands.inactive` | Lists players inactive for at least the given number of days. |
| `/god` | `mods.scarabol.commands.god` | Toggles your own super-permission, e.g. to try the regular player experience. |
| `/drain` | `mods.scarabol.commands.drain` | Drains a small lake/puddle, up to 5k blocks. |

### Colony & Banner

| Command | Permission | Description |
|---|---|---|
| `/bannername` | *(none)* | Shows the name/owner of the closest banner that isn't yours. |
| `/setcolonyname {new name}` | `mods.scarabol.commands.setcolonyname` | Renames the colony whose banner is closest to you, regardless of ownership. |
| `/list {player}` | `mods.scarabol.commands.listplayer` | Lists a player's colonies and last-seen time. |
| `/top [c\|colony\|p\|player] {score\|food\|colonists\|time\|item}` | *(none)* | Shows the top-10 leaderboard. `mods.scarabol.commands.hidefromtopcmd` hides a player/group from it. |
| `/mtop {score\|food\|colonists\|time\|item}` | *(none)* | Same as `/top`, but always includes your own rank even outside the top 10. |
| `/total {itemname}` | *(none)* | Shows the total amount of an item existing across the server. |

### Jail System

| Command | Permission | Description |
|---|---|---|
| `/setjail {x y z\|range}` | `mods.scarabol.commands.setjailposition` | Sets the jail location and size at your current position. |
| `/setjail visitor` | `mods.scarabol.commands.setjailposition` | Sets the jail visitor spot. |
| `/jail {player} [time] {reason}` | `mods.scarabol.commands.jail` | Sends a player to jail. |
| `/jailrelease {player}` | `mods.scarabol.commands.jail` | Releases a player from jail early. |
| `/jailrec [player]` | `mods.scarabol.commands.jail` | Shows jail history - everyone's (last 10), or one player's in full. |
| `/jailtime` | *(none)* | Shows your own remaining jail time. |
| `/jailvisit` (alias `/visitjail`) | *(none)* | Visits the jail as an observer. |
| `/jailleave` (alias `/leavejail`) | *(none)* | Returns from visiting the jail. |

### Chat & Roleplay

| Command | Permission | Description |
|---|---|---|
| `/rp on\|off` | *(none)* | Toggles your `[RP]` roleplay marker in chat. |
| `/rp` | *(none)* | Lists players currently roleplaying. |
| `/rpban {player} {duration}[m\|h\|d] {reason}` | `mods.scarabol.commands.roleplayban` | Bans a player from using `/rp`. |
| `/rpunban {player}` | `mods.scarabol.commands.roleplayban` | Lifts a roleplay ban. |
| `/whisper {player} {message}` (alias `/w`) | *(none)* | Sends a private message to a player. |
| `/bcreload` | `mods.scarabol.commands.bcreload` | Reloads this mod's config files live (see "Reloading Config" above). |

### Teleport & Travel

| Command | Permission | Description |
|---|---|---|
| `/tpa {player}` | `mods.scarabol.commands.tpa.self` | Sends a teleport request, expiring after 60 seconds. |
| `/tpaccept` | *(none)* | Accepts your pending teleport request; requester teleports to you after a 3s countdown. |
| `/tpadeny` | *(none)* | Denies your pending teleport request. |
| `/warp {player}` | `mods.scarabol.commands.warp.self` | Warps you to a player's position. |
| `/warp {player} {other}` | `mods.scarabol.commands.warp.player` | Warps a second player to the first player's position. |
| `/warpbanner [name]` | `mods.scarabol.commands.warp.banner.self` (own colonies) or `.warp.banner` (any colony) | Warps to your closest colony, or the one given by name. |
| `/warpspawn` | `mods.scarabol.commands.warp.spawn.self` | Warps you to the spawn point. |
| `/warpspawn {player}` | `mods.scarabol.commands.warp.spawn` | Warps another player to the spawn point. |
| `/warpplace {x} {y} {z}` | `mods.scarabol.commands.warp.place` | Warps you to a specific coordinate. |
| `/travel add\|remove ...` | `mods.scarabol.commands.travelpaths` | Manages fast-travel points players auto-warp through. |
| `/stuck` | *(none)* | Auto-warps you to spawn if you don't move for 1 minute. |

### Player Info & Utility

| Command | Permission | Description |
|---|---|---|
| `/help [filter]` | *(none)* | Lists the commands you have permission to use, optionally filtered by text. |
| `/groups {player}` | `mods.scarabol.commands.groups` | Lists a player's explicitly assigned permission group(s). |
| `/online [id]` | *(none)* | Lists online players; `id` adds an 8-digit hash usable in place of a name. |
| `/lastseen {player}` | *(none)* | Shows a player's last-seen time and total playtime. |
| `/serverpop` | *(none)* | Shows the current server population. |
| `/itemid` | *(none)* | Lists your inventory items and their item keys, for use with `/trade`/`/trash`. |
| `/trade {player} {item} {amount}` | `mods.scarabol.commands.trade` | Transfers items from your stockpile to another player's. |
| `/trash {item} {amount}` | *(none)* | Deletes items from your stockpile and inventory. |
| `/promote {group} {player}` | `permissions.setgroup.<group>` | Sets a player's permission group. |

### Admin & Server Tools

| Command | Permission | Description |
|---|---|---|
| `/spawnnpc {number}` | `mods.scarabol.commands.npcandbeds` | Spawns colonists at no food cost and no beds required (admin/test use). |
| `/beds {number}` | `mods.scarabol.commands.npcandbeds` | Places beds in a spiral around your banner (admin/test use). |
| `/announcements {add\|remove\|edit\|move\|enable\|disable\|interval} ...` | `mods.scarabol.commands.announcements.list` | Manages scheduled, rotating chat announcements. |
| `/war start [duration]` | *(none)*, requires an active colony with colonists | Enables consenting player/colony war mode (NPC PvP) for a duration. |
| `/war` | *(none)* | Lists players currently in war mode and their remaining time. |
| `/war end` | `mods.scarabol.commands.endwar` | Ends all currently active wars. |

## Installation

**This mod must be installed on the server!**

* download a compatible release ZIP or build from source code
* place the unzipped `BetterColonyCommands` folder inside your *Colony Survival/gamedata/mods/* directory
* the installed mod folder must contain `ColonyCommands.dll`, `modInfo.json`, `README.md`, `LICENSE`, `preview.png`, and the example JSON config files

## Building

See [BUILDING.md](BUILDING.md).

**Pull requests welcome!**
