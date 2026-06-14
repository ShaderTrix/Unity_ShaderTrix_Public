using UnityEngine;
using UnityEngine.UI;

namespace NativeTreesJobs
{
    public static class UIOcclusionRules
    {
        public static bool IsValidGraphic(Graphic g) =>
        IsFullyOpaque(g) &&
        NoMask(g) &&
        NoTag(g);

        private static bool IsFullyOpaque(Graphic g) => GetEffectiveAlpha(g) >= 0.999f;
        private static bool NoMask(Graphic g) => !g.TryGetComponent<Mask>(out _);
        private static bool NoTag(Graphic g) => g.gameObject.tag == "IgnoreUICulling" ? false : true;
        private static float GetEffectiveAlpha(Graphic g)
        {
            if (!g) return 0f;
            float a = g.color.a;

            // Parent CanvasGroups (respect ignoreParentGroups)
            Transform t = g.transform;
            while (t != null)
            {
                if (t.TryGetComponent<CanvasGroup>(out var cg))
                {
                    a *= Mathf.Clamp01(cg.alpha);
                    if (cg.ignoreParentGroups) break; // don't include ancestors above this group
                }
                t = t.parent;
            }

            // CanvasRenderer alpha (can be driven by CrossFadeAlpha/animations)
            a *= Mathf.Clamp01(g.canvasRenderer.GetAlpha());
            // // Material color alpha if present
            // if (g is MaskableGraphic mg && mg.material != null && mg.material.HasProperty("_Color"))
            //     a *= mg.material.color.a;
            return Mathf.Clamp01(a);
        }
    }
}
