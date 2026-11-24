using System;
using System.Collections.Generic;
using Chatting;
using Pipliz;
using UnityEngine;

namespace ColonyCommands
{
	// /rtp - random teleport to a safe spot between MinDistance and MaxDistance blocks around the
	// player's current position (bcc-settings.json -> Rtp). Cooldown per player; permission
	// mods.scarabol.commands.rtp.nocooldown bypasses it.
	public class RtpChatCommand : IChatCommand
	{
		public const string PERMISSION_NOCOOLDOWN = AntiGrief.MOD_PREFIX + "rtp.nocooldown";
		static readonly Dictionary<string, DateTime> lastUse = new Dictionary<string, DateTime>();
		static readonly System.Random random = new System.Random();

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/rtp", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			BccSettings.RtpSettings cfg = BccSettingsManager.Settings.Rtp;
			if (!cfg.Enabled) {
				BccChat.Send(causedBy, Style.Error("/rtp is disabled on this server."));
				return true;
			}
			if (JailManager.IsPlayerJailed(causedBy)) {
				BccChat.Send(causedBy, Style.Error("You can't use /rtp while in jail."));
				return true;
			}
			string key = causedBy.ID.ToStringReadable();
			bool bypass = PermissionsManager.HasPermission(causedBy, PERMISSION_NOCOOLDOWN);
			if (!bypass && lastUse.TryGetValue(key, out DateTime last)) {
				double left = cfg.CooldownSeconds - (DateTime.UtcNow - last).TotalSeconds;
				if (left > 0) {
					BccChat.Send(causedBy, Style.Error($"/rtp is on cooldown - try again in {TextHelper.FormatDuration(System.Math.Ceiling(left / 60.0) * 60.0)} ({(int)System.Math.Ceiling(left)}s)."));
					return true;
				}
			}

			Pipliz.Vector3Int origin = causedBy.PositionVoxelStanding;
			for (int attempt = 0; attempt < cfg.MaxAttempts; attempt++) {
				double angle = random.NextDouble() * System.Math.PI * 2.0;
				// uniform over the ring area, not biased to the center
				double minSq = (double)cfg.MinDistance * cfg.MinDistance;
				double maxSq = (double)cfg.MaxDistance * cfg.MaxDistance;
				double dist = System.Math.Sqrt(minSq + random.NextDouble() * (maxSq - minSq));
				int x = origin.x + (int)System.Math.Round(System.Math.Cos(angle) * dist);
				int z = origin.z + (int)System.Math.Round(System.Math.Sin(angle) * dist);
				int y;
				bool exact = TryFindSafeY(x, z, origin.y, out y, out bool loaded);
				if (!exact) {
					// chunk not loaded on the server (far from players/colonies): use the terrain
					// generator's height - players land on top and fall the last blocks
					if (loaded || !TryTerrainHeight(x, z, out y)) {
						continue;
					}
				}
				Pipliz.Vector3Int target = new Pipliz.Vector3Int(x, y, z);
				if (!IsAllowedArea(causedBy, target, cfg)) {
					continue;
				}
				if (!bypass) {
					lastUse[key] = DateTime.UtcNow;
				}
				// parachute: no fall damage until the first landing (max. 30 s)
				parachute[causedBy] = DateTime.UtcNow.AddSeconds(30);
				Helper.TeleportPlayer(causedBy, new Vector3(x + 0.5f, y + 0.1f, z + 0.5f));
				int distance = (int)System.Math.Round(System.Math.Sqrt(System.Math.Pow(x - origin.x, 2) + System.Math.Pow(z - origin.z, 2)));
				BccChat.Send(causedBy, Style.Success($"Whoosh! Teleported {distance} blocks away to {x} {y} {z}."));
				if (!exact) {
					// the generator height is only an estimate: once the chunk is loaded around the
					// player, put them exactly on the ground (max. 2 blocks above, never inside)
					CorrectHeightLater(causedBy, x, z, y, 0);
				}
				return true;
			}
			BccChat.Send(causedBy, Style.Error("No safe spot found nearby (unloaded or protected terrain) - please try again."));
			return true;
		}

		static readonly Dictionary<Players.Player, DateTime> parachute = new Dictionary<Players.Player, DateTime>();

		// called for every hit a player takes: cancels fall damage while the parachute is active
		public static void OnPlayerHit(Players.Player player, ModLoader.OnHitData hit)
		{
			if (player == null || hit == null || hit.HitSourceType != ModLoader.OnHitData.EHitSourceType.FallDamage) {
				return;
			}
			if (parachute.TryGetValue(player, out DateTime until)) {
				parachute.Remove(player);
				if (DateTime.UtcNow <= until) {
					hit.ResultDamage = 0f;
				}
			}
		}

