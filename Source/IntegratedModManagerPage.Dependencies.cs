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

namespace Lilly.IntegratedModManager
{
    internal sealed partial class IntegratedModManagerPage
    {
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

            // 상단 상태 표시줄용 통계는 목록 필터와 관계없이 전체 설치 모드를 기준으로 계산한다.
            activeModCount = 0;
            requiredModIssueCount = 0;
            conflictingModCount = 0;
            loadOrderIssueCount = 0;
            versionIssueCount = 0;
            outdatedModCount = 0;
            foreach (ModMetaData mod in modsByPackageId.Values)
            {
                if (mod == null || !IsModActiveInManager(mod))
                    continue;

                activeModCount++;
                ModRowIssue issue = GetModRowIssue(mod);
                if (issue == ModRowIssue.RequiredMod)
                    requiredModIssueCount++;
                else if (issue == ModRowIssue.LoadOrder)
                    loadOrderIssueCount++;
                else if (issue == ModRowIssue.Version)
                    versionIssueCount++;

                if (HasActiveIncompatibility(mod))
                    conflictingModCount++;
                if (IsModOutdatedForCurrentGameVersion(mod))
                    outdatedModCount++;
            }
        }

private bool HasActiveIncompatibility(ModMetaData mod)
        {
            if (mod == null)
                return false;

            try
            {
                foreach (ModRequirement requirement in mod.GetRequirements())
                {
                    if (requirement is ModIncompatibility &&
                        !IsRequirementSatisfiedInManager(requirement))
                        return true;
                }
            }
            catch (Exception)
            {
                // 충돌 정보를 읽을 수 없어도 모드 목록 자체는 계속 표시한다.
            }
            return false;
        }

private static bool IsModOutdatedForCurrentGameVersion(ModMetaData mod)
        {
            if (mod == null)
                return false;

            List<Version> supportedVersions = mod.SupportedVersionsReadOnly;
            if (supportedVersions == null || supportedVersions.Count == 0)
                return false;

            int currentMajor = VersionControl.CurrentMajor;
            int currentMinor = VersionControl.CurrentMinor;
            bool hasCurrentOrNewerVersion = supportedVersions.Any(version =>
                version != null &&
                (version.Major > currentMajor ||
                 (version.Major == currentMajor && version.Minor >= currentMinor)));

            return !hasCurrentOrNewerVersion;
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
    }
}
