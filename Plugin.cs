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
using static M_Level;
using static System.Net.Mime.MediaTypeNames;
using static UnityEngine.UIElements.StylePropertyAnimationSystem;
using static WorldLoader;

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
                    //LoadLevelG(level.name);
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
            //LoadLevelG(curname);
        }
    }
    //public static string NextLevel;
    /*public static void LoadLevelG(string name)
    {
        CL_Leaderboard.WK_Leaderboard_Core.disableLeaderboards = true;
        Debug.LogWarning("Disabled leaderboards due to level loader use");
        SceneManager.sceneLoaded += AfterLevelLoaded;
        NextLevel = name;
        CL_GameManager.gamemodeArgs = [];
        M_Gamemode gamemodeAsset = CL_AssetManager.GetGamemodeAsset("GM_Level_Tester");
        CL_GameManager.gMan.SetGamemode(gamemodeAsset);
        CL_GameManager.gMan.baseGamemode = gamemodeAsset;
        SceneManager.LoadScene("Game-Main");
    }
    public static void AfterLevelLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= AfterLevelLoaded;
        if (scene.name != "Game-Main") return;
        CL_GameManager.gMan.uiMan.ascentHeader.ShowText("<color=red>Disabled leaderboards due to level loader use.<br> This will persist until the game is restarted</color>"); // doesnt show up
        Transform[] allChildren = WorldLoader.instance.GetComponentsInChildren<Transform>(true);
        foreach(Transform child in allChildren)
        {
            child.gameObject.SetActive(false);
        }
        GameObject obj = GameObject.Instantiate(levelPrefabsList.First((e) => e.Key.name == NextLevel).Key);
        WorldLoader.instance.GetHandholdManager().LoadHandholds(obj);
        ENT_Player.playerObject.Teleport(obj.GetComponent<M_Level>().GetSpawnPosition());
        WorldLoader.instance.StartCoroutine(MassFuckOff());
    }
    public static IEnumerator MassFuckOff()
    {
        while (DEN_DeathFloor.instance == null || DEN_DeathFloor.instance.active)
        {
            if (DEN_DeathFloor.instance != null)
            {
                DEN_DeathFloor.instance.DeathGooGoAway([]);
                yield break;
            }
        }
    }*/

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
            var holder = M_Level.LevelAssetHolder.GetNewHolderFromLevel(gameObject.GetComponent<M_Level>());
            CL_AssetManager.GetBaseAssetDatabase().levelAssets.Add(holder);
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
            CL_AssetManager.GetBaseAssetDatabase().levelAssets.Remove(M_Level.LevelAssetHolder.GetNewHolderFromLevel(thing.GetComponent<M_Level>()));
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
    [HarmonyPatch(typeof(CL_AssetManager), nameof(CL_AssetManager.UnloadAllLevels))]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    static bool Patch2()
    {
        return false;
    }
}
/*
[Info   : Unity Log] ldarg.0 NULL
[Info   : Unity Log] newobj void System.Collections.Generic.Dictionary<string, UnityEngine.GameObject>::.ctor()
[Info   : Unity Log] stfld System.Collections.Generic.Dictionary<string, UnityEngine.GameObject> CL_AssetManager+WKDatabaseHolder::assetDictionary
[Info   : Unity Log] ldarg.0 NULL
[Info   : Unity Log] newobj void System.Collections.Generic.Dictionary<string, UnityEngine.AddressableAssets.AssetReferenceGameObject>::.ctor()
[Info   : Unity Log] stfld System.Collections.Generic.Dictionary<string, UnityEngine.AddressableAssets.AssetReferenceGameObject> CL_AssetManager+WKDatabaseHolder::addressibleAssetDictionary
[Info   : Unity Log] ldarg.0 NULL
[Info   : Unity Log] ldarg.0 NULL
[Info   : Unity Log] ldfld WKAssetDatabase CL_AssetManager+WKDatabaseHolder::database
[Info   : Unity Log] ldfld System.Collections.Generic.List<UnityEngine.GameObject> WKAssetDatabase::itemPrefabs
[Info   : Unity Log] call void CL_AssetManager+WKDatabaseHolder::<RefreshDictionary>g__FillObjectDictionaryFromList|4_0(System.Collections.Generic.List<UnityEngine.GameObject> objectList)
[Info   : Unity Log] ldarg.0 NULL
[Info   : Unity Log] ldarg.0 NULL
[Info   : Unity Log] ldfld WKAssetDatabase CL_AssetManager+WKDatabaseHolder::database
[Info   : Unity Log] ldfld System.Collections.Generic.List<UnityEngine.GameObject> WKAssetDatabase::entityPrefabs
[Info   : Unity Log] call void CL_AssetManager+WKDatabaseHolder::<RefreshDictionary>g__FillObjectDictionaryFromList|4_0(System.Collections.Generic.List<UnityEngine.GameObject> objectList)
[Info   : Unity Log] ldarg.0 NULL
[Info   : Unity Log] ldfld WKAssetDatabase CL_AssetManager+WKDatabaseHolder::database
[Info   : Unity Log] ldfld System.Collections.Generic.List<M_Level+LevelAssetHolder> WKAssetDatabase::levelAssets
[Info   : Unity Log] callvirt System.Collections.Generic.List<M_Level+LevelAssetHolder>+Enumerator System.Collections.Generic.List<M_Level+LevelAssetHolder>::GetEnumerator()
[Info   : Unity Log] stloc.0 NULL
[Info   : Unity Log] br Label1 [EX_BeginException]
[Info   : Unity Log] ldloca.s 0 (System.Collections.Generic.List`1+Enumerator[M_Level+LevelAssetHolder]) [Label2]
[Info   : Unity Log] call virtual M_Level+LevelAssetHolder System.Collections.Generic.List<M_Level+LevelAssetHolder>+Enumerator::get_Current()
[Info   : Unity Log] stloc.1 NULL
[Info   : Unity Log] ldarg.0 NULL
[Info   : Unity Log] ldfld System.Collections.Generic.Dictionary<string, UnityEngine.AddressableAssets.AssetReferenceGameObject> CL_AssetManager+WKDatabaseHolder::addressibleAssetDictionary
[Info   : Unity Log] ldloc.1 NULL
[Info   : Unity Log] ldfld string M_Level+LevelAssetHolder::id
[Info   : Unity Log] ldloc.1 NULL
[Info   : Unity Log] ldfld UnityEngine.AddressableAssets.AssetReferenceGameObject M_Level+LevelAssetHolder::levelAssetReference
[Info   : Unity Log] callvirt virtual void System.Collections.Generic.Dictionary<string, UnityEngine.AddressableAssets.AssetReferenceGameObject>::Add(string key, UnityEngine.AddressableAssets.AssetReferenceGameObject value)
[Info   : Unity Log] ldloca.s 0 (System.Collections.Generic.List`1+Enumerator[M_Level+LevelAssetHolder]) [Label1]
[Info   : Unity Log] call virtual bool System.Collections.Generic.List<M_Level+LevelAssetHolder>+Enumerator::MoveNext()
[Info   : Unity Log] brtrue Label2
[Info   : Unity Log] leave Label3
[Info   : Unity Log] ldloca.s 0 (System.Collections.Generic.List`1+Enumerator[M_Level+LevelAssetHolder]) [EX_BeginFinally]
[Info   : Unity Log] constrained. System.Collections.Generic.List`1+Enumerator[M_Level+LevelAssetHolder]
[Info   : Unity Log] callvirt abstract virtual void IDisposable::Dispose()
[Info   : Unity Log] endfinally NULL [EX_EndException]
[Info   : Unity Log] ret NULL [Label3] 
*/