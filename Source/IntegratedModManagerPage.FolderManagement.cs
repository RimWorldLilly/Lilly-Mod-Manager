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
    }
}
