using System.Collections.Generic;

namespace ColonyCommands
{
	// Descriptions, usage, parameters and permissions of every Better Colony Commands command.
	// "" as permission = the command has no permission of its own and gets an auto-created
	// "bcc.cmd.<name>" permission that is open to everyone until an admin assigns it somewhere.
	public static class BuiltinCommands
	{
		const string P = AntiGrief.MOD_PREFIX;

		static BccCommandParam Arg(string name, string description, bool optional = false)
		{
			return new BccCommandParam(name, description, optional);
		}

		static void Add(string command, string category, string description, string usage, string permission, BccCommandParam[] parameters = null, string[] aliases = null, string[] extraPermissions = null, bool uiExecutable = true)
		{
			CommandRegistry.Register(new BccCommandInfo {
				Command = command,
				Mod = CommandRegistry.BCC_MOD,
				Category = category,
				Description = description,
				Usage = usage,
				Permission = permission,
				Parameters = parameters != null ? new List<BccCommandParam>(parameters) : new List<BccCommandParam>(),
				Aliases = aliases != null ? new List<string>(aliases) : new List<string>(),
				ExtraPermissions = extraPermissions != null ? new List<string>(extraPermissions) : new List<string>(),
				Source = "builtin",
				UiExecutable = uiExecutable
			});
		}

