﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System;
using MSCLoader.Helper;
using MSCLoader.NexusMods;

#pragma warning disable CS1591
namespace MSCLoader
{
    // Copyright (C) Konrad Figura 2021
    // This file is a part of MSCLoader Pro.
    // You cannot use it any other project.
    public class ModUpdater : MonoBehaviour
    {
        static ModUpdater instance;
        public static ModUpdater Instance => instance;

        public GameObject headerUpdateAllButton;
        public GameObject headerProgressBar;
        public Slider sliderProgressBar;
        public Text textProgressBar;
        public Text menuLabelUpdateText;

        const int MaxDots = 3;

        bool isBusy;
        public bool IsBusy => isBusy;

        internal static string UpdaterDirectory => Path.Combine(Directory.GetCurrentDirectory(), "ModUpdater");
        internal static string UpdaterPath => Path.Combine(UpdaterDirectory, "CoolUpdater.exe");
        string DownloadsDirectory => Path.Combine(UpdaterDirectory, "Downloads");
        const int TimeoutTime = 10; // in seconds.
        const int TimeoutTimeDownload = 60; // in seconds.

        bool autoUpdateChecked;

        ModUpdaterDatabase modUpdaterDatabase;

        const string ModLoaderApiUri = "https://api.github.com/repos/MSCLoaderPro/MSCModLoaderPro/releases";
        const string InstallerApiUri = "https://api.github.com/repos/MSCLoaderPro/docs/releases/latest";
        string modLoaderLatestVersion;
        bool modLoaderUpdateAvailable, installModLoaderUpdate;
        string TempPathModLoaderPro => Path.Combine(Path.GetTempPath(), "modloaderpro");
        const string InstallerName = "installer.exe";
        string InstallerPath => Path.Combine(TempPathModLoaderPro, InstallerName);

        public ModUpdater()
        {
            instance = this;
        }

        void Start()
        {
            modUpdaterDatabase = new ModUpdaterDatabase();

            // Populate list from the database.
            if (modUpdaterDatabase.GetAll().Count > 0)
            {
                foreach (var f in modUpdaterDatabase.GetAll())
                {
                    Mod mod = ModLoader.LoadedMods.FirstOrDefault(m => m.ID == f.Key);
                    if (mod != null)
                    {
                        mod.ModUpdateData = f.Value;

                        if (IsNewerVersionAvailable(mod.Version, mod.ModUpdateData.LatestVersion))
                        {
                            mod.ModUpdateData.UpdateStatus = UpdateStatus.Available;
                            mod.modListElement.ToggleUpdateButton(true);
                            headerUpdateAllButton.SetActive(true);
                            ModLoader.modContainer.UpdateModCountText();
                        }
                        else
                        {
                            mod.ModUpdateData.UpdateStatus = UpdateStatus.NotChecked;
                        }
                    }
                }

                StartCoroutine(NotifyUpdatesAvailable());
            }

            if (ShouldCheckForUpdates())
            {
                autoUpdateChecked = true;
                LookForUpdates();
            }
        }

        bool ShouldCheckForUpdates()
        {
            if (autoUpdateChecked)
            {
                return false;
            }

            DateTime now = DateTime.Now;
            DateTime lastCheck = ModLoader.modLoaderSettings.lastUpdateCheckDate;

            switch (ModLoader.modLoaderSettings.updateInterval.Value)
            {
                default:
                    return false;
                case 0: // Every launch
                    return true;
                case 1: // Daily
                    return now >= lastCheck.AddDays(1);
                case 2: // Weekly
                    return now >= lastCheck.AddDays(7);
                case 3: // Never
                    return false;
            }
        }

        IEnumerator currentSliderText;
        IEnumerator UpdateSliderText(string message, string finishedMessage)
        {
            menuLabelUpdateText.gameObject.SetActive(true);
            WaitForSeconds wait = new WaitForSeconds(0.25f);
            int numberOfDots = 0;
            while (isBusy)
            {
                string dots = new string('.', numberOfDots);
                textProgressBar.text = $"{dots}{message}{dots}";
                menuLabelUpdateText.text = $"{message}{dots}";
                numberOfDots++;
                if (numberOfDots > MaxDots)
                {
                    numberOfDots = 0;
                }
                yield return wait;
            }
            textProgressBar.text = finishedMessage;
            menuLabelUpdateText.text = finishedMessage;

            int updateCount = ModLoader.LoadedMods.Count(x => x.ModUpdateData.UpdateStatus == UpdateStatus.Available);
            if (updateCount > 0)
            {
                string updateMessage = updateCount > 1 ? $" THERE ARE {updateCount} MOD UPDATES AVAILABLE!" : $" THERE IS {updateCount} MOD UPDATE AVAILABLE!";
                menuLabelUpdateText.text += $"<color=#87f032>{updateMessage}</color>";
            }

            yield return new WaitForSeconds(5f);

            headerProgressBar.SetActive(false);
            menuLabelUpdateText.gameObject.SetActive(false);
        }

