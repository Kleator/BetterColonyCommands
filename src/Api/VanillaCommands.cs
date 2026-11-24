namespace ColonyCommands
{
	// The game's own chat commands (Chatting.Commands in Assembly-CSharp, game 0.18.0.x) with the
	// permissions the game really checks (taken from the decompiled server code). Commands the game
	// does not check at all get an auto-created "colonysurvival.cmd.<command>" permission - open to
	// everyone until an admin assigns it to a role, which then restricts it (enforced by BCC's interceptor).
	// "(cheats)" = the game additionally requires cheats to be enabled for the world.
	public static class VanillaCommands
	{
		public const string MOD = "Colony Survival";
		const string CAT = "Colony Survival (vanilla)";

		static void Add(string command, string description, string usage, string permission, params string[] parameters)
		{
			BccCommandParam[] prms = new BccCommandParam[parameters.Length];
			for (int i = 0; i < parameters.Length; i++) {
				string p = parameters[i];
				bool optional = p.EndsWith("?");
				prms[i] = new BccCommandParam(optional ? p.Substring(0, p.Length - 1) : p, "", optional);
			}
			CommandRegistry.Register(new BccCommandInfo {
				Command = command,
				Mod = MOD,
				Category = CAT,
				Description = description,
				Usage = usage,
				Permission = permission,
				Parameters = new System.Collections.Generic.List<BccCommandParam>(prms),
				Source = "vanilla"
			});
		}

		public static void RegisterAll()
		{
			// time & info
			Add("/time", "Shows the current game day and time.", "/time", "");
			Add("/time add", "Adds hours to the game time (cheats).", "/time add {hours}", "time.add", "hours");
			Add("/time day", "Skips to the next morning (cheats).", "/time day", "time.day");
			Add("/time night", "Skips to the night (cheats).", "/time night", "time.night");
			Add("/tps", "Shows the server's ticks per second.", "/tps", "");
			Add("/worldseed", "Shows the world seed.", "/worldseed", "");

			// server
			Add("/save", "Saves the world.", "/save", "server.save");
			Add("/backup", "Creates a backup of the world.", "/backup", "server.backup");
			Add("/stopserver", "Stops the server.", "/stopserver", "server.stop");
			Add("/disableachievements", "Disables achievements for this world.", "/disableachievements", "achievements.disable");

			// player cheats
			Add("/deus vult", "Kills all zombies attacking you or another player (cheats, disables achievements).", "/deus vult [player]", "deusvult.self", "player?");
			Add("/sethealth", "Sets your health (cheats).", "/sethealth {amount}", "health.set", "amount");
			Add("/loot", "Gives you (or a player) items (cheats).", "/loot {item} [amount] [player]", "loot.self", "item", "amount?", "player?");
			Add("/lootall", "Gives you one stack of every item (cheats).", "/lootall", "loot.all");
			Add("/setflight", "Enables or disables flight (cheats).", "/setflight {true|false}", "flight", "true/false");
			Add("/setspawn", "Sets the world spawn point to your position (cheats).", "/setspawn", "spawn.set");
			Add("/teleport", "Teleports you to your banner, coordinates, a colony, a player or an NPC (cheats).", "/teleport [banner | x y z | colony name | player name | npc id]", "teleport.self", "target?");
			Add("/teleportother", "Teleports another player to a banner, to you or to coordinates (cheats).", "/teleportother {player} [banner | here | x y z]", "teleport.other", "player", "target?");

			// colony
			Add("/colony addowner", "Adds a co-owner to the colony you are in.", "/colony addowner {player}", "", "player");
			Add("/colony removeowner", "Removes a co-owner from the colony you are in.", "/colony removeowner {player}", "", "player");
			Add("/colony setleader", "Makes another owner the leader of the colony you are in.", "/colony setleader {player}", "", "player");
			Add("/colony printhere", "Shows which colony the place you are standing at belongs to.", "/colony printhere", "");

			// permissions
			Add("/addpermission", "Gives a player a single permission.", "/addpermission {permission} {player}", "permissions.addpermission", "permission", "player");
			Add("/removepermission", "Removes a single permission from a player.", "/removepermission {permission} {player}", "permissions.removepermission", "permission", "player");
			Add("/addgroup", "Adds a role (permission group) to a player.", "/addgroup {group} {player}", "permissions.addgroup", "group", "player");
			Add("/setgroup", "Sets a player's role, replacing the others.", "/setgroup {group} {player}", "permissions.setgroup", "group", "player");
			Add("/removegroup", "Removes a role from a player.", "/removegroup {group} {player}", "permissions.removegroup", "group", "player");
			Add("/reloadpermission", "Reloads permissiongroups.json and permissionusers.json.", "/reloadpermission", "permissions.reload");

			// white/black list
			Add("/whitelist add", "Adds a player (name or SteamID) to the whitelist.", "/whitelist add {player}", "whitelist.add", "player");
			Add("/whitelist remove", "Removes a player from the whitelist.", "/whitelist remove {player}", "whitelist.remove", "player");
			Add("/whitelist clear", "Clears the whitelist.", "/whitelist clear", "whitelist.clear");
			Add("/blacklist add", "Adds a player (name or SteamID) to the blacklist.", "/blacklist add {player}", "blacklist.add", "player");
			Add("/blacklist remove", "Removes a player from the blacklist.", "/blacklist remove {player}", "blacklist.remove", "player");
			Add("/blacklist clear", "Clears the blacklist.", "/blacklist clear", "blacklist.clear");

			// debug (the game checks some of them not at all - those get an auto permission)
			Add("/debug wand1", "Debug: wand tool 1.", "/debug wand1", "debug.wand");
			Add("/debug wand2", "Debug: wand tool 2.", "/debug wand2", "debug.wand");
			Add("/debug copy", "Debug: copies the wand selection.", "/debug copy", "debug.wand");
			Add("/debug paste", "Debug: pastes the copied wand selection.", "/debug paste", "debug.wand");
			Add("/debug colonypoints", "Debug: adds colony points.", "/debug colonypoints {amount}", "debug.points", "amount");
			Add("/debug sanctifiedpoints", "Debug: adds sanctified points.", "/debug sanctifiedpoints {amount}", "debug.points", "amount");
			Add("/debug ownall", "Debug: makes you owner of all colonies.", "/debug ownall", "debug.ownall");
			Add("/debug lagspike", "Debug: causes a lag spike.", "/debug lagspike {ms}", "debug.lagspike", "ms");
			Add("/debug lagspikepersistent", "Debug: causes a lag spike every frame.", "/debug lagspikepersistent {ms}", "debug.lagspike", "ms");
			Add("/debug setsimspeed", "Debug: sets the simulation speed.", "/debug setsimspeed {factor}", "debug.simspeed", "factor");
			Add("/debug resetsimspeed", "Debug: resets the simulation speed.", "/debug resetsimspeed", "debug.simspeed");
			Add("/debug getsimspeed", "Debug: shows the simulation speed (not checked by the game).", "/debug getsimspeed", "");
			Add("/debug researchall", "Debug: completes all research.", "/debug researchall", "debug.researchall");
			Add("/debug resetresearch", "Debug: resets all research.", "/debug resetresearch", "debug.resetresearch");
			Add("/debug resetquests", "Debug: resets the quests.", "/debug resetquests", "debug.resetquests");
			Add("/debug completequests", "Debug: completes the quests.", "/debug completequests", "debug.completequests");
			Add("/debug clearnotifications", "Debug: clears your notifications (not checked by the game).", "/debug clearnotifications", "");
			Add("/debug clearinventory", "Debug: clears YOUR inventory (not checked by the game!).", "/debug clearinventory", "");
			Add("/debug clearstockpile", "Debug: clears the stockpile of your active colony (not checked by the game!).", "/debug clearstockpile", "");
			Add("/debug void", "Debug: void test (not checked by the game).", "/debug void", "");
			Add("/debug printblockhere", "Debug: prints the block at your position (not checked by the game).", "/debug printblockhere", "");
			Add("/debug printnpchere", "Debug: prints the NPC near you (not checked by the game).", "/debug printnpchere", "");
			Add("/debug removeblockhere", "Debug: removes the block at your position.", "/debug removeblockhere", "debug.blockedit");
			Add("/debug placeblockhere", "Debug: places a block at your position.", "/debug placeblockhere {block}", "debug.blockedit", "block");
			Add("/debug killnpcs", "Debug: kills NPCs of your colony.", "/debug killnpcs", "debug.killnpcs");
			Add("/debug killnpcsexcept", "Debug: kills NPCs of your colony except some.", "/debug killnpcsexcept", "debug.killexceptnpcs");
			Add("/debug killnpcsall", "Debug: kills all NPCs.", "/debug killnpcsall", "debug.killallnpcs");
			Add("/debug killnpcsallexcept", "Debug: kills all NPCs except some.", "/debug killnpcsallexcept", "debug.killallnpcsexcept");
			Add("/debug spawnbanner", "Debug: spawns a banner.", "/debug spawnbanner {player}", "debug.spawnbanner", "player");
			Add("/debug drain", "Debug: drains water around you.", "/debug drain {range}", "debug.drain", "range");
			Add("/debug simplewave", "Debug: spawns a simple zombie wave.", "/debug simplewave", "debug.simplewave");
			Add("/debug resetnetworkstats", "Debug: resets the network statistics.", "/debug resetnetworkstats", "debug.resetstats");
			Add("/debug resetspawn", "Debug: resets the spawn point.", "/debug resetspawn", "debug.resetspawn");
			Add("/debug generatecolony", "Debug: generates a test colony.", "/debug generatecolony", "debug.debugstructures");
			Add("/debug clearcube", "Debug: clears a cube around you.", "/debug clearcube {size}", "debug.debugstructures", "size");
			Add("/debug complexblocks", "Debug: places test blocks.", "/debug complexblocks", "debug.debugstructures");
			Add("/debug lightcheck", "Debug: light test structure.", "/debug lightcheck", "debug.debugstructures");
			Add("/debug meshcheck", "Debug: mesh test structure.", "/debug meshcheck", "debug.debugstructures");
			Add("/debug texturecheck", "Debug: texture test structure.", "/debug texturecheck", "debug.debugstructures");
			Add("/debug printspreadsheet", "Debug: writes statistics to the log.", "/debug printspreadsheet", "debug.logging");
			Add("/debug logcategories", "Debug: log categories.", "/debug logcategories", "debug.logging");
			Add("/debug printtypes", "Debug: writes all item types to the log.", "/debug printtypes", "debug.logging");
			Add("/debug printthreat", "Debug: writes threat values to the log.", "/debug printthreat", "debug.logging");
			Add("/debug printthreat2d", "Debug: writes a threat map to the log.", "/debug printthreat2d", "debug.logging");
			Add("/debug checkfiles", "Debug: checks the game files.", "/debug checkfiles", "debug.logging");
			Add("/debug checkblueprintable", "Debug: checks which blocks can be used in blueprints.", "/debug checkblueprintable", "debug.checkblueprintable");
		}
	}
}
