﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using UnityEngine;

namespace MSCLoader
{
    public enum CurrentScene { MainMenu, Game, NewGameIntro }

    public class ModLoader : MonoBehaviour
    {
        public static readonly string ModLoaderVersion = "1.0";
        internal static string ModsFolder = $@"Mods";
        internal static string AssetsFolder = $@"{ModsFolder}\Assets";
        internal static string SettingsFolder = $@"{ModsFolder}\Settings";

        public static List<Mod> LoadedMods { get; internal set; }
        public static List<List<Mod>> ModMethods;

        internal static ModLoader modLoaderInstance;
        internal static ModUnloader modUnloader;
        internal static ModLoaderSettings modLoaderSettings;
        internal static ModContainer modContainer;

        Stopwatch timer;
        static bool loaderInitialized = false;
        internal static bool unloading = false, mainMenuReturn = false;

        GameObject modUILoadScreen;
        public UILoadHandler modSceneLoadHandler;
        bool newGameStarted = false, vSyncEnabled = false;
        GUISkin modLoaderSkin;

        static CurrentScene CurrentGameScene;
        public static CurrentScene GetCurrentScene() => CurrentGameScene;

        internal static string GetModSettingsFolder(Mod mod, bool create = true)
        {
            string path = Path.Combine(SettingsFolder, mod.ID);

            if (!Directory.Exists(path) && create) Directory.CreateDirectory(path);

            return path;
        }

        public static string GetModAssetsFolder(Mod mod) => Path.Combine(AssetsFolder, mod.ID);

        public static bool IsModPresent(string ModID) => 
            LoadedMods.FirstOrDefault(mod => mod.ID.Equals(ModID) && !mod.isDisabled) != null;

        internal static void Init()
        {
            if (unloading) return;

            if (!loaderInitialized)
            {
                loaderInitialized = true;
                GameObject mscLoader = new GameObject("MSCLoader");
                modLoaderInstance = mscLoader.AddComponent<ModLoader>();
                DontDestroyOnLoad(mscLoader);
            }
        }

        void OnLevelWasLoaded(int level)
        {
            switch (Application.loadedLevelName)
            {
                case "MainMenu":
                    CurrentGameScene = CurrentScene.MainMenu;

                    if (GameObject.Find("Music")) GameObject.Find("Music").GetComponent<AudioSource>().Play();

                    if (QualitySettings.vSyncCount != 0) vSyncEnabled = true;
                    if (modLoaderSettings.UseVsyncInMenu && !vSyncEnabled) QualitySettings.vSyncCount = 1;

                    if (mainMenuReturn)
                    {
                        mainMenuReturn = false;
                        loaderInitialized = false;
                        modUnloader.Reset();
                        unloading = true;
                    }
                    break;

                case "Intro":
                    CurrentGameScene = CurrentScene.NewGameIntro;

                    newGameStarted = true;
                    break;

                case "GAME":
                    CurrentGameScene = CurrentScene.Game;

                    if (modLoaderSettings.UseVsyncInMenu && !vSyncEnabled) QualitySettings.vSyncCount = 0;

                    mainMenuReturn = true;

                    SetupModMenuHandler();

                    StartCoroutine(LoadMods());
                    break;
            }
        }

        void Awake()
        {
            timer = new Stopwatch();

            if (GameObject.Find("Music") && Application.loadedLevelName == "MainMenu")
                GameObject.Find("Music").GetComponent<AudioSource>().Stop();

            CreateUnloader();

            LoadModLoaderUI();
            LoadModLoaderSettings();

            LoadedMods = new List<Mod>();
            ModMethods = new List<List<Mod>>() {
                new List<Mod>(), // 0 - OnGUI
                new List<Mod>(), // 1 - Update
                new List<Mod>(), // 2 - FixedUpdate
                new List<Mod>(), // 3 - PostLoad
                new List<Mod>(), // 4 - OnSave
                new List<Mod>(), // 5 - OnNewGame
                new List<Mod>(), // 6 - PreLoad
                new List<Mod>(), // 7 - OnMenuLoad
                new List<Mod>(), // 8 - MenuOnGUI
                new List<Mod>(), // 9 - MenuUpdate
                new List<Mod>(), // 10 - MenuFixedUpdate
            };

            ModConsole.Print($"MOD LOADER PRO <b>VERSION: {ModLoaderVersion}</b> READY!");

            LoadReferences();
            InitializeMods();
            SetupModList();

            ModConsole.Print($"<b>{LoadedMods.Count}</b> MOD(S) FOUND!");

            LoadModsSettings();
            CallOnMenuLoad();

            string[] methodNames = { "OnGUI", "Update", "FixedUpdate", "PostLoad", "OnSave", "OnNewGame", "PreLoad", "OnMenuLoad" };
            string modString = "";
            for (int i = 0; i < methodNames.Length; i++)
                modString += $"\n{ModMethods[i].Count} Mod(s) using {methodNames[i]}.{(ModMethods[i].Count > 0 ? "\n  " : "")}{string.Join("\n  ", ModMethods[i].Select(x => x.Name).ToArray())}";
            ModConsole.Print(modString);

            /*
                LOAD MOD UI
                LOAD MOD LOADER SETTINGS

                LOAD MOD DLL
                MODSETTINGS()
                LOAD MOD SETTINGS
            */
        }

