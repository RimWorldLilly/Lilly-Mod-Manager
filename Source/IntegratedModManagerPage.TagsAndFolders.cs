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
                                    if (target == null || target.Folder == null)
                                        return;
                
                                    if (tagMode)
                                    {
                                        if (target.Tag == null)
                                            return;
                
                                        MoveSelectionToTagFolder(target.Tag, target.Folder);
                                        return;
                                    }
                
                                    if (target.Category == null)
                                        return;
                
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
                                selectedIds,
                                tag => selectedIds.Count > 0 && selectedIds.All(id => HasTag(id, tag.Id)),
                                (tag, add) =>
                                {
                                    if (selectedIds.Count > 0)
                                        SetTagForSelection(selectedIds, tag, add);
                                    else if (add)
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
    }
}
