// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.Database;
using Game.DataStorage;
using System.Collections.Generic;

namespace Game.Entities
{
    public static class ItemEnchantmentManager
    {
        public static void LoadRandomEnchantmentsTable()
        {
            RelativeTime oldMSTime = Time.NowRelative;

            RandomEnchantmentData.Clear();
            RandomBonusListData.Clear();

            // 1.60.1 world schema (reference ItemEnchantmentMgr::LoadItemRandomBonusListTemplates):
            // `item_random_enchantment_template` no longer exists, random items use `item_random_bonus_list_template`.
            // RandomEnchantmentData (RandomProperties/RandomSuffix) stays empty; GenerateRandomProperties then logs and returns none.
            //                                         0   1            2
            SQLResult result = DB.World.Query("SELECT Id, BonusListID, Chance FROM item_random_bonus_list_template");

            if (result.IsEmpty())
            {
                Log.outInfo(LogFilter.ServerLoading,
                    "Loaded 0 Random item bonus list definitions. " +
                    "DB table `item_random_bonus_list_template` is empty.");
                return;
            }
            uint count = 0;

            do
            {
                int id = (int)result.Read<uint>(0);
                int bonusListId = (int)result.Read<uint>(1);
                float chance = result.Read<float>(2);

                if (chance < 0.000001f || chance > 100.0f)
                {
                    Log.outError(LogFilter.Sql,
                        $"Bonus list {bonusListId} used in `item_random_bonus_list_template` " +
                        $"by id {id} has invalid chance {chance}");
                    continue;
                }

                if (!RandomBonusListData.ContainsKey(id))
                    RandomBonusListData[id] = new();

                RandomBonusListData[id].Add((bonusListId, chance));

                ++count;
            } while (result.NextRow());

            Log.outInfo(LogFilter.ServerLoading,
                $"Loaded {count} Random item bonus list definitions in {Time.Diff(oldMSTime)} ms.");
        }

        /// <summary>Reference GenerateItemRandomBonusListId: picks a weighted bonus list for a RandomBonusListTemplateId.</summary>
        public static int GenerateItemRandomBonusListId(int randomBonusListTemplateId)
        {
            if (randomBonusListTemplateId == 0)
                return 0;

            var tab = RandomBonusListData.LookupByKey(randomBonusListTemplateId);
            if (tab == null)
            {
                Log.outError(LogFilter.Sql,
                    $"Item RandomBonusListTemplateId id #{randomBonusListTemplateId} used in `item_template_addon` " +
                    $"but it does not have records in `item_random_bonus_list_template` table.");
                return 0;
            }

            return tab.SelectRandomElementByWeight(x => x.Chance).BonusListID;
        }

        public static int GetRandomPropertyPoints(int itemLevel, ItemQuality quality, InventoryType inventoryType)
        {
            int propIndex;

            switch (inventoryType)
            {
                // Items of that type don`t have points
                case InventoryType.NonEquip:
                case InventoryType.Bag:
                case InventoryType.Tabard:
                case InventoryType.Ammo:
                case InventoryType.Quiver:
                case InventoryType.Relic:
                    return 0;

                // Select point coefficient
                case InventoryType.Head:
                case InventoryType.Body:
                case InventoryType.Chest:
                case InventoryType.Legs:
                case InventoryType.Weapon2Hand:
                case InventoryType.Robe:
                    propIndex = 0;
                    break;
                case InventoryType.Shoulders:
                case InventoryType.Waist:
                case InventoryType.Feet:
                case InventoryType.Hands:
                case InventoryType.Trinket:
                    propIndex = 1;
                    break;
                case InventoryType.Neck:
                case InventoryType.Wrists:
                case InventoryType.Finger:
                case InventoryType.Shield:
                case InventoryType.Cloak:
                case InventoryType.Holdable:
                    propIndex = 2;
                    break;
                case InventoryType.Weapon:
                case InventoryType.WeaponMainhand:
                case InventoryType.WeaponOffhand:
                    propIndex = 3;
                    break;
                case InventoryType.Ranged:
                case InventoryType.Thrown:
                case InventoryType.RangedRight:
                    propIndex = 4;
                    break;
                default:
                    return 0;
            }

            RandPropPointsRecord randPropPointsEntry = CliDB.RandPropPointsStorage.LookupByKey(itemLevel);
            if (randPropPointsEntry == null)
                return 0;

            switch (quality)
            {
                case ItemQuality.Uncommon:
                    return randPropPointsEntry.Good[propIndex];
                case ItemQuality.Rare:
                case ItemQuality.Heirloom:
                    return randPropPointsEntry.Superior[propIndex];
                case ItemQuality.Epic:
                case ItemQuality.Legendary:
                case ItemQuality.Artifact:
                    return randPropPointsEntry.Epic[propIndex];
            }

            return 0;
        }

