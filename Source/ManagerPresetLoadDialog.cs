using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class ManagerPresetLoadDialog : Window
    {
        private const float RowHeight = 38f;
        private readonly IList<ManagerPresetData> presets;
        private readonly List<FileInfo> vanillaLists;
        private readonly Action<ManagerPresetData> onLoad;
        private readonly Action<ManagerPresetData> onDelete;
        private readonly Action<IList<string>> onImportVanillaList;
        private Vector2 presetScrollPosition;
        private Vector2 vanillaScrollPosition;

        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(UI.screenWidth - 80f, 1040f),
            Mathf.Min(UI.screenHeight - 100f, 620f));

        internal ManagerPresetLoadDialog(
            IList<ManagerPresetData> presets,
            Action<ManagerPresetData> onLoad,
            Action<ManagerPresetData> onDelete,
            Action<IList<string>> onImportVanillaList)
        {
            this.presets = presets ?? new List<ManagerPresetData>();
            this.onLoad = onLoad;
            this.onDelete = onDelete;
            this.onImportVanillaList = onImportVanillaList;
            vanillaLists = GenFilePaths.AllModListFiles
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToList();
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

            const float gap = 16f;
            float columnWidth = (rect.width - gap) * 0.5f;
            float headingY = 44f;
            float listY = 76f;
            float listHeight = rect.height - 116f;
            DrawListColumn(new Rect(0f, headingY, columnWidth, 28f),
                ManagerTranslation.Get("LMM_0130"));
            DrawListColumn(new Rect(columnWidth + gap, headingY, columnWidth, 28f),
                ManagerTranslation.Get("LMM_0131"));

            Rect vanillaRect = new Rect(0f, listY, columnWidth, listHeight);
            DrawVanillaLists(vanillaRect);
            Rect presetRect = new Rect(columnWidth + gap, listY, columnWidth, listHeight);
            DrawPresets(presetRect);

            if (Widgets.ButtonText(new Rect(rect.width - 120f, rect.height - 34f,
                120f, 30f), ManagerTranslation.Get("LMM_0124")))
                Close();
        }

        private static void DrawListColumn(Rect rect, string title)
        {
            Widgets.Label(rect, title);
            Widgets.DrawBox(rect);
        }

        private void DrawVanillaLists(Rect listRect)
        {
            float contentHeight = Mathf.Max(listRect.height, vanillaLists.Count * RowHeight);
            float scrollbarWidth = contentHeight > listRect.height
                ? ManagerUiMetrics.VerticalScrollbarWidth : 0f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - scrollbarWidth, contentHeight);
            Widgets.BeginScrollView(listRect, ref vanillaScrollPosition, viewRect);
            for (int i = 0; i < vanillaLists.Count; i++)
            {
                FileInfo file = vanillaLists[i];
                Rect rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                DrawAlternatingRow(rowRect, i);
                string name = Path.GetFileNameWithoutExtension(file.Name);
                Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y + 4f,
                    Mathf.Max(40f, rowRect.width - 112f), 28f), name);
                if (Widgets.ButtonText(new Rect(rowRect.xMax - 96f, rowRect.y + 4f,
                    88f, 28f), ManagerTranslation.Get("LMM_0123")))
                {
                    List<string> activeIds;
                    if (TryReadVanillaModList(file, out activeIds))
                    {
                        Close();
                        if (onImportVanillaList != null)
                            onImportVanillaList(activeIds);
                    }
                    else
                    {
                        Messages.Message(ManagerTranslation.Get("LMM_0132") + name,
                            MessageTypeDefOf.RejectInput);
                    }
                    Widgets.EndScrollView();
                    return;
                }
            }
            Widgets.EndScrollView();
        }

        private void DrawPresets(Rect listRect)
        {
            float contentHeight = Mathf.Max(listRect.height, presets.Count * RowHeight);
            float scrollbarWidth = contentHeight > listRect.height
                ? ManagerUiMetrics.VerticalScrollbarWidth : 0f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - scrollbarWidth, contentHeight);
            Widgets.BeginScrollView(listRect, ref presetScrollPosition, viewRect);
            for (int i = 0; i < presets.Count; i++)
            {
                ManagerPresetData preset = presets[presets.Count - 1 - i];
                if (preset == null)
                    continue;

                Rect rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                DrawAlternatingRow(rowRect, i);
                Widgets.Label(new Rect(8f, rowRect.y + 4f,
                    Mathf.Max(40f, rowRect.width - 190f), 28f), preset.Name);
                if (Widgets.ButtonText(new Rect(rowRect.xMax - 176f, rowRect.y + 4f,
                    88f, 28f), ManagerTranslation.Get("LMM_0123")))
                {
                    Close();
                    if (onLoad != null)
                        onLoad(preset);
                    Widgets.EndScrollView();
                    return;
                }

                if (Widgets.ButtonText(new Rect(rowRect.xMax - 82f, rowRect.y + 4f,
                    74f, 28f), ManagerTranslation.Get("LMM_0045")) && onDelete != null)
                    onDelete(preset);
            }
            Widgets.EndScrollView();
        }

        private static void DrawAlternatingRow(Rect rect, int index)
        {
            Widgets.DrawBoxSolid(rect, index % 2 == 0
                ? new Color(0.075f, 0.075f, 0.075f, 1f)
                : new Color(0.12f, 0.12f, 0.12f, 1f));
        }

        private static bool TryReadVanillaModList(FileInfo file, out List<string> activeIds)
        {
            activeIds = null;
            try
            {
                RimWorld.ModList modList;
                if (!GameDataSaveLoader.TryLoadModList(file.FullName, out modList) || modList == null)
                    return false;

                var idsField = typeof(RimWorld.ModList).GetField("ids",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                activeIds = idsField == null
                    ? null
                    : idsField.GetValue(modList) as List<string>;
                return activeIds != null;
            }
            catch (Exception exception)
            {
                Log.Warning("Failed to load vanilla mod list " + file.FullName + ": " + exception);
                Scribe.ForceStop();
                return false;
            }
        }
    }
}
