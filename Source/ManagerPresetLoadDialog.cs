using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class ManagerPresetLoadDialog : Window
    {
        private const float RowHeight = 42f;
        private readonly IList<ManagerPresetData> presets;
        private readonly Action<ManagerPresetData> onLoad;
        private readonly Action<ManagerPresetData> onDelete;
        private Vector2 scrollPosition;

        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(Mathf.Min(UI.screenWidth - 80f, 620f),
                    Mathf.Min(UI.screenHeight - 100f, 500f));
            }
        }

        internal ManagerPresetLoadDialog(
            IList<ManagerPresetData> presets,
            Action<ManagerPresetData> onLoad,
            Action<ManagerPresetData> onDelete)
        {
            this.presets = presets ?? new List<ManagerPresetData>();
            this.onLoad = onLoad;
            this.onDelete = onDelete;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            closeOnAccept = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 34f), ManagerTranslation.Get("LMM_0122"));
            Text.Font = GameFont.Small;
            Widgets.DrawBoxSolid(new Rect(0f, 36f, rect.width, 1f),
                new Color(0.45f, 0.39f, 0.28f, 1f));

            Rect listRect = new Rect(0f, 44f, rect.width, rect.height - 84f);
            float contentHeight = Mathf.Max(listRect.height, presets.Count * RowHeight);
            float scrollbarWidth = contentHeight > listRect.height
                ? ManagerUiMetrics.VerticalScrollbarWidth
                : 0f;
            Rect viewRect = new Rect(0f, 0f,
                listRect.width - scrollbarWidth, contentHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            for (int i = 0; i < presets.Count; i++)
            {
                ManagerPresetData preset = presets[i];
                if (preset == null)
                    continue;

                Rect rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                Widgets.DrawBoxSolid(rowRect, i % 2 == 0
                    ? new Color(0.075f, 0.075f, 0.075f, 1f)
                    : new Color(0.12f, 0.12f, 0.12f, 1f));
                Widgets.Label(new Rect(10f, rowRect.y + 5f,
                    Mathf.Max(80f, rowRect.width - 210f), 30f), preset.Name);

                Rect loadRect = new Rect(rowRect.xMax - 184f, rowRect.y + 5f, 94f, 30f);
                if (Widgets.ButtonText(loadRect, ManagerTranslation.Get("LMM_0123")))
                {
                    Close();
                    if (onLoad != null)
                        onLoad(preset);
                    Widgets.EndScrollView();
                    return;
                }

                Rect deleteRect = new Rect(rowRect.xMax - 82f, rowRect.y + 5f, 72f, 30f);
                if (Widgets.ButtonText(deleteRect, ManagerTranslation.Get("LMM_0045")) && onDelete != null)
                    onDelete(preset);
            }
            Widgets.EndScrollView();

            if (Widgets.ButtonText(new Rect(rect.width - 120f, rect.height - 34f,
                120f, 30f), ManagerTranslation.Get("LMM_0124")))
                Close();
        }
    }
}