		public static void RegisterAll()
		{
			const string MOD = "Moderation";
			const string COL = "Colony & Banner";
			const string JAIL = "Jail";
			const string CHAT = "Chat & Roleplay";
			const string TP = "Teleport & Travel";
			const string INFO = "Info & Utility";
			const string ADMIN = "Admin & Server";
			const string ECO = "Economy";

			// ---- Info & Utility
			Add("/help", INFO, "Shows all commands you may use. Opens the command window; '/help chat' lists them in chat.", "/help [filter|page|chat]", "",
				new[] { Arg("filter", "Text to filter commands by, or a page number", true) });
			if (RoleTimeData.Available) {
				Add("/roletime", INFO, "Shows your online time, the next role you will reach and the time left, plus all roles still ahead of you (time based roles of Pytonia Automation).", "/roletime", "");
			}
			Add("/achievements", INFO, "Shows whether Steam achievements are on for you, your colonies and this world (cheats switch them off). Admins: '/achievements enable {player|all}' switches them back on.", "/achievements [enable] [player|all]", "",
				new[] { Arg("enable", "'enable' to switch them back on (admins)", true), Arg("player", "Player, or 'all' (empty = yourself)", true) },
				extraPermissions: new[] { P + "achievements.enable" });
			Add("/roles", INFO, "Lists all roles (permission groups) with their chat formatting.", "/roles", "");
			Add("/roletest", ADMIN, "Tests a role for 2 minutes: you get only this role, then your own roles come back automatically (also after a disconnect or a server stop). '/roletest end' ends it early - anyone in a test may use that.", "/roletest [role|end]", P + "config",
				new[] { Arg("role", "Role to test, or 'end' to end your test", true) });
			Add("/modlist", INFO, "Lists all installed mods with version and description. Opens the mod window.", "/modlist [page|modname]", "",
				new[] { Arg("mod", "Mod name for details, or a page number", true) });
			Add("/bcui", INFO, "Choose which commands may show their output in a window: '/bcui exclude <command>', '/bcui include <command>', '/bcui list', '/bcui reset'.", "/bcui {exclude|include|list|reset} [command]", "",
				new[] { Arg("action", "exclude, include, list or reset"), Arg("command", "e.g. /top", true) });
			Add("/bcsettings", INFO, "Opens your personal Better Colony Commands settings window.", "/bcsettings", "");
			Add("/groups", INFO, "Lists a player's explicitly assigned permission group(s).", "/groups {player}", P + "groups",
				new[] { Arg("player", "Player name") });
			Add("/online", INFO, "Lists online players; 'id' adds an 8-digit hash usable in place of a name.", "/online [id]", "",
				new[] { Arg("id", "Type 'id' to show player hashes", true) });
			Add("/lastseen", INFO, "Shows a player's last-seen time and total playtime.", "/lastseen {player}", "",
				new[] { Arg("player", "Player name") });
			Add("/serverpop", INFO, "Shows the current server population.", "/serverpop", "");
			Add("/itemid", INFO, "Lists your inventory items and their item keys, for use with /trade and /trash.", "/itemid", "");
			Add("/trade", INFO, "Transfers items from your stockpile to another player's.", "/trade {player} {item} {amount}", P + "trade",
				new[] { Arg("player", "Receiving player"), Arg("item", "Item key (see /itemid)"), Arg("amount", "Amount") });
			Add("/trash", INFO, "Deletes items from your stockpile and inventory.", "/trash {item} {amount}", "",
				new[] { Arg("item", "Item key (see /itemid)"), Arg("amount", "Amount") });
			Add("/top", INFO, "Shows the top-10 leaderboard of colonies or players.", "/top [c|colony|p|player] {score|food|colonists|time|item}", "",
				new[] { Arg("type", "c/colony or p/player", true), Arg("category", "score, food, colonists, time or an item name") },
				extraPermissions: new[] { P + "hidefromtopcmd" });
			Add("/mtop", INFO, "Like /top, but always includes your own rank.", "/mtop {score|food|colonists|time|item}", "",
				new[] { Arg("category", "score, food, colonists, time or an item name") });
			Add("/total", INFO, "Shows the total amount of an item existing across the server.", "/total {itemname}", "",
				new[] { Arg("item", "Item name") });
			Add("/list", INFO, "Lists a player's colonies and last-seen time.", "/list {player}", P + "listplayer",
				new[] { Arg("player", "Player name") });

			// ---- Teleport & Travel
			Add("/rtp", TP, "Random teleport to a spot around your current position (server-defined range and cooldown).", "/rtp", "",
				extraPermissions: new[] { P + "rtp.nocooldown" });
			Add("/tpa", TP, "Sends a teleport request to another player (expires after 60 seconds).", "/tpa {player}", P + "tpa.self",
				new[] { Arg("player", "Player to teleport to") });
			Add("/tpaccept", TP, "Accepts your pending teleport request.", "/tpaccept", "");
			Add("/tpadeny", TP, "Denies your pending teleport request.", "/tpadeny", "");
			Add("/warp", TP, "Warps you to a player, or a second player to the first one.", "/warp {player} [other player]", P + "warp.self",
				new[] { Arg("player", "Target player"), Arg("other", "Player to move instead of you", true) },
				extraPermissions: new[] { P + "warp.player" });
			Add("/warpbanner", TP, "Warps you to your closest colony, or to a colony by its displayed name (formatting and symbols are ignored).", "/warpbanner [colony name]", P + "warp.banner.self",
				new[] { Arg("colony", "Colony/banner name, e.g. Highrise-City3", true) },
				extraPermissions: new[] { P + "warp.banner" });
			Add("/warpspawn", TP, "Warps you (or another player) to the spawn point.", "/warpspawn [player]", P + "warp.spawn.self",
				new[] { Arg("player", "Player to warp", true) }, new[] { "/spawn" }, new[] { P + "warp.spawn" });
			Add("/warpplace", TP, "Warps you to specific coordinates.", "/warpplace {x} [y] {z}", P + "warp.place",
				new[] { Arg("x", "X coordinate"), Arg("y", "Y coordinate (optional)", true), Arg("z", "Z coordinate") });
			Add("/travel", TP, "Manages fast-travel points players auto-warp through.", "/travel {add|remove} ...", P + "travelpaths",
				new[] { Arg("arguments", "add / remove ...") });
			Add("/stuck", TP, "Warps you to spawn if you don't move for 1 minute.", "/stuck", "");
			// home points are a Better Colony Commands feature
			{
				Add("/sethome", TP, "Sets your current position as a home point (default name 'home'). The number of homes depends on your role.", "/sethome [name]", "",
					new[] { Arg("name", "Name of the home", true) });
				Add("/home", TP, "Teleports you to one of your home points.", "/home [name]", "",
					new[] { Arg("name", "Name of the home", true) });
				Add("/delhome", TP, "Removes one of your home points.", "/delhome [name]", "",
					new[] { Arg("name", "Name of the home", true) });
			}
			Add("/trust", COL, "Choose who may build on your claims (colony banner areas): opens 'Manage Permission' for your active colony - also the button next to 'Manage Owners' in the Colony tab. '/trust {player}' shows that player with all your claims; '/trust add|remove {player}' and '/trust list' work in chat.", "/trust [player|add|remove|list] [player]", "",
				new[] { Arg("player", "Empty = window of your active colony; a player name = that player with all your claims; or add/remove/list", true), Arg("name", "Player for add/remove", true) });
			Add("/homes", TP, "Lists your home points; opens the 'My homes' window (teleport, set, delete).", "/homes", "");

			// ---- Colony & Banner
			Add("/antigrief", COL, "Grants/revokes building rights in your banner area, or sets a custom protection area.", "/antigrief {grant|revoke} {player} | /antigrief area {x z}", "",
				new[] { Arg("arguments", "e.g. grant PlayerName") });
			Add("/customarea", COL, "Shows the protection area you're standing in, or the nearest one.", "/customarea", "");
			Add("/bannername", COL, "Shows name and owner of the closest banner that isn't yours.", "/bannername", "");
			Add("/colonycap", COL, "Shows (or with permission sets) the colonist-per-colony limit.", "/colonycap [limit]", "",
				new[] { Arg("limit", "New limit (admins only)", true) }, extraPermissions: new[] { P + "colonycap" });
			Add("/setcolonyname", COL, "Renames the colony whose banner is closest to you.", "/setcolonyname {new name}", P + "setcolonyname",
				new[] { Arg("name", "New colony name") });
			Add("/war", COL, "Shows active wars, starts one ('/war start 2h') or ends all ('/war end', staff).", "/war [start <duration>|end]", "",
				new[] { Arg("arguments", "start 2h / end", true) }, extraPermissions: new[] { P + "endwar" });

			// ---- Jail
			Add("/jail", JAIL, "Sends a player to jail.", "/jail {player} [minutes] {reason}", P + "jail",
				new[] { Arg("player", "Player"), Arg("minutes", "Jail time", true), Arg("reason", "Reason") });
			Add("/jailrelease", JAIL, "Releases a player from jail early.", "/jailrelease {player}", P + "jail",
				new[] { Arg("player", "Player") });
			Add("/jailrec", JAIL, "Shows the jail history (everyone's last 10, or one player's).", "/jailrec [player]", P + "jail",
				new[] { Arg("player", "Player", true) });
			Add("/jailtime", JAIL, "Shows your remaining jail time.", "/jailtime", "");
			Add("/jailvisit", JAIL, "Visits the jail as an observer.", "/jailvisit", "", aliases: new[] { "/visitjail" });
			Add("/jailleave", JAIL, "Returns from visiting the jail.", "/jailleave", "", aliases: new[] { "/leavejail" });
			Add("/setjail", JAIL, "Sets the jail position/size at your position, or the visitor spot.", "/setjail {x y z|range|visitor}", P + "setjailposition",
				new[] { Arg("arguments", "range, 'x y z' or visitor") });

			// ---- Chat & Roleplay
			Add("/rp", CHAT, "Toggles your [RP] marker ('/rp on|off') or lists roleplaying players.", "/rp [on|off]", "",
				new[] { Arg("state", "on or off", true) });
			Add("/rpban", CHAT, "Bans a player from using /rp.", "/rpban {player} {duration}[m|h|d] {reason}", P + "roleplayban",
				new[] { Arg("player", "Player"), Arg("duration", "e.g. 2h"), Arg("reason", "Reason") });
			Add("/rpunban", CHAT, "Lifts a roleplay ban.", "/rpunban {player}", P + "roleplayban",
				new[] { Arg("player", "Player") });
			Add("/whisper", CHAT, "Sends a private message to a player.", "/whisper {player} {message}", "",
				new[] { Arg("player", "Player"), Arg("message", "Message") }, new[] { "/w" });
			Add("/mute", CHAT, "Mutes a player's chat for a set duration.", "/mute {player} {minutes}", P + "mute",
				new[] { Arg("player", "Player"), Arg("minutes", "Minutes") });
			Add("/unmute", CHAT, "Removes an active mute.", "/unmute {player}", P + "mute",
				new[] { Arg("player", "Player") });

			// ---- Moderation
			Add("/ban", MOD, "Bans a player (by name or SteamID, online or offline) and kicks them.", "/ban {player} {days} {reason}", P + "ban",
				new[] { Arg("player", "Player or SteamID"), Arg("days", "Days"), Arg("reason", "Reason") });
			Add("/banlog", MOD, "Shows the ban history log.", "/banlog", P + "ban");
			Add("/kick", MOD, "Kicks a player from the server.", "/kick {player}", P + "kick",
				new[] { Arg("player", "Player") });
			Add("/killplayer", MOD, "Kills the given player, or yourself.", "/killplayer [player]", P + "killplayer.self",
				new[] { Arg("player", "Player", true) }, extraPermissions: new[] { P + "killplayer" });
			Add("/killnpcs", MOD, "Kills all colonists of the given player (or your own).", "/killnpcs [player]", P + "killnpcs.self",
				new[] { Arg("player", "Player", true) }, extraPermissions: new[] { P + "killnpcs" });
			Add("/noflight", MOD, "Toggles disabling flight mode for a player.", "/noflight {player}", P + "noflight",
				new[] { Arg("player", "Player") });
			Add("/purgebanner", MOD, "Removes the nearest banner, a whole colony, a player's banners, or all banners in range / of inactive players.", "/purgebanner [colony|player|all <range>|days <age>]", P + "purgebanner",
				new[] { Arg("arguments", "colony / player name / all 50 / days 30", true) }, extraPermissions: new[] { P + "purgeallbanner" });
			Add("/inactive", MOD, "Lists players inactive for at least the given number of days.", "/inactive {days}", P + "inactive",
				new[] { Arg("days", "Days") });
			Add("/graceperiod", MOD, "Sets how long new joins are protected after server start.", "/graceperiod {minutes}", P + "antigrief.setgraceperiod",
				new[] { Arg("minutes", "Minutes") });
			Add("/god", MOD, "Toggles your own super-permission.", "/god", P + "god");
			Add("/drain", MOD, "Drains a small lake/puddle (up to 5k blocks).", "/drain", P + "drain");
			Add("/promote", MOD, "Sets a player's permission group.", "/promote {group} {player}", "permissions.setgroup",
				new[] { Arg("group", "Group/role name"), Arg("player", "Player") });

			// ---- Admin & Server
			Add("/permissions", ADMIN, "Shows every permission that exists in the game, which mod it belongs to, who has it and which ones are unused.", "/permissions [filter|page]", BccPermissions.PERMISSION_OVERVIEW,
				new[] { Arg("filter", "Text filter or page number", true) });
			Add("/players", MOD, "Opens player moderation: every player (last online first) with ban/jail/mute status and incidents; edit money, roles, permissions, colonies, homes, online time and see the moderation history.", "/players [search]", UiPlayerPages.PERMISSION,
				new[] { Arg("search", "Player name or SteamID", true) });
			Add("/bcconfig", ADMIN, "Opens the admin configuration window for all Better Colony Commands settings.", "/bcconfig", BccPermissions.PERMISSION_CONFIG);
			Add("/audio", ADMIN, "Audio sources: place music or sounds (Ogg Vorbis files in the world's bcc-audio folder) in the world - range, volume, loop with crossfade, fades, triggers (in range, enter, click, join, interval, daybreak, nightfall, time of day, manual), channels. Opens the audio window; '/audio show' shows the sources as markers.", AudioChatCommand.USAGE, BccAudio.PERMISSION,
				new[] { Arg("action", "list, tracks, add, remove, move, tp, play, stop, on, off, set, info, show, near, edit", true), Arg("arguments", "e.g. add lobby spawn-music", true) });
			Add("/bcreload", ADMIN, "Reloads this mod's config files from disk.", "/bcreload", P + "bcreload");
			Add("/announcements", ADMIN, "Manages scheduled, rotating chat announcements.", "/announcements {add|remove|edit|move|enable|disable|interval} ...", P + "announcements.list",
				new[] { Arg("arguments", "e.g. add Welcome!", true) });
			Add("/spawnnpc", ADMIN, "Spawns colonists without food cost or beds (admin/test use).", "/spawnnpc {number} [player]", P + "npcandbeds",
				new[] { Arg("number", "Amount"), Arg("player", "Target player", true) });
			Add("/beds", ADMIN, "Places beds in a spiral around your banner (admin/test use).", "/beds {number}", P + "beds",
				new[] { Arg("number", "Amount") });
			Add("/colortest", ADMIN, "Shows a text in the given color.", "/colortest {color}", P + "mute",
				new[] { Arg("color", "e.g. #FF8800 or red") });

			// ---- Economy
			// with Pytonia Automation installed it answers /money, /addmoney, /removemoney and /setmoney
			// itself (and lists them with its own descriptions) - don't list them twice
			bool pytonia = PytoniaEconomyBridge.IsInstalled;
			if (!pytonia) Add("/money", ECO, "Shows your balance (or another player's).", "/money [player]", "",
				new[] { Arg("player", "Player", true) });
			Add("/pay", ECO, "Sends money to another player.", "/pay {player} {amount}", "",
				new[] { Arg("player", "Receiving player"), Arg("amount", "Amount") });
			Add("/baltop", ECO, "Shows the richest players.", "/baltop [page]", "",
				new[] { Arg("page", "Page", true) });
			Add("/exchange", ECO, "Exchanges Trader Coins (THE TRADER) from your stockpile into money or back.", "/exchange {tomoney|tocoins} {amount}", "",
				new[] { Arg("direction", "tomoney or tocoins"), Arg("amount", "Amount") });
			if (!pytonia) Add("/addmoney", ECO, "Adds money to a player's balance.", "/addmoney {player} {amount}", EconomyManager.PERMISSION_ADMIN,
				new[] { Arg("player", "Player"), Arg("amount", "Amount") });
			if (!pytonia) Add("/removemoney", ECO, "Removes money from a player's balance.", "/removemoney {player} {amount}", EconomyManager.PERMISSION_ADMIN,
				new[] { Arg("player", "Player"), Arg("amount", "Amount") });
			if (!pytonia) Add("/setmoney", ECO, "Sets a player's balance.", "/setmoney {player} {amount}", EconomyManager.PERMISSION_ADMIN,
				new[] { Arg("player", "Player"), Arg("amount", "Amount") });

			BccPermissions.Register(EconomyManager.PERMISSION_ADMIN, CommandRegistry.BCC_MOD, "Economy admin: /addmoney, /removemoney, /setmoney");

			// ---- other permissions used by BCC that are not tied to one command
			BccPermissions.Register(AntiGrief.PERMISSION_SUPER, CommandRegistry.BCC_MOD, "Staff: may build everywhere, bypasses all anti-grief protection");
			BccPermissions.Register(AntiGrief.PERMISSION_SPAWN_CHANGE, CommandRegistry.BCC_MOD, "May change blocks in the spawn and custom protection areas");
			BccPermissions.Register(AntiGrief.PERMISSION_BANNER_PREFIX, CommandRegistry.BCC_MOD, "Prefix: .colony.<id> or .<steamid> grants building rights in a foreign banner area (/antigrief grant)");
			BccPermissions.Register(P + ".placewater", CommandRegistry.BCC_MOD, "May place water");
			BccPermissions.Register(P + "betterchat.", CommandRegistry.BCC_MOD, "Prefix: betterchat.<group> applies that chatcolors.json group to the player");
			BccPermissions.Register("colonistcapacity.tier", CommandRegistry.BCC_MOD, "Prefix: colonistcapacity.tier<N> raises the colonist limit to ColonistTierLimits[N-1]");
			BccPermissions.Register(P + "announcements.add", CommandRegistry.BCC_MOD, "/announcements add");
			BccPermissions.Register(P + "announcements.remove", CommandRegistry.BCC_MOD, "/announcements remove");
			BccPermissions.Register(P + "announcements.edit", CommandRegistry.BCC_MOD, "/announcements edit");
			BccPermissions.Register(P + "announcements.move", CommandRegistry.BCC_MOD, "/announcements move");
			BccPermissions.Register(P + "announcements.enable", CommandRegistry.BCC_MOD, "/announcements enable");
			BccPermissions.Register(P + "announcements.disable", CommandRegistry.BCC_MOD, "/announcements disable");
			BccPermissions.Register(P + "announcements.interval", CommandRegistry.BCC_MOD, "/announcements interval");
			BccPermissions.Register(P + "announcements.welcome", CommandRegistry.BCC_MOD, "Edit the welcome announcement");
		}
	}
}
