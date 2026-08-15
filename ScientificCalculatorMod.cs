using ModLoader;
using ScientificCalculatorMod.UI;
using UnityEngine;
using System.IO;

namespace ScientificCalculatorMod
{
    /// <summary>
    /// Scientific/graphing calculator mod for Spaceflight Simulator (PC) v1.6.0.
    /// Press F8 in-game to open or close the calculator.
    /// </summary>
    public class ScientificCalculatorEntry : Mod
    {
        public override string ModNameID => "ScientificCalculator";
        public override string DisplayName => "Scientific Calculator";
        public override string Author => "Rauli";
        public override string MinimumGameVersionNecessary => "1.6.0";
        public override string ModVersion => "1.0.0";
        public override string Description =>
            "Adds a scientific and graphing calculator tool, opened with F8. " +
            "Live evaluation as you type, and a graph mode that can plot up to 3 functions at once.";

        /// <summary>
        /// Overrides ModLoader.Mod.IconLink (a virtual string property, default
        /// implementation just returns null — hence no icon showing up before).
        /// It's a path to the icon file, NOT a Texture2D like a previous attempt
        /// here assumed; the loader itself loads/displays the image from this path.
        /// </summary>
        public override string IconLink => Path.Combine(ModFolder, "icon.png");

        public override void Load()
        {
            if (GameObject.Find("ScientificCalculatorMod_Root") != null) return;

            GameObject root = new GameObject("ScientificCalculatorMod_Root");
            Object.DontDestroyOnLoad(root);
            root.AddComponent<CalculatorController>();

            Debug.Log("[ScientificCalculatorMod] Loaded successfully. Press F8 to open the calculator.");
        }
    }
}
