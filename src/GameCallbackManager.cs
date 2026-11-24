using System.Collections.Generic;
using ModLoaderInterfaces;
using NetworkUI;
using NPC;
using Recipes;
using Pipliz;
using Shared;
using UnityEngine;

namespace ColonyCommands {

	public class GameCallbackManager:
		IAfterItemTypesDefined,
		IAfterWorldLoad,
		IAfterModsLoaded,
		IOnAssemblyLoaded,
		IOnPlayerClicked,
		IOnTryChangeBlock,
		IOnNPCHit,
		IOnPlayerConnectedEarly,
		IOnPlayerConnectedLate,
		IOnPlayerMoved2,
		IOnAutoSaveWorld,
		IOnPlayerDisconnected,
		IOnPlayerPushedNetworkUIButton,
		IOnQuit,
		IOnUpdate,
		IOnPlayerHit,
		IOnConstructInventoryManageColonyUI
	{
		public void OnPlayerHit(Players.Player player, ModLoader.OnHitData hit)
		{
			RtpChatCommand.OnPlayerHit(player, hit);
		}


		// "Manage Permission" right of "Manage Owners" in the Colony tab - after the game built the tab
		[ModLoader.ModCallback("bcc.trust.colonytab")]
		[ModLoader.ModCallbackDependsOn("pipliz.buildbase")]
		public void OnConstructInventoryManageColonyUI(Players.Player player, NetworkMenu menu, (NetworkUI.Items.Table left, NetworkUI.Items.Table right) tables)
		{
			try {
				UiTrustPages.AddColonyTabButton(player, tables.left);
			} catch (System.Exception e) {
				Log.WriteException("Better Colony Commands: could not add the Manage Permission button", e);
			}
		}

		public void AfterItemTypesDefined()
		{
			AntiGrief.AfterItemTypesDefined();
		}


		public void AfterWorldLoad()
		{
			Announcements.AfterWorldLoad();
			AntiGrief.AfterWorldLoad();
			JailManager.Load();
            TravelManager.Load();
			WarManager.Load();
            ChatColors.LoadChatColors();
            RoleplayManager.Load();
            BannedPlayerManager.Load();
			ActivityTracker.Load();
			ColonyNameGuard.Load();
			BccSettingsManager.Load();
			PermissionGroupsGuard.AfterWorldLoad();
			ModerationLog.Load();
			UiPreferences.Load();
			CommandRegistry.EnsureBuiltins();
			AntiGrief.EnsureCommandInterceptorFirst();
			EconomyManager.AfterWorldLoad();
			HomesManager.AfterWorldLoad();
			RoleTestManager.AfterWorldLoad();
			RoleIndex.CleanDuplicates();
			RoleIndex.AfterWorldLoad();
			EconomyHud.Initialize();
			ModInfoCollector.RegisterImages();
			UiManager.RegisterAssets();
			SelfTest.AfterWorldLoad();
			// other mods register their commands at various points of the startup - collect them once all are done
			ThreadManager.InvokeOnMainThread(delegate {
				AntiGrief.EnsureCommandInterceptorFirst();
				CommandRegistry.ForceRefresh();
				Log.Write($"Better Colony Commands: {CommandRegistry.All().Count} commands known ({CommandRegistry.All().FindAll(c => c.Mod != CommandRegistry.BCC_MOD).Count} from other mods)");
			}, 10.0);
		}

		public void OnUpdate()
		{
			EconomyManager.OnUpdate();
			RoleTestManager.OnUpdate();

		}


		public void AfterModsLoaded(List<ModLoader.ModDescription> mods)
		{
			AntiGrief.AfterModsLoaded(mods);
		}


		public void OnAssemblyLoaded(string path)
		{
			AntiGrief.OnAssemblyLoaded(path);
		}


		public void OnTryChangeBlock(ModLoader.OnTryChangeBlockData userData)
		{
			AntiGrief.OnTryChangeBlock(userData);
		}


		public void OnPlayerClicked(Players.Player player, PlayerClickedData click)
		{
			TransportCompatibility.OnPlayerClicked(player, click);
		}


		public void OnPlayerPushedNetworkUIButton(ButtonPressCallbackData data)
		{
			AntiGrief.OnPlayerPushedNetworkUIButton(data);
			UiManager.OnPlayerPushedNetworkUIButton(data);
			WarningPopup.OnPlayerPushedNetworkUIButton(data);
		}


		public void OnPlayerConnectedEarly(Players.Player player)
		{
			ActivityTracker.OnPlayerConnectedEarly(player);
		}


		public void OnPlayerConnectedLate(Players.Player player)
		{
			Announcements.OnPlayerConnectedLate(player);
			AntiGrief.OnPlayerConnectedLate(player);
			ActivityTracker.OnPlayerConnectedLate(player);
			JailManager.OnPlayerConnectedLate(player);
			EconomyManager.OnPlayerConnectedLate(player);
			WarningPopup.OnPlayerConnectedLate(player);
			RoleTestManager.OnPlayerConnectedLate(player);
			RoleIndex.CleanDuplicates(player);
			PermissionGroupsGuard.OnPlayerConnectedLate(player);
		}


		public void OnPlayerMoved2(Players.Player player, Vector3 oldStandingPosition, Vector3 oldCameraPosition)
		{
			JailManager.OnPlayerMoved(player, player.PositionStanding);
		}


		public void OnNPCHit(NPC.NPCBase npc, ModLoader.OnHitData data)
		{
			AntiGrief.OnNPCHit(npc, data);
		}


		public void OnAutoSaveWorld()
		{
			ActivityTracker.OnAutoSaveWorld();
			PermissionGroupsGuard.OnAutoSave();
			EconomyManager.OnAutoSaveOrQuit();
		}


		public void OnPlayerDisconnected(Players.Player player)
		{
			RoleTestManager.OnPlayerDisconnected(player);
			ActivityTracker.OnPlayerDisconnected(player);
			JailManager.OnPlayerDisconnected(player);
			UiManager.OnPlayerDisconnected(player);
			ProtectionNotices.OnPlayerDisconnected(player);
		}


		public void OnQuit()
		{
			RoleTestManager.OnQuit();
			ActivityTracker.OnQuit();
			EconomyManager.OnAutoSaveOrQuit();
		}

	}

}
