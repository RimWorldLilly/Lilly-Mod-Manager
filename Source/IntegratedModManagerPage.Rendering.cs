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
        private void DrawModList(Rect rect)
                {
                    if (allModsView)
                    {
                        DrawAllModsList(rect);
                        return;
                    }
        
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
                    modListViewportHeight = innerRect.height;
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
                            PersistCategoryData();
                            RefreshVisibleMods();
                        }
        
                        DrawFolderEnabledCheckbox(
                            new Rect(headerRect.x + 32f, y + 2f, 26f, 23f), folder.ModIds);
                        Rect titleRect = new Rect(headerRect.x + 66f, y + 2f,
                            Mathf.Max(30f, headerRect.width - 259f), 26f);
                        HandleFolderReorder(folder, titleRect, category.Folders, false);
                        if (folder.Id == currentFolderId)
                            Widgets.DrawHighlight(titleRect);
                        if (dragFolderId == folder.Id)
                        {
                            Widgets.DrawHighlight(titleRect);
                            Widgets.DrawBox(titleRect, 2);
                        }
                        string folderCountLabel = folder.Name + " (" +
                            GetActiveCount(folder) + "/" + GetInstalledCount(folder) + ")";
                        if (Widgets.ButtonText(titleRect, folderCountLabel))
                            currentFolderId = folder.Id;
                        TooltipHandler.TipRegion(titleRect, folderCountLabel);
        
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
        
        private void DrawAllModsList(Rect rect)
                {
                    Widgets.DrawBox(rect);
                    Rect innerRect = rect.ContractedBy(8f);
                    modListViewportHeight = innerRect.height;
                    float viewHeight = Mathf.Max(innerRect.height, visibleMods.Count * RowHeight);
                    float scrollbarWidth = viewHeight > innerRect.height
                        ? ManagerUiMetrics.VerticalScrollbarWidth
                        : 0f;
                    Rect viewRect = new Rect(0f, 0f, innerRect.width - scrollbarWidth, viewHeight);
                    Widgets.BeginScrollView(innerRect, ref modListScroll, viewRect);
        
                    for (int i = 0; i < visibleMods.Count; i++)
                        DrawModRow(visibleMods[i], i, viewRect.width, i * RowHeight);
        
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
                    modListViewportHeight = innerRect.height;
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
                        DrawFolderEnabledCheckbox(
                            new Rect(headerRect.x + 32f, y + 2f, 26f, 23f), tagFolderIds);
                        Rect titleRect = new Rect(headerRect.x + 66f, y + 2f,
                            Mathf.Max(30f, headerRect.width - 259f), 26f);
                        HandleFolderReorder(folder, titleRect, tag.Folders, true);
                        if (folder.Id == currentFolderId)
                            Widgets.DrawHighlight(titleRect);
                        if (dragFolderId == folder.Id)
                        {
                            Widgets.DrawHighlight(titleRect);
                            Widgets.DrawBox(titleRect, 2);
                        }
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
                    Color previousGuiColor = GUI.color;
                    GUI.color = Color.white;
        
                    // 오른쪽 4px는 카테고리/태그 목록과 같은 위치의 선택 표시 막대 공간으로 남긴다.
                    Rect rowRect = new Rect(0f, y, Mathf.Max(0f, width - 5f), RowHeight);
                    bool rowIsVisible = y + RowHeight >= modListScroll.y &&
                        y <= modListScroll.y + modListViewportHeight;
                    Rect checkboxHitRect = new Rect(rowRect.x, rowRect.y, 32f, rowRect.height);
        
        // 기존 교차 배경색
        Color baseRowColor = visibleIndex % 2 == 0
            ? new Color(0.09f, 0.09f, 0.09f, 1f)
            : new Color(0.055f, 0.055f, 0.055f, 1f);
        
        // 현재 모드의 개별 색상
        ManagerModData state = FindModState(mod.PackageId);
        
        string rowColorHex = state != null
            ? state.ColorHex
            : null;
        
        // 모드에 지정된 색상이 있으면 사용
        // 없으면 기존 교차 배경색 사용
        Color finalRowColor = ManagerTagColors.IsValidHex(rowColorHex)
            ? ManagerTagColors.Background(rowColorHex)
            : baseRowColor;
        
        // 행 전체에 배경색 적용
        Widgets.DrawBoxSolid(rowRect, finalRowColor);
        
        // 행 전체에 배경색 적용
        
        
        
        
                    bool isSelected = selectedPackageIds.Contains(mod.PackageId);
                    bool isPrimarySelection = isSelected &&
                        string.Equals(primarySelectedPackageId, mod.PackageId,
                            StringComparison.OrdinalIgnoreCase);
                    if (isSelected)
                        Widgets.DrawHighlight(rowRect);
                    else
                        Widgets.DrawHighlightIfMouseover(rowRect);
        
                    if (isPrimarySelection)
                    {
                        // 카테고리/태그 목록과 같은 위치 및 크기(4x25px)로 청록색 막대를 표시한다.
                        Rect markerRect = new Rect(rowRect.xMax + 1f, rowRect.y + 1f,
                            4f, 25f);
                        Widgets.DrawBoxSolid(markerRect, new Color(0f, 0.9f, 1f, 1f));
        
                        // 주 선택 모드의 테두리는 현재 키보드 포커스 위치에 따라 구분한다.
                        Color previousSelectionColor = GUI.color;
                        GUI.color = modListHasKeyboardFocus
                            ? Color.white
                            : new Color(0.28f, 0.32f, 0.34f, 1f);
                        Widgets.DrawBox(rowRect, 1);
                        GUI.color = previousSelectionColor;
                    }
                    else if (isSelected)
                    {
                        // 추가 선택 항목도 주 선택 항목과 같은 위치 및 크기(4x25px)를 사용한다.
                        // 색상만 기존 어두운 청록색으로 유지한다.
                        Rect markerRect = new Rect(rowRect.xMax + 1f, rowRect.y + 1f,
                            4f, 25f);
                        Widgets.DrawBoxSolid(markerRect, new Color(0f, 0.38f, 0.43f, 1f));
        
                        Color previousSelectionColor = GUI.color;
                        GUI.color = new Color(0.28f, 0.32f, 0.34f, 1f);
                        Widgets.DrawBox(rowRect, 1);
                        GUI.color = previousSelectionColor;
                    }
        
                    if (!allModsView && isDraggingMod && !dragPackageId.NullOrEmpty() &&
                        !string.Equals(dragPackageId, mod.PackageId, StringComparison.OrdinalIgnoreCase) &&
                        rowRect.Contains(Event.current.mousePosition))
                    {
                        Widgets.DrawLightHighlight(rowRect);
                        if (Event.current.type == EventType.MouseUp)
                            HandleDragDropToMod(mod.PackageId);
                    }
         
                    if (Event.current.type == EventType.MouseDown &&
                        Event.current.button == 0 &&
                        rowRect.Contains(Event.current.mousePosition))
                    {
                        // 필터가 적용된 상태에서도 실제 모드 행을 클릭하면
                        // 키보드 포커스가 반드시 모드 목록으로 전환되도록 행 자체에서 처리한다.
                        modListHasKeyboardFocus = true;
                        categoryTabsHaveKeyboardFocus = false;
                    }
        
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                        rowRect.Contains(Event.current.mousePosition) &&
                        !checkboxHitRect.Contains(Event.current.mousePosition))
                    {
                        if (Event.current.control || Event.current.shift)
                        {
                            SelectModByPackageId(mod.PackageId, Event.current.control, Event.current.shift);
                        }
                        else
                        {
                            // 일반 클릭은 항상 클릭한 모드 하나만 선택한다.
                            // 단, 기존 다중 선택 항목을 드래그할 수 있도록 드래그 대상은 별도로 보존한다.
                            List<string> selectionForDrag = selectedPackageIds.Contains(mod.PackageId)
                                ? selectedPackageIds.ToList()
                                : new List<string> { mod.PackageId };
        
                            SelectModByPackageId(mod.PackageId, false, false);
        
                            dragPackageId = mod.PackageId;
                            dragSelection.Clear();
                            dragSelection.AddRange(selectionForDrag);
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
                    Rect checkRect = new Rect(rowRect.x + 3f, rowRect.y + 3f, 24f, 24f);
                    bool checkedValue = state != null && state.Enabled;
                    bool protectedMod = IsProtected(mod);
                    Widgets.Checkbox(checkRect.position, ref checkedValue, checkRect.width, protectedMod);
                    if (!protectedMod && state != null && checkedValue != state.Enabled)
                    {
                        state.Enabled = checkedValue;
                        RefreshVisibleMods();
                    }
        
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
                        SelectModByPackageId(mod.PackageId, Event.current.control, Event.current.shift);
        
                    string name = GetDisplayName(mod);
                    // Color previousTextColor = GUI.color;
                    ModRowIssue rowIssue = rowIsVisible ? GetModRowIssue(mod) : ModRowIssue.None;
                    bool conflictingMod = rowIsVisible &&
                        IsModActiveInManager(mod) && HasActiveIncompatibility(mod);
                    bool outdatedForGameVersion = rowIsVisible &&
                        IsModActiveInManager(mod) && IsModOutdatedForCurrentGameVersion(mod);
        
                    // 강조 우선순위: 필수 모드 누락 > 충돌 > 로드 순서 > 요구 버전 불일치 > 게임 지원 버전 경고.
                    // 게임 지원 버전 경고는 다른 문제가 없을 때만 표시한다.
                    if (rowIssue == ModRowIssue.RequiredMod)
                        GUI.color = new Color(1f, 0.28f, 0.28f, 1f);
                    else if (conflictingMod)
                        GUI.color = new Color(1f, 0.415f, 0.26f, 1f);
                    else if (rowIssue == ModRowIssue.LoadOrder)
                        GUI.color = new Color(1f, 0.55f, 0.24f, 1f);
                    else if (rowIssue == ModRowIssue.Version)
                        GUI.color = new Color(1f, 0.685f, 0.22f, 1f);
                    else if (outdatedForGameVersion)
                        GUI.color = new Color(1f, 0.82f, 0.2f, 1f);
        
                    if (rowIssue == ModRowIssue.RequiredMod)
                        TooltipHandler.TipRegion(nameRect, ManagerTranslation.Get("LMM_0145"));
                    else if (conflictingMod)
                        TooltipHandler.TipRegion(nameRect, "활성 모드와 충돌하는 모드입니다.");
                    else if (rowIssue == ModRowIssue.LoadOrder)
                        TooltipHandler.TipRegion(nameRect, ManagerTranslation.Get("LMM_0143"));
                    else if (rowIssue == ModRowIssue.Version)
                        TooltipHandler.TipRegion(nameRect, ManagerTranslation.Get("LMM_0146"));
                    else if (outdatedForGameVersion)
                        TooltipHandler.TipRegion(nameRect,
                            "이 모드는 현재 게임 버전보다 낮은 버전만 지원합니다. 지원 버전: " +
                            GetSupportedGameVersionText(mod));
                    string rowName = protectedMod ? name + ManagerTranslation.Get("LMM_0001") : name;
                    Widgets.Label(nameRect, rowName);
                    bool workshopItemMissing = false;
                    if (rowIsVisible && mod.OnSteamWorkshop)
                    {
                        ulong workshopId = GetCachedWorkshopId(mod);
                        workshopItemMissing = WorkshopPreviewGallery.RequestAvailability(workshopId) ==
                            WorkshopPreviewGallery.WorkshopItemStatus.Missing;
                    }
                    if (workshopItemMissing)
                    {
                        float strikeWidth = Mathf.Min(Text.CalcSize(rowName).x, nameRect.width);
                        Widgets.DrawLineHorizontal(nameRect.x, rowRect.y + RowHeight * 0.52f,
                            strikeWidth);
                        TooltipHandler.TipRegion(nameRect, ManagerTranslation.Get("LMM_0144"));
                    }
                    // GUI.color = previousTextColor;
                    GUI.color = previousGuiColor;
                }
        
        private void DrawModDragPreview(Rect windowRect)
                {
                    if (!isDraggingMod || Event.current.type != EventType.Repaint ||
                        dragPackageId.NullOrEmpty())
                        return;
        
                    ModMetaData draggedMod = FindMod(dragPackageId);
                    if (draggedMod == null)
                        return;
        
                    string label = draggedMod.Name;
                    if (dragSelection.Count > 1)
                        label += " (" + dragSelection.Count + ")";
                    float width = Mathf.Clamp(Text.CalcSize(label).x + 24f, 120f, 360f);
                    Vector2 mouse = Event.current.mousePosition;
                    Rect preview = new Rect(mouse.x + 14f, mouse.y + 14f, width, 28f);
                    preview.x = Mathf.Clamp(preview.x, windowRect.xMin + 4f,
                        windowRect.xMax - preview.width - 4f);
                    preview.y = Mathf.Clamp(preview.y, windowRect.yMin + 4f,
                        windowRect.yMax - preview.height - 4f);
        
                    Widgets.DrawBoxSolid(preview, new Color(0.22f, 0.18f, 0.12f, 0.96f));
                    Widgets.DrawBox(preview);
                    TextAnchor previousAnchor = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(new Rect(preview.x + 8f, preview.y, preview.width - 16f,
                        preview.height), label);
                    Text.Anchor = previousAnchor;
                }
        
        private void DrawFolderDragPreview(Rect windowRect)
                {
                    if (Event.current.type != EventType.Repaint || dragFolderId.NullOrEmpty())
                        return;
        
                    ManagerFolderData folder = null;
                    if (dragFolderInTagMode)
                        folder = FindFolder(FindCurrentTag(), dragFolderId);
                    else
                    {
                        ManagerCategoryData source = categories.FirstOrDefault(item =>
                            item != null && item.Id == dragFolderSourceCategoryId);
                        folder = FindFolder(source, dragFolderId);
                    }
                    if (folder == null)
                        return;
        
                    string label = "폴더 이동: " + folder.Name;
                    float width = Mathf.Clamp(Text.CalcSize(label).x + 24f, 150f, 360f);
                    Vector2 mouse = Event.current.mousePosition;
                    Rect preview = new Rect(mouse.x + 14f, mouse.y + 14f, width, 30f);
                    preview.x = Mathf.Clamp(preview.x, windowRect.xMin + 4f,
                        windowRect.xMax - preview.width - 4f);
                    preview.y = Mathf.Clamp(preview.y, windowRect.yMin + 4f,
                        windowRect.yMax - preview.height - 4f);
        
                    Widgets.DrawBoxSolid(preview, new Color(0.22f, 0.18f, 0.12f, 0.96f));
                    Widgets.DrawBox(preview, 2);
                    TextAnchor previousAnchor = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(new Rect(preview.x + 8f, preview.y,
                        preview.width - 16f, preview.height), label);
                    Text.Anchor = previousAnchor;
                }
        
        private void DrawSidebarDragPreview(Rect windowRect)
                {
                    if (Event.current.type != EventType.Repaint ||
                        (dragCategoryId.NullOrEmpty() && dragTagId.NullOrEmpty()))
                        return;
        
                    string itemName = null;
                    string itemType = ManagerTranslation.Get(dragCategoryId.NullOrEmpty()
                        ? "LMM_0159" : "LMM_0158");
                    if (!dragCategoryId.NullOrEmpty())
                    {
                        ManagerCategoryData category = categories.FirstOrDefault(item =>
                            item != null && item.Id == dragCategoryId);
                        itemName = category?.Name;
                    }
                    else
                    {
                        ManagerTagData tag = tags.FirstOrDefault(item =>
                            item != null && item.Id == dragTagId);
                        itemName = tag?.Name;
                    }
                    if (itemName.NullOrEmpty())
                        return;
        
                    string label = itemType + ": " + itemName;
                    float width = Mathf.Clamp(Text.CalcSize(label).x + 24f, 140f, 360f);
                    Vector2 mouse = Event.current.mousePosition;
                    Rect preview = new Rect(mouse.x + 14f, mouse.y + 14f, width, 28f);
                    preview.x = Mathf.Clamp(preview.x, windowRect.xMin + 4f,
                        windowRect.xMax - preview.width - 4f);
                    preview.y = Mathf.Clamp(preview.y, windowRect.yMin + 4f,
                        windowRect.yMax - preview.height - 4f);
        
                    Widgets.DrawBoxSolid(preview, new Color(0.22f, 0.18f, 0.12f, 0.96f));
                    Widgets.DrawBox(preview);
                    TextAnchor previousAnchor = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(new Rect(preview.x + 8f, preview.y, preview.width - 16f,
                        preview.height), label);
                    Text.Anchor = previousAnchor;
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
        
                    // 좌클릭만 태그 화면 이동으로 처리한다. 우클릭은 호출부의 제거 동작만 처리한다.
                    Event current = Event.current;
                    return current.type == EventType.MouseDown &&
                        current.button == 0 && rect.Contains(current.mousePosition);
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
                    Rect viewport = rect.ContractedBy(12f);
                    ModMetaData mod = FindMod(primarySelectedPackageId);
                    if (mod == null)
                    {
                        Widgets.Label(viewport, ManagerTranslation.Get("LMM_0037"));
                        return;
                    }
        
                    float contentHeight = GetDetailsContentHeight(mod, viewport.width);
                    float scrollbarWidth = contentHeight > viewport.height
                        ? ManagerUiMetrics.VerticalScrollbarWidth
                        : 0f;
                    if (scrollbarWidth > 0f)
                        contentHeight = GetDetailsContentHeight(mod, viewport.width - scrollbarWidth);
                    Rect viewRect = new Rect(0f, 0f, viewport.width - scrollbarWidth, contentHeight);
                    bool pointerInsideViewport = Mouse.IsOver(viewport);
                    Widgets.BeginScrollView(viewport, ref detailsScroll, viewRect);
                    Rect inner = new Rect(0f, 0f, viewRect.width, viewRect.height);
        
                    float y = inner.y;
                    Texture2D modPreviewImage = mod.PreviewImage;
                    if (!string.Equals(workshopGalleryPackageId, mod.PackageId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        workshopGalleryPackageId = mod.PackageId;
                        workshopGalleryScroll = Vector2.zero;
                    }
                    WorkshopPreviewGallery.GalleryEntry gallery = WorkshopPreviewGallery.Request(
                        GetCachedWorkshopId(mod));
                    IReadOnlyList<WorkshopPreviewGallery.GalleryImage> galleryImages =
                        WorkshopPreviewGallery.GetImages(gallery);
        
                    float previewHeight = 0f;
        
                    if (modPreviewImage != null || galleryImages.Count > 0)
                        previewHeight = DetailsPreviewMaxHeight;
                    // if (modPreviewImage != null)
                    //     previewHeight = Mathf.Min(DetailsPreviewMaxHeight,
                    //         inner.width * modPreviewImage.height /
                    //         Mathf.Max(1f, modPreviewImage.width));
                    // else if (galleryImages.Count > 0)
                    //     previewHeight = DetailsPreviewMaxHeight;
        
                    float previewTop = y;
                    if (previewHeight > 0f)
                        y += previewHeight + 8f;
        
                    string workshopPageUrl = GetSteamWorkshopPageUrl(mod);
                    int hoveredImageIndex = DrawWorkshopGallery(inner, ref y, galleryImages,
                        pointerInsideViewport);
                    if (previewHeight > 0f)
                    {
                        Rect previewRect = new Rect(inner.x, previewTop, inner.width, previewHeight);
                        Widgets.DrawBoxSolid(previewRect, new Color(0.08f, 0.08f, 0.08f, 1f));
                        int displayIndex = hoveredImageIndex;
                        if (displayIndex < 0 && modPreviewImage == null && galleryImages.Count > 0)
                            displayIndex = 0;
                        Texture2D previewTexture = displayIndex >= 0
                            ? WorkshopPreviewGallery.GetTexture(galleryImages[displayIndex],
                                Time.realtimeSinceStartup)
                            : modPreviewImage;
                        if (previewTexture != null)
                            GUI.DrawTexture(previewRect, previewTexture, ScaleMode.ScaleToFit);
                            // GUI.DrawTexture(previewRect, previewTexture, ScaleMode.ScaleAndCrop);
                    }
        
                    Text.Font = GameFont.Medium;
                    Rect detailNameRect = new Rect(inner.x, y, inner.width - 34f, 36f);
                    Widgets.Label(detailNameRect, GetDisplayName(mod));
                    Rect editNameRect = new Rect(inner.xMax - 28f, y + 2f, 26f, 26f);
                    if (Widgets.ButtonText(editNameRect, "E"))
                        OpenRenameDisplayNameDialog(mod);
                    TooltipHandler.TipRegion(editNameRect, ManagerTranslation.Get("LMM_0105"));
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
                    Widgets.Label(new Rect(inner.x, y, inner.width, 24f),
                        ManagerTranslation.Get("LMM_0073") +
                        (supportedVersions.NullOrEmpty() ? "확인할 수 없음" : supportedVersions));
                    y += 26f;
        
                    ulong workshopIdForUpdate = GetCachedWorkshopId(mod);
                    DateTime lastUpdated;
                    if (mod.OnSteamWorkshop &&
                        WorkshopPreviewGallery.TryGetLastUpdatedDate(workshopIdForUpdate, out lastUpdated))
                    {
                        Widgets.Label(new Rect(inner.x, y, inner.width, 24f),
                            "마지막 업데이트: " + lastUpdated.ToString("yyyy-MM-dd"));
                        y += 26f;
                    }
        
                    Widgets.Label(new Rect(inner.x, y, inner.width, 24f),
                        "PackageId: " + mod.PackageIdPlayerFacing);
                    y += 28f;
                    Widgets.Label(new Rect(inner.x, y, inner.width, 24f),
                        ManagerTranslation.Get("LMM_0072") + mod.AuthorsString);
                    y += 32f;
        
                    List<string> requiredModIds = GetDependencyIds(mod,
                        new[] { "modDependencies", "ModDependencies", "Dependencies", "RequiredMods" });
                    List<string> loadAfterIds = GetDependencyIds(mod, new[] { "LoadAfter" });
                    List<string> loadBeforeIds = GetDependencyIds(mod, new[] { "LoadBefore" });
                    if (requiredModIds.Count > 0)
                        DrawDependencyLinks(inner, ref y, ManagerTranslation.Get("LMM_0151"),
                            mod, requiredModIds, true, true);
                    if (loadAfterIds.Count > 0)
                        DrawDependencyLinks(inner, ref y, ManagerTranslation.Get("LMM_0059"),
                            mod, loadAfterIds, true, false);
                    if (loadBeforeIds.Count > 0)
                        DrawDependencyLinks(inner, ref y, ManagerTranslation.Get("LMM_0142"),
                            mod, loadBeforeIds, false, false);
        
                    // About.xml의 modIncompatibilities를 선후행 정보 바로 아래에 표시
                    List<ModRequirement> incompatibilities = mod.GetRequirements()
                        .Where(requirement => requirement is ModIncompatibility)
                        .ToList();
                    if (incompatibilities.Count > 0)
                        DrawIncompatibilityLinks(inner, ref y, incompatibilities);
        
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
                        Event current = Event.current;
                        if (current.type == EventType.MouseDown && chipRect.Contains(current.mousePosition))
                        {
                            if (current.button == 1)
                            {
                                // 우클릭은 태그 제거만 수행하며 좌클릭 선택 동작은 실행하지 않는다.
                                SetTagForSelection(new List<string> { mod.PackageId }, tag, false);
                                current.Use();
                            }
                            else if (current.button == 0)
                            {
                                tagMode = true;
                                SelectTag(tag);
                                current.Use();
                            }
                        }
                        DrawTagChip(chipRect, tag);
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
                            bool requirementSatisfied = IsRequirementSatisfiedInManager(requirement);
                            if (requirement == null ||
                                (requirementSatisfied && !displayFulfilledRequirements))
                                continue;
        
                            Rect requirementRect = new Rect(inner.x + 8f, y,
                                inner.width - 8f, 24f);
                            Rect statusIconRect = new Rect(requirementRect.x,
                                requirementRect.y + 2f, 20f, 20f);
                            string requirementPackageId = GetRequirementPackageId(requirement);
                            bool requirementInstalled = FindMod(requirementPackageId) != null;
                            Texture2D statusIcon = requirementSatisfied
                                ? ModRequirement.Resolved
                                : requirementInstalled
                                    ? ModRequirement.Installed
                                    : ModRequirement.NotInstalled;
                            if (statusIcon != null)
                                GUI.DrawTexture(statusIconRect, statusIcon,
                                    ScaleMode.ScaleToFit);
        
                            string requirementName = ReadMetadataValue(requirement, "displayName") as string;
                            if (requirementName.NullOrEmpty())
                                requirementName = ReadMetadataValue(requirement, "packageId") as string;
                            if (requirementName.NullOrEmpty())
                                requirementName = requirementPackageId;
                            if (requirementName.NullOrEmpty())
                                requirementName = requirement.RequirementTypeLabel;
                            string workshopUrl = !requirementSatisfied &&
                                FindMod(requirementPackageId) == null
                                    ? GetMissingDependencyWorkshopUrl(mod, requirement, requirementPackageId)
                                    : string.Empty;
                            float workshopButtonWidth = workshopUrl.NullOrEmpty() ? 0f : 112f;
                            Rect requirementNameRect = new Rect(statusIconRect.xMax + 4f,
                                requirementRect.y,
                                requirementRect.width - 24f - workshopButtonWidth, requirementRect.height);
                            Widgets.Label(requirementNameRect, requirementName);
                            TooltipHandler.TipRegion(requirementRect, requirementSatisfied
                                ? ManagerTranslation.Get("LMM_0147") + requirementName
                                : requirement.Tooltip);
                            if (!requirementPackageId.NullOrEmpty() &&
                                Widgets.ButtonInvisible(requirementNameRect))
                                SelectDependency(requirementPackageId);
                            if (!workshopUrl.NullOrEmpty() && Widgets.ButtonText(
                                new Rect(requirementRect.xMax - workshopButtonWidth, requirementRect.y,
                                    workshopButtonWidth, requirementRect.height),
                                ManagerTranslation.Get("LMM_0152")))
                                OpenUrl(workshopUrl);
                            y += 24f;
                        }
        
                        if (priorDisplayFulfilled != displayFulfilledRequirements)
                            descriptionScroll = Vector2.zero;
                    }
        
                    List<string> folderIssues = GetCachedModLoadFolderIssues(mod);
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
                    if (!workshopPageUrl.NullOrEmpty())
                    {
                        DrawDetailActionButton(inner, ref actionX, ref actionY, 180f,
                            "WorkshopPage".Translate(), () => OpenUrl(workshopPageUrl));
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
                    string description = mod.Description.NullOrEmpty()
                        ? ManagerTranslation.Get("LMM_0061")
                        : WebUtility.HtmlDecode(mod.Description);
                    float descriptionHeight = Mathf.Max(80f, Text.CalcHeight(description, inner.width));
                    Widgets.Label(new Rect(inner.x, y, inner.width, descriptionHeight), description);
                    Widgets.EndScrollView();
                }
        
        private float GetDetailsContentHeight(ModMetaData mod, float width)
                {
                    float y = 0f;
                    Texture2D previewImage = mod.PreviewImage;
                    ulong workshopId = GetCachedWorkshopId(mod);
                    IReadOnlyList<WorkshopPreviewGallery.GalleryImage> gallery =
                        WorkshopPreviewGallery.GetImages(WorkshopPreviewGallery.Request(workshopId));
                    if (previewImage != null || gallery.Count > 0)
                        y += DetailsPreviewMaxHeight + 8f;
                    // if (previewImage != null)
                    //     y += Mathf.Min(190f, width * previewImage.height /
                    //         Mathf.Max(1f, previewImage.width)) + 8f;
                    // else if (gallery.Count > 0)
                    //     y += 190f + 8f;
                    if (gallery.Count > 0)
                    {
                        float galleryWidth = Mathf.Max(width, gallery.Count * 106f);
                        y += 76f + (galleryWidth > width
                            ? ManagerUiMetrics.HorizontalScrollbarHeight : 0f) + 6f;
                    }
        
                    y += 38f;
                    string vanillaName = mod.Name.NullOrEmpty() ? mod.FolderName : mod.Name;
                    if (!string.Equals(GetDisplayName(mod), vanillaName, StringComparison.Ordinal))
                        y += 26f;
                    if (!GetModVersionText(mod).NullOrEmpty())
                        y += 26f;
                    y += 26f;
                    DateTime lastUpdated;
                    if (mod.OnSteamWorkshop &&
                        WorkshopPreviewGallery.TryGetLastUpdatedDate(workshopId, out lastUpdated))
                        y += 26f;
                    y += 28f + 32f;
        
                    List<string> requiredIds = GetDependencyIds(mod,
                        new[] { "modDependencies", "ModDependencies", "Dependencies", "RequiredMods" });
                    List<string> loadAfterIds = GetDependencyIds(mod, new[] { "LoadAfter" });
                    List<string> loadBeforeIds = GetDependencyIds(mod, new[] { "LoadBefore" });
                    if (requiredIds.Count > 0)
                        y += EstimateDependencyLinksHeight(width,
                            ManagerTranslation.Get("LMM_0151"), requiredIds);
                    if (loadAfterIds.Count > 0)
                        y += EstimateDependencyLinksHeight(width,
                            ManagerTranslation.Get("LMM_0059"), loadAfterIds);
                    if (loadBeforeIds.Count > 0)
                        y += EstimateDependencyLinksHeight(width,
                            ManagerTranslation.Get("LMM_0142"), loadBeforeIds);
                    int incompatibilityCount = mod.GetRequirements()
                        .Count(requirement => requirement is ModIncompatibility);
                    if (incompatibilityCount > 0)
                        y += 25f + incompatibilityCount * 22f;
                    y += 32f;
        
                    List<ModRequirement> requirements = mod.GetRequirements().ToList();
                    if (requirements.Count > 0)
                    {
                        y += 28f;
                        y += requirements.Count(requirement => requirement != null &&
                            (!IsRequirementSatisfiedInManager(requirement) || displayFulfilledRequirements)) * 24f;
                    }
        
                    List<string> folderIssues = GetCachedModLoadFolderIssues(mod);
                    if (folderIssues != null && folderIssues.Count > 0)
                    {
                        string issueText = GenText.ToLineList(folderIssues, "  - ");
                        y += 24f + Text.CalcHeight(issueText, Mathf.Max(1f, width - 8f)) + 6f;
                    }
        
                    List<float> buttonWidths = new List<float>();
                    if (GetAvailableModActionCount(mod) > 0)
                        buttonWidths.Add(140f);
                    ManagerModData selectedState = FindModState(mod.PackageId);
                    if (!IsProtected(mod) && selectedState != null)
                        buttonWidths.Add(120f);
                    if (!GetSteamWorkshopPageUrl(mod).NullOrEmpty())
                        buttonWidths.Add(180f);
                    Mod loadedMod = FindLoadedMod(mod);
                    if (loadedMod != null && !loadedMod.SettingsCategory().NullOrEmpty())
                        buttonWidths.Add(150f);
                    if (SteamManager.Initialized && mod.CanToUploadToWorkshop())
                        buttonWidths.Add(220f);
                    if (buttonWidths.Count > 0)
                    {
                        int rows = 1;
                        float x = 0f;
                        for (int i = 0; i < buttonWidths.Count; i++)
                        {
                            float buttonWidth = Mathf.Min(buttonWidths[i], width);
                            if (x + buttonWidth > width && x > 0f)
                            {
                                rows++;
                                x = 0f;
                            }
                            x += buttonWidth + 6f;
                        }
                        y += rows * 34f;
                    }
        
                    string description = mod.Description.NullOrEmpty()
                        ? ManagerTranslation.Get("LMM_0061")
                        : WebUtility.HtmlDecode(mod.Description);
                    y += Mathf.Max(80f, Text.CalcHeight(description, width)) + 4f;
                    return Mathf.Max(y, 100f);
                }
        
        private float EstimateDependencyLinksHeight(
                    float width,
                    string label,
                    IList<string> packageIds)
                {
                    const float lineHeight = 22f;
                    float x = Text.CalcSize(label).x + 2f;
                    int lines = 1;
                    for (int i = 0; i < packageIds.Count; i++)
                    {
                        ModMetaData dependency = FindMod(packageIds[i]);
                        string name = dependency == null ? packageIds[i] : GetDisplayName(dependency);
                        float itemWidth = Text.CalcSize(name + (i + 1 < packageIds.Count ? ", " : "")).x;
                        if (x + itemWidth > width && x > 0f)
                        {
                            lines++;
                            x = 0f;
                        }
                        x += itemWidth;
                    }
                    return lines * lineHeight + 3f;
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
        
        private List<string> GetCachedModLoadFolderIssues(ModMetaData mod)
                {
                    if (mod == null || mod.PackageId.NullOrEmpty())
                        return new List<string>();
                    List<string> issues;
                    if (modLoadFolderIssuesCache.TryGetValue(mod.PackageId, out issues))
                        return issues;
                    try
                    {
                        issues = GetModLoadFolderIssues(mod) ?? new List<string>();
                    }
                    catch (Exception exception)
                    {
                        Log.Warning("Could not inspect load folders for " + mod.PackageId + ": " + exception);
                        issues = new List<string>();
                    }
                    modLoadFolderIssuesCache[mod.PackageId] = issues;
                    return issues;
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
                    float buttonWidth = (rect.width - gap * 11f) / 12f;
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
                            OpenCreateCategoryDialog(GetSelectedCategoryInsertIndex());
                    }
                    x += buttonWidth + gap;
        
                    if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0067")))
                        SortAllCategories();
                    x += buttonWidth + gap;
        
                    if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0023")))
                        ConfirmUnsubscribeSelected();
                    x += buttonWidth + gap;
        
                    if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0185")))
                        Find.WindowStack.Add(new WorkshopMissingItemsWindow());
                    x += buttonWidth + gap;
        
                    // 이전 사이드바 렌더링의 GUI.enabled 상태와 무관하게
                    // 빠른 폴더 이동/바로가기 버튼은 첫 화면부터 입력을 받는다.
                    bool previousFastMoveGuiEnabled = GUI.enabled;
                    GUI.enabled = true;
                    bool openFastFolderMove = Widgets.ButtonText(
                        new Rect(x, rect.y, buttonWidth, rect.height),
                        ManagerTranslation.Get("LMM_0094"));
                    GUI.enabled = previousFastMoveGuiEnabled;
                    if (openFastFolderMove)
                        OpenFastFolderMoveDialog();
                    x += buttonWidth + gap;
        
                    if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0039")))
                        OpenSavePresetDialog();
                    x += buttonWidth + gap;
        
                    if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0038")))
                        OpenPresetMenu();
                    x += buttonWidth + gap;
        
                    // '폴더 열기'는 선택된 모드의 실제 폴더가 있을 때만 활성화한다.
                    ModMetaData selectedModForFolder = FindMod(primarySelectedPackageId);
                    bool canOpenSelectedModFolder = selectedModForFolder != null
                        && selectedModForFolder.RootDir != null
                        && !selectedModForFolder.Official;
                    bool previousOpenFolderGuiEnabled = GUI.enabled;
                    GUI.enabled = previousOpenFolderGuiEnabled && canOpenSelectedModFolder;
                    bool openSelectedModFolder = Widgets.ButtonText(
                        new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0186"));
                    GUI.enabled = previousOpenFolderGuiEnabled;
                    if (openSelectedModFolder)
                        OpenModFolder(selectedModForFolder);
                    x += buttonWidth + gap;
        
                    if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0187")))
                        Find.WindowStack.Add(new ManagerOptionsWindow(settings));
                    x += buttonWidth + gap;
        
                    if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0044")))
                        CancelChanges();
                    x += buttonWidth + gap;
        
                    if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0070")))
                        ApplyChanges();
                }
    }
}
