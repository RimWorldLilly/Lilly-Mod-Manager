using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class ManagerPresetSaveDialog : Window
    {
        private readonly Action<ManagerPresetData> onDelete;
        private const float RowHeight = 38f;
        private readonly IList<ManagerPresetData> presets;
        private readonly List<FileInfo> vanillaLists;
        private readonly Action<string, ManagerPresetData> onSave;
        private readonly Action<string, Action> onSaveVanillaList;
        private Vector2 presetScrollPosition;
        private Vector2 vanillaScrollPosition;
        private string inputName = "";
        private bool focusNameOnNextDraw = true;

        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(UI.screenWidth - 80f, 1000f),
            Mathf.Min(UI.screenHeight - 100f, 620f));

        internal ManagerPresetSaveDialog(
            IList<ManagerPresetData> presets,
            Action<string, ManagerPresetData> onSave,
            Action<string, Action> onSaveVanillaList,
            Action<ManagerPresetData> onDelete)
        {
            this.presets = presets ?? new List<ManagerPresetData>();
            vanillaLists = new List<FileInfo>();
            RefreshVanillaLists();
            this.onSave = onSave;
            this.onSaveVanillaList = onSaveVanillaList;
            this.onDelete = onDelete;

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            closeOnAccept = false;
        }

        public override void DoWindowContents(Rect rect)
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                SaveNewPreset();
                e.Use();
                return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                Close();
                e.Use();
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), ManagerTranslation.Get("LMM_0117"));
            Text.Font = GameFont.Small;
            GUI.SetNextControlName("LillyModManagerPresetNameInput");
            inputName = Widgets.TextField(new Rect(0f, 38f, rect.width, 32f), inputName);
            if (focusNameOnNextDraw)
            {
                GUI.FocusControl("LillyModManagerPresetNameInput");
                focusNameOnNextDraw = false;
            }

            float headingY = 80f;
            const float columnGap = 14f;
            float columnWidth = (rect.width - columnGap) * 0.5f;
            Rect vanillaHeading = new Rect(0f, headingY, columnWidth, 26f);
            Rect presetHeading = new Rect(columnWidth + columnGap, headingY, columnWidth, 26f);
            Widgets.Label(vanillaHeading, ManagerTranslation.Get("LMM_0130"));
            Widgets.DrawBox(vanillaHeading);
            Widgets.Label(presetHeading, ManagerTranslation.Get("LMM_0131"));
            Widgets.DrawBox(presetHeading);
            float listY = headingY + 28f;
            float listHeight = rect.height - listY - 44f;
            DrawVanillaLists(new Rect(0f, listY, columnWidth, listHeight));
            DrawPresets(new Rect(columnWidth + columnGap, listY, columnWidth, listHeight));

            float buttonY = rect.height - 34f;
            if (Widgets.ButtonText(new Rect(0f, buttonY, 190f, 30f),
                ManagerTranslation.Get("LMM_0160")))
                SaveVanillaList();
            if (Widgets.ButtonText(new Rect(rect.width - 250f, buttonY, 120f, 30f),
                ManagerTranslation.Get("LMM_0124")))
                Close();
            if (Widgets.ButtonText(new Rect(rect.width - 120f, buttonY, 120f, 30f),
                ManagerTranslation.Get("LMM_0135")))
                SaveNewPreset();
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
                Widgets.Label(new Rect(8f, rowRect.y + 4f,
                    Mathf.Max(40f, rowRect.width - 164f), 28f), name);
                if (Widgets.ButtonText(new Rect(rowRect.xMax - 152f, rowRect.y + 4f,
                    84f, 28f), ManagerTranslation.Get("LMM_0134")))
                    SaveVanillaList(name);
                if (Widgets.ButtonText(new Rect(rowRect.xMax - 62f, rowRect.y + 4f,
                    54f, 28f), ManagerTranslation.Get("LMM_0045")))
                    ConfirmDeleteVanillaList(file);
            }
            Widgets.EndScrollView();
        }

        private void RefreshVanillaLists()
        {
            vanillaLists.Clear();
            vanillaLists.AddRange(GenFilePaths.AllModListFiles
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase));
        }

        private void ConfirmDeleteVanillaList(FileInfo file)
        {
            if (file == null)
                return;
            string name = Path.GetFileNameWithoutExtension(file.Name);
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                ManagerTranslation.Get("LMM_0164") + name,
                () =>
                {
                    try
                    {
                        if (file.Exists)
                            file.Delete();
                        vanillaLists.Remove(file);
                        Messages.Message(ManagerTranslation.Get("LMM_0165") + name,
                            RimWorld.MessageTypeDefOf.PositiveEvent);
                    }
                    catch (Exception exception)
                    {
                        Log.Warning("Failed to delete vanilla mod list '" + name + "': " + exception);
                        Messages.Message(ManagerTranslation.Get("LMM_0166"),
                            RimWorld.MessageTypeDefOf.RejectInput);
                    }
                }, destructive: true));
        }
private void ConfirmDeletePreset(ManagerPresetData preset)
{
    if (preset == null || onDelete == null)
        return;

    Find.WindowStack.Add(
        Dialog_MessageBox.CreateConfirmation(
            ManagerTranslation.Get("LMM_0164") + preset.Name,
            () =>
            {
                onDelete(preset);
            },
            destructive: true));
}
private void DrawPresets(Rect listRect)
{
    float contentHeight = Mathf.Max(listRect.height, presets.Count * RowHeight);
    float scrollbarWidth = contentHeight > listRect.height
        ? ManagerUiMetrics.VerticalScrollbarWidth : 0f;

    Rect viewRect = new Rect(
        0f, 0f,
        listRect.width - scrollbarWidth,
        contentHeight);

    Widgets.BeginScrollView(
        listRect, ref presetScrollPosition, viewRect);

    for (int i = 0; i < presets.Count; i++)
    {
        ManagerPresetData preset = presets[presets.Count - 1 - i];
        if (preset == null)
            continue;

        Rect rowRect = new Rect(
            0f, i * RowHeight,
            viewRect.width, RowHeight);

        DrawAlternatingRow(rowRect, i);

        Widgets.Label(
            new Rect(8f, rowRect.y + 4f,
                Mathf.Max(40f, rowRect.width - 190f), 28f),
            preset.Name);

        // 덮어쓰기
        if (Widgets.ButtonText(
            new Rect(rowRect.xMax - 176f, rowRect.y + 4f,
                82f, 28f),
            ManagerTranslation.Get("LMM_0134")))
        {
            ConfirmOverwrite(preset);
        }

        // 삭제
        if (Widgets.ButtonText(
            new Rect(rowRect.xMax - 86f, rowRect.y + 4f,
                78f, 28f),
            ManagerTranslation.Get("LMM_0045")))
        {
            ConfirmDeletePreset(preset);
        }
    }

    Widgets.EndScrollView();
}

        private static void DrawAlternatingRow(Rect rect, int index)
        {
            Widgets.DrawBoxSolid(rect, index % 2 == 0
                ? new Color(0.075f, 0.075f, 0.075f, 1f)
                : new Color(0.12f, 0.12f, 0.12f, 1f));
        }

        private void SaveVanillaList()
        {
            string name = inputName == null ? "" : inputName.Trim();
            SaveVanillaList(name);
        }

        private void SaveVanillaList(string name)
        {
            if (name.NullOrEmpty())
            {
                Messages.Message(ManagerTranslation.Get("LMM_0064"),
                    RimWorld.MessageTypeDefOf.RejectInput);
                return;
            }
            if (onSaveVanillaList != null)
                onSaveVanillaList(name, RefreshVanillaLists);
        }

        private void SaveNewPreset()
        {
            string name = inputName == null ? "" : inputName.Trim();
            if (name.NullOrEmpty())
            {
                Messages.Message(ManagerTranslation.Get("LMM_0064"),
                    RimWorld.MessageTypeDefOf.RejectInput);
                return;
            }
            if (presets.Any(preset => preset != null &&
                string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                Messages.Message(ManagerTranslation.Get("LMM_0018"),
                    RimWorld.MessageTypeDefOf.RejectInput);
                return;
            }

            if (onSave != null)
                onSave(name, null);
            Close();
        }

        private void ConfirmOverwrite(ManagerPresetData preset)
        {
            if (preset == null)
                return;
            Find.WindowStack.Add(new Dialog_MessageBox(
                ManagerTranslation.Get("LMM_0136") + preset.Name,
                ManagerTranslation.Get("LMM_0134"),
                () =>
                {
                    if (onSave != null)
                        onSave(preset.Name, preset);
                    Close();
                },
                ManagerTranslation.Get("LMM_0074"), null,
                null, false, null, null, WindowLayer.Dialog));
        }
    }
}
