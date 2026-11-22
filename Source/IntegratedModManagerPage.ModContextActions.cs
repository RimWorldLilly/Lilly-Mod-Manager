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
    }
}
