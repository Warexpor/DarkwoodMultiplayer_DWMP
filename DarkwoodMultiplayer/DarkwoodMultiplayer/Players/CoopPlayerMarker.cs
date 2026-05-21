using UnityEngine;

namespace DarkwoodMultiplayer.Players
{
    /// <summary>
    /// Marks a cloned Player that must not replace Player.Instance on registerMe.
    /// </summary>
    public sealed class CoopPlayerMarker : MonoBehaviour
    {
        public bool IsLocalCoopSecond = true;
    }
}
