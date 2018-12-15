﻿namespace MaterialColor
{
    using Harmony;
    using Extensions;
    using Helpers;
    using ONI_Common.Core;
    using ONI_Common.IO;
    using Rendering;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using JetBrains.Annotations;

    using UnityEngine;
    using Action = Action;
	using static KInputController;
	using System.Reflection;
    using MaterialColor.Data;

    public static class HarmonyPatches
    {

        private static bool _configuratorStateChanged;

        private static bool _elementColorInfosChanged;

        private static bool _initialized;

        private static bool _typeColorOffsetsChanged;

        public static void OnLoad() { }

        public static void RefreshMaterialColor()
        {
            UpdateBuildingsColors();
            RebuildAllTiles();
        }

        public static void UpdateBuildingColor(BuildingComplete building)
        {
            string    buildingName = building.name.Replace("Complete", string.Empty);
            SimHashes material     = MaterialHelper.ExtractMaterial(building);

            Color color;

            if (State.ConfiguratorState.Enabled)
            {
                switch (State.ConfiguratorState.ColorMode)
                {
                    case ColorMode.Json:
                        color = material.GetMaterialColorForType(buildingName);
                        break;

                    case ColorMode.DebugColor:
                        color = material.ToDebugColor();
                        break;

                    default:
                        color = ColorHelper.DefaultColor;
                        break;
                }
            }
            else
            {
                color = ColorHelper.DefaultColor;
            }

            if (State.TileNames.Contains(buildingName))
            {
                try
                {
                    if (ColorHelper.TileColors == null)
                    {
                        ColorHelper.TileColors = new Color?[Grid.CellCount];
                    }

                    ColorHelper.TileColors[Grid.PosToCell(building.gameObject)] = color;

                    return;
                }
                catch (Exception e)
                {
                    State.Logger.Log("Error while aquiring cell color");
                    State.Logger.Log(e);
                }
            }

            // TODO reimplement
            Color dimmedColor = new Color(1, 1, 1); //color.SetBrightness(color.GetBrightness() / 2);

            // storagelocker
            StorageLocker storageLocker = building.GetComponent<StorageLocker>();

            //////////////

            if (storageLocker != null)
            {
				SetFilteredStorageColors(storageLocker, (Color)color, (Color)dimmedColor);
			}
            else
            {
                // ownable
                Ownable ownable = building.GetComponent<Ownable>();

                if (ownable != null)
                {
					SetField(ownable, "ownedTint", (Color)color);
					SetField(ownable, "unownedTint", (Color)dimmedColor);
					Invoke(ownable, "UpdateTint");
                }
                else
                {
                    // rationbox
                    RationBox rationBox = building.GetComponent<RationBox>();

                    if (rationBox != null)
                    {
						SetFilteredStorageColors(rationBox, (Color)color, (Color)dimmedColor);
					}
                    else
                    {
                        // refrigerator
                        Refrigerator fridge = building.GetComponent<Refrigerator>();

                        if (fridge != null)
                        {
							SetFilteredStorageColors(fridge, (Color)color, (Color)dimmedColor);
						}
                        else
                        {
                            // anything else
                            KAnimControllerBase kAnimControllerBase = building.GetComponent<KAnimControllerBase>();

                            if (kAnimControllerBase != null)
                            {

                                KBatchedAnimInstanceData batchInstanceData = (KBatchedAnimInstanceData)AccessTools.Field(typeof(KAnimControllerBase), "batchInstanceData").GetValue(kAnimControllerBase); //GetValue instead?
                                batchInstanceData.SetTintColour(color);
                                //batchInstanceData.SetTintColour(new Color(0,5,5));
                                //kAnimControllerBase.TintColour = color;
                            }
                            else
                            {
                                Debug.Log($"MaterialColor: Can't find KAnimControllerBase component in <{buildingName}> and its not a registered tile.");
                            }
                        }
                    }
                }
            }
        }

        private static void Initialize()
        {
            SubscribeToFileChangeNotifier();
            _initialized = true;
        }

        private static void OnBuildingsCompletesAdd(BuildingComplete building) => UpdateBuildingColor(building);

        private static void OnElementColorsInfosChanged(object sender, FileSystemEventArgs e)
        {
            bool reloadColorInfosResult = false;

            try
            {
                reloadColorInfosResult = State.TryReloadElementColorInfos();
            }
            catch (Exception ex)
            {
                State.Logger.Log("ReloadElementColorInfos failed.");
                State.Logger.Log(ex);
            }

            if (reloadColorInfosResult)
            {
                _elementColorInfosChanged = true;

                const string message = "Element color infos changed.";

                State.Logger.Log(message);
                Debug.LogError(message);
            }
            else
            {
                State.Logger.Log("Reload element color infos failed");
            }
        }

        private static void OnMaterialStateChanged(object sender, FileSystemEventArgs e)
        {
            if (!State.TryReloadConfiguratorState())
            {
                return;
            }

            _configuratorStateChanged = true;

            const string message = "Configurator state changed.";

            State.Logger.Log(message);
            Debug.LogError(message);
        }

        private static void OnTypeColorOffsetsChanged(object sender, FileSystemEventArgs e)
        {
            if (!State.TryReloadTypeColorOffsets())
            {
                return;
            }

            _typeColorOffsetsChanged = true;

            const string message = "Type colors changed.";

            State.Logger.Log(message);
            Debug.LogError(message);
        }

        private static void RebuildAllTiles()
        {
            for (int i = 0; i < Grid.CellCount; i++)
            {
                World.Instance.blockTileRenderer.Rebuild(ObjectLayer.FoundationTile, i);
            }

            State.Logger.Log("All tiles rebuilt.");
        }

        private static void SaveTemperatureThresholdsAsDefault()
        {
            if (SimDebugView.Instance?.temperatureThresholds != null)
            {
                foreach (SimDebugView.ColorThreshold threshold in SimDebugView.Instance?.temperatureThresholds)
                {
                    State.DefaultTemperatureColors.Add(threshold.color);
                    State.DefaultTemperatures.Add(threshold.value);
                }
            }
        }

        // TODO: find a way to make it Color not Color32 here
        private static void SetFilteredStorageColors(
        [NotNull] object		  _storage,
        Color32                   color,
        Color32                   dimmedColor)
        {
			FilteredStorage storage = (FilteredStorage) GetField(_storage, "filteredStorage");

			SetField(storage, "filterTint", color);
			SetField(storage, "noFilterTint", dimmedColor);
			storage.FilterChanged();
        }
		
		private static object GetField(object _instance, string name)
		{
			FieldInfo fi = AccessTools.Field(_instance.GetType(), name);
			return fi.GetValue(_instance);
		}

		private static void SetField(object _instance, string name, object value)
		{
			FieldInfo fi = AccessTools.Field(_instance.GetType(), name);
			fi.SetValue(_instance, value);
		}

		private static object Invoke(object _instance, string name)
		{
			MethodInfo mi = AccessTools.Method(_instance.GetType(), name);
			return mi.Invoke(_instance, new object[] { });
		}

        private static void SubscribeToFileChangeNotifier()
        {
            const string jsonFilter = "*.json";

            try
            {
                FileChangeNotifier.StartFileWatch(
                                                  jsonFilter,
                                                  Paths.ElementColorInfosDirectory,
                                                  OnElementColorsInfosChanged);
                FileChangeNotifier.StartFileWatch(
                                                  jsonFilter,
                                                  Paths.TypeColorOffsetsDirectory,
                                                  OnTypeColorOffsetsChanged);

                FileChangeNotifier.StartFileWatch(
                                                  Paths.MaterialColorStateFileName,
                                                  Paths.MaterialConfigPath,
                                                  OnMaterialStateChanged);
            }
            catch (Exception e)
            {
                State.Logger.Log("SubscribeToFileChangeNotifier failed");
                State.Logger.Log(e);
            }
        }

        private static void UpdateBuildingsColors()
        {
            State.Logger.Log($"Trying to update {Components.BuildingCompletes.Count} buildings.");

            try
            {
                foreach (BuildingComplete building in Components.BuildingCompletes.Items)
                {
                    OnBuildingsCompletesAdd(building);
                }

                State.Logger.Log("Buildings updated successfully.");
            }
            catch (Exception e)
            {
                State.Logger.Log("Buildings colors update failed.");
                State.Logger.Log(e);
            }
        }

        private static void LogTemperatureThresholds()
        {
            for (int i = 0; i < SimDebugView.Instance.temperatureThresholds.Length; i++)
            {
                string message = SimDebugView.Instance.temperatureThresholds[i].value.ToString();
                Color32 color = SimDebugView.Instance.temperatureThresholds[i].color;

                State.Logger.Log("Temperature Color " + i + " at " + message + " K: " + color);
            }
        }

        [HarmonyPatch(typeof(BlockTileRenderer), nameof(BlockTileRenderer.GetCellColour))]
        public static class BlockTileRenderer_GetCellColour
        {
            public static bool Prefix(int cell, SimHashes element, BlockTileRenderer __instance, ref Color __result)
            {
                try
                {
                    Color tileColor;

                    if (State.ConfiguratorState.Enabled)
                    {
                        if (State.ConfiguratorState.LegacyTileColorHandling)
                        {
                            switch (State.ConfiguratorState.ColorMode)
                            {
                                case ColorMode.Json:
                                    tileColor = ColorHelper.GetCellColorJson(cell);
                                    break;

                                case ColorMode.DebugColor:
                                    tileColor = ColorHelper.GetCellColorDebug(cell);
                                    break;

                                default:
                                    tileColor = ColorHelper.DefaultCellColor;
                                    break;
                            }
                        }
                        else
                        {
                            if (ColorHelper.TileColors.Length > cell && ColorHelper.TileColors[cell].HasValue)
                            {
                                tileColor = ColorHelper.TileColors[cell].Value;
                            }
                            else
                            {
								if (cell == (int) GetField(__instance, "invalidPlaceCell"))
								{
                                    __result = ColorHelper.InvalidCellColor;
                                    return false;
                                }

                                tileColor = ColorHelper.DefaultCellColor;
                            }
                        }
                    }
                    else
                    {
                        tileColor = ColorHelper.DefaultCellColor;
                    }

					if (cell == (int) GetField(__instance, "selectedCell"))
					{
                        __result = tileColor * 1.5f;
                        return false;
                    }

					if (cell == (int)GetField(__instance, "highlightCell"))
					{
                        __result = tileColor * 1.25f;
                        return false;
                    }

                    __result = tileColor;
                    return false;
                }
                catch (Exception e)
                {
                    State.Logger.Log("EnterCell failed.");
                    State.Logger.Log(e);
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Deconstructable), "OnCompleteWork")]
        public static class Deconstructable_OnCompleteWork_MatCol
        {
            public static void Postfix(Deconstructable __instance)
            {
                ResetCell(__instance.GetCell());
            }

            public static void ResetCell(int cellIndex)
            {
                if (ColorHelper.TileColors.Length > cellIndex)
                {
                    ColorHelper.TileColors[cellIndex] = null;
                }
            }
        }

        [HarmonyPatch(typeof(Game), "Update")]
        public static class Game_Update_EnterEveryUpdate
        {
            public static void Prefix()
            {
                try
                {
                    if (_elementColorInfosChanged || _typeColorOffsetsChanged || _configuratorStateChanged)
                    {
                        RefreshMaterialColor();
                        _elementColorInfosChanged = _typeColorOffsetsChanged = _configuratorStateChanged = false;
                    }
                }
                catch (Exception e)
                {
                    State.Logger.Log("EnterEveryUpdate failed.");
                    State.Logger.Log(e);
                }
            }
        }

        [HarmonyPatch(typeof(Game), "Update")]
        public static class Game_Update_EnterEveryUpdate_CoreUpdateQueueManager
        {
            public static void Prefix()
            {
                UpdateQueueManager.OnGameUpdate();
            }
        }

        [HarmonyPatch(typeof(Global), "GenerateDefaultBindings")]
        public static class Global_GenerateDefaultBindings
        {
            public static void Postfix(ref BindingEntry[] __result)
            {

                if (State.ConfiguratorState.LogElementsData)
                {
                    State.Logger.Log("Element List:");
                    var values = Enum.GetNames(typeof(SimHashes));
                    Array.Sort(values);
                    string elementsLog = "";
                    foreach (var name in values)
                    {
                        elementsLog += Environment.NewLine+name;
                    }
                    State.Logger.Log(elementsLog);
                }

                try
                {
                    List<BindingEntry> bind = __result.ToList();
                    BindingEntry entry = new BindingEntry(
                                                          "Root",
                                                          GamepadButton.NumButtons,
                                                          KKeyCode.F6,
                                                          Modifier.Alt,
                                                          (Action)IDs.ToggleMaterialColorOverlayAction,
                                                          true,
                                                          true);
                    bind.Add(entry);
                    __result = bind.ToArray();
                }
                catch (Exception e)
                {
                    State.Logger.Log("Keybindings failed:\n" + e);
                    throw;
                }

            }
        }

        [HarmonyPatch(typeof(OverlayMenu), "InitializeToggles")]
        public static class OverlayMenu_InitializeToggles
        {
            // TODO: read from file instead
            public static void Postfix(OverlayMenu __instance, ref List<KIconToggleMenu.ToggleInfo> __result)
            {
				Type oti = AccessTools.Inner(typeof(OverlayMenu), "OverlayToggleInfo");
				
				ConstructorInfo ci =  oti.GetConstructor(new Type[] { typeof(string), typeof(string), typeof(SimViewMode), typeof(string), typeof(Action), typeof(string), typeof(string) });
				object ooti = ci.Invoke(new object[] {
						"Toggle MaterialColor",
						"overlay_materialcolor",
						(SimViewMode)IDs.ToggleMaterialColorOverlayID,
						string.Empty,
						(Action)IDs.ToggleMaterialColorOverlayAction,
						"Toggles MaterialColor overlay",
						"MaterialColor"
				});
				((KIconToggleMenu.ToggleInfo)ooti).getSpriteCB = GetUISprite;

				__result.Add((KIconToggleMenu.ToggleInfo)ooti);

				/*
				__result.Add(
                             new OverlayMenu.OverlayToggleInfo(
                                                               "Toggle MaterialColor",
                                                               "overlay_materialcolor",
                                                               (SimViewMode)IDs.ToggleMaterialColorOverlayID,
                                                               string.Empty,
                                                               (Action)IDs.ToggleMaterialColorOverlayAction,
                                                               "Toggles MaterialColor overlay",
                                                               "MaterialColor") {
                                                                                   getSpriteCB = () => GetUISprite()
                                                                                });
				*/
			}

            private static Sprite GetUISprite()
            {
                return FileManager.LoadSpriteFromFile(Paths.MaterialColorOverlayIconPath, 256, 256);
            }
        }

        [HarmonyPatch(typeof(OverlayMenu), "OnOverlayChanged")]
        public static class OverlayMenu_OnOverlayChanged_OverlayChangedEntry
        {
            public static void Prefix()
            {
                try
                {
                    switch (OverlayScreen.Instance.GetMode())
                    {
                        case SimViewMode.PowerMap:
                        case SimViewMode.GasVentMap:
                        case SimViewMode.LiquidVentMap:
                        case SimViewMode.Logic:
                            RefreshMaterialColor();
                            break;
                    }
                }
                catch (Exception e)
                {
                    State.Logger.Log("OverlayChangedEntry failed");
                    State.Logger.Log(e);
                }
            }
        }

		[HarmonyPatch(typeof(KeyDef), MethodType.Constructor)]
		[HarmonyPatch(new Type[] {typeof(KKeyCode), typeof(Modifier) })]
		public static class KeyDef_Constructor
		{
			[HarmonyPostfix]

			// ReSharper disable once InconsistentNaming
			public static void ExitKeyDef(KeyDef __instance)
			{
				__instance.mActionFlags = new bool[1000];
			}
		}

		[HarmonyPatch(typeof(KInputController), MethodType.Constructor)]
		[HarmonyPatch(new Type[] { typeof(bool) })]
		public static class KInputController_Constructor
		{
			[HarmonyPostfix]

			// ReSharper disable once InconsistentNaming
			public static void KInputControllerMod(KeyDef __instance)
			{
				SetField(__instance, "mActionState", new bool[1000]);
			}
		}

		[HarmonyPatch(typeof(OverlayMenu), "OnToggleSelect")]
        public static class OverlayMenu_OnToggleSelect_MatCol
        {
            [HarmonyPrefix]

            // ReSharper disable once InconsistentNaming
            public static bool EnterToggle(OverlayMenu __instance, KIconToggleMenu.ToggleInfo toggle_info)
            {
                try
                {
					//bool toggleMaterialColor = ((OverlayMenu.OverlayToggleInfo)toggle_info).simView
					bool toggleMaterialColor = (SimViewMode)GetField(toggle_info, "simView")
											== (SimViewMode)IDs.ToggleMaterialColorOverlayID;

                    if (!toggleMaterialColor)
                    {
                        return true;
                    }

                    State.ConfiguratorState.Enabled = !State.ConfiguratorState.Enabled;

                    RefreshMaterialColor();

                    return false;
                }
                catch (Exception e)
                {
                    State.Logger.Log("EnterToggle failed.");
                    State.Logger.Log(e);
                    return true;
                }
            }
        }

        /// <summary>
        /// Material + element color
        /// </summary>
        [HarmonyPatch(typeof(Game), "OnPrefabInit")]
        public static class SimDebugView_OnPrefabInit_EnterOnce
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
               
                try
                {
                    Components.BuildingCompletes.OnAdd += OnBuildingsCompletesAdd;

                    if (!_initialized)
                    {
                        Initialize();
                    }

                    _elementColorInfosChanged = _typeColorOffsetsChanged = _configuratorStateChanged = true;
                }
                catch (Exception e)
                {
                    string message = "Injection failed\n" + e.Message + '\n';

                    State.Logger.Log(message);
                    State.Logger.Log(e);

                    Debug.LogError(message);
                }
            }
        }
    }
}