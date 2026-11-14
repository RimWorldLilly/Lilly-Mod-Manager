using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Net;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace Lilly.IntegratedModManager
{
    internal sealed class IntegratedModManagerPage : Page
    {
        private enum ModFilter
        {
            All,
            Active,
            Inactive,
            Subscribed,
            Untagged,
            Uncategorized
        }

        private const float RowHeight = 27f;
        private const float BottomHeight = 30f;

        private readonly IntegratedModManagerSettings settings;
        private readonly List<ManagerCategoryData> categories;
        private readonly List<ManagerTagData> tags;
        private readonly List<ManagerModData> modStates;
        private readonly List<ManagerPresetData> presets;
        private readonly HashSet<string> selectedPackageIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ModMetaData> modsByPackageId =
            new Dictionary<string, ModMetaData>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ModMetaData> visibleMods = new List<ModMetaData>();

        private string currentCategoryId;
        private string currentTagId;
        private string primarySelectedPackageId;
        private int lastSelectedIndex = -1;
        private string currentFolderId;
        private string searchText = "";
        private ModFilter currentModFilter = ModFilter.All;
        private bool tagMode;
        private bool displayFulfilledRequirements;
        private bool modListHasKeyboardFocus;
        private Vector2 categoryTabScroll;
        private Vector2 tagTabScroll;
        private Vector2 modListScroll;
        private Vector2 descriptionScroll;
        private string dragPackageId;
        private bool isDraggingMod;
        private readonly List<string> dragSelection = new List<string>();
        private string dragCategoryId;
        private string dragTagId;
        private string dragFolderId;
        private bool dragFolderInTagMode;

        public override Vector2 InitialSize
        {
            get { return new Vector2(UI.screenWidth, UI.screenHeight); }
        }

        protected override float Margin
        {
            get { return 0f; }
        }

        public IntegratedModManagerPage()
        {
            doWindowBackground = false;
            doCloseX = false;
            absorbInputAroundWindow = true;

            if (IntegratedModManagerMod.Instance == null)
                throw new InvalidOperationException("Integrated Mod Manager did not initialize.");

            settings = IntegratedModManagerMod.Instance.Settings;
            bool persistGeneratedTagColors = settings.Tags == null || settings.Tags.Count == 0 ||
                settings.Tags.Any(tag => tag != null && !ManagerTagColors.IsValidHex(tag.ColorHex));
            settings.EnsureInitialized();
            if (persistGeneratedTagColors)
                IntegratedModManagerMod.Instance.WriteSettings();
            categories = settings.Categories.Select(category => category.Copy()).ToList();
            tags = settings.Tags.Select(tag => tag.Copy()).ToList();
            modStates = settings.Mods.Select(mod => mod.Copy()).ToList();
            presets = settings.Presets.Select(preset => preset.Copy()).ToList();

            foreach (ModMetaData mod in ModLister.AllInstalledMods)
            {
                if (mod != null && !mod.PackageId.NullOrEmpty())
                    modsByPackageId[mod.PackageId] = mod;
            }

            currentCategoryId = categories.FirstOrDefault()?.Id;
            currentTagId = tags.FirstOrDefault()?.Id;
            EnsureCurrentSelectionValid();
            RefreshVisibleMods();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, Color.black);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(20f, 10f, inRect.width - 470f, 36f),
                "Lilly - Integrated Mod Manager");
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(inRect.width - 150f, 10f, 130f, 30f), ManagerTranslation.Get("LMM_0067")))
                SortAllCategories();
            float tabScrollbarReserve = GetTabScrollbarReserve(inRect.width - 40f);
            float tabContentHeight = 60f;
            DrawCategoryTabs(new Rect(20f, 48f, inRect.width - 40f,
                tabContentHeight + tabScrollbarReserve));

            float searchTop = 48f + tabContentHeight + tabScrollbarReserve + 4f;
            Rect searchRect = new Rect(20f, searchTop, inRect.width * 0.66f - 30f, 30f);
            float contentTop = searchTop + 38f;
            float contentBottom = inRect.height - BottomHeight - 20f;
            float contentHeight = Mathf.Max(100f, contentBottom - contentTop);
            float leftWidth = inRect.width * 0.66f;
            Rect listRect = new Rect(20f, contentTop, leftWidth - 30f, contentHeight);
            Rect detailsRect = new Rect(leftWidth, contentTop, inRect.width - leftWidth - 20f,
                contentHeight);

            if (Event.current.type == EventType.MouseDown)
                modListHasKeyboardFocus = listRect.Contains(Event.current.mousePosition);
            DrawSearchField(searchRect);

            DrawModList(listRect);
            DrawDetails(detailsRect);
            HandleModListNavigation(listRect);
            DrawBottomButtons(new Rect(20f, inRect.height - BottomHeight - 8f,
                inRect.width - 40f, BottomHeight));
            HandleSelectAll();
            if (Event.current.type == EventType.MouseUp)
            {
                dragCategoryId = null;
                dragTagId = null;
                dragFolderId = null;
                if (isDraggingMod)
                    EndModDrag();
            }
        }

        private void DrawSearchField(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, 52f, rect.height), ManagerTranslation.Get("LMM_0021"));
            string newSearchText = Widgets.TextField(
                new Rect(rect.x + 54f, rect.y, rect.width - 196f, rect.height),
                searchText);
            if (!string.Equals(newSearchText, searchText, StringComparison.Ordinal))
            {
                searchText = newSearchText;
                selectedPackageIds.Clear();
                primarySelectedPackageId = null;
                lastSelectedIndex = -1;
                RefreshVisibleMods();
            }

            if (!searchText.NullOrEmpty() &&
                Widgets.ButtonText(new Rect(rect.xMax - 150f, rect.y, 34f, rect.height), "×"))
            {
                searchText = "";
                selectedPackageIds.Clear();
                primarySelectedPackageId = null;
                lastSelectedIndex = -1;
                RefreshVisibleMods();
            }

            Rect filterRect = new Rect(rect.xMax - 112f, rect.y, 108f, rect.height);
            if (Widgets.ButtonText(filterRect, GetFilterLabel()))
            {
                currentModFilter = NextFilter(currentModFilter);
                RefreshVisibleMods();
            }
        }

        private static ModFilter NextFilter(ModFilter filter)
        {
            switch (filter)
            {
                case ModFilter.All:
                    return ModFilter.Active;
                case ModFilter.Active:
                    return ModFilter.Inactive;
                case ModFilter.Inactive:
                    return ModFilter.Subscribed;
                case ModFilter.Subscribed:
                    return ModFilter.Untagged;
                case ModFilter.Untagged:
                    return ModFilter.Uncategorized;
                default:
                    return ModFilter.All;
            }
        }

        private string GetFilterLabel()
        {
            switch (currentModFilter)
            {
                case ModFilter.Active:
                    return ManagerTranslation.Get("LMM_0115");
                case ModFilter.Inactive:
                    return ManagerTranslation.Get("LMM_0112");
                case ModFilter.Subscribed:
                    return ManagerTranslation.Get("LMM_0110");
                case ModFilter.Untagged:
                    return ManagerTranslation.Get("LMM_0114");
                case ModFilter.Uncategorized:
                    return ManagerTranslation.Get("LMM_0111");
                default:
                    return ManagerTranslation.Get("LMM_0113");
            }
        }

        private void DrawCategoryTabs(Rect rect)
        {
            if (tagMode)
            {
                DrawTagTabs(rect);
                return;
            }

            List<ManagerCategoryData> visibleCategories = categories
                .Where(MatchesCategorySearch)
                .ToList();
            float tabContentHeight = rect.height - GetTabScrollbarReserve(rect.width);
            float contentWidth = visibleCategories.Sum(GetCategoryTabSlotWidth);
            if (visibleCategories.Count > 0)
                contentWidth -= 6f;
            float viewWidth = Mathf.Max(rect.width, contentWidth);
            Rect viewRect = new Rect(0f, 0f, viewWidth, rect.height);
            Widgets.BeginScrollView(rect, ref categoryTabScroll, viewRect);
            float tabX = 0f;
            for (int i = 0; i < visibleCategories.Count; i++)
            {
                ManagerCategoryData category = visibleCategories[i];
                int categoryIndex = categories.IndexOf(category);
                float tabSlotWidth = GetCategoryTabSlotWidth(category);
                Rect tabRect = new Rect(tabX, 0f, tabSlotWidth - 6f, tabContentHeight);
                if (isDraggingMod && Event.current.type == EventType.MouseUp &&
                    tabRect.Contains(Event.current.mousePosition))
                {
                    MoveSelectionTo(category);
                    EndModDrag();
                    Event.current.Use();
                }
                if (category.Id == currentCategoryId)
                    Widgets.DrawHighlight(tabRect);

                Rect enabledRect = new Rect(tabRect.x + 4f, tabRect.y + 8f, 24f, 24f);
                bool enabled = category.Enabled;
                Widgets.Checkbox(enabledRect.position, ref enabled);
                if (enabled != category.Enabled)
                    category.Enabled = enabled;

                Rect labelRect = new Rect(enabledRect.xMax + 4f, tabRect.y,
                    Mathf.Max(30f, tabRect.width - 63f), 30f);
                HandleCategoryTabReorder(category, labelRect);
                string label = category.Name + " (" + GetInstalledCount(category) + ")";
                if (Widgets.ButtonText(labelRect, label))
                {
                    currentCategoryId = category.Id;
                    currentFolderId = category.Folders.Count > 0
                        ? category.Folders[0].Id
                        : null;
                    selectedPackageIds.Clear();
                    primarySelectedPackageId = null;
                    lastSelectedIndex = -1;
                    RefreshVisibleMods();
                }
                TooltipHandler.TipRegion(labelRect, label);
                Rect editRect = new Rect(labelRect.xMax + 3f, tabRect.y + 3f, 24f, 24f);
                if (Widgets.ButtonText(editRect, "E"))
                    OpenRenameCategoryDialog(category);
                TooltipHandler.TipRegion(editRect, ManagerTranslation.Get("LMM_0125"));

                float actionX = tabRect.x + 4f;
                float actionY = tabRect.y + 32f;
                float[] actionWidths = { 22f, 22f, 24f, 22f, 22f, 22f };
                float actionGap = Mathf.Max(3f,
                    (tabRect.width - 8f - actionWidths.Sum()) / (actionWidths.Length - 1));
                if (Widgets.ButtonText(new Rect(actionX, actionY, actionWidths[0], 24f), "+"))
                    OpenCreateCategoryDialog(categoryIndex);
                float buttonX = actionX + actionWidths[0] + actionGap;
                GUI.enabled = categoryIndex > 0;
                if (Widgets.ButtonText(new Rect(buttonX, actionY, actionWidths[1], 24f), "‹"))
                    MoveCategory(categoryIndex, -1);
                buttonX += actionWidths[1] + actionGap;
                GUI.enabled = true;
                Rect movePositionRect = new Rect(buttonX, actionY, actionWidths[2], 24f);
                if (Widgets.ButtonText(movePositionRect, "M"))
                    OpenMoveCategoryToPositionMenu(category);
                TooltipHandler.TipRegion(movePositionRect, ManagerTranslation.Get("LMM_0127"));
                buttonX += actionWidths[2] + actionGap;
                GUI.enabled = categoryIndex < categories.Count - 1;
                if (Widgets.ButtonText(new Rect(buttonX, actionY, actionWidths[3], 24f), "›"))
                    MoveCategory(categoryIndex, 1);
                GUI.enabled = true;
                buttonX += actionWidths[3] + actionGap;
                if (Widgets.ButtonText(new Rect(buttonX, actionY, actionWidths[4], 24f), "+"))
                    OpenCreateCategoryDialog(categoryIndex + 1);
                buttonX += actionWidths[4] + actionGap;
                if (Widgets.ButtonText(new Rect(buttonX, actionY, actionWidths[5], 24f), "×"))
                    ConfirmDeleteCategory(category);
                tabX += tabSlotWidth;
            }
            Widgets.EndScrollView();
        }

        private float GetCategoryTabSlotWidth(ManagerCategoryData category)
        {
            if (category == null)
                return 163f;

            string label = category.Name + " (" + GetInstalledCount(category) + ")";
            float titleWidth = Text.CalcSize(label).x;
            float requiredCardWidth = Mathf.Max(157f, titleWidth + 63f);
            return requiredCardWidth + 6f;
        }

        private float GetTabScrollbarReserve(float availableWidth)
        {
            if (tagMode)
            {
                ManagerTagData untagged = FindTag("untagged");
                float systemWidth = GetSystemTagTabSlotWidth(untagged);
                List<ManagerTagData> userTags = tags
                    .Where(tag => tag != null && !tag.IsSystem)
                    .ToList();
                float scrollWidth = Mathf.Max(0f, availableWidth - systemWidth);
                float tagContentWidth = userTags.Sum(GetTagTabSlotWidth);
                if (userTags.Count > 0)
                    tagContentWidth -= 6f;
                return tagContentWidth > scrollWidth
                    ? ManagerUiMetrics.HorizontalScrollbarHeight
                    : 0f;
            }

            List<ManagerCategoryData> visibleCategories = categories
                .Where(MatchesCategorySearch)
                .ToList();
            float contentWidth = visibleCategories.Sum(GetCategoryTabSlotWidth);
            if (visibleCategories.Count > 0)
                contentWidth -= 6f;
            return contentWidth > availableWidth
                ? ManagerUiMetrics.HorizontalScrollbarHeight
                : 0f;
        }

        private bool MatchesCategorySearch(ManagerCategoryData category)
        {
            if (category == null)
                return false;
            if (searchText.NullOrEmpty())
                return true;

            return category.ModIds.Any(packageId =>
            {
                ModMetaData mod = FindMod(packageId);
                return mod != null && MatchesSearch(mod);
            });
        }

        private void MoveCategory(int index, int offset)
        {
            int targetIndex = index + offset;
            if (index < 0 || targetIndex < 0 || targetIndex >= categories.Count)
                return;

            ManagerCategoryData category = categories[index];
            categories[index] = categories[targetIndex];
            categories[targetIndex] = category;
        }

        private void OpenMoveCategoryToPositionMenu(ManagerCategoryData category)
        {
            int currentIndex = categories.IndexOf(category);
            if (currentIndex < 0)
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < categories.Count; i++)
            {
                int targetIndex = i;
                if (targetIndex == currentIndex)
                    continue;
                options.Add(new FloatMenuOption(string.Format(
                    ManagerTranslation.Get("LMM_0128"), targetIndex + 1),
                    () => MoveCategoryToIndex(category, targetIndex)));
            }
            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        private void MoveCategoryToIndex(ManagerCategoryData category, int targetIndex)
        {
            int currentIndex = categories.IndexOf(category);
            if (currentIndex < 0 || targetIndex < 0 || targetIndex >= categories.Count ||
                currentIndex == targetIndex)
                return;

            categories.RemoveAt(currentIndex);
            categories.Insert(targetIndex, category);
        }

        private void OpenCreateCategoryDialog(int insertIndex)
        {
            Find.WindowStack.Add(new ManagerFolderNameDialog(
                ManagerTranslation.Get("LMM_0046"),
                "",
                name =>
                {
                    string trimmed = name == null ? string.Empty : name.Trim();
                    if (trimmed.NullOrEmpty())
                        return false;

                    if (categories.Any(category =>
                        string.Equals(category.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                    {
                        Messages.Message(ManagerTranslation.Get("LMM_0015"),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    ManagerCategoryData category = new ManagerCategoryData(
                        Guid.NewGuid().ToString("N"), trimmed);
                    category.Folders.Add(new ManagerFolderData("uncategorized", ManagerTranslation.Get("LMM_0040")));
                    if (insertIndex >= 0 && insertIndex <= categories.Count)
                        categories.Insert(insertIndex, category);
                    else
                        categories.Add(category);
                    currentCategoryId = category.Id;
                    currentFolderId = category.Folders.FirstOrDefault()?.Id;
                    RefreshVisibleMods();
                    return true;
                }));
        }

        private void OpenRenameCategoryDialog(ManagerCategoryData category)
        {
            if (category == null)
                return;

            Find.WindowStack.Add(new ManagerFolderNameDialog(
                ManagerTranslation.Get("LMM_0078"),
                category.Name,
                name =>
                {
                    string trimmed = name == null ? string.Empty : name.Trim();
                    if (trimmed.NullOrEmpty())
                        return false;

                    if (categories.Any(existing => existing != category &&
                        string.Equals(existing.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                    {
                        Messages.Message(ManagerTranslation.Get("LMM_0015"),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    category.Name = trimmed;
                    return true;
                }));
        }

        private void ConfirmDeleteCategory(ManagerCategoryData category)
        {
            if (category == null)
                return;

            if (category.ModIds != null && category.ModIds.Count > 0)
            {
                Messages.Message(ManagerTranslation.Get("LMM_0036"),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                ManagerTranslation.Get("LMM_0076") + category.Name + ManagerTranslation.Get("LMM_0006"),
                () =>
                {
                    categories.Remove(category);
                    if (string.Equals(currentCategoryId, category.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        currentCategoryId = categories.FirstOrDefault()?.Id;
                        currentFolderId = FindCurrentCategory()?.Folders.FirstOrDefault()?.Id;
                    }
                    RefreshVisibleMods();
                },
                destructive: true));
        }

        private void DrawTagTabs(Rect rect)
        {
            float tabContentHeight = rect.height - GetTabScrollbarReserve(rect.width);
            ManagerTagData untagged = FindTag("untagged");
            float systemWidth = GetSystemTagTabSlotWidth(untagged);
            if (untagged != null)
                DrawTagTab(untagged,
                    new Rect(rect.x, rect.y, systemWidth - 6f, tabContentHeight));

            List<ManagerTagData> userTags = new List<ManagerTagData>();
            for (int i = 0; i < tags.Count; i++)
            {
                ManagerTagData tag = tags[i];
                if (tag == null || string.Equals(tag.Id, "untagged", StringComparison.OrdinalIgnoreCase))
                    continue;
                userTags.Add(tag);
            }

            float scrollWidth = Mathf.Max(0f, rect.width - systemWidth);
            Rect scrollRect = new Rect(rect.x + systemWidth, rect.y, scrollWidth, rect.height);
            float contentWidth = userTags.Sum(GetTagTabSlotWidth);
            if (userTags.Count > 0)
                contentWidth -= 6f;
            float viewWidth = Mathf.Max(scrollWidth, contentWidth);
            Rect viewRect = new Rect(0f, 0f, viewWidth, rect.height);
            Widgets.BeginScrollView(scrollRect, ref tagTabScroll, viewRect);
            float tabX = 0f;
            for (int i = 0; i < userTags.Count; i++)
            {
                float tabSlotWidth = GetTagTabSlotWidth(userTags[i]);
                DrawTagTab(userTags[i],
                    new Rect(tabX, 0f, tabSlotWidth - 6f, tabContentHeight));
                tabX += tabSlotWidth;
            }
            Widgets.EndScrollView();
        }

        private float GetSystemTagTabSlotWidth(ManagerTagData tag)
        {
            if (tag == null)
                return 6f;
            string label = tag.Name + " (" + GetInstalledCount(tag) + ")";
            return Mathf.Max(130f, Text.CalcSize(label).x + 43f) + 6f;
        }

        private float GetTagTabSlotWidth(ManagerTagData tag)
        {
            if (tag == null)
                return 163f;
            string label = tag.Name + " (" + GetInstalledCount(tag) + ")";
            return Mathf.Max(157f, Text.CalcSize(label).x + 63f) + 6f;
        }

        private void DrawTagTab(ManagerTagData tag, Rect tabRect)
        {
            if (tag == null)
                return;
            if (isDraggingMod && Event.current.type == EventType.MouseUp &&
                tabRect.Contains(Event.current.mousePosition))
            {
                if (tag.IsSystem)
                    ClearTagsForSelection(dragSelection);
                else
                    SetTagForSelection(dragSelection, tag, true);
                EndModDrag();
                Event.current.Use();
            }
            if (tag.Id == currentTagId)
                Widgets.DrawHighlight(tabRect);

            Rect stateRect = new Rect(tabRect.x + 2f, tabRect.y + 3f, 30f, 24f);
            if (Widgets.ButtonText(stateRect, GetTagStateLabel(tag)))
                ToggleTagEnabled(tag);

            if (tag.IsSystem)
            {
                Rect labelRect = new Rect(stateRect.xMax + 3f, tabRect.y,
                    Mathf.Max(20f, tabRect.width - 43f), 30f);
                if (Widgets.ButtonText(labelRect,
                    tag.Name + " (" + GetInstalledCount(tag) + ")"))
                    SelectTag(tag);
                TooltipHandler.TipRegion(labelRect, tag.Name + " (" + GetInstalledCount(tag) + ")");
                return;
            }

            Rect labelRectUser = new Rect(stateRect.xMax + 3f, tabRect.y,
                Mathf.Max(20f, tabRect.width - 63f), 30f);
            HandleTagTabReorder(tag, labelRectUser);
            if (Widgets.ButtonText(labelRectUser,
                tag.Name + " (" + GetInstalledCount(tag) + ")"))
                SelectTag(tag);
            TooltipHandler.TipRegion(labelRectUser,
                tag.Name + " (" + GetInstalledCount(tag) + ")");
            Rect editRect = new Rect(labelRectUser.xMax + 3f, tabRect.y + 3f, 24f, 24f);
            if (Widgets.ButtonText(editRect, "E"))
                OpenRenameTagDialog(tag);
            TooltipHandler.TipRegion(editRect, ManagerTranslation.Get("LMM_0126"));

            float actionY = tabRect.y + 32f;
            float actionX = tabRect.x + 4f;
            float[] actionWidths = { 22f, 24f, 22f, 22f, 22f, 22f };
            float actionGap = Mathf.Max(3f,
                (tabRect.width - 8f - actionWidths.Sum()) / (actionWidths.Length - 1));
            GUI.enabled = tags.IndexOf(tag) > 1;
            if (Widgets.ButtonText(new Rect(actionX, actionY, actionWidths[0], 24f), "‹"))
                MoveTag(tag, -1);
            actionX += actionWidths[0] + actionGap;
            GUI.enabled = true;
            Rect movePositionRect = new Rect(actionX, actionY, actionWidths[1], 24f);
            if (Widgets.ButtonText(movePositionRect, "M"))
                OpenMoveTagToPositionMenu(tag);
            TooltipHandler.TipRegion(movePositionRect, ManagerTranslation.Get("LMM_0127"));
            actionX += actionWidths[1] + actionGap;
            GUI.enabled = tags.IndexOf(tag) >= 1 && tags.IndexOf(tag) < tags.Count - 1;
            if (Widgets.ButtonText(new Rect(actionX, actionY, actionWidths[2], 24f), "›"))
                MoveTag(tag, 1);
            GUI.enabled = true;
            actionX += actionWidths[2] + actionGap;

            Rect colorRect = new Rect(actionX, actionY, actionWidths[3], 24f);
            if (Widgets.ButtonText(colorRect, "C"))
                OpenTagColorDialog(tag);
            TooltipHandler.TipRegion(colorRect, ManagerTranslation.Get("LMM_0051"));
            actionX += actionWidths[3] + actionGap;
            if (Widgets.ButtonText(new Rect(actionX, actionY, actionWidths[4], 24f), "+"))
                OpenCreateTagDialog();
            actionX += actionWidths[4] + actionGap;
            if (Widgets.ButtonText(new Rect(actionX, actionY, actionWidths[5], 24f), "×"))
                ConfirmDeleteTag(tag);
        }

        private void OpenMoveTagToPositionMenu(ManagerTagData tag)
        {
            int currentIndex = tags.IndexOf(tag);
            if (currentIndex <= 0)
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 1; i < tags.Count; i++)
            {
                int targetIndex = i;
                if (targetIndex == currentIndex)
                    continue;
                options.Add(new FloatMenuOption(string.Format(
                    ManagerTranslation.Get("LMM_0128"), targetIndex + 1),
                    () => MoveTagToIndex(tag, targetIndex)));
            }
            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        private void MoveTagToIndex(ManagerTagData tag, int targetIndex)
        {
            int currentIndex = tags.IndexOf(tag);
            if (currentIndex <= 0 || targetIndex <= 0 || targetIndex >= tags.Count ||
                currentIndex == targetIndex)
                return;

            tags.RemoveAt(currentIndex);
            tags.Insert(targetIndex, tag);
            PersistTagData();
        }

        private void HandleCategoryTabReorder(ManagerCategoryData category, Rect dragRect)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 &&
                dragRect.Contains(current.mousePosition))
            {
                dragCategoryId = category.Id;
                current.Use();
            }
            else if (current.type == EventType.MouseUp && !dragCategoryId.NullOrEmpty() &&
                dragRect.Contains(current.mousePosition))
            {
                int from = categories.FindIndex(item => item.Id == dragCategoryId);
                int to = categories.IndexOf(category);
                if (from >= 0 && to >= 0 && from != to)
                {
                    ManagerCategoryData moving = categories[from];
                    categories.RemoveAt(from);
                    categories.Insert(to, moving);
                }
                currentCategoryId = category.Id;
                if (FindFolder(category, currentFolderId) == null)
                    currentFolderId = category.Folders.FirstOrDefault()?.Id;
                RefreshVisibleMods();
                dragCategoryId = null;
                current.Use();
            }
        }

        private void HandleTagTabReorder(ManagerTagData tag, Rect dragRect)
        {
            Event current = Event.current;
            if (tag.IsSystem)
                return;
            if (current.type == EventType.MouseDown && current.button == 0 &&
                dragRect.Contains(current.mousePosition))
            {
                dragTagId = tag.Id;
                current.Use();
            }
            else if (current.type == EventType.MouseUp && !dragTagId.NullOrEmpty() &&
                dragRect.Contains(current.mousePosition))
            {
                int from = tags.FindIndex(item => item.Id == dragTagId);
                int to = tags.IndexOf(tag);
                if (from >= 0 && to >= 0 && from != to)
                {
                    ManagerTagData moving = tags[from];
                    tags.RemoveAt(from);
                    tags.Insert(to, moving);
                    PersistTagData();
                }
                SelectTag(tag);
                dragTagId = null;
                current.Use();
            }
        }

        private void HandleFolderReorder(
            ManagerFolderData folder,
            Rect dragRect,
            List<ManagerFolderData> folderList,
            bool inTagMode)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 &&
                dragRect.Contains(current.mousePosition))
            {
                dragFolderId = folder.Id;
                dragFolderInTagMode = inTagMode;
                current.Use();
            }
            else if (current.type == EventType.MouseUp && !dragFolderId.NullOrEmpty() &&
                dragFolderInTagMode == inTagMode && dragRect.Contains(current.mousePosition))
            {
                int from = folderList.FindIndex(item => item.Id == dragFolderId);
                int to = folderList.IndexOf(folder);
                if (from >= 0 && to >= 0 && from != to)
                {
                    ManagerFolderData moving = folderList[from];
                    folderList.RemoveAt(from);
                    folderList.Insert(to, moving);
                    if (inTagMode)
                        PersistTagData();
                }
                currentFolderId = folder.Id;
                dragFolderId = null;
                current.Use();
                RefreshVisibleMods();
            }
        }

        private void MoveTag(ManagerTagData tag, int offset)
        {
            int index = tags.IndexOf(tag);
            int targetIndex = index + offset;
            if (index <= 0 || targetIndex <= 0 || targetIndex >= tags.Count)
                return;

            ManagerTagData target = tags[targetIndex];
            tags[targetIndex] = tag;
            tags[index] = target;
            PersistTagData();
        }

        private void SelectTag(ManagerTagData tag)
        {
                if (tag == null)
                    return;

                currentTagId = tag.Id;
                currentFolderId = tag.Folders.FirstOrDefault()?.Id;
                selectedPackageIds.Clear();
                primarySelectedPackageId = null;
                lastSelectedIndex = -1;
                RefreshVisibleMods();
        }

        private void DrawModList(Rect rect)
        {
            if (tagMode)
            {
                DrawTagModList(rect);
                return;
            }

            ManagerCategoryData category = FindCurrentCategory();
            if (category == null)
                return;

            Widgets.DrawBox(rect);
            Rect innerRect = rect.ContractedBy(8f);
            float viewHeight = Mathf.Max(innerRect.height, GetFolderListHeight(category));
            float scrollbarWidth = viewHeight > innerRect.height
                ? ManagerUiMetrics.VerticalScrollbarWidth
                : 0f;
            Rect viewRect = new Rect(0f, 0f, innerRect.width - scrollbarWidth, viewHeight);
            Widgets.BeginScrollView(innerRect, ref modListScroll, viewRect);

            float y = 0f;
            int visibleIndex = 0;
            for (int folderIndex = 0; folderIndex < category.Folders.Count; folderIndex++)
            {
                ManagerFolderData folder = category.Folders[folderIndex];
                Rect headerRect = new Rect(0f, y, viewRect.width, RowHeight);
                if (isDraggingMod && Event.current.type == EventType.MouseUp &&
                    headerRect.Contains(Event.current.mousePosition))
                {
                    MoveSelectionToFolder(category, folder);
                    EndModDrag();
                    Event.current.Use();
                }
                if (Event.current.type == EventType.MouseDown && Event.current.button == 1 &&
                    headerRect.Contains(Event.current.mousePosition))
                {
                    OpenFolderContextMenu(category, folder);
                    Event.current.Use();
                }
                Widgets.DrawLightHighlight(headerRect);
                string foldLabel = folder.Collapsed ? "▶" : "▼";
                if (Widgets.ButtonText(new Rect(headerRect.x + 2f, y + 2f, 28f, 26f), foldLabel))
                {
                    folder.Collapsed = !folder.Collapsed;
                    RefreshVisibleMods();
                }

                if (Widgets.ButtonText(new Rect(headerRect.x + 32f, y + 2f, 30f, 23f),
                    GetFolderStateLabel(folder.ModIds)))
                    SetFolderEnabled(folder.ModIds, GetFolderStateLabel(folder.ModIds) != "●");
                Rect titleRect = new Rect(headerRect.x + 66f, y + 2f,
                    Mathf.Max(30f, headerRect.width - 259f), 26f);
                HandleFolderReorder(folder, titleRect, category.Folders, false);
                if (folder.Id == currentFolderId)
                    Widgets.DrawHighlight(titleRect);
                if (Widgets.ButtonText(titleRect,
                    folder.Name + " (" + GetInstalledCount(folder) + ")"))
                    currentFolderId = folder.Id;

                float actionX = headerRect.xMax - 186f;
                if (Widgets.ButtonText(new Rect(actionX, y + 2f, 42f, 26f), "+"))
                    OpenCreateFolderDialog(category, folder);
                if (Widgets.ButtonText(new Rect(actionX + 46f, y + 2f, 62f, 26f), ManagerTranslation.Get("LMM_0063")))
                    OpenRenameFolderDialog(category, folder);
                bool isDefaultFolder = folder.Id == "uncategorized";
                GUI.enabled = !isDefaultFolder;
                if (Widgets.ButtonText(new Rect(actionX + 112f, y + 2f, 62f, 26f), ManagerTranslation.Get("LMM_0045")))
                    ConfirmDeleteFolder(category, folder);
                GUI.enabled = true;
                y += RowHeight;

                if (folder.Collapsed)
                    continue;

                for (int folderModIndex = 0; folderModIndex < folder.ModIds.Count; folderModIndex++)
                {
                    ModMetaData mod = FindMod(folder.ModIds[folderModIndex]);
                    if (mod == null)
                        continue;

                    if (!MatchesSearch(mod))
                        continue;

                    DrawModRow(mod, visibleIndex, viewRect.width, y);

                    y += RowHeight;
                    visibleIndex++;
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawTagModList(Rect rect)
        {
            ManagerTagData tag = FindCurrentTag();
            if (tag == null)
            {
                Widgets.DrawBox(rect);
                Widgets.Label(rect.ContractedBy(12f),
                    ManagerTranslation.Get("LMM_0090"));
                return;
            }

            Widgets.DrawBox(rect);
            Rect innerRect = rect.ContractedBy(8f);
            float viewHeight = Mathf.Max(innerRect.height, GetFolderListHeight(tag));
            float scrollbarWidth = viewHeight > innerRect.height
                ? ManagerUiMetrics.VerticalScrollbarWidth
                : 0f;
            Rect viewRect = new Rect(0f, 0f, innerRect.width - scrollbarWidth, viewHeight);
            Widgets.BeginScrollView(innerRect, ref modListScroll, viewRect);

            float y = 0f;
            int visibleIndex = 0;
            for (int folderIndex = 0; folderIndex < tag.Folders.Count; folderIndex++)
            {
                ManagerFolderData folder = tag.Folders[folderIndex];
                Rect headerRect = new Rect(0f, y, viewRect.width, RowHeight);
                if (isDraggingMod && Event.current.type == EventType.MouseUp &&
                    headerRect.Contains(Event.current.mousePosition))
                {
                    MoveSelectionToTagFolder(tag, folder);
                    EndModDrag();
                    Event.current.Use();
                }
                if (Event.current.type == EventType.MouseDown && Event.current.button == 1 &&
                    headerRect.Contains(Event.current.mousePosition))
                {
                    OpenFolderContextMenu(tag, folder);
                    Event.current.Use();
                }
                Widgets.DrawLightHighlight(headerRect);
                if (Widgets.ButtonText(new Rect(headerRect.x + 2f, y + 2f, 28f, 26f),
                    folder.Collapsed ? "▶" : "▼"))
                {
                    folder.Collapsed = !folder.Collapsed;
                    PersistTagData();
                    RefreshVisibleMods();
                }

                List<string> tagFolderIds = GetTagFolderModIds(tag, folder);
                if (Widgets.ButtonText(new Rect(headerRect.x + 32f, y + 2f, 30f, 23f),
                    GetFolderStateLabel(tagFolderIds)))
                    SetFolderEnabled(tagFolderIds, GetFolderStateLabel(tagFolderIds) != "●");
                Rect titleRect = new Rect(headerRect.x + 66f, y + 2f,
                    Mathf.Max(30f, headerRect.width - 259f), 26f);
                HandleFolderReorder(folder, titleRect, tag.Folders, true);
                if (folder.Id == currentFolderId)
                    Widgets.DrawHighlight(titleRect);
                if (Widgets.ButtonText(titleRect,
                    folder.Name + " (" + GetInstalledCount(tag, folder) + ")"))
                    currentFolderId = folder.Id;

                float actionX = headerRect.xMax - 186f;
                if (Widgets.ButtonText(new Rect(actionX, y + 2f, 42f, 26f), "+"))
                    OpenCreateFolderDialog(tag, folder);
                if (Widgets.ButtonText(new Rect(actionX + 46f, y + 2f, 62f, 26f), ManagerTranslation.Get("LMM_0063")))
                    OpenRenameFolderDialog(tag, folder);
                GUI.enabled = folder.Id != "uncategorized";
                if (Widgets.ButtonText(new Rect(actionX + 112f, y + 2f, 62f, 26f), ManagerTranslation.Get("LMM_0045")))
                    ConfirmDeleteFolder(tag, folder);
                GUI.enabled = true;
                y += RowHeight;

                if (folder.Collapsed)
                    continue;

                List<string> packageIds = tagFolderIds;
                for (int modIndex = 0; modIndex < packageIds.Count; modIndex++)
                {
                    ModMetaData mod = FindMod(packageIds[modIndex]);
                    if (mod == null || !MatchesSearch(mod))
                        continue;

                    DrawModRow(mod, visibleIndex, viewRect.width, y);
                    y += RowHeight;
                    visibleIndex++;
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawModRow(ModMetaData mod, int visibleIndex, float width, float y)
        {
            Rect rowRect = new Rect(0f, y, width, RowHeight);
            Rect checkboxHitRect = new Rect(rowRect.x, rowRect.y, 32f, rowRect.height);
            Widgets.DrawBoxSolid(rowRect, visibleIndex % 2 == 0
                ? new Color(0.095f, 0.095f, 0.095f, 1f)
                : new Color(0.055f, 0.055f, 0.055f, 1f));
            if (selectedPackageIds.Contains(mod.PackageId))
                Widgets.DrawHighlight(rowRect);
            else
                Widgets.DrawHighlightIfMouseover(rowRect);

            if (isDraggingMod && !dragPackageId.NullOrEmpty() &&
                !string.Equals(dragPackageId, mod.PackageId, StringComparison.OrdinalIgnoreCase) &&
                rowRect.Contains(Event.current.mousePosition))
            {
                Widgets.DrawLightHighlight(rowRect);
                if (Event.current.type == EventType.MouseUp)
                    HandleDragDropToMod(mod.PackageId);
            }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                rowRect.Contains(Event.current.mousePosition) &&
                !checkboxHitRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.control || Event.current.shift)
                {
                    SelectMod(visibleIndex, Event.current.control, Event.current.shift);
                }
                else
                {
                    if (!selectedPackageIds.Contains(mod.PackageId))
                        SelectMod(visibleIndex, false, false);
                    else
                        primarySelectedPackageId = mod.PackageId;
                    dragPackageId = mod.PackageId;
                    dragSelection.Clear();
                    dragSelection.AddRange(selectedPackageIds);
                    isDraggingMod = true;
                }
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseDown && Event.current.button == 1 &&
                rowRect.Contains(Event.current.mousePosition))
            {
                if (!selectedPackageIds.Contains(mod.PackageId))
                {
                    selectedPackageIds.Clear();
                    selectedPackageIds.Add(mod.PackageId);
                    primarySelectedPackageId = mod.PackageId;
                }
                OpenModContextMenu(mod);
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseUp && isDraggingMod)
            {
                EndModDrag();
                Event.current.Use();
            }

            Rect checkRect = new Rect(rowRect.x + 3f, rowRect.y + 3f, 24f, 24f);
            ManagerModData state = FindModState(mod.PackageId);
            bool checkedValue = state != null && state.Enabled;
            bool protectedMod = IsProtected(mod);
            Widgets.Checkbox(checkRect.position, ref checkedValue, checkRect.width, protectedMod);
            if (!protectedMod && state != null && checkedValue != state.Enabled)
                state.Enabled = checkedValue;

            Rect nameRect = new Rect(checkRect.xMax + 5f, rowRect.y,
                rowRect.width - checkRect.width - 12f, rowRect.height);
            ManagerModData modData = FindModState(mod.PackageId);
            List<ManagerTagData> assignedTags = modData == null || modData.TagIds == null
                ? new List<ManagerTagData>()
                : modData.TagIds.Select(FindTag).Where(tag => tag != null).ToList();
            string tagTooltip = string.Join(", ", assignedTags.Select(tag => tag.Name));
            float maximumTagArea = nameRect.width * 0.55f;
            bool hasTagOverflow = assignedTags.Sum(tag => GetTagChipWidth(tag.Name) + 3f) > maximumTagArea;
            float ellipsisWidth = hasTagOverflow ? Mathf.Min(22f, maximumTagArea) : 0f;
            float chipsCapacity = Mathf.Max(0f, maximumTagArea - ellipsisWidth);
            List<ManagerTagData> visibleTags = new List<ManagerTagData>();
            float visibleTagWidth = 0f;
            for (int i = 0; i < assignedTags.Count; i++)
            {
                float chipWidth = GetTagChipWidth(assignedTags[i].Name);
                float nextWidth = visibleTagWidth + (visibleTags.Count > 0 ? 3f : 0f) + chipWidth;
                if (nextWidth > chipsCapacity)
                    break;
                visibleTags.Add(assignedTags[i]);
                visibleTagWidth = nextWidth;
            }
            float tagAreaWidth = visibleTagWidth + (visibleTags.Count > 0 && hasTagOverflow ? 3f : 0f) +
                (hasTagOverflow ? ellipsisWidth : 0f);
            if (tagAreaWidth > 0f)
            {
                float tagsX = nameRect.xMax - tagAreaWidth;
                nameRect.width = Mathf.Max(0f, tagsX - nameRect.x - 4f);
                float chipX = tagsX;
                for (int i = 0; i < visibleTags.Count; i++)
                {
                    float chipWidth = GetTagChipWidth(visibleTags[i].Name);
                    DrawTagChip(new Rect(chipX, rowRect.y + 3f, chipWidth, 21f), visibleTags[i]);
                    chipX += chipWidth + 3f;
                }
                if (hasTagOverflow && ellipsisWidth > 0f)
                {
                    Rect moreRect = new Rect(tagsX + visibleTagWidth +
                        (visibleTags.Count > 0 ? 3f : 0f), rowRect.y + 3f,
                        ellipsisWidth, 21f);
                    DrawTagOverflowChip(moreRect);
                    TooltipHandler.TipRegion(moreRect, tagTooltip);
                }
            }
            if (Widgets.ButtonInvisible(nameRect))
                SelectMod(visibleIndex, Event.current.control, Event.current.shift);

            string name = GetDisplayName(mod);
            Widgets.Label(nameRect, protectedMod ? name + ManagerTranslation.Get("LMM_0001") : name);
        }

        private static float GetTagChipWidth(string name)
        {
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float width = Mathf.Clamp(Text.CalcSize(name ?? string.Empty).x + 12f, 34f, 112f);
            Text.Font = oldFont;
            return width;
        }

        private static bool DrawTagChip(Rect rect, ManagerTagData tag)
        {
            if (tag == null)
                return false;
            Widgets.DrawBoxSolid(rect, ManagerTagColors.Background(tag.ColorHex));
            Widgets.DrawBox(rect);
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = ManagerTagColors.Foreground(tag.ColorHex);
            Widgets.Label(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height), tag.Name);
            GUI.color = oldColor;
            Text.Font = oldFont;
            TooltipHandler.TipRegion(rect, tag.Name);
            return Widgets.ButtonInvisible(rect);
        }

        private static void DrawTagOverflowChip(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.24f, 0.24f, 0.24f, 1f));
            Widgets.DrawBox(rect);
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.white;
            Widgets.Label(rect, "…");
            GUI.color = oldColor;
            Text.Font = oldFont;
        }

        private void DrawDetails(Rect rect)
        {
            Widgets.DrawBox(rect);
            Rect inner = rect.ContractedBy(12f);
            ModMetaData mod = FindMod(primarySelectedPackageId);
            if (mod == null)
            {
                Widgets.Label(inner, ManagerTranslation.Get("LMM_0037"));
                return;
            }

            float y = inner.y;
            Texture2D previewImage = mod.PreviewImage;
            if (previewImage != null)
            {
                float previewHeight = Mathf.Min(190f,
                    inner.width * previewImage.height / Mathf.Max(1f, previewImage.width));
                Rect previewRect = new Rect(inner.x, y, inner.width, previewHeight);
                GUI.DrawTexture(previewRect, previewImage, ScaleMode.ScaleToFit);
                y += previewHeight + 8f;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, y, inner.width, 36f), GetDisplayName(mod));
            Text.Font = GameFont.Small;
            y += 38f;

            string vanillaName = mod.Name.NullOrEmpty() ? mod.FolderName : mod.Name;
            if (!string.Equals(GetDisplayName(mod), vanillaName, StringComparison.Ordinal))
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 24f),
                    ManagerTranslation.Get("LMM_0024") + vanillaName);
                y += 26f;
            }

            string versionText = GetModVersionText(mod);
            if (!versionText.NullOrEmpty())
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 24f), ManagerTranslation.Get("LMM_0042") + versionText);
                y += 26f;
            }

            string supportedVersions = GetSupportedGameVersionText(mod);
            if (!supportedVersions.NullOrEmpty())
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 24f),
                    ManagerTranslation.Get("LMM_0073") + supportedVersions);
                y += 26f;
            }

            Widgets.Label(new Rect(inner.x, y, inner.width, 24f),
                "PackageId: " + mod.PackageIdPlayerFacing);
            y += 28f;
            Widgets.Label(new Rect(inner.x, y, inner.width, 24f),
                ManagerTranslation.Get("LMM_0072") + mod.AuthorsString);
            y += 32f;

            ManagerModData modData = FindModState(mod.PackageId);
            Widgets.Label(new Rect(inner.x, y, 48f, 26f), ManagerTranslation.Get("LMM_0089"));
            float chipX = inner.x + 48f;
            float chipRight = inner.xMax - 38f;
            List<ManagerTagData> assignedTags = new List<ManagerTagData>();
            if (modData != null && modData.TagIds != null)
            {
                for (int i = 0; i < modData.TagIds.Count; i++)
                {
                    ManagerTagData tag = FindTag(modData.TagIds[i]);
                    if (tag == null)
                        continue;
                    assignedTags.Add(tag);
                }
            }

            string allTagNames = string.Join(", ", assignedTags.Select(tag => tag.Name));
            float tagWidthTotal = assignedTags.Sum(tag => GetTagChipWidth(tag.Name) + 3f);
            bool tagsOmitted = tagWidthTotal > chipRight - chipX;
            float chipLimit = chipRight - chipX - (tagsOmitted ? 25f : 0f);
            for (int i = 0; i < assignedTags.Count; i++)
            {
                ManagerTagData tag = assignedTags[i];
                float chipWidth = GetTagChipWidth(tag.Name);
                float renderedWidth = chipX - (inner.x + 48f);
                float requiredWidth = (i > 0 ? 3f : 0f) + chipWidth;
                if (renderedWidth + requiredWidth > chipLimit)
                {
                    tagsOmitted = true;
                    break;
                }

                Rect chipRect = new Rect(chipX, y + 1f, chipWidth, 23f);
                if (DrawTagChip(chipRect, tag))
                {
                    tagMode = true;
                    SelectTag(tag);
                }
                if (Event.current.type == EventType.MouseDown && Event.current.button == 1 &&
                    chipRect.Contains(Event.current.mousePosition))
                {
                    SetTagForSelection(new List<string> { mod.PackageId }, tag, false);
                    Event.current.Use();
                }
                chipX += chipWidth + 3f;
            }

            if (tagsOmitted)
            {
                Rect moreRect = new Rect(chipX, y + 1f,
                    22f, 23f);
                DrawTagOverflowChip(moreRect);
                TooltipHandler.TipRegion(moreRect, allTagNames);
            }
            if (Widgets.ButtonText(new Rect(inner.xMax - 30f, y, 30f, 26f), "+"))
                OpenCreateTagDialog(new[] { mod.PackageId });
            y += 32f;

            List<ModRequirement> requirements = mod.GetRequirements().ToList();
            if (requirements.Count > 0)
            {
                Rect checkboxRect = new Rect(inner.x, y, inner.width, 26f);
                bool priorDisplayFulfilled = displayFulfilledRequirements;
                Widgets.CheckboxLabeled(checkboxRect,
                    "ModDisplayFulfilledRequirements".Translate(),
                    ref displayFulfilledRequirements);
                y += 28f;

                for (int i = 0; i < requirements.Count; i++)
                {
                    ModRequirement requirement = requirements[i];
                    if (requirement == null ||
                        (requirement.IsSatisfied && !displayFulfilledRequirements))
                        continue;

                    Rect requirementRect = new Rect(inner.x + 8f, y,
                        inner.width - 8f, 24f);
                    Rect statusIconRect = new Rect(requirementRect.x,
                        requirementRect.y + 2f, 20f, 20f);
                    if (requirement.StatusIcon != null)
                        GUI.DrawTexture(statusIconRect, requirement.StatusIcon,
                            ScaleMode.ScaleToFit);

                    string requirementName = ReadMetadataValue(requirement, "displayName") as string;
                    if (requirementName.NullOrEmpty())
                        requirementName = ReadMetadataValue(requirement, "packageId") as string;
                    if (requirementName.NullOrEmpty())
                        requirementName = requirement.RequirementTypeLabel;
                    Widgets.Label(new Rect(statusIconRect.xMax + 4f, requirementRect.y,
                        requirementRect.width - 24f, requirementRect.height), requirementName);
                    TooltipHandler.TipRegion(requirementRect, requirement.Tooltip);
                    y += 24f;
                }

                if (priorDisplayFulfilled != displayFulfilledRequirements)
                    descriptionScroll = Vector2.zero;
            }

            List<string> folderIssues = GetModLoadFolderIssues(mod);
            if (folderIssues != null && folderIssues.Count > 0)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 24f),
                    "ModHadLoadFolderIssues".Translate());
                y += 24f;
                string issueText = GenText.ToLineList(folderIssues, "  - ");
                float issueHeight = Text.CalcHeight(issueText, inner.width);
                Widgets.Label(new Rect(inner.x + 8f, y, inner.width - 8f, issueHeight), issueText);
                y += issueHeight + 6f;
            }

            float actionX = inner.x;
            float actionY = y;
            bool hasActionButtons = false;
            if (GetAvailableModActionCount(mod) > 0)
            {
                DrawDetailActionButton(inner, ref actionX, ref actionY, 140f,
                    "MoreActions".Translate(), () => OpenVanillaModActions(mod));
                hasActionButtons = true;
            }
            ManagerModData selectedModState = FindModState(mod.PackageId);
            if (!IsProtected(mod) && selectedModState != null)
            {
                DrawDetailActionButton(inner, ref actionX, ref actionY, 120f,
                    (selectedModState.Enabled ? "Disable" : "Enable").Translate(),
                    () => selectedModState.Enabled = !selectedModState.Enabled);
                hasActionButtons = true;
            }

            Mod loadedMod = FindLoadedMod(mod);
            if (loadedMod != null && !loadedMod.SettingsCategory().NullOrEmpty())
            {
                DrawDetailActionButton(inner, ref actionX, ref actionY, 150f,
                    "ModOptions".Translate(),
                    () => Find.WindowStack.Add(new Dialog_ModSettings(loadedMod)));
                hasActionButtons = true;
            }

            if (SteamManager.Initialized && mod.CanToUploadToWorkshop())
            {
                DrawDetailActionButton(inner, ref actionX, ref actionY, 220f,
                    GetWorkshopUploadLabel(mod), () => OpenWorkshopUploadDialog(mod));
                hasActionButtons = true;
            }

            if (hasActionButtons)
                y = actionY + 34f;
            Rect descriptionRect = new Rect(inner.x, y, inner.width,
                Mathf.Max(80f, inner.height - (y - inner.y) - 4f));
            string description = mod.Description.NullOrEmpty()
                ? ManagerTranslation.Get("LMM_0061")
                : WebUtility.HtmlDecode(mod.Description);
            float descriptionHeight = Text.CalcHeight(description, descriptionRect.width);
            float descriptionScrollbarWidth = descriptionHeight > descriptionRect.height
                ? ManagerUiMetrics.VerticalScrollbarWidth
                : 0f;
            float descriptionTextWidth = descriptionRect.width - descriptionScrollbarWidth;
            descriptionHeight = Mathf.Max(descriptionRect.height,
                Text.CalcHeight(description, descriptionTextWidth));
            Rect descriptionView = new Rect(0f, 0f, descriptionTextWidth, descriptionHeight);
            Widgets.BeginScrollView(descriptionRect, ref descriptionScroll, descriptionView);
            Widgets.Label(new Rect(0f, 0f, descriptionView.width, descriptionView.height), description);
            Widgets.EndScrollView();
        }

        private static void DrawDetailActionButton(
            Rect bounds,
            ref float x,
            ref float y,
            float preferredWidth,
            string label,
            Action action)
        {
            float width = Mathf.Min(preferredWidth, bounds.width);
            if (x + width > bounds.xMax && x > bounds.x)
            {
                x = bounds.x;
                y += 34f;
            }

            Rect buttonRect = new Rect(x, y, width, 28f);
            if (Widgets.ButtonText(buttonRect, label) && action != null)
                action();
            x += width + 6f;
        }

        private static int GetAvailableModActionCount(ModMetaData mod)
        {
            if (mod == null)
                return 0;

            int count = 0;
            if (SteamManager.Initialized && mod.OnSteamWorkshop)
                count++;
            if (!GetWorkshopUrl(mod).NullOrEmpty())
                count++;
            if (mod.RootDir != null && !mod.Official)
                count++;
            Mod loadedMod = FindLoadedMod(mod);
            if (loadedMod != null && !loadedMod.SettingsCategory().NullOrEmpty())
                count++;
            return count;
        }

        private void OpenVanillaModActions(ModMetaData mod)
        {
            if (mod == null)
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            string workshopUrl = GetWorkshopUrl(mod);
            if (SteamManager.Initialized && mod.OnSteamWorkshop)
                options.Add(new FloatMenuOption("Unsubscribe".Translate(),
                    () => ConfirmUnsubscribe(new List<string> { mod.PackageId })));
            if (!workshopUrl.NullOrEmpty())
                options.Add(new FloatMenuOption("WorkshopPage".Translate(),
                    () => OpenUrl(workshopUrl)));
            if (mod.RootDir != null && !mod.Official)
                options.Add(new FloatMenuOption("ModFolder".Translate(),
                    () => OpenModFolder(mod)));
            if (!mod.Url.NullOrEmpty())
                options.Add(new FloatMenuOption("ModWebsite".Translate(),
                    () => OpenUrl(mod.Url)));
            Mod loadedMod = FindLoadedMod(mod);
            if (loadedMod != null && !loadedMod.SettingsCategory().NullOrEmpty())
                options.Add(new FloatMenuOption("ModOptions".Translate(),
                    () => Find.WindowStack.Add(new Dialog_ModSettings(loadedMod))));

            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void OpenWorkshopUploadDialog(ModMetaData mod)
        {
            if (mod == null || !SteamManager.Initialized || !mod.CanToUploadToWorkshop())
                return;

            Find.WindowStack.Add(new Dialog_ConfirmModUpload(mod,
                () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "ConfirmContentAuthor".Translate(),
                    () => InvokeWorkshopUpload(mod)))));
        }

        private static Mod FindLoadedMod(ModMetaData mod)
        {
            if (mod == null)
                return null;
            return LoadedModManager.ModHandles.FirstOrDefault(loaded => loaded != null &&
                loaded.Content != null && string.Equals(loaded.Content.PackageId, mod.PackageId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> GetModLoadFolderIssues(ModMetaData mod)
        {
            object loadFolders = ReadMetadataValue(mod, "loadFolders");
            if (loadFolders == null)
                return new List<string>();
            MethodInfo method = loadFolders.GetType().GetMethod("GetIssueList",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(ModMetaData) }, null);
            return method == null
                ? new List<string>()
                : method.Invoke(loadFolders, new object[] { mod }) as List<string>
                    ?? new List<string>();
        }

        private static string GetWorkshopUploadLabel(ModMetaData mod)
        {
            MethodInfo getPublishedFileId = typeof(ModMetaData).GetMethod("GetPublishedFileId",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object publishedFileId = getPublishedFileId == null
                ? null
                : getPublishedFileId.Invoke(mod, null);
            Type workshopType = typeof(ModMetaData).Assembly.GetType("Verse.Steam.Workshop");
            MethodInfo uploadButtonLabel = workshopType == null || publishedFileId == null
                ? null
                : workshopType.GetMethod("UploadButtonLabel",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { publishedFileId.GetType() }, null);
            return uploadButtonLabel == null
                ? ManagerTranslation.Get("LMM_0129")
                : (string)uploadButtonLabel.Invoke(null, new[] { publishedFileId });
        }

        private static void InvokeWorkshopUpload(ModMetaData mod)
        {
            Type workshopType = typeof(ModMetaData).Assembly.GetType("Verse.Steam.Workshop");
            MethodInfo bakeAndUpload = workshopType == null
                ? null
                : workshopType.GetMethod("BakeAndUpload",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(ModMetaData) }, null);
            if (bakeAndUpload == null)
            {
                Log.Error("[Lilly Mod Manager] RimWorld Workshop upload API was not found.");
                return;
            }
            bakeAndUpload.Invoke(null, new object[] { mod });
        }

        private static void OpenModFolder(ModMetaData mod)
        {
            if (mod == null || mod.RootDir == null)
                return;
            try
            {
                Process.Start(new ProcessStartInfo(mod.RootDir.FullName)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                Log.Error("[Lilly Mod Manager] Could not open mod folder: " + exception);
                Messages.Message(ManagerTranslation.Get("LMM_0034"), MessageTypeDefOf.RejectInput);
            }
        }

        private void DrawBottomButtons(Rect rect)
        {
            float gap = 5f;
            float buttonWidth = (rect.width - gap * 7f) / 8f;
            float x = rect.x;
            string modeLabel = tagMode ? ManagerTranslation.Get("LMM_0075") : ManagerTranslation.Get("LMM_0080");
            if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), modeLabel))
                ToggleManagementMode();
            x += buttonWidth + gap;

            if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height),
                tagMode ? ManagerTranslation.Get("LMM_0086") : ManagerTranslation.Get("LMM_0079")))
            {
                if (tagMode)
                    OpenCreateTagDialog();
                else
                    OpenCreateCategoryDialog(categories.Count);
            }
            x += buttonWidth + gap;

            if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0023")))
                ConfirmUnsubscribeSelected();
            x += buttonWidth + gap;

            if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0094")))
                OpenFastFolderMoveDialog();
            x += buttonWidth + gap;

            if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0039")))
                OpenSavePresetDialog();
            x += buttonWidth + gap;

            if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0038")))
                OpenPresetMenu();
            x += buttonWidth + gap;

            if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0044")))
                CancelChanges();
            x += buttonWidth + gap;

            if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0070")))
                ApplyChanges();
        }

        private void ToggleManagementMode()
        {
            tagMode = !tagMode;
            selectedPackageIds.Clear();
            primarySelectedPackageId = null;
            lastSelectedIndex = -1;
            currentFolderId = tagMode
                ? FindCurrentTag()?.Folders.FirstOrDefault()?.Id
                : FindCurrentCategory()?.Folders.FirstOrDefault()?.Id;
            RefreshVisibleMods();
        }

        private void CancelChanges()
        {
            PersistTagData();
            Close();
        }

        private void HandleSelectAll()
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown || current.keyCode != KeyCode.A ||
                !current.control)
                return;

            SelectAllVisible();
            current.Use();
        }

        private void HandleModListNavigation(Rect listRect)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown)
                modListHasKeyboardFocus = listRect.Contains(current.mousePosition);

            if (!modListHasKeyboardFocus || current.type != EventType.KeyDown ||
                (current.keyCode != KeyCode.UpArrow && current.keyCode != KeyCode.DownArrow) ||
                current.control || current.alt || current.command)
                return;

            if (visibleMods.Count == 0)
                return;

            int currentIndex = visibleMods.FindIndex(mod =>
                string.Equals(mod.PackageId, primarySelectedPackageId,
                    StringComparison.OrdinalIgnoreCase));
            int nextIndex;
            if (currentIndex < 0)
                nextIndex = current.keyCode == KeyCode.DownArrow ? 0 : visibleMods.Count - 1;
            else
                nextIndex = Mathf.Clamp(currentIndex +
                    (current.keyCode == KeyCode.DownArrow ? 1 : -1), 0, visibleMods.Count - 1);

            if (nextIndex == currentIndex)
            {
                current.Use();
                return;
            }

            SelectMod(nextIndex, false, current.shift && lastSelectedIndex >= 0);
            float rowY = GetVisibleModRowY(visibleMods[nextIndex].PackageId);
            float viewportHeight = Mathf.Max(1f, listRect.height - 16f);
            if (rowY < modListScroll.y)
                modListScroll.y = rowY;
            else if (rowY + RowHeight > modListScroll.y + viewportHeight)
                modListScroll.y = rowY + RowHeight - viewportHeight;
            modListScroll.y = Mathf.Max(0f, modListScroll.y);
            current.Use();
        }

        private float GetVisibleModRowY(string packageId)
        {
            float y = 0f;
            if (tagMode)
            {
                ManagerTagData tag = FindCurrentTag();
                if (tag == null)
                    return 0f;
                for (int folderIndex = 0; folderIndex < tag.Folders.Count; folderIndex++)
                {
                    ManagerFolderData folder = tag.Folders[folderIndex];
                    y += RowHeight;
                    if (folder.Collapsed)
                        continue;
                    List<string> ids = GetTagFolderModIds(tag, folder);
                    for (int i = 0; i < ids.Count; i++)
                    {
                        ModMetaData mod = FindMod(ids[i]);
                        if (mod == null || !MatchesSearch(mod))
                            continue;
                        if (string.Equals(mod.PackageId, packageId,
                            StringComparison.OrdinalIgnoreCase))
                            return y;
                        y += RowHeight;
                    }
                }
            }
            else
            {
                ManagerCategoryData category = FindCurrentCategory();
                if (category == null)
                    return 0f;
                for (int folderIndex = 0; folderIndex < category.Folders.Count; folderIndex++)
                {
                    ManagerFolderData folder = category.Folders[folderIndex];
                    y += RowHeight;
                    if (folder.Collapsed)
                        continue;
                    for (int i = 0; i < folder.ModIds.Count; i++)
                    {
                        ModMetaData mod = FindMod(folder.ModIds[i]);
                        if (mod == null || !MatchesSearch(mod))
                            continue;
                        if (string.Equals(mod.PackageId, packageId,
                            StringComparison.OrdinalIgnoreCase))
                            return y;
                        y += RowHeight;
                    }
                }
            }
            return 0f;
        }

        private void SelectAllVisible()
        {
            selectedPackageIds.Clear();
            for (int i = 0; i < visibleMods.Count; i++)
                selectedPackageIds.Add(visibleMods[i].PackageId);
            if (visibleMods.Count > 0)
            {
                primarySelectedPackageId = visibleMods[0].PackageId;
                lastSelectedIndex = 0;
            }
        }

        private void ClearSelection()
        {
            selectedPackageIds.Clear();
            primarySelectedPackageId = null;
            lastSelectedIndex = -1;
        }

        private void SelectMod(int index, bool control, bool shift)
        {
            if (index < 0 || index >= visibleMods.Count)
                return;

            string packageId = visibleMods[index].PackageId;
            if (shift && lastSelectedIndex >= 0)
            {
                if (!control)
                    selectedPackageIds.Clear();
                int start = Mathf.Min(lastSelectedIndex, index);
                int end = Mathf.Max(lastSelectedIndex, index);
                for (int i = start; i <= end; i++)
                    selectedPackageIds.Add(visibleMods[i].PackageId);
            }
            else if (control)
            {
                if (!selectedPackageIds.Add(packageId))
                    selectedPackageIds.Remove(packageId);
                lastSelectedIndex = index;
            }
            else
            {
                selectedPackageIds.Clear();
                selectedPackageIds.Add(packageId);
                lastSelectedIndex = index;
            }

            primarySelectedPackageId = selectedPackageIds.Contains(packageId)
                ? packageId
                : selectedPackageIds.FirstOrDefault();
        }

        private void SetSelectedEnabled(bool enabled)
        {
            for (int i = 0; i < selectedPackageIds.Count; i++)
            {
                string packageId = selectedPackageIds.ElementAt(i);
                ModMetaData mod = FindMod(packageId);
                ManagerModData state = FindModState(packageId);
                if (mod != null && state != null && !IsProtected(mod))
                    state.Enabled = enabled;
            }
        }

        private void OpenFolderContextMenu(ManagerCategoryData category, ManagerFolderData folder)
        {
            if (category == null || folder == null)
                return;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0099"),
                () => OpenRenameFolderDialog(category, folder)));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0101"),
                () => SetFolderEnabled(folder.ModIds, true)));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0095"),
                () => SetFolderEnabled(folder.ModIds, false)));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0100"),
                () => OpenCreateFolderDialog(category, folder)));
            if (folder.Id != "uncategorized")
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0096"),
                    () => ConfirmDeleteFolder(category, folder)));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0093"),
                () => SelectFolderMods(folder.ModIds)));
            options.Add(new FloatMenuOption(folder.Collapsed ? ManagerTranslation.Get("LMM_0092") : ManagerTranslation.Get("LMM_0071"),
                () => folder.Collapsed = !folder.Collapsed));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenFolderContextMenu(ManagerTagData tag, ManagerFolderData folder)
        {
            if (tag == null || folder == null)
                return;
            List<string> packageIds = GetTagFolderModIds(tag, folder);
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0099"),
                () => OpenRenameFolderDialog(tag, folder)));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0101"),
                () => SetFolderEnabled(packageIds, true)));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0095"),
                () => SetFolderEnabled(packageIds, false)));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0100"),
                () => OpenCreateFolderDialog(tag, folder)));
            if (folder.Id != "uncategorized")
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0096"),
                    () => ConfirmDeleteFolder(tag, folder)));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0093"),
                () => SelectFolderMods(packageIds)));
            options.Add(new FloatMenuOption(folder.Collapsed ? ManagerTranslation.Get("LMM_0092") : ManagerTranslation.Get("LMM_0071"), () =>
            {
                folder.Collapsed = !folder.Collapsed;
                PersistTagData();
                RefreshVisibleMods();
            }));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void SetFolderEnabled(IEnumerable<string> packageIds, bool enabled)
        {
            if (packageIds == null)
                return;
            foreach (string packageId in packageIds)
            {
                ModMetaData mod = FindMod(packageId);
                ManagerModData state = FindModState(packageId);
                if (mod != null && state != null && !IsProtected(mod))
                    state.Enabled = enabled;
            }
        }

        private string GetFolderStateLabel(IEnumerable<string> packageIds)
        {
            int enabledCount = 0;
            int eligibleCount = 0;
            if (packageIds != null)
            {
                foreach (string packageId in packageIds)
                {
                    ModMetaData mod = FindMod(packageId);
                    ManagerModData state = FindModState(packageId);
                    if (mod == null || state == null || IsProtected(mod))
                        continue;
                    eligibleCount++;
                    if (state.Enabled)
                        enabledCount++;
                }
            }
            if (eligibleCount == 0 || enabledCount == eligibleCount)
                return "●";
            if (enabledCount == 0)
                return "○";
            return "◐";
        }

        private void SelectFolderMods(IEnumerable<string> packageIds)
        {
            selectedPackageIds.Clear();
            if (packageIds != null)
            {
                foreach (string packageId in packageIds)
                    if (FindMod(packageId) != null && MatchesSearch(FindMod(packageId)))
                        selectedPackageIds.Add(packageId);
            }
            primarySelectedPackageId = selectedPackageIds.FirstOrDefault();
            lastSelectedIndex = primarySelectedPackageId == null
                ? -1
                : visibleMods.FindIndex(mod => mod.PackageId == primarySelectedPackageId);
        }

        private void OpenMoveMenu()
        {
            if (selectedPackageIds.Count == 0)
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < categories.Count; i++)
            {
                ManagerCategoryData category = categories[i];
                ManagerCategoryData target = category;
                options.Add(new FloatMenuOption(target.Name + ManagerTranslation.Get("LMM_0003"),
                    () => OpenMoveSelectionToNewFolder(target)));
                if (target.Id != currentCategoryId)
                {
                    options.Add(new FloatMenuOption(target.Name + ManagerTranslation.Get("LMM_0002"),
                        () => MoveSelectionTo(target)));
                    continue;
                }

                for (int folderIndex = 0; folderIndex < target.Folders.Count; folderIndex++)
                {
                    ManagerFolderData folder = target.Folders[folderIndex];
                    if (folder.Id == currentFolderId)
                        continue;

                    ManagerFolderData targetFolder = folder;
                    options.Add(new FloatMenuOption(
                        target.Name + " / " + targetFolder.Name,
                        () => MoveSelectionToFolder(target, targetFolder)));
                }
            }
            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenMoveCategoryMenu()
        {
            if (selectedPackageIds.Count == 0)
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < categories.Count; i++)
            {
                ManagerCategoryData target = categories[i];
                if (target == null || target.Id == currentCategoryId)
                    continue;

                ManagerCategoryData category = target;
                options.Add(new FloatMenuOption(category.Name,
                    () => MoveSelectionTo(category)));
            }

            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
            else
                Messages.Message(ManagerTranslation.Get("LMM_0062"), MessageTypeDefOf.RejectInput);
        }

        private void OpenMoveSelectionToNewFolder(ManagerCategoryData category)
        {
            List<string> movingIds = selectedPackageIds.ToList();
            Find.WindowStack.Add(new ManagerFolderNameDialog(
                ManagerTranslation.Get("LMM_0102"),
                "",
                name =>
                {
                    ManagerFolderData folder = category.Folders.FirstOrDefault(existing =>
                        string.Equals(existing.Name, name.Trim(),
                            StringComparison.OrdinalIgnoreCase));
                    if (folder == null)
                    {
                        folder = new ManagerFolderData(Guid.NewGuid().ToString("N"), name.Trim());
                        category.Folders.Add(folder);
                    }

                    folder.Collapsed = false;
                    ManagerFolderData targetFolder = folder;
                    List<string> priorSelection = selectedPackageIds.ToList();
                    selectedPackageIds.Clear();
                    for (int i = 0; i < movingIds.Count; i++)
                        selectedPackageIds.Add(movingIds[i]);
                    MoveSelectionToFolder(category, targetFolder);
                    selectedPackageIds.Clear();
                    foreach (string id in priorSelection)
                        if (FindMod(id) != null)
                            selectedPackageIds.Add(id);
                    currentCategoryId = category.Id;
                    currentFolderId = targetFolder.Id;
                    RefreshVisibleMods();
                    return true;
                }));
        }

        private void ClearTagsForSelection(IEnumerable<string> packageIds)
        {
                if (packageIds == null)
                    return;

                HashSet<string> selectedIds =
                    new HashSet<string>(packageIds, StringComparer.OrdinalIgnoreCase);
                foreach (ManagerModData modData in modStates)
                {
                    if (modData == null || !selectedIds.Contains(modData.PackageId))
                        continue;
                    if (modData.TagIds != null)
                        modData.TagIds.Clear();
                }

                foreach (ManagerTagData tag in tags)
                {
                    if (tag == null || tag.Folders == null)
                        continue;
                    foreach (ManagerFolderData folder in tag.Folders)
                    {
                        if (folder != null && folder.ModIds != null)
                            folder.ModIds.RemoveAll(selectedIds.Contains);
                    }
                }

                PersistTagData();
                if (tagMode)
                    RefreshVisibleMods();
        }

        private void OpenTagFolderMoveMenu()
        {
            ManagerTagData tag = FindCurrentTag();
            if (tag == null || selectedPackageIds.Count == 0)
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0050"),
                () => OpenMoveSelectionToNewTagFolder(tag)));
            for (int i = 0; i < tag.Folders.Count; i++)
            {
                ManagerFolderData folder = tag.Folders[i];
                if (folder.Id == currentFolderId)
                    continue;

                ManagerFolderData target = folder;
                options.Add(new FloatMenuOption(target.Name,
                    () => MoveSelectionToTagFolder(tag, target)));
            }
            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenMoveSelectionToNewTagFolder(ManagerTagData tag)
        {
            Find.WindowStack.Add(new ManagerFolderNameDialog(
                ManagerTranslation.Get("LMM_0088"),
                "",
                name =>
                {
                    ManagerFolderData folder = tag.Folders.FirstOrDefault(existing =>
                        string.Equals(existing.Name, name.Trim(),
                            StringComparison.OrdinalIgnoreCase));
                    if (folder == null)
                    {
                        folder = new ManagerFolderData(Guid.NewGuid().ToString("N"), name.Trim());
                        tag.Folders.Add(folder);
                    }
                    MoveSelectionToTagFolder(tag, folder);
                    return true;
                }));
        }

        private void OpenSelectedTagMenu(IEnumerable<string> packageIds)
        {
            List<string> selectedIds = packageIds == null
                ? new List<string>()
                : packageIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (selectedIds.Count == 0)
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < tags.Count; i++)
            {
                ManagerTagData tag = tags[i];
                if (tag == null || tag.IsSystem)
                    continue;

                ManagerTagData target = tag;
                bool allHaveTag = selectedIds.All(id => HasTag(id, target.Id));
                string action = allHaveTag ? ManagerTranslation.Get("LMM_0085") : ManagerTranslation.Get("LMM_0087");
                options.Add(new FloatMenuOption(action + target.Name,
                    () => SetTagForSelection(selectedIds, target, !allHaveTag)));
            }
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0048"),
                () => OpenCreateTagDialog(selectedIds)));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenFastFolderMoveDialog()
        {
            if (selectedPackageIds.Count == 0)
                return;

            List<List<FastFolderMoveDialog.FolderMoveTarget>> columns =
                new List<List<FastFolderMoveDialog.FolderMoveTarget>>();

            if (tagMode)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    ManagerTagData tag = tags[i];
                    if (tag == null)
                        continue;

                    List<FastFolderMoveDialog.FolderMoveTarget> items =
                        new List<FastFolderMoveDialog.FolderMoveTarget>();
                    for (int folderIndex = 0; folderIndex < tag.Folders.Count; folderIndex++)
                    {
                        ManagerFolderData folder = tag.Folders[folderIndex];
                        if (folder == null)
                            continue;
                        items.Add(new FastFolderMoveDialog.FolderMoveTarget(
                            folder.Name + " (" + GetInstalledCount(tag, folder) + ")",
                            tag.Name,
                            null,
                            tag,
                            folder,
                            "tag:" + tag.Id + ":" + folder.Id,
                            tag.Id == currentTagId && folder.Id == currentFolderId));
                    }
                    columns.Add(items);
                }
            }
            else
            {
                for (int i = 0; i < categories.Count; i++)
                {
                    ManagerCategoryData category = categories[i];
                    if (category == null)
                        continue;

                    List<FastFolderMoveDialog.FolderMoveTarget> items =
                        new List<FastFolderMoveDialog.FolderMoveTarget>();
                    for (int folderIndex = 0; folderIndex < category.Folders.Count; folderIndex++)
                    {
                        ManagerFolderData folder = category.Folders[folderIndex];
                        if (folder == null)
                            continue;
                        items.Add(new FastFolderMoveDialog.FolderMoveTarget(
                            folder.Name + " (" + GetInstalledCount(folder) + ")",
                            category.Name,
                            category,
                            null,
                            folder,
                            "category:" + category.Id + ":" + folder.Id,
                            category.Id == currentCategoryId && folder.Id == currentFolderId));
                    }
                    columns.Add(items);
                }
            }

            if (columns.Count == 0)
                return;

            Find.WindowStack.Add(new FastFolderMoveDialog(columns,
                target =>
                {
                    if (tagMode)
                    {
                        if (target != null && target.Tag != null && target.Folder != null)
                            MoveSelectionToTagFolder(target.Tag, target.Folder);
                        return;
                    }

                    if (target != null && target.Category != null && target.Folder != null)
                        MoveSelectionToFolder(target.Category, target.Folder);
                }));
        }

        private void SetTagForSelection(
            List<string> packageIds,
            ManagerTagData tag,
            bool add)
        {
            if (tag == null || tag.IsSystem)
                return;

            ManagerFolderData unfiled = FindFolder(tag, "uncategorized");
            if (unfiled == null)
            {
                unfiled = new ManagerFolderData("uncategorized", ManagerTranslation.Get("LMM_0040"));
                tag.Folders.Insert(0, unfiled);
            }

            for (int i = 0; i < packageIds.Count; i++)
            {
                ManagerModData modData = FindModState(packageIds[i]);
                if (modData == null)
                    continue;
                if (modData.TagIds == null)
                    modData.TagIds = new List<string>();

                if (add)
                {
                    if (!modData.TagIds.Contains(tag.Id))
                    {
                        modData.TagIds.Add(tag.Id);
                        unfiled.ModIds.Add(packageIds[i]);
                    }
                }
                else
                {
                    modData.TagIds.RemoveAll(id =>
                        string.Equals(id, tag.Id, StringComparison.OrdinalIgnoreCase));
                    for (int folderIndex = 0; folderIndex < tag.Folders.Count; folderIndex++)
                        tag.Folders[folderIndex].ModIds.RemoveAll(id =>
                            string.Equals(id, packageIds[i], StringComparison.OrdinalIgnoreCase));
                }
            }

            if (tagMode && tag.Id == currentTagId)
                RefreshVisibleMods();
            PersistTagData();
        }

        private void OpenCreateTagDialog(IEnumerable<string> packageIds = null)
        {
            List<string> selectedIds = packageIds == null
                ? new List<string>()
                : packageIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Find.WindowStack.Add(new ManagerFolderNameDialog(
                ManagerTranslation.Get("LMM_0047"),
                "",
                name =>
                {
                    ManagerTagData existingTag = tags.FirstOrDefault(tag =>
                        string.Equals(tag.Name, name.Trim(),
                            StringComparison.OrdinalIgnoreCase));
                    if (existingTag != null)
                    {
                        if (selectedIds.Count > 0)
                            SetTagForSelection(selectedIds, existingTag, true);
                        return true;
                    }

                    ManagerTagData tag = new ManagerTagData(
                        Guid.NewGuid().ToString("N"), name.Trim());
                    tags.Add(tag);
                    if (selectedIds.Count > 0)
                        SetTagForSelection(selectedIds, tag, true);

                    PersistTagData();
                    return true;
                }));
        }

        private void OpenRenameTagDialog(ManagerTagData tag)
        {
            if (tag == null || tag.IsSystem)
                return;

            Find.WindowStack.Add(new ManagerFolderNameDialog(
                ManagerTranslation.Get("LMM_0084"),
                tag.Name,
                name =>
                {
                    if (tags.Any(existing => existing != tag &&
                        string.Equals(existing.Name, name.Trim(),
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        Messages.Message(ManagerTranslation.Get("LMM_0016"),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    tag.Name = name.Trim();
                    PersistTagData();
                    return true;
                }));
        }

        private void OpenTagColorDialog(ManagerTagData tag)
        {
            if (tag == null || tag.IsSystem)
                return;
            Find.WindowStack.Add(new ManagerTagColorDialog(tag, colorHex =>
            {
                tag.ColorHex = colorHex;
                PersistTagData();
            }));
        }

        private void ConfirmDeleteTag(ManagerTagData tag)
        {
            if (tag == null)
                return;
            if (tag.IsSystem)
            {
                Messages.Message(ManagerTranslation.Get("LMM_0083"), MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                ManagerTranslation.Get("LMM_0091"),
                () =>
                {
                    for (int i = 0; i < modStates.Count; i++)
                    {
                        if (modStates[i].TagIds != null)
                            modStates[i].TagIds.RemoveAll(id =>
                                string.Equals(id, tag.Id, StringComparison.OrdinalIgnoreCase));
                    }
                    tags.Remove(tag);
                    if (currentTagId == tag.Id)
                    {
                        currentTagId = tags.FirstOrDefault()?.Id;
                        currentFolderId = FindCurrentTag()?.Folders.FirstOrDefault()?.Id;
                        selectedPackageIds.Clear();
                        primarySelectedPackageId = null;
                        RefreshVisibleMods();
                    }
                    PersistTagData();
                },
                destructive: true));
        }

        private void ToggleTagEnabled(ManagerTagData tag)
        {
            List<ManagerModData> members = new List<ManagerModData>();
            for (int i = 0; i < modStates.Count; i++)
            {
                ManagerModData mod = modStates[i];
                ModMetaData modMeta = FindMod(mod.PackageId);
                if (modMeta == null || IsProtected(modMeta))
                    continue;

                bool matchesTag = tag.IsSystem
                    ? (mod.TagIds == null || mod.TagIds.Count == 0)
                    : (mod.TagIds != null && mod.TagIds.Contains(tag.Id));
                if (matchesTag)
                    members.Add(mod);
            }

            if (members.Count == 0)
                return;

            bool enable = members.Any(mod => !mod.Enabled);
            for (int i = 0; i < members.Count; i++)
                members[i].Enabled = enable;
        }

        private string GetTagStateLabel(ManagerTagData tag)
        {
            if (tag == null)
                return "○";

            int enabled = 0;
            int total = 0;
            for (int i = 0; i < modStates.Count; i++)
            {
                ManagerModData state = modStates[i];
                ModMetaData mod = FindMod(state.PackageId);
                if (mod == null || IsProtected(mod))
                    continue;

                bool matchesTag = tag.IsSystem
                    ? (state.TagIds == null || state.TagIds.Count == 0)
                    : (state.TagIds != null && state.TagIds.Contains(tag.Id));
                if (!matchesTag)
                    continue;

                total++;
                if (state.Enabled)
                    enabled++;
            }
            if (total == 0 || enabled == 0)
                return "○";
            return enabled == total ? "●" : "◐";
        }

        private int GetInstalledCount(ManagerTagData tag)
        {
            if (tag == null)
                return 0;

            int count = 0;
            for (int i = 0; i < modStates.Count; i++)
            {
                ManagerModData modData = modStates[i];
                if (modData == null || FindMod(modData.PackageId) == null)
                    continue;

                bool isTagged = modData.TagIds != null && modData.TagIds.Contains(tag.Id);
                bool isUntagged = tag.IsSystem && (modData.TagIds == null || modData.TagIds.Count == 0);
                if ((tag.IsSystem && isUntagged) || (!tag.IsSystem && isTagged))
                    count++;
            }
            return count;
        }

        private void OpenModContextMenu(ModMetaData mod)
        {
            if (mod == null)
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            List<string> targets = selectedPackageIds.Count > 0
                ? selectedPackageIds.ToList()
                : new List<string> { mod.PackageId };

            if (targets.Count > 0)
            {
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0054"),
                    () => SetSelectedEnabled(true)));
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0052"),
                    () => SetSelectedEnabled(false)));
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0056"),
                    () => ClearSelection()));
                if (!tagMode)
                    options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0077"),
                        () => OpenMoveCategoryMenu()));
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0098"),
                    () =>
                    {
                        if (tagMode)
                            OpenTagFolderMoveMenu();
                        else
                            OpenMoveMenu();
                    }));
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0055"),
                    () => OpenSelectedTagMenu(targets)));
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0053"),
                    () => ClearTagsForSelection(targets)));
            }

            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0105"),
                () => OpenRenameDisplayNameDialog(mod)));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0025"),
                () =>
                {
                    ManagerModData state = FindModState(mod.PackageId);
                    if (state != null)
                        state.DisplayName = null;
                }));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0008"),
                () =>
                {
                    GUIUtility.systemCopyBuffer = mod.PackageId;
                    Messages.Message(ManagerTranslation.Get("LMM_0009") + mod.PackageId,
                        MessageTypeDefOf.TaskCompletion);
                }));
            string workshopUrl = GetWorkshopUrl(mod);
            if (!workshopUrl.NullOrEmpty())
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0014"),
                    () => OpenUrl(workshopUrl)));
            if (mod.RootDir != null)
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0030"),
                    () =>
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(mod.RootDir.FullName)
                            {
                                UseShellExecute = true
                            });
                        }
                        catch (Exception)
                        {
                            Messages.Message(ManagerTranslation.Get("LMM_0034"), MessageTypeDefOf.RejectInput);
                        }
                    }));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0023"),
                () => ConfirmUnsubscribe(new List<string> { mod.PackageId })));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0022"),
                () => ConfirmRemoveFromManager(mod)));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0028"),
                () => ConfirmDeleteModFolder(mod)));
            if (!string.IsNullOrEmpty(mod.Description))
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0060"),
                    () => Messages.Message(mod.Description, MessageTypeDefOf.NeutralEvent)));

            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ConfirmUnsubscribeSelected()
        {
            List<string> targets = selectedPackageIds.Count > 0
                ? selectedPackageIds.ToList()
                : primarySelectedPackageId.NullOrEmpty()
                    ? new List<string>()
                    : new List<string> { primarySelectedPackageId };
            ConfirmUnsubscribe(targets);
        }

        private void ConfirmUnsubscribe(List<string> packageIds)
        {
            List<ModMetaData> workshopMods = (packageIds ?? new List<string>())
                .Select(FindMod)
                .Where(mod => mod != null && GetWorkshopItemId(mod) != 0UL)
                .Distinct()
                .ToList();
            if (workshopMods.Count == 0)
            {
                Messages.Message(ManagerTranslation.Get("LMM_0057"),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                string.Format(ManagerTranslation.Get("LMM_0019"), workshopMods.Count),
                () =>
                {
                    int succeeded = 0;
                    List<string> failures = new List<string>();
                    for (int i = 0; i < workshopMods.Count; i++)
                    {
                        ModMetaData mod = workshopMods[i];
                        try
                        {
                            if (TryUnsubscribeWorkshopItem(GetWorkshopItemId(mod)))
                                succeeded++;
                            else
                                failures.Add(mod.Name);
                        }
                        catch (Exception exception)
                        {
                            Log.Warning("[Lilly Mod Manager] Steam unsubscribe failed for " +
                                mod.PackageId + ": " + exception.Message);
                            failures.Add(mod.Name);
                        }
                    }

                    string message = ManagerTranslation.Get("LMM_0010") + succeeded + ManagerTranslation.Get("LMM_0020");
                    if (failures.Count > 0)
                        message += ManagerTranslation.Get("LMM_0004") + string.Join(", ", failures);
                    Messages.Message(message,
                        failures.Count == 0 ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput);
                }, destructive: true));
        }

        private static ulong GetWorkshopItemId(ModMetaData mod)
        {
            if (mod == null)
                return 0UL;
            foreach (string propertyName in new[]
                { "PublishedFileId", "WorkshopId", "WorkshopFileId", "publishedFileId", "m_PublishedFileId" })
            {
                object value = ReadMetadataValue(mod, propertyName);
                ulong directId;
                if (value != null && ulong.TryParse(value.ToString(), out directId) && directId > 0UL)
                    return directId;
            }
            string url = GetWorkshopUrl(mod);
            if (url.NullOrEmpty())
                return 0UL;
            Match match = Regex.Match(url, @"(?:id=|/)(\d{6,})", RegexOptions.IgnoreCase);
            ulong id;
            return match.Success && ulong.TryParse(match.Groups[1].Value, out id) ? id : 0UL;
        }

        private static bool TryUnsubscribeWorkshopItem(ulong itemId)
        {
            if (itemId == 0UL)
                return false;

            try
            {
                Assembly.Load("com.rlabrecque.steamworks.net");
            }
            catch (Exception)
            {
                // The Steamworks assembly can be absent when the game is launched outside Steam.
            }
            FindSteamUgcType();

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type steamUgc = assemblies[i].GetType("Steamworks.SteamUGC", false);
                if (steamUgc == null)
                    continue;
                MethodInfo[] methods = steamUgc.GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int methodIndex = 0; methodIndex < methods.Length; methodIndex++)
                {
                    MethodInfo method = methods[methodIndex];
                    if (method.Name != "Unsubscribe" && method.Name != "UnsubscribeItem")
                        continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 1)
                        continue;
                    Type idType = parameters[0].ParameterType;
                    object idValue;
                    try
                    {
                        idValue = Activator.CreateInstance(idType, new object[] { itemId });
                    }
                    catch (MissingMethodException)
                    {
                        continue;
                    }
                    method.Invoke(null, new[] { idValue });
                    return true;
                }
            }
            return false;
        }

        private static bool IsSteamSubscribed(ModMetaData mod)
        {
            ulong itemId = GetWorkshopItemId(mod);
            if (itemId == 0UL)
                return false;
            try
            {
                Type steamUgc = FindSteamUgcType();
                if (steamUgc == null)
                    return false;
                MethodInfo[] methods = steamUgc.GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].Name != "GetItemState")
                        continue;
                    ParameterInfo[] parameters = methods[i].GetParameters();
                    if (parameters.Length != 1)
                        continue;
                    object idValue;
                    if (!TryCreateSteamFileId(parameters[0].ParameterType, itemId, out idValue))
                        continue;
                    long state = Convert.ToInt64(methods[i].Invoke(null, new[] { idValue }));
                    return (state & 1L) != 0L;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return false;
        }

        private static Type FindSteamUgcType()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type loadedType = assemblies[i].GetType("Steamworks.SteamUGC", false);
                if (loadedType != null)
                    return loadedType;
            }
            try
            {
                return Assembly.Load("com.rlabrecque.steamworks.net")
                    .GetType("Steamworks.SteamUGC", false);
            }
            catch (Exception)
            {
                try
                {
                    string steamAssemblyPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "com.rlabrecque.steamworks.net.dll");
                    if (File.Exists(steamAssemblyPath))
                        return Assembly.LoadFrom(steamAssemblyPath)
                            .GetType("Steamworks.SteamUGC", false);
                }
                catch (Exception)
                {
                }
                return null;
            }
        }

        private static bool TryCreateSteamFileId(Type idType, ulong itemId, out object idValue)
        {
            try
            {
                idValue = Activator.CreateInstance(idType, new object[] { itemId });
                return idValue != null;
            }
            catch (Exception)
            {
                idValue = null;
                return false;
            }
        }

        private void ConfirmRemoveFromManager(ModMetaData mod)
        {
            if (mod == null)
                return;
            ManagerModData state = FindModState(mod.PackageId);
            bool active = (state != null && state.Enabled) || IsProtected(mod) ||
                ModsConfig.ActiveModsInLoadOrder.Any(activeMod => activeMod != null &&
                    string.Equals(activeMod.PackageId, mod.PackageId,
                        StringComparison.OrdinalIgnoreCase));
            if (active)
            {
                Messages.Message(ManagerTranslation.Get("LMM_0119"),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "'" + mod.Name + ManagerTranslation.Get("LMM_0005") +
                ManagerTranslation.Get("LMM_0031"),
                () =>
                {
                    for (int i = 0; i < categories.Count; i++)
                    {
                        ManagerCategoryData category = categories[i];
                        category.ModIds.RemoveAll(id => string.Equals(id, mod.PackageId,
                            StringComparison.OrdinalIgnoreCase));
                        for (int j = 0; j < category.Folders.Count; j++)
                            category.Folders[j].ModIds.RemoveAll(id => string.Equals(id,
                                mod.PackageId, StringComparison.OrdinalIgnoreCase));
                    }
                    for (int i = 0; i < tags.Count; i++)
                    {
                        ManagerTagData tag = tags[i];
                        for (int j = 0; j < tag.Folders.Count; j++)
                            tag.Folders[j].ModIds.RemoveAll(id => string.Equals(id,
                                mod.PackageId, StringComparison.OrdinalIgnoreCase));
                    }
                    modStates.RemoveAll(item => string.Equals(item.PackageId, mod.PackageId,
                        StringComparison.OrdinalIgnoreCase));
                    if (!settings.ExcludedPackageIds.Contains(mod.PackageId))
                        settings.ExcludedPackageIds.Add(mod.PackageId);
                    settings.Mods.RemoveAll(item => item == null ||
                        string.Equals(item.PackageId, mod.PackageId,
                            StringComparison.OrdinalIgnoreCase));
                    selectedPackageIds.Remove(mod.PackageId);
                    primarySelectedPackageId = null;
                    PersistTagData();
                    RefreshVisibleMods();
                }, destructive: true));
        }

        private void ConfirmDeleteModFolder(ModMetaData mod)
        {
            if (mod == null || mod.RootDir == null)
                return;
            if (IsProtected(mod) || ModsConfig.ActiveModsInLoadOrder.Any(activeMod =>
                activeMod != null && string.Equals(activeMod.PackageId, mod.PackageId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                Messages.Message(ManagerTranslation.Get("LMM_0120"),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            string folderPath = Path.GetFullPath(mod.RootDir.FullName);
            bool workshopManaged = GetWorkshopItemId(mod) > 0UL;
            string warning = workshopManaged
                ? ManagerTranslation.Get("LMM_0012")
                : string.Empty;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                warning + ManagerTranslation.Get("LMM_0035") + folderPath,
                () =>
                {
                    try
                    {
                        if (Directory.Exists(folderPath))
                            Directory.Delete(folderPath, true);
                        Messages.Message(ManagerTranslation.Get("LMM_0033"),
                            MessageTypeDefOf.TaskCompletion);
                    }
                    catch (Exception exception)
                    {
                        Log.Error("[Lilly Mod Manager] Could not delete mod folder " +
                            folderPath + ": " + exception);
                        Messages.Message(ManagerTranslation.Get("LMM_0032") + exception.Message,
                            MessageTypeDefOf.RejectInput);
                    }
                }, destructive: true));
        }

        private void OpenRenameDisplayNameDialog(ModMetaData mod)
        {
            if (mod == null)
                return;

            ManagerModData state = FindModState(mod.PackageId);
            if (state == null)
                return;

            Find.WindowStack.Add(new ManagerFolderNameDialog(
                ManagerTranslation.Get("LMM_0105"),
                state.DisplayName ?? GetDisplayName(mod),
                name =>
                {
                    string trimmed = name == null ? string.Empty : name.Trim();
                    state.DisplayName = trimmed.Length == 0 ? null : trimmed;
                    return true;
                }));
        }

        private void MoveSelectionTo(ManagerCategoryData target)
        {
            if (target == null)
                return;

            List<string> movingIds = selectedPackageIds.ToList();
            ManagerFolderData targetFolder = FindFolder(target, "uncategorized");
            if (targetFolder == null)
            {
                targetFolder = new ManagerFolderData("uncategorized", ManagerTranslation.Get("LMM_0040"));
                target.Folders.Insert(0, targetFolder);
            }
            for (int i = 0; i < movingIds.Count; i++)
            {
                string packageId = movingIds[i];
                ModMetaData mod = FindMod(packageId);
                if (mod == null || IsProtected(mod))
                    continue;

                for (int j = 0; j < categories.Count; j++)
                {
                    categories[j].ModIds.RemoveAll(id =>
                        string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase));
                    for (int folderIndex = 0; folderIndex < categories[j].Folders.Count; folderIndex++)
                        categories[j].Folders[folderIndex].ModIds.RemoveAll(id =>
                            string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase));
                }
                target.ModIds.Add(packageId);
                targetFolder.ModIds.Add(packageId);
            }

            selectedPackageIds.Clear();
            primarySelectedPackageId = null;
            RefreshVisibleMods();
        }

        private void MoveSelectionToFolder(
            ManagerCategoryData category,
            ManagerFolderData folder)
        {
            if (category == null || folder == null)
                return;

            folder.Collapsed = false;
            List<string> movingIds = selectedPackageIds.ToList();
            for (int i = 0; i < movingIds.Count; i++)
            {
                string packageId = movingIds[i];
                ModMetaData mod = FindMod(packageId);
                if (mod == null || IsProtected(mod))
                    continue;

                for (int folderIndex = 0; folderIndex < category.Folders.Count; folderIndex++)
                    category.Folders[folderIndex].ModIds.RemoveAll(id =>
                        string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase));
                folder.ModIds.Add(packageId);
            }

            currentFolderId = folder.Id;
            selectedPackageIds.Clear();
            primarySelectedPackageId = null;
            RefreshVisibleMods();
        }

        private void MoveSelectionToTagFolder(
            ManagerTagData tag,
            ManagerFolderData folder)
        {
            if (tag == null || folder == null)
                return;

            folder.Collapsed = false;
            List<string> movingIds = selectedPackageIds.ToList();
            for (int i = 0; i < movingIds.Count; i++)
            {
                string packageId = movingIds[i];
                if (!HasTag(packageId, tag.Id))
                    continue;

                for (int folderIndex = 0; folderIndex < tag.Folders.Count; folderIndex++)
                    tag.Folders[folderIndex].ModIds.RemoveAll(id =>
                        string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase));
                folder.ModIds.Add(packageId);
            }

            currentFolderId = folder.Id;
            selectedPackageIds.Clear();
            primarySelectedPackageId = null;
            RefreshVisibleMods();
            PersistTagData();
        }

        private void OpenCreateFolderDialog(
            ManagerCategoryData category,
            ManagerFolderData insertAfter)
        {
            Find.WindowStack.Add(new ManagerFolderNameDialog(
                ManagerTranslation.Get("LMM_0049"),
                "",
                name =>
                {
                    if (HasFolderName(category, name, null))
                    {
                        Messages.Message(ManagerTranslation.Get("LMM_0017"),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    ManagerFolderData folder = new ManagerFolderData(
                        Guid.NewGuid().ToString("N"), name.Trim());
                    int index = category.Folders.IndexOf(insertAfter);
                    category.Folders.Insert(index < 0 ? category.Folders.Count : index + 1,
                        folder);
                    currentFolderId = folder.Id;
                    return true;
                }));
        }

        private void OpenCreateFolderDialog(
            ManagerTagData tag,
            ManagerFolderData insertAfter)
        {
            Find.WindowStack.Add(new ManagerFolderNameDialog(
                ManagerTranslation.Get("LMM_0049"),
                "",
                name =>
                {
                    if (HasFolderName(tag, name, null))
                    {
                        Messages.Message(ManagerTranslation.Get("LMM_0017"),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    ManagerFolderData folder = new ManagerFolderData(
                        Guid.NewGuid().ToString("N"), name.Trim());
                    int index = tag.Folders.IndexOf(insertAfter);
                    tag.Folders.Insert(index < 0 ? tag.Folders.Count : index + 1, folder);
                    currentFolderId = folder.Id;
                    PersistTagData();
                    return true;
                }));
        }

        private void OpenRenameFolderDialog(
            ManagerCategoryData category,
            ManagerFolderData folder)
        {
            Find.WindowStack.Add(new ManagerFolderNameDialog(
                ManagerTranslation.Get("LMM_0099"),
                folder.Name,
                name =>
                {
                    if (HasFolderName(category, name, folder))
                    {
                        Messages.Message(ManagerTranslation.Get("LMM_0017"),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    folder.Name = name.Trim();
                    return true;
                }));
        }

        private void OpenRenameFolderDialog(ManagerTagData tag, ManagerFolderData folder)
        {
            Find.WindowStack.Add(new ManagerFolderNameDialog(
                ManagerTranslation.Get("LMM_0099"),
                folder.Name,
                name =>
                {
                    if (HasFolderName(tag, name, folder))
                    {
                        Messages.Message(ManagerTranslation.Get("LMM_0017"),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    folder.Name = name.Trim();
                    PersistTagData();
                    return true;
                }));
        }

        private void ConfirmDeleteFolder(
            ManagerCategoryData category,
            ManagerFolderData folder)
        {
            if (folder == null || folder.Id == "uncategorized")
                return;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                ManagerTranslation.Get("LMM_0103"),
                () =>
                {
                    ManagerFolderData uncategorized = FindFolder(category, "uncategorized");
                    if (uncategorized == null)
                    {
                        uncategorized = new ManagerFolderData("uncategorized", ManagerTranslation.Get("LMM_0040"));
                        category.Folders.Insert(0, uncategorized);
                    }

                    for (int i = 0; i < folder.ModIds.Count; i++)
                    {
                        string packageId = folder.ModIds[i];
                        if (!uncategorized.ModIds.Contains(packageId))
                            uncategorized.ModIds.Add(packageId);
                    }
                    category.Folders.Remove(folder);
                    currentFolderId = uncategorized.Id;
                    RefreshVisibleMods();
                },
                destructive: true));
        }

        private void ConfirmDeleteFolder(ManagerTagData tag, ManagerFolderData folder)
        {
            if (folder == null || folder.Id == "uncategorized")
                return;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                ManagerTranslation.Get("LMM_0104"),
                () =>
                {
                    ManagerFolderData unfiled = FindFolder(tag, "uncategorized");
                    if (unfiled == null)
                    {
                        unfiled = new ManagerFolderData("uncategorized", ManagerTranslation.Get("LMM_0040"));
                        tag.Folders.Insert(0, unfiled);
                    }
                    for (int i = 0; i < folder.ModIds.Count; i++)
                    {
                        if (!unfiled.ModIds.Contains(folder.ModIds[i]))
                            unfiled.ModIds.Add(folder.ModIds[i]);
                    }
                    tag.Folders.Remove(folder);
                    currentFolderId = unfiled.Id;
                    RefreshVisibleMods();
                    PersistTagData();
                },
                destructive: true));
        }

        private static bool HasFolderName(
            ManagerCategoryData category,
            string name,
            ManagerFolderData except)
        {
            if (name.NullOrEmpty())
                return true;
            for (int i = 0; i < category.Folders.Count; i++)
            {
                ManagerFolderData folder = category.Folders[i];
                if (folder != except && string.Equals(folder.Name, name.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool HasFolderName(
            ManagerTagData tag,
            string name,
            ManagerFolderData except)
        {
            if (name.NullOrEmpty())
                return true;
            for (int i = 0; i < tag.Folders.Count; i++)
            {
                ManagerFolderData folder = tag.Folders[i];
                if (folder != except && string.Equals(folder.Name, name.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void SortCurrentView()
        {
            if (tagMode)
            {
                ManagerTagData tag = FindCurrentTag();
                if (tag == null)
                    return;
                ManagerFolderData folder = FindFolder(tag, currentFolderId);
                if (folder != null)
                {
                    SortFolderModIds(folder);
                    PersistTagData();
                }
                return;
            }

            ManagerCategoryData category = FindCurrentCategory();
            if (category == null)
                return;
            ManagerFolderData folderCurrent = FindFolder(category, currentFolderId);
            if (folderCurrent != null)
                SortFolderModIds(folderCurrent);
        }

        private void SortFolderModIds(ManagerFolderData folder)
        {
            if (folder == null || folder.ModIds == null || folder.ModIds.Count <= 1)
                return;

            folder.ModIds = OrderByDependencies(folder.ModIds);
            RefreshVisibleMods();
        }

        private void SortAllCategories()
        {
            for (int categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
            {
                ManagerCategoryData category = categories[categoryIndex];
                if (category == null || category.Folders == null)
                    continue;
                for (int folderIndex = 0; folderIndex < category.Folders.Count; folderIndex++)
                    SortFolderModIds(category.Folders[folderIndex]);
            }
            Messages.Message(ManagerTranslation.Get("LMM_0029"),
                MessageTypeDefOf.TaskCompletion);
        }

        private List<string> OrderByDependencies(IList<string> source)
        {
            List<string> original = source == null ? new List<string>() : source.ToList();
            Dictionary<string, int> order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < original.Count; i++)
                if (!original[i].NullOrEmpty() && !order.ContainsKey(original[i]))
                    order.Add(original[i], i);

            Dictionary<string, HashSet<string>> requirements =
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string packageId in order.Keys)
                requirements[packageId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string packageId in order.Keys)
            {
                ModMetaData mod = FindMod(packageId);
                if (mod == null)
                    continue;
                AddDependencyIds(mod, new[] { "Dependencies", "RequiredMods", "LoadAfter" },
                    id => { if (requirements.ContainsKey(id)) requirements[packageId].Add(id); });
                AddDependencyIds(mod, new[] { "LoadBefore" }, id =>
                {
                    if (requirements.ContainsKey(id))
                        requirements[id].Add(packageId);
                });
            }

            List<string> result = new List<string>(original.Count);
            HashSet<string> emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (result.Count < order.Count)
            {
                string next = null;
                foreach (string candidate in original)
                {
                    if (candidate.NullOrEmpty() || emitted.Contains(candidate) || !order.ContainsKey(candidate))
                        continue;
                    if (requirements[candidate].All(emitted.Contains))
                    {
                        next = candidate;
                        break;
                    }
                }
                if (next == null)
                {
                    foreach (string candidate in original)
                        if (!candidate.NullOrEmpty() && !emitted.Contains(candidate) && order.ContainsKey(candidate))
                            result.Add(candidate);
                    break;
                }
                result.Add(next);
                emitted.Add(next);
            }

            return result;
        }

        private static void AddDependencyIds(
            ModMetaData mod,
            IEnumerable<string> propertyNames,
            Action<string> add)
        {
            foreach (string propertyName in propertyNames)
            {
                object value = ReadMetadataValue(mod, propertyName);
                if (value == null)
                    continue;
                if (value is IEnumerable enumerable && !(value is string))
                {
                    foreach (object item in enumerable)
                    {
                        string id = ReadDependencyPackageId(item);
                        if (!id.NullOrEmpty())
                            add(id);
                    }
                }
                else
                {
                    string id = ReadDependencyPackageId(value);
                    if (!id.NullOrEmpty())
                        add(id);
                }
            }
        }

        private static string ReadDependencyPackageId(object dependency)
        {
            if (dependency == null)
                return null;
            if (dependency is string)
                return (string)dependency;
            foreach (string propertyName in new[] { "PackageId", "packageId", "PackageIdPlayerFacing", "packageIdPlayerFacing" })
            {
                PropertyInfo property = dependency.GetType().GetProperty(propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    object value = property.GetValue(dependency, null);
                    if (value != null && !value.ToString().NullOrEmpty())
                        return value.ToString();
                }
                FieldInfo field = dependency.GetType().GetField(propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    object value = field.GetValue(dependency);
                    if (value != null && !value.ToString().NullOrEmpty())
                        return value.ToString();
                }
            }
            return dependency.ToString();
        }

        private void ApplyChanges()
        {
            List<string> activePackageIds = new List<string>();
            for (int categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
            {
                ManagerCategoryData category = categories[categoryIndex];
                if (category == null || category.Folders == null)
                    continue;

                for (int folderIndex = 0; folderIndex < category.Folders.Count; folderIndex++)
                {
                    ManagerFolderData folder = category.Folders[folderIndex];
                    if (folder == null || folder.ModIds == null)
                        continue;

                    for (int modIndex = 0; modIndex < folder.ModIds.Count; modIndex++)
                    {
                        string packageId = folder.ModIds[modIndex];
                        ModMetaData mod = FindMod(packageId);
                        if (mod == null)
                            continue;

                        ManagerModData state = FindModState(packageId);
                        if (IsProtected(mod) || (category.Enabled && state != null && state.Enabled))
                            activePackageIds.Add(mod.PackageId);
                    }
                }
            }

            activePackageIds = OrderByDependencies(activePackageIds);

            List<string> previouslyActive = ModsConfig.ActiveModsInLoadOrder
                .Where(mod => mod != null && !mod.PackageId.NullOrEmpty())
                .Select(mod => mod.PackageId).ToList();
            bool hasOrderingIssues = false;
            try
            {
                ModsConfig.SetActiveToList(activePackageIds);
                hasOrderingIssues = ModsConfig.ActiveModsInLoadOrder.Any(mod =>
                    mod != null && ModsConfig.ModHasAnyOrderingIssues(mod));
            }
            finally
            {
                ModsConfig.SetActiveToList(previouslyActive);
            }

            if (hasOrderingIssues)
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    ManagerTranslation.Get("LMM_0027"),
                    () => CommitAppliedChanges(activePackageIds), destructive: false));
                return;
            }

            CommitAppliedChanges(activePackageIds);
        }

        private void CommitAppliedChanges(List<string> activePackageIds)
        {
            ModsConfig.SetActiveToList(activePackageIds);
            ModsConfig.Save();
            settings.Categories = categories.Select(category => category.Copy()).ToList();
            settings.Tags = tags.Select(tag => tag.Copy()).ToList();
            settings.Mods = modStates.Select(mod => mod.Copy()).ToList();
            IntegratedModManagerMod.Instance.WriteSettings();
            Close();

            Find.WindowStack.Add(new Dialog_MessageBox(
                ManagerTranslation.Get("LMM_0026"),
                ManagerTranslation.Get("LMM_0118"), null,
                ManagerTranslation.Get("LMM_0068"), () => GenCommandLine.Restart(),
                null, false, null, null, WindowLayer.Dialog));
        }

        private void OpenSavePresetDialog()
        {
            Find.WindowStack.Add(new ManagerFolderNameDialog(
                ManagerTranslation.Get("LMM_0117"),
                "",
                name =>
                {
                    if (presets.Any(preset => string.Equals(preset.Name, name.Trim(),
                        StringComparison.OrdinalIgnoreCase)))
                    {
                        Messages.Message(ManagerTranslation.Get("LMM_0018"),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    ManagerPresetData preset = new ManagerPresetData(
                        Guid.NewGuid().ToString("N"),
                        name.Trim(),
                        categories,
                        tags,
                        modStates);
                    presets.Add(preset);
                    SavePresetsToSettings();
                    Messages.Message(ManagerTranslation.Get("LMM_0108") + preset.Name,
                        MessageTypeDefOf.PositiveEvent);
                    return true;
                }));
        }

        private void OpenPresetMenu()
        {
            if (presets.Count == 0)
            {
                Messages.Message(ManagerTranslation.Get("LMM_0069"), MessageTypeDefOf.RejectInput);
                return;
            }
            Find.WindowStack.Add(new ManagerPresetLoadDialog(
                presets, LoadPreset, ConfirmDeletePreset, ImportVanillaModList));
        }

        private void ImportVanillaModList(IList<string> activePackageIds)
        {
            HashSet<string> active = new HashSet<string>(
                activePackageIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < categories.Count; i++)
                categories[i].Enabled = true;

            for (int i = 0; i < modStates.Count; i++)
            {
                ManagerModData state = modStates[i];
                ModMetaData mod = FindMod(state.PackageId);
                if (mod == null)
                    continue;

                state.Enabled = IsProtected(mod) || active.Contains(mod.PackageId);
            }

            RefreshVisibleMods();
            Messages.Message(ManagerTranslation.Get("LMM_0133"),
                MessageTypeDefOf.PositiveEvent);
        }

        private void LoadPreset(ManagerPresetData preset)
        {
            if (preset == null)
                return;

            categories.Clear();
            categories.AddRange(preset.Categories.Select(category => category.Copy()));
            tags.Clear();
            tags.AddRange(preset.Tags.Select(tag => tag.Copy()));
            modStates.Clear();
            modStates.AddRange(preset.Mods.Select(mod => mod.Copy()));
            AddInstalledModsMissingFromPreset();

            currentCategoryId = categories.FirstOrDefault()?.Id;
            currentTagId = tags.FirstOrDefault()?.Id;
            EnsureCurrentSelectionValid();
            selectedPackageIds.Clear();
            primarySelectedPackageId = null;
            lastSelectedIndex = -1;
            modListScroll = Vector2.zero;
            RefreshVisibleMods();
            PersistTagData();
            Messages.Message(
                ManagerTranslation.Get("LMM_0109") + preset.Name,
                MessageTypeDefOf.PositiveEvent);
        }

        private void AddInstalledModsMissingFromPreset()
        {
            HashSet<string> excluded = new HashSet<string>(settings.ExcludedPackageIds,
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < categories.Count; i++)
            {
                if (categories[i] == null)
                    continue;
                if (categories[i].ModIds != null)
                    categories[i].ModIds.RemoveAll(excluded.Contains);
                if (categories[i].Folders != null)
                    for (int j = 0; j < categories[i].Folders.Count; j++)
                        if (categories[i].Folders[j] != null && categories[i].Folders[j].ModIds != null)
                            categories[i].Folders[j].ModIds.RemoveAll(excluded.Contains);
            }
            for (int i = 0; i < tags.Count; i++)
                if (tags[i] != null && tags[i].Folders != null)
                    for (int j = 0; j < tags[i].Folders.Count; j++)
                        if (tags[i].Folders[j] != null && tags[i].Folders[j].ModIds != null)
                            tags[i].Folders[j].ModIds.RemoveAll(excluded.Contains);
            modStates.RemoveAll(state => state == null || excluded.Contains(state.PackageId));

            ManagerCategoryData normalCategory = categories.FirstOrDefault(category =>
                string.Equals(category.Id, "normal", StringComparison.OrdinalIgnoreCase));
            if (normalCategory == null)
            {
                normalCategory = new ManagerCategoryData("normal", ManagerTranslation.Get("LMM_0065"));
                categories.Add(normalCategory);
            }

            ManagerFolderData unfiled = FindFolder(normalCategory, "uncategorized");
            if (unfiled == null)
            {
                unfiled = new ManagerFolderData("uncategorized", ManagerTranslation.Get("LMM_0040"));
                normalCategory.Folders.Insert(0, unfiled);
            }

            foreach (ModMetaData mod in modsByPackageId.Values)
            {
                if (excluded.Contains(mod.PackageId))
                    continue;
                if (FindModState(mod.PackageId) == null)
                    modStates.Add(new ManagerModData(mod.PackageId, mod.Active));

                if (categories.Any(category => category.ModIds != null &&
                    category.ModIds.Any(packageId => string.Equals(
                        packageId, mod.PackageId, StringComparison.OrdinalIgnoreCase))))
                    continue;

                ManagerCategoryData destination = mod.IsCoreMod || mod.Official
                    ? categories.FirstOrDefault(category =>
                        string.Equals(category.Id, "core", StringComparison.OrdinalIgnoreCase))
                    : normalCategory;
                if (destination == null)
                    destination = normalCategory;
                ManagerFolderData destinationFolder =
                    FindFolder(destination, "uncategorized");
                if (destinationFolder == null)
                {
                    destinationFolder = new ManagerFolderData("uncategorized", ManagerTranslation.Get("LMM_0040"));
                    destination.Folders.Insert(0, destinationFolder);
                }
                destination.ModIds.Add(mod.PackageId);
                destinationFolder.ModIds.Add(mod.PackageId);
            }
        }

        private void ConfirmDeletePreset(ManagerPresetData preset)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                ManagerTranslation.Get("LMM_0106") + preset.Name + ManagerTranslation.Get("LMM_0006"),
                () =>
                {
                    presets.Remove(preset);
                    SavePresetsToSettings();
                    Messages.Message(ManagerTranslation.Get("LMM_0107") + preset.Name,
                        MessageTypeDefOf.PositiveEvent);
                },
                destructive: true));
        }

        private void SavePresetsToSettings()
        {
            settings.Presets = presets.Select(preset => preset.Copy()).ToList();
            IntegratedModManagerMod.Instance.WriteSettings();
        }

        private void PersistTagData()
        {
            settings.Tags = tags.Select(tag => tag.Copy()).ToList();

            if (settings.Mods == null)
                settings.Mods = new List<ManagerModData>();

            for (int i = 0; i < modStates.Count; i++)
            {
                ManagerModData workingState = modStates[i];
                if (workingState == null || workingState.PackageId.NullOrEmpty())
                    continue;

                ManagerModData savedState = settings.FindModData(workingState.PackageId);
                if (savedState == null)
                {
                    savedState = new ManagerModData(
                        workingState.PackageId, workingState.Enabled);
                    settings.Mods.Add(savedState);
                }

                savedState.TagIds = workingState.TagIds == null
                    ? new List<string>()
                    : new List<string>(workingState.TagIds);
            }

            IntegratedModManagerMod.Instance.WriteSettings();
        }

        private void RefreshVisibleMods()
        {
            EnsureCurrentSelectionValid();
            visibleMods.Clear();
            if (tagMode)
            {
                ManagerTagData tag = FindCurrentTag();
                if (tag == null)
                    return;
                for (int folderIndex = 0; folderIndex < tag.Folders.Count; folderIndex++)
                {
                    ManagerFolderData folder = tag.Folders[folderIndex];
                    if (folder.Collapsed)
                        continue;
                    List<string> packageIds = GetTagFolderModIds(tag, folder);
                    for (int modIndex = 0; modIndex < packageIds.Count; modIndex++)
                    {
                        ModMetaData mod = FindMod(packageIds[modIndex]);
                        if (mod != null && MatchesSearch(mod))
                            visibleMods.Add(mod);
                    }
                }
            }
            else
            {
                ManagerCategoryData category = FindCurrentCategory();
                if (category == null)
                    return;

                for (int folderIndex = 0; folderIndex < category.Folders.Count; folderIndex++)
                {
                    ManagerFolderData folder = category.Folders[folderIndex];
                    if (folder.Collapsed)
                        continue;
                    for (int modIndex = 0; modIndex < folder.ModIds.Count; modIndex++)
                    {
                        ModMetaData mod = FindMod(folder.ModIds[modIndex]);
                        if (mod != null && MatchesSearch(mod))
                            visibleMods.Add(mod);
                    }
                }
            }

            selectedPackageIds.RemoveWhere(packageId =>
                !visibleMods.Any(mod =>
                    string.Equals(mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase)));
            if (!selectedPackageIds.Contains(primarySelectedPackageId))
                primarySelectedPackageId = selectedPackageIds.FirstOrDefault();
            if (primarySelectedPackageId != null && !visibleMods.Any(mod =>
                    string.Equals(mod.PackageId, primarySelectedPackageId, StringComparison.OrdinalIgnoreCase)))
                primarySelectedPackageId = null;
        }

        private void EnsureCurrentSelectionValid()
        {
            if (tagMode)
            {
                ManagerTagData currentTag = FindCurrentTag();
                if (currentTag == null)
                {
                    currentTagId = tags.FirstOrDefault()?.Id;
                    currentTag = FindCurrentTag();
                }

                if (currentTag != null)
                {
                    if (currentFolderId.NullOrEmpty() || FindFolder(currentTag, currentFolderId) == null)
                        currentFolderId = currentTag.Folders.FirstOrDefault()?.Id;
                    return;
                }

                currentFolderId = null;
                return;
            }

            ManagerCategoryData currentCategory = FindCurrentCategory();
            if (currentCategory == null)
            {
                currentCategoryId = categories.FirstOrDefault()?.Id;
                currentCategory = FindCurrentCategory();
            }

            if (currentCategory != null)
            {
                if (currentFolderId.NullOrEmpty() || FindFolder(currentCategory, currentFolderId) == null)
                    currentFolderId = currentCategory.Folders.FirstOrDefault()?.Id;
                return;
            }

            currentFolderId = null;
        }

        private int GetInstalledCount(ManagerCategoryData category)
        {
            int count = 0;
            for (int i = 0; i < category.ModIds.Count; i++)
            {
                if (modsByPackageId.ContainsKey(category.ModIds[i]))
                    count++;
            }
            return count;
        }

        private int GetInstalledCount(ManagerFolderData folder)
        {
            int count = 0;
            for (int i = 0; i < folder.ModIds.Count; i++)
            {
                if (modsByPackageId.ContainsKey(folder.ModIds[i]))
                    count++;
            }
            return count;
        }

        private int GetInstalledCount(ManagerTagData tag, ManagerFolderData folder)
        {
            return GetTagFolderModIds(tag, folder)
                .Count(packageId => FindMod(packageId) != null);
        }

        private List<string> GetTagFolderModIds(
            ManagerTagData tag,
            ManagerFolderData folder)
        {
            List<string> packageIds = new List<string>();
            if (tag == null || folder == null)
                return packageIds;

            HashSet<string> includedIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < folder.ModIds.Count; i++)
            {
                string packageId = folder.ModIds[i];
                if (HasTag(packageId, tag.Id) && includedIds.Add(packageId))
                    packageIds.Add(packageId);
            }

            if (!tag.IsSystem || !string.Equals(
                folder.Id, "uncategorized", StringComparison.OrdinalIgnoreCase))
                return packageIds;

            HashSet<string> assignedToAnotherFolder =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int folderIndex = 0; folderIndex < tag.Folders.Count; folderIndex++)
            {
                ManagerFolderData otherFolder = tag.Folders[folderIndex];
                if (otherFolder == null || ReferenceEquals(otherFolder, folder))
                    continue;

                for (int modIndex = 0; modIndex < otherFolder.ModIds.Count; modIndex++)
                    assignedToAnotherFolder.Add(otherFolder.ModIds[modIndex]);
            }

            for (int modIndex = 0; modIndex < modStates.Count; modIndex++)
            {
                ManagerModData modData = modStates[modIndex];
                if (modData != null && HasTag(modData.PackageId, tag.Id) &&
                    !assignedToAnotherFolder.Contains(modData.PackageId) &&
                    includedIds.Add(modData.PackageId))
                    packageIds.Add(modData.PackageId);
            }

            return packageIds;
        }

        private float GetFolderListHeight(ManagerCategoryData category)
        {
            float height = 0f;
            for (int i = 0; i < category.Folders.Count; i++)
            {
                ManagerFolderData folder = category.Folders[i];
                height += RowHeight;
                if (folder.Collapsed)
                    continue;
                for (int modIndex = 0; modIndex < folder.ModIds.Count; modIndex++)
                {
                    ModMetaData mod = FindMod(folder.ModIds[modIndex]);
                    if (mod != null && MatchesSearch(mod))
                        height += RowHeight;
                }
            }
            return height;
        }

        private float GetFolderListHeight(ManagerTagData tag)
        {
            float height = 0f;
            for (int i = 0; i < tag.Folders.Count; i++)
            {
                ManagerFolderData folder = tag.Folders[i];
                height += RowHeight;
                if (folder.Collapsed)
                    continue;
                List<string> packageIds = GetTagFolderModIds(tag, folder);
                for (int modIndex = 0; modIndex < packageIds.Count; modIndex++)
                {
                    ModMetaData mod = FindMod(packageIds[modIndex]);
                    if (mod != null && MatchesSearch(mod))
                        height += RowHeight;
                }
            }
            return height;
        }

        private static ManagerFolderData FindFolder(ManagerCategoryData category, string id)
        {
            if (category == null || category.Folders == null)
                return null;
            for (int i = 0; i < category.Folders.Count; i++)
            {
                if (category.Folders[i] != null &&
                    string.Equals(category.Folders[i].Id, id,
                        StringComparison.OrdinalIgnoreCase))
                    return category.Folders[i];
            }
            return null;
        }

        private static ManagerFolderData FindFolder(ManagerTagData tag, string id)
        {
            if (tag == null || tag.Folders == null)
                return null;
            for (int i = 0; i < tag.Folders.Count; i++)
            {
                if (tag.Folders[i] != null &&
                    string.Equals(tag.Folders[i].Id, id,
                        StringComparison.OrdinalIgnoreCase))
                    return tag.Folders[i];
            }
            return null;
        }

        private ManagerCategoryData FindCurrentCategory()
        {
            for (int i = 0; i < categories.Count; i++)
            {
                if (categories[i].Id == currentCategoryId)
                    return categories[i];
            }
            return null;
        }

        private ManagerTagData FindCurrentTag()
        {
            return FindTag(currentTagId);
        }

        private ManagerTagData FindTag(string tagId)
        {
            if (tagId.NullOrEmpty())
                return null;
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i].Id, tagId, StringComparison.OrdinalIgnoreCase))
                    return tags[i];
            }
            return null;
        }

        private bool HasTag(string packageId, string tagId)
        {
            if (tagId == null)
                return false;

            ManagerModData modData = FindModState(packageId);
            if (modData == null)
                return false;

            if (string.Equals(tagId, "untagged", StringComparison.OrdinalIgnoreCase))
                return modData.TagIds == null || modData.TagIds.Count == 0;

            return modData.TagIds != null && modData.TagIds.Contains(tagId);
        }

        private ManagerModData FindModState(string packageId)
        {
            for (int i = 0; i < modStates.Count; i++)
            {
                if (string.Equals(modStates[i].PackageId, packageId,
                    StringComparison.OrdinalIgnoreCase))
                    return modStates[i];
            }
            return null;
        }

        private ModMetaData FindMod(string packageId)
        {
            if (packageId.NullOrEmpty())
                return null;
            ModMetaData mod;
            return modsByPackageId.TryGetValue(packageId, out mod) ? mod : null;
        }

        private static bool IsProtected(ModMetaData mod)
        {
            return mod.IsCoreMod || mod.Official;
        }

        private bool MatchesSearch(ModMetaData mod)
        {
            if (!MatchesFilter(mod))
                return false;

            if (searchText.NullOrEmpty())
                return true;

            string query = searchText.Trim();
            if (query.Length == 0)
                return true;

            ManagerModData state = FindModState(mod.PackageId);
            string customName = state != null ? state.DisplayName : null;
            string displayName = GetDisplayName(mod);

            if (displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (mod.Name != null && mod.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (mod.PackageId != null && mod.PackageId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (mod.AuthorsString != null && mod.AuthorsString.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!customName.NullOrEmpty() && customName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!mod.Description.NullOrEmpty() && mod.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (GetModVersionText(mod).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                GetSupportedGameVersionText(mod).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                GetDependencyText(mod).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (state != null && state.TagIds != null)
            {
                for (int i = 0; i < state.TagIds.Count; i++)
                {
                    ManagerTagData tag = FindTag(state.TagIds[i]);
                    if (tag != null && tag.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            for (int i = 0; i < categories.Count; i++)
            {
                ManagerCategoryData category = categories[i];
                if (category == null || category.ModIds == null)
                    continue;
                if (category.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    category.ModIds.Any(id => string.Equals(id, mod.PackageId, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            return false;
        }

        private bool MatchesFilter(ModMetaData mod)
        {
            ManagerModData state = FindModState(mod.PackageId);
            bool active = state != null && state.Enabled;

            switch (currentModFilter)
            {
                case ModFilter.Active:
                    return active;
                case ModFilter.Inactive:
                    return !active;
                case ModFilter.Subscribed:
                    return IsSteamSubscribed(mod);
                case ModFilter.Untagged:
                    return state == null || state.TagIds == null || state.TagIds.Count == 0;
                case ModFilter.Uncategorized:
                    return IsInUncategorizedFolder(mod.PackageId);
                default:
                    return true;
            }
        }

        private bool IsInUncategorizedFolder(string packageId)
        {
            if (tagMode)
            {
                ManagerFolderData tagUnfiled = FindFolder(FindCurrentTag(), "uncategorized");
                return tagUnfiled != null && GetTagFolderModIds(FindCurrentTag(), tagUnfiled)
                    .Any(id => string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase));
            }

            for (int categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
            {
                ManagerCategoryData category = categories[categoryIndex];
                if (category == null || category.Folders == null)
                    continue;

                ManagerFolderData unfiled = FindFolder(category, "uncategorized");
                if (unfiled != null && unfiled.ModIds.Any(id =>
                    string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            return false;
        }

        private void EndModDrag()
        {
            isDraggingMod = false;
            dragPackageId = null;
            dragSelection.Clear();
        }

        private void HandleDragDropToMod(string targetPackageId)
        {
            if (dragPackageId.NullOrEmpty() ||
                string.Equals(dragPackageId, targetPackageId, StringComparison.OrdinalIgnoreCase))
                return;

            List<ManagerFolderData> folders = tagMode
                ? FindCurrentTag()?.Folders
                : FindCurrentCategory()?.Folders;
            ManagerFolderData targetFolder = folders == null
                ? null
                : folders.FirstOrDefault(folder => folder != null && folder.ModIds != null &&
                    folder.ModIds.Any(id => string.Equals(id, targetPackageId,
                        StringComparison.OrdinalIgnoreCase)));
            if (targetFolder != null && targetFolder.Id != currentFolderId)
            {
                if (tagMode)
                    MoveSelectionToTagFolder(FindCurrentTag(), targetFolder);
                else
                    MoveSelectionToFolder(FindCurrentCategory(), targetFolder);
                EndModDrag();
                return;
            }

            List<string> modIds = GetCurrentFolderModIds();
            if (modIds == null || modIds.Count <= 1)
                return;

            List<string> movingIds = dragSelection.Count > 0
                ? dragSelection.Where(id => !id.NullOrEmpty()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string> { dragPackageId };
            if (movingIds.Count == 0)
                return;

            List<string> sourceList = new List<string>(modIds);
            foreach (string movingId in movingIds)
            {
                int currentIndex = sourceList.IndexOf(movingId);
                if (currentIndex >= 0)
                    sourceList.RemoveAt(currentIndex);
            }

            int targetIndex = sourceList.IndexOf(targetPackageId);
            if (targetIndex < 0)
                targetIndex = sourceList.Count;

            List<string> reordered = new List<string>(sourceList.Count + movingIds.Count);
            for (int i = 0; i < sourceList.Count; i++)
            {
                if (i == targetIndex)
                {
                    reordered.AddRange(movingIds);
                }
                reordered.Add(sourceList[i]);
            }
            if (targetIndex >= sourceList.Count)
                reordered.AddRange(movingIds);

            SetCurrentFolderModIds(reordered);
            selectedPackageIds.Clear();
            for (int i = 0; i < movingIds.Count; i++)
                selectedPackageIds.Add(movingIds[i]);
            primarySelectedPackageId = movingIds.LastOrDefault();
            RefreshVisibleMods();
            isDraggingMod = false;
            dragPackageId = null;
            dragSelection.Clear();
        }

        private List<string> GetCurrentFolderModIds()
        {
            ManagerFolderData folder = tagMode
                ? FindFolder(FindCurrentTag(), currentFolderId)
                : FindFolder(FindCurrentCategory(), currentFolderId);
            return folder == null ? null : folder.ModIds;
        }

        private void SetCurrentFolderModIds(List<string> modIds)
        {
            ManagerFolderData folder = tagMode
                ? FindFolder(FindCurrentTag(), currentFolderId)
                : FindFolder(FindCurrentCategory(), currentFolderId);
            if (folder == null)
                return;
            folder.ModIds = modIds ?? new List<string>();
        }

        private string GetDisplayName(ModMetaData mod)
        {
            if (mod == null)
                return string.Empty;

            ManagerModData state = FindModState(mod.PackageId);
            if (state != null && !state.DisplayName.NullOrEmpty())
                return state.DisplayName;

            return mod.Name.NullOrEmpty() ? mod.FolderName : mod.Name;
        }

        private static string GetModVersionText(ModMetaData mod)
        {
            if (mod == null)
                return string.Empty;

            string[] candidateNames = { "Version", "ModVersion", "CurrentVersion", "WorkshopVersion" };
            foreach (string propertyName in candidateNames)
            {
                object value = ReadMetadataValue(mod, propertyName);
                string text = value == null ? null : value.ToString();
                if (!text.NullOrEmpty())
                    return text;
            }

            return string.Empty;
        }

        private static string GetSupportedGameVersionText(ModMetaData mod)
        {
            if (mod == null)
                return string.Empty;

            string[] candidateNames = { "SupportedVersions", "GameVersion", "VersionString" };
            foreach (string propertyName in candidateNames)
            {
                object value = ReadMetadataValue(mod, propertyName);
                if (value == null)
                    continue;

                if (value is IEnumerable enumerable && !(value is string))
                {
                    List<string> values = new List<string>();
                    foreach (object item in enumerable)
                    {
                        if (item != null)
                            values.Add(item.ToString());
                    }
                    string joined = string.Join(", ", values);
                    if (!joined.NullOrEmpty())
                        return joined;
                }

                string text = value.ToString();
                if (!text.NullOrEmpty())
                    return text;
            }

            return string.Empty;
        }

        private static string GetDependencyText(ModMetaData mod)
        {
            if (mod == null)
                return string.Empty;

            List<string> dependencyNames = new List<string>();
            foreach (string propertyName in new[] { "Dependencies", "LoadBefore", "LoadAfter", "RequiredMods" })
            {
                object value = ReadMetadataValue(mod, propertyName);
                if (value == null)
                    continue;

                if (value is IEnumerable enumerable && !(value is string))
                {
                    foreach (object item in enumerable)
                    {
                        if (item != null && !item.ToString().NullOrEmpty())
                            dependencyNames.Add(item.ToString());
                    }
                    continue;
                }

                string text = value.ToString();
                if (!text.NullOrEmpty())
                    dependencyNames.Add(text);
            }

            if (dependencyNames.Count == 0)
                return string.Empty;

            return string.Join(", ", dependencyNames.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static string GetWorkshopUrl(ModMetaData mod)
        {
            if (mod == null)
                return string.Empty;

            string[] propertyNames =
            {
                "WorkshopUri",
                "WorkshopUrl",
                "WorkshopPage",
                "SourceUrl",
                "Url",
                "SteamWorkshopUrl"
            };

            foreach (string propertyName in propertyNames)
            {
                object value = ReadMetadataValue(mod, propertyName);
                if (value != null)
                {
                    string text = value.ToString();
                    if (!text.NullOrEmpty())
                        return text;
                }
            }

            return string.Empty;
        }

        private static object ReadMetadataValue(object mod, string name)
        {
            if (mod == null || name.NullOrEmpty())
                return null;

            Type type = mod.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
                return property.GetValue(mod, null);

            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field != null ? field.GetValue(mod) : null;
        }

        private static void OpenUrl(string url)
        {
            if (url.NullOrEmpty())
                return;

            try
            {
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                Messages.Message(ManagerTranslation.Get("LMM_0011"), MessageTypeDefOf.RejectInput);
            }
        }
    }
}