        void ClearSliderText()
        {
            headerProgressBar.SetActive(false);
            menuLabelUpdateText.gameObject.SetActive(false);
        }

        IEnumerator NotifyUpdatesAvailable()
        {
            int updateCount = ModLoader.LoadedMods.Count(x => x.ModUpdateData.UpdateStatus == UpdateStatus.Available);
            if (updateCount > 0)
            {
                menuLabelUpdateText.gameObject.SetActive(true);

                string updateMessage = updateCount > 1 ? $" THERE ARE {updateCount} MOD UPDATES AVAILABLE!" : $" THERE IS {updateCount} MOD UPDATE AVAILABLE!";
                menuLabelUpdateText.text = $"<color=#87f032>{updateMessage}</color>";

                yield return new WaitForSeconds(5f);
                if (!IsBusy) menuLabelUpdateText.gameObject.SetActive(false);
            }
        }

        #region Looking for updates
        /// <summary> Starts looking for the update of the specific mod. </summary>
        public void LookForUpdates()
        {
            if (IsBusy)
            {
                ModPrompt.CreatePrompt("MOD LOADER IS BUSY LOOKING FOR UPDATES.", "MOD UPDATER");
                return;
            }

            StartCoroutine(CheckModLoaderUpdate());
        }

        void StartLookingForUpdates()
        {
            if (!File.Exists(UpdaterPath))
            {
                throw new MissingComponentException("Updater component does not exist!");
            }

            StartCoroutine(CheckForModUpdates(ModLoader.LoadedMods.Where(x => !string.IsNullOrEmpty(x.UpdateLink))));
        }

        bool userAnswered, userAbort;