        void CreateUnloader()
        {
            if (GameObject.Find("MSCUnloader") == null)
            {
                GameObject go = new GameObject("MSCUnloader");
                modUnloader = go.AddComponent<ModUnloader>();
                DontDestroyOnLoad(go);
            }
            else
                modUnloader = GameObject.Find("MSCUnloader").GetComponent<ModUnloader>();
        }

        void LoadModLoaderUI()
        {
            AssetBundle bundle = AssetBundle.CreateFromMemoryImmediate(Properties.Resources.mscloadercanvas);
            GameObject loadedObject = bundle.LoadAsset<GameObject>("MSCLoaderCanvas.prefab");
            ModUI.canvasGO = Instantiate(loadedObject);
            Destroy(loadedObject);
            DontDestroyOnLoad(ModUI.canvasGO);

            modUILoadScreen = ModUI.canvasGO.transform.Find("ModLoaderUI/ModLoadScreen").gameObject;
            modSceneLoadHandler = ModUI.canvasGO.GetComponent<UILoadHandler>();
            Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(x => !x.activeSelf && x.name == "Loading").AddComponent<UIMainMenuLoad>().loadHandler = modSceneLoadHandler;

            modLoaderSkin = bundle.LoadAsset<GUISkin>("ModLoaderSkin.guiskin");
            bundle.Unload(false);
        }

        void LoadModLoaderSettings()
        {
            modLoaderSettings = ModUI.canvasGO.GetComponentsInChildren<ModLoaderSettings>(true)[0];

            modLoaderSettings.disableSave = true;

            modLoaderSettings.Version = ModLoaderVersion;
            modLoaderSettings.SkipGameLauncher = MSCLoader.settings.SkipGameLauncher;
            modLoaderSettings.SkipSplashScreen = MSCLoader.settings.SkipSplashScreen;

            modLoaderSettings.UseVsyncInMenu = MSCLoader.settings.UseVsyncInMenu;
            modLoaderSettings.useVsyncInMenu.OnValueChanged.AddListener(delegate
            {
                if (!vSyncEnabled && CurrentGameScene == CurrentScene.MainMenu)
                    QualitySettings.vSyncCount = modLoaderSettings.UseVsyncInMenu ? 1 : 0;
            });

            modLoaderSettings.CheckUpdatesAutomatically = MSCLoader.settings.CheckUpdateAutomatically;

            modLoaderSettings.OpenConsoleKeyKeybind = MSCLoader.settings.OpenConsoleKey[0];
            modLoaderSettings.OpenConsoleKeyModifiers = MSCLoader.settings.OpenConsoleKey.Skip(1).ToArray();
            modLoaderSettings.openConsoleKey.bindPostfix = modLoaderSettings.SaveSettings;

            modLoaderSettings.ConsoleFontSize = MSCLoader.settings.ConsoleFontSize;
            modLoaderSettings.ConsoleAutoOpen = MSCLoader.settings.ConsoleAutoOpen;
            modLoaderSettings.ConsoleWindowHeight = MSCLoader.settings.ConsoleWindowHeight;
            modLoaderSettings.ConsoleWindowWidth = MSCLoader.settings.ConsoleWindowWidth;

            modLoaderSettings.disableSave = false;
        }
        
        void LoadReferences()
        {
            if (Directory.Exists(Path.Combine(ModsFolder, "References")))
                foreach (string file in Directory.GetFiles(Path.Combine(ModsFolder, "References"), "*.dll"))
                    Assembly.LoadFrom(file);
        }

