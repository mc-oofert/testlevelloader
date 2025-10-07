using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static System.Net.Mime.MediaTypeNames;
using static UnityEngine.UIElements.StylePropertyAnimationSystem;

namespace TestLevelLoader;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public static new ManualLogSource Logger;

    public static HashSet<AssetBundle> loadedBundles = [];

    public static Dictionary<GameObject, string> levelPrefabsList = [];

    public static string bundleFolderPath;

    public static ConfigEntry<KeyCode> toggleKey;
    public static ConfigEntry<string> bundleFolder;
    public static ConfigEntry<bool> bundleFolderRelative;
    public static bool UIEnabled = false;

    public void Awake() // As of 7.8.2025 White Knuckle still seems to require HideManagerGameObject enabled in BepInEx config.
    {
        // Plugin startup logic
        Logger = base.Logger;
        bundleFolder = Config.Bind("Misc", "PathToFolder", "CustomLevels", "Where we look for bundles.");
        bundleFolderRelative = Config.Bind("Misc", "PathToFolderIsRelative", true, "Is PathToFolder relative to the bepinex plugin folder?");
        bundleFolderPath = bundleFolderRelative.Value ? Path.Combine(Paths.PluginPath, bundleFolder.Value) : bundleFolder.Value;

        UIEnabled = Config.Bind<bool>("Controls", "UIStartsEnabled", false).Value;
        if (!Directory.Exists(bundleFolderPath))
        {
            Directory.CreateDirectory(bundleFolderPath);
        }
        toggleKey = Config.Bind("Controls", "UIKey", KeyCode.F7);
        Harmony.CreateAndPatchAll(typeof(Patches)); // Does all the patches in Patches
    }
    public static Rect windowRect = new Rect(20, 20, 400, 300);
    public static Vector2 scrollPos = Vector2.zero;
    void OnGUI()
    {
        if (!UIEnabled) return;
        windowRect = GUI.Window(69, windowRect, DoMyWindow, "Level Loader ("+toggleKey.Value+" to hide)");
    }
    void Update()
    {
        if (Input.GetKeyDown(toggleKey.Value))
        {
            UIEnabled = !UIEnabled;
        }
    }
    static void DoMyWindow(int windowID)
    {
        var centeredStyle = GUI.skin.GetStyle("Label");
        centeredStyle.alignment = TextAnchor.UpperCenter;
        centeredStyle.fontSize = 18;
        centeredStyle.richText = true;
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
        GUILayout.Label("Loaded bundles: "+loadedBundles.Count);
        if(loadedBundles.Count <= 0)
        {
            GUILayout.Label("No bundles loaded. Databases either havent loaded or something messed up");
        }
        scrollPos = GUILayout.BeginScrollView(scrollPos, alwaysShowHorizontal: false, alwaysShowVertical: true);
        {
            foreach (AssetBundle bundle in loadedBundles)
            {
                if (bundle.name.IsNullOrWhiteSpace()) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label("Bundle: " + bundle.name, centeredStyle);
                GUILayout.EndHorizontal();
                IEnumerable<KeyValuePair<GameObject, string>> levels = levelPrefabsList.Where((levelPair) => levelPair.Value == bundle.name);
                foreach (var levelPair in levels)
                {
                    var level = levelPair.Key;
                    if (GUILayout.Button(level.name))
                        CL_GameManager.gMan.LoadLevels([level.name]);
                }
            }
        }
        GUILayout.EndScrollView();
        if (GUILayout.Button("Reload All Bundles (buggy, might break mats)"))
        {
            string curname = CL_EventManager.currentLevel != null ? CL_EventManager.currentLevel.levelName : "";
            UnloadBundles();
            LoadAllBundles();
            FXManager.handholdMaterialDict = null;
            if (SceneManager.GetActiveScene().name != "Game-Main") return;
            CL_GameManager.gMan.LoadLevels([curname]);
        }
    }

    public static void LoadAllBundles()
    {
        string[] files = Directory.GetFiles(bundleFolderPath, "*.*", SearchOption.AllDirectories);
        AssetBundle[] loadedAlready = AssetBundle.GetAllLoadedAssetBundles().ToArray();
        foreach (string text in files)
        {
            try
            {
                string filename = Path.GetFileName(text);
                string extension = Path.GetExtension(filename);
                if (!extension.IsNullOrWhiteSpace() || loadedAlready.Any((b) => b.name == filename)) continue;
                AssetBundle val = AssetBundle.LoadFromFile(text);
                if (val == null) continue;
                if(val.name.IsNullOrWhiteSpace())
                {
                    Plugin.Logger.LogWarning("Ignoring file " + filename + " because it is missing a bundle name");
                    val.Unload(false);
                    continue;
                }
                loadedBundles.Add(val);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e);
            }
        }
        RegisterAllLevels();
    }

    public static void RegisterAllLevels()
    {
        if (levelPrefabsList == null) return;
        foreach (AssetBundle bundle in loadedBundles)
        {
            RegisterLevelsFromBundle(bundle);
        }
    }

    public static void RegisterLevelsFromBundle(AssetBundle bundle)
    {
        if (bundle == null) return;
        GameObject[] array = bundle.LoadAllAssets<GameObject>();
        foreach (GameObject gameObject in array)
        {
            if (levelPrefabsList.Any((level) => level.Key.name == gameObject.name)) continue;
            levelPrefabsList.Add(gameObject, bundle.name);
            CL_AssetManager.GetBaseAssetDatabase().levelPrefabs.Add(gameObject);
        }
    }

    public static void UnloadBundle(AssetBundle bundle)
    {
        loadedBundles.Remove(bundle);
        List<GameObject> toRemove = new();
        foreach (var pair in levelPrefabsList)
        {
            if(pair.Value == bundle.name)
                toRemove.Add(pair.Key);
        }
        foreach(var thing in toRemove)
        {
            CL_AssetManager.GetBaseAssetDatabase().levelPrefabs.Remove(thing);
            levelPrefabsList.Remove(thing);
        }
        bundle.Unload(false);
    }

    public static void UnloadBundles()
    {
        AssetBundle[] bundles = loadedBundles.ToArray();
        foreach (AssetBundle bundle in bundles)
        {
            UnloadBundle(bundle);
        }
        loadedBundles.Clear();
    }
}
class Patches
{
    [HarmonyPatch(typeof(CL_AssetManager), nameof(CL_AssetManager.InitializeAssetManager))]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    static void Patch()
    {
        if (Plugin.loadedBundles.Count > 0) return;
        Plugin.LoadAllBundles();
    }
}
