using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;
using ServerSync;
using System.Security.Cryptography;
using static ClutterSystem;
using UnityEngine.SocialPlatforms;

namespace BossPowerGuard.Patches;

static class Keys
{
    public static Dictionary<string, string> enemyToPlayer = new Dictionary<string, string>();
    public static Dictionary<string, string> playerToGlobal = new Dictionary<string, string>();
    public static HashSet<string> creatures = new HashSet<string>();
    public static Dictionary<string, string> guardianPower = new Dictionary<string, string>();

    public static void Awake()
    {
        enemyToPlayer["$enemy_eikthyr"] = "GP_Eikthyr";
        enemyToPlayer["$enemy_gdking"] = "GP_TheElder";
        enemyToPlayer["$enemy_bonemass"] = "GP_Bonemass";
        enemyToPlayer["$enemy_dragon"] = "GP_Moder";
        enemyToPlayer["$enemy_goblinking"] = "GP_Yagluth";
        enemyToPlayer["$enemy_seekerqueen"] = "GP_Queen";

        playerToGlobal["GP_Eikthyr"] = "defeated_eikthyr";
        playerToGlobal["GP_TheElder"] = "defeated_gdking";
        playerToGlobal["GP_Bonemass"] = "defeated_bonemass";
        playerToGlobal["GP_Moder"] = "defeated_dragon";
        playerToGlobal["GP_Yagluth"] = "defeated_goblinking";
        playerToGlobal["GP_Queen"] = "defeated_queen";

        creatures.Add("killedtroll");
        creatures.Add("killed_surtling");
        creatures.Add("killedbat");

        guardianPower["$se_eikthyr_name"] = "GP_Eikthyr";
        guardianPower["$se_theelder_name"] = "GP_TheElder";
        guardianPower["$se_bonemass_name"] = "GP_Bonemass";
        guardianPower["$se_moder_name"] = "GP_Moder";
        guardianPower["$se_yagluth_name"] = "GP_Yagluth";
        guardianPower["$se_queen_name"] = "GP_Queen";
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
public static class PlayerOnSpawned
{
    public static ManualLogSource Logger = new ManualLogSource($"{Plugin.GUID}.PlayerOnSpawned");

    private static List<string> bossesToClear = new List<string>();
    private static List<string> creaturesToClear = new List<string>();

    private static Dictionary<string, string> bossesToFix = new Dictionary<string, string>();

    public static void Postfix()
    {
        // Process keys
        HashSet<string> playerKeySet = new HashSet<string>();
        List<string> playerKeys = Player.m_localPlayer.GetUniqueKeys();
        playerKeys.ForEach(key => playerKeySet.Add(key));

        // Fix missing keys from legacy implementation
        foreach(var kv in Keys.playerToGlobal)
        {
            if (playerKeySet.Contains(kv.Key))
            {
                Player.m_localPlayer.AddUniqueKey(kv.Value);

                ref ZoneSystem zone = ref ZoneSystem.m_instance;
                if (!zone.GetGlobalKey(kv.Value))
                    zone.GlobalKeyAdd(kv.Value);
            }
        }

        if (Plugin.syncRemoveCreatureKeys.Value)
        {
            foreach (var key in Keys.creatures)
            {
                ref ZoneSystem zone = ref ZoneSystem.m_instance;
                if (zone.GetGlobalKey(key))
                {
                    zone.RemoveGlobalKey(key);
                    Logger.LogInfo($"Removed global creature key '{key}'");
                }
            }
        }
    }
}

[HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SendGlobalKeys), typeof(long))]
public static class ZoneSystemSendGlobalKeys
{
    public static ManualLogSource Logger = new ManualLogSource($"{Plugin.GUID}.SendGlobalKeys");

