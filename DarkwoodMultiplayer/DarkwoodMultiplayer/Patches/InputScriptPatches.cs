using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Hooks the game's input loop so our menu runs in-game (BepInEx plugin objects
    /// may not receive Update/OnGUI reliably in this Unity build).
    /// </summary>
    [HarmonyPatch(typeof(InputScript), "Update")]
    public static class InputScriptUpdatePatch
    {
        private static void Postfix()
        {
            ModRuntime.EnsureRunning();
            if (Input.GetKeyDown(KeyCode.F2))
                MultiplayerMenu.ToggleVisible();
            ModRuntime.LocalSecondPlayer?.Tick();
        }
    }

    [HarmonyPatch(typeof(InputScript), "Awake")]
    public static class InputScriptAwakePatch
    {
        private static void Postfix(InputScript __instance)
        {
            ModRuntime.EnsureRunning();
            ModRuntime.Log?.LogInfo("Hooked into InputScript — multiplayer menu active in-game.");
        }
    }


}
