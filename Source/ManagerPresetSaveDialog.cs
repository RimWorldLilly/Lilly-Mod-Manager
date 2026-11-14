using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class ManagerPresetSaveDialog : Window
    {
        private const float RowHeight = 38f;
        private readonly IList<ManagerPresetData> presets;
        private readonly Action<string, ManagerPresetData> onSave;
        private Vector2 scrollPosition;
        private string inputName = "";
        private bool focusNameOnNextDraw = true;

        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(UI.screenWidth - 80f, 600f),
            Mathf.Min(UI.screenHeight - 100f, 520f));

        internal ManagerPresetSaveDialog(
            IList<ManagerPresetData> presets,
            Action<string, ManagerPresetData> onSave)
        {
            this.presets = presets ?? new List<ManagerPresetData>();
            this.onSave = onSave;
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
            Widgets.Label(new Rect(0f, headingY, rect.width, 26f),
                ManagerTranslation.Get("LMM_0131"));
            float listY = headingY + 28f;
            float listHeight = rect.height - listY - 44f;
            Rect listRect = new Rect(0f, listY, rect.width, listHeight);
            float contentHeight = Mathf.Max(listRect.height, presets.Count * RowHeight);
            float scrollbarWidth = contentHeight > listRect.height
                ? ManagerUiMetrics.VerticalScrollbarWidth : 0f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - scrollbarWidth, contentHeight);
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
                Widgets.Label(new Rect(8f, rowRect.y + 4f,
                    Mathf.Max(40f, rowRect.width - 120f), 28f), preset.Name);
                if (Widgets.ButtonText(new Rect(rowRect.xMax - 104f, rowRect.y + 4f,
                    96f, 28f), ManagerTranslation.Get("LMM_0134")))
                    ConfirmOverwrite(preset);
            }
            Widgets.EndScrollView();

            float buttonY = rect.height - 34f;
            if (Widgets.ButtonText(new Rect(rect.width - 250f, buttonY, 120f, 30f),
                ManagerTranslation.Get("LMM_0124")))
                Close();
            if (Widgets.ButtonText(new Rect(rect.width - 120f, buttonY, 120f, 30f),
                ManagerTranslation.Get("LMM_0135")))
                SaveNewPreset();
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
