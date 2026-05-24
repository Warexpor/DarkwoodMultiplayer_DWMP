using BepInEx;

namespace DarkwoodMultiplayer
{
    /// <summary>
    /// Minimal BepInEx entry — must stay tiny so Unity can instantiate it.
    /// </summary>
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed class DarkwoodMultiplayerEntry : BaseUnityPlugin
    {
        private void Awake()
        {
            ModRuntime.Start(Logger, Config);
        }

        private void OnDestroy()
        {
            ModRuntime.Stop();
        }
    }
}