        void InitializeMods()
        {
            foreach (string file in Directory.GetFiles(ModsFolder).Where(file => file.EndsWith(".dll")))
            {
                try
                {
                    Assembly modAssembly = Assembly.LoadFrom(file);
                    bool isMod = false;

                    AssemblyName[] list = modAssembly.GetReferencedAssemblies();
                    string fileString = File.ReadAllText(file);
                    if (fileString.Contains("RegistryKey") || fileString.Contains("Steamworks")) throw new FileLoadException();

                    Type modType = modAssembly.GetTypes().FirstOrDefault(type => typeof(Mod).IsAssignableFrom(type));
                    if (modType != null)
                    {
                        if (list.Any(assembly => assembly.Name == "Assembly-CSharp-firstpass") && (File.ReadAllText(file).Contains("Steamworks") || File.ReadAllText(file).Contains("GetSteamID")))
                            throw new Exception("Targeting forbidden reference");

                        string msVer = "";
                        AssemblyName mscLoader = list.FirstOrDefault(assembly => assembly.Name == "MSCLoader");
                        if (mscLoader != null)
                            msVer = $"{string.Join(".", mscLoader.Version.ToString().Split('.').Take(3).ToArray())}";

                        isMod = true;

                        Mod mod = (Mod)Activator.CreateInstance(modType);
                        // Check if mod already exists
                        if (!LoadedMods.Contains(mod))
                        {
                            mod.compiledVersion = msVer;
                            mod.fileName = file;
                            LoadedMods.Add(mod);

                            // FRED TWEAK
                            // Check if OnGUI, Update, FixedUpdate, PostLoad (and legacy), OnSave and OnNewGame are override methods and add them to list if so.
                            if (CheckEmptyMethod(mod, "OnGUI")) ModMethods[0].Add(mod);
                            if (CheckEmptyMethod(mod, "Update")) ModMethods[1].Add(mod);
                            if (CheckEmptyMethod(mod, "FixedUpdate")) ModMethods[2].Add(mod);
                            if (CheckEmptyMethod(mod, "PostLoad")) ModMethods[3].Add(mod);
                            if (CheckEmptyMethod(mod, "SecondPassOnLoad")) ModMethods[3].Add(mod);
                            if (CheckEmptyMethod(mod, "OnSave")) ModMethods[4].Add(mod);
                            if (CheckEmptyMethod(mod, "OnNewGame")) ModMethods[5].Add(mod);
                            if (CheckEmptyMethod(mod, "PreLoad")) ModMethods[6].Add(mod);
                            if (CheckEmptyMethod(mod, "OnMenuLoad")) ModMethods[7].Add(mod);
                            if (CheckEmptyMethod(mod, "MenuOnGUI")) ModMethods[8].Add(mod);
                            if (CheckEmptyMethod(mod, "MenuUpdate")) ModMethods[9].Add(mod);
                            if (CheckEmptyMethod(mod, "MenuFixedUpdate")) ModMethods[10].Add(mod);
                            // FRED TWEAK
                        }
                        else
                            ModConsole.Error($"<color=orange><b>Mod with ID: <color=red>{mod.ID}</color> already loaded:</color></b>");
                    }

                    if (!isMod)
                    {
                        ModConsole.Error($"<b>{Path.GetFileName(file)}</b> can't be loaded as a mod. Contact the mod author and ask for help.");
                    }
                }
                catch (Exception e)
                {
                    ModConsole.Error($"<b>{Path.GetFileName(file)}</b> can't be loaded as a mod. Contact the mod author and ask for help.");
                    ModConsole.Error(e.ToString());
                    System.Console.WriteLine(e);
                }
            }
        }

        bool CheckEmptyMethod(Mod mod, string methodName)
        {
            MethodInfo method = mod.GetType().GetMethod(methodName);
            return (method.IsVirtual && method.DeclaringType == mod.GetType() && method.GetMethodBody().GetILAsByteArray().Length > 2);
        }

        void SetupModList()
        {
            modContainer = ModUI.canvasGO.GetComponentInChildren<ModContainer>();
            for (int i = 0; i < LoadedMods.Count; i++)
            {
                Mod mod = LoadedMods[i];
                mod.modListElement = modContainer.CreateModListElement(mod);
                mod.modSettings = modContainer.CreateModSettingWindow(mod);
            }
        }

