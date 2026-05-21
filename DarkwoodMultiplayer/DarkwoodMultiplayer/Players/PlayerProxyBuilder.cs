using System;
using BepInEx.Logging;
using UnityEngine;

namespace DarkwoodMultiplayer.Players
{
    public enum PlayerCloneKind
    {
        Remote,
        LocalSecond
    }

    public static class PlayerProxyBuilder
    {
        public static bool IsSpawningCoopClone { get; private set; }

        public static GameObject CreatePlayerClone(
            Player sourcePlayer,
            string objectName,
            Vector3 positionOffset,
            PlayerCloneKind kind,
            ManualLogSource log)
        {
            if (sourcePlayer == null)
            {
                log?.LogWarning("Cannot spawn player clone: source Player is null.");
                return null;
            }

            GameObject original = sourcePlayer.gameObject;
            if (!original.activeInHierarchy)
            {
                log?.LogWarning("Cannot spawn player clone: source Player is inactive.");
                return null;
            }

            GameObject clone;
            IsSpawningCoopClone = kind == PlayerCloneKind.LocalSecond;

            // Create inactive dummy parent so Awake() doesn't fire on cloned components
            // (PlayerRagdoll.Awake calls EnforceFullRagdoll which doesn't exist in this build)
            GameObject dummyParent = new GameObject("DummyParent");
            dummyParent.SetActive(false);
            try
            {
                clone = UnityEngine.Object.Instantiate(original, dummyParent.transform);
            }
            catch (Exception ex)
            {
                log?.LogError("Instantiate player clone failed: " + ex);
                UnityEngine.Object.DestroyImmediate(dummyParent);
                return null;
            }
            finally
            {
                IsSpawningCoopClone = false;
            }

            if (clone == null)
            {
                log?.LogError("Instantiate returned null for player clone.");
                UnityEngine.Object.DestroyImmediate(dummyParent);
                return null;
            }

            // Strip ragdoll component before anything awakens
            // (PlayerRagdoll.Awake calls EnforceFullRagdoll which doesn't exist in this build)
            Component ragdoll = clone.GetComponent("PlayerRagdoll");
            if (ragdoll != null)
            {
                UnityEngine.Object.DestroyImmediate(ragdoll);
                log?.LogInfo("Player clone: destroyed PlayerRagdoll to prevent EnforceFullRagdoll crash.");
            }

            // Reparent to root and activate
            clone.transform.SetParent(null, false);
            clone.transform.position = original.transform.position + positionOffset;
            clone.SetActive(true);
            UnityEngine.Object.DestroyImmediate(dummyParent);

            if (kind == PlayerCloneKind.LocalSecond)
            {
                if (clone.GetComponent<CoopPlayerMarker>() == null)
                    clone.AddComponent<CoopPlayerMarker>();
            }

            clone.name = objectName;
            clone.SetActive(true);

            StripGameplayComponents(clone, kind);
            CleanupVision(clone, kind);
            RemoveUnityLights(clone);
            EnsureVisible(clone);

            if (kind == PlayerCloneKind.Remote)
            {
                if (clone.GetComponent<SecondPlayerAnimController>() == null)
                    clone.AddComponent<SecondPlayerAnimController>();
            }
            else if (kind == PlayerCloneKind.LocalSecond)
            {
                SecondPlayerAnimController remoteAnim = clone.GetComponent<SecondPlayerAnimController>();
                if (remoteAnim != null)
                    UnityEngine.Object.Destroy(remoteAnim);
            }

            if (kind == PlayerCloneKind.LocalSecond)
                PrepareLocalCoopPlayer(clone, sourcePlayer, log);

            log?.LogInfo(
                "Spawned player clone: "
                + objectName
                + " at "
                + clone.transform.position
                + " ("
                + kind
                + ")");
            return clone;
        }

        private static void PrepareLocalCoopPlayer(GameObject clone, Player template, ManualLogSource log)
        {
            Player player = clone.GetComponent<Player>();
            if (player == null)
            {
                log?.LogError("Local co-op clone is missing Player component after spawn.");
                return;
            }

            CoopPlayerBootstrap.RunLightweightStart(player, template);

            LocalPlayerBodyController bodyController = clone.GetComponent<LocalPlayerBodyController>();
            if (bodyController == null)
                bodyController = clone.AddComponent<LocalPlayerBodyController>();

            if (bodyController != null && template != null)
                bodyController.SetupFromMain(template);

            PlayerControlRouter.RegisterSecond(player);
            player.immobilised = true;
            log?.LogInfo("Local co-op second player ready (full Player mechanics).");
        }

        public static void StripGameplayComponents(GameObject clone, PlayerCloneKind kind)
        {
            if (kind == PlayerCloneKind.Remote)
            {
                Player player = clone.GetComponent<Player>();
                if (player != null)
                    UnityEngine.Object.Destroy(player);

                PlayerSkills skills = clone.GetComponent<PlayerSkills>();
                if (skills != null)
                    UnityEngine.Object.Destroy(skills);

                SkillsMenu menu = clone.GetComponent<SkillsMenu>();
                if (menu != null)
                    UnityEngine.Object.Destroy(menu);
            }

            if (kind == PlayerCloneKind.Remote)
            {
                foreach (Collider col in clone.GetComponentsInChildren<Collider>(true))
                    col.enabled = false;

                Rigidbody rb = clone.GetComponent<Rigidbody>();
                if (rb != null)
                    UnityEngine.Object.Destroy(rb);

                Rigidbody2D rb2d = clone.GetComponent<Rigidbody2D>();
                if (rb2d != null)
                    UnityEngine.Object.Destroy(rb2d);
            }
        }

        public static void CleanupVision(GameObject root, PlayerCloneKind kind)
        {
            if (kind == PlayerCloneKind.Remote)
            {
                DestroyComponentByName(root, "PlayerVision");
                DestroyComponentByName(root, "VisionCone");
                DestroyComponentByName(root, "PlayerLight");
                DestroyComponentByName(root, "Flashlight");

                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    string name = child.name.ToLowerInvariant();
                    if (!name.Contains("vision")
                        && !name.Contains("light")
                        && !name.Contains("cone")
                        && !name.Contains("flash")
                        && !name.Contains("fov"))
                        continue;

                    child.gameObject.SetActive(false);
                }
            }

            PlayerVisionController.From(root)?.SetAllVisionDisabled();
        }

        public static void RemoveUnityLights(GameObject root)
        {
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
                UnityEngine.Object.Destroy(light);
        }

        private static void DestroyComponentByName(GameObject root, string typeName)
        {
            Component component = root.GetComponent(typeName);
            if (component != null)
                UnityEngine.Object.Destroy(component);

            foreach (Component child in root.GetComponentsInChildren<Component>(true))
            {
                if (child != null && child.GetType().Name == typeName)
                    UnityEngine.Object.Destroy(child);
            }
        }

        private static void EnsureVisible(GameObject clone)
        {
            foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = true;

            if (clone.transform.localScale.x < 0.95f)
                clone.transform.localScale = Vector3.one;
        }
    }
}
