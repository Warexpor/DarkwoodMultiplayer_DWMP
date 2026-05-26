using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMultiplayer.Networking
{
    [Serializable]
    public class ClientStateBackupData
    {
        public string Timestamp;
        public int Day;
        public int GameTimeMinutes;
        public float PosX, PosY, PosZ;
        public float Health, Stamina;
        public int Experience, CurrentLevel;
        public int HealthUpgrades, StaminaUpgrades, HotbarUpgrades, InventoryUpgrades;
        public int Lives;
        public float Saturation;
        public bool FedToday;
        public int LastTimeAte;
        public List<string> Recipes;
        public List<SkillEntry> Skills;
        public List<string> AvailableSkillNames;
        public List<ItemEntry> InventoryItems;
        public List<ItemEntry> HotbarItems;
    }

    [Serializable]
    public class ItemEntry
    {
        public int Slot;
        public string Type;
        public float Durability;
        public int Amount;
        public bool IsRecipe;
        public string RecipeFor;
    }

    [Serializable]
    public class SkillEntry
    {
        public string Name;
        public int TimesUsed;
    }

    public static class ClientStateBackup
    {
        public static ClientStateBackupData CollectBackupData()
        {
            var data = new ClientStateBackupData();
            Player player = Player.Instance;
            if (player == null) return data;

            data.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            Vector3 pos = player.transform.position;
            data.PosX = pos.x; data.PosY = pos.y; data.PosZ = pos.z;

            data.Health = player.health;
            data.Stamina = player.stamina;
            data.Experience = player.experience;
            data.CurrentLevel = player.currentLevel;
            data.HealthUpgrades = player.healthUpgrades;
            data.StaminaUpgrades = player.staminaUpgrades;
            data.HotbarUpgrades = player.hotbarUpgrades;
            data.InventoryUpgrades = player.inventoryUpgrades;
            data.Lives = player.lifes;
            data.Saturation = player.saturation;
            data.FedToday = player.fedToday;
            data.LastTimeAte = player.lastTimeAte;

            if (player.recipes != null)
            {
                data.Recipes = new List<string>();
                for (int i = 0; i < player.recipes.Count; i++)
                {
                    if (player.recipes[i] != null)
                    {
                        InvItem comp = player.recipes[i].GetComponent<InvItem>();
                        if (comp != null && !string.IsNullOrEmpty(comp.type) && !data.Recipes.Contains(comp.type))
                            data.Recipes.Add(comp.type);
                    }
                }
            }

            if (player.skills != null)
            {
                if (player.skills.skills != null)
                {
                    data.Skills = new List<SkillEntry>();
                    for (int i = 0; i < player.skills.skills.Count; i++)
                    {
                        var sk = player.skills.skills[i];
                        if (sk != null)
                            data.Skills.Add(new SkillEntry { Name = sk.name ?? sk.gameObject.name, TimesUsed = sk.timesUsed });
                    }
                }
                if (player.skills.availableSkills != null)
                {
                    data.AvailableSkillNames = new List<string>();
                    for (int i = 0; i < player.skills.availableSkills.Count; i++)
                    {
                        var sk = player.skills.availableSkills[i];
                        if (sk != null)
                            data.AvailableSkillNames.Add(sk.name ?? sk.gameObject.name);
                    }
                }
            }

            if (player.Inventory?.slots != null)
            {
                data.InventoryItems = new List<ItemEntry>();
                for (int i = 0; i < player.Inventory.slots.Count; i++)
                {
                    var slot = player.Inventory.slots[i];
                    if (slot != null && !InvItemClass.isNull(slot.invItem))
                        data.InventoryItems.Add(MakeItemEntry(slot.invItem, i));
                }
            }

            if (player.Hotbar?.slots != null)
            {
                data.HotbarItems = new List<ItemEntry>();
                for (int i = 0; i < player.Hotbar.slots.Count; i++)
                {
                    var slot = player.Hotbar.slots[i];
                    if (slot != null && !InvItemClass.isNull(slot.invItem))
                        data.HotbarItems.Add(MakeItemEntry(slot.invItem, i));
                }
            }

            var controller = Singleton<Controller>.Instance;
            if (controller != null)
            {
                data.Day = controller.day;
                data.GameTimeMinutes = controller.CurrentTime;
            }

            return data;
        }

        private static ItemEntry MakeItemEntry(InvItemClass item, int slot)
        {
            return new ItemEntry
            {
                Slot = slot,
                Type = item.type,
                Durability = item.durability,
                Amount = item.amount,
                IsRecipe = item.isRecipe,
                RecipeFor = item.recipeFor
            };
        }

        public static string SerializeToJson(ClientStateBackupData data)
        {
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }

        public static ClientStateBackupData DeserializeFromJson(string json)
        {
            return JsonConvert.DeserializeObject<ClientStateBackupData>(json);
        }

        public static string GetBackupFilePath()
        {
            string saveDir = Application.persistentDataPath + "/1_4Save";
            string profileName = "prof" + (Core.currentProfile?.id ?? 1);
            string dir = saveDir + "/" + profileName;
            try { Directory.CreateDirectory(dir); } catch { }
            return dir + "/client_backup.json";
        }

        public static void SaveBackupFile(string json)
        {
            try
            {
                string path = GetBackupFilePath();
                File.WriteAllText(path, json);
                ModRuntime.Log?.LogInfo("[ClientBackup] saved to " + path);
            }
            catch (Exception ex)
            {
                ModRuntime.Log?.LogError("[ClientBackup] failed to save: " + ex.Message);
            }
        }
    }
}
