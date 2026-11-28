using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class ManagerApplyChangesDialog : Window
    {
        private const float RowHeight = 27f;
        private readonly List<string> beforeIds;
        private readonly List<string> afterIds;
        private readonly Func<string, string> getDisplayName;
        private readonly Action cancelChanges;
        private readonly Action<bool> applyChanges;
        private readonly Action loadSelection;
        private readonly Dictionary<string, int> beforePositions;
        private readonly Dictionary<string, int> afterPositions;
        private readonly HashSet<string> movedIds;
        private readonly int addedCount;
        private readonly int removedCount;
        private readonly Vector2[] scroll = new Vector2[2];

        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(
                    Mathf.Clamp(UI.screenWidth - 80f, 680f, 1240f),
                    Mathf.Clamp(UI.screenHeight - 80f, 500f, 760f));
            }
        }

        internal ManagerApplyChangesDialog(
            IList<string> beforePackageIds,
            IList<string> afterPackageIds,
            Func<string, string> getDisplayName,
            Action cancelChanges,
            Action<bool> applyChanges)
            : this(beforePackageIds, afterPackageIds, getDisplayName,
                cancelChanges, applyChanges, null)
        {
        }

        internal ManagerApplyChangesDialog(
            IList<string> beforePackageIds,
            IList<string> afterPackageIds,
            Func<string, string> getDisplayName,
            Action cancelChanges,
            Action loadSelection)
            : this(beforePackageIds, afterPackageIds, getDisplayName,
                cancelChanges, null, loadSelection)
        {
        }

        private ManagerApplyChangesDialog(
            IList<string> beforePackageIds,
            IList<string> afterPackageIds,
            Func<string, string> getDisplayName,
            Action cancelChanges,
            Action<bool> applyChanges,
            Action loadSelection)
        {
            beforeIds = CopyIds(beforePackageIds);
            afterIds = CopyIds(afterPackageIds);
            this.getDisplayName = getDisplayName;
            this.cancelChanges = cancelChanges;
            this.applyChanges = applyChanges;
            this.loadSelection = loadSelection;
            beforePositions = BuildPositions(beforeIds);
            afterPositions = BuildPositions(afterIds);
            movedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, int> pair in beforePositions)
            {
                int newIndex;
                if (afterPositions.TryGetValue(pair.Key, out newIndex) && pair.Value != newIndex)
                    movedIds.Add(pair.Key);
            }

            addedCount = afterIds.Count(id => !beforePositions.ContainsKey(id));
            removedCount = beforeIds.Count(id => !afterPositions.ContainsKey(id));
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(0f, 0f, rect.width - 34f, 30f),
                    ManagerTranslation.Get("LMM_0213"));

                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(0f, 32f, rect.width, 24f),
                    string.Format(ManagerTranslation.Get("LMM_0216"),
                        addedCount, removedCount, movedIds.Count));

                const float gap = 12f;
                const float top = 62f;
                const float footer = 70f;
                float width = (rect.width - gap) * 0.5f;
                float height = Mathf.Max(100f, rect.height - top - footer);
                DrawColumn(new Rect(0f, top, width, height), "LMM_0214", beforeIds, afterPositions, 0);
                DrawColumn(new Rect(width + gap, top, width, height), "LMM_0215", afterIds, beforePositions, 1);

                Text.Font = GameFont.Tiny;
                Rect legend = new Rect(0f, rect.height - 66f, rect.width, 22f);
                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 0.58f, 0.16f, 1f);
                Widgets.Label(legend, ManagerTranslation.Get("LMM_0217"));
                GUI.color = oldColor;

                Text.Font = GameFont.Small;
                if (loadSelection != null)
                {
                    float loadButtonGap = 8f;
                    float loadButtonWidth = (rect.width - loadButtonGap) * 0.5f;
                    float loadButtonY = rect.height - 36f;

                    if (Widgets.ButtonText(
                        new Rect(0f, loadButtonY, loadButtonWidth, 30f),
                        ManagerTranslation.Get("LMM_0223")))
                    {
                        Close();
                        return;
                    }

                    if (Widgets.ButtonText(
                        new Rect(loadButtonWidth + loadButtonGap, loadButtonY, loadButtonWidth, 30f),
                        ManagerTranslation.Get("LMM_0123")))
                    {
                        Close();
                        loadSelection();
                        return;
                    }

                    return;
                }

                float buttonGap = 8f;
                float buttonWidth = (rect.width - buttonGap * 3f) * 0.25f;
                float x = 0f;
                float y = rect.height - 36f;

                if (Widgets.ButtonText(new Rect(x, y, buttonWidth, 30f), ManagerTranslation.Get("LMM_0219")))
                {
                    Close();
                    if (cancelChanges != null)
                        cancelChanges();
                    return;
                }
                x += buttonWidth + buttonGap;
                if (Widgets.ButtonText(new Rect(x, y, buttonWidth, 30f), ManagerTranslation.Get("LMM_0220")))
                {
                    Close();
                    return;
                }
                x += buttonWidth + buttonGap;
                if (Widgets.ButtonText(new Rect(x, y, buttonWidth, 30f), ManagerTranslation.Get("LMM_0221")))
                {
                    Close();
                    if (applyChanges != null)
                        applyChanges(true);
                    return;
                }
                x += buttonWidth + buttonGap;
                if (Widgets.ButtonText(new Rect(x, y, buttonWidth, 30f), ManagerTranslation.Get("LMM_0222")))
                {
                    Close();
                    if (applyChanges != null)
                        applyChanges(false);
                    return;
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
            }
        }

        private void DrawColumn(
            Rect rect,
            string headingKey,
            IList<string> ids,
            Dictionary<string, int> otherPositions,
            int scrollIndex)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.065f, 0.065f, 0.065f, 1f));
            Widgets.DrawBox(rect);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, 28f),
                string.Format(ManagerTranslation.Get(headingKey), ids.Count));

            Rect viewport = new Rect(rect.x + 4f, rect.y + 32f, rect.width - 8f,
                Mathf.Max(40f, rect.height - 36f));
            float contentHeight = Mathf.Max(viewport.height, ids.Count * RowHeight);
            float barWidth = contentHeight > viewport.height
                ? ManagerUiMetrics.VerticalScrollbarWidth : 0f;
            Rect view = new Rect(0f, 0f, Mathf.Max(0f, viewport.width - barWidth), contentHeight);
            Widgets.BeginScrollView(viewport, ref scroll[scrollIndex], view);

            if (ids.Count == 0)
            {
                Widgets.Label(new Rect(8f, 4f, Mathf.Max(40f, view.width - 16f), 24f),
                    ManagerTranslation.Get("LMM_0218"));
            }
            else
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    string id = ids[i];
                    Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                    Widgets.DrawBoxSolid(row, i % 2 == 0
                        ? new Color(0.09f, 0.09f, 0.09f, 1f)
                        : new Color(0.13f, 0.13f, 0.13f, 1f));

                    if (movedIds.Contains(id) && otherPositions.ContainsKey(id))
                    {
                        Color oldColor = GUI.color;
                        GUI.color = new Color(1f, 0.58f, 0.16f, 1f);
                        Widgets.DrawBox(row.ContractedBy(1f), 2);
                        GUI.color = oldColor;
                    }

                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(row.x + 8f, row.y + 2f, 34f, RowHeight - 4f),
                        (i + 1).ToString());
                    Text.Font = GameFont.Small;
                    string name = getDisplayName == null ? id : getDisplayName(id);
                    Widgets.Label(new Rect(row.x + 44f, row.y + 2f,
                        Mathf.Max(30f, row.width - 52f), RowHeight - 4f),
                        string.IsNullOrEmpty(name) ? id : name);
                }
            }

            Widgets.EndScrollView();
        }

        private static List<string> CopyIds(IList<string> source)
        {
            return source == null ? new List<string>()
                : source.Where(id => !string.IsNullOrEmpty(id)).ToList();
        }

        private static Dictionary<string, int> BuildPositions(IList<string> ids)
        {
            Dictionary<string, int> result =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ids.Count; i++)
                if (!result.ContainsKey(ids[i]))
                    result.Add(ids[i], i);
            return result;
        }
    }
}