    static bool Prefix(ZoneSystem __instance, long peer)
    {
        List<string> _keysToRemove = new List<string>();

        // Fill up _keysToRemove with existing global keys found in keysToRemove.
        __instance.GetGlobalKeys().ForEach(key =>
        {
            if (Plugin.syncRemoveCreatureKeys.Value)
            {
                if (Keys.creatures.Contains(key))
                    _keysToRemove.Add(key);
            }
        });

        // Remove the keys we found
        _keysToRemove.ForEach(key =>
        {
            __instance.RemoveGlobalKey(key);
            Logger.LogInfo($"Removed global creature key '{key}'");
        });

        return true;
    }
}

// When ZNet is created
[HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
static class ZNetAwake
{
    public static ManualLogSource Logger = new ManualLogSource($"{Plugin.GUID}.ZNetAwake");
    public static bool isServer = false;

    static void Postfix(ZNet __instance)
    {
        Logger.LogDebug("Postfix called");
        isServer = __instance.IsServer();

        if (isServer)
        {
            ZRoutedRpc.instance.Register<string, string, int>("BossPowerGuard_RPC_OnDamaged", DeathHandler.BossPowerGuard_RPC_OnDamaged);
            Logger.LogInfo("Registered BossPowerGuard_RPC_OnDamaged RPC");

            ZRoutedRpc.instance.Register<string, int>("BossPowerGuard_RPC_Death", DeathHandler.BossPowerGuard_RPC_Death);
            Logger.LogInfo("Registered BossPowerGuard_RPC_Death RPC");
        }
    }
}

[HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection), typeof(ZNetPeer))]
static class ZNetOnNewConnection
{
    public static ManualLogSource Logger = new ManualLogSource($"{Plugin.GUID}.ZNetOnNewConnection");
    static void Postfix(ZNet __instance, ZNetPeer peer)
    {
        // Register peer RPCs
        peer.m_rpc.Register<string>("BossPowerGuard_Death", DeathHandler.BossPowerGuard_Peer_Death);
        Logger.LogInfo("Registered BossPowerGuard_Death RPC");
    }
}

static class DeathHandler
{
    public static ManualLogSource Logger = new ManualLogSource($"{Plugin.GUID}.DeathHandler");

    public static void BossPowerGuard_RPC_OnDamaged(long sender, string playerName, string bossName, int instanceID)
    {
        if (!Keys.enemyToPlayer.ContainsKey(bossName))
            return;

        Logger.LogDebug($"Received damage to '{bossName}' from '{playerName}'");
        if (!CharacterOnDamaged.bosses.ContainsKey(instanceID))
            CharacterOnDamaged.bosses[instanceID] = new HashSet<string>();

        if (!CharacterOnDamaged.bosses[instanceID].Contains(playerName))
        {
            Logger.LogInfo($"Adding '{playerName}' to hit list for '{bossName}'");
            CharacterOnDamaged.bosses[instanceID].Add(playerName);
        }
    }

    public static void BossPowerGuard_RPC_Death(long sender, string bossName, int instanceID)
    {
        Logger.LogInfo("BossPowerGuard_RPC_Death");

        // If the name of the Character that died wasn't a boss, no-op
        if (!Keys.enemyToPlayer.ContainsKey(bossName))
            return;

        HashSet<string> players = CharacterOnDamaged.bosses[instanceID];
        // Send data off to the clients
        foreach(ZNetPeer peer in ZNet.instance.GetPeers())
        {
            // If this peer is in the set of players who attacked the boss
            if (players.Contains(peer.m_playerName))
            {
                Logger.LogInfo($"Distributing '{bossName}' kill to '{peer.m_playerName}'");
                peer.m_rpc.Invoke("BossPowerGuard_Death", bossName);
            }
        }

        CharacterOnDamaged.bosses.Remove(instanceID);
        Logger.LogDebug($"Removed cache for {instanceID}");
    }

    public static void BossPowerGuard_Peer_Death(ZRpc rpc, string bossName)
    {
        Logger.LogDebug("BossPowerGuard_Peer_Death");
        BossPowerGuard_Death(0, bossName);
    }

    public static void BossPowerGuard_Death(long sender, string bossName)
    {
        HandleDeath(bossName);
    }

