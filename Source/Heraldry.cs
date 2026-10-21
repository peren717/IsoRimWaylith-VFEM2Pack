using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsoRimVFEM2Pack
{
    // Heraldry on the 3D tabard (IsoRim Api 10 apparel decals; an older IsoRim: never set up, the tabard keeps its plain look).
    // VFE Medieval 2 draws the tabard in 2D with VE Framework render nodes (PawnRenderNode_Omni) on the apparel: the field
    // (a two-colour mask of the faction's division pattern over the tabard, CutoutComplex, colours A and B) and the symbol
    // (the faction's emblem, Cutout, scaled down under the chest), each shown facing south and north. Their colours, pattern
    // and symbol come from VEF's tagged values (HeraldryColorA/B, HeraldryPattern, HeraldrySymbol, HeraldrySymbolColor:
    // the item's own, else the wearer's faction's, its ideology's, ... as the player edits them, or picked from the faction's
    // seed). Here each of those nodes is made once per worn item (VEF's own node class, by name: no reference to VEF) and
    // asked for its graphic as 2D asks it (GraphicFor), each time IsoRim works out the wearer's look, so a change of faction,
    // ideology colour or edited heraldry shows as in 2D. The field goes on as its pattern and colours only (the 2D tabard's
    // outline and shading are left to the 3D garment), the symbol as 2D draws it; both on the front (south picture) and
    // the back (north picture), placed and sized as the node places them on the body sprite (drawData offset and scale).
    // Every apparel with VFEM2's heraldry comp and VEF Omni render nodes gets this (today the tabard).
    internal static class Heraldry
    {
        private const string Tag = "[IsoRim VFEM2 Pack] ";

        private static Type omniProps;
        private static ConstructorInfo omniCtor;

        private sealed class Nodes
        {
            public Pawn wearer;
            public PawnRenderNode[] nodes;
        }

        private static readonly Dictionary<int, Nodes> byItem = new Dictionary<int, Nodes>();

        // (tests) decals handed out, and the last item asked about.
        public static int Given;
        public static string Last = "";

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void Setup()
        {
            omniProps = AccessTools.TypeByName("VEF.Graphics.PawnRenderNodeProperties_Omni");
            Type omniNode = AccessTools.TypeByName("VEF.Graphics.PawnRenderNode_Omni");
            Type heraldic = AccessTools.TypeByName("VFEMedieval.CompProperties_EditHeraldic");
            omniCtor = omniNode != null ? omniNode.GetConstructor(new[] { typeof(Pawn), typeof(PawnRenderNodeProperties), typeof(PawnRenderTree), typeof(Apparel) }) : null;
            if (omniProps == null || omniCtor == null || heraldic == null)
            {
                Log.Warning(Tag + "VE Framework's render nodes or VFE Medieval 2's heraldry not found: tabards keep a plain look in 3D.");
                return;
            }
            int n = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.apparel == null || def.apparel.RenderNodeProperties == null || def.comps == null || !def.comps.Exists(c => heraldic.IsInstanceOfType(c))
                    || !def.apparel.RenderNodeProperties.Exists(p => omniProps.IsInstanceOfType(p)))
                {
                    continue;
                }
                // An explicit new delegate: a plain method group would be cached in a compiler-made static field of the
                // Api 10 delegate type, and the game's startup check reads every static field's type (TypeLoadException
                // on an older IsoRim even though this code never runs there).
                IsoRim.Api.AddApparelDecals(def, new IsoRim.ApparelDecalProvider(Provide));
                n++;
            }
            Log.Message(Tag + "heraldry on " + n + " 3D garment(s)");
        }

        private static PawnRenderNode[] NodesFor(Apparel apparel, Pawn wearer)
        {
            Nodes have;
            if (byItem.TryGetValue(apparel.thingIDNumber, out have) && have.wearer == wearer)
            {
                return have.nodes;
            }
            if (byItem.Count > 256)
            {
                byItem.Clear();   // items long gone; the ones still worn are made again
            }
            PawnRenderTree tree = wearer.Drawer != null && wearer.Drawer.renderer != null ? wearer.Drawer.renderer.renderTree : null;
            List<PawnRenderNode> list = new List<PawnRenderNode>();
            if (tree != null)
            {
                foreach (PawnRenderNodeProperties p in apparel.def.apparel.RenderNodeProperties)
                {
                    if (omniProps.IsInstanceOfType(p))
                    {
                        list.Add((PawnRenderNode)omniCtor.Invoke(new object[] { wearer, p, tree, apparel }));
                    }
                }
            }
            have = new Nodes { wearer = wearer, nodes = list.ToArray() };
            byItem[apparel.thingIDNumber] = have;
            return have.nodes;
        }

        // IsoRim's apparel decal provider (asked when it works out the wearer's look).
        private static void Provide(Apparel apparel, Pawn wearer, List<IsoRim.ApparelDecal> decals)
        {
            if (apparel == null || wearer == null || wearer.story == null)
            {
                return;
            }
            PawnRenderNode[] nodes = NodesFor(apparel, wearer);
            float quad = HumanlikeMeshPoolUtility.HumanlikeBodyWidthForPawn(wearer);
            for (int i = 0; i < nodes.Length; i++)
            {
                PawnRenderNode node = nodes[i];
                PawnRenderNodeProperties props = node.Props;
                Graphic g = node.GraphicFor(wearer);
                if (g == null)
                {
                    continue;
                }
                Side(decals, node, props, g, wearer, Rot4.South, IsoRim.DecalSide.Front, quad);
                Side(decals, node, props, g, wearer, Rot4.North, IsoRim.DecalSide.Back, quad);
            }
            Last = apparel.def.defName + " on " + wearer.LabelShort + ": " + decals.Count + " decals";
        }

        private static void Side(List<IsoRim.ApparelDecal> decals, PawnRenderNode node, PawnRenderNodeProperties props, Graphic g, Pawn wearer, Rot4 rot,
            IsoRim.DecalSide side, float quad)
        {
            if (props.visibleFacing != null && !props.visibleFacing.Contains(rot))
            {
                return;
            }
            Material m = g.MatAt(rot);
            if (m == null)
            {
                return;
            }
            Texture mask = m.HasProperty(ShaderPropertyIDs.MaskTex) ? m.GetTexture(ShaderPropertyIDs.MaskTex) : null;
            bool field = false;
            if (mask is Texture2D maskTex && maskTex != BaseContent.BlackTex && maskTex != BaseContent.WhiteTex)
            {
                field = true;
                // The field: its pattern in its two colours on a plain cloth (the 2D tabard's own outline and shading are the
                // 3D garment's job), with the game's own two-colour shader, so the colours come out as in 2D.
                MaterialRequest req = new MaterialRequest(BaseContent.WhiteTex, m.shader, g.color);
                req.colorTwo = g.colorTwo;
                req.maskTex = maskTex;
                m = MaterialPool.MatFrom(req);
            }
            DrawData dd = props.drawData;
            Vector3 off = dd != null ? dd.OffsetForRot(rot) : Vector3.zero;
            float scale = dd != null ? dd.ScaleFor(wearer) : 1f;
            decals.Add(new IsoRim.ApparelDecal
            {
                material = m,
                side = side,
                centre = new Vector2(off.x, off.z),
                size = new Vector2(quad * scale * g.drawSize.x, quad * scale * g.drawSize.y),
                wrap = field,   // the field covers the whole garment, its top and edges too: the 2D tabard is all heraldry
            });
            Given++;
        }
    }
}
