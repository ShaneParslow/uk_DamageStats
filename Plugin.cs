using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace DamageStats;


[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public static ManualLogSource log;
    private static Harmony harmony;

    public static BepInEx.Configuration.ConfigEntry<uint> numlabels;
    public static BepInEx.Configuration.ConfigEntry<float> uiscale_config;
    public float scale;

    static List<string> texts = new List<string>();
    static string cachedLastOriginal; // Most recent damage string, without xN multiplier.
    static GUIStyle style = new GUIStyle {
        fontSize = 22, richText = true, normal = { textColor = Color.white }
    };
    static int multiplier;

    private void Awake()
    {
        log = base.Logger;
        // Plugin startup logic
        numlabels = Config.Bind<uint>(
            "General",
            "num_lines",
            8,
            "Number of lines of damage history to display"
            );
        uiscale_config = Config.Bind<float>(
            "General",
            "ui_scale",
            0.75f,
            "Value by which to scale ui. Recommend 1 for 1440p, 0.75 for 1080p"
            );
        scale = uiscale_config.Value;
        style.fontSize = (int)(style.fontSize * scale);
        // Apply patches
        harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();

        // Initialize strings
        ResetText();
    }

    public static void AddText(string text, float damage, float dhealth)
    {
        if (text == cachedLastOriginal) {
            multiplier++;
            string dmgstring = (damage * multiplier).ToString("0.000");
            string dhealthstring = (dhealth * multiplier).ToString("0.000");
            texts.RemoveAt(0);
            texts.Insert(0, text + $" (<b>x{multiplier}</b> = {dmgstring} | {dhealthstring})");
            return;
        }
        texts.Insert(0, text);
        cachedLastOriginal = text;
        multiplier = 1;
        texts.RemoveAt(Convert.ToInt32(numlabels.Value));
    }
    public static void ResetText()
    {
        texts.Clear();
        for (int i = 0; i < numlabels.Value; i++) {
            texts.Insert(i, "");
        }
    }
    // Called each frame
    void OnGUI()
    {
        // Border
        UnityEngine.GUI.Box(new Rect(10*scale, 10*scale, 750*scale, (30*numlabels.Value + 10)*scale), "");
        // Put strings in text boxes
        int i = 0;
        foreach (string text in texts) {
            UnityEngine.GUI.Label(new Rect(20*scale, ((30*numlabels.Value)-30*i-15)*scale, 360*scale, 40*scale), text, style);
            i++;
        }
    }
}

[HarmonyPatch(typeof(EnemyIdentifier))]
class EnemyIdentifierPatch
{
    [HarmonyPatch(nameof(EnemyIdentifier.DeliverDamage))]
    static void Prefix(EnemyIdentifier __instance, out float __state)
    {
        // Save previous health
        __state = __instance.health;
    }
    [HarmonyPatch(nameof(EnemyIdentifier.DeliverDamage))]
    static void Postfix(EnemyIdentifier __instance, float __state, float multiplier, GameObject sourceWeapon)
    {
        float initial_health = __state;
        float final_health;
        bool killing_blow = false;
        
        // this guy was dead even before we started
        if (initial_health <= 0) {
            return;
        }
        
        // Maybe unnecessary?
        __instance.ForceGetHealth();

        // Clamp value if it was a killing blow
        if (__instance.health <= 0) {
            final_health = 0;
            killing_blow = true;
        }
        else {
            final_health = __instance.health;
        }
        float dhealth = initial_health - final_health;
        string dhealth_string = dhealth.ToString("0.000");
        string multiplier_string = multiplier.ToString("0.000");

        Plugin.AddText($"dmg: <b>{multiplier_string}</b> | dHealth: <b>{dhealth_string}</b> (<color=lime>{__instance.hitter}</color>)" + (killing_blow ? " [<color=red>killed</color>]" : ""), multiplier, dhealth);
        //Plugin.log.LogInfo($"{multiplier} damage delivered! ({__instance.hitter}) dHealth = {dhealth} before = {initial_health} after = {final_health}");
    }
}

// Clear on level changes
[HarmonyPatch(typeof(SceneHelper))]
class SceneChangePatch
{
    [HarmonyPatch(nameof(SceneHelper.LoadScene))]
    static void Prefix()
    {
        Plugin.ResetText();
    }
}

[HarmonyPatch(typeof(SceneHelper))]
class SceneRestartPatch
{
    [HarmonyPatch(nameof(SceneHelper.RestartScene))]
    static void Prefix()
    {
        Plugin.ResetText();
    }
}