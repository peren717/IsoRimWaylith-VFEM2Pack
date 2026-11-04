using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace IsoRimVFEM2Pack
{
    // The visor up (IsoRim Api 54; older: never set up, the helmet stays closed). VFE Medieval 2 draws apparel whose
    // ApparelExtension.showOpenWhenNotShowingWeapons is set with its _Open sprite while it is worn and the game does not show the
    // wearer's weapon (ApparelGraphicRecordGetter_TryGetGraphicApparel_Patch: !PawnRenderUtility.CarryWeaponOpenly). The same test
    // names the item's state "open" here, for the binding's byState (Patches/VFEM2_ZApi54.xml). IsoRim asks when it works out the
    // wearer's look; drafting asks for that at once, as VFE Medieval 2 redraws the sprite then.
    internal static class OpenHelmet
    {
        private const string Tag = "[IsoRim VFEM2 Pack] ";
        private static readonly HashSet<ThingDef> defs = new HashSet<ThingDef>();

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void Setup(Harmony h)
        {
            Type extension = AccessTools.TypeByName("VFEMedieval.ApparelExtension");
            FieldInfo open = extension != null ? AccessTools.Field(extension, "showOpenWhenNotShowingWeapons") : null;
            MethodInfo drafted = AccessTools.PropertySetter(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted));
            if (open == null || drafted == null)
            {
                Log.Warning(Tag + "VFE Medieval 2's open apparel not found: the heavy plate helmet stays closed in 3D.");
                return;
            }
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.apparel == null || def.modExtensions == null)
                {
                    continue;
                }
                foreach (DefModExtension e in def.modExtensions)
                {
                    if (extension.IsInstanceOfType(e) && (bool)open.GetValue(e))
                    {
                        defs.Add(def);
                    }
                }
            }
            if (defs.Count == 0)
            {
                return;
            }
            IsoRim.Api.AddModelState(new Func<Thing, string>(State));
            h.Patch(drafted, postfix: new HarmonyMethod(typeof(OpenHelmet), nameof(Drafted_Postfix)));
        }

        private static string State(Thing t)
        {
            Apparel a = t as Apparel;
            return a != null && defs.Contains(a.def) && a.Wearer != null && !PawnRenderUtility.CarryWeaponOpenly(a.Wearer) ? "open" : null;
        }

        public static void Drafted_Postfix(Pawn_DraftController __instance)
        {
            IsoRim.Api.RefreshApparelDecals(__instance.pawn);
        }
    }
}
