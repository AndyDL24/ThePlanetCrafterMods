﻿// Copyright (c) 2022-2024, David Karnok & Contributors
// Licensed under the Apache License, Version 2.0

using BepInEx;
using SpaceCraft;
using HarmonyLib;
using TMPro;
using System;

namespace UIShowETA
{
    [BepInPlugin("akarnokd.theplanetcraftermods.uishoweta", "(UI) Show ETA", PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("akarnokd.theplanetcraftermods.fixunofficialpatches", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public void Awake()
        {
            LibCommon.BepInExLoggerFix.ApplyFix();

            // Plugin startup logic
            Logger.LogInfo($"Plugin is loaded!");


            Harmony.CreateAndPatchAll(typeof(Plugin));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ScreenTerraStage), "RefreshDisplay", new Type[0])]
        static void ScreenTerraStage_RefreshDisplay(
            TextMeshProUGUI ___percentageProcess, 
            TerraformStagesHandler ___terraformStagesHandler)
        {
            TerraformStage nextGlobalStage = ___terraformStagesHandler.GetNextGlobalStage();
            if (nextGlobalStage == null)
            {
                ___percentageProcess.text = "<br><color=#FFFF00>ETA</color><br>Done";
            }
            else
            {
                var wuh = Managers.GetManager<WorldUnitsHandler>();
                var speed = wuh.GetUnit(nextGlobalStage.GetWorldUnitType()).GetCurrentValuePersSec();
                var remaining = nextGlobalStage.GetStageStartValue() - wuh.GetUnit(nextGlobalStage.GetWorldUnitType()).GetValue();

                if (speed <= 0)
                {
                    ___percentageProcess.text += "<br><color=#FFFF00>ETA</color><br>Infinite";
                }
                else
                {
                    var time = (long)(remaining / speed);
                    if (time > 0)
                    {
                        if (time < 366 * 24 * 60 * 60)
                        {
                            var ts = TimeSpan.FromSeconds(time);

                            if (ts.Days > 0)
                            {
                                ___percentageProcess.text += string.Format("<br><color=#FFFF00>ETA</color><br>{0:#} days<br>{1}:{2:00}:{3:00}", ts.Days, ts.Hours, ts.Minutes, ts.Seconds);
                            }
                            else
                            {
                                ___percentageProcess.text += string.Format("<br><color=#FFFF00>ETA</color><br>{1}:{2:00}:{3:00}", ts.Days, ts.Hours, ts.Minutes, ts.Seconds);
                            }
                        }
                        else
                        {
                            ___percentageProcess.text += "<br><color=#FFFF00>ETA</color><br>Year+";
                        }
                    }
                    else
                    {
                        ___percentageProcess.text += "<br><color=#FFFF00>ETA</color><br>Done";
                    }
                }
            }
        }
    }
}
