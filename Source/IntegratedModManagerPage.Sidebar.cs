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
    internal sealed partial class IntegratedModManagerPage
    {
        private void DrawCategoryTabs(Rect rect)
                {
                    if (Event.current.type == EventType.MouseDown &&
                        rect.Contains(Event.current.mousePosition))
                    {
                        categoryTabsHaveKeyboardFocus = true;
                        modListHasKeyboardFocus = false;
                    }
        
                    if (tagMode)
                    {
                        DrawTagTabs(rect);
                        return;
                    }
        
                    Widgets.DrawBox(rect);
                    Text.Font = GameFont.Medium;
                    Widgets.Label(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 44f, 28f),
                        ManagerTranslation.Get("LMM_0158"));
                    Text.Font = GameFont.Small;
                    Rect addRect = new Rect(rect.xMax - 34f, rect.y + 2f, 28f, 26f);
                    if (Widgets.ButtonText(addRect, "+"))
                        OpenCreateCategoryDialog(GetSelectedCategoryInsertIndex());
        
                    Rect listRect = new Rect(rect.x + 4f, rect.y + 32f,
                        rect.width - 8f, rect.height - 36f);
                    float contentHeight = Mathf.Max(listRect.height, (categories.Count + 1) * 32f);
                    float scrollbarWidth = contentHeight > listRect.height
                        ? ManagerUiMetrics.VerticalScrollbarWidth : 0f;
                    Rect viewRect = new Rect(0f, 0f, listRect.width - scrollbarWidth, contentHeight);
                    Widgets.BeginScrollView(listRect, ref categoryTabScroll, viewRect);
                    DrawAllModsTab(new Rect(0f, 2f, viewRect.width, 30f), 0);
                    for (int i = 0; i < categories.Count; i++)
                    {
                        ManagerCategoryData category = categories[i];
                        // 오른쪽 5px는 선택 강조 막대 영역으로 남겨 둔다.
                        Rect rowRect = new Rect(0f, (i + 1) * 32f + 2f, Mathf.Max(0f, viewRect.width - 5f), 30f);
        
        // 기존 검정/회색 교차 배경색
        Color baseRowColor = i % 2 == 0
            ? new Color(0.09f, 0.09f, 0.09f, 1f)
            : new Color(0.055f, 0.055f, 0.055f, 1f);
        
        // 사용자 지정색은 그라데이션 없이 단일 배경색으로 표시
        Color categoryRowColor = ManagerTagColors.IsValidHex(category.ColorHex)
            ? ManagerTagColors.Background(category.ColorHex)
            : baseRowColor;
        Widgets.DrawBoxSolid(rowRect, categoryRowColor);
        
                        bool isDragTarget = !dragCategoryId.NullOrEmpty() &&
                            rowRect.Contains(Event.current.mousePosition);
                        if (dragCategoryId == category.Id)
                            Widgets.DrawHighlight(rowRect);
                        if (isDragTarget)
                        {
                            Widgets.DrawLightHighlight(rowRect);
                            Widgets.DrawBox(rowRect);
                        }
                        if (isDraggingMod && rowRect.Contains(Event.current.mousePosition))
                            Widgets.DrawLightHighlight(rowRect);
                        if (!tagMode && !dragFolderInTagMode &&
                            !dragFolderId.NullOrEmpty() &&
                            dragFolderSourceCategoryId != category.Id &&
                            rowRect.Contains(Event.current.mousePosition))
                        {
                            Widgets.DrawLightHighlight(rowRect);
                            Widgets.DrawBox(rowRect, 2);
                        }
                        if (!tagMode && !dragFolderInTagMode &&
                            !dragFolderId.NullOrEmpty() &&
                            Event.current.type == EventType.MouseUp &&
                            rowRect.Contains(Event.current.mousePosition))
                        {
                            MoveFolderToCategory(dragFolderId, dragFolderSourceCategoryId, category);
                            dragFolderId = null;
                            dragFolderSourceCategoryId = null;
                            Event.current.Use();
                        }
                        if (isDraggingMod && Event.current.type == EventType.MouseUp &&
                            rowRect.Contains(Event.current.mousePosition))
                        {
                            MoveSelectionTo(category);
                            EndModDrag();
                            Event.current.Use();
                        }
                        Rect enabledRect = new Rect(rowRect.x + 5f, rowRect.y + 3f, 22f, 22f);
                        bool enabled = category.Enabled;
                        Widgets.Checkbox(enabledRect.position, ref enabled);
                        if (enabled != category.Enabled)
                        {
                            category.Enabled = enabled;
                            PersistCategoryData();
                            RefreshVisibleMods();
                        }
        
                        Rect editRect = new Rect(rowRect.xMax - 52f, rowRect.y + 2f, 22f, 24f);
                        Rect deleteRect = new Rect(rowRect.xMax - 27f, rowRect.y + 2f, 22f, 24f);
                        Rect labelRect = new Rect(enabledRect.xMax + 4f, rowRect.y,
                            Mathf.Max(24f, editRect.x - enabledRect.xMax - 7f), 26f);
                        string label = "(" + GetActiveCount(category) + "/" +
            GetInstalledCount(category) + ") " + category.Name;
        
        
        Color previousContentColor = GUI.contentColor;
        GUI.contentColor = GetReadableTextColor(categoryRowColor);
        
        // 선후행 오류가 있으면 배경과 관계없이 오류 표시 색상을 유지
        if (HasCategoryLoadOrderIssue(category))
            GUI.contentColor = new Color(1f, 0.28f, 0.28f, 1f);
        
        Widgets.Label(labelRect, label);
        GUI.contentColor = previousContentColor;
        
        
                        TooltipHandler.TipRegion(labelRect, label);
                        HandleCategoryTabReorder(category, labelRect);
                        previousContentColor = GUI.contentColor;
                        GUI.contentColor = Color.white;
                        if (Widgets.ButtonText(editRect, "E"))
                            OpenRenameCategoryDialog(category);
                        TooltipHandler.TipRegion(editRect, ManagerTranslation.Get("LMM_0125"));
                        if (Widgets.ButtonText(deleteRect, "×"))
                            ConfirmDeleteCategory(category);
                        GUI.contentColor = previousContentColor;
                        TooltipHandler.TipRegion(deleteRect, ManagerTranslation.Get("LMM_0045"));
        
        // 텍스트 검색 또는 체크 필터가 활성화된 경우,
        // 조건에 맞는 모드가 하나도 없는 카테고리를 회색 처리
        if ((!searchText.NullOrEmpty() || HasActiveModFilters()) &&
            !category.ModIds.Any(packageId =>
            {
                ModMetaData mod = FindMod(packageId);
                return mod != null && MatchesSearch(mod);
            }))
        {
            Widgets.DrawBoxSolid(
                rowRect,
                new Color(0.45f, 0.45f, 0.45f, 0.55f));
        }
        
        // 검색 결과 음영보다 위에 선택 테두리를 그려 선택 상태를 유지
        if (!allModsView && category.Id == currentCategoryId)
        {
            // 선택 테두리 바깥쪽의 별도 여백에 표시해 배경색과 겹치지 않게 한다.
            Widgets.DrawBox(rowRect, 2);
            Widgets.DrawBoxSolid(
                new Rect(rowRect.xMax + 1f, rowRect.y + 2.5f, 4f, 25f),
                new Color(0f, 0.9f, 1f, 1f));
        }
        if (!allModsView && categoryTabsHaveKeyboardFocus && category.Id == currentCategoryId)
        {
            Color previousFocusColor = GUI.color;
            GUI.color = Color.white;
            Widgets.DrawBox(rowRect, 2);
            GUI.color = previousFocusColor;
        }
        
                    }
                    Widgets.EndScrollView();
                }
        
        private float GetCategoryTabSlotWidth(ManagerCategoryData category)
                {
                    if (category == null)
                        return 163f;
        
                    string label = "(" + GetInstalledCount(category) + ") " + category.Name;
                    float titleWidth = Text.CalcSize(label).x;
                    float requiredCardWidth = Mathf.Max(157f, titleWidth + 63f);
                    return requiredCardWidth + 6f;
                }
        
        private bool HasCategoryLoadOrderIssue(ManagerCategoryData category)
                {
                    if (category == null || !category.Enabled || category.Folders == null)
                        return false;
                    for (int folderIndex = 0; folderIndex < category.Folders.Count; folderIndex++)
                    {
                        ManagerFolderData folder = category.Folders[folderIndex];
                        if (folder != null && folder.ModIds != null &&
                            folder.ModIds.Any(loadOrderIssuePackageIds.Contains))
                            return true;
                    }
                    return false;
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
                    PersistCategoryData();
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
                        options.Add(new FloatMenuOption(categories[targetIndex].Name,
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
                    PersistCategoryData();
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
                            PersistCategoryData();
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
                category.ColorHex,
                (name, colorHex) =>
                {
                    string trimmed = name == null
                        ? string.Empty
                        : name.Trim();
        
                    if (trimmed.NullOrEmpty())
                        return false;
        
                    if (categories.Any(existing =>
                        existing != category &&
                        string.Equals(
                            existing.Name,
                            trimmed,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        Messages.Message(
                            ManagerTranslation.Get("LMM_0015"),
                            MessageTypeDefOf.RejectInput);
        
                        return false;
                    }
        
                    // 이름 변경
                    category.Name = trimmed;
        
                    // 색상 변경
                    category.ColorHex = colorHex;
        
                    // 영구 저장
                    PersistCategoryData();
        
                    return true;
                }));
        }
        
        private void ConfirmDeleteCategory(ManagerCategoryData category)
                {
                    if (category == null)
                        return;
        
                    // 저장 데이터에 남아 있는 미설치 모드 ID는 실제 모드로 취급하지 않는다.
                    // 다만 폴더 안에 설치된 모드가 있다면 카테고리 삭제를 막아 데이터 유실을 방지한다.
                    bool hasInstalledMods =
                        (category.ModIds != null &&
                            category.ModIds.Any(packageId => FindMod(packageId) != null)) ||
                        (category.Folders != null &&
                            category.Folders.Any(folder => folder != null &&
                                folder.ModIds != null &&
                                folder.ModIds.Any(packageId => FindMod(packageId) != null)));
        
                    if (hasInstalledMods)
                    {
                        Messages.Message(ManagerTranslation.Get("LMM_0036"),
                            MessageTypeDefOf.RejectInput);
                        return;
                    }
        
                    // 미설치/삭제된 모드의 오래된 ID를 정리한 뒤 빈 카테고리를 삭제한다.
                    if (category.ModIds != null)
                        category.ModIds.RemoveAll(packageId => FindMod(packageId) == null);
        
                    if (category.Folders != null)
                    {
                        foreach (ManagerFolderData folder in category.Folders)
                        {
                            if (folder != null && folder.ModIds != null)
                                folder.ModIds.RemoveAll(packageId => FindMod(packageId) == null);
                        }
                    }
        
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        ManagerTranslation.Get("LMM_0076") + category.Name + ManagerTranslation.Get("LMM_0006"),
                        () =>
                        {
                            categories.Remove(category);
                            PersistCategoryData();
                            if (string.Equals(currentCategoryId, category.Id, StringComparison.OrdinalIgnoreCase))
                            {
                                currentCategoryId = categories.FirstOrDefault()?.Id;
                                currentFolderId = FindCurrentCategory()?.Folders.FirstOrDefault()?.Id;
                            }
                            RefreshVisibleMods();
                        },
                        destructive: true));
                }
        
        private void DrawAllModsTab(Rect rowRect, int rowIndex)
                {
                    Color baseRowColor = rowIndex % 2 == 0
                        ? new Color(0.09f, 0.09f, 0.09f, 1f)
                        : new Color(0.055f, 0.055f, 0.055f, 1f);
                    Widgets.DrawBoxSolid(rowRect, baseRowColor);
        
                    if (allModsView)
                    {
                        Rect selectedRect = new Rect(rowRect.x, rowRect.y,
                            Mathf.Max(0f, rowRect.width * 2f / 3f - 2f), rowRect.height);
                        Widgets.DrawBoxSolid(selectedRect, new Color(0.18f, 0.30f, 0.48f, 0.85f));
                        Widgets.DrawBox(selectedRect, 2);
                    }
                    if (categoryTabsHaveKeyboardFocus && allModsView)
                        Widgets.DrawBox(rowRect, 2);
        
                    string label = "전체 목록 (" + GetAllInstalledModCount() + ")";
                    Rect labelRect = new Rect(rowRect.x + 10f, rowRect.y + 2f, rowRect.width - 20f, rowRect.height - 4f);
                    Widgets.Label(labelRect, label);
                    TooltipHandler.TipRegion(rowRect, "카테고리와 태그 구분 없이 설치된 모든 모드 보기");
        
                    if (Widgets.ButtonInvisible(rowRect))
                        SelectAllModsView();
                }
        
        private int GetAllInstalledModCount()
                {
                    return ModLister.AllInstalledMods
                        .Where(mod => mod != null && !mod.PackageId.NullOrEmpty())
                        .Select(mod => mod.PackageId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();
                }
        
        private void SelectAllModsView()
                {
                    allModsView = true;
                    selectedPackageIds.Clear();
                    primarySelectedPackageId = null;
                    lastSelectedIndex = -1;
                    modListScroll = Vector2.zero;
                    categoryTabsHaveKeyboardFocus = true;
                    modListHasKeyboardFocus = false;
                    RefreshVisibleMods();
                }
        
        private void DrawTagTabs(Rect rect)
                {
                    if (Event.current.type == EventType.MouseDown &&
                        rect.Contains(Event.current.mousePosition))
                    {
                        categoryTabsHaveKeyboardFocus = true;
                        modListHasKeyboardFocus = false;
                    }
        
                    Widgets.DrawBox(rect);
                    Text.Font = GameFont.Medium;
                    Widgets.Label(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 44f, 28f),
                        ManagerTranslation.Get("LMM_0159"));
                    Text.Font = GameFont.Small;
                    Rect addRect = new Rect(rect.xMax - 34f, rect.y + 2f, 28f, 26f);
                    if (Widgets.ButtonText(addRect, "+"))
                        OpenCreateTagDialog(insertIndex: GetSelectedTagInsertIndex());
        
                    Rect listRect = new Rect(rect.x + 4f, rect.y + 32f,
                        rect.width - 8f, rect.height - 36f);
                    float contentHeight = Mathf.Max(listRect.height, (tags.Count + 1) * 32f);
                    float scrollbarWidth = contentHeight > listRect.height
                        ? ManagerUiMetrics.VerticalScrollbarWidth : 0f;
                    Rect viewRect = new Rect(0f, 0f, listRect.width - scrollbarWidth, contentHeight);
                    Widgets.BeginScrollView(listRect, ref tagTabScroll, viewRect);
                    DrawAllModsTab(new Rect(0f, 2f, viewRect.width, 30f), 0);
                    int rowIndex = 1;
                    for (int i = 0; i < tags.Count; i++)
                    {
                        ManagerTagData tag = tags[i];
                        if (tag == null)
                            continue;
                        // 오른쪽 5px는 선택 강조 막대 영역으로 남겨 둔다.
                        Rect rowRect = new Rect(0f, rowIndex * 32f + 2f, Mathf.Max(0f, viewRect.width - 5f), 30f);
        
        
        // 기존 검정/회색 교차 배경색
        Color baseRowColor = rowIndex % 2 == 0
            ? new Color(0.09f, 0.09f, 0.09f, 1f)
            : new Color(0.055f, 0.055f, 0.055f, 1f);
        
        // 사용자 지정색은 그라데이션 없이 단일 배경색으로 표시
        Color tagRowColor = ManagerTagColors.IsValidHex(tag.ColorHex)
            ? ManagerTagColors.Background(tag.ColorHex)
            : baseRowColor;
        Widgets.DrawBoxSolid(rowRect, tagRowColor);
        
                        bool isDragTarget = !dragTagId.NullOrEmpty() && !tag.IsSystem &&
                            rowRect.Contains(Event.current.mousePosition);
                        if (dragTagId == tag.Id)
                            Widgets.DrawHighlight(rowRect);
                        if (isDragTarget)
                        {
                            Widgets.DrawLightHighlight(rowRect);
                            Widgets.DrawBox(rowRect);
                        }
                        if (isDraggingMod && rowRect.Contains(Event.current.mousePosition))
                            Widgets.DrawLightHighlight(rowRect);
                        if (isDraggingMod && Event.current.type == EventType.MouseUp &&
                            rowRect.Contains(Event.current.mousePosition))
                        {
                            if (tag.IsSystem)
                                ClearTagsForSelection(dragSelection);
                            else
                                SetTagForSelection(dragSelection, tag, true);
                            EndModDrag();
                            Event.current.Use();
                        }
                        Rect stateRect = new Rect(rowRect.x + 5f, rowRect.y + 3f, 26f, 22f);
                        Color previousTagContentColor = GUI.contentColor;
                        // 왼쪽 상태 체크박스 글자는 배경색과 관계없이 흰색으로 고정한다.
                        GUI.contentColor = Color.white;
                        DrawTagEnabledCheckbox(stateRect, tag);
                        GUI.contentColor = previousTagContentColor;
        
                        Rect editRect = new Rect(rowRect.xMax - 52f, rowRect.y + 2f, 22f, 24f);
                        Rect deleteRect = new Rect(rowRect.xMax - 27f, rowRect.y + 2f, 22f, 24f);
                        if (tag.IsSystem)
                        {
                            editRect = new Rect(rowRect.xMax - 27f, rowRect.y + 2f, 22f, 24f);
                            deleteRect = new Rect(rowRect.xMax + 2f, rowRect.y + 2f, 0f, 24f);
                        }
                        Rect labelRect = new Rect(stateRect.xMax + 4f, rowRect.y,
                            Mathf.Max(24f, editRect.x - stateRect.xMax - 7f), 26f);
                        string label = "(" + GetActiveCount(tag) + "/" +
            GetInstalledCount(tag) + ") " + tag.Name;
        
        // 태그 배경의 상대 휘도에 따라 글자색을 검정 또는 흰색으로 자동 선택한다.
        GUI.contentColor = ManagerTagColors.IsValidHex(tag.ColorHex)
            ? ManagerTagColors.Foreground(tag.ColorHex)
            : Color.white;
        Widgets.Label(labelRect, label);
        GUI.contentColor = previousTagContentColor;
        
                        TooltipHandler.TipRegion(labelRect, label);
        
                        if (tag.IsSystem)
                        {
                            if (Widgets.ButtonInvisible(labelRect))
                                SelectTag(tag);
                        }
                        else
                        {
                            HandleTagTabReorder(tag, labelRect);
                            previousTagContentColor = GUI.contentColor;
                            GUI.contentColor = Color.white;
                            if (Widgets.ButtonText(editRect, "E"))
                                OpenRenameTagDialog(tag);
                            TooltipHandler.TipRegion(editRect, ManagerTranslation.Get("LMM_0126"));
                            if (Widgets.ButtonText(deleteRect, "×"))
                                ConfirmDeleteTag(tag);
                            GUI.contentColor = previousTagContentColor;
                            TooltipHandler.TipRegion(deleteRect, ManagerTranslation.Get("LMM_0045"));
                        }
                        rowIndex++;
        
        // 검색 결과가 없는 태그 회색 처리
        if (!searchText.NullOrEmpty() &&
            !MatchesTagSearch(tag))
        {
            Widgets.DrawBoxSolid(
                rowRect,
                new Color(0.45f, 0.45f, 0.45f, 0.55f));
        }
        
        // 검색 결과 음영보다 위에 선택 테두리를 그려 선택 상태를 유지
        if (!allModsView && tag.Id == currentTagId)
        {
            // 선택 테두리 바깥쪽의 별도 여백에 표시해 배경색과 겹치지 않게 한다.
            Widgets.DrawBox(rowRect, 2);
            Widgets.DrawBoxSolid(
                new Rect(rowRect.xMax + 1f, rowRect.y + 2f, 4f, rowRect.height - 4f),
                new Color(0f, 0.9f, 1f, 1f));
        }
        
                    }
                    Widgets.EndScrollView();
                }
        
        private int GetSelectedCategoryInsertIndex()
                {
                    int index = categories.FindIndex(category => category.Id == currentCategoryId);
                    return index < 0 ? categories.Count : index + 1;
                }
        
        private int GetSelectedTagInsertIndex()
                {
                    int index = tags.FindIndex(tag => tag.Id == currentTagId);
                    return index < 0 ? tags.Count : index + 1;
                }
        
        private float GetSystemTagTabSlotWidth(ManagerTagData tag)
                {
                    if (tag == null)
                        return 6f;
                    string label = "(" + GetActiveCount(tag) + "/" +
            GetInstalledCount(tag) + ") " + tag.Name;
                    return Mathf.Max(130f, Text.CalcSize(label).x + 43f) + 6f;
                }
        
        private float GetTagTabSlotWidth(ManagerTagData tag)
                {
                    if (tag == null)
                        return 163f;
                    string label = tag.Name + " (" +
            GetActiveCount(tag) + "/" +
            GetInstalledCount(tag) + ")";
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
                            "(" + GetInstalledCount(tag) + ") " + tag.Name))
                            SelectTag(tag);
                        TooltipHandler.TipRegion(labelRect, "(" + GetInstalledCount(tag) + ") " + tag.Name);
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
                        options.Add(new FloatMenuOption(tags[targetIndex].Name,
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
                        categoryTabsHaveKeyboardFocus = true;
                        modListHasKeyboardFocus = false;
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
                            PersistCategoryData();
                        }
                        allModsView = false;
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
                        dragFolderSourceCategoryId = inTagMode ? null : currentCategoryId;
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
                            else
                                PersistCategoryData();
                        }
                        currentFolderId = folder.Id;
                        dragFolderId = null;
                        current.Use();
                        RefreshVisibleMods();
                    }
                }
        
        private void MoveFolderToCategory(
                    string folderId,
                    string sourceCategoryId,
                    ManagerCategoryData targetCategory)
                {
                    if (folderId.NullOrEmpty() ||
                        folderId == "uncategorized" ||
                        sourceCategoryId.NullOrEmpty() ||
                        targetCategory == null ||
                        string.Equals(sourceCategoryId, targetCategory.Id,
                            StringComparison.OrdinalIgnoreCase))
                        return;
        
                    ManagerCategoryData sourceCategory = categories.FirstOrDefault(
                        item => item != null &&
                            string.Equals(item.Id, sourceCategoryId,
                                StringComparison.OrdinalIgnoreCase));
                    if (sourceCategory == null || sourceCategory.Folders == null)
                        return;
        
                    ManagerFolderData movingFolder = FindFolder(sourceCategory, folderId);
                    if (movingFolder == null || movingFolder.Id == "uncategorized")
                        return;
        
                    if (targetCategory.Folders == null)
                        targetCategory.Folders = new List<ManagerFolderData>();
        
                    // 폴더 ID가 대상 카테고리에 이미 있으면 새 ID를 부여한다.
                    if (FindFolder(targetCategory, movingFolder.Id) != null)
                        movingFolder.Id = Guid.NewGuid().ToString("N");
        
                    // 폴더에 포함된 모드의 카테고리 소속도 함께 이전한다.
                    // 카테고리 건수는 category.ModIds를 기준으로 집계하므로 이 목록을 갱신해야 한다.
                    List<string> movingPackageIds = movingFolder.ModIds == null
                        ? new List<string>()
                        : movingFolder.ModIds
                            .Where(packageId => !packageId.NullOrEmpty())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
        
                    if (sourceCategory.ModIds != null)
                    {
                        HashSet<string> movingIdSet = new HashSet<string>(
                            movingPackageIds, StringComparer.OrdinalIgnoreCase);
                        sourceCategory.ModIds.RemoveAll(packageId =>
                            !packageId.NullOrEmpty() && movingIdSet.Contains(packageId));
                    }
        
                    if (targetCategory.ModIds == null)
                        targetCategory.ModIds = new List<string>();
        
                    foreach (string packageId in movingPackageIds)
                    {
                        if (!targetCategory.ModIds.Contains(packageId,
                            StringComparer.OrdinalIgnoreCase))
                            targetCategory.ModIds.Add(packageId);
                    }
        
                    sourceCategory.Folders.Remove(movingFolder);
                    movingFolder.Collapsed = false;
                    targetCategory.Folders.Add(movingFolder);
        
                    currentCategoryId = targetCategory.Id;
                    currentFolderId = movingFolder.Id;
                    selectedPackageIds.Clear();
                    primarySelectedPackageId = null;
                    lastSelectedIndex = -1;
        
                    PersistCategoryData();
                    RefreshVisibleMods();
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
        
                        allModsView = false;
                        currentTagId = tag.Id;
                        currentFolderId = tag.Folders.FirstOrDefault()?.Id;
                        selectedPackageIds.Clear();
                        primarySelectedPackageId = null;
                        lastSelectedIndex = -1;
                        RefreshVisibleMods();
                }
    }
}
