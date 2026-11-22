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
        
        private static bool BackupCurrentModList()
                {
                    string backupName = DateTime.Now.ToString("yyyyMMddHHmm") + ".backup";
                    try
                    {
                        List<string> currentPackageIds = ModsConfig.ActiveModsInLoadOrder
                            .Where(mod => mod != null && !mod.PackageId.NullOrEmpty())
                            .Select(mod => mod.PackageId)
                            .ToList();
        
                        Type modListType = typeof(RimWorld.ModList);
                        object modList = Activator.CreateInstance(modListType, true);
                        if (modList == null)
                            throw new MissingMemberException("Could not initialize the vanilla mod list.");
        
                        FieldInfo idsField = modListType.GetField("ids",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        if (idsField == null)
                            throw new MissingFieldException(modListType.FullName, "ids");
        
                        idsField.SetValue(modList, currentPackageIds);
        
                        string absolutePath = GenFilePaths.AbsFilePathForModList(backupName);
                        MethodInfo saveMethod = typeof(GameDataSaveLoader).GetMethod(
                            "SaveModList",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { modListType, typeof(string) },
                            null);
        
                        if (saveMethod == null)
                            throw new MissingMethodException(
                                "Could not find GameDataSaveLoader.SaveModList(ModList, string).");
        
                        saveMethod.Invoke(null, new[] { modList, (object)absolutePath });
        
                        FileInfo backupFile = new FileInfo(absolutePath);
                        if (!backupFile.Exists || backupFile.Length == 0)
                            throw new IOException("The backup mod list file was not created: " + absolutePath);
        
                        Log.Message("[Lilly Mod Manager] Backed up the previous active mod list to: " +
                            backupFile.FullName);
                        return true;
                    }
                    catch (Exception exception)
                    {
                        Exception actualException = exception is TargetInvocationException &&
                            ((TargetInvocationException)exception).InnerException != null
                                ? ((TargetInvocationException)exception).InnerException
                                : exception;
                        Log.Error("[Lilly Mod Manager] Failed to back up the previous active mod list: " +
                            actualException);
                        Messages.Message(
                            "모드 목록 백업에 실패하여 적용을 취소했습니다. 로그를 확인하세요.",
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }
                }
        
        private void CommitAppliedChanges(List<string> activePackageIds)
                {
                    // 실제 적용 직전에 현재 활성 모드 목록을 백업한다.
                    // 백업에 실패하면 기존 목록을 보존하기 위해 적용을 중단한다.
                    if (!BackupCurrentModList())
                        return;
        
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
                        if (modList == null)
                            throw new MissingMemberException("Could not initialize the vanilla mod list.");
        
                        FieldInfo idsField = modListType.GetField("ids",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        if (idsField == null)
                            throw new MissingFieldException(modListType.FullName, "ids");
        
                        // Vanilla ModList stores the package IDs in its public ids field.
                        idsField.SetValue(modList, packageIds ?? new List<string>());
        
                        // GameDataSaveLoader.SaveModList expects an ABSOLUTE file path,
                        // not the display name. Passing only 'name' was the reason the
                        // vanilla list could not be saved.
                        string absolutePath = GenFilePaths.AbsFilePathForModList(name);
                        MethodInfo saveMethod = typeof(GameDataSaveLoader).GetMethod(
                            "SaveModList",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { modListType, typeof(string) },
                            null);
        
                        if (saveMethod == null)
                            throw new MissingMethodException(
                                "Could not find GameDataSaveLoader.SaveModList(ModList, string).");
        
                        saveMethod.Invoke(null, new[] { modList, (object)absolutePath });
        
                        // The vanilla SaveModList method handles its own exceptions,
                        // so verify that the target file was actually created.
                        FileInfo savedFile = new FileInfo(absolutePath);
                        if (!savedFile.Exists || savedFile.Length == 0)
                            throw new IOException(
                                "The vanilla mod list file was not created: " + absolutePath);
        
                        Messages.Message(ManagerTranslation.Get("LMM_0161") + name,
                            MessageTypeDefOf.PositiveEvent);
                        if (onSaved != null)
                            onSaved();
                    }
                    catch (Exception exception)
                    {
                        Exception actualException = exception is TargetInvocationException &&
                            ((TargetInvocationException)exception).InnerException != null
                                ? ((TargetInvocationException)exception).InnerException
                                : exception;
                        Log.Warning("Failed to save vanilla mod list '" + name + "': " +
                            actualException);
                        Messages.Message(ManagerTranslation.Get("LMM_0163"),
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
        
        private static void OpenPresetFolder()
                {
                    // 프리셋 XML 파일이 저장되는 사용자 설정 폴더를 연다.
                    string presetDirectory = Path.Combine(
                        GenFilePaths.ConfigFolderPath, "LillyModManager", "Presets");
                    try
                    {
                        Directory.CreateDirectory(presetDirectory);
                        // Windows 탐색기를 직접 실행해 Unity/Mono의 기본 폴더 연결 처리에
                        // 의존하지 않고 항상 프리셋 폴더를 연다.
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = "\"" + presetDirectory + "\"",
                            UseShellExecute = false
                        });
                    }
                    catch (Exception exception)
                    {
                        Log.Error("[Lilly Mod Manager] Could not open preset folder: " + exception);
                        Messages.Message("프리셋 폴더를 열지 못했습니다.", MessageTypeDefOf.RejectInput);
                    }
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
        
        private bool AddInstalledModsMissingFromPreset()
                {
                    bool changed = false;
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
                    int previousStateCount = modStates.Count;
                    modStates.RemoveAll(state => state == null || excluded.Contains(state.PackageId));
                    changed |= previousStateCount != modStates.Count;
        
                    ManagerCategoryData normalCategory = categories.FirstOrDefault(category =>
                        string.Equals(category.Id, "normal", StringComparison.OrdinalIgnoreCase));
                    if (normalCategory == null)
                    {
                        normalCategory = new ManagerCategoryData("normal", ManagerTranslation.Get("LMM_0065"));
                        categories.Add(normalCategory);
                        changed = true;
                    }
        
                    ManagerFolderData unfiled = FindFolder(normalCategory, "uncategorized");
                    if (unfiled == null)
                    {
                        unfiled = new ManagerFolderData("uncategorized", ManagerTranslation.Get("LMM_0040"));
                        normalCategory.Folders.Insert(0, unfiled);
                        changed = true;
                    }
        
                    // 활성 모드 순서를 먼저 유지하고, 비활성 설치 모드는 뒤에 보충한다.
                    foreach (ModMetaData mod in ModsConfig.ActiveModsInLoadOrder.Concat(ModLister.AllInstalledMods))
                    {
                        if (mod == null || mod.PackageId.NullOrEmpty() ||
                            excluded.Contains(mod.PackageId))
                            continue;
        
                        ManagerModData state = FindModState(mod.PackageId);
                        bool isNewMod = state == null;
                        if (isNewMod)
                        {
                            state = new ManagerModData(mod.PackageId, mod.Active);
                            modStates.Add(state);
                            changed = true;
                            if (settings.AutoTagNewMods)
                                changed |= AddAutomaticTag(state, "auto-new", "신규");
                        }
        
                        string currentVersion = GetModVersionText(mod);
                        if (!currentVersion.NullOrEmpty())
                        {
                            if (!state.InstalledVersion.NullOrEmpty() &&
                                !string.Equals(state.InstalledVersion, currentVersion,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                if (settings.AutoTagUpdatedMods)
                                    changed |= AddAutomaticTag(state, "auto-updated", "변경됨");
                                changed = true;
                            }
        
                            if (!string.Equals(state.InstalledVersion, currentVersion,
                                StringComparison.OrdinalIgnoreCase))
                            {
                                state.InstalledVersion = currentVersion;
                                changed = true;
                            }
                        }
        
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
                        changed = true;
                    }
        
                    return changed;
                }
        
        private bool AddAutomaticTag(ManagerModData state, string tagId, string tagName)
                {
                    if (state == null)
                        return false;
        
                    if (state.TagIds == null)
                        state.TagIds = new List<string>();
        
                    ManagerTagData tag = tags.FirstOrDefault(candidate => candidate != null &&
                        string.Equals(candidate.Id, tagId, StringComparison.OrdinalIgnoreCase));
                    if (tag == null)
                    {
                        tag = tags.FirstOrDefault(candidate => candidate != null &&
                            string.Equals(candidate.Name, tagName, StringComparison.OrdinalIgnoreCase));
                    }
                    if (tag == null)
                    {
                        tag = new ManagerTagData(tagId, tagName);
                        tags.Add(tag);
                    }
        
                    if (state.TagIds.Contains(tag.Id))
                        return false;
        
                    state.TagIds.Add(tag.Id);
                    return true;
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
                        savedState.InstalledVersion = workingState.InstalledVersion;
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
    }
}
