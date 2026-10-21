using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace IsoRimVFEM2Pack
{
    // The few VFE Medieval 2 things the pack's models need code for. Everything else is XML (Defs\VFEM2_Models.xml).
    //  - Leather boilpot: shown with its "full" model (ModelDef byState) while the VE Framework processor holds a brew
    //    (working or finished), through IsoRim's Api.AddModelState; the processor's flat working/finished overlays are
    //    left out while a 3D model stands in for the body.
    //  - Heraldic standing banner: VEF's DynamicGraphicBuilding draws its own flat sprites (cloth, pattern, symbol) in
    //    place of the game's body sprite, so IsoRim cannot hold that back; it is left out while the model stands in.
    // VEF types are found by name, so nothing here stops the pack loading if VEF changes; a missing type or member just
    // leaves that part off (logged once).
    [StaticConstructorOnStartup]
    public static class PackMain
    {
        private const string Tag = "[IsoRim VFEM2 Pack] ";

        private static Type processorType;
        private static Func<object, object> processOf;      // CompAdvancedResourceProcessor.Process
        private static Func<object, float> progressOf;      // Process.Progress
        private static Func<object, bool> pickUpReadyOf;    // Process.PickUpReady
        private static readonly string[] overlayFields =
        {
            "cachedProgressGraphic", "cachedProgressGraphic_multi", "cachedFinishedGraphic", "cachedFinishedGraphic_multi"
        };
        private static FieldInfo[] overlays;

        static PackMain()
        {
            try
            {
                Warbow.Setup(new Harmony("peren.isorimwaylith.vfem2pack.warbow"));
            }
            catch (Exception e)
            {
                Log.Error(Tag + "Warbow setup failed: " + e);
            }
            try
            {
                if (IsoRim.Api.Version < 6)
                {
                    Log.Warning(Tag + "IsoRim Waylith is older than this pack needs: the boilpot's full look and the banner fix are off.");
                    return;
                }
                if (IsoRim.Api.Version < 9)
                {
                    // The apparel models (Patches/VFEM2_Apparel.xml, VFEM2_Apparel2.xml) were left out by PatchOperation_IfIsoRimApi.
                    Log.Message(Tag + "IsoRim Waylith has no apparel model bindings (Api 9): VFE Medieval 2 apparel keeps IsoRim's own look.");
                }
                Setup();
            }
            catch (Exception e)
            {
                Log.Error(Tag + "Setup failed: " + e);
            }
            try
            {
                // Faction heraldry on the 3D tabard: needs IsoRim's apparel decals (Api 10); older: the tabard's plain look.
                if (IsoRim.Api.Version >= 10)
                {
                    Heraldry.Setup();
                }
            }
            catch (Exception e)
            {
                Log.Error(Tag + "Heraldry setup failed: " + e);
            }
        }

        private static void Setup()
        {
            Harmony h = new Harmony("peren.isorimwaylith.vfem2pack");

            processorType = AccessTools.TypeByName("PipeSystem.CompAdvancedResourceProcessor");
            Type processType = AccessTools.TypeByName("PipeSystem.Process");
            MethodInfo processGetter = processorType != null ? AccessTools.PropertyGetter(processorType, "Process") : null;
            MethodInfo progressGetter = processType != null ? AccessTools.PropertyGetter(processType, "Progress") : null;
            MethodInfo readyGetter = processType != null ? AccessTools.PropertyGetter(processType, "PickUpReady") : null;
            MethodInfo postDraw = processorType != null ? AccessTools.DeclaredMethod(processorType, "PostDraw") : null;
            if (processGetter != null && progressGetter != null && readyGetter != null)
            {
                processOf = o => processGetter.Invoke(o, null);
                progressOf = o => (float)progressGetter.Invoke(o, null);
                pickUpReadyOf = o => (bool)readyGetter.Invoke(o, null);
                IsoRim.Api.AddModelState(BrewState);
            }
            else
            {
                Log.Warning(Tag + "VE Framework's resource processor not found: the boilpot keeps its empty look.");
            }
            List<FieldInfo> found = new List<FieldInfo>();
            foreach (string name in overlayFields)
            {
                FieldInfo f = processorType != null ? AccessTools.Field(processorType, name) : null;
                if (f != null)
                {
                    found.Add(f);
                }
            }
            overlays = found.ToArray();
            if (postDraw != null && overlays.Length > 0)
            {
                h.Patch(postDraw, prefix: new HarmonyMethod(typeof(PackMain), nameof(ProcessorPostDraw_Prefix)),
                    finalizer: new HarmonyMethod(typeof(PackMain), nameof(ProcessorPostDraw_Finalizer)));
            }

            Type banner = AccessTools.TypeByName("VEF.Graphics.DynamicGraphicBuilding");
            MethodInfo drawAt = banner != null ? AccessTools.DeclaredMethod(banner, "DrawAt") : null;
            if (drawAt != null)
            {
                h.Patch(drawAt, prefix: new HarmonyMethod(typeof(PackMain), nameof(DynamicDrawAt_Prefix)));
            }
        }

        // The processor's comp on the thing, or null.
        public static ThingComp Processor(Thing t)
        {
            ThingWithComps twc = t as ThingWithComps;
            if (twc == null || processorType == null)
            {
                return null;
            }
            List<ThingComp> comps = twc.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                if (processorType.IsInstanceOfType(comps[i]))
                {
                    return comps[i];
                }
            }
            return null;
        }

        // The processor (or null): its current process, while it has one.
        public static object ProcessOf(Thing t)
        {
            ThingComp c = Processor(t);
            return c != null ? processOf(c) : null;
        }

        // "full" while the processor holds a brew: working (as the game's working overlay, Progress above 0) or done
        // (its finished overlay). Asked by IsoRim only for things whose ModelDef has byState models.
        private static string BrewState(Thing t)
        {
            object p = ProcessOf(t);
            if (p == null)
            {
                return null;
            }
            return progressOf(p) > 0f || pickUpReadyOf(p) ? "full" : null;
        }

        // While a 3D model stands in, the processor's flat working/finished overlays are not drawn (its bars still are).
        public static void ProcessorPostDraw_Prefix(ThingComp __instance, out object[] __state)
        {
            __state = null;
            if (__instance == null || !IsoRim.Api.ModelStandsIn(__instance.parent))
            {
                return;
            }
            __state = new object[overlays.Length];
            for (int i = 0; i < overlays.Length; i++)
            {
                __state[i] = overlays[i].GetValue(__instance);
                overlays[i].SetValue(__instance, null);
            }
        }

        public static Exception ProcessorPostDraw_Finalizer(ThingComp __instance, object[] __state, Exception __exception)
        {
            if (__state != null)
            {
                for (int i = 0; i < overlays.Length; i++)
                {
                    overlays[i].SetValue(__instance, __state[i]);
                }
            }
            return __exception;
        }

        // VEF's dynamic-graphic buildings draw their own flat sprites; none while a 3D model stands in.
        public static bool DynamicDrawAt_Prefix(Thing __instance)
        {
            return !IsoRim.Api.ModelStandsIn(__instance);
        }
    }

    // Harness only (command line -vfem2packtest, dev mode): every leather boilpot standing on an odd x holds a brew
    // half done, so one in-engine pass shows both the empty and the full model. Does nothing in normal play.
    public class PackTest : GameComponent
    {
        private static readonly bool On = GenCommandLine.CommandLineArgPassed("vfem2packtest");
        private bool done;
        private int tries;

        public PackTest(Game game)
        {
        }

        public override void GameComponentUpdate()
        {
            if (!On || done || !Prefs.DevMode || Find.CurrentMap == null)
            {
                return;
            }
            int set = 0, seen = 0;
            string why = "";
            foreach (Thing t in Find.CurrentMap.listerThings.AllThings)
            {
                if (t.def.defName != "VFEM2_LeatherBoilpot" || t.Position.x % 2 == 0)
                {
                    continue;
                }
                seen++;
                object p = PackMain.ProcessOf(t);
                if (p == null)
                {
                    // No current process (none started yet): the first one of its stack, as the game starts it.
                    ThingComp c = PackMain.Processor(t);
                    object stack = c != null ? AccessTools.Property(c.GetType(), "ProcessStack")?.GetValue(c) : null;
                    System.Collections.IList list = stack != null ? AccessTools.Property(stack.GetType(), "Processes")?.GetValue(stack) as System.Collections.IList : null;
                    why = "comp " + (c != null) + " stack " + (stack != null) + " processes " + (list != null ? list.Count : -1);
                    if (list != null && list.Count == 0)
                    {
                        // As the player picking the boilpot's process from its menu (CompAdvancedResourceProcessor).
                        System.Collections.IList defs = AccessTools.Field(c.props.GetType(), "processes")?.GetValue(c.props) as System.Collections.IList;
                        MethodInfo add = AccessTools.Method(stack.GetType(), "AddProcess");
                        if (defs != null && defs.Count > 0 && add != null)
                        {
                            add.Invoke(stack, new object[] { defs[0], t, RimWorld.BillRepeatModeDefOf.RepeatCount, 1, RimWorld.QualityCategory.Normal });
                        }
                        why += " added " + list.Count;
                    }
                    if (list != null && list.Count > 0)
                    {
                        AccessTools.Field(stack.GetType(), "currentProcess")?.SetValue(stack, list[0]);
                        p = PackMain.ProcessOf(t);
                    }
                }
                PropertyInfo prop = p != null ? AccessTools.Property(p.GetType(), "Progress") : null;
                if (prop != null)
                {
                    prop.SetValue(p, 0.5f);
                    set++;
                }
            }
            if (set > 0 || (seen > 0 && ++tries > 600))
            {
                done = true;
                Log.Message("[IsoRim VFEM2 Pack] test: " + set + " of " + seen + " boilpot(s) set half brewed " + why);
            }
        }
    }
}
