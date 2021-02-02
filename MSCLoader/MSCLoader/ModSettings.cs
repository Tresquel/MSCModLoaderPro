﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MSCLoader
{
    public class ModContainer : MonoBehaviour
    {
        public ModLoader modLoader;

        public Dictionary<Mod, ModSettings> settingsDictionary = new Dictionary<Mod, ModSettings>();
        public Dictionary<Mod, ModListElement> modListDictionary = new Dictionary<Mod, ModListElement>();

        public Text modCountText;

        public ModLoaderSettings modLoaderSettings;

        public Transform modList;
        public GameObject modListElementPrefab;

        public Transform settingsList;
        public GameObject settingsWindowPrefab;

        public ModListElement CreateModListElement(Mod mod)
        {
            ModListElement modListElement = Instantiate(modListElementPrefab).GetComponent<ModListElement>();
            modListElement.modContainer = this;
            modListElement.mod = mod;
            modListElement.ID = mod.ID;
            modListElement.Name = mod.Name;
            modListElement.Author = mod.Author;
            modListElement.Version = mod.Version;

            if (mod.Icon != null)
            {
                Texture2D iconTexture = new Texture2D(1, 1);
                iconTexture.LoadImage(mod.Icon);

                modListElement.SetModIcon(iconTexture);
            }
            //else if (!string.IsNullOrEmpty(mod.IconName))
            //{
            //    Texture2D iconTexture = new Texture2D(1, 1);
            //    iconTexture.LoadImage(GetIcon(mod, mod.IconName));
            //
            //    modListElement.SetModIcon(iconTexture);
            //}

            modListElement.transform.SetParent(modList, false);
            modListDictionary.Add(mod, modListElement);

            modListElement.gameObject.SetActive(true);

            return modListElement;
        }

        public ModSettings CreateModSettingWindow(Mod mod)
        {
            ModSettings modSettings = Instantiate(settingsWindowPrefab).GetComponent<ModSettings>();
            modSettings.modContainer = this;
            modSettings.mod = mod;
            modSettings.Name = mod.Name;
            modSettings.ID = mod.ID;
            modSettings.Description = mod.Description;

            modSettings.transform.SetParent(settingsList, false);
            RectTransform rect = modSettings.GetComponent<RectTransform>();
            rect.offsetMin = new Vector2(0, 0);
            rect.offsetMax = new Vector2(0, 0);

            mod.modListElement.modSettings = modSettings;
            mod.modListElement.ToggleSettingsOff();

            settingsDictionary.Add(mod, modSettings);

            return modSettings;
        }

        public void UpdateModCountText()
        {
            modCountText.text = $"{ModLoader.LoadedMods.Count} MODS";
            if (ModLoader.LoadedMods.Any(mod => mod.isDisabled)) modCountText.text += $", {ModLoader.LoadedMods.Count(mod => mod.isDisabled)} DISABLED.";
        }

        // NOT WORKING BECAUSE OF UNITY SYSTEM.DRAWING
        public byte[] GetIcon(Mod mod, string name)
        {
            //https://stackoverflow.com/a/9901769
            System.Reflection.Assembly assembly = mod.GetType().Assembly;

            string resourceName = assembly.GetManifestResourceNames().FirstOrDefault(x => x.Contains(name));

            ModConsole.Log(resourceName);

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, (int)stream.Length);
                return buffer;
            }
        }
    }

    public class ModListElement : MonoBehaviour
    {
        public ModContainer modContainer;

        public Mod mod;
        public ModSettings modSettings;

        public Toggle modToggle, modSettingsToggle;

        public Text nameText, authorText, versionText;
        public RawImage iconImage;

        public string ID { get => gameObject.name; set => gameObject.name = value; }
        public string Name { get => nameText.text; set => nameText.text = value; }
        public string Author { get => authorText.text; set => authorText.text = $"AUTHOR{(value.Contains(",") || value.Contains("&") ? "S" : "")}: {value}"; }
        public string Version { get => versionText.text; set => versionText.text = $"VERSION: {value}"; }

        public void SetModIcon(Texture2D icon)
        {
            iconImage.texture = icon;
        }

        bool suspendAction = false;
        public void ToggleSettingsActive()
        {
            if (suspendAction) return;

            modContainer.modLoaderSettings.ToggleMenuOff();
            foreach (ModListElement otherMod in modContainer.modListDictionary.Values.Where(x => x != this))
                otherMod.ToggleSettingsOff();

            modSettings.gameObject.SetActive(modSettingsToggle.isOn);
        }

        public void ToggleSettingsOff()
        {
            suspendAction = true;

            modSettingsToggle.isOn = false;
            modSettings.gameObject.SetActive(false);

            suspendAction = false;
        }

        public void ToggleModEnabled()
        {
            mod.disabled = !modToggle.isOn;
            modSettings.SaveSettings();

            nameText.color = modToggle.isOn ? ModUI.MSCYellow : ModUI.ModDisabledRed;
            modContainer.UpdateModCountText();

            ModConsole.Log($"<b>{mod.ID}:</b> {(mod.isDisabled ? "<color=red>DISABLED</color>" : "<color=green>ENABLED</color>")}");
        }

        public void SetModEnabled(bool enabled)
        {
            modToggle.isOn = enabled;
        }
    }

    public class ModSettings : MonoBehaviour
    {
        public ModContainer modContainer;
        public Mod mod;

        public Transform settingsList;
        public List<ModSetting> settings = new List<ModSetting>();

        public ModConfig loadedSettings;

        public GameObject prefabDefaultText, defaultText, resetButton, headerSettings;

        public GameObject prefabButton, prefabHeader, prefabKeybind, prefabRadioButtons, prefabSlider, prefabSpacer, prefabText, prefabTextBox, prefabToggle;

        public Text nameText;
        public string Name
        {
            get => nameText.text; set
            {
                nameText.text = value;
                gameObject.name = value;
            }
        }

        public Text idText;
        public string ID
        {
            get => idText.text; set
            {
                gameObject.name = value;
                idText.text = $"ID: {value}";
            }
        }

        public GameObject descriptionHeader;
        public Text descriptionText;
        public string Description
        {
            get => descriptionText.text;
            set
            {
                descriptionText.text = value;
                descriptionHeader.SetActive(!string.IsNullOrEmpty(value));
            }
        }

        void OnEnable()
        {
            CheckForSettings();
        }

        void OnDisable()
        {
            try { if (loadedSettings != null) SaveSettings(); } catch { }
        }

        void OnApplicationQuit()
        {
            if (loadedSettings != null) SaveSettings();
        }

        public void LoadSettings()
        {
            string path = Path.Combine(ModLoader.GetModSettingsFolder(mod, true), $"{mod.ID}.json");
            if (!File.Exists(path)) SaveSettings();

            loadedSettings = JsonConvert.DeserializeObject<ModConfig>(File.ReadAllText(path));

            mod.modListElement.SetModEnabled(!loadedSettings.Disabled);
        }

        public void SaveSettings()
        {
            ModConfig modConfig = new ModConfig {
                Disabled = mod.isDisabled,
                Keybinds = new List<ConfigKeybind>(),
                Numbers = new List<ConfigNumber>(),
                Booleans = new List<ConfigBool>(),
                Strings = new List<ConfigString>()
            };
            foreach (ModSetting setting in settings) setting.SaveSetting(modConfig);

            string path = $@"{ModLoader.GetModSettingsFolder(mod, true)}\{mod.ID}.json";
            string data = JsonConvert.SerializeObject(modConfig, Formatting.Indented);
            File.WriteAllText(path, data);
        }

        void AddSettingToList(ModSetting setting)
        {
            settings.Add(setting);
            setting.transform.SetParent(settingsList, false);

            CheckForSettings();
        }

        void CheckForSettings()
        {
            if (settings.Count == 0 && defaultText == null)
            {
                defaultText = Instantiate(prefabDefaultText);
                defaultText.transform.SetParent(settingsList, false);
                resetButton.SetActive(false);
                headerSettings.SetActive(false);
            }
            else if (settings.Count > 0)
            {
                if (defaultText != null) Destroy(defaultText);
                resetButton.SetActive(true);
                headerSettings.SetActive(true);
            }
        }

        public SettingButton AddButton(string id, string buttonText, string name = "", UnityAction action = null, bool blockSuspension = false)
        {
            SettingButton button = Instantiate(prefabButton).GetComponent<SettingButton>();
            button.ID = id;
            button.Name = name;
            button.ButtonText = buttonText;

            if (action != null)
                button.AddAction(action, blockSuspension);

            AddSettingToList(button);

            return button;
        }

        public SettingButton AddButton(string id, string buttonText, UnityAction action, bool blockSuspension = false)
        {
            SettingButton button = AddButton(id, buttonText, "", action, blockSuspension);

            return button;
        }

        public SettingHeader AddHeader(string text)
        {
            SettingHeader header = Instantiate(prefabHeader).GetComponent<SettingHeader>();
            header.Text = text;

            AddSettingToList(header);

            return header;
        }

        public SettingHeader AddHeader(string text, Color backgroundColor)
        {
            SettingHeader header = AddHeader(text);
            header.BackgroundColor = backgroundColor;

            return header;
        }

        public SettingHeader AddHeader(string text, Color backgroundColor, Color textColor)
        {
            SettingHeader header = AddHeader(text, backgroundColor);
            header.text.color = textColor;

            return header;
        }

        public SettingHeader AddHeader(string text, Color backgroundColor, Color textColor, Color outlineColor)
        {
            SettingHeader header = AddHeader(text, backgroundColor, textColor);
            header.OutlineColor = outlineColor;

            return header;
        }

        public SettingKeybind AddKeybind(string id, string name, KeyCode key, params KeyCode[] modifiers)
        {
            SettingKeybind keybind = Instantiate(prefabKeybind).GetComponent<SettingKeybind>();
            keybind.ID = id;
            keybind.Name = name;
            keybind.keybind = key;
            keybind.modifiers = (modifiers.Length > 0 ? modifiers : new KeyCode[0]);
            keybind.defaultKeybind = key;
            keybind.defaultModifiers = (modifiers.Length > 0 ? modifiers : new KeyCode[0]);
            keybind.KeyText = keybind.AdjustKeyNames();

            AddSettingToList(keybind);

            ConfigKeybind configKeybind = loadedSettings.Keybinds.FirstOrDefault(x => x.id == id);
            if (configKeybind != null)
            {
                keybind.keybind = configKeybind.keybind;
                keybind.modifiers = configKeybind.modifiers;
            }

            return keybind;
        }

        public SettingRadioButtons AddRadioButtons(string id, string name, int value, string[] options, UnityAction<int> action = null)
        {
            SettingRadioButtons radioButtons = Instantiate(prefabRadioButtons).GetComponent<SettingRadioButtons>();
            radioButtons.ID = id;
            radioButtons.Name = name;
            for (int i = 0; i < options.Length; i++) radioButtons.AddButton(options[i]);
            radioButtons.Value = value;
            radioButtons.defaultValue = value;

            AddSettingToList(radioButtons);

            ConfigNumber configNumber = loadedSettings.Numbers.FirstOrDefault(x => x.id == id);
            if (configNumber != null) radioButtons.Value = (int)configNumber.value;

            if (action != null) radioButtons.AddAction(action);

            return radioButtons;
        }

        public SettingSlider AddSlider(string id, string name, float value, float minValue, float maxValue, int roundDigits = -1, UnityAction<float> action = null)
        {
            SettingSlider slider = Instantiate(prefabSlider).GetComponent<SettingSlider>();
            slider.ID = id;
            slider.Name = name;
            slider.MaxValue = maxValue;
            slider.MinValue = minValue;

            if (roundDigits >= 0) slider.roundDigits = roundDigits;

            slider.Value = value;
            slider.defaultValue = value;

            AddSettingToList(slider);

            ConfigNumber configNumber = loadedSettings.Numbers.FirstOrDefault(x => x.id == id);
            if (configNumber != null) slider.Value = configNumber.value;

            if (action != null) slider.AddAction(action);

            return slider;
        }

        public SettingSlider AddSlider(string id, string name, float value, float minValue, float maxValue, UnityAction<float> action = null)
        {
            SettingSlider slider = AddSlider(id, name, value, maxValue, minValue, -1, action);

            return slider;
        }

        public SettingSlider AddSlider(string id, string name, float value, float minValue, float maxValue)
        {
            SettingSlider slider = AddSlider(id, name, value, maxValue, minValue, -1, null);

            return slider;
        }

        public SettingSlider AddSlider(string id, string name, int value, int minValue, int maxValue, UnityAction<float> action = null)
        {
            SettingSlider slider = AddSlider(id, name, (float)value, maxValue, minValue, action: action);
            slider.WholeNumbers = true;

            return slider;
        }
        
        public SettingSpacer AddSpacer(float height)
        {
            SettingSpacer spacer = Instantiate(prefabSpacer).GetComponent<SettingSpacer>();
            spacer.Height = height;

            AddSettingToList(spacer);

            return spacer;
        }

        public SettingText AddText(string text)
        {
            SettingText settingText = Instantiate(prefabText).GetComponent<SettingText>();
            settingText.Text = text;

            AddSettingToList(settingText);

            return settingText;
        }

        public SettingText AddText(string text, Color backgroundColor)
        {
            SettingText settingText = AddText(text);
            settingText.BackgroundColor = backgroundColor;

            return settingText;
        }

        public SettingText AddText(string text, Color backgroundColor, Color outlineColor)
        {
            SettingText settingText = AddText(text);
            settingText.BackgroundColor = backgroundColor;
            settingText.OutlineColor = outlineColor;

            return settingText;
        }

        public SettingTextBox AddTextBox(string id, string name, string value, UnityAction<string> action = null, string placeholder = "ENTER TEXT...")
        {
            SettingTextBox textBox = Instantiate(prefabTextBox).GetComponent<SettingTextBox>();
            textBox.ID = id;
            textBox.Name = name;
            textBox.Value = value;
            textBox.defaultValue = value;
            textBox.Placeholder = placeholder;

            AddSettingToList(textBox);

            ConfigString configString = loadedSettings.Strings.FirstOrDefault(x => x.id == id);
            if (configString != null) textBox.Value = configString.value;

            if (action != null) textBox.AddOnValueChangeAction(action);

            return textBox;
        }

        public SettingTextBox AddTextBox(string id, string name, string value, string placeholder)
        {
            SettingTextBox textBox = AddTextBox(id, name, value, null, placeholder);

            return textBox;
        }

        public SettingToggle AddToggle(string id, string name, bool value)
        {
            SettingToggle toggle = Instantiate(prefabToggle).GetComponent<SettingToggle>();
            toggle.ID = id;
            toggle.Name = name;
            toggle.Value = value;
            toggle.defaultValue = value;

            AddSettingToList(toggle);

            ConfigBool configBool = loadedSettings.Booleans.FirstOrDefault(x => x.id == id);
            if (configBool != null) toggle.Value = configBool.value;

            return toggle;
        }

        public SettingToggle AddToggle(string id, string name, bool value, UnityAction<bool> action)
        {
            SettingToggle toggle = AddToggle(id, name, value);
            toggle.OnValueChanged.AddListener(action);

            return toggle;
        }
    }
}