        public static ItemRandomProperties GenerateRandomProperties(int item_id)
        {
            var properties = new ItemRandomProperties();

            ItemTemplate itemProto = Global.ObjectMgr.GetItemTemplate(item_id);
            if (itemProto == null)
                return properties;

            var randomSelect = itemProto.GetRandomSelect();
            var randomSuffix = itemProto.GetRandomSuffixGroupID();

            if (randomSelect == 0 && randomSuffix == 0)
                return properties;

            if (randomSelect != 0)
            {
                var tab = RandomEnchantmentData.LookupByKey(randomSelect);
                if (tab == null)
                {
                    Log.outError(LogFilter.Sql, 
                        $"Item RandomSelect Id {randomSelect} used " +
                        $"but it does not have records in `item_random_enchantment_template` table.");
                    return properties;
                }

                var randomPropertiesId = tab.SelectRandomElementByWeight(x => x.Chance);
                var randomPropertiesEntry = CliDB.ItemRandomPropertiesStorage.LookupByKey(randomPropertiesId.EnchantmentID);
                if (randomPropertiesEntry == null)
                {
                    Log.outError(LogFilter.Sql, 
                        $"Enchantment Id {randomPropertiesId.EnchantmentID} used " +
                        $"but it doesn't have records in 'ItemRandomProperties.db2'.");
                    return properties;
                }

                properties.RandomPropertiesID = randomPropertiesEntry.Id;
            }
            else if (randomSuffix != 0)
            {
                var tab = RandomEnchantmentData.LookupByKey(randomSuffix);
                if (tab == null)
                {
                    Log.outError(LogFilter.Sql, 
                        $"Item RandomSuffixGroupID Id {randomSuffix} used " +
                        $"but it does not have records in `item_random_enchantment_template` table.");
                    return properties;
                }

                var randomSuffixId = tab.SelectRandomElementByWeight(x => x.Chance);
                var randomSuffixEntry = CliDB.ItemRandomSuffixStorage.LookupByKey(randomSuffixId.EnchantmentID);
                if (randomSuffixEntry == null)
                {
                    Log.outError(LogFilter.Sql, 
                        $"Enchantment id Id {randomSuffixId} used " +
                        $"but it doesn't have records in 'ItemRandomSuffixEntry.db2'.");
                    return properties;
                }

                properties.RandomPropertiesID = randomSuffixEntry.Id;
                properties.RandomPropertiesSeed = GetRandomPropertyPoints(itemProto.GetItemLevel(), itemProto.GetQuality(), itemProto.GetInventoryType());
            }

            return properties;
        }

        public static Dictionary<int, List<(int EnchantmentID, float Chance)>> RandomEnchantmentData = new();
        public static Dictionary<int, List<(int BonusListID, float Chance)>> RandomBonusListData = new();
    }

    public struct ItemRandomProperties
    {
        public ItemRandomProperties(int randomPropertiesId = 0, int randomPropertiesSeed = 0)
        {
            RandomPropertiesID = randomPropertiesId;
            RandomPropertiesSeed = randomPropertiesSeed;
        }

        public int RandomPropertiesID;
        public int RandomPropertiesSeed;
    }
}
