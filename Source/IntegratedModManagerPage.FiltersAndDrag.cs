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
        private void RefreshVisibleMods()
                {
                    EnsureCurrentSelectionValid();
                    RebuildLoadOrderIssueIds();
                    visibleMods.Clear();
                    if (allModsView)
                    {
                        HashSet<string> addedPackageIds =
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (ModMetaData mod in ModLister.AllInstalledMods)
                        {
                            if (mod != null && !mod.PackageId.NullOrEmpty() &&
                                addedPackageIds.Add(mod.PackageId) && MatchesSearch(mod))
                                visibleMods.Add(mod);
                        }
                        visibleMods.Sort((left, right) =>
                        {
                            int nameCompare = StringComparer.OrdinalIgnoreCase.Compare(
                                GetDisplayName(left), GetDisplayName(right));
                            return nameCompare != 0 ? nameCompare :
                                StringComparer.OrdinalIgnoreCase.Compare(left.PackageId, right.PackageId);
                        });
                    }
                    else if (tagMode)
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
                    if (allModsView)
                        return;
        
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
        
        private int GetActiveCount(ManagerFolderData folder)
                {
                    if (folder == null || folder.ModIds == null)
                        return 0;
        
                    int count = 0;
                    HashSet<string> counted = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < folder.ModIds.Count; i++)
                    {
                        string packageId = folder.ModIds[i];
                        if (packageId.NullOrEmpty() || !counted.Add(packageId))
                            continue;
        
                        ModMetaData mod = FindMod(packageId);
                        if (mod != null && IsModActiveInManager(mod))
                            count++;
                    }
                    return count;
                }
        
        private int GetInstalledCount(ManagerFolderData folder)
                {
                    if (folder == null || folder.ModIds == null)
                        return 0;
        
                    int count = 0;
                    HashSet<string> counted = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < folder.ModIds.Count; i++)
                    {
                        string packageId = folder.ModIds[i];
                        if (!packageId.NullOrEmpty() && counted.Add(packageId) &&
                            modsByPackageId.ContainsKey(packageId))
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
        
                    string displayName = GetDisplayName(mod);
                    switch (searchScope)
                    {
                        case SearchScope.Name:
                            return ContainsSearchText(displayName, query) ||
                                ContainsSearchText(mod.Name, query);
        
                        case SearchScope.Description:
                            return ContainsSearchText(mod.Description, query);
        
                        case SearchScope.Author:
                            return ContainsSearchText(mod.AuthorsString, query);
        
                        case SearchScope.PackageId:
                            return ContainsSearchText(mod.PackageId, query);
        
                        default:
                            return MatchesAllSearchFields(mod, query);
                    }
                }
        
        private static bool ContainsSearchText(string value, string query)
                {
                    return !value.NullOrEmpty() &&
                        value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                }
        
        private bool MatchesAllSearchFields(ModMetaData mod, string query)
                {
                    ManagerModData state = FindModState(mod.PackageId);
                    if (ContainsSearchText(GetDisplayName(mod), query) ||
                        ContainsSearchText(mod.Name, query) ||
                        ContainsSearchText(mod.PackageId, query) ||
                        ContainsSearchText(mod.AuthorsString, query) ||
                        ContainsSearchText(state != null ? state.DisplayName : null, query) ||
                        ContainsSearchText(mod.Description, query) ||
                        ContainsSearchText(GetModVersionText(mod), query) ||
                        ContainsSearchText(GetSupportedGameVersionText(mod), query) ||
                        ContainsSearchText(GetDependencyText(mod), query))
                        return true;
        
                    if (state != null && state.TagIds != null)
                    {
                        for (int i = 0; i < state.TagIds.Count; i++)
                        {
                            ManagerTagData tag = FindTag(state.TagIds[i]);
                            if (tag != null && ContainsSearchText(tag.Name, query))
                                return true;
                        }
                    }
        
                    for (int i = 0; i < categories.Count; i++)
                    {
                        ManagerCategoryData category = categories[i];
                        if (category == null || category.ModIds == null)
                            continue;
                        if (ContainsSearchText(category.Name, query) &&
                            category.ModIds.Any(id => string.Equals(id, mod.PackageId,
                                StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }
        
                    return false;
                }
        
        private bool HasActiveModFilters()
                {
                    return filterActive || filterInactive || filterSubscribed ||
                        filterUntagged || filterUncategorized || filterLoadOrderIssue ||
                        filterMissingRequired || filterVersionMismatch || filterLocal ||
                        filterWorkshop || filterOfficial || filterNoFolder;
                }
        
        private bool MatchesFilter(ModMetaData mod)
                {
                    if (mod == null)
                        return false;
        
                    ManagerModData state = FindModState(mod.PackageId);
                    int selectedCount = 0;
                    int matchedCount = 0;
        
                    if (filterActive)
                    {
                        selectedCount++;
                        if (state != null && state.Enabled) matchedCount++;
                        else if (filterUseAnd) return false;
                    }
                    if (filterInactive)
                    {
                        selectedCount++;
                        if (state == null || !state.Enabled) matchedCount++;
                        else if (filterUseAnd) return false;
                    }
                    if (filterSubscribed)
                    {
                        selectedCount++;
                        if (IsSteamSubscribed(mod)) matchedCount++;
                        else if (filterUseAnd) return false;
                    }
                    if (filterUntagged)
                    {
                        selectedCount++;
                        if (state == null || state.TagIds == null || state.TagIds.Count == 0)
                            matchedCount++;
                        else if (filterUseAnd) return false;
                    }
                    if (filterUncategorized)
                    {
                        selectedCount++;
                        if (IsInUncategorizedFolder(mod.PackageId)) matchedCount++;
                        else if (filterUseAnd) return false;
                    }
                    if (filterLoadOrderIssue)
                    {
                        selectedCount++;
                        if (GetModRowIssue(mod) == ModRowIssue.LoadOrder) matchedCount++;
                        else if (filterUseAnd) return false;
                    }
                    if (filterMissingRequired)
                    {
                        selectedCount++;
                        if (GetModRowIssue(mod) == ModRowIssue.RequiredMod) matchedCount++;
                        else if (filterUseAnd) return false;
                    }
                    if (filterVersionMismatch)
                    {
                        selectedCount++;
                        if (GetModRowIssue(mod) == ModRowIssue.Version ||
                            (IsModActiveInManager(mod) && IsModOutdatedForCurrentGameVersion(mod)))
                            matchedCount++;
                        else if (filterUseAnd) return false;
                    }
                    if (filterLocal)
                    {
                        selectedCount++;
                        if (!mod.OnSteamWorkshop && !mod.IsCoreMod && !mod.Official)
                            matchedCount++;
                        else if (filterUseAnd) return false;
                    }
                    if (filterWorkshop)
                    {
                        selectedCount++;
                        if (mod.OnSteamWorkshop) matchedCount++;
                        else if (filterUseAnd) return false;
                    }
                    if (filterOfficial)
                    {
                        selectedCount++;
                        if (mod.IsCoreMod || mod.Official) matchedCount++;
                        else if (filterUseAnd) return false;
                    }
                    if (filterNoFolder)
                    {
                        selectedCount++;
                        if (!IsAssignedToAnyCategoryFolder(mod.PackageId)) matchedCount++;
                        else if (filterUseAnd) return false;
                    }
        
                    if (selectedCount == 0)
                        return true;
                    return filterUseAnd ? matchedCount == selectedCount : matchedCount > 0;
                }
        
        private bool HasUnsatisfiedRequirement(ModMetaData mod, bool versionIssue)
                {
                    if (mod == null || !IsModActiveInManager(mod))
                        return false;
        
                    try
                    {
                        foreach (ModRequirement requirement in mod.GetRequirements())
                        {
                            if (requirement == null || IsRequirementSatisfiedInManager(requirement))
                                continue;
        
                            string requirementType = requirement.RequirementTypeLabel ?? string.Empty;
                            string tooltip = requirement.Tooltip ?? string.Empty;
                            bool isVersionIssue =
                                requirementType.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                requirementType.IndexOf("踰꾩쟾", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                tooltip.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                tooltip.IndexOf("踰꾩쟾", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (isVersionIssue == versionIssue)
                                return true;
                        }
                    }
                    catch (Exception)
                    {
                        // 잘못된 메타데이터가 있어도 필터 UI가 중단되지 않도록 한다.
                    }
                    return false;
                }
        
        private bool IsAssignedToAnyCategoryFolder(string packageId)
                {
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
                            if (folder.ModIds.Any(id => string.Equals(id, packageId,
                                StringComparison.OrdinalIgnoreCase)))
                                return true;
                        }
                    }
                    return false;
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
    }
}