    public static void HandleDeath(string bossName)
    {
        Logger.LogDebug("HandleDeath");

        Logger.LogInfo($"Handling boss kill for '{bossName}'");
        if (!Keys.enemyToPlayer.ContainsKey(bossName))
            return;

        string playerKey = Keys.enemyToPlayer[bossName];
        if (!Player.m_localPlayer.GetUniqueKeys().Contains(playerKey))
        {
            string defeatedKey = Keys.playerToGlobal[playerKey];

            Player.m_localPlayer.AddUniqueKey(playerKey);
            Logger.LogInfo($"Added unique player key '{playerKey}'");

            Player.m_localPlayer.AddUniqueKey(defeatedKey);
            Logger.LogInfo($"Added unique player key '{defeatedKey}'");
        }
    }

    public static void Handle(Character __instance)
    {
        Logger.LogDebug("Handling OnDeath callback");

        // Invoke routed RPC onDeath
        string bossName = __instance.m_name;
        int instanceID = __instance.GetInstanceID();
        Logger.LogDebug($"Instance ID: {instanceID}");

        if (!Keys.enemyToPlayer.ContainsKey(__instance.m_name))
            return;

        ZRoutedRpc.instance.InvokeRoutedRPC("BossPowerGuard_RPC_Death", bossName, instanceID);

        // Process local onDeath
        HandleDeath(bossName);
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
static class CharacterOnDeath
{
    static void Postfix(Character __instance)
    {
        DeathHandler.Handle(__instance);
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
static class CharacterOnDamaged
{
    public static ManualLogSource Logger = new ManualLogSource($"{Plugin.GUID}.CharactedOnDamaged");
    public static Dictionary<int, HashSet<string>> bosses = new Dictionary<int, HashSet<string>>();

    static void Postfix(Character __instance, long sender, HitData hit)
    {
        Logger.LogDebug("Postfix");

        if (!Keys.enemyToPlayer.ContainsKey(__instance.m_name))
            return;
        
        Character attacker = __instance.m_lastHit.GetAttacker();
        if (attacker.IsPlayer())
        {
            int instanceID = __instance.GetInstanceID();
            Player player = attacker as Player;
            string playerName = player.GetPlayerName();

            ZRoutedRpc.instance.InvokeRoutedRPC(
                "BossPowerGuard_RPC_OnDamaged",
                playerName,
                __instance.m_name,
                instanceID
            );
        }
    }
}

[HarmonyPatch(typeof(ItemStand), nameof(ItemStand.Interact), typeof(Humanoid), typeof(bool), typeof(bool))]
public static class ItemStandInteract
{
    public static ManualLogSource Logger = new ManualLogSource($"{Plugin.GUID}.ItemStandInteract");

    private static Dictionary<string, string> display = new Dictionary<string, string>();

    public static void Awake()
    {
        display["GP_Eikthyr"] = "Eikthyr";
        display["GP_TheElder"] = "the Elder";
        display["GP_Bonemass"] = "Bonemass";
        display["GP_Moder"] = "Moder";
        display["GP_Yagluth"] = "Yagluth";
        display["GP_Queen"] = "the Queen";
    }

    public static bool Prefix(ItemStand __instance, Humanoid user, bool hold, bool alt)
    {
        if (__instance.m_guardianPower != null)
        {
            // If it's not a guardian item stand, return true.
            string powerName = __instance.m_guardianPower.m_name;
            if (!Keys.guardianPower.ContainsKey(powerName))
                return true;

            string playerKey = Keys.guardianPower[powerName];
            List<string> playerKeys = Player.m_localPlayer.GetUniqueKeys();
            bool isPermissed = !Plugin.syncPowerRequiresBoss.Value || playerKeys.Contains(playerKey);
            if (!isPermissed)
            {
                string displayName = display[playerKey];
                string message = $"You have to kill {displayName} before using its power";
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, message);
            }

            return isPermissed;
        }

        return true;
    }
}