using System;
using System.Reflection;
using HarmonyLib;

namespace DarkwoodMultiplayer.Players
{
    /// <summary>
    /// Routes InputScript click delegates to whichever Player is currently active.
    /// </summary>
    public static class CoopInputBridge
    {
        private static bool _storedMainDelegates;
        private static clickDelegate _mainInstant;
        private static clickDelegate _mainShort;
        private static clickDelegate _mainLong;
        private static clickDelegate _mainContext;

        public static void EnsureMainDelegatesStored()
        {
            if (_storedMainDelegates)
                return;

            Player main = PlayerControlRouter.MainPlayer;
            InputScript input = Singleton<InputScript>.Instance;
            if (main == null || input == null)
                return;

            _mainInstant = input.onInstantClick;
            _mainShort = input.onShortClick;
            _mainLong = input.onLongClick;
            _mainContext = input.onContextMenu;
            _storedMainDelegates = true;
        }

        public static void BindToPlayer(Player player)
        {
            if (player == null)
                return;

            InputScript input = Singleton<InputScript>.Instance;
            if (input == null)
                return;

            clickDelegate playerInstant = CreateDelegate(player, "onInstantClick");
            clickDelegate playerShort = CreateDelegate(player, "onShortClick");
            clickDelegate playerLong = CreateDelegate(player, "onLongClick");
            clickDelegate playerContext = CreateDelegate(player, "onLongClick");

            input.onInstantClick = (clickDelegate)Delegate.Combine(playerInstant, input.onInstantClick);
            input.onShortClick = (clickDelegate)Delegate.Combine(playerShort, input.onShortClick);
            input.onLongClick = (clickDelegate)Delegate.Combine(playerLong, input.onLongClick);
            input.onContextMenu = (clickDelegate)Delegate.Combine(playerContext, input.onContextMenu);
        }

        public static void RestoreMainDelegates()
        {
            InputScript input = Singleton<InputScript>.Instance;
            if (input == null)
                return;

            if (_storedMainDelegates)
            {
                input.onInstantClick = _mainInstant;
                input.onShortClick = _mainShort;
                input.onLongClick = _mainLong;
                input.onContextMenu = _mainContext;
                return;
            }

            Player main = PlayerControlRouter.MainPlayer;
            if (main != null)
                BindToPlayer(main);
        }

        private static clickDelegate CreateDelegate(Player player, string methodName)
        {
            MethodInfo method = AccessTools.Method(typeof(Player), methodName);
            return (clickDelegate)Delegate.CreateDelegate(typeof(clickDelegate), player, method);
        }
    }
}
