﻿using BepInEx;
using MijuTools;
using SpaceCraft;
using HarmonyLib;
using TMPro;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using System;
using System.Reflection;
using BepInEx.Logging;

namespace CheatAutoHarvest
{
    [BepInPlugin("akarnokd.theplanetcraftermods.cheatautoharvest", "(Cheat) Automatically Harvest Food n Algae", "1.0.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        static MethodInfo updateGrowing;
        static MethodInfo instantiateAtRandomPosition;
        static FieldInfo machineGrowerInventory;
        static FieldInfo worldObjectsDictionary;

        static ManualLogSource logger;
        static bool debugAlgae = false;
        static bool debugFood = false;

        static ConfigEntry<bool> harvestAlgae;
        static ConfigEntry<bool> harvestFood;

        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin is loaded!");

            logger = Logger;
            updateGrowing = AccessTools.Method(typeof(MachineOutsideGrower), "UpdateGrowing", new Type[] { typeof(float) });
            instantiateAtRandomPosition = AccessTools.Method(typeof(MachineOutsideGrower), "InstantiateAtRandomPosition", new Type[] { typeof(GameObject), typeof(bool) });
            machineGrowerInventory = AccessTools.Field(typeof(MachineGrower), "inventory");
            worldObjectsDictionary = AccessTools.Field(typeof(WorldObjectsHandler), "worldObjects");
            harvestAlgae = Config.Bind("General", "HarvestAlgae", true, "Enable auto harvesting for algae.");
            harvestFood = Config.Bind("General", "HarvestFood", true, "Enable auto harvesting for food.");

            Harmony.CreateAndPatchAll(typeof(Plugin));
        }

        static void logAlgae(string s)
        {
            if (debugAlgae)
            {
                logger.LogInfo(s);
            }
        }
        static void logFood(string s)
        {
            if (debugFood)
            {
                logger.LogInfo(s);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MachineOutsideGrower), "Grow")]
        static void MachineOutsideGrower_Grow(
            MachineOutsideGrower __instance, 
            float ___growSize,
            WorldObject ___worldObjectGrower, 
            List<GameObject> ___instantiatedGameObjects,
            float ___updateInterval,
            int ___spawNumber)
        {
            if (!harvestAlgae.Value)
            {
                return;
            }

            if (___instantiatedGameObjects != null)
            {
                bool restartCoroutine = false;

                logAlgae("Grower: " + ___worldObjectGrower.GetId() + " @ " + ___worldObjectGrower.GetGrowth() + " - " + ___instantiatedGameObjects.Count + " < " + ___spawNumber);
                foreach (GameObject go in new List<GameObject>(___instantiatedGameObjects))
                {
                    if (go != null)
                    {
                        ActionGrabable ag = go.GetComponent<ActionGrabable>();
                        if (ag != null)
                        {
                            WorldObjectAssociated woa = go.GetComponent<WorldObjectAssociated>();
                            if (woa != null)
                            {
                                WorldObject wo = woa.GetWorldObject();
                                if (wo != null)
                                {
                                    float progress = 100f * go.transform.localScale.x / ___growSize;
                                    logAlgae("  - [" + wo.GetId() + "]  "  + wo.GetGroup().GetId() + " @ " + (progress) + "%");
                                    if (progress >= 100f)
                                    {
                                        if (FindInventory(wo, out Inventory inv))
                                        {
                                            if (inv.AddItem(wo))
                                            {
                                                logAlgae("    Deposited [" + wo.GetId() + "]  *" + wo.GetGroup().GetId());
                                                wo.SetDontSaveMe(false);

                                                ___instantiatedGameObjects.Remove(go);
                                                UnityEngine.Object.Destroy(go);

                                                // from OnGrabedAGrowing to avoid reentrance

                                                GroupItem growableGroup = ((GroupItem)wo.GetGroup()).GetGrowableGroup();
                                                GameObject objectToInstantiate = (growableGroup != null) ? growableGroup.GetAssociatedGameObject() : wo.GetGroup().GetAssociatedGameObject();
                                                instantiateAtRandomPosition.Invoke(__instance, new object[] { objectToInstantiate, false });

                                                restartCoroutine = true;
                                            }
                                        }
                                        else
                                        {
                                            logAlgae("    No inventory for [" + wo.GetId() + "]  *" + wo.GetGroup().GetId());
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                logAlgae("Grower: " + ___worldObjectGrower.GetId() + " @ " + ___worldObjectGrower.GetGrowth() + " - " + ___instantiatedGameObjects.Count + " ---- DONE");

                if (restartCoroutine)
                {
                    __instance.StopAllCoroutines();
                    __instance.StartCoroutine((IEnumerator)updateGrowing.Invoke(__instance, new object[] { ___updateInterval }));
                }
            }
        }

        void Start()
        {
            StartCoroutine(CheckFoodGrowersLoop(5));
        }

        IEnumerator CheckFoodGrowersLoop(float delay)
        {
            for (; ; )
            {
                CheckFoodGrowers();
                yield return new WaitForSeconds(delay);
            }
        }

        void CheckFoodGrowers()
        {
            if (!harvestFood.Value)
            {
                return;
            }
            logFood("Edible: Ingame?");
            if (Managers.GetManager<PlayersManager>() == null)
            {
                return;
            }

            logFood("Edible: begin search");
            int deposited = 0;
            MachineGrower[] allMachineGrowers = null;
            Dictionary<WorldObject, GameObject> map = (Dictionary<WorldObject, GameObject>)worldObjectsDictionary.GetValue(null);

            foreach (WorldObject wo in WorldObjectsHandler.GetAllWorldObjects())
            {
                GroupItem g = wo.GetGroup() as GroupItem;
                if (g != null && g.GetUsableType() == DataConfig.UsableType.Eatable)
                {
                    if (map.TryGetValue(wo, out GameObject go) && go != null)
                    {
                        ActionGrabable ag = go.GetComponent<ActionGrabable>();
                        if (ag != null)
                        {
                            logFood("Edible for grab: " + wo.GetId() + " of *" + g.id);
                            if (FindInventory(wo, out Inventory inv))
                            {
                                logFood("  Found inventory.");
                                if (allMachineGrowers == null)
                                {
                                    allMachineGrowers = UnityEngine.Object.FindObjectsOfType<MachineGrower>();
                                }

                                bool found = false;
                                // we have to find which grower wo came from so it can be reset
                                foreach (MachineGrower mg in allMachineGrowers)
                                {
                                    if ((wo.GetPosition() - mg.spawnPoint.transform.position).magnitude < 0.2f)
                                    {
                                        logFood("  Found MachineGrower");
                                        if (inv.AddItem(wo))
                                        {
                                            logFood("  Adding to target inventory");
                                            UnityEngine.Object.Destroy(go);

                                            // readd seed
                                            Inventory machineInventory = (Inventory)machineGrowerInventory.GetValue(mg);

                                            WorldObject seed = machineInventory.GetInsideWorldObjects()[0];

                                            machineInventory.RemoveItem(seed, false);
                                            seed.SetLockInInventoryTime(0f);
                                            machineInventory.AddItem(seed);

                                            deposited++;
                                            found = true;
                                        }

                                        break;
                                    }
                                }
                                if (!found)
                                {
                                    logFood("  Could not find MachineGrower of this edible");
                                }
                            }
                        }
                    }
                }
            }
            logFood("Edible deposited: " + deposited);
        }

        static bool FindInventory(WorldObject wo, out Inventory inventory)
        {
            string gid = "*" + wo.GetGroup().GetId().ToLower();
            //logger.LogInfo("    Finding inventory for " + gid);
            foreach (WorldObject wo2 in WorldObjectsHandler.GetAllWorldObjects())
            {
                if (wo2 != null && wo2.HasLinkedInventory())
                {
                    Inventory inv2 = InventoriesHandler.GetInventoryById(wo2.GetLinkedInventoryId());
                    if (inv2 != null && !inv2.IsFull())
                    {
                        string txt = wo2.GetText();
                        if (txt != null && txt.ToLower().Contains(gid))
                        {
                            inventory = inv2;
                            return true;
                        }
                    }
                }
            }
            inventory = null;
            return false;
        }
    }
}