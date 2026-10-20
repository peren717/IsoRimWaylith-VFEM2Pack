using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace IsoRimVFEM2Pack
{
    // VFE Medieval 2's warbow between the floors of ProjectZ (only while ProjectZ is loaded; found by name, so the pack
    // loads the same without it). On one floor VFEM2 decides as always: a warbow lobs over walls when neither end is
    // roofed. Between floors the pack tells ProjectZ the same through its lobbed-shot API (Api.RegisterLobbedShot):
    //   ProjectZ sees a line between the two (CanSeeBetweenFloors): a direct shot;
    //   else both ends under open sky (OpenSkyOver: no roof and no floor built over them): lobbed over what is between;
    //   else no shot (direct, with no line).
    // As in VFEM2, lobbing is for hitting only, not for finding targets: the warbow keeps requireLineOfSight, so the
    // game's target search (AttackTargetFinder: auto-attack of drafted colonists, raiders' AI) picks only targets it
    // can see. While that search runs the rule answers "direct", so ProjectZ asks for a line there too; a target the
    // player orders an attack on (or one the AI already picked) is shot lobbed as above.
    public static class Warbow
    {
        private const string Tag = "[IsoRim VFEM2 Pack] ";

        private static Type warbowProps;
        private static Func<IntVec3, IntVec3, Map, bool> canSee;
        private static Func<IntVec3, Map, bool> openSky;

        // > 0 while the game's target search runs on this thread.
        [ThreadStatic]
        private static int searching;

        public static bool Active { get; private set; }

        public static void Setup(Harmony h)
        {
            Type api = AccessTools.TypeByName("ProjectZ.Api");
            if (api == null)
            {
                return;   // no ProjectZ: VFEM2's own rule on the one floor there is
            }
            warbowProps = AccessTools.TypeByName("VFEMedieval.VerbProperties_Warbow");
            MethodInfo register = AccessTools.Method(api, "RegisterLobbedShot", new[] { typeof(Func<Verb, IntVec3, IntVec3, bool?>) });
            MethodInfo see = AccessTools.Method(api, "CanSeeBetweenFloors", new[] { typeof(IntVec3), typeof(IntVec3), typeof(Map) });
            MethodInfo sky = AccessTools.Method(api, "OpenSkyOver", new[] { typeof(IntVec3), typeof(Map) });
            if (warbowProps == null || register == null || see == null || sky == null)
            {
                Log.Warning(Tag + "ProjectZ without the lobbed-shot API (or VFEM2 without its warbow): warbows shoot between floors as ProjectZ's other bows.");
                return;
            }
            canSee = (Func<IntVec3, IntVec3, Map, bool>)Delegate.CreateDelegate(typeof(Func<IntVec3, IntVec3, Map, bool>), see);
            openSky = (Func<IntVec3, Map, bool>)Delegate.CreateDelegate(typeof(Func<IntVec3, Map, bool>), sky);

            HarmonyMethod enter = new HarmonyMethod(typeof(Warbow), nameof(Search_Prefix)) { priority = Priority.First };
            HarmonyMethod leave = new HarmonyMethod(typeof(Warbow), nameof(Search_Finalizer));
            h.Patch(AccessTools.Method(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget)), prefix: enter, finalizer: leave);
            h.Patch(AccessTools.Method(typeof(AttackTargetFinder), nameof(AttackTargetFinder.CanSee)), prefix: enter, finalizer: leave);

            register.Invoke(null, new object[] { new Func<Verb, IntVec3, IntVec3, bool?>(Rule) });
            Active = true;
        }

        // ProjectZ's question for a shot between two floors: true lobbed, false direct, null not a warbow.
        public static bool? Rule(Verb verb, IntVec3 from, IntVec3 to)
        {
            if (verb == null || verb.verbProps == null || !warbowProps.IsInstanceOfType(verb.verbProps))
            {
                return null;
            }
            Map map = verb.caster?.Map;
            if (map == null || searching > 0)
            {
                return false;
            }
            if (canSee(from, to, map))
            {
                return false;
            }
            return openSky(from, map) && openSky(to, map);
        }

        public static void Search_Prefix(out bool __state)
        {
            searching++;
            __state = true;
        }

        public static Exception Search_Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                searching--;
            }
            return __exception;
        }
    }
}
