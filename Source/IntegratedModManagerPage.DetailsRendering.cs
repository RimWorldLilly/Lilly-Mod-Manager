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

                            if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0185")))
                                Find.WindowStack.Add(new WorkshopMissingItemsWindow());
                            x += buttonWidth + gap;

                            // 폴더 바로가기 버튼은 기존 동작과 활성화 상태를 유지한다.
                            bool previousFastMoveGuiEnabled = GUI.enabled;
                            GUI.enabled = true;
                            bool openFastFolderMove = Widgets.ButtonText(
                                new Rect(x, rect.y, buttonWidth, rect.height),
                                ManagerTranslation.Get("LMM_0094"));
                            GUI.enabled = previousFastMoveGuiEnabled;
                            if (openFastFolderMove)
                                OpenFastFolderMoveDialog();
                            x += buttonWidth + gap;

                            if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0023")))
                                ConfirmUnsubscribeSelected();
                            x += buttonWidth + gap;

                            // 선택된 모드의 실제 폴더가 있을 때만 활성화한다.
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

                            if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0039")))
                                OpenSavePresetDialog();
                            x += buttonWidth + gap;

                            if (Widgets.ButtonText(new Rect(x, rect.y, buttonWidth, rect.height), ManagerTranslation.Get("LMM_0038")))
                                OpenPresetMenu();
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
