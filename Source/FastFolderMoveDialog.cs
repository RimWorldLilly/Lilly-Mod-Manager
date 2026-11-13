using System;
using System.Collections.Generic;
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

            public FolderMoveTarget(
                string title,
                string sourceName,
                ManagerCategoryData category,
                ManagerTagData tag,
                ManagerFolderData folder)
            {
                Title = title;
                SourceName = sourceName;
                Category = category;
                Tag = tag;
                Folder = folder;
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
            Widgets.Label(new Rect(0f, 0f, rect.width, 28f), "폴더 바로가기");
            Text.Font = GameFont.Small;

            Rect searchRect = new Rect(10f, 34f, Mathf.Min(280f, rect.width * 0.35f), 28f);
            Widgets.Label(new Rect(searchRect.x, searchRect.y, 48f, searchRect.height), "검색");
            searchText = Widgets.TextField(
                new Rect(searchRect.x + 48f, searchRect.y, searchRect.width - 48f, searchRect.height),
                searchText);

            int lastFirstColumn = Mathf.Max(0, columns.Count - 3);
            if (columns.Count > 3)
            {
                if (Widgets.ButtonText(new Rect(rect.width - 100f, 34f, 40f, 28f), "‹"))
                    firstColumn = Mathf.Max(0, firstColumn - 1);
                Widgets.Label(new Rect(rect.width - 58f, 34f, 20f, 28f),
                    (firstColumn + 1) + "–" + Mathf.Min(firstColumn + 3, columns.Count));
                if (Widgets.ButtonText(new Rect(rect.width - 30f, 34f, 40f, 28f), "›"))
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
                    List<FolderMoveTarget> visibleTargets = targets.FindAll(target =>
                        searchText.NullOrEmpty() || target.Folder.Name.IndexOf(
                            searchText, StringComparison.OrdinalIgnoreCase) >= 0);
                    float viewHeight = Mathf.Max(listRect.height, visibleTargets.Count * 32f);
                    Widgets.BeginScrollView(listRect, ref columnScroll[visibleColumn],
                        new Rect(0f, 0f, listRect.width - 16f, viewHeight));
                    for (int i = 0; i < visibleTargets.Count; i++)
                    {
                        FolderMoveTarget target = visibleTargets[i];
                        Rect buttonRect = new Rect(0f, i * 32f, listRect.width - 18f, 28f);
                        if (Widgets.ButtonText(buttonRect, target.Title))
                        {
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
                        columnRect.width - 16f, 24f), "폴더 없음");
                }
            }
        }
    }
}