		static void CorrectHeightLater(Players.Player player, int x, int z, int estimatedY, int tries)
		{
			ThreadManager.InvokeOnMainThread(delegate {
				if (player == null || player.ConnectionState != Players.EConnectionState.Connected) {
					return;
				}
				// stop if the player moved away on their own
				Vector3 pos = player.PositionStanding;
				if (System.Math.Abs(pos.x - (x + 0.5f)) > 6f || System.Math.Abs(pos.z - (z + 0.5f)) > 6f) {
					return;
				}
				if (TryFindSafeY(x, z, estimatedY, out int groundY, out bool loaded)) {
					if (pos.y > groundY + 2.5f || pos.y < groundY - 0.5f) {
						parachute[player] = DateTime.UtcNow.AddSeconds(10);
						Helper.TeleportPlayer(player, new Vector3(x + 0.5f, groundY + 0.1f, z + 0.5f));
					}
					return;
				}
				if (tries < 30) {
					CorrectHeightLater(player, x, z, estimatedY, tries + 1);
				}
			}, 0.5);
		}

		// generator height of the untouched terrain; rejects water (at or below the water level)
		static bool TryTerrainHeight(int x, int z, out int y)
		{
			y = 0;
			try {
				if (ServerManager.TerrainGenerator == null) {
					return false;
				}
				ServerManager.TerrainGenerator.GetLocationData(new Pipliz.Vector3Int(x, 0, z), out float height, out float _, out float _, out ushort _, out ushort _);
				int water = 0;
				if (ServerManager.TerrainGenerator is TerrainGeneration2.TerrainGenerator2 tg) {
					water = tg.Settings.TerrainWaterLevel;
				}
				if (height <= water + 1) {
					return false;
				}
				y = (int)System.Math.Ceiling(height) + 2;
				return true;
			} catch {
				return false;
			}
		}

		// Highest standable block with two free (air-like, non-water) blocks above it in loaded
		// chunks. loaded = false if no block of the column is loaded on the server.
		static bool TryFindSafeY(int x, int z, int startY, out int y, out bool loaded)
		{
			y = 0;
			loaded = false;
			int top = startY + 160;
			int bottom = System.Math.Max(startY - 160, 1);
			ushort water = BlockTypes.BuiltinBlocks.Indices.water;
			bool aboveFree1 = false, aboveFree2 = false;
			
			for (int cy = top; cy >= bottom; cy--) {
				if (!World.TryGetTypeAt(new Pipliz.Vector3Int(x, cy, z), out ItemTypes.ItemType type)) {
					aboveFree1 = aboveFree2 = false;
					continue;
				}
				loaded = true;
				
				if (type == null) {
					aboveFree2 = aboveFree1;
					aboveFree1 = true;
					continue;
				}
				if (type.ItemIndex == water) {
					return false; // lake or sea - don't land in water
				}
				bool airLike = type.ItemIndex == 0 || type.PathingImpactAsAir;
				if (airLike) {
					aboveFree2 = aboveFree1;
					aboveFree1 = true;
					continue;
				}
				if (type.PathingImpactCanStandOn && aboveFree1 && aboveFree2) {
					y = cy + 1;
					return true;
				}
				aboveFree1 = aboveFree2 = false;
			}
			return false;
		}

		static bool IsAllowedArea(Players.Player player, Pipliz.Vector3Int pos, BccSettings.RtpSettings cfg)
		{
			ModConfig ag = AntiGrief.config;
			if (cfg.AvoidProtectedAreas && ag != null) {
				Pipliz.Vector3Int spawn = ServerManager.GetSpawnPoint().Position;
				if (System.Math.Abs(pos.x - spawn.x) <= ag.SpawnProtectionRange && System.Math.Abs(pos.z - spawn.z) <= ag.SpawnProtectionRange) {
					return false;
				}
				foreach (CustomProtectionArea area in ag.CustomAreas) {
					if (area.Contains(pos)) {
						return false;
					}
				}
			}
			if (cfg.AvoidForeignColonies && ag != null) {
				int range = ag.BannerProtectionRange;
				var groups = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
				while (groups.MoveNext()) {
					ColonyGroup group = groups.Current;
					if (group == null || group.Owners.IndexOf(player) >= 0) {
						continue;
					}
					for (int i = 0; i < group.Colonies.Count; i++) {
						Colony c = group.Colonies.GetAt(i);
						for (int j = 0; j < c.Banners.Count; j++) {
							Pipliz.Vector3Int b = c.Banners.GetAt(j).Position;
							if (System.Math.Abs(pos.x - b.x) <= range && System.Math.Abs(pos.z - b.z) <= range) {
								return false;
							}
						}
					}
				}
			}
			return true;
		}
	}
}
