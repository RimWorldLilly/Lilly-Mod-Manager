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
        
        private void HandleModListNavigation(Rect categoryRect, Rect listRect)
                {
                    Event current = Event.current;
                    if (current.type == EventType.MouseDown)
                    {
                        bool clickedCategory = categoryRect.Contains(current.mousePosition);
                        bool clickedModList = listRect.Contains(current.mousePosition);
                        if (clickedCategory)
                        {
                            categoryTabsHaveKeyboardFocus = true;
                            modListHasKeyboardFocus = false;
                        }
                        else if (clickedModList)
                        {
                            modListHasKeyboardFocus = true;
                            categoryTabsHaveKeyboardFocus = false;
                        }
                    }
        
                    if (current.type != EventType.KeyDown ||
                        current.control || current.alt || current.command)
                        return;
        
                    if (modListHasKeyboardFocus && current.keyCode == KeyCode.LeftArrow)
                    {
                        modListHasKeyboardFocus = false;
                        categoryTabsHaveKeyboardFocus = true;
                        current.Use();
                        return;
                    }
        
                    if (categoryTabsHaveKeyboardFocus && current.keyCode == KeyCode.RightArrow)
                    {
                        categoryTabsHaveKeyboardFocus = false;
                        modListHasKeyboardFocus = true;
                        if (visibleMods.Count > 0 &&
                            visibleMods.FindIndex(mod => string.Equals(
                                mod.PackageId, primarySelectedPackageId,
                                StringComparison.OrdinalIgnoreCase)) < 0)
                        {
                            SelectMod(0, false, false);
                            modListScroll.y = Mathf.Max(0f,
                                GetVisibleModRowY(visibleMods[0].PackageId));
                        }
                        current.Use();
                        return;
                    }
        
                    bool categoryNavigationKey =
                        current.keyCode == KeyCode.UpArrow ||
                        current.keyCode == KeyCode.DownArrow ||
                        current.keyCode == KeyCode.PageUp ||
                        current.keyCode == KeyCode.PageDown ||
                        current.keyCode == KeyCode.Home ||
                        current.keyCode == KeyCode.End;
        
                    if (categoryTabsHaveKeyboardFocus && categoryNavigationKey)
                    {
                        int itemCount = tagMode ? tags.Count : categories.Count;
                        if (itemCount <= 0)
                        {
                            current.Use();
                            return;
                        }
        
                        int currentSlot;
                        if (allModsView)
                        {
                            currentSlot = 0;
                        }
                        else if (tagMode)
                        {
                            int selectedIndex = tags.FindIndex(tag => tag != null &&
                                string.Equals(tag.Id, currentTagId,
                                    StringComparison.OrdinalIgnoreCase));
                            currentSlot = selectedIndex < 0 ? 0 : selectedIndex + 1;
                        }
                        else
                        {
                            int selectedIndex = categories.FindIndex(category =>
                                string.Equals(category.Id, currentCategoryId,
                                    StringComparison.OrdinalIgnoreCase));
                            currentSlot = selectedIndex + 1;
                        }
        
                        int pageStep = Mathf.Max(1,
                            Mathf.FloorToInt((categoryRect.height - 52f) / 32f));
                        int nextSlot = currentSlot;
                        switch (current.keyCode)
                        {
                            case KeyCode.Home:
                                nextSlot = 0;
                                break;
                            case KeyCode.End:
                                nextSlot = itemCount;
                                break;
                            case KeyCode.PageUp:
                                nextSlot = Mathf.Max(0, currentSlot - pageStep);
                                break;
                            case KeyCode.PageDown:
                                nextSlot = Mathf.Min(itemCount, currentSlot + pageStep);
                                break;
                            case KeyCode.UpArrow:
                                nextSlot = Mathf.Max(0, currentSlot - 1);
                                break;
                            case KeyCode.DownArrow:
                                nextSlot = Mathf.Min(itemCount, currentSlot + 1);
                                break;
                        }
        
                        if (nextSlot != currentSlot)
                        {
                            if (nextSlot == 0)
                            {
                                SelectAllModsView();
                            }
                            else if (tagMode)
                            {
                                ManagerTagData nextTag = tags[nextSlot - 1];
                                if (nextTag != null)
                                    SelectTag(nextTag);
                            }
                            else
                            {
                                ManagerCategoryData nextCategory = categories[nextSlot - 1];
                                allModsView = false;
                                currentCategoryId = nextCategory.Id;
                                currentFolderId = nextCategory.Folders.FirstOrDefault()?.Id;
                                selectedPackageIds.Clear();
                                primarySelectedPackageId = null;
                                lastSelectedIndex = -1;
                                RefreshVisibleMods();
                            }
        
                            float rowY = nextSlot * 32f;
                            float rowHeight = 30f;
                            float viewportHeight = Mathf.Max(1f, categoryRect.height - 52f);
                            if (rowY < categoryTabScroll.y)
                                categoryTabScroll.y = rowY;
                            else if (rowY + rowHeight > categoryTabScroll.y + viewportHeight)
                                categoryTabScroll.y = rowY + rowHeight - viewportHeight;
                            categoryTabScroll.y = Mathf.Max(0f, categoryTabScroll.y);
                            if (tagMode)
                                tagTabScroll.y = categoryTabScroll.y;
                        }
        
                        current.Use();
                        return;
                    }
        
                    bool modNavigationKey =
                        current.keyCode == KeyCode.UpArrow ||
                        current.keyCode == KeyCode.DownArrow ||
                        current.keyCode == KeyCode.PageUp ||
                        current.keyCode == KeyCode.PageDown ||
                        current.keyCode == KeyCode.Home ||
                        current.keyCode == KeyCode.End;
                    if (!modListHasKeyboardFocus || !modNavigationKey)
                        return;
        
                    if (visibleMods.Count == 0)
                    {
                        current.Use();
                        return;
                    }
        
                    List<ModMetaData> navigationMods = allModsView
                        ? GetAllModsSorted()
                        : visibleMods;
                    int modIndex = navigationMods.FindIndex(mod =>
                        string.Equals(mod.PackageId, primarySelectedPackageId,
                            StringComparison.OrdinalIgnoreCase));
                    int nextModIndex = modIndex < 0 ? 0 : modIndex;
                    int modPageStep = Mathf.Max(1,
                        Mathf.FloorToInt((listRect.height - 16f) / RowHeight));
                    switch (current.keyCode)
                    {
                        case KeyCode.Home:
                            nextModIndex = 0;
                            break;
                        case KeyCode.End:
                            nextModIndex = navigationMods.Count - 1;
                            break;
                        case KeyCode.PageUp:
                            nextModIndex = Mathf.Max(0, nextModIndex - modPageStep);
                            break;
                        case KeyCode.PageDown:
                            nextModIndex = Mathf.Min(navigationMods.Count - 1,
                                nextModIndex + modPageStep);
                            break;
                        case KeyCode.UpArrow:
                            nextModIndex = Mathf.Max(0, nextModIndex - 1);
                            break;
                        case KeyCode.DownArrow:
                            nextModIndex = Mathf.Min(visibleMods.Count - 1,
                                nextModIndex + 1);
                            break;
                    }
        
                    if (nextModIndex != modIndex)
                    {
                        if (allModsView)
                            SelectModByPackageId(navigationMods[nextModIndex].PackageId, false,
                                current.shift && lastSelectedIndex >= 0);
                        else
                            SelectMod(nextModIndex, false,
                                current.shift && lastSelectedIndex >= 0);
                        float modRowY = GetVisibleModRowY(navigationMods[nextModIndex].PackageId);
                        float modViewportHeight = Mathf.Max(1f, listRect.height - 16f);
                        if (modRowY < modListScroll.y)
                            modListScroll.y = modRowY;
                        else if (modRowY + RowHeight > modListScroll.y + modViewportHeight)
                            modListScroll.y = modRowY + RowHeight - modViewportHeight;
                        modListScroll.y = Mathf.Max(0f, modListScroll.y);
                    }
                    current.Use();
                }
        
        private float GetVisibleModRowY(string packageId)
                {
                    if (allModsView)
                    {
                        List<ModMetaData> orderedMods = GetAllModsSorted();
                        int index = orderedMods.FindIndex(mod => mod != null &&
                            string.Equals(mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
                        return index < 0 ? 0f : index * RowHeight;
                    }
        
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
                    List<ModMetaData> selectionMods = allModsView
                        ? GetAllModsSorted()
                        : visibleMods;
                    if (index < 0 || index >= selectionMods.Count)
                        return;
        
                    string packageId = selectionMods[index].PackageId;
                    if (shift && lastSelectedIndex >= 0)
                    {
                        if (!control)
                            selectedPackageIds.Clear();
                        int start = Mathf.Min(lastSelectedIndex, index);
                        int end = Mathf.Max(lastSelectedIndex, index);
                        for (int i = start; i <= end; i++)
                            selectedPackageIds.Add(selectionMods[i].PackageId);
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
        
        private void SelectModByPackageId(string packageId, bool control, bool shift)
                {
                    List<ModMetaData> selectionMods = allModsView
                        ? GetAllModsSorted()
                        : visibleMods;
                    int index = selectionMods.FindIndex(mod => mod != null &&
                        string.Equals(mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0)
                        SelectMod(index, control, shift);
                }
        
        private void SetSelectedEnabled(bool enabled)
                {
                    List<string> targets = selectedPackageIds.ToList();
                    for (int i = 0; i < targets.Count; i++)
                    {
                        string packageId = targets[i];
                        ModMetaData mod = FindMod(packageId);
                        ManagerModData state = FindModState(packageId);
                        if (mod != null && state != null && !IsProtected(mod))
                            state.Enabled = enabled;
                    }
        
                    // 일괄 활성화/비활성화 직후 오류 및 상태표시줄 집계를 즉시 다시 계산한다.
                    RefreshVisibleMods();
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
                        () =>
                        {
                            folder.Collapsed = !folder.Collapsed;
                            PersistCategoryData();
                            RefreshVisibleMods();
                        }));
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
        
        private void DrawPaneFocusBorders(Rect categoryRect, Rect modListRect)
                {
                    Color previousColor = GUI.color;
                    GUI.color = categoryTabsHaveKeyboardFocus
                        ? Color.white
                        : new Color(0.72f, 0.72f, 0.72f, 1f);
                    Widgets.DrawBox(categoryRect, 1);
                    GUI.color = modListHasKeyboardFocus
                        ? Color.white
                        : new Color(0.72f, 0.72f, 0.72f, 1f);
                    Widgets.DrawBox(modListRect, 1);
                    GUI.color = previousColor;
                }
        
        private void DrawTagEnabledCheckbox(Rect rect, ManagerTagData tag)
                {
                    if (tag == null)
                        return;
                    List<string> packageIds = tag.Folders == null
                        ? new List<string>()
                        : tag.Folders.Where(folder => folder != null)
                            .SelectMany(folder => GetTagFolderModIds(tag, folder))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    DrawFolderEnabledCheckbox(rect, packageIds);
                }
        
        private void DrawFolderEnabledCheckbox(Rect rect, IEnumerable<string> packageIds)
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
        
                    MultiCheckboxState checkboxState;
                    if (eligibleCount == 0)
                        checkboxState = MultiCheckboxState.Off;
                    else if (enabledCount == 0)
                        checkboxState = MultiCheckboxState.Off;
                    else if (enabledCount == eligibleCount)
                        checkboxState = MultiCheckboxState.On;
                    else
                        checkboxState = MultiCheckboxState.Partial;
        
                    if (eligibleCount == 0)
                    {
                        // 변경 가능한 모드가 없으면 기존과 동일하게 비활성 체크박스로 표시한다.
                        bool disabledValue = false;
                        Widgets.Checkbox(new Vector2(rect.x + 3f, rect.y + 1f),
                            ref disabledValue, 20f, true);
                        return;
                    }
        
                    MultiCheckboxState newState = Widgets.CheckboxMulti(
                        new Rect(rect.x + 3f, rect.y + 1f, 20f, 20f), checkboxState);
                    if (newState != checkboxState)
                        SetFolderEnabled(packageIds, newState == MultiCheckboxState.On);
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
    }
}
