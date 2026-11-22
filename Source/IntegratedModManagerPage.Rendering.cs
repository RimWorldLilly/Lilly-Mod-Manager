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
                            float categoryColumnWidth = settings.UseImprovedLayout ? 112f : 0f;
                            Rect checkboxHitRect = new Rect(rowRect.x + categoryColumnWidth,
                                rowRect.y, 32f, rowRect.height);
                
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
                                    // 이미 선택된 항목을 드래그하면 기존 다중 선택을 유지한다.
                                    // 선택되지 않은 항목을 드래그할 때만 해당 항목 하나를 새로 선택한다.
                                    bool wasAlreadySelected = selectedPackageIds.Contains(mod.PackageId);
                                    if (!wasAlreadySelected)
                                    {
                                        SelectModByPackageId(mod.PackageId, false, false);
                                    }
                                    else
                                    {
                                        primarySelectedPackageId = mod.PackageId;
                                        lastSelectedIndex = visibleMods.FindIndex(item =>
                                            item != null && string.Equals(item.PackageId, mod.PackageId,
                                                StringComparison.OrdinalIgnoreCase));
                                    }

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
                            if (categoryColumnWidth > 0f)
                            {
                                ManagerCategoryData assignedCategory = FindCategoryForPackage(mod.PackageId);
                                if (assignedCategory != null)
                                {
                                    Rect categoryChipRect = new Rect(rowRect.x + 4f, rowRect.y + 3f,
                                        categoryColumnWidth - 10f, 21f);
                                    Color previousCategoryGuiColor = GUI.color;
                                    TextAnchor previousCategoryAnchor = Text.Anchor;
                                    Widgets.DrawBoxSolid(categoryChipRect,
                                        ManagerTagColors.Background(assignedCategory.ColorHex));
                                    GUI.color = ManagerTagColors.Foreground(assignedCategory.ColorHex);
                                    Text.Anchor = TextAnchor.MiddleCenter;
                                    Widgets.Label(categoryChipRect.ContractedBy(3f), assignedCategory.Name);
                                    GUI.color = previousCategoryGuiColor;
                                    Text.Anchor = previousCategoryAnchor;
                                    TooltipHandler.TipRegion(categoryChipRect, assignedCategory.Name);
                                }
                            }

                            Rect checkRect = new Rect(rowRect.x + categoryColumnWidth + 3f,
                                rowRect.y + 3f, 24f, 24f);
                            bool checkedValue = state != null && state.Enabled;
                            bool protectedMod = IsProtected(mod);
                            Widgets.Checkbox(checkRect.position, ref checkedValue, checkRect.width, protectedMod);
                            if (!protectedMod && state != null && checkedValue != state.Enabled)
                            {
                                state.Enabled = checkedValue;
                                RefreshVisibleMods();
                            }
                
                            Rect nameRect = new Rect(checkRect.xMax + 5f, rowRect.y,
                                Mathf.Max(0f, rowRect.xMax - checkRect.xMax - 5f), rowRect.height);
                            string name = GetDisplayName(mod);
                            ModRowIssue rowIssue = rowIsVisible ? GetModRowIssue(mod) : ModRowIssue.None;
                            bool conflictingMod = rowIsVisible &&
                                IsModActiveInManager(mod) && HasActiveIncompatibility(mod);
                            bool outdatedForGameVersion = rowIsVisible &&
                                IsModActiveInManager(mod) && IsModOutdatedForCurrentGameVersion(mod);

                            // 시스템 경고를 먼저 배치하고, 사용자 태그는 남은 공간에 표시한다.
                            List<ModWarningTag> warningTags = new List<ModWarningTag>();
                            if (rowIssue == ModRowIssue.RequiredMod)
                                warningTags.Add(new ModWarningTag(ManagerTranslation.Get("LMM_0204"),
                                    new Color(1f, 0.28f, 0.28f, 1f), ManagerTranslation.Get("LMM_0145")));
                            if (conflictingMod)
                                warningTags.Add(new ModWarningTag(ManagerTranslation.Get("LMM_0205"),
                                    new Color(1f, 0.415f, 0.26f, 1f), "활성 모드와 충돌하는 모드입니다."));
                            if (rowIssue == ModRowIssue.LoadOrder)
                                warningTags.Add(new ModWarningTag(ManagerTranslation.Get("LMM_0206"),
                                    new Color(1f, 0.55f, 0.24f, 1f), ManagerTranslation.Get("LMM_0143")));
                            if (rowIssue == ModRowIssue.Version)
                                warningTags.Add(new ModWarningTag(ManagerTranslation.Get("LMM_0207"),
                                    new Color(1f, 0.685f, 0.22f, 1f), ManagerTranslation.Get("LMM_0146")));
                            if (outdatedForGameVersion)
                                warningTags.Add(new ModWarningTag(ManagerTranslation.Get("LMM_0208"),
                                    new Color(1f, 0.82f, 0.2f, 1f),
                                    "이 모드는 현재 게임 버전보다 낮은 버전만 지원합니다. 지원 버전: " +
                                    GetSupportedGameVersionText(mod)));

                            ManagerModData modData = FindModState(mod.PackageId);
                            List<ManagerTagData> assignedTags = modData == null || modData.TagIds == null
                                ? new List<ManagerTagData>()
                                : modData.TagIds.Select(FindTag).Where(tag => tag != null).ToList();

                            float maximumTagArea = nameRect.width * 0.55f;
                            float fullWarningWidth = warningTags.Sum(tag => GetWarningChipWidth(tag.Label) + 3f);
                            if (warningTags.Count > 0)
                                fullWarningWidth -= 3f;
                            float fullUserTagWidth = assignedTags.Sum(tag => GetTagChipWidth(tag.Name) + 3f);
                            if (assignedTags.Count > 0)
                                fullUserTagWidth -= 3f;
                            float fullCombinedWidth = fullWarningWidth + fullUserTagWidth +
                                (warningTags.Count > 0 && assignedTags.Count > 0 ? 3f : 0f);
                            bool needsOverflowChip = fullCombinedWidth > maximumTagArea;
                            float ellipsisWidth = needsOverflowChip
                                ? Mathf.Min(22f, maximumTagArea)
                                : 0f;
                            float chipsCapacity = Mathf.Max(0f, maximumTagArea - ellipsisWidth);
                            List<ModWarningTag> visibleWarnings = new List<ModWarningTag>();
                            float visibleWarningWidth = 0f;
                            for (int i = 0; i < warningTags.Count; i++)
                            {
                                float chipWidth = GetWarningChipWidth(warningTags[i].Label);
                                float nextWidth = visibleWarningWidth +
                                    (visibleWarnings.Count > 0 ? 3f : 0f) + chipWidth;
                                if (nextWidth > chipsCapacity)
                                    break;
                                visibleWarnings.Add(warningTags[i]);
                                visibleWarningWidth = nextWidth;
                            }

                            List<ManagerTagData> visibleTags = new List<ManagerTagData>();
                            float visibleTagWidth = 0f;
                            if (visibleWarnings.Count == warningTags.Count)
                            {
                                for (int i = 0; i < assignedTags.Count; i++)
                                {
                                    float chipWidth = GetTagChipWidth(assignedTags[i].Name);
                                    float nextWidth = visibleWarningWidth +
                                        (visibleTags.Count > 0 || visibleWarnings.Count > 0 ? 3f : 0f) +
                                        visibleTagWidth + chipWidth;
                                    if (nextWidth > chipsCapacity)
                                        break;
                                    visibleTags.Add(assignedTags[i]);
                                    visibleTagWidth += (visibleTags.Count > 1 ? 3f : 0f) + chipWidth;
                                }
                            }

                            bool hasTagOverflow = visibleWarnings.Count < warningTags.Count ||
                                visibleTags.Count < assignedTags.Count;
                            float chipContentWidth = visibleWarningWidth + visibleTagWidth +
                                (visibleWarnings.Count > 0 && visibleTags.Count > 0 ? 3f : 0f);
                            float tagAreaWidth = chipContentWidth +
                                (hasTagOverflow && ellipsisWidth > 0f
                                    ? (chipContentWidth > 0f ? 3f : 0f) + ellipsisWidth
                                    : 0f);

                            if (tagAreaWidth > 0f)
                            {
                                float tagsX = nameRect.xMax - tagAreaWidth;
                                nameRect.width = Mathf.Max(0f, tagsX - nameRect.x - 4f);
                                float chipX = tagsX;

                                // 사용자 태그를 먼저 배치하고, 경고 태그는 그 오른쪽 끝에 표시한다.
                                for (int i = 0; i < visibleTags.Count; i++)
                                {
                                    float chipWidth = GetTagChipWidth(visibleTags[i].Name);
                                    DrawTagChip(new Rect(chipX, rowRect.y + 3f, chipWidth, 21f), visibleTags[i]);
                                    chipX += chipWidth + 3f;
                                }

                                for (int i = 0; i < visibleWarnings.Count; i++)
                                {
                                    float chipWidth = GetWarningChipWidth(visibleWarnings[i].Label);
                                    Rect chipRect = new Rect(chipX, rowRect.y + 3f, chipWidth, 21f);
                                    DrawWarningTagChip(chipRect, visibleWarnings[i]);
                                    chipX += chipWidth + 3f;
                                }

                                if (hasTagOverflow && ellipsisWidth > 0f)
                                {
                                    Rect moreRect = new Rect(chipX, rowRect.y + 3f,
                                        ellipsisWidth, 21f);
                                    DrawTagOverflowChip(moreRect);
                                    string overflowTooltip = string.Join(", ",
                                        warningTags.Select(tag => tag.Tooltip)
                                            .Concat(assignedTags.Select(tag => tag.Name)));
                                    TooltipHandler.TipRegion(moreRect, overflowTooltip);
                                }
                            }
                            if (Widgets.ButtonInvisible(nameRect))
                                SelectModByPackageId(mod.PackageId, Event.current.control, Event.current.shift);

                            string rowName = protectedMod ? name + ManagerTranslation.Get("LMM_0001") : name;
                            Color rowTextColor = ManagerTagColors.Foreground(finalRowColor);
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

        private struct ModWarningTag
        {
            internal readonly string Label;
            internal readonly Color Color;
            internal readonly string Tooltip;

            internal ModWarningTag(string label, Color color, string tooltip)
            {
                Label = label;
                Color = color;
                Tooltip = tooltip;
            }
        }

        private static float GetWarningChipWidth(string label)
        {
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float width = Mathf.Clamp(Text.CalcSize(label ?? string.Empty).x + 12f, 34f, 112f);
            Text.Font = oldFont;
            return width;
        }

        private static void DrawWarningTagChip(Rect rect, ModWarningTag warning)
        {
            Widgets.DrawBoxSolid(rect, Color.black);
            Color oldColor = GUI.color;
            GUI.color = warning.Color;
            Widgets.DrawBox(rect, 1);
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height), warning.Label);
            Text.Font = oldFont;
            GUI.color = oldColor;
            TooltipHandler.TipRegion(rect, warning.Tooltip);
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
                            Color oldColor = GUI.color;
                            GUI.color = Color.white;
                            Widgets.DrawBox(rect);
                            GameFont oldFont = Text.Font;
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
    }
}