        void LoadModsSettings()
        {
            timer.Reset();
            timer.Start();

            ModConsole.Print("Calling ModSettings()..");
            foreach (Mod mod in LoadedMods)
            {
                try
                {
                    mod.modSettings.LoadSettings();
                    mod.ModSettings();
                }
                catch (Exception e)
                {
                    ModConsole.Error($"Settings error for mod <b>{mod.ID}</b>.\n{e.Message}");
                    ModConsole.Error(e.ToString());
                    Console.WriteLine(e);
                }
            }
            timer.Stop();
            ModConsole.Print($"ModSettings() Done ({timer.ElapsedMilliseconds}ms)..");

            timer.Reset();
            timer.Start();
            ModConsole.Print("Calling ModSettingsLoaded()..");
            foreach (Mod mod in LoadedMods)
            {
                try { mod.ModSettingsLoaded(); }
                catch (Exception e)
                {
                    ModConsole.Error($"Settings error for mod <b>{mod.ID}</b>.\n{e.Message}");
                    ModConsole.Error(e.ToString());
                    Console.WriteLine(e);
                }
            }
            timer.Stop();
            ModConsole.Print($"ModSettingsLoaded() Done ({timer.ElapsedMilliseconds}ms)..");
        }

        void CallOnMenuLoad()
        {
            if (ModMethods[7].Count > 0)
            {
                timer.Reset();
                timer.Start();
                ModConsole.Print("Calling OnMenuLoad()..");
                for (int i = 0; i < ModMethods[7].Count; i++)
                {
                    try { if (!ModMethods[7][i].isDisabled) ModMethods[7][i].OnMenuLoad(); }
                    catch (Exception e) { LogError(e, ModMethods[7][i]); }
                }
                timer.Stop();
                ModConsole.Print($"OnMenuLoad() Done ({timer.ElapsedMilliseconds}ms)..");
            }
            GameObject menuMethods = new GameObject("ModLoaderMenuMethods");

            if (ModMethods[8].Count > 0) menuMethods.AddComponent<ModMenuOnGUICall>().modLoader = this;
            if (ModMethods[9].Count > 0) menuMethods.AddComponent<ModMenuUpdateCall>().modLoader = this;
            if (ModMethods[10].Count > 0) menuMethods.AddComponent<ModMenuFixedUpdateCall>().modLoader = this;
        }

        void SetupModMenuHandler()
        {
            ModUI.canvasGO.transform.Find("ModMenuUIHandler").GetComponent<UIModMenuHandler>().Setup();
        }

