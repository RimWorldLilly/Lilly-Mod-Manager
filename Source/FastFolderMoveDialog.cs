using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class FastFolderMoveDialog : Window
    {
        public sealed class FolderMoveTarget
        {
            public readonly string Title;
            public readonly string SourceName;
            public readonly ManagerCategoryData Category;
            public readonly ManagerTagData Tag;
            public readonly ManagerFolderData Folder;
            public readonly string Key;
            public readonly bool IsCurrent;

            public FolderMoveTarget(
                string title,
                string sourceName,
                ManagerCategoryData category,
                ManagerTagData tag,
                ManagerFolderData folder,
                string key,
                bool isCurrent)
            {
                Title = title;
                SourceName = sourceName;
                Category = category;
                Tag = tag;
                Folder = folder;
                Key = key ?? string.Empty;
                IsCurrent = isCurrent;
            }
        }

        private readonly List<List<FolderMoveTarget>> columns;
        private readonly Action<FolderMoveTarget> onTargetSelected;
        private readonly Vector2[] columnScroll = new Vector2[3];
        private string searchText = string.Empty;
        private int firstColumn;

        public override Vector2 InitialSize
        {
            get { return new Vector2(Mathf.Min(UI.screenWidth - 80f, 1120f),
                Mathf.Min(UI.screenHeight - 80f, 650f)); }
        }

        public FastFolderMoveDialog(
            List<List<FolderMoveTarget>> columns,
            Action<FolderMoveTarget> onTargetSelected)
        {
            this.columns = columns ?? new List<List<FolderMoveTarget>>();
            this.onTargetSelected = onTargetSelected;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            closeOnAccept = false;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 28f), ManagerTranslation.Get("LMM_0094"));
            Text.Font = GameFont.Small;

            Rect searchRect = new Rect(10f, 34f, Mathf.Min(280f, rect.width * 0.35f), 28f);
            Widgets.Label(new Rect(searchRect.x, searchRect.y, 48f, searchRect.height), ManagerTranslation.Get("LMM_0021"));
            searchText = Widgets.TextField(
                new Rect(searchRect.x + 48f, searchRect.y, searchRect.width - 48f, searchRect.height),
                searchText);

            int lastFirstColumn = Mathf.Max(0, columns.Count - 3);
            if (columns.Count > 3)
            {
                if (Widgets.ButtonText(new Rect(rect.width - 116f, 34f, 30f, 28f), "‹"))
                    firstColumn = Mathf.Max(0, firstColumn - 1);
                Widgets.Label(new Rect(rect.width - 82f, 34f, 40f, 28f),
                    (firstColumn + 1) + "–" + Mathf.Min(firstColumn + 3, columns.Count));
                if (Widgets.ButtonText(new Rect(rect.width - 40f, 34f, 30f, 28f), "›"))
                    firstColumn = Mathf.Min(lastFirstColumn, firstColumn + 1);
            }
            firstColumn = Mathf.Clamp(firstColumn, 0, lastFirstColumn);

            float columnWidth = (rect.width - 30f) / 3f;
            float y = 70f;
            float listHeight = rect.height - 80f;

            for (int visibleColumn = 0; visibleColumn < 3; visibleColumn++)
            {
                int columnIndex = firstColumn + visibleColumn;
                if (columnIndex >= columns.Count)
                    break;
                List<FolderMoveTarget> targets = columns[columnIndex];
                float x = 10f + visibleColumn * columnWidth;
                Rect columnRect = new Rect(x, y, columnWidth - 10f, listHeight);
                Widgets.DrawBoxSolid(columnRect, new Color(0.08f, 0.08f, 0.08f, 1f));

                if (targets != null && targets.Count > 0)
                {
                    Widgets.Label(new Rect(columnRect.x + 8f, columnRect.y + 6f,
                        columnRect.width - 16f, 26f), targets[0].SourceName);
                    Rect listRect = new Rect(columnRect.x + 6f, columnRect.y + 34f,
                        columnRect.width - 12f, columnRect.height - 40f);
                    IntegratedModManagerSettings settings = IntegratedModManagerMod.Instance.Settings;
                    List<FolderMoveTarget> visibleTargets = targets.FindAll(target =>
                        searchText.NullOrEmpty() || target.Folder.Name.IndexOf(
                            searchText, StringComparison.OrdinalIgnoreCase) >= 0);
                    visibleTargets = visibleTargets
                        .OrderByDescending(target => settings.FavoriteFolderKeys.Contains(target.Key))
                        .ThenBy(target =>
                        {
                            int recentIndex = settings.RecentFolderKeys.IndexOf(target.Key);
                            return recentIndex < 0 ? int.MaxValue : recentIndex;
                        })
                        .ToList();
                    float viewHeight = Mathf.Max(listRect.height, visibleTargets.Count * 32f);
                    float scrollbarWidth = viewHeight > listRect.height
                        ? ManagerUiMetrics.VerticalScrollbarWidth
                        : 0f;
                    Widgets.BeginScrollView(listRect, ref columnScroll[visibleColumn],
                        new Rect(0f, 0f, listRect.width - scrollbarWidth, viewHeight));
                    for (int i = 0; i < visibleTargets.Count; i++)
                    {
                        FolderMoveTarget target = visibleTargets[i];
                        Rect buttonRect = new Rect(0f, i * 32f,
                            listRect.width - scrollbarWidth - 48f, 28f);
                        if (target.IsCurrent)
                            Widgets.DrawHighlight(buttonRect);
                        string favoriteLabel = settings.FavoriteFolderKeys.Contains(target.Key) ? "★" : "☆";
                        if (Widgets.ButtonText(new Rect(listRect.width - scrollbarWidth - 44f,
                            i * 32f, 30f, 28f),
                            favoriteLabel))
                        {
                            if (settings.FavoriteFolderKeys.Contains(target.Key))
                                settings.FavoriteFolderKeys.Remove(target.Key);
                            else
                                settings.FavoriteFolderKeys.Add(target.Key);
                            IntegratedModManagerMod.Instance.WriteSettings();
                        }
                        if (Widgets.ButtonText(buttonRect, target.Title))
                        {
                            settings.RecentFolderKeys.Remove(target.Key);
                            settings.RecentFolderKeys.Insert(0, target.Key);
                            if (settings.RecentFolderKeys.Count > 10)
                                settings.RecentFolderKeys.RemoveRange(10,
                                    settings.RecentFolderKeys.Count - 10);
                            IntegratedModManagerMod.Instance.WriteSettings();
                            if (onTargetSelected != null)
                                onTargetSelected(target);
                            Close();
                        }
                    }
                    Widgets.EndScrollView();
                }
                else
                {
                    Widgets.Label(new Rect(columnRect.x + 8f, columnRect.y + 8f,
                        columnRect.width - 16f, 24f), ManagerTranslation.Get("LMM_0097"));
                }
            }
        }
    }
}