        /// <summary> Goes through all mods and checks if an update on GitHub or Nexus is available for them. </summary>
        IEnumerator CheckForModUpdates(IEnumerable<Mod> mods)
        {
            if (mods.Count() == 0)
            {
                isBusy = false;
                yield break;
            }

            if (currentSliderText != null) StopCoroutine(currentSliderText);

            if (!NexusSSO.Instance.IsValid)
            {
                ModPrompt prompt = ModPrompt.CreateYesNoPrompt("Looks like you're not logged into NexusMods.\n" +
                                                          "Some mods require NexusMods to be able to check for updates.\n\n" +
                                                          "Are you sure you want to continue?", "Mod Updater", () => { }, onNo: () => { userAbort = true; }, onPromptClose: () => { userAnswered = true; });

                while (!userAnswered)
                {
                    yield return null;
                }
                if (userAbort)
                {
                    isBusy = false;
                    ClearSliderText();
                    yield break;
                }
            }


            isBusy = true;

            // Disable the Update All button while checking for updates!
            headerUpdateAllButton.SetActive(false);

            ModLoader.modLoaderSettings.RefreshUpdateCheckTime();

            // Enable the progress bar.
            int i = 0;
            sliderProgressBar.value = i;
            headerProgressBar.SetActive(true);
            sliderProgressBar.maxValue = mods.Count();
            StartCoroutine(UpdateSliderText("CHECKING FOR UPDATES", "UPDATE CHECK COMPLETE!"));

            foreach (Mod mod in mods)
            {
                ModConsole.Log($"\nLooking for update of {mod.Name}");
                string url = mod.UpdateLink;

                i++;
                sliderProgressBar.value = i - 1;

                // Formatting the link.
                if (url.Contains("github.com"))
                {
                    // If is not direct api.github.com link, modify it so it matches it correctly.
                    if (!url.Contains("api."))
                    {
                        url = url.Replace("https://", "").Replace("www.", "").Replace("github.com/", "");
                        url = "https://api.github.com/repos/" + url;
                    }

                    if (!url.EndsWith("/releases/latest"))
                    {
                        url += "/releases/latest";
                    }
                    else if (!url.EndsWith("releases/latest"))
                    {
                        url += "releases/latest";
                    }

                    ModConsole.Log($"URL: {url}");

                    ModConsole.Log($"Starting checking for update process...");
                    Process p = GetMetaFile(url);
                    string output = "";
                    int downloadTime = 0;
                    while (!p.HasExited)
                    {
                        downloadTime++;
                        if (downloadTime > TimeoutTime)
                        {
                            ModConsole.LogError($"Mod Updater: Getting metadata of {mod.ID} timed-out.");
                            p.Kill();
                            break;
                        }

                        yield return new WaitForSeconds(1);
                    }

                    p.Close();
                    ModConsole.Log($"Mod Updater: {mod.ID} - pulling metadata succeeded!");

                    output = lastDataOut;

                    mod.ModUpdateData = new ModUpdateData();

                    // Reading the metadata file info that we want.
                    try
                    {
                        if (url.Contains("github.com"))
                        {
                            if (output.Contains("\"message\": \"Not Found\"") || output.Contains("(404) Not Found"))
                            {
                                ModConsole.LogError($"Mod Updater: Mod {mod.ID}'s GitHub repository returned \"Not found\" status.");
                                continue;
                            }
                            string[] outputArray = ReadMetadataToArray();

                            bool foundProBuild = false;
                            foreach (string s in outputArray)
                            {
                                // Finding tag of the latest release, this servers as latest version number.
                                if (s.Contains("\"tag_name\""))
                                {
                                    mod.ModUpdateData.LatestVersion = s.Split(':')[1].Replace("\"", "");
                                }

                                if (s.Contains("\"browser_download_url\"") && s.Contains(".zip"))
                                {
                                    string[] separated = s.Split(':');
                                    mod.ModUpdateData.ZipUrl = (separated[1] + ":" + separated[2]).Replace("\"", "").Replace("}", "").Replace("]", "");

                                    // If we are 100% positive that Mod Loader Pro found the mod version for Mod Loader Pro,
                                    // only then leave the loop.
                                    // Otherwise, keep looking for pro build.
                                    if (mod.ModUpdateData.ZipUrl.ToLower().EndsWith(".pro.zip"))
                                    {
                                        foundProBuild = true;
                                    }
                                }

                                // Breaking out of the loop, if we found all that we've been looking for.
                                if (!string.IsNullOrEmpty(mod.ModUpdateData.ZipUrl) && !string.IsNullOrEmpty(mod.ModUpdateData.LatestVersion) || foundProBuild)
                                {
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ModConsole.LogError($"An error has occured while reading the metadata of {mod.Name}:\n\n{ex}");
                    }
                }
                else if (url.Contains("nexusmods.com"))
                {
                    //SAMPLE: https://www.nexusmods.com/mysummercar/mods/146
                    // First we need mod ID.
                    if (!NexusSSO.Instance.IsValid)
                    {
                        ModConsole.LogError("Mods that use NexusMods for its updates require user authentication API key. Please provide one first.");
                    }

                    string modID = url.Split('/').Last();
                    string mainModInfo = $"https://api.nexusmods.com/v1/games/mysummercar/mods/{modID}.json";

                    if (string.IsNullOrEmpty(NexusSSO.Instance.ApiKey))
                    {
                        ModConsole.LogError("NexusMods API key is empty.");
                        continue;
                    }

                    // Now we are getting version info.
                    Process modInfoProcess = GetMetaFile(mainModInfo, NexusSSO.Instance.ApiKey);
                    mod.ModUpdateData = new ModUpdateData();
                    int downloadTime = 0;
                    while (!modInfoProcess.HasExited)
                    {
                        downloadTime++;
                        if (downloadTime > TimeoutTime)
                        {
                            ModConsole.LogError($"Mod Updater: Getting metadata of {mod.ID} timed-out.");
                            modInfoProcess.Kill();
                            break;
                        }
                        yield return new WaitForSeconds(1);
                    }
                    if (lastDataOut.Contains("\"message\": \"Not Found\"") || lastDataOut.Contains("(404) Not Found"))
                    {
                        ModConsole.LogError($"Mod Updater: NexusMods returned \"Not Found\" status for mod {mod.ID}.");
                        continue;
                    }
                    string[] output = ReadMetadataToArray();
                    foreach (string s in output)
                    {

                        if (s.Contains("version"))
                        {
                            mod.ModUpdateData.LatestVersion = s.Split(':')[1].Replace("\"", "");
                            break;
                        }
                    }

                    // Retrieve latest file version.
                    if (NexusSSO.Instance.IsPremium)
                    {
                        string modFiles = $"https://api.nexusmods.com/v1/games/mysummercar/mods/{modID}/files.json?category=main";
                        Process modFilesProcess = GetMetaFile(modFiles, NexusSSO.Instance.ApiKey);
                        downloadTime = 0;
                        while (!modFilesProcess.HasExited)
                        {
                            downloadTime++;
                            if (downloadTime > TimeoutTime)
                            {
                                ModConsole.LogError($"Mod Updater: Getting list of files of {mod.ID} timed-out.");
                                modFilesProcess.Kill();
                                break;
                            }
                            yield return new WaitForSeconds(1);
                        }
                        output = ReadMetadataToArray();
                        string lastFileID = "";
                        bool isZipFound = false;
                        bool isProFileFound = false;

                        foreach (string s in output)
                        {
                            if (s.Contains("file_id"))
                            {
                                lastFileID = s.Split(':')[1].Trim();
                            }

                            if (s.Contains("file_name") && s.Contains(".pro"))
                            {
                                isProFileFound = true;
                            }

                            if (s.Contains("file_name") && s.Contains(".zip"))
                            {
                                isZipFound = true;
                            }

                            // We got the file_id of latest version. We can break out of the loop!
                            if (s.Contains("],") && s.Contains(mod.ModUpdateData.LatestVersion) || isProFileFound && isZipFound)
                            {
                                break;
                            }
                        }

                        // Get download link to mod file, woo!
                        if (!string.IsNullOrEmpty(lastFileID))
                        {
                            string requestDownloads = $"https://api.nexusmods.com/v1/games/mysummercar/mods/{modID}/files/{lastFileID}/download_link.json";
                            Process fileProcess = GetMetaFile(requestDownloads, NexusSSO.Instance.ApiKey);
                            downloadTime = 0;
                            while (!fileProcess.HasExited)
                            {
                                downloadTime++;
                                if (downloadTime > TimeoutTime)
                                {
                                    ModConsole.LogError($"Mod Updater: Getting downlaod link of {mod.ID} timed-out.");
                                    fileProcess.Kill();
                                    break;
                                }
                                yield return new WaitForSeconds(1);
                            }
                            output = ReadMetadataToArray();
                            foreach (string s in output)
                            {
                                if (s.Contains("URI"))
                                {
                                    string[] separated = s.Split(':');
                                    mod.ModUpdateData.ZipUrl = (separated[1] + ":" + separated[2]).Replace("\"", "").Replace("}", "").Replace("]", "").Replace("\n", "").Replace(@"\u0026", "&");
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        mod.ModUpdateData.ZipUrl = mod.UpdateLink;
                    }
                }

                if (IsNewerVersionAvailable(mod.Version, mod.ModUpdateData.LatestVersion))
                {
                    mod.ModUpdateData.UpdateStatus = UpdateStatus.Available;
                    ModConsole.Log($"<color=green>Mod Updater: {mod.ID} has an update available!</color>");
                    mod.modListElement.ToggleUpdateButton(true);
                }
                else
                {
                    mod.ModUpdateData.UpdateStatus = UpdateStatus.NotAvailable;
                    ModConsole.Log($"<color=green>Mod Updater: {mod.ID} is up-to-date!</color>");
                    mod.modListElement.ToggleUpdateButton(false);
                }

                ModConsole.Log($"Mod Updater: {mod.ID} Latest version: {mod.ModUpdateData.LatestVersion}");
                ModConsole.Log($"Mod Updater: {mod.ID} Your version:   {mod.Version}");
                ModConsole.Log($"Mod Updater: {mod.ID} Link: {mod.ModUpdateData.ZipUrl}");
            }

            sliderProgressBar.value = sliderProgressBar.maxValue;

            // SHOW THE UPDATE ALL BUTTON THEN UPDATE THE MOD COUNT LABEL TO REFLECT HOW MANY MODS HAVE UPDATES AVAILABLE!
            headerUpdateAllButton.SetActive(mods.Any(x => x.ModUpdateData.UpdateStatus == UpdateStatus.Available));
            ModLoader.modContainer.UpdateModCountText();

            IEnumerable<Mod> modsWithUpdates = mods.Where(x => x.ModUpdateData.UpdateStatus == UpdateStatus.Available);
            if (modsWithUpdates.Count() > 0)
            {
                switch (ModLoader.modLoaderSettings.UpdateMode)
                {
                    case 1: // Notify only
                        string modNames = "";
                        foreach (var mod in modsWithUpdates)
                            modNames += $"{mod.Name}, ";
                        modNames = modNames.Remove(modNames.Length - 2, 1);
                        ModPrompt prompt = ModPrompt.CreateCustomPrompt();
                        prompt.Text = $"MOD UPDATE IS AVAILABLE FOR THE FOLLOWING MODS:\n\n<color=yellow>{modNames}</color>\n\n" +
                                      $"YOU CAN USE \"UPDATE ALL MODS\" BUTTON TO QUICKLY UPDATE THEM.";
                        prompt.Title = "MOD UPDATER";
                        prompt.AddButton("UPDATE ALL MODS", () => UpdateAll());
                        prompt.AddButton("CLOSE", null);
                        break;
                    case 2: // Download
                        UpdateAll();
                        break;
                }
            }

            isBusy = false;
        }

        static Process GetMetaFile(params string[] args)
        {
            if (!File.Exists(UpdaterPath))
            {
                throw new Exception("Missing CoolUpdater!");
            }

            Process p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = UpdaterPath,
                    Arguments = "get-metafile " + string.Join(" ", args),
                    WorkingDirectory = UpdaterDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            lastDataOut = "";
            p.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
            p.ErrorDataReceived += new DataReceivedEventHandler(ErrorHandler);

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        private static void ErrorHandler(object sender, DataReceivedEventArgs e)
        {
            UnityEngine.Debug.Log(e.Data);
            lastDataOut += e.Data + "\n";
        }

        static string lastDataOut;
        static void OutputHandler(object sendingProcess, DataReceivedEventArgs e)
        {
            lastDataOut += e.Data + "\n";
        }

        internal static string[] ReadMetadataToArray()
        {
            return string.IsNullOrEmpty(lastDataOut) ? new string[] { "" } : lastDataOut.Split(',');
        }

        bool IsNewerVersionAvailable(string currentVersion, string serverVersion)
        {
            // Messy af, but reliably compares version numbers of the currently installed mod,
            // and the version that is available on the server.

            // The best thing is it won't show an outdated mod info, 
            // if the local mod version is newer than the publicly available one.

            // First we convert string version to individual integers.
            try
            {
                int modMajor, modMinor, modRevision = 0;
                string[] modVersionSpliited = currentVersion.Split('.');
                modMajor = int.Parse(modVersionSpliited[0]);
                modMinor = int.Parse(modVersionSpliited[1]);
                if (modVersionSpliited.Length == 3)
                    modRevision = int.Parse(modVersionSpliited[2]);

                // Same for the newest server version.
                int major, minor, revision = 0;
                string[] verSplitted = serverVersion.Split('.');
                major = int.Parse(verSplitted[0]);
                minor = int.Parse(verSplitted[1]);
                if (verSplitted.Length == 3)
                    revision = int.Parse(verSplitted[2]);

                // And now we finally compare numbers.
                bool isOutdated = false;
                if (major > modMajor)
                {
                    isOutdated = true;
                }
                else
                {
                    if (minor > modMinor && major == modMajor)
                    {
                        isOutdated = true;
                    }
                    else
                    {
                        if (revision > modRevision && minor == modMinor && major == modMajor)
                        {
                            isOutdated = true;
                        }
                    }
                }

                return isOutdated;
            }
            catch
            {
                //ModConsole.LogError($"Mod Updater: Incorrectly formated version tag: {currentVersion} | {serverVersion}");
                //return false;

                // Accurate parsing failed. Try simple comparison instead.
                return currentVersion.ToLower() != serverVersion.ToLower();
            }
        }
        #endregion
        #region Downloading the updates
        List<Mod> updateDownloadQueue = new List<Mod>();
        int currentModInQueue;

        public void DownloadModUpdate(Mod mod)
        {
            if (!File.Exists(UpdaterPath))
            {
                throw new MissingComponentException("Updater component is missing!");
            }

            if (mod.UpdateLink.Contains("nexusmods.com"))
            {
                if (!NexusSSO.Instance.IsPremium)
                {
                    ModPrompt.CreateYesNoPrompt($"MOD <color=yellow>{mod.Name}</color> USES NEXUSMODS FOR UPDATE DOWNLOADS. " +
                                            $"UNFORTUNATELY, DUE TO NEXUSMODS POLICY, ONLY PREMIUM USERS CAN USE AUTO UPDATE FEATURE.\n\n" +
                                            $"YOUR VERSION IS <color=yellow>{mod.Version}</color> AND THE NEWEST VERSION IS <color=yellow>{mod.ModUpdateData.LatestVersion}</color>.\n\n" +
                                            $"WOULD YOU LIKE TO OPEN MOD PAGE TO DOWNLOAD THE UPDATE MANUALLY?\n\n" +
                                            $"<color=red>WARNING: THIS WILL OPEN YOUR DEFAULT WEB BROWSER.</color>"
                                            , "MOD UPDATER", () => ModHelper.OpenWebsite(mod.UpdateLink));
                    return;
                }
            }

            if (ModLoader.modLoaderSettings.AskBeforeDownload)
            {
                ModPrompt prompt = ModPrompt.CreateCustomPrompt();
                prompt.Text = $"ARE YOU SURE YOU WANT TO DOWNLOAD UPATE FOR MOD:\n\n<color=yellow>\"{mod.Name}\"</color>\n\n" +
                              $"YOUR VERSION IS {mod.Version} AND THE NEWEST VERSION IS {mod.ModUpdateData.LatestVersion}.";
                prompt.Title = "MOD UPDATER";
                prompt.AddButton("YES", () => AddModToDownloadQueue(mod));
                prompt.AddButton("YES, AND DON'T ASK AGAIN", () => { ModLoader.modLoaderSettings.AskBeforeDownload = false; AddModToDownloadQueue(mod); });
                prompt.AddButton("NO", null);
            }
            else
            {
                AddModToDownloadQueue(mod);
            }
        }

        void AddModToDownloadQueue(Mod mod)
        {
            if (!updateDownloadQueue.Contains(mod))
            {
                updateDownloadQueue.Add(mod);
                sliderProgressBar.maxValue = updateDownloadQueue.Count();
            }

            StartDownload();
        }

        /// <summary>
        /// Populates the queue list with all mods with the UpdateStatus.Available state.
        /// </summary>
        public void UpdateAll()
        {
            if (isBusy) return;

            Mod[] mods = ModLoader.LoadedMods.Where(x => x.ModUpdateData.UpdateStatus == UpdateStatus.Available).ToArray();
            foreach (Mod mod in mods)
            {
                if (!updateDownloadQueue.Contains(mod))
                {
                    updateDownloadQueue.Add(mod);
                }
            }

            StartDownload();
        }

        void StartDownload()
        {
            if (isBusy)
            {
                return;
            }

            if (currentDownloadRoutine != null)
            {
                return;
            }
            currentDownloadRoutine = DownloadModUpdateRoutine();
            StartCoroutine(currentDownloadRoutine);
        }

        private IEnumerator currentDownloadRoutine;
        IEnumerator DownloadModUpdateRoutine()
        {
            isBusy = true;

            int i = 0;
            sliderProgressBar.value = i;
            headerProgressBar.SetActive(true);
            StartCoroutine(UpdateSliderText("DOWNLOADING UPDATES", "DOWNLOADS COMPLETE!"));

            for (; currentModInQueue < updateDownloadQueue.Count(); currentModInQueue++)
            {
                Mod mod = updateDownloadQueue[currentModInQueue];
                ModConsole.Log($"\nMod Updater: Downloading mod update of {mod.ID}...");

                if (!Directory.Exists(DownloadsDirectory))
                {
                    Directory.CreateDirectory(DownloadsDirectory);
                }

                // If a ZipUrl couldn't be obtained, or the link doesn't end with .ZIP file, we open the Mod.UpdateLink website.
                // We are also assuming that mod has been updated by the user.
                if (string.IsNullOrEmpty(mod.ModUpdateData.ZipUrl) || !mod.ModUpdateData.ZipUrl.Contains(".zip"))
                {
                    Process.Start(mod.UpdateLink);
                    mod.ModUpdateData.UpdateStatus = UpdateStatus.Downloaded;
                    continue;
                }

                string downloadToPath = Path.Combine(DownloadsDirectory, $"{mod.ID}.zip");
                string args = $"get-file \"{mod.ModUpdateData.ZipUrl}\" \"{downloadToPath}\"";
                if (mod.ModUpdateData.ZipUrl.Contains("nexusmods.com"))
                {
                    args += $" \"{NexusSSO.Instance.ApiKey}\"";
                }

                Process p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = UpdaterPath,
                        Arguments = args,
                        WorkingDirectory = UpdaterDirectory,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                p.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
                p.ErrorDataReceived += new DataReceivedEventHandler(ErrorHandler);

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                int downloadTime = 0;
                while (!p.HasExited)
                {
                    downloadTime++;
                    if (downloadTime > TimeoutTimeDownload)
                    {
                        ModConsole.LogError($"Mod Update Check for {mod.ID} timed-out.");
                        p.Kill();
                        break;
                    }

                    yield return new WaitForSeconds(1);
                }

                if (File.Exists(downloadToPath))
                {
                    ModConsole.Log($"Mod Updater: Update downloading for {mod.ID} completed!");
                    mod.ModUpdateData.UpdateStatus = UpdateStatus.Downloaded;
                }
                else
                {
                    ModConsole.Log($"<color=red>Mod Updater: Update downloading for {mod.ID} failed.</color>");
                }
                i++;
                sliderProgressBar.value = i;
            }

            currentDownloadRoutine = null;
            isBusy = false;

            // Asking user if he wants to update now or later.
            int downloadedUpdates = ModLoader.LoadedMods.Where(x => x.ModUpdateData.UpdateStatus == UpdateStatus.Downloaded).Count();
            if (downloadedUpdates > 0)
            {
                ModPrompt.CreateYesNoPrompt($"THERE {(downloadedUpdates > 1 ? "ARE" : "IS")} <color=yellow>{downloadedUpdates}</color> MOD UPDATE{(downloadedUpdates > 1 ? "S" : "")} READY TO BE INSTALLED.\n\n" +
                                        $"WOULD YOU LIKE TO INSTALL THEM NOW?\n\n" +
                                        $"<color=red>WARNING: THIS WILL CLOSE YOUR GAME, AND ALL UNSAVED PROGRESS WILL BE LOST!</color>",
                                        "MOD UPDATER", () => { waitForInstall = true; Application.Quit(); }, null, () => { waitForInstall = true; });
            }
        }
        #endregion
        #region Waiting for install
        bool waitForInstall;
        // Unity function: https://docs.unity3d.com/ScriptReference/MonoBehaviour.OnApplicationQuit.html

        void StartInstaller()
        {
            string pathToGame = Path.GetFullPath(ModLoader.ModsFolder).Replace("\\" + MSCLoader.settings.ModsFolderPath, "").Replace(" ", "%20");

            Process p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C \"\" {InstallerName} fast-install {pathToGame}",
                    WorkingDirectory = TempPathModLoaderPro,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            p.Start();
            ModPrompt.CreatePrompt(pathToGame);
        }

        void OnApplicationQuit()
        {
            modUpdaterDatabase.Save();

            // Mod Loader update has a priority over mods.
            if (installModLoaderUpdate)
            {
                StartInstaller();
                return;
            }

            if (waitForInstall)
            {
                Process p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/C \"\" CoolUpdater.exe update-all {Path.GetFullPath(ModLoader.ModsFolder).Replace(" ", "%20")}",
                        WorkingDirectory = UpdaterDirectory,
                        UseShellExecute = true
                    }
                };

                p.Start();
            }
        }
        #endregion

        #region Mod Loader Update Check
        IEnumerator CheckModLoaderUpdate()
        {
            currentSliderText = UpdateSliderText("COMMUNICATING WITH NEXUSMODS", "");
            StartCoroutine(currentSliderText);
            int waitTime = 0;
            while (!NexusSSO.Instance.IsReady)
            {
                waitTime++;
                if (waitTime > 20)
                    break;

                yield return new WaitForSeconds(1);
            }
            StopCoroutine(currentSliderText);

            isBusy = true;

            currentSliderText = UpdateSliderText("LOOKING FOR MOD LOADER PRO UPDATES", "CHECKING FOR MOD LOADER UPDATE FINISHED!");
            StartCoroutine(currentSliderText);
            ModConsole.Log($"Looking for Mod Loader Pro updates...");
            Process p = GetMetaFile(ModLoaderApiUri);
            int downloadTime = 0;
            while (!p.HasExited)
            {
                downloadTime++;
                if (downloadTime > TimeoutTime)
                {
                    ModConsole.LogError($"Mod Updater: Getting metadata of Mod Loader Pro timed-out.");
                    isBusy = false;
                    if (p != null) p.Close();
                    yield break;
                }

                yield return new WaitForSeconds(1);
            }

            p.Close();
            ModConsole.Log($"Mod Updater: Mod Loader Pro pulling metadata succeeded!");

            string output = lastDataOut.Replace(",\"", ",\n\"").Replace(":{", ":\n{\n").Replace("},", "\n},").Replace(":[{", ":[{\n").Replace("}],", "\n}],");
            foreach (string s in output.Split('\n'))
            {
                // Finding tag of the latest release, this servers as latest version number.
                if (s.Contains("\"tag_name\""))
                {
                    modLoaderLatestVersion = s.Split(':')[1].Replace(",", "").Replace("\"", "").Trim();
                    break;
                }
            }

            bool isRemoteRC, isLocalRC = false;
            isRemoteRC = modLoaderLatestVersion.Contains("-RC");
            isLocalRC = ModLoader.Version.Contains("-RC");

            string modLoaderLatestDisplay = modLoaderLatestVersion;
            if (isRemoteRC)
                modLoaderLatestVersion = modLoaderLatestVersion.Replace("-RC", ".");

            string localVersion = ModLoader.Version;
            if (isLocalRC)
                localVersion = localVersion.Replace("-RC", ".");

            modLoaderUpdateAvailable = IsNewerVersionAvailable(localVersion, modLoaderLatestVersion);
            isBusy = false;

            if (!isRemoteRC && isLocalRC)
            {
                modLoaderUpdateAvailable = true;
            }

            if (modLoaderUpdateAvailable)
            {
                ModPrompt.CreateYesNoPrompt($"Mod Loader Pro update is available to download!\n\n" +
                    $"Your version is <color=yellow>{ModLoader.Version}</color> and the newest available is <color=yellow>{modLoaderLatestDisplay}</color>.\n\n" +
                    $"Would you like to download it now?", "Mod Loader Update Available!", DownloadModLoaderUpdate, () => StartLookingForUpdates());
            }
            else
            {
                StartLookingForUpdates();
            }

        }

        void DownloadModLoaderUpdate()
        {
            StartCoroutine(GetModLoaderInstaller());
        }

        IEnumerator GetModLoaderInstaller()
        {
            isBusy = true;
            StartCoroutine(UpdateSliderText("DOWNLOADING MOD LOADER PRO INSTALLER", "MOD LOADER PRO INSTALLER HAS BEEN DOWNLOADED!"));

            // get metadata of installer first
            string installerUri = "";

            Process p = GetMetaFile(InstallerApiUri);
            int downloadTime = 0;
            while (!p.HasExited)
            {
                downloadTime++;
                if (downloadTime > TimeoutTime)
                {
                    ModConsole.LogError($"Mod Updater: Getting metadata of Mod Loader Pro timed-out.");
                    isBusy = false;
                    if (p != null) p.Kill();
                    yield break;
                }

                yield return new WaitForSeconds(1);
            }

            p.Close();
            ModConsole.Log($"Mod Updater: Mod Loader Pro pulling metadata succeeded!");

            if (!Directory.Exists(TempPathModLoaderPro))
            {
                Directory.CreateDirectory(TempPathModLoaderPro);
            }

            string output = lastDataOut.Replace(",\"", ",\n\"").Replace(":{", ":\n{\n").Replace("},", "\n},").Replace(":[{", ":[{\n").Replace("}],", "\n}],");
            foreach (string s in output.Split('\n'))
            {
                // Finding tag of the latest release, this servers as latest version number.
                if (s.Contains("\"browser_download_url\"") && s.Contains(".exe"))
                {
                    string[] separated = s.Split(':');
                    installerUri = (separated[1] + ":" + separated[2]).Replace("\"", "").Replace("}", "").Replace("]", "");
                    break;
                }
            }
            string args = $"get-file \"{installerUri}\" \"{InstallerPath}\"";

            p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = UpdaterPath,
                    Arguments = args,
                    WorkingDirectory = UpdaterDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            p.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
            p.ErrorDataReceived += new DataReceivedEventHandler(ErrorHandler);

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            downloadTime = 0;
            while (!p.HasExited)
            {
                downloadTime++;
                if (downloadTime > TimeoutTimeDownload)
                {
                    ModConsole.LogError($"Downloading Installer timed-out.");
                    p.Kill();
                    break;
                }

                yield return new WaitForSeconds(1);
            }

            installModLoaderUpdate = true;

            if (File.Exists(InstallerPath))
            {
                ModPrompt.CreateYesNoPrompt("Mod Loader Pro will update after you quit the game. Would you like to do that now?", "Mod Loader Pro Update is ready!", () => { Application.Quit(); });
            }

            isBusy = false;
        }
        #endregion
    }

    enum UpdateStatus { NotChecked, NotAvailable, Available, Downloaded }

    /// <summary> Stores the info about mod update found. </summary>
    internal struct ModUpdateData
    {
        public string ZipUrl;
        public string LatestVersion;
        public UpdateStatus UpdateStatus;
    }

    internal class ModUpdaterDatabase
    {
        // Because of some weird conflict between Newtonsoft.Json.Linq and System.Linq conflict,
        // we are forced to use a custom database solution.

        string DatabaseFile = Path.Combine(ModUpdater.UpdaterDirectory, "Updater.txt");

        Dictionary<string, ModUpdateData> modUpdateData;

        public ModUpdaterDatabase()
        {
            if (!File.Exists(DatabaseFile))
            {
                File.Create(DatabaseFile);
            }

            modUpdateData = new Dictionary<string, ModUpdateData>();

            string[] fileContent = File.ReadAllLines(DatabaseFile);
            foreach (var s in fileContent)
            {
                if (string.IsNullOrEmpty(s))
                {
                    continue;
                }

                try
                {
                    string id, url, latest = "";
                    string[] spliitted = s.Split(',');
                    id = spliitted[0];
                    url = spliitted[1];
                    latest = spliitted[2];

                    ModUpdateData data = new ModUpdateData
                    {
                        ZipUrl = url,
                        LatestVersion = latest
                    };

                    modUpdateData.Add(id, data);
                }
                catch
                {
                    continue;
                }
            }
        }

        internal void Save()
        {
            IEnumerable<Mod> mods = ModLoader.LoadedMods.Where(m => m.ModUpdateData.UpdateStatus == UpdateStatus.Available);
            string output = "";
            foreach (Mod mod in mods)
            {
                string updateLink = string.IsNullOrEmpty(mod.ModUpdateData.ZipUrl) ? mod.UpdateLink : mod.ModUpdateData.ZipUrl;
                output += $"{mod.ID},{updateLink},{mod.ModUpdateData.LatestVersion}\n";
            }

            if (File.Exists(DatabaseFile))
                File.Delete(DatabaseFile);

            File.WriteAllText(DatabaseFile, output);
        }

        internal ModUpdateData Get(Mod mod)
        {
            return modUpdateData.FirstOrDefault(m => m.Key == mod.ID).Value;
        }

        internal Dictionary<string, ModUpdateData> GetAll()
        {
            return modUpdateData;
        }
    }
}