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
        
                    // RimWorld는 About.xml의 supportedVersions를 내부 메타데이터에서
                    // Version 목록으로 파싱해 SupportedVersionsReadOnly로 공개한다.
                    // supportedVersions 원본 필드는 ModMetaData가 아니라 내부 meta 객체에 있다.
                    List<Version> supportedVersions = mod.SupportedVersionsReadOnly;
                    if (supportedVersions == null || supportedVersions.Count == 0)
                        return string.Empty;
        
                    return string.Join(", ", supportedVersions
                        .Where(version => version != null)
                        .Select(version => version.ToString())
                        .ToArray());
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
        
                    try
                    {
                        const BindingFlags flags =
                            BindingFlags.Instance |
                            BindingFlags.Public |
                            BindingFlags.NonPublic;
        
                        Type type = mod.GetType();
                        PropertyInfo property = type.GetProperties(flags)
                            .FirstOrDefault(candidate =>
                                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase) &&
                                candidate.GetIndexParameters().Length == 0 &&
                                candidate.GetGetMethod(true) != null);
                        if (property != null)
                            return property.GetValue(mod, null);
        
                        FieldInfo field = type.GetField(name, flags | BindingFlags.IgnoreCase);
                        return field != null ? field.GetValue(mod) : null;
                    }
                    catch (Exception)
                    {
                        // 게임 버전이나 모드별 메타데이터 구현 차이로 Reflection이 실패해도
                        // 모드 관리자 화면 전체가 중단되지 않도록 해당 값만 사용할 수 없는 것으로 처리한다.
                        return null;
                    }
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
