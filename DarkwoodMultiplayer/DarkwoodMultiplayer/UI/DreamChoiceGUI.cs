using System;
using UnityEngine;

namespace DarkwoodMultiplayer
{
    internal class DreamChoiceGUI : MonoBehaviour
    {
        public enum DreamChoice { Spectate, Join }

        private static DreamChoiceGUI _instance;
        private Action<DreamChoice> _onChoice;
        private float _timeout = 15f;
        private bool _choiceMade;

        public static void Show(Action<DreamChoice> onChoice)
        {
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }

            var go = new GameObject("DreamChoiceGUI");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DreamChoiceGUI>();
            _instance._onChoice = onChoice;
        }

        public static void Hide()
        {
            if (_instance != null)
            {
                _instance._choiceMade = true;
                Destroy(_instance.gameObject);
                _instance = null;
            }
        }

        private void OnGUI()
        {
            if (_choiceMade) return;

            _timeout -= Time.deltaTime;
            if (_timeout <= 0)
            {
                MakeChoice(DreamChoice.Spectate);
                return;
            }

            float w = 400f;
            float h = 200f;
            float x = (Screen.width - w) / 2f;
            float y = (Screen.height - h) / 2f;

            GUI.Box(new Rect(x, y, w, h), "The other player entered a dream");

            GUI.Label(new Rect(x + 20f, y + 40f, w - 40f, 60f),
                "Would you like to join the dream or spectate?\n(Spectating: watch from outside while frozen)\n(Joining: teleport into the dream and move around)");

            if (GUI.Button(new Rect(x + 50f, y + 120f, 130f, 40f), "Spectate"))
                MakeChoice(DreamChoice.Spectate);

            if (GUI.Button(new Rect(x + 220f, y + 120f, 130f, 40f), "Join"))
                MakeChoice(DreamChoice.Join);
        }

        private void MakeChoice(DreamChoice choice)
        {
            _choiceMade = true;
            _onChoice?.Invoke(choice);
            Destroy(gameObject);
            _instance = null;
        }
    }
}
