using BepInEx;
using HarmonyLib;
using System.Reflection;
using BepInEx.Logging;
using BepInEx.Configuration;
using BossPowerGuard.Patches;
using ServerSync;

namespace BossPowerGuard
{
    [BepInPlugin(Plugin.GUID, Plugin.NAME, Plugin.VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string NAME = "BossPowerGuard";
        public const string AUTHOR = "Kevver";
        public const string GUID = $"{AUTHOR}.{NAME}";
        public const string VERSION = "1.0.5";

        private readonly Harmony harmony = new(NAME);

        public static ServerSync.ConfigSync configSync = new ServerSync.ConfigSync(GUID)
        {
            DisplayName = NAME,
            CurrentVersion = VERSION,
            MinimumRequiredVersion = VERSION
        };

        public static ConfigEntry<bool> serverConfigLocked;
        public static SyncedConfigEntry<bool> syncServerConfigLocked;

        public static ConfigEntry<bool> powerRequiresBoss;
        public static SyncedConfigEntry<bool> syncPowerRequiresBoss;

        public static ConfigEntry<bool> removeBossKeys;
        public static SyncedConfigEntry<bool> syncRemoveBossKeys;

        public static ConfigEntry<bool> removeCreatureKeys;
        public static SyncedConfigEntry<bool> syncRemoveCreatureKeys;

        private void Awake()
        {
            string desc = "Lock the server configuration (Synced with server)";
            serverConfigLocked = Config.Bind("General", "serverConfigLocked", true, desc);
            syncServerConfigLocked = configSync.AddLockingConfigEntry<bool>(serverConfigLocked);
            syncServerConfigLocked.SynchronizedConfig = true;

            desc = "Boss power requires boss kill (Synced with server)";
            powerRequiresBoss = Config.Bind("General", "powerRequiresBoss", true, desc);
            syncPowerRequiresBoss = configSync.AddConfigEntry<bool>(powerRequiresBoss);
            syncPowerRequiresBoss.SynchronizedConfig = true;

            desc = "Remove global creature keys on kill (Synced with server)";
            removeCreatureKeys = Config.Bind("General", "removeCreatureKeys", false, desc);
            syncRemoveCreatureKeys = configSync.AddConfigEntry<bool>(removeCreatureKeys);
            syncRemoveCreatureKeys.SynchronizedConfig = true;

            BepInEx.Logging.Logger.Sources.Add(ZoneSystemSendGlobalKeys.Logger);
            BepInEx.Logging.Logger.Sources.Add(ZNetAwake.Logger);
            BepInEx.Logging.Logger.Sources.Add(ZNetOnNewConnection.Logger);
            BepInEx.Logging.Logger.Sources.Add(CharacterOnDamaged.Logger);
            BepInEx.Logging.Logger.Sources.Add(DeathHandler.Logger);
            BepInEx.Logging.Logger.Sources.Add(PlayerOnSpawned.Logger);
            BepInEx.Logging.Logger.Sources.Add(ItemStandInteract.Logger);

            // Initialize keys
            Keys.Awake();
            ItemStandInteract.Awake();

            Assembly assembly = Assembly.GetExecutingAssembly();
            harmony.PatchAll(assembly);
        }
    }
}
