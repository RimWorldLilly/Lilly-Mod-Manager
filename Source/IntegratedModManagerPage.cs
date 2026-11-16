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
        private enum ModRowIssue
        {
            None,
            RequiredMod,
            Version,
            LoadOrder
        }

        private const float RowHeight = 27f;
        private const float BottomHeight = 30f;
        private const float DetailsPreviewMaxHeight = 320f;
        

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
        private readonly HashSet<string> loadOrderIssuePackageIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> activeLoadPositions =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> loadAfterCache =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> loadBeforeCache =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ulong> workshopIdCache =
            new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ModRowIssue> modRowIssueCache =
            new Dictionary<string, ModRowIssue>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> modLoadFolderIssuesCache =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private string currentCategoryId;
        private string currentTagId;
        private string primarySelectedPackageId;
        

        // 새 모드 검색 간격 (초)
private const float InstalledModRefreshInterval = 10f;

// 다음 모드 검색 시간
private float nextInstalledModRefreshTime;
        private int lastSelectedIndex = -1;
        private string currentFolderId;
        private string searchText = "";
        private bool filterActive;
        private bool filterInactive;
        private bool filterSubscribed;
        private bool filterUntagged;
        private bool filterUncategorized;
        private bool filterUseAnd = true;
        private bool tagMode;
        private bool displayFulfilledRequirements;
        private bool modListHasKeyboardFocus;
        private Vector2 categoryTabScroll;
        private Vector2 tagTabScroll;
        private Vector2 modListScroll;
        private Vector2 descriptionScroll;
        private Vector2 workshopGalleryScroll;
        private string workshopGalleryPackageId;
        private Vector2 detailsScroll;
        private float modListViewportHeight;
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


bool persistGeneratedColors =
    settings.Tags == null ||
    settings.Tags.Count == 0 ||
    settings.Tags.Any(tag =>
        tag != null && !ManagerTagColors.IsValidHex(tag.ColorHex));

settings.EnsureInitialized();

if (persistGeneratedColors)
    IntegratedModManagerMod.Instance.WriteSettings();


            categories = settings.Categories.Select(category => category.Copy()).ToList();
            tags = settings.Tags.Select(tag => tag.Copy()).ToList();
            modStates = settings.Mods.Select(mod => mod.Copy()).ToList();
            // presets = settings.Presets.Select(preset => preset.Copy()).ToList();
            // 개별 XML 프리셋 읽기 및 기존 프리셋 자동 분리
presets = ManagerPresetStorage.LoadAndMigrate(settings);


foreach (ModMetaData mod in ModLister.AllInstalledMods)
{
    if (mod != null && !mod.PackageId.NullOrEmpty())
        modsByPackageId[mod.PackageId] = mod;
}

// 저장된 카테고리/모드 데이터에 없는 설치 모드 등록
AddInstalledModsMissingFromPreset();

// 변경된 데이터 저장
PersistCategoryData();
PersistTagData();

// 다음 신규 모드 검색 시간 설정
nextInstalledModRefreshTime =
    Time.realtimeSinceStartup + InstalledModRefreshInterval;

            currentCategoryId = categories.FirstOrDefault()?.Id;
            currentTagId = tags.FirstOrDefault()?.Id;
            EnsureCurrentSelectionValid();
            RefreshVisibleMods();
        }


        public override void DoWindowContents(Rect inRect)
        {
            // 신규 설치 모드 확인
            RefreshInstalledModsIfNeeded();

            Widgets.DrawBoxSolid(inRect, Color.black);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(20f, 10f, inRect.width - 470f, 36f),
                "Lilly - Integrated Mod Manager");
            Text.Font = GameFont.Small;
            float contentBottom = inRect.height - BottomHeight - 20f;
            float sidebarWidth = inRect.width * 0.25f;
            float mainX = 20f + sidebarWidth + 12f;
            float mainWidth = Mathf.Max(100f, inRect.width - mainX - 20f);

            const float searchTop = 48f;
            const float searchHeight = 30f;
            const float sortButtonWidth = 130f;
            const float searchGap = 8f;
            if (Widgets.ButtonText(new Rect(20f, searchTop, sortButtonWidth, searchHeight),
                ManagerTranslation.Get("LMM_0067")))
                SortAllCategories();
            Rect searchRect = new Rect(20f + sortButtonWidth + searchGap, searchTop,
                Mathf.Max(100f, inRect.width - 40f - sortButtonWidth - searchGap), searchHeight);
            float sectionTop = searchRect.yMax + 8f;
            DrawCategoryTabs(new Rect(20f, sectionTop, sidebarWidth,
                contentBottom - sectionTop));

            float contentTop = searchRect.yMax + 8f;
            float contentHeight = Mathf.Max(100f, contentBottom - contentTop);
            float listWidth = Mathf.Min(mainWidth - 120f, inRect.width * 0.25f);
            Rect listRect = new Rect(mainX, contentTop, listWidth, contentHeight);
            Rect detailsRect = new Rect(mainX + listWidth + 10f, contentTop,
                mainWidth - listWidth - 10f, contentHeight);

            if (Event.current.type == EventType.MouseDown)
                modListHasKeyboardFocus = listRect.Contains(Event.current.mousePosition);
            DrawSearchField(searchRect);

            DrawModList(listRect);
            DrawDetails(detailsRect);
            DrawModDragPreview(inRect);
            DrawSidebarDragPreview(inRect);
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
            string[] filterLabels =
            {
                ManagerTranslation.Get("LMM_0115"),
                ManagerTranslation.Get("LMM_0112"),
                ManagerTranslation.Get("LMM_0110"),
                ManagerTranslation.Get("LMM_0114"),
                ManagerTranslation.Get("LMM_0111")
            };
            float[] filterWidths = filterLabels.Select(label => Text.CalcSize(label).x + 28f).ToArray();
            float radioWidth = Text.CalcSize(ManagerTranslation.Get("LMM_0140")).x +
                Text.CalcSize(ManagerTranslation.Get("LMM_0141")).x + 54f;
            float filterWidth = filterWidths.Sum() + 6f * (filterWidths.Length - 1) + radioWidth + 8f;
            float searchWidth = Mathf.Max(120f, rect.width - 54f - filterWidth - 12f);
            bool hasSearchText = !searchText.NullOrEmpty();
            float clearButtonWidth = hasSearchText ? 34f : 0f;
            string newSearchText = Widgets.TextField(
                new Rect(rect.x + 54f, rect.y, searchWidth - clearButtonWidth, rect.height),
                searchText);
            if (!string.Equals(newSearchText, searchText, StringComparison.Ordinal))
            {
                searchText = newSearchText;
                selectedPackageIds.Clear();
                primarySelectedPackageId = null;
                lastSelectedIndex = -1;
                RefreshVisibleMods();
            }

            if (hasSearchText &&
                Widgets.ButtonText(new Rect(rect.x + 54f + searchWidth - clearButtonWidth,
                    rect.y, clearButtonWidth, rect.height), "×"))
            {
                searchText = "";
                selectedPackageIds.Clear();
                primarySelectedPackageId = null;
                lastSelectedIndex = -1;
                RefreshVisibleMods();
            }

            float filterX = rect.x + 54f + searchWidth + 12f;
            bool filtersChanged = false;
            bool activeChanged = DrawFilterCheckbox(ref filterActive, filterLabels[0],
                new Rect(filterX, rect.y, filterWidths[0], rect.height));
            filtersChanged |= activeChanged;
            filterX += filterWidths[0] + 6f;
            bool inactiveChanged = DrawFilterCheckbox(ref filterInactive, filterLabels[1],
                new Rect(filterX, rect.y, filterWidths[1], rect.height));
            filtersChanged |= inactiveChanged;
            filterX += filterWidths[1] + 6f;
            filtersChanged |= DrawFilterCheckbox(ref filterSubscribed, filterLabels[2],
                new Rect(filterX, rect.y, filterWidths[2], rect.height));
            filterX += filterWidths[2] + 6f;
            filtersChanged |= DrawFilterCheckbox(ref filterUntagged, filterLabels[3],
                new Rect(filterX, rect.y, filterWidths[3], rect.height));
            filterX += filterWidths[3] + 6f;
            filtersChanged |= DrawFilterCheckbox(ref filterUncategorized, filterLabels[4],
                new Rect(filterX, rect.y, filterWidths[4], rect.height));
            filterX += filterWidths[4] + 8f;

            bool priorFilterUseAnd = filterUseAnd;
            float andWidth = Text.CalcSize(ManagerTranslation.Get("LMM_0140")).x + 20f;
            float orWidth = Text.CalcSize(ManagerTranslation.Get("LMM_0141")).x + 20f;
            if (Widgets.RadioButtonLabeled(new Rect(filterX, rect.y, andWidth, rect.height),
                ManagerTranslation.Get("LMM_0140"), filterUseAnd))
                filterUseAnd = true;
            filterX += andWidth;
            if (Widgets.RadioButtonLabeled(new Rect(filterX, rect.y, orWidth, rect.height),
                ManagerTranslation.Get("LMM_0141"), !filterUseAnd))
                filterUseAnd = false;
            filtersChanged |= priorFilterUseAnd != filterUseAnd;

            if (filtersChanged)
            {
                if (activeChanged && filterActive)
                    filterInactive = false;
                if (inactiveChanged && filterInactive)
                    filterActive = false;
                RefreshVisibleMods();
            }
        }

        private static bool DrawFilterCheckbox(ref bool value, string label, Rect rect)
        {
            bool previous = value;
            Widgets.CheckboxLabeled(rect, label, ref value);
            return previous != value;
        }


// 대각선 그라데이션 텍스처 캐시
private static readonly Dictionary<string, Texture2D>
    diagonalGradientCache = new Dictionary<string, Texture2D>();

// 좌상단 기본색 → 우하단 사용자 지정색
private static void DrawDiagonalGradient(
    Rect rect,
    Color startColor,
    Color endColor)
{
    Color32 start = startColor;
    Color32 end = endColor;

    string key =
        start.r + "_" + start.g + "_" + start.b + "_" +
        end.r + "_" + end.g + "_" + end.b;

    if (!diagonalGradientCache.TryGetValue(key, out Texture2D texture))
    {
        const int size = 64;

        texture = new Texture2D(
            size,
            size,
            TextureFormat.RGBA32,
            false);

        texture.name = "LillyModManager_DiagonalGradient";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.hideFlags = HideFlags.HideAndDontSave;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 좌상단은 0, 우하단은 1
                float t = (x + (size - 1 - y)) /
                    (float)(2 * (size - 1));

                texture.SetPixel(
                    x,
                    y,
                    Color.Lerp(startColor, endColor, t));
            }
        }

        texture.Apply(false, true);
        diagonalGradientCache.Add(key, texture);
    }

    GUI.DrawTexture(
        rect,
        texture,
        ScaleMode.StretchToFill,
        false);
}


        private void DrawCategoryTabs(Rect rect)
        {
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
            float contentHeight = Mathf.Max(listRect.height, categories.Count * 34f);
            float scrollbarWidth = contentHeight > listRect.height
                ? ManagerUiMetrics.VerticalScrollbarWidth : 0f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - scrollbarWidth, contentHeight);
            Widgets.BeginScrollView(listRect, ref categoryTabScroll, viewRect);
            for (int i = 0; i < categories.Count; i++)
            {
                ManagerCategoryData category = categories[i];
                Rect rowRect = new Rect(0f, i * 30f, viewRect.width, 30f);

// 기존 검정/회색 교차 배경색
Color baseRowColor = i % 2 == 0
    ? new Color(0.09f, 0.09f, 0.09f, 1f)
    : new Color(0.055f, 0.055f, 0.055f, 1f);

// 사용자 지정색이 있으면 대각선 그라데이션
if (ManagerTagColors.IsValidHex(category.ColorHex))
{
    DrawDiagonalGradient(
        rowRect,
        baseRowColor,
        ManagerTagColors.Background(category.ColorHex));
}
else
{
    Widgets.DrawBoxSolid(rowRect, baseRowColor);
}

// 선택된 카테고리 강조
if (category.Id == currentCategoryId)
{
    Rect selectedRect = new Rect(
        rowRect.x,
        rowRect.y,
        rowRect.width * 2f / 3f,
        rowRect.height);

    Widgets.DrawBoxSolid(
        selectedRect,
        new Color(0.18f, 0.30f, 0.48f, 0.85f));

    Widgets.DrawBox(selectedRect, 2);
}


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
                if (isDraggingMod && Event.current.type == EventType.MouseUp &&
                    rowRect.Contains(Event.current.mousePosition))
                {
                    MoveSelectionTo(category);
                    EndModDrag();
                    Event.current.Use();
                }
                Rect enabledRect = new Rect(rowRect.x + 3f, rowRect.y + 4f, 22f, 22f);
                bool enabled = category.Enabled;
                Widgets.Checkbox(enabledRect.position, ref enabled);
                if (enabled != category.Enabled)
                {
                    category.Enabled = enabled;
                    PersistCategoryData();
                    RefreshVisibleMods();
                }

                Rect editRect = new Rect(rowRect.xMax - 52f, rowRect.y + 3f, 22f, 24f);
                Rect deleteRect = new Rect(rowRect.xMax - 27f, rowRect.y + 3f, 22f, 24f);
                Rect labelRect = new Rect(enabledRect.xMax + 4f, rowRect.y,
                    Mathf.Max(24f, editRect.x - enabledRect.xMax - 7f), 32f);
                string label = category.Name + " (" +
    GetActiveCount(category) + "/" +
    GetInstalledCount(category) + ")";


Color previousLabelColor = GUI.color;

// 선후행 오류가 있으면 빨간색 유지
if (HasCategoryLoadOrderIssue(category))
{
    GUI.color = new Color(1f, 0.28f, 0.28f, 1f);
}

Widgets.Label(labelRect, label);

GUI.color = previousLabelColor;


                TooltipHandler.TipRegion(labelRect, label);
                HandleCategoryTabReorder(category, labelRect);
                if (Widgets.ButtonText(editRect, "E"))
                    OpenRenameCategoryDialog(category);
                TooltipHandler.TipRegion(editRect, ManagerTranslation.Get("LMM_0125"));
                if (Widgets.ButtonText(deleteRect, "×"))
                    ConfirmDeleteCategory(category);
                TooltipHandler.TipRegion(deleteRect, ManagerTranslation.Get("LMM_0045"));

// 검색 결과가 없는 카테고리 회색 처리
if (!searchText.NullOrEmpty() &&
    !MatchesCategorySearch(category))
{
    Widgets.DrawBoxSolid(
        rowRect,
        new Color(0.45f, 0.45f, 0.45f, 0.55f));
}

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



private bool MatchesTagSearch(ManagerTagData tag)
{
    if (tag == null)
        return false;

    if (searchText.NullOrEmpty())
        return true;

    if (tag.Folders == null)
        return false;

    foreach (ManagerFolderData folder in tag.Folders)
    {
        if (folder == null || folder.ModIds == null)
            continue;

        foreach (string packageId in folder.ModIds)
        {
            ModMetaData mod = FindMod(packageId);

            if (mod != null && MatchesSearch(mod))
                return true;
        }
    }

    return false;
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

        private void DrawTagTabs(Rect rect)
        {
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
            float contentHeight = Mathf.Max(listRect.height, tags.Count * 34f);
            float scrollbarWidth = contentHeight > listRect.height
                ? ManagerUiMetrics.VerticalScrollbarWidth : 0f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - scrollbarWidth, contentHeight);
            Widgets.BeginScrollView(listRect, ref tagTabScroll, viewRect);
            int rowIndex = 0;
            for (int i = 0; i < tags.Count; i++)
            {
                ManagerTagData tag = tags[i];
                if (tag == null)
                    continue;
                Rect rowRect = new Rect(0f, rowIndex * 30f, viewRect.width, 30f);


// 기존 검정/회색 교차 배경색
Color baseRowColor = rowIndex % 2 == 0
    ? new Color(0.09f, 0.09f, 0.09f, 1f)
    : new Color(0.055f, 0.055f, 0.055f, 1f);

// 사용자 지정색이 있으면 대각선 그라데이션
if (ManagerTagColors.IsValidHex(tag.ColorHex))
{
    DrawDiagonalGradient(
        rowRect,
        baseRowColor,
        ManagerTagColors.Background(tag.ColorHex));
}
else
{
    Widgets.DrawBoxSolid(rowRect, baseRowColor);
}

// 선택된 태그 강조
if (tag.Id == currentTagId)
{
    Rect selectedRect = new Rect(
        rowRect.x,
        rowRect.y,
        rowRect.width * 2f / 3f,
        rowRect.height);

    Widgets.DrawBoxSolid(
        selectedRect,
        new Color(0.18f, 0.30f, 0.48f, 0.85f));

    Widgets.DrawBox(selectedRect, 2);
}



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
                Rect stateRect = new Rect(rowRect.x + 3f, rowRect.y + 4f, 26f, 22f);
                if (Widgets.ButtonText(stateRect, GetTagStateLabel(tag)))
                    ToggleTagEnabled(tag);

                Rect editRect = new Rect(rowRect.xMax - 52f, rowRect.y + 3f, 22f, 24f);
                Rect deleteRect = new Rect(rowRect.xMax - 27f, rowRect.y + 3f, 22f, 24f);
                if (tag.IsSystem)
                {
                    editRect = new Rect(rowRect.xMax - 27f, rowRect.y + 3f, 22f, 24f);
                    deleteRect = new Rect(rowRect.xMax + 2f, rowRect.y + 3f, 0f, 24f);
                }
                Rect labelRect = new Rect(stateRect.xMax + 4f, rowRect.y,
                    Mathf.Max(24f, editRect.x - stateRect.xMax - 7f), 32f);
                string label = tag.Name + " (" +
    GetActiveCount(tag) + "/" +
    GetInstalledCount(tag) + ")";

Widgets.Label(labelRect, label);


                TooltipHandler.TipRegion(labelRect, label);

                if (tag.IsSystem)
                {
                    if (Widgets.ButtonInvisible(labelRect))
                        SelectTag(tag);
                }
                else
                {
                    HandleTagTabReorder(tag, labelRect);
                    if (Widgets.ButtonText(editRect, "E"))
                        OpenRenameTagDialog(tag);
                    TooltipHandler.TipRegion(editRect, ManagerTranslation.Get("LMM_0126"));
                    if (Widgets.ButtonText(deleteRect, "×"))
                        ConfirmDeleteTag(tag);
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
            string label = tag.Name + " (" +
    GetActiveCount(tag) + "/" +
    GetInstalledCount(tag) + ")";
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
                    else
                        PersistCategoryData();
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
/// <summary>
/// 태그 모드에서 선택한 모드들을 다른 카테고리로 이동하는 메뉴
/// </summary>
private void OpenMoveToCategoryMenu()
{
    if (selectedPackageIds.Count == 0)
        return;

    List<FloatMenuOption> options = new List<FloatMenuOption>();

    for (int i = 0; i < categories.Count; i++)
    {
        ManagerCategoryData category = categories[i];

        if (category == null)
            continue;

        ManagerCategoryData target = category;

        options.Add(new FloatMenuOption(
            target.Name,
            () =>
            {
                MoveSelectionTo(target);
            }));
    }

    if (options.Count > 0)
    {
        Find.WindowStack.Add(new FloatMenu(options));
    }
}
        private void DrawModRow(ModMetaData mod, int visibleIndex, float width, float y)
        {
            Color previousGuiColor = GUI.color;
            GUI.color = Color.white;

            Rect rowRect = new Rect(0f, y, width, RowHeight);
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
Widgets.DrawBoxSolid(rowRect, finalRowColor);



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
                    SelectModByPackageId(mod.PackageId, Event.current.control, Event.current.shift);
                }
                else
                {
                    if (!selectedPackageIds.Contains(mod.PackageId))
                        SelectModByPackageId(mod.PackageId, false, false);
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
            if (rowIssue == ModRowIssue.RequiredMod)
                GUI.color = new Color(1f, 0.28f, 0.28f, 1f);
            else if (rowIssue == ModRowIssue.Version)
                GUI.color = new Color(1f, 0.82f, 0.2f, 1f);
            else if (rowIssue == ModRowIssue.LoadOrder)
                GUI.color = new Color(1f, 0.55f, 0.16f, 1f);
            if (rowIssue != ModRowIssue.None)
            {
                string tooltipKey = rowIssue == ModRowIssue.RequiredMod
                    ? "LMM_0145"
                    : rowIssue == ModRowIssue.Version
                        ? "LMM_0146"
                        : "LMM_0143";
                TooltipHandler.TipRegion(nameRect, ManagerTranslation.Get(tooltipKey));
            }
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
            if (!GetSupportedGameVersionText(mod).NullOrEmpty())
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
                    OpenCreateCategoryDialog(GetSelectedCategoryInsertIndex());
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

        private void SelectModByPackageId(string packageId, bool control, bool shift)
        {
            int index = visibleMods.FindIndex(mod => mod != null &&
                string.Equals(mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                SelectMod(index, control, shift);
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

        private void OpenCreateTagDialog(
            IEnumerable<string> packageIds = null,
            int? insertIndex = null)
        {
            List<string> selectedIds = packageIds == null
                ? new List<string>()
                : packageIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Find.WindowStack.Add(new ManagerTagSelectDialog(
                tags,
                tag =>
                {
                    if (selectedIds.Count > 0)
                        SetTagForSelection(selectedIds, tag, true);
                    else
                    {
                        tagMode = true;
                        SelectTag(tag);
                    }
                },
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
                    int targetIndex = insertIndex ?? GetSelectedTagInsertIndex();
                    if (targetIndex >= 0 && targetIndex <= tags.Count)
                        tags.Insert(targetIndex, tag);
                    else
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
        tag.ColorHex,
        (name, colorHex) =>
        {
            if (tags.Any(existing =>
                existing != tag &&
                string.Equals(
                    existing.Name,
                    name.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
            {
                Messages.Message(
                    ManagerTranslation.Get("LMM_0016"),
                    MessageTypeDefOf.RejectInput);

                return false;
            }

            // 이름 변경
            tag.Name = name.Trim();

            // 색상 변경
            tag.ColorHex = colorHex;

            // 영구 저장
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
private void OpenModColorDialog(List<string> packageIds)
{
    if (packageIds == null || packageIds.Count == 0)
        return;

    ManagerModData firstMod = FindModState(packageIds[0]);

    string initialColor = firstMod != null
        ? firstMod.ColorHex
        : null;

    Find.WindowStack.Add(new ManagerModColorDialog(
        initialColor,
        colorHex =>
        {
            foreach (string packageId in packageIds)
            {
                ManagerModData state = FindModState(packageId);

                if (state != null)
                    state.ColorHex = colorHex;
            }

            PersistTagData();
            RefreshVisibleMods();
        }));
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
if (tagMode)
{
    options.Add(new FloatMenuOption(
        ManagerTranslation.Get("LMM_0077"),
        () => OpenMoveToCategoryMenu()));
}
else
{
    options.Add(new FloatMenuOption(
        ManagerTranslation.Get("LMM_0077"),
        () => OpenMoveCategoryMenu()));
}
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

// 선택한 모드들의 배경색 변경
options.Add(new FloatMenuOption("모드 색상 변경",
    () => OpenModColorDialog(targets)));

// 선택한 모드들의 배경색 초기화
options.Add(new FloatMenuOption("모드 색상 초기화",
    () =>
    {
        foreach (string packageId in targets)
        {
            ManagerModData state = FindModState(packageId);
            if (state != null)
                state.ColorHex = null;
        }

        PersistTagData();
        RefreshVisibleMods();
    }));
            }

            List<string> precedingIds = GetPrecedingIds(mod);
            List<string> followingIds = GetFollowingIds(mod);
            if (precedingIds.Count > 0)
            {
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0154"),
                    () => MoveDependencyMods(mod, precedingIds, moveTargets: true, beforeSubject: true)));
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0155"),
                    () => MoveDependencyMods(mod, precedingIds, moveTargets: false, beforeSubject: true)));
            }
            if (followingIds.Count > 0)
            {
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0156"),
                    () => MoveDependencyMods(mod, followingIds, moveTargets: true, beforeSubject: false)));
                options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0157"),
                    () => MoveDependencyMods(mod, followingIds, moveTargets: false, beforeSubject: false)));
            }

            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0105"),
                () => OpenRenameDisplayNameDialog(mod)));
            options.Add(new FloatMenuOption(ManagerTranslation.Get("LMM_0025"),
                () =>
                {
                    ManagerModData state = FindModState(mod.PackageId);
                    if (state != null)
                    {
                        state.DisplayName = null;
                        PersistTagData();
                        RefreshVisibleMods();
                    }
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

        private List<string> GetPrecedingIds(ModMetaData mod)
        {
            List<string> constraints = GetDependencyIds(mod, new[]
                { "modDependencies", "ModDependencies", "Dependencies", "RequiredMods", "LoadAfter" });
            HashSet<string> constraintSet = new HashSet<string>(constraints,
                StringComparer.OrdinalIgnoreCase);
            return GetAllManagedPackageIds().Where(id => !string.Equals(id, mod.PackageId,
                    StringComparison.OrdinalIgnoreCase) && constraintSet.Contains(id) &&
                FindMod(id) != null && !IsProtected(FindMod(id))).ToList();
        }

        private List<string> GetFollowingIds(ModMetaData mod)
        {
            HashSet<string> following = new HashSet<string>(
                GetDependencyIds(mod, new[] { "LoadBefore" }),
                StringComparer.OrdinalIgnoreCase);
            List<string> allPackageIds = GetAllManagedPackageIds();
            foreach (string packageId in allPackageIds)
            {
                if (string.Equals(packageId, mod.PackageId, StringComparison.OrdinalIgnoreCase))
                    continue;
                ModMetaData candidate = FindMod(packageId);
                if (candidate == null || IsProtected(candidate))
                    continue;
                List<string> predecessors = GetDependencyIds(candidate, new[]
                    { "modDependencies", "ModDependencies", "Dependencies", "RequiredMods", "LoadAfter" });
                if (predecessors.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase))
                    following.Add(packageId);
            }
            return allPackageIds.Where(id => following.Contains(id) &&
                !string.Equals(id, mod.PackageId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private List<string> GetAllManagedPackageIds()
        {
            List<string> packageIds = new List<string>();
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
                        if (!packageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                            packageIds.Add(packageId);
                    }
                }
            }
            return packageIds;
        }

        private void MoveDependencyMods(
            ModMetaData subject,
            IList<string> targets,
            bool moveTargets,
            bool beforeSubject)
        {
            if (subject == null || IsProtected(subject))
                return;
            ManagerCategoryData subjectCategory = FindCategoryForPackage(subject.PackageId);
            ManagerFolderData subjectFolder = FindFolderForPackage(subjectCategory, subject.PackageId);
            if (subjectCategory == null || subjectFolder == null)
                return;

            HashSet<string> targetSet = new HashSet<string>(targets ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            targetSet.Remove(subject.PackageId);
            List<string> targetIds = (targets ?? new List<string>())
                .Where(id => !string.Equals(id, subject.PackageId,
                    StringComparison.OrdinalIgnoreCase) && targetSet.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<string> crossCategoryTargetIds = targetIds.Where(id =>
                FindCategoryForPackage(id) != subjectCategory).ToList();
            bool changed = false;

            if (crossCategoryTargetIds.Count > 0)
            {
                if (moveTargets)
                {
                    MovePackagesToFolder(crossCategoryTargetIds, subjectCategory, subjectFolder);
                    changed = true;
                }
                else
                {
                    string nearestTargetId = beforeSubject
                        ? crossCategoryTargetIds[crossCategoryTargetIds.Count - 1]
                        : crossCategoryTargetIds[0];
                    ManagerCategoryData destinationCategory = FindCategoryForPackage(nearestTargetId);
                    ManagerFolderData destinationFolder = FindFolderForPackage(
                        destinationCategory, nearestTargetId);
                    if (destinationCategory != null && destinationFolder != null)
                    {
                        MovePackagesToFolder(new[] { subject.PackageId },
                            destinationCategory, destinationFolder);
                        subjectCategory = destinationCategory;
                        subjectFolder = destinationFolder;
                        changed = true;
                    }
                }
            }

            List<ManagerFolderData> targetFolders = new List<ManagerFolderData>();
            List<string> sameFolderTargets = new List<string>();
            foreach (string targetId in targetIds)
            {
                if (FindCategoryForPackage(targetId) != subjectCategory)
                    continue;
                ManagerFolderData targetFolder = FindFolderForPackage(subjectCategory, targetId);
                if (targetFolder == null)
                    continue;
                if (targetFolder != subjectFolder)
                {
                    if (!targetFolders.Contains(targetFolder))
                        targetFolders.Add(targetFolder);
                }
                else
                    sameFolderTargets.Add(targetId);
            }
            if (targetFolders.Count > 0)
                changed |= ReorderContainers(subjectCategory.Folders, subjectFolder,
                    targetFolders, moveTargets, beforeSubject);
            if (sameFolderTargets.Count > 0)
                changed |= ReorderPackageIds(subjectFolder.ModIds, subject.PackageId,
                    sameFolderTargets, moveTargets, beforeSubject);
            if (changed)
            {
                PersistCategoryData();
                RefreshVisibleMods();
            }
        }

        private void MovePackagesToFolder(
            IEnumerable<string> packageIds,
            ManagerCategoryData destinationCategory,
            ManagerFolderData destinationFolder)
        {
            if (packageIds == null || destinationCategory == null || destinationFolder == null)
                return;

            foreach (string packageId in packageIds)
            {
                if (packageId.NullOrEmpty())
                    continue;
                for (int categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
                {
                    ManagerCategoryData category = categories[categoryIndex];
                    if (category == null)
                        continue;
                    if (category.ModIds != null)
                        category.ModIds.RemoveAll(id => string.Equals(id, packageId,
                            StringComparison.OrdinalIgnoreCase));
                    if (category.Folders == null)
                        continue;
                    for (int folderIndex = 0; folderIndex < category.Folders.Count; folderIndex++)
                    {
                        ManagerFolderData folder = category.Folders[folderIndex];
                        if (folder != null && folder.ModIds != null)
                            folder.ModIds.RemoveAll(id => string.Equals(id, packageId,
                                StringComparison.OrdinalIgnoreCase));
                    }
                }
                if (destinationCategory.ModIds != null &&
                    !destinationCategory.ModIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                    destinationCategory.ModIds.Add(packageId);
                if (destinationFolder.ModIds != null &&
                    !destinationFolder.ModIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                    destinationFolder.ModIds.Add(packageId);
            }
        }

        private static bool ReorderContainers<T>(
            List<T> items,
            T subject,
            IEnumerable<T> targets,
            bool moveTargets,
            bool beforeSubject) where T : class
        {
            HashSet<T> targetSet = new HashSet<T>(targets.Where(item => item != null && item != subject));
            List<T> movingTargets = items.Where(targetSet.Contains).ToList();
            if (movingTargets.Count == 0 || !items.Contains(subject))
                return false;

            if (moveTargets)
            {
                items.RemoveAll(targetSet.Contains);
                int subjectIndex = items.IndexOf(subject);
                int insertIndex = beforeSubject ? subjectIndex : subjectIndex + 1;
                items.InsertRange(Mathf.Clamp(insertIndex, 0, items.Count), movingTargets);
            }
            else
            {
                items.Remove(subject);
                List<int> targetIndexes = movingTargets.Select(item => items.IndexOf(item))
                    .Where(index => index >= 0).ToList();
                if (targetIndexes.Count == 0)
                    return false;
                int insertIndex = beforeSubject ? targetIndexes.Max() + 1 : targetIndexes.Min();
                items.Insert(Mathf.Clamp(insertIndex, 0, items.Count), subject);
            }
            return true;
        }

        private static bool ReorderPackageIds(
            List<string> items,
            string subject,
            IEnumerable<string> targets,
            bool moveTargets,
            bool beforeSubject)
        {
            HashSet<string> targetSet = new HashSet<string>(targets,
                StringComparer.OrdinalIgnoreCase);
            List<string> movingTargets = items.Where(targetSet.Contains).ToList();
            if (movingTargets.Count == 0 || !items.Contains(subject,
                StringComparer.OrdinalIgnoreCase))
                return false;

            if (moveTargets)
            {
                items.RemoveAll(targetSet.Contains);
                int subjectIndex = items.FindIndex(id => string.Equals(id, subject,
                    StringComparison.OrdinalIgnoreCase));
                int insertIndex = beforeSubject ? subjectIndex : subjectIndex + 1;
                items.InsertRange(Mathf.Clamp(insertIndex, 0, items.Count), movingTargets);
            }
            else
            {
                items.RemoveAll(id => string.Equals(id, subject, StringComparison.OrdinalIgnoreCase));
                List<int> targetIndexes = movingTargets.Select(id => items.FindIndex(item =>
                    string.Equals(item, id, StringComparison.OrdinalIgnoreCase)))
                    .Where(index => index >= 0).ToList();
                if (targetIndexes.Count == 0)
                    return false;
                int insertIndex = beforeSubject ? targetIndexes.Max() + 1 : targetIndexes.Min();
                items.Insert(Mathf.Clamp(insertIndex, 0, items.Count), subject);
            }
            return true;
        }

        private ManagerCategoryData FindCategoryForPackage(string packageId)
        {
            for (int categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
            {
                ManagerCategoryData category = categories[categoryIndex];
                if (FindFolderForPackage(category, packageId) != null)
                    return category;
            }
            return null;
        }

        private static ManagerFolderData FindFolderForPackage(
            ManagerCategoryData category,
            string packageId)
        {
            if (category == null || category.Folders == null)
                return null;
            for (int folderIndex = 0; folderIndex < category.Folders.Count; folderIndex++)
            {
                ManagerFolderData folder = category.Folders[folderIndex];
                if (folder != null && folder.ModIds != null && folder.ModIds.Any(id =>
                    string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase)))
                    return folder;
            }
            return null;
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

        private ulong GetCachedWorkshopId(ModMetaData mod)
        {
            if (mod == null || mod.PackageId.NullOrEmpty())
                return 0UL;
            ulong workshopId;
            if (workshopIdCache.TryGetValue(mod.PackageId, out workshopId))
                return workshopId;
            workshopId = GetWorkshopItemId(mod);
            workshopIdCache[mod.PackageId] = workshopId;
            return workshopId;
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

            MethodInfo getPublishedFileId = typeof(ModMetaData).GetMethod("GetPublishedFileId",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (getPublishedFileId != null)
            {
                try
                {
                    ulong publishedId = ParseWorkshopId(getPublishedFileId.Invoke(mod, null));
                    if (publishedId > 0UL)
                        return publishedId;
                }
                catch (Exception)
                {
                    // A Steamworks ID type may not be available outside Steam.
                }
            }
            string url = GetWorkshopUrl(mod);
            if (!url.NullOrEmpty())
            {
                Match match = Regex.Match(url, @"(?:id=|/)(\d{6,})", RegexOptions.IgnoreCase);
                ulong id;
                if (match.Success && ulong.TryParse(match.Groups[1].Value, out id))
                    return id;
            }

            if (mod.OnSteamWorkshop && !mod.FolderName.NullOrEmpty())
            {
                Match folderIdMatch = Regex.Match(mod.FolderName, @"^(\d{6,})$");
                ulong folderId;
                if (folderIdMatch.Success && ulong.TryParse(folderIdMatch.Groups[1].Value, out folderId))
                    return folderId;
            }
            return 0UL;
        }

        private static ulong ParseWorkshopId(object value)
        {
            if (value == null)
                return 0UL;
            ulong parsed;
            if (ulong.TryParse(value.ToString(), out parsed))
                return parsed;

            Type type = value.GetType();
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].Name.IndexOf("PublishedFile", StringComparison.OrdinalIgnoreCase) < 0 &&
                    fields[i].Name.IndexOf("Value", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                object fieldValue = fields[i].GetValue(value);
                if (fieldValue != null && ulong.TryParse(fieldValue.ToString(), out parsed))
                    return parsed;
            }
            return 0UL;
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
    state.ColorHex,
    (name, colorHex) =>
    {
        string trimmed = name == null
            ? string.Empty
            : name.Trim();

        state.DisplayName = trimmed.Length == 0
            ? null
            : trimmed;

        state.ColorHex = ManagerTagColors.IsValidHex(colorHex)
            ? colorHex
            : null;

        PersistTagData();
        RefreshVisibleMods();

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
            PersistCategoryData();
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
            PersistCategoryData();
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
                    PersistCategoryData();
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
                    PersistCategoryData();
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
                    PersistCategoryData();
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
            {
                SortFolderModIds(folderCurrent);
                PersistCategoryData();
            }
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
            PersistCategoryData();
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
                AddDependencyIds(mod, new[]
                    { "modDependencies", "ModDependencies", "Dependencies", "RequiredMods", "LoadAfter" },
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

        private static List<string> GetDependencyIds(
            ModMetaData mod,
            IEnumerable<string> propertyNames)
        {
            List<string> ids = new List<string>();
            AddDependencyIds(mod, propertyNames, id =>
            {
                if (!id.NullOrEmpty() && !ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                    ids.Add(id);
            });
            return ids;
        }

        private string FormatDependencyNames(IEnumerable<string> packageIds)
        {
            List<string> names = new List<string>();
            foreach (string packageId in packageIds)
            {
                ModMetaData dependency = FindMod(packageId);
                names.Add(dependency == null ? packageId : GetDisplayName(dependency));
            }
            return string.Join(", ", names);
        }

        private void DrawDependencyLinks(
            Rect bounds,
            ref float y,
            string label,
            ModMetaData subject,
            IList<string> packageIds,
            bool dependencyMustPrecedeSubject,
            bool mandatory)
        {
            const float lineHeight = 22f;
            float x = bounds.x;
            float labelWidth = Text.CalcSize(label).x;
            Widgets.Label(new Rect(x, y, labelWidth + 2f, lineHeight), label);
            x += labelWidth + 2f;

            for (int i = 0; i < packageIds.Count; i++)
            {
                ModMetaData dependency = FindMod(packageIds[i]);
                string name = dependency == null ? packageIds[i] : GetDisplayName(dependency);
                string segment = name + (i + 1 < packageIds.Count ? ", " : "");
                float width = Mathf.Max(16f, Text.CalcSize(segment).x);
                if (x + width > bounds.xMax && x > bounds.x)
                {
                    y += lineHeight;
                    x = bounds.x;
                }

                Rect linkRect = new Rect(x, y, Mathf.Min(width, bounds.xMax - x), lineHeight);
                Color oldColor = GUI.color;
                bool missing = dependency == null;
                bool hasVerdict = false;
                bool satisfied = !missing && IsLoadConstraintSatisfied(
                    subject, dependency, dependencyMustPrecedeSubject, out hasVerdict);
                GUI.color = missing
                    ? mandatory
                        ? new Color(1f, 0.3f, 0.3f, 1f)
                        : new Color(0.7f, 0.7f, 0.7f, 1f)
                    : satisfied
                        ? new Color(0.3f, 1f, 0.38f, 1f)
                        : hasVerdict
                            ? new Color(1f, 0.58f, 0.18f, 1f)
                            : new Color(0.7f, 0.7f, 0.7f, 1f);
                Widgets.Label(linkRect, segment);
                Widgets.DrawLineHorizontal(linkRect.x, linkRect.yMax - 2f,
                    Mathf.Min(Text.CalcSize(name).x, linkRect.width));
                GUI.color = oldColor;
                string statusKey = missing
                    ? mandatory ? "LMM_0149" : "LMM_0153"
                    : satisfied ? "LMM_0147" : hasVerdict ? "LMM_0148" : "LMM_0167";
                TooltipHandler.TipRegion(linkRect, ManagerTranslation.Get(statusKey) + packageIds[i]);
                if (Widgets.ButtonInvisible(linkRect))
                    SelectDependency(packageIds[i]);
                x += width;
            }

            y += lineHeight + 3f;
        }

        private void DrawIncompatibilityLinks(
            Rect bounds,
            ref float y,
            IList<ModRequirement> incompatibilities)
        {
            const float lineHeight = 22f;
            Widgets.Label(new Rect(bounds.x, y, bounds.width, lineHeight), "충돌");
            y += lineHeight;

            for (int i = 0; i < incompatibilities.Count; i++)
            {
                ModRequirement incompatibility = incompatibilities[i];
                if (incompatibility == null)
                    continue;

                List<string> packageIds = GetRequirementPackageIds(incompatibility);
                string displayName = ReadMetadataValue(incompatibility, "displayName") as string;
                if (displayName.NullOrEmpty())
                    displayName = packageIds.Count > 0
                        ? FormatDependencyNames(packageIds)
                        : incompatibility.RequirementTypeLabel;

                bool conflictActive = packageIds.Any(packageId =>
                    IsModActiveInManager(FindMod(packageId)));
                Rect itemRect = new Rect(bounds.x + 8f, y, bounds.width - 8f, lineHeight);
                Color oldColor = GUI.color;
                GUI.color = conflictActive
                    ? new Color(1f, 0.3f, 0.3f, 1f)
                    : new Color(0.7f, 0.7f, 0.7f, 1f);
                Widgets.Label(itemRect, (conflictActive ? "⚠ " : "  ") + displayName);
                GUI.color = oldColor;

                TooltipHandler.TipRegion(itemRect, conflictActive
                    ? "활성화된 충돌 모드입니다: " + displayName
                    : "충돌 모드가 비활성화되어 있습니다: " + displayName);

                if (packageIds.Count == 1 && Widgets.ButtonInvisible(itemRect))
                    SelectDependency(packageIds[0]);
                y += lineHeight;
            }
            y += 3f;
        }

        private bool IsLoadConstraintSatisfied(
            ModMetaData subject,
            ModMetaData dependency,
            bool dependencyMustPrecedeSubject,
            out bool hasVerdict)
        {
            hasVerdict = false;
            if (subject == null || dependency == null)
                return false;

            try
            {
                foreach (ModRequirement requirement in subject.GetRequirements())
                {
                    if (requirement == null)
                        continue;
                    if (RequirementMatchesPackage(requirement, dependency.PackageId) &&
                        !IsRequirementSatisfiedInManager(requirement))
                    {
                        hasVerdict = true;
                        return false;
                    }
                }
            }
            catch (Exception)
            {
                hasVerdict = true;
                return false;
            }

            // 선후행 순서 검사는 활성 모드만 대상으로 한다.
            int subjectPosition;
            int dependencyPosition;
            if (!activeLoadPositions.TryGetValue(subject.PackageId, out subjectPosition) ||
                !activeLoadPositions.TryGetValue(dependency.PackageId, out dependencyPosition))
                return false;

            hasVerdict = true;
            return dependencyMustPrecedeSubject
                ? dependencyPosition < subjectPosition
                : subjectPosition < dependencyPosition;
        }

        private bool IsRequirementSatisfiedInManager(ModRequirement requirement)
        {
            if (requirement == null)
                return true;

            bool incompatible = requirement is ModIncompatibility;
            List<string> packageIds = GetRequirementPackageIds(requirement);
            for (int i = 0; i < packageIds.Count; i++)
            {
                ModMetaData target = FindMod(packageIds[i]);
                bool active = IsModActiveInManager(target);
                if (incompatible)
                {
                    if (active)
                        return false;
                }
                else if (active)
                {
                    return true;
                }
            }
            return incompatible;
        }

        private bool IsModActiveInManager(ModMetaData mod)
        {
            if (mod == null || mod.PackageId.NullOrEmpty())
                return false;
            if (IsProtected(mod))
                return true;
            if (activeLoadPositions.ContainsKey(mod.PackageId))
                return true;

            // 관리 목록에 없는 모드는 실제 게임 활성 상태를 따른다.
            return FindCategoryForPackage(mod.PackageId) == null && mod.Active;
        }

        private static bool RequirementMatchesPackage(
            ModRequirement requirement,
            string packageId)
        {
            if (requirement == null || packageId.NullOrEmpty())
                return false;
            List<string> packageIds = GetRequirementPackageIds(requirement);
            for (int i = 0; i < packageIds.Count; i++)
            {
                if (string.Equals(packageIds[i], packageId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static List<string> GetRequirementPackageIds(ModRequirement requirement)
        {
            List<string> packageIds = new List<string>();
            if (requirement == null)
                return packageIds;
            if (!requirement.packageId.NullOrEmpty())
                packageIds.Add(requirement.packageId);
            if (requirement.alternativePackageIds != null)
            {
                for (int i = 0; i < requirement.alternativePackageIds.Count; i++)
                {
                    string packageId = requirement.alternativePackageIds[i];
                    if (!packageId.NullOrEmpty() &&
                        !packageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                        packageIds.Add(packageId);
                }
            }
            return packageIds;
        }

        private static string GetRequirementPackageId(ModRequirement requirement)
        {
            if (requirement == null)
                return null;
            foreach (string propertyName in new[]
                { "packageId", "PackageId", "packageIdPlayerFacing", "PackageIdPlayerFacing" })
            {
                string value = ReadMetadataValue(requirement, propertyName) as string;
                if (!value.NullOrEmpty())
                    return value;
            }
            return ReadDependencyPackageId(requirement);
        }

        private string GetMissingDependencyWorkshopUrl(
            ModMetaData sourceMod,
            ModRequirement requirement,
            string packageId)
        {
            string url = GetWorkshopUrlFromObject(requirement);
            if (!url.NullOrEmpty())
                return url;

            foreach (string propertyName in new[] { "modDependencies", "ModDependencies" })
            {
                object value = ReadMetadataValue(sourceMod, propertyName);
                if (!(value is IEnumerable dependencies) || value is string)
                    continue;
                foreach (object dependency in dependencies)
                {
                    if (dependency == null || !string.Equals(
                        ReadDependencyPackageId(dependency), packageId,
                        StringComparison.OrdinalIgnoreCase))
                        continue;
                    url = GetWorkshopUrlFromObject(dependency);
                    if (!url.NullOrEmpty())
                        return url;
                }
            }
            return string.Empty;
        }

        private static string GetWorkshopUrlFromObject(object value)
        {
            if (value == null)
                return string.Empty;
            foreach (string propertyName in new[]
                { "steamWorkshopUrl", "SteamWorkshopUrl", "workshopUrl", "WorkshopUrl", "WorkshopUri" })
            {
                object url = ReadMetadataValue(value, propertyName);
                string text = url == null ? null : url.ToString();
                if (!text.NullOrEmpty())
                    return text;
            }
            return string.Empty;
        }

        private void SelectDependency(string packageId)
        {
            if (packageId.NullOrEmpty() || FindMod(packageId) == null)
                return;

            ManagerCategoryData ownerCategory = null;
            ManagerFolderData ownerFolder = null;
            for (int categoryIndex = 0; categoryIndex < categories.Count && ownerFolder == null; categoryIndex++)
            {
                ManagerCategoryData category = categories[categoryIndex];
                for (int folderIndex = 0; category.Folders != null && folderIndex < category.Folders.Count; folderIndex++)
                {
                    ManagerFolderData folder = category.Folders[folderIndex];
                    if (folder.ModIds != null && folder.ModIds.Any(id =>
                        string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase)))
                    {
                        ownerCategory = category;
                        ownerFolder = folder;
                        break;
                    }
                }
            }

            if (ownerFolder != null)
            {
                tagMode = false;
                currentCategoryId = ownerCategory.Id;
                currentFolderId = ownerFolder.Id;
                ownerFolder.Collapsed = false;
                selectedPackageIds.Clear();
                primarySelectedPackageId = null;
                lastSelectedIndex = -1;
                RefreshVisibleMods();
                int visibleIndex = visibleMods.FindIndex(mod => string.Equals(
                    mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
                if (visibleIndex >= 0)
                {
                    SelectMod(visibleIndex, false, false);
                    float rowY = GetVisibleModRowY(packageId);
                    modListScroll.y = Mathf.Max(0f, rowY - RowHeight);
                    return;
                }
            }
            else
            {
                ManagerTagData ownerTag = null;
                ManagerFolderData ownerTagFolder = null;
                for (int tagIndex = 0; tagIndex < tags.Count && ownerTagFolder == null; tagIndex++)
                {
                    ManagerTagData tag = tags[tagIndex];
                    for (int folderIndex = 0; tag.Folders != null && folderIndex < tag.Folders.Count; folderIndex++)
                    {
                        ManagerFolderData folder = tag.Folders[folderIndex];
                        if (folder.ModIds != null && folder.ModIds.Any(id =>
                            string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase)))
                        {
                            ownerTag = tag;
                            ownerTagFolder = folder;
                            break;
                        }
                    }
                }
                if (ownerTagFolder != null)
                {
                    tagMode = true;
                    currentTagId = ownerTag.Id;
                    currentFolderId = ownerTagFolder.Id;
                    ownerTagFolder.Collapsed = false;
                    selectedPackageIds.Clear();
                    primarySelectedPackageId = null;
                    lastSelectedIndex = -1;
                    RefreshVisibleMods();
                    int visibleIndex = visibleMods.FindIndex(mod => string.Equals(
                        mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
                    if (visibleIndex >= 0)
                    {
                        SelectMod(visibleIndex, false, false);
                        float rowY = GetVisibleModRowY(packageId);
                        modListScroll.y = Mathf.Max(0f, rowY - RowHeight);
                        return;
                    }
                }
            }

            selectedPackageIds.Clear();
            primarySelectedPackageId = packageId;
            lastSelectedIndex = -1;
        }

        private ModRowIssue GetModRowIssue(ModMetaData mod)
        {
            if (mod == null || mod.PackageId.NullOrEmpty())
                return ModRowIssue.None;

            // 행 강조(선행/후행 포함)는 활성 모드만 대상으로 한다.
            if (!IsModActiveInManager(mod))
                return ModRowIssue.None;

            ModRowIssue cached;
            if (modRowIssueCache.TryGetValue(mod.PackageId, out cached))
                return cached == ModRowIssue.None && IsLoadOrderCheckTarget(mod) &&
                    loadOrderIssuePackageIds.Contains(mod.PackageId)
                    ? ModRowIssue.LoadOrder
                    : cached;

            ModRowIssue issue = ModRowIssue.None;
            try
            {
                foreach (ModRequirement requirement in mod.GetRequirements())
                {
                    if (requirement == null || IsRequirementSatisfiedInManager(requirement))
                        continue;
                    string requirementType = requirement.RequirementTypeLabel ?? string.Empty;
                    string tooltip = requirement.Tooltip ?? string.Empty;
                    bool isVersionIssue = requirementType.IndexOf("version",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                        requirementType.IndexOf("踰꾩쟾", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        tooltip.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        tooltip.IndexOf("踰꾩쟾", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isVersionIssue)
                        issue = ModRowIssue.Version;
                    else if (issue != ModRowIssue.Version)
                        issue = ModRowIssue.RequiredMod;
                }
            }
            catch (Exception)
            {
                // Keep the row usable if game metadata contains an invalid requirement.
            }

            modRowIssueCache[mod.PackageId] = issue;
            return issue == ModRowIssue.None && IsLoadOrderCheckTarget(mod) &&
                loadOrderIssuePackageIds.Contains(mod.PackageId)
                ? ModRowIssue.LoadOrder
                : issue;
        }

        private bool IsLoadOrderCheckTarget(ModMetaData mod)
        {
            return mod != null && !mod.PackageId.NullOrEmpty() &&
                activeLoadPositions.ContainsKey(mod.PackageId);
        }

        private void RebuildLoadOrderIssueIds()
        {
            loadOrderIssuePackageIds.Clear();
            activeLoadPositions.Clear();
            modRowIssueCache.Clear();
            List<string> activeOrder = new List<string>();
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
                        ManagerModData state = FindModState(packageId);
                        if (mod != null && (IsProtected(mod) ||
                            (category.Enabled && state != null && state.Enabled)) &&
                            !activeOrder.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase))
                            activeOrder.Add(mod.PackageId);
                    }
                }
            }

            for (int i = 0; i < activeOrder.Count; i++)
                activeLoadPositions[activeOrder[i]] = i;

            for (int i = 0; i < activeOrder.Count; i++)
            {
                string packageId = activeOrder[i];
                ModMetaData mod = FindMod(packageId);
                if (mod == null)
                    continue;

                List<string> loadAfter;
                if (!loadAfterCache.TryGetValue(packageId, out loadAfter))
                {
                    loadAfter = GetDependencyIds(mod,
                        new[] { "modDependencies", "ModDependencies", "Dependencies", "RequiredMods", "LoadAfter" });
                    loadAfterCache[packageId] = loadAfter;
                }
                for (int dependencyIndex = 0; dependencyIndex < loadAfter.Count; dependencyIndex++)
                {
                    int dependencyPosition;
                    if (activeLoadPositions.TryGetValue(loadAfter[dependencyIndex], out dependencyPosition) &&
                        dependencyPosition > i)
                    {
                        loadOrderIssuePackageIds.Add(packageId);
                        loadOrderIssuePackageIds.Add(loadAfter[dependencyIndex]);
                    }
                }

                List<string> loadBefore;
                if (!loadBeforeCache.TryGetValue(packageId, out loadBefore))
                {
                    loadBefore = GetDependencyIds(mod, new[] { "LoadBefore" });
                    loadBeforeCache[packageId] = loadBefore;
                }
                for (int dependencyIndex = 0; dependencyIndex < loadBefore.Count; dependencyIndex++)
                {
                    int dependencyPosition;
                    if (activeLoadPositions.TryGetValue(loadBefore[dependencyIndex], out dependencyPosition) &&
                        dependencyPosition < i)
                    {
                        loadOrderIssuePackageIds.Add(packageId);
                        loadOrderIssuePackageIds.Add(loadBefore[dependencyIndex]);
                    }
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
            Find.WindowStack.Add(new ManagerPresetSaveDialog(
                presets,
                SavePreset,
                SaveVanillaModList,
                ConfirmDeletePreset));
        }

        private void SaveVanillaModList(string requestedName, Action onSaved)
        {
            string name = SanitizeModListName(requestedName);
            if (name.NullOrEmpty())
            {
                Messages.Message(ManagerTranslation.Get("LMM_0064"),
                    MessageTypeDefOf.RejectInput);
                return;
            }

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
                    foreach (string packageId in folder.ModIds)
                    {
                        ModMetaData mod = FindMod(packageId);
                        ManagerModData state = FindModState(packageId);
                        if (mod != null && (IsProtected(mod) ||
                            (category.Enabled && state != null && state.Enabled)))
                            activePackageIds.Add(mod.PackageId);
                    }
                }
            }
            activePackageIds = OrderByDependencies(activePackageIds);

            FileInfo existingFile = GenFilePaths.AllModListFiles.FirstOrDefault(file =>
                string.Equals(Path.GetFileNameWithoutExtension(file.Name), name,
                    StringComparison.OrdinalIgnoreCase));
            if (existingFile != null)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    ManagerTranslation.Get("LMM_0162") + name,
                    ManagerTranslation.Get("LMM_0134"),
                    () => WriteVanillaModList(name, activePackageIds, onSaved),
                    ManagerTranslation.Get("LMM_0074"), null,
                    null, false, null, null, WindowLayer.Dialog));
                return;
            }

            WriteVanillaModList(name, activePackageIds, onSaved);
        }

        private static string SanitizeModListName(string name)
        {
            if (name.NullOrEmpty())
                return "";
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            string sanitized = new string(name.Trim().Select(character =>
                invalidCharacters.Contains(character) ? '_' : character).ToArray());
            return sanitized.Trim().Trim('.');
        }

        private static void WriteVanillaModList(
            string name,
            List<string> packageIds,
            Action onSaved)
        {
            try
            {
                Type modListType = typeof(RimWorld.ModList);
                object modList = Activator.CreateInstance(modListType, true);
                FieldInfo idsField = modListType.GetField("ids",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (modList == null || idsField == null)
                    throw new MissingMemberException("Could not initialize the vanilla mod list.");
                idsField.SetValue(modList, packageIds);

                MethodInfo[] saveMethods = typeof(GameDataSaveLoader).GetMethods(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(method => method.Name == "SaveModList")
                    .OrderByDescending(method => method.GetParameters().Length).ToArray();
                foreach (MethodInfo method in saveMethods)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    object[] arguments = new object[parameters.Length];
                    bool supported = true;
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        Type parameterType = parameters[i].ParameterType;
                        if (parameterType.IsAssignableFrom(modListType))
                            arguments[i] = modList;
                        else if (parameterType == typeof(string))
                            arguments[i] = name;
                        else if (parameterType.IsAssignableFrom(typeof(List<string>)))
                            arguments[i] = packageIds;
                        else if (parameterType == typeof(string[]))
                            arguments[i] = packageIds.ToArray();
                        else
                        {
                            supported = false;
                            break;
                        }
                    }
                    if (!supported)
                        continue;

                    object result = method.Invoke(null, arguments);
                    if (method.ReturnType == typeof(bool) && result is bool && !(bool)result)
                        throw new IOException("The game rejected the mod list save request.");
                    Messages.Message(ManagerTranslation.Get("LMM_0161") + name,
                        MessageTypeDefOf.PositiveEvent);
                    if (onSaved != null)
                        onSaved();
                    return;
                }

                throw new MissingMethodException("Could not find a compatible vanilla mod list save method.");
            }
            catch (Exception exception)
            {
                Log.Warning("Failed to save vanilla mod list '" + name + "': " + exception);
                Messages.Message(ManagerTranslation.Get("LMM_0163"),
                    MessageTypeDefOf.RejectInput);
            }
        }


private void SavePreset(string name, ManagerPresetData existingPreset)
{
    string safeName = SanitizeModListName(name);

    if (safeName.NullOrEmpty())
    {
        Messages.Message(
            ManagerTranslation.Get("LMM_0064"),
            MessageTypeDefOf.RejectInput);
        return;
    }

    ManagerPresetData snapshot = new ManagerPresetData(
        existingPreset == null
            ? Guid.NewGuid().ToString("N")
            : existingPreset.Id,
        name,
        categories,
        tags,
        modStates);

    try
    {
        // 새 프리셋 파일 저장
        ManagerPresetStorage.Save(snapshot);

        // 이름을 변경한 경우 이전 파일 삭제
        if (existingPreset != null &&
            !string.Equals(
                existingPreset.Name,
                snapshot.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            ManagerPresetStorage.Delete(existingPreset);
        }

        if (existingPreset != null)
            presets.Remove(existingPreset);

        presets.Add(snapshot);

        SavePresetsToSettings();

        Messages.Message(
            ManagerTranslation.Get("LMM_0108") + snapshot.Name,
            MessageTypeDefOf.PositiveEvent);
    }
    catch (Exception exception)
    {
        Log.Warning(
            "[Lilly Mod Manager] 프리셋 저장 실패: " + exception);

        Messages.Message(
            "프리셋 저장에 실패했습니다. 로그를 확인하세요.",
            MessageTypeDefOf.RejectInput);
    }
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

            List<ManagerCategoryData> existingCategories = categories
                .Where(category => category != null)
                .Select(category => category.Copy()).ToList();
            List<ManagerCategoryData> mergedCategories = (preset.Categories ??
                new List<ManagerCategoryData>())
                .Where(category => category != null)
                .Select(category => category.Copy()).ToList();
            foreach (ManagerCategoryData existingCategory in existingCategories)
            {
                if (!mergedCategories.Any(category => string.Equals(category.Id,
                    existingCategory.Id, StringComparison.OrdinalIgnoreCase)))
                    mergedCategories.Add(existingCategory);
            }
            categories.Clear();
            categories.AddRange(mergedCategories);
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
            PersistCategoryData();
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
        ManagerTranslation.Get("LMM_0106") +
        preset.Name +
        ManagerTranslation.Get("LMM_0006"),
        () =>
        {
            try
            {
                // XML 파일 삭제
                ManagerPresetStorage.Delete(preset);

                presets.Remove(preset);

                SavePresetsToSettings();

                Messages.Message(
                    ManagerTranslation.Get("LMM_0107") + preset.Name,
                    MessageTypeDefOf.PositiveEvent);
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[Lilly Mod Manager] 프리셋 삭제 실패: " +
                    exception);

                Messages.Message(
                    "프리셋 삭제에 실패했습니다.",
                    MessageTypeDefOf.RejectInput);
            }
        },
        destructive: true));
}



private void SavePresetsToSettings()
{
    // 모든 프리셋을 개별 XML 파일로 저장
    ManagerPresetStorage.SaveAll(presets);

    // 기존 통합 설정 파일에는 프리셋을 저장하지 않음
    settings.Presets.Clear();

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
                savedState.DisplayName = workingState.DisplayName;
            }

            IntegratedModManagerMod.Instance.WriteSettings();
        }

        private void PersistCategoryData()
        {
            settings.Categories = categories
                .Where(category => category != null)
                .Select(category => category.Copy()).ToList();
            IntegratedModManagerMod.Instance.WriteSettings();
        }

// 게임 도중 새로 설치된 모드 검색
// 게임 도중 새로 설치된 모드 검색
private void RefreshInstalledModsIfNeeded()
{
    // 15초마다 검색
    if (Time.realtimeSinceStartup < nextInstalledModRefreshTime)
        return;

    nextInstalledModRefreshTime =
        Time.realtimeSinceStartup + InstalledModRefreshInterval;

    // 기존 모드 목록 기억
    HashSet<string> previousPackageIds =
        new HashSet<string>(
            modsByPackageId.Keys,
            StringComparer.OrdinalIgnoreCase);

    // 림월드의 원본 모드 목록 갱신
    if (!IntegratedModManagerStartup.RefreshModList())
        return;

    // 현재 설치된 모드 다시 수집
    modsByPackageId.Clear();

    bool newModFound = false;

    foreach (ModMetaData mod in ModLister.AllInstalledMods)
    {
        if (mod == null || mod.PackageId.NullOrEmpty())
            continue;

        modsByPackageId[mod.PackageId] = mod;

        if (!previousPackageIds.Contains(mod.PackageId))
            newModFound = true;
    }

    // 신규 모드가 없다면 종료
    if (!newModFound)
        return;

    // 신규 모드를 카테고리와 모드 데이터에 등록
    AddInstalledModsMissingFromPreset();

    // 데이터 저장
    PersistCategoryData();
    PersistTagData();

    // 현재 화면 갱신
    RefreshVisibleMods();

    Messages.Message(
        "새로운 모드가 목록에 추가되었습니다.",
        MessageTypeDefOf.PositiveEvent);
}
        private void RefreshVisibleMods()
        {
            EnsureCurrentSelectionValid();
            RebuildLoadOrderIssueIds();
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

private int GetActiveCount(ManagerCategoryData category)
{
    if (category == null || category.ModIds == null)
        return 0;

    int count = 0;

    for (int i = 0; i < category.ModIds.Count; i++)
    {
        ModMetaData mod = FindMod(category.ModIds[i]);

        if (mod != null && IsModActiveInManager(mod))
            count++;
    }

    return count;
}

private int GetActiveCount(ManagerTagData tag)
{
    if (tag == null)
        return 0;

    int count = 0;

    for (int i = 0; i < modStates.Count; i++)
    {
        ManagerModData modData = modStates[i];

        if (modData == null || FindMod(modData.PackageId) == null)
            continue;

        bool belongsToTag = tag.IsSystem
            ? modData.TagIds == null || modData.TagIds.Count == 0
            : modData.TagIds != null && modData.TagIds.Contains(tag.Id);

        if (!belongsToTag)
            continue;

        ModMetaData mod = FindMod(modData.PackageId);

        if (IsModActiveInManager(mod))
            count++;
    }

    return count;
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
            int selectedCount = 0;
            int matchedCount = 0;

            if (filterActive)
            {
                selectedCount++;
                if (state != null && state.Enabled)
                    matchedCount++;
                else if (filterUseAnd)
                    return false;
                else if (matchedCount > 0)
                    return true;
            }
            if (filterInactive)
            {
                selectedCount++;
                if (state == null || !state.Enabled)
                    matchedCount++;
                else if (filterUseAnd)
                    return false;
                else if (matchedCount > 0)
                    return true;
            }
            if (filterSubscribed)
            {
                selectedCount++;
                if (IsSteamSubscribed(mod))
                    matchedCount++;
                else if (filterUseAnd)
                    return false;
                else if (matchedCount > 0)
                    return true;
            }
            if (filterUntagged)
            {
                selectedCount++;
                if (state == null || state.TagIds == null || state.TagIds.Count == 0)
                    matchedCount++;
                else if (filterUseAnd)
                    return false;
                else if (matchedCount > 0)
                    return true;
            }
            if (filterUncategorized)
            {
                selectedCount++;
                if (IsInUncategorizedFolder(mod.PackageId))
                    matchedCount++;
                else if (filterUseAnd)
                    return false;
                else if (matchedCount > 0)
                    return true;
            }

            if (selectedCount == 0)
                return true;
            return filterUseAnd
                ? matchedCount == selectedCount
                : matchedCount > 0;
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
            if (tagMode)
                PersistTagData();
            else
                PersistCategoryData();
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

        private static string GetSteamWorkshopPageUrl(ModMetaData mod)
        {
            if (mod == null)
                return string.Empty;

            ulong workshopId = GetWorkshopItemId(mod);
            if (workshopId > 0UL)
                return "https://steamcommunity.com/sharedfiles/filedetails/?id=" + workshopId;

            string[] propertyNames = { "WorkshopUri", "WorkshopUrl", "WorkshopPage", "SteamWorkshopUrl" };
            for (int i = 0; i < propertyNames.Length; i++)
            {
                object value = ReadMetadataValue(mod, propertyNames[i]);
                if (value != null && !value.ToString().NullOrEmpty())
                    return value.ToString();
            }
            return string.Empty;
        }

        private int DrawWorkshopGallery(
            Rect inner,
            ref float y,
            IReadOnlyList<WorkshopPreviewGallery.GalleryImage> images,
            bool pointerInsideViewport)
        {
            if (images == null || images.Count == 0)
                return -1;

            const float cellWidth = 106f;
            const float galleryHeight = 76f;
            float contentWidth = Mathf.Max(inner.width, images.Count * cellWidth);
            float horizontalScrollbarHeight = contentWidth > inner.width
                ? ManagerUiMetrics.HorizontalScrollbarHeight
                : 0f;
            Rect galleryRect = new Rect(inner.x, y, inner.width,
                galleryHeight + horizontalScrollbarHeight);
            Rect viewRect = new Rect(0f, 0f, contentWidth, galleryHeight);
            Widgets.BeginScrollView(galleryRect, ref workshopGalleryScroll, viewRect);
            int hoveredIndex = -1;
            for (int i = 0; i < images.Count; i++)
            {
                Rect imageRect = new Rect(i * cellWidth, 0f, cellWidth - 6f,
                    galleryHeight - 4f);
                bool hovered = pointerInsideViewport && Mouse.IsOver(imageRect);
                if (hovered)
                {
                    hoveredIndex = i;
                    Widgets.DrawBoxSolid(imageRect, new Color(0.32f, 0.27f, 0.18f, 1f));
                }
                Texture2D texture = hovered
                    ? WorkshopPreviewGallery.GetTexture(images[i], Time.realtimeSinceStartup)
                    : WorkshopPreviewGallery.GetStillTexture(images[i]);
                if (texture != null)
                    GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleToFit);
                if (hovered)
                    Widgets.DrawBox(imageRect);
            }
            Widgets.EndScrollView();
            y += galleryRect.height + 6f;
            return hoveredIndex;
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
