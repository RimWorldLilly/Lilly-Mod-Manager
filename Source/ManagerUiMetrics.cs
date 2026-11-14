using UnityEngine;

namespace Lilly.IntegratedModManager
{
    internal static class ManagerUiMetrics
    {
        internal static float VerticalScrollbarWidth
        {
            get
            {
                GUIStyle style = GUI.skin == null ? null : GUI.skin.verticalScrollbar;
                if (style == null)
                    return 16f;
                float width = Mathf.Max(style.fixedWidth, style.CalcSize(new GUIContent(" ")).x);
                return width > 0f ? width : 16f;
            }
        }

        internal static float HorizontalScrollbarHeight
        {
            get
            {
                GUIStyle style = GUI.skin == null ? null : GUI.skin.horizontalScrollbar;
                if (style == null)
                    return 16f;
                float height = Mathf.Max(style.fixedHeight, style.CalcSize(new GUIContent(" ")).y);
                return height > 0f ? height : 16f;
            }
        }
    }
}
