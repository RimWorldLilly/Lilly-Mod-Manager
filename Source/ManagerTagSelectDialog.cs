using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class ManagerTagSelectDialog : Window
    {
        private const float RowHeight = 36f;
        private readonly IList<ManagerTagData> tags;
        private readonly Action<ManagerTagData> onSelect;
        private readonly Func<string, bool> onCreate;
        private Vector2 scrollPosition;
        private string newTagName = "";
        private bool focusNameOnNextDraw = true;

        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(UI.screenWidth - 80f, 460f),
            Mathf.Min(UI.screenHeight - 100f, 520f));

        internal ManagerTagSelectDialog(
            IList<ManagerTagData> tags,
            Action<ManagerTagData> onSelect,
            Func<string, bool> onCreate)
        {
            this.tags = tags ?? new List<ManagerTagData>();
            this.onSelect = onSelect;
            this.onCreate = onCreate;
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
                CreateTag();
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
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), ManagerTranslation.Get("LMM_0047"));
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 38f, rect.width, 26f), ManagerTranslation.Get("LMM_0138"));

            float listY = 66f;
            float listHeight = rect.height - 166f;
            Rect listRect = new Rect(0f, listY, rect.width, listHeight);
            float contentHeight = Mathf.Max(listRect.height, tags.Count * RowHeight);
            float scrollbarWidth = contentHeight > listRect.height
                ? ManagerUiMetrics.VerticalScrollbarWidth : 0f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - scrollbarWidth, contentHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            for (int i = 0; i < tags.Count; i++)
            {
                ManagerTagData tag = tags[i];
                if (tag == null || tag.IsSystem)
                    continue;

                Rect rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                Widgets.DrawBoxSolid(rowRect, i % 2 == 0
                    ? new Color(0.075f, 0.075f, 0.075f, 1f)
                    : new Color(0.12f, 0.12f, 0.12f, 1f));
                Widgets.Label(new Rect(8f, rowRect.y + 4f,
                    Mathf.Max(40f, rowRect.width - 100f), 28f), tag.Name);
                if (Widgets.ButtonText(new Rect(rowRect.xMax - 92f, rowRect.y + 4f,
                    84f, 28f), ManagerTranslation.Get("LMM_0137")))
                {
                    if (onSelect != null)
                        onSelect(tag);
                    Close();
                    Widgets.EndScrollView();
                    return;
                }
            }
            Widgets.EndScrollView();

            float inputY = listRect.yMax + 8f;
            GUI.SetNextControlName("LillyModManagerNewTagInput");
            newTagName = Widgets.TextField(new Rect(0f, inputY, rect.width, 30f), newTagName);
            if (focusNameOnNextDraw)
            {
                GUI.FocusControl("LillyModManagerNewTagInput");
                focusNameOnNextDraw = false;
            }

            float buttonY = rect.height - 34f;
            if (Widgets.ButtonText(new Rect(rect.width - 250f, buttonY, 120f, 30f),
                ManagerTranslation.Get("LMM_0124")))
                Close();
            if (Widgets.ButtonText(new Rect(rect.width - 120f, buttonY, 120f, 30f),
                ManagerTranslation.Get("LMM_0139")))
                CreateTag();
        }

        private void CreateTag()
        {
            string trimmed = newTagName == null ? "" : newTagName.Trim();
            if (trimmed.NullOrEmpty())
            {
                Messages.Message(ManagerTranslation.Get("LMM_0064"),
                    RimWorld.MessageTypeDefOf.RejectInput);
                return;
            }
            if (onCreate != null && onCreate(trimmed))
                Close();
        }
    }
}
