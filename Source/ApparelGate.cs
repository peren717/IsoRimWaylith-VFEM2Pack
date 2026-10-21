using System;
using System.Xml;
using Verse;

namespace IsoRimVFEM2Pack
{
    // Runs its operation (apply) only when IsoRim Waylith's Api.Version is at least minVersion; otherwise does nothing (and
    // counts as done, so no patch error). The apparel bindings (Patches/VFEM2_Apparel.xml) are IsoRim.ApparelModelDef defs,
    // a type an IsoRim older than Api 9 does not have: added as XML only when it does, so the pack loads either way.
    public class PatchOperation_IfIsoRimApi : PatchOperation
    {
        public int minVersion;
        public PatchOperation apply;

        protected override bool ApplyWorker(XmlDocument xml)
        {
            int have;
            try
            {
                have = IsoRim.Api.Version;
            }
            catch (Exception)
            {
                have = 0;
            }
            if (have < minVersion || apply == null)
            {
                return true;
            }
            return apply.Apply(xml);
        }
    }
}
