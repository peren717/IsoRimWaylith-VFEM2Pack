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

        // ---- the standing banner (IsoRim Api 53 model decals; an older IsoRim: never set up, the cloth stays plain)

        // VEF's DynamicGraphicBuilding draws two graphics in 2D: the field (the banner sprite with the two-colour mask of the
        // pattern, CutoutComplex) and the symbol (Cutout). Both are asked for as 2D asks them (GetDynamicGraphics: the item's own
        // tags, else its faction's, else plain), when IsoRim builds the model, and printed on the cloth: the field's mask only (the
        // sprite's frame and shading are the model's job), front and back alike, so the back reads as the front does. The graphics
        // change only through Notify_ColorChanged (VEF's cache; VFEM2's heraldry dialog calls it on everything), so that asks IsoRim
        // to build the model again.
        private const string BannerDef = "VFEM2_StandingBanner";
        private const string BannerCloth = "banner_cloth_tint";

        // The cloth in the 256 px mask (columns 86 to 169, rows 38 to 158 from the top, and its tabs up to row 24) ...
        private static readonly Rect SpriteCloth = new Rect(86f / 256f, 1f - 158f / 256f, 83f / 256f, 134f / 256f);
        private const float SpriteClothRows = 120f / 256f;
        // ... and in the model (vfem2_standing_banner.obj): 0.72 across, 0.66 to 1.68 up, its tabs elsewhere than the sprite's
        // (the field spreads: its colours reach them, and the corners the sprite rounds off).
        private const float ClothWidth = 0.72f, ClothBottom = 0.66f, ClothTop = 1.68f;

        private static MethodInfo dynamicGraphics;

        // (tests) decals handed out for banners.
        public static int BannerGiven;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void SetupBanner(Harmony h)
        {
            Type building = AccessTools.TypeByName("VEF.Graphics.DynamicGraphicBuilding");
            dynamicGraphics = building != null ? AccessTools.Method(building, "GetDynamicGraphics") : null;
            MethodInfo changed = building != null ? AccessTools.DeclaredMethod(building, "Notify_ColorChanged") : null;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(BannerDef);
            if (dynamicGraphics == null || changed == null || def == null || dynamicGraphics.ReturnType != typeof(List<Graphic>))
            {
                Log.Warning(Tag + "VE Framework's dynamic graphics or the standing banner not found: the banner keeps its plain cloth in 3D.");
                return;
            }
            // An explicit new delegate, as for the tabard above.
            IsoRim.Api.AddModelDecals(def, BannerCloth, new IsoRim.ModelDecalProvider(BannerDecals));
            h.Patch(changed, postfix: new HarmonyMethod(typeof(Heraldry), nameof(ColorChanged_Postfix)));
        }

        public static void ColorChanged_Postfix(Thing __instance)
        {
            IsoRim.Api.RefreshModelDecals(__instance);
        }

        private static void BannerDecals(Thing banner, List<IsoRim.ModelDecal> decals)
        {
            List<Graphic> graphics = (List<Graphic>)dynamicGraphics.Invoke(banner, null);
            // Model units per sprite cell (the sprite's 256 px are 2 cells), and where the sprite's middle lands on the cloth.
            float across = ClothWidth / (SpriteCloth.width * 2f), up = (ClothTop - ClothBottom) / (SpriteClothRows * 2f);
            float middle = ClothBottom + (0.5f - SpriteCloth.y) * 2f * up;
            foreach (Graphic g in graphics)
            {
                Material m = g.MatAt(Rot4.North);
                Texture2D mask = m != null && m.HasProperty(ShaderPropertyIDs.MaskTex) ? m.GetTexture(ShaderPropertyIDs.MaskTex) as Texture2D : null;
                if (mask != null && mask != BaseContent.BlackTex && mask != BaseContent.WhiteTex)
                {
                    float height = SpriteCloth.height * 2f * up;
                    Both(decals, null, mask, g, new Vector2(0f, ClothBottom + height / 2f), new Vector2(ClothWidth, height), SpriteCloth, true);
                }
                else if (m != null && g.path != banner.def.graphicData.texPath && m.mainTexture is Texture2D symbol)
                {
                    Vector3 off = g.DrawOffset(Rot4.North);
                    Both(decals, symbol, null, g, new Vector2(off.x * across, middle + off.z * up), new Vector2(g.drawSize.x * across, g.drawSize.y * up), new Rect(0f, 0f, 1f, 1f), false);
                }
            }
        }

        // ---- the heraldic standard (IsoRim Api 54; older: never set up, it stays the game's flat sprite)

        // A weapon of VEF's DynamicGraphicThing: its own Thing.DrawAt on the ground and, in hand, a prefix on the game's
        // DrawEquipmentAiming drawing straight to Unity, field and symbol as for the banner. The draw in hand is given to IsoRim
        // (CatchWeaponDraws: only while the weapon shows its model), the one on the ground left out while the model stands in;
        // the heraldry is printed on the cloth as for the banner, from the tags 2D reads: the item's own, else its bearer's, so it
        // is asked again on an edit and on equipping and dropping it.
        private const string StandardDef = "VFEM2_MeleeWeapon_Standard";
        private const string StandardCloth = "standard_cloth";

        // The cloth in the mask (columns 96 to 158, rows 43 to 144 from the top) and in the model (vfem2_standard.obj, one unit
        // to the sprite's cell, 128 px; tools/make_standardhelm.py): the sprite's own shape and size, on a longer pole.
        private static readonly Rect StandardSpriteCloth = new Rect(96f / 256f, 1f - 145f / 256f, 63f / 256f, 102f / 256f);
        private const float StandardClothWidth = 0.4844f, StandardClothBottom = 1.2506f, StandardClothTop = 2.0397f, StandardMiddle = 1.3756f;

        private static MethodInfo thingGraphics;

        // (tests) decals handed out for standards.
        public static int StandardGiven;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void SetupStandard(Harmony h)
        {
            Type thing = AccessTools.TypeByName("VEF.Graphics.DynamicGraphicThing");
            Type patches = AccessTools.TypeByName("VEF.Graphics.VanillaExpandedFramework_DynamicGraphicPatches");
            MethodInfo inHand = patches != null ? AccessTools.Method(patches, "DrawEquipmentAimingPrefix") : null;
            thingGraphics = thing != null ? AccessTools.Method(thing, "GetDynamicGraphics") : null;
            MethodInfo drawAt = thing != null ? AccessTools.DeclaredMethod(thing, "DrawAt") : null;
            MethodInfo changed = thing != null ? AccessTools.DeclaredMethod(thing, "Notify_ColorChanged") : null;
            MethodInfo equipped = thing != null ? AccessTools.DeclaredMethod(thing, "Notify_Equipped") : null;
            MethodInfo unequipped = thing != null ? AccessTools.DeclaredMethod(thing, "Notify_Unequipped") : null;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(StandardDef);
            if (inHand == null || drawAt == null || changed == null || equipped == null || unequipped == null || def == null
                || thingGraphics == null || thingGraphics.ReturnType != typeof(List<Graphic>))
            {
                Log.Warning(Tag + "VE Framework's dynamic graphics or the heraldic standard not found: the standard stays the game's sprite in 3D.");
                return;
            }
            IsoRim.Api.CatchWeaponDraws(inHand);
            // An explicit new delegate, as for the tabard above.
            IsoRim.Api.AddModelDecals(def, StandardCloth, new IsoRim.ModelDecalProvider(StandardDecals));
            h.Patch(drawAt, prefix: new HarmonyMethod(typeof(PackMain), nameof(PackMain.DynamicDrawAt_Prefix)));
            HarmonyMethod refresh = new HarmonyMethod(typeof(Heraldry), nameof(ColorChanged_Postfix));
            h.Patch(changed, postfix: refresh);
            h.Patch(equipped, postfix: refresh);
            h.Patch(unequipped, postfix: refresh);
        }

        private static void StandardDecals(Thing standard, List<IsoRim.ModelDecal> decals)
        {
            List<Graphic> graphics = (List<Graphic>)thingGraphics.Invoke(standard, null);
            foreach (Graphic g in graphics)
            {
                Material m = g.MatAt(Rot4.North);
                Texture2D mask = m != null && m.HasProperty(ShaderPropertyIDs.MaskTex) ? m.GetTexture(ShaderPropertyIDs.MaskTex) as Texture2D : null;
                if (mask != null && mask != BaseContent.BlackTex && mask != BaseContent.WhiteTex)
                {
                    Both(decals, null, mask, g, new Vector2(0f, (StandardClothBottom + StandardClothTop) / 2f),
                        new Vector2(StandardClothWidth, StandardClothTop - StandardClothBottom), StandardSpriteCloth, true);
                }
                else if (m != null && g.path != standard.def.graphicData.texPath && m.mainTexture is Texture2D symbol)
                {
                    Vector3 off = g.DrawOffset(Rot4.North);
                    Both(decals, symbol, null, g, new Vector2(off.x, StandardMiddle + off.z), g.drawSize, new Rect(0f, 0f, 1f, 1f), false);
                }
            }
            StandardGiven += decals.Count;
        }

        private static void Both(List<IsoRim.ModelDecal> decals, Texture2D texture, Texture2D mask, Graphic g, Vector2 centre, Vector2 size, Rect source, bool spread)
        {
            decals.Add(new IsoRim.ModelDecal { texture = texture, mask = mask, colour = g.color, colourTwo = g.colorTwo, side = IsoRim.DecalSide.Front, centre = centre, size = size, source = source, spread = spread });
            decals.Add(new IsoRim.ModelDecal { texture = texture, mask = mask, colour = g.color, colourTwo = g.colorTwo, side = IsoRim.DecalSide.Back, centre = centre, size = size, source = source, spread = spread });
            BannerGiven += 2;
        }
    }
}
