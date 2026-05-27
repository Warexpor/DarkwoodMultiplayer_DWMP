using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    internal static class CompressorSyncHelpers
    {
        internal static bool IsAddingToStash { get; set; }

        internal static void AddToWorkbenchStash()
        {
            if (IsAddingToStash) return;
            IsAddingToStash = true;
            try
            {
                Workbench wb = FindWorkbench();
                if (wb == null || wb.normalInventory == null)
                {
                    ModRuntime.Log?.LogInfo("[CompressorSync] no Workbench found to stash oxygen tank");
                    return;
                }
                var existing = wb.normalInventory.getAllItems("oxygentank_empty");
                if (existing != null && existing.Count > 0)
                {
                    bool found = false;
                    foreach (var i in existing)
                    {
                        if (!InvItemClass.isNull(i)) { found = true; break; }
                    }
                    if (found)
                    {
                        ModRuntime.Log?.LogInfo("[CompressorSync] oxygentank_empty already in stash");
                        return;
                    }
                }
                wb.normalInventory.addItemType("oxygentank_empty", 1);
                ModRuntime.Log?.LogInfo("[CompressorSync] stashed oxygentank_empty in Workbench");
            }
            finally
            {
                IsAddingToStash = false;
            }
        }

        private static Workbench FindWorkbench()
        {
            Workbench[] all = Resources.FindObjectsOfTypeAll<Workbench>();
            foreach (Workbench wb in all)
            {
                if (wb == null) continue;
                if (wb.normalInventory != null && wb.gameObject.scene.isLoaded)
                    return wb;
            }
            return null;
        }

        internal static void SendOxygenStashMessage()
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || !net.IsConnected) return;
            net.Send(NetMessageType.OxygenTankStash, w => new OxygenTankStashMessage().Serialize(w), DeliveryMethod.ReliableOrdered);
        }

        internal static bool IsCompressorGameEvents(GameEvents ge)
        {
            if (ge == null) return false;
            Transform t = ge.transform;
            for (int i = 0; i < 3; i++)
            {
                if (t == null) break;
                string name = t.name.ToLowerInvariant();
                if (name.Contains("compressor")) return true;
                t = t.parent;
            }
            return false;
        }

        internal static void ConvertRemoteTank()
        {
            if (Player.Instance == null) return;
            Inventory inv = Player.Instance.Inventory;
            if (inv == null) return;

            var emptyTanks = inv.getAllItems("oxygentank_empty");
            InvItemClass target = null;
            if (emptyTanks != null)
            {
                foreach (var item in emptyTanks)
                {
                    if (!InvItemClass.isNull(item)) { target = item; break; }
                }
            }
            if (target == null)
            {
                ModRuntime.Log?.LogInfo("[CompressorSync] remote player has no oxygentank_empty to convert");
                return;
            }
            target.removeAmount(1);
            inv.addItemType("oxygentank_full", 1);
            ModRuntime.Log?.LogInfo("[CompressorSync] converted oxygentank_empty -> oxygentank_full on remote player");
        }
    }

    [HarmonyPatch(typeof(InvSlot), "createItem", new[] { typeof(InvItemClass), typeof(int) })]
    internal static class OxygenTankAcquirePatch
    {
        private static void Postfix(InvSlot __instance, InvItemClass _invItem)
        {
            if (ModRuntime.Network == null) return;
            if (Core.loadingGame) return;
            if (LanNetworkManager.IsApplyingRemoteState) return;
            if (CompressorSyncHelpers.IsAddingToStash) return;
            if (_invItem == null || _invItem.type != "oxygentank_empty") return;

            ModRuntime.Log?.LogInfo("[CompressorSync] oxygentank_empty entering inventory, stashing copy");
            CompressorSyncHelpers.AddToWorkbenchStash();
            CompressorSyncHelpers.SendOxygenStashMessage();
        }
    }

    public static class OxygenTankStashHandler
    {
        public static void Handle()
        {
            if (ModRuntime.Network == null) return;
            CompressorSyncHelpers.AddToWorkbenchStash();
        }
    }

    [HarmonyPatch(typeof(GameEvents), "fire")]
    internal static class CompressorConvertDetectPatch
    {
        private static void Postfix(GameEvents __instance)
        {
            if (ModRuntime.Network == null) return;
            var net = (LanNetworkManager)ModRuntime.Network;
            if (LanNetworkManager.IsApplyingRemoteState) return;
            if (net.Role != NetworkRole.Host) return;
            if (!CompressorSyncHelpers.IsCompressorGameEvents(__instance)) return;

            ModRuntime.Log?.LogInfo("[CompressorSync] compressor GameEvents fired on host, sending convert message");
            net.Send(NetMessageType.CompressorTankConvert, w => new CompressorTankConvertMessage().Serialize(w), DeliveryMethod.ReliableOrdered);
        }
    }

    public static class CompressorTankConvertHandler
    {
        public static void Handle()
        {
            if (ModRuntime.Network == null) return;
            if (LanNetworkManager.IsApplyingRemoteState) return;
            CompressorSyncHelpers.ConvertRemoteTank();
        }
    }
}
