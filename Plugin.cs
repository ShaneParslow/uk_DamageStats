using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;
using MonoMod.Utils;
using System.Linq;
using System.Diagnostics;

namespace DamageStats;


// Handles UI and what strings are currently displayed
public class DamageUIController : MonoBehaviour
{
    static GUIStyle style = new GUIStyle
    {
        fontSize = 22,
        richText = true,
        normal = { textColor = Color.white }
    };

    uint lines;
    float scale;

    // All the data for a single line of text
    class DamageInstance
    {
        public decimal damage;
        public decimal dhealth;
        public string hitter;
        public bool killing_blow;
        public uint multiplier = 1; // number of times this exact same instance of damage has repeated

        public DamageInstance(float damage, float dhealth, string hitter, bool killing_blow)
        {
            this.damage = new decimal(damage);
            this.damage = Decimal.Round(this.damage, 3);
            
            this.dhealth = new decimal(dhealth);
            this.dhealth = Decimal.Round(this.dhealth, 3);
            
            this.hitter = hitter; // idk if this string gets freed hopefully it just references a constant
            this.killing_blow = killing_blow;
        }

        public override string ToString()
        {
            // Old line formatting
            string hit = $" (<color=lime>{hitter}</color>)";
            string killed = killing_blow ? " [<color=red>killed</color>]" : "";
            string total = multiplier != 1 ? $" (<b>x{multiplier}</b> = {damage * multiplier} | {dhealth * multiplier})" : "";
            return $"dmg: <b>{damage}</b> | dHealth: <b>{dhealth}</b>{total}{hit}{killed}";
        }
    }

    // All lines of text currently present.
    List<DamageInstance> DamageInstances;

    // Cant have constructors in MonoBehaviours. This gets called right after this class gets registered with Unity.
    public void Init(uint lines, float scale)
    {
        this.lines = lines;
        DamageInstances = new List<DamageInstance>();
        this.scale = scale;
        style.fontSize = (int)(style.fontSize * scale);
    }

    public void NewDamageInstance(float damage, float dhealth, string hitter, bool killing_blow)
    {
        decimal dmg = new decimal(damage);
        dmg = Decimal.Round(dmg, 3);
        decimal dh = new decimal(dhealth);
        dh = Decimal.Round(dh, 3);

        if (DamageInstances.Count > 0)
        {
            DamageInstance last = DamageInstances.Last();
            // Exact same metrics? Also make sure we aren't messing with a previous line that showed a kill
            if (last.damage == dmg && last.dhealth == dh && last.hitter == hitter && last.killing_blow == false)
            {
                // Mark as killed if it just so happens that the metrics are the same and we just killed a guy
                if(killing_blow)
                {
                    last.killing_blow = true;
                }
                // Increment x{n} counter
                last.multiplier += 1;
                return;
            }
        }
        
        // Drop the first one if lines are all full
        if (DamageInstances.Count == lines)
        {
            DamageInstances.RemoveAt(0);
        }
        // New line
        DamageInstance new_dmg = new DamageInstance(damage, dhealth, hitter, killing_blow);
        DamageInstances.Add(new_dmg);
    }

    public void ResetText()
    {
        DamageInstances.Clear();
    }

    // Called each frame
    private void OnGUI()
    {
        // Static elements
        UnityEngine.GUI.Box(new Rect(10 * scale, 10 * scale, 600 * scale, (30 * lines + 40) * scale), "");
        UnityEngine.GUI.Label(new Rect(20 * scale, 15 * scale, 80 * scale, 40 * scale), "dmg", style);
        UnityEngine.GUI.Label(new Rect(90 * scale, 15 * scale, 80 * scale, 40 * scale), "dHealth", style);

        // Put metrics in text boxes
        int i = 0;
        foreach (DamageInstance inst in DamageInstances)
        {
            string multipliers = $"(<b>x{inst.multiplier}</b> = {(inst.damage * inst.multiplier).ToString("0.000")} | {(inst.dhealth * inst.multiplier).ToString("0.000")})";
            string hitter_killed = $"(<color=lime>{inst.hitter}</color>)" + (inst.killing_blow ? " [<color=red>killed</color>]" : "");

            // Just hitters/killed or include multipliers?
            string epilogue;
            if (inst.multiplier > 1)
            {
                epilogue = multipliers + " " + hitter_killed;
            }
            else
            {
                epilogue = hitter_killed;
            }

            UnityEngine.GUI.Label(new Rect(20 * scale, (45 + 30 * i) * scale, 70 * scale, 40 * scale), inst.damage.ToString("0.000"), style);
            UnityEngine.GUI.Label(new Rect(90 * scale, (45 + 30 * i) * scale, 70 * scale, 40 * scale), inst.dhealth.ToString("0.000"), style);
            UnityEngine.GUI.Label(new Rect(160 * scale, (45 + 30 * i) * scale, 400 * scale, 40 * scale), epilogue, style);

            i++;
        }
    }
    private void OnDestroy()
    {
        Plugin.log.LogError("uk being a bitch and deleting my shit");
        Debugger.Launch();
    }
}

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin   
{
    public static ManualLogSource log;
    private static Harmony harmony;

    private static BepInEx.Configuration.ConfigEntry<uint> numlabels_cfg;
    private static BepInEx.Configuration.ConfigEntry<float> uiscale_cfg;

    public static DamageUIController UIController;

    private void Awake()
    {
        log = base.Logger;

        // Plugin config
        numlabels_cfg = Config.Bind<uint>(
            "General",
            "num_lines",
            8,
            "Number of lines of damage history to display"
            );
        uiscale_cfg = Config.Bind<float>(
            "General",
            "ui_scale",
            0.75f,
            "Value by which to scale ui. Recommend 1 for 1440p, 0.75 for 1080p"
            );
        
        // Apply patches
        harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        Plugin.log.LogInfo("Patches loaded");

        // Register UI class with Unity and give it config parameters
        // Register it under a base object that is hidden so that it doesnt get destroyed. DontDestroyOnLoad or whatever its called doesn't work.
        GameObject baseObject = new GameObject("DamageStats");
        UIController = baseObject.AddComponent<DamageUIController>();
        baseObject.hideFlags = HideFlags.HideAndDontSave; // 
        UIController.Init(numlabels_cfg.Value, uiscale_cfg.Value); // If the controller ticks and runs OnGUI things should break. This should all be synchronous though?
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
    static void Postfix(EnemyIdentifier __instance, float __state, float multiplier, float critMultiplier, GameObject sourceWeapon)
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

        Plugin.log.LogInfo($"{multiplier} damage delivered! ({__instance.hitter}) dHealth = {dhealth} before = {initial_health} after = {final_health} critMultiplier = {critMultiplier}");
        Plugin.UIController.NewDamageInstance(multiplier, dhealth, __instance.hitter, killing_blow);
    }
}

// Clear on level changes
// TODO: Don't render on menu
[HarmonyPatch(typeof(SceneHelper))]
class SceneChangePatch
{
    [HarmonyPatch(nameof(SceneHelper.LoadScene))]
    static void Prefix()
    {
        Plugin.UIController.ResetText();
    }
}

[HarmonyPatch(typeof(SceneHelper))]
class SceneRestartPatch
{
    [HarmonyPatch(nameof(SceneHelper.RestartScene))]
    static void Prefix()
    {
        Plugin.UIController.ResetText();
    }
}