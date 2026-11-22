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
    internal sealed partial class IntegratedModManagerPage : Page
    {
        private enum ModRowIssue
        {
            None,
            RequiredMod,
            Version,
            LoadOrder
        }

        private enum SearchScope
        {
            All,
            Name,
            Description,
            Author,
            PackageId
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
        private int activeModCount;
        private int requiredModIssueCount;
        private int conflictingModCount;
        private int loadOrderIssueCount;
        private int versionIssueCount;
        private int outdatedModCount;

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
        private SearchScope searchScope = SearchScope.All;
        private bool filterActive;
        private bool filterInactive;
        private bool filterSubscribed;
        private bool filterUntagged;
        private bool filterUncategorized;
        private bool filterLoadOrderIssue;
        private bool filterMissingRequired;
        private bool filterVersionMismatch;
        private bool filterLocal;
        private bool filterWorkshop;
        private bool filterOfficial;
        private bool filterNoFolder;
        private bool filterUseAnd = true;
        private bool tagMode;
        private bool allModsView;
        private bool displayFulfilledRequirements;
        private bool modListHasKeyboardFocus;
        private bool categoryTabsHaveKeyboardFocus;
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
        private string dragFolderSourceCategoryId;
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

            const string pageTitle = "Lilly - Mod Manager";
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(20f, 10f, 260f, 36f), pageTitle);

            Text.Font = GameFont.Small;
            string[] statusItems =
            {
                ManagerTranslation.Get("LMM_0168") + requiredModIssueCount,
                ManagerTranslation.Get("LMM_0169") + conflictingModCount,
                ManagerTranslation.Get("LMM_0170") + loadOrderIssueCount,
                ManagerTranslation.Get("LMM_0171") + versionIssueCount,
                ManagerTranslation.Get("LMM_0172") + outdatedModCount,
                ManagerTranslation.Get("LMM_0173") + activeModCount,
                ManagerTranslation.Get("LMM_0174") + selectedPackageIds.Count
            };
            Color[] statusColors =
            {
                new Color(1f, 0.28f, 0.28f, 1f),
                new Color(1f, 0.415f, 0.26f, 1f),
                new Color(1f, 0.55f, 0.24f, 1f),
                new Color(1f, 0.685f, 0.22f, 1f),
                new Color(1f, 0.82f, 0.2f, 1f),
                new Color(0.25f, 0.85f, 0.9f, 1f),
                new Color(0.25f, 0.85f, 0.9f, 1f)
            };
            const string statusSeparator = "  |  ";
            float statusWidth = 0f;
            for (int i = 0; i < statusItems.Length; i++)
            {
                statusWidth += Text.CalcSize(statusItems[i]).x;
                if (i > 0)
                    statusWidth += Text.CalcSize(statusSeparator).x;
            }

            float statusX = Mathf.Max(280f, inRect.width - statusWidth - 20f);
            Text.Anchor = TextAnchor.MiddleLeft;
            for (int i = 0; i < statusItems.Length; i++)
            {
                if (i > 0)
                {
                    float separatorWidth = Text.CalcSize(statusSeparator).x;
                    GUI.color = new Color(0.55f, 0.55f, 0.55f, 1f);
                    Widgets.Label(new Rect(statusX, 12f, separatorWidth, 28f),
                        statusSeparator);
                    statusX += separatorWidth;
                }

                float itemWidth = Text.CalcSize(statusItems[i]).x;
                GUI.color = statusColors[i];
                Widgets.Label(new Rect(statusX, 12f, itemWidth, 28f), statusItems[i]);
                statusX += itemWidth;
            }

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            float contentBottom = inRect.height - BottomHeight - 20f;
            bool improvedLayout = settings.UseImprovedLayout;
            // 개선 레이아웃은 사이드바를 기존 너비의 2/3로 줄이고,
            // 확보한 너비만큼 모드 목록 영역을 확장한다.
            float sidebarWidth = inRect.width * (improvedLayout ? (1f / 6f) : 0.25f);
            float mainX = 20f + sidebarWidth + 12f;
            float mainWidth = Mathf.Max(100f, inRect.width - mainX - 20f);

            const float searchTop = 48f;
            const float searchHeight = 30f;
            // 검색 및 모든 필터를 한 줄에 표시한다. 정렬 버튼은 하단 버튼 영역으로 이동.
            Rect searchRect = new Rect(20f, searchTop,
                Mathf.Max(100f, inRect.width - 40f), searchHeight);
            float sectionTop = searchRect.yMax + 8f;
            Rect categoryRect = new Rect(20f, sectionTop, sidebarWidth,
                contentBottom - sectionTop);

            float contentTop = searchRect.yMax + 8f;
            float contentHeight = Mathf.Max(100f, contentBottom - contentTop);
            float listWidth = Mathf.Min(mainWidth - 120f,
                inRect.width * (improvedLayout ? (1f / 3f) : 0.25f));
            Rect listRect = new Rect(mainX, contentTop, listWidth, contentHeight);
            Rect detailsRect = new Rect(mainX + listWidth + 10f, contentTop,
                mainWidth - listWidth - 10f, contentHeight);

            // 키보드 탐색은 검색창/목록 등의 IMGUI 컨트롤이 KeyDown 이벤트를
            // 먼저 소비하기 전에 처리해야 한다. 팝업을 닫은 직후에도 이 위치에서
            // 방향키 입력을 먼저 받아 목록 포커스를 복구할 수 있다.
            HandleModListNavigation(categoryRect, listRect);

            // 하단 버튼을 사이드바/모드 목록보다 먼저 그려 IMGUI 컨트롤 ID와
            // 마우스 입력 처리가 목록의 선택 상태에 영향을 받지 않게 한다.
            DrawBottomButtons(new Rect(20f, inRect.height - BottomHeight - 8f,
                inRect.width - 40f, BottomHeight));

            DrawCategoryTabs(categoryRect);
            DrawSearchField(searchRect);

            DrawModList(listRect);
            DrawDetails(detailsRect);
            DrawPaneFocusBorders(categoryRect, listRect);
            DrawModDragPreview(inRect);
            DrawSidebarDragPreview(inRect);
            DrawFolderDragPreview(inRect);
            HandleSelectAll();
            if (Event.current.type == EventType.MouseUp)
            {
                dragCategoryId = null;
                dragTagId = null;
                dragFolderId = null;
                dragFolderSourceCategoryId = null;
                if (isDraggingMod)
                    EndModDrag();
            }
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




private void OpenModColorDialog(List<string> packageIds)
{
    if (packageIds == null || packageIds.Count == 0)
        return;

    ManagerModData firstMod = FindModState(packageIds[0]);

    string initialColor = firstMod != null
        ? firstMod.ColorHex
        : null;

    Find.WindowStack.Add(new ManagerFolderNameDialog(
        "모드 배경색 선택",
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
        },
        true));
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
    // 목록 자체에 변화가 없어도 기존 모드의 업데이트 여부를 검사해야 하므로
    // RefreshModList()의 반환값만으로 검사를 중단하지 않는다.
    IntegratedModManagerStartup.RefreshModList();

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

    // 신규 설치와 기존 모드의 버전 변경을 모두 확인한다.
    bool dataChanged = AddInstalledModsMissingFromPreset();
    if (!newModFound && !dataChanged)
        return;

    // 데이터 저장
    PersistCategoryData();
    PersistTagData();

    // 현재 화면 갱신
    RefreshVisibleMods();

    if (newModFound)
    {
        Messages.Message(
            "새로운 모드가 목록에 추가되었습니다.",
            MessageTypeDefOf.PositiveEvent);
    }
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






































    internal sealed class ManagerOptionsWindow : Window
    {
        private readonly IntegratedModManagerSettings settings;

        public ManagerOptionsWindow(IntegratedModManagerSettings settings)
        {
            this.settings = settings;
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(460f, 260f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f),
                ManagerTranslation.Get("LMM_0187"));
            Text.Font = GameFont.Small;

            bool oldImprovedLayout = settings.UseImprovedLayout;
            bool oldAutoTagNewMods = settings.AutoTagNewMods;
            bool oldAutoTagUpdatedMods = settings.AutoTagUpdatedMods;

            Widgets.CheckboxLabeled(
                new Rect(0f, 46f, inRect.width, 30f),
                ManagerTranslation.Get("LMM_0191"),
                ref settings.UseImprovedLayout);
            Widgets.CheckboxLabeled(
                new Rect(0f, 82f, inRect.width, 30f),
                ManagerTranslation.Get("LMM_0188"),
                ref settings.AutoTagNewMods);
            Widgets.CheckboxLabeled(
                new Rect(0f, 118f, inRect.width, 30f),
                ManagerTranslation.Get("LMM_0189"),
                ref settings.AutoTagUpdatedMods);

            if (Widgets.ButtonText(
                new Rect(0f, 158f, inRect.width, 34f),
                ManagerTranslation.Get("LMM_0209")))
            {
                DesktopShortcut.CreateFromOptions();
            }

            if (oldImprovedLayout != settings.UseImprovedLayout ||
                oldAutoTagNewMods != settings.AutoTagNewMods ||
                oldAutoTagUpdatedMods != settings.AutoTagUpdatedMods)
            {
                IntegratedModManagerMod.Instance.WriteSettings();
            }
        }
    }
}
}