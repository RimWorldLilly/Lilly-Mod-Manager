using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class ManagerTagSelectDialog : Window
    {
        private const float RowHeight = 36f;
        private readonly IList<ManagerTagData> tags;
        private readonly List<string> packageIds;
        private readonly Func<ManagerTagData, bool> isAssigned;
        private readonly Action<ManagerTagData, bool> onToggle;
        private readonly Func<string, bool> onCreate;
        private Vector2 scrollPosition;
        private string newTagName = "";
        private bool focusNameOnNextDraw = true;

        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(UI.screenWidth - 80f, 460f),
            Mathf.Min(UI.screenHeight - 100f, 700f));

        internal ManagerTagSelectDialog(
            IList<ManagerTagData> tags,
            IEnumerable<string> packageIds,
            Func<ManagerTagData, bool> isAssigned,
            Action<ManagerTagData, bool> onToggle,
            Func<string, bool> onCreate)
        {
            this.tags = tags ?? new List<ManagerTagData>();
            this.packageIds = packageIds == null
                ? new List<string>()
                : packageIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            this.isAssigned = isAssigned;
            this.onToggle = onToggle;
            this.onCreate = onCreate;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            closeOnAccept = false;
        }

        public override void DoWindowContents(Rect rect)
        {
            Event e = Event.current;
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

            float inputY = rect.height - 76f;
            float listY = 66f;
            float listHeight = Mathf.Max(80f, inputY - listY - 8f);
            Rect listRect = new Rect(0f, listY, rect.width, listHeight);
            int visibleTagCount = tags.Count(tag => tag != null && !tag.IsSystem);
            float contentHeight = Mathf.Max(listRect.height, visibleTagCount * RowHeight);
            float scrollbarWidth = contentHeight > listRect.height
                ? ManagerUiMetrics.VerticalScrollbarWidth : 0f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - scrollbarWidth, contentHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

            int rowIndex = 0;
            for (int i = 0; i < tags.Count; i++)
            {
                ManagerTagData tag = tags[i];
                if (tag == null || tag.IsSystem)
                    continue;

                Rect rowRect = new Rect(0f, rowIndex * RowHeight, viewRect.width, RowHeight);
                Widgets.DrawBoxSolid(rowRect, rowIndex % 2 == 0
                    ? new Color(0.075f, 0.075f, 0.075f, 1f)
                    : new Color(0.12f, 0.12f, 0.12f, 1f));

                bool assigned = packageIds.Count > 0 && isAssigned != null && isAssigned(tag);
                bool newAssigned = assigned;
                Rect checkboxRect = new Rect(rowRect.x + 7f, rowRect.y + 6f, 24f, 24f);
                Widgets.Checkbox(checkboxRect.position, ref newAssigned, checkboxRect.width);
                if (newAssigned != assigned && onToggle != null)
                    onToggle(tag, newAssigned);

                Rect tagRect = new Rect(rowRect.x + 38f, rowRect.y + 4f,
                    rowRect.width - 46f, 28f);
                Color tagBackground = ManagerTagColors.Background(tag.ColorHex);
                Widgets.DrawBoxSolid(tagRect, tagBackground);
                Widgets.DrawBox(tagRect);
                GameFont oldFont = Text.Font;
                Color oldColor = GUI.color;
                Text.Font = GameFont.Small;
                GUI.color = ManagerTagColors.Foreground(tag.ColorHex);
                Widgets.Label(new Rect(tagRect.x + 7f, tagRect.y,
                    tagRect.width - 14f, tagRect.height), tag.Name);
                GUI.color = oldColor;
                Text.Font = oldFont;

                // 우측 태그 라벨 영역도 체크박스와 동일하게 클릭해 선택 상태를 전환한다.
                if (Widgets.ButtonInvisible(tagRect) && onToggle != null)
                    onToggle(tag, !assigned);

                rowIndex++;
            }
            Widgets.EndScrollView();

            const float createButtonWidth = 104f;
            GUI.SetNextControlName("LillyModManagerNewTagInput");
            newTagName = Widgets.TextField(
                new Rect(0f, inputY, rect.width - createButtonWidth - 8f, 30f),
                newTagName);
            if (focusNameOnNextDraw)
            {
                GUI.FocusControl("LillyModManagerNewTagInput");
                focusNameOnNextDraw = false;
            }
            if (Widgets.ButtonText(new Rect(rect.width - createButtonWidth, inputY,
                createButtonWidth, 30f), ManagerTranslation.Get("LMM_0139")))
                CreateTag();

            float buttonY = rect.height - 34f;
            if (Widgets.ButtonText(new Rect(rect.width - 120f, buttonY, 120f, 30f),
                ManagerTranslation.Get("LMM_0124")))
                Close();
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
            {
                newTagName = "";
                // 생성 직후 창을 유지하여 갱신된 태그 목록을 계속 편집할 수 있다.
                GUI.FocusControl("LillyModManagerNewTagInput");
            }
        }
    }
}