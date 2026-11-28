using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class ManagerApplyChangesDialog : Window
    {
        internal sealed class TagMetadata
        {
            internal readonly string Name;
            internal readonly string ColorHex;

            internal TagMetadata(string name, string colorHex)
            {
                Name = name;
                ColorHex = colorHex;
            }
        }

        internal sealed class ModMetadata
        {
            internal readonly string CategoryLabel;
            internal readonly string CategoryColorHex;
            internal readonly List<TagMetadata> Tags = new List<TagMetadata>();

            internal ModMetadata(string categoryLabel, string categoryColorHex)
            {
                CategoryLabel = categoryLabel;
                CategoryColorHex = categoryColorHex;
            }
        }

        internal sealed class ComparisonData
        {
            internal readonly List<string> PackageIds;
            internal readonly Dictionary<string, ModMetadata> Metadata;

            internal ComparisonData(
                IList<string> packageIds,
                Dictionary<string, ModMetadata> metadata)
            {
                PackageIds = CopyIds(packageIds);
                Metadata = metadata ?? new Dictionary<string, ModMetadata>(
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        private const float RowHeight = 27f;
        private readonly List<string> beforeIds;
        private readonly List<string> afterIds;
        private readonly Func<string, string> getDisplayName;
        private readonly IDictionary<string, ModMetadata> beforeMetadata;
        private readonly IDictionary<string, ModMetadata> afterMetadata;
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
            IDictionary<string, ModMetadata> beforeMetadata,
            IDictionary<string, ModMetadata> afterMetadata,
            Action cancelChanges,
            Action<bool> applyChanges)
            : this(beforePackageIds, afterPackageIds, getDisplayName,
                beforeMetadata, afterMetadata, cancelChanges, applyChanges, null)
        {
        }

        internal ManagerApplyChangesDialog(
            IList<string> beforePackageIds,
            IList<string> afterPackageIds,
            Func<string, string> getDisplayName,
            IDictionary<string, ModMetadata> beforeMetadata,
            IDictionary<string, ModMetadata> afterMetadata,
            Action cancelChanges,
            Action loadSelection)
            : this(beforePackageIds, afterPackageIds, getDisplayName,
                beforeMetadata, afterMetadata, cancelChanges, null, loadSelection)
        {
        }

        private ManagerApplyChangesDialog(
            IList<string> beforePackageIds,
            IList<string> afterPackageIds,
            Func<string, string> getDisplayName,
            IDictionary<string, ModMetadata> beforeMetadata,
            IDictionary<string, ModMetadata> afterMetadata,
            Action cancelChanges,
            Action<bool> applyChanges,
            Action loadSelection)
        {
            beforeIds = CopyIds(beforePackageIds);
            afterIds = CopyIds(afterPackageIds);
            this.getDisplayName = getDisplayName;
            this.beforeMetadata = beforeMetadata;
            this.afterMetadata = afterMetadata;
            this.cancelChanges = cancelChanges;
            this.applyChanges = applyChanges;
            this.loadSelection = loadSelection;

            // 추가/제거된 모드는 뒤쪽 항목의 절대 순번을 밀어내므로,
            // 양쪽에 공통으로 존재하는 모드만 추려 상대 순서를 비교한다.
            HashSet<string> beforeIdSet =
                new HashSet<string>(beforeIds, StringComparer.OrdinalIgnoreCase);
            HashSet<string> afterIdSet =
                new HashSet<string>(afterIds, StringComparer.OrdinalIgnoreCase);
            beforePositions = BuildPositions(
                beforeIds.Where(afterIdSet.Contains).ToList());
            afterPositions = BuildPositions(
                afterIds.Where(beforeIdSet.Contains).ToList());
            movedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, int> pair in beforePositions)
            {
                int newIndex;
                if (afterPositions.TryGetValue(pair.Key, out newIndex) && pair.Value != newIndex)
                    movedIds.Add(pair.Key);
            }

            // 추가/제거 개수는 공통 항목이 아닌 원본 전체 목록을 기준으로 계산한다.
            addedCount = afterIds.Count(id => !beforeIdSet.Contains(id));
            removedCount = beforeIds.Count(id => !afterIdSet.Contains(id));
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
                DrawColumn(new Rect(0f, top, width, height), "LMM_0214",
                    beforeIds, afterPositions, beforeMetadata, 0);
                DrawColumn(new Rect(width + gap, top, width, height), "LMM_0215",
                    afterIds, beforePositions, afterMetadata, 1);

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
            IDictionary<string, ModMetadata> metadataByPackageId,
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
                    Widgets.Label(new Rect(row.x + 6f, row.y + 2f, 30f, RowHeight - 4f),
                        (i + 1).ToString());

                    ModMetadata metadata = null;
                    if (metadataByPackageId != null)
                        metadataByPackageId.TryGetValue(id, out metadata);

                    float categoryWidth = metadata != null &&
                        !string.IsNullOrEmpty(metadata.CategoryLabel)
                            ? Mathf.Clamp(view.width * 0.19f, 76f, 104f)
                            : 0f;
                    float tagsWidth = metadata != null && metadata.Tags.Count > 0
                        ? Mathf.Clamp(view.width * 0.29f, 90f, 148f)
                        : 0f;

                    float categoryX = row.x + 40f;
                    if (categoryWidth > 0f)
                        DrawCategoryChip(
                            new Rect(categoryX, row.y + 3f, categoryWidth, 21f), metadata);

                    float nameX = categoryX + categoryWidth +
                        (categoryWidth > 0f ? 6f : 0f);
                    float tagsX = row.xMax - tagsWidth - 6f;
                    if (tagsWidth > 0f)
                        DrawTagChips(
                            new Rect(tagsX, row.y + 3f, tagsWidth, 21f), metadata.Tags);

                    Text.Font = GameFont.Small;
                    string name = getDisplayName == null ? id : getDisplayName(id);
                    float nameRight = tagsWidth > 0f ? tagsX - 5f : row.xMax - 6f;
                    Widgets.Label(new Rect(nameX, row.y + 2f,
                        Mathf.Max(30f, nameRight - nameX), RowHeight - 4f),
                        string.IsNullOrEmpty(name) ? id : name);
                }
            }

            Widgets.EndScrollView();
        }

        private static void DrawCategoryChip(Rect rect, ModMetadata metadata)
        {
            if (metadata == null || string.IsNullOrEmpty(metadata.CategoryLabel))
                return;

            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Widgets.DrawBoxSolid(rect, ManagerTagColors.Background(metadata.CategoryColorHex));
            GUI.color = ManagerTagColors.Foreground(metadata.CategoryColorHex);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect.ContractedBy(2f), metadata.CategoryLabel);
            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            TooltipHandler.TipRegion(rect, metadata.CategoryLabel);
        }

        private static void DrawTagChips(Rect rect, IList<TagMetadata> tags)
        {
            if (tags == null || tags.Count == 0 || rect.width <= 0f)
                return;

            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            float x = rect.x;
            for (int i = 0; i < tags.Count; i++)
            {
                TagMetadata tag = tags[i];
                if (tag == null || string.IsNullOrEmpty(tag.Name))
                    continue;

                float chipWidth = Mathf.Clamp(Text.CalcSize(tag.Name).x + 10f, 28f, 74f);
                if (x + chipWidth > rect.xMax)
                {
                    int hiddenCount = tags.Count - i;
                    float overflowWidth = Mathf.Min(34f, rect.xMax - x);
                    if (overflowWidth >= 24f)
                    {
                        Rect moreRect = new Rect(x, rect.y, overflowWidth, rect.height);
                        Widgets.DrawBoxSolid(moreRect, new Color(0.22f, 0.22f, 0.22f, 1f));
                        GUI.color = Color.white;
                        Widgets.Label(moreRect, "+" + hiddenCount);
                        GUI.color = oldColor;
                    }
                    break;
                }

                Rect chipRect = new Rect(x, rect.y, chipWidth, rect.height);
                Widgets.DrawBoxSolid(chipRect, ManagerTagColors.Background(tag.ColorHex));
                GUI.color = ManagerTagColors.Foreground(tag.ColorHex);
                Widgets.Label(chipRect.ContractedBy(2f), tag.Name);
                GUI.color = oldColor;
                x += chipWidth + 3f;
            }

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            TooltipHandler.TipRegion(rect,
                string.Join(", ", tags.Where(tag => tag != null)
                    .Select(tag => tag.Name).ToArray()));
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