        IEnumerator LoadMods()
        {
            modUILoadScreen.SetActive(true);

            ModConsole.Print("<color=#FFFF00>Loading mods...</color>");
            ModConsole.controller.AppendLogLine("<color=#505050ff>");

            if (newGameStarted && ModMethods[5].Count > 0)
            {
                ModConsole.Print("Calling OnNewGame()..");
                for (int i = 0; i < ModMethods[5].Count; i++)
                {
                    try { ModMethods[5][i].OnNewGame(); }
                    catch (Exception e) { LogError(e, ModMethods[5][i]); }
                }

                newGameStarted = false;

                yield return null;
            }

            if (ModMethods[6].Count > 0)
            {
                ModConsole.Print("Calling PreLoad()..");
                for (int i = 0; i < ModMethods[6].Count; i++)
                {
                    try { if (!ModMethods[6][i].isDisabled) ModMethods[6][i].PreLoad(); }
                    catch (Exception e) { LogError(e, ModMethods[6][i]); }
                }
                yield return null;
            }

            while (GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera") == null) yield return null;

            Stopwatch timer = new Stopwatch();
            timer.Start();

            yield return null;

            ModConsole.Print("Calling OnLoad()..");
            for (int i = 0; i < LoadedMods.Count; i++)
            {
                try { if (!LoadedMods[i].isDisabled) LoadedMods[i].OnLoad(); }
                catch (Exception e) { LogError(e, LoadedMods[i]); }
            }

            if (ModMethods[3].Count > 0)
            {
                yield return null;

                ModConsole.Print("Calling PostLoad()..");
                for (int i = 0; i < ModMethods[3].Count; i++)
                {
                    try { if (!ModMethods[3][i].isDisabled) ModMethods[3][i].PostLoad(); }
                    catch (Exception e) { LogError(e, ModMethods[3][i]); }
                }
            }

            if (ModMethods[4].Count > 0) FsmHook.FsmInject(GameObject.Find("ITEMS"), "Save game", ModOnSave);

            timer.Stop();

            ModConsole.controller.AppendLogLine("</color>");
            ModConsole.Print($"<color=#FFFF00>Loading Mods Done! ({timer.ElapsedMilliseconds}ms)</color>");

            if (ModMethods[0].Count > 0) gameObject.AddComponent<ModOnGUICall>().modLoader = this;
            if (ModMethods[1].Count > 0) gameObject.AddComponent<ModUpdateCall>().modLoader = this;
            if (ModMethods[2].Count > 0) gameObject.AddComponent<ModFixedUpdateCall>().modLoader = this;

            modUILoadScreen.SetActive(false);
        }

        void LogError(Exception e, Mod mod)
        {
            StackFrame frame = new StackTrace(e, true).GetFrame(0);

            ModConsole.Error($"<b>{mod.ID}</b>! <b>Details:</b>\n{e.Message} in <b>{frame.GetMethod()}</b>.");
            ModConsole.Error(e.ToString());
            System.Console.WriteLine(e);
        }

        internal void ModMenuOnGUI()
        {
            GUI.skin = modLoaderSkin;
            for (int i = 0; i < ModMethods[8].Count; i++)
            {
                try { if (!ModMethods[8][i].isDisabled) ModMethods[8][i].MenuOnGUI(); }
                catch (Exception e) { Console.WriteLine(e); }
            }
        }

        internal void ModMenuUpdate()
        {
            for (int i = 0; i < ModMethods[9].Count; i++)
            {
                try { if (!ModMethods[9][i].isDisabled) ModMethods[9][i].MenuUpdate(); }
                catch (Exception e) { Console.WriteLine(e); }
            }
        }

        internal void ModMenuFixedUpdate()
        {
            for (int i = 0; i < ModMethods[10].Count; i++)
            {
                try { if (!ModMethods[10][i].isDisabled) ModMethods[10][i].MenuFixedUpdate(); }
                catch (Exception e) { Console.WriteLine(e); }
            }
        }

        internal void ModOnGUI()
        {
            GUI.skin = modLoaderSkin;
            for (int i = 0; i < ModMethods[0].Count; i++)
            {
                try { if (!ModMethods[0][i].isDisabled) ModMethods[0][i].OnGUI(); }
                catch (Exception e) { Console.WriteLine(e); }
            }
        }

        internal void ModUpdate()
        {
            for (int i = 0; i < ModMethods[1].Count; i++)
            {
                try { if (!ModMethods[1][i].isDisabled) ModMethods[1][i].Update(); }
                catch (Exception e) { Console.WriteLine(e); }
            }
        }

        internal void ModFixedUpdate()
        {
            for (int i = 0; i < ModMethods[2].Count; i++)
            {
                try { if (!ModMethods[2][i].isDisabled) ModMethods[2][i].FixedUpdate(); }
                catch (Exception e) { Console.WriteLine(e); }
            }
        }

        internal void ModOnSave()
        {
            ModConsole.Print("Calling OnSave()..");
            for (int i = 0; i < ModMethods[4].Count; i++)
            {
                try { if (!ModMethods[4][i].isDisabled) ModMethods[4][i].PostLoad(); }
                catch (Exception e) { LogError(e, ModMethods[4][i]); }
            }
        }

        // LEGACY
        [Obsolete("Obsolete, does not do anything.")]
        public static bool CheckSteam() => true;
        internal static string steamID = "NOYOUDONT";
        public static readonly string MSCLoader_Ver = ModLoaderVersion;
        public static bool LogAllErrors = false;
        public static bool CheckIfExperimental()
        {
            try
            {
                return Steamworks.SteamApps.GetCurrentBetaName(out string Name, 128) && Name != "default_32bit";
            }
            catch
            {
                return false;
            }
        }
        [Obsolete("Deprecated, use GetModSettingsFolder() instead.")]
        public static string GetModConfigFolder(Mod mod) => GetModSettingsFolder(mod);
    }

    class ModMenuOnGUICall : MonoBehaviour
    {
        public ModLoader modLoader;
        void OnGUI() => modLoader.ModMenuOnGUI();
    }

    class ModMenuUpdateCall : MonoBehaviour
    {
        public ModLoader modLoader;
        void Update() => modLoader.ModMenuUpdate();
    }

    class ModMenuFixedUpdateCall : MonoBehaviour
    {
        public ModLoader modLoader;
        void FixedUpdate() => modLoader.ModMenuFixedUpdate();
    }

    class ModOnGUICall : MonoBehaviour
    {
        public ModLoader modLoader;
        void OnGUI() => modLoader.ModOnGUI();
    }

    class ModUpdateCall : MonoBehaviour
    {
        public ModLoader modLoader;
        void Update() => modLoader.ModUpdate();
    }

    class ModFixedUpdateCall : MonoBehaviour
    {
        public ModLoader modLoader;
        void FixedUpdate() => modLoader.ModFixedUpdate();
    }
}