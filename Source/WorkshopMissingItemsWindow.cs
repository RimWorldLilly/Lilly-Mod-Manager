using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class WorkshopMissingItem
    {
        public string Id;
        public string Name;
    }

    internal sealed class WorkshopMissingItemsWindow : Window
    {
        private readonly List<WorkshopMissingItem> items;
        private readonly List<string> missingActivePackageIds;
        private Vector2 scrollPosition;

        public override Vector2 InitialSize
        {
            get { return new Vector2(Mathf.Min(760f, UI.screenWidth - 80f), Mathf.Min(650f, UI.screenHeight - 80f)); }
        }

        public WorkshopMissingItemsWindow()
        {
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            items = FindMissingItems();
            missingActivePackageIds = FindMissingActivePackageIds();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "Steam 창작마당 누락 항목 검사");
            Text.Font = GameFont.Small;

            Widgets.Label(new Rect(0f, 36f, inRect.width, 42f),
                "Steam 구독 기록과 실제 폴더를 비교합니다. 폴더가 없다는 사실만으로 창작마당에서 삭제되었다고 확정할 수는 없습니다.");
            Widgets.Label(new Rect(0f, 80f, inRect.width, 26f),
                "창작마당 폴더 누락: " + items.Count + "개    |    활성 목록에만 남은 PackageId: " + missingActivePackageIds.Count + "개");

            if (items.Count == 0 && missingActivePackageIds.Count == 0)
            {
                // 빈 상태에서는 목록 제목과 안내 문구가 같은 위치에 그려지지 않도록
                // 스크롤 목록을 생략하고 안내 문구만 표시한다.
                Widgets.Label(new Rect(6f, 116f, inRect.width - 12f, 44f),
                    "누락 항목이 없거나 Steam 설치 경로/appworkshop 기록 파일을 찾지 못했습니다.");
            }
            else
            {
                Rect listRect = new Rect(0f, 110f, inRect.width, inRect.height - 150f);
                const float rowHeight = 58f;
                float contentHeight = 30f + items.Count * rowHeight +
                    (missingActivePackageIds.Count > 0 ? 34f + missingActivePackageIds.Count * rowHeight : 0f);
                Rect viewRect = new Rect(0f, 0f, listRect.width - 18f, Mathf.Max(listRect.height, contentHeight));
                Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

                float y = 0f;
                if (items.Count > 0)
                {
                    Widgets.Label(new Rect(6f, y, viewRect.width - 12f, 26f),
                        "Steam 창작마당 기록에는 있지만 로컬 폴더가 없는 항목");
                    y += 28f;
                    for (int i = 0; i < items.Count; i++)
                    {
                        WorkshopMissingItem item = items[i];
                        Rect row = new Rect(0f, y, viewRect.width, rowHeight - 4f);
                        DrawDiagnosticRowBackground(row, i);
                        Widgets.Label(new Rect(row.x + 8f, row.y + 4f, row.width - 190f, 22f),
                            (item.Name.NullOrEmpty() ? "이름 확인 필요" : item.Name) + "  [" + item.Id + "]");
                        Widgets.Label(new Rect(row.x + 8f, row.y + 27f, row.width - 190f, 20f),
                            "로컬 폴더 없음 · 삭제/다운로드 실패/Steam 기록 잔존 가능");
                        if (Widgets.ButtonText(new Rect(row.xMax - 174f, row.y + 12f, 166f, 30f), "창작마당 페이지 열기"))
                            OpenWorkshopPage(item.Id);
                        y += rowHeight;
                    }
                }

                if (missingActivePackageIds.Count > 0)
                {
                    if (items.Count > 0)
                        y += 4f;
                    Widgets.Label(new Rect(6f, y, viewRect.width - 12f, 26f),
                        "ModsConfig.xml에는 활성화되어 있지만 설치된 모드에서 찾지 못한 항목");
                    y += 28f;
                    for (int i = 0; i < missingActivePackageIds.Count; i++)
                    {
                        Rect row = new Rect(0f, y, viewRect.width, rowHeight - 4f);
                        DrawDiagnosticRowBackground(row, i);
                        Widgets.Label(new Rect(row.x + 8f, row.y + 5f, row.width - 16f, 22f),
                            missingActivePackageIds[i]);
                        Widgets.Label(new Rect(row.x + 8f, row.y + 28f, row.width - 16f, 18f),
                            "ModsConfig.xml의 activeMods에 남아 있음 · 게임 종료 후 파일 백업 및 수동 정리 대상");
                        y += rowHeight;
                    }
                }

                Widgets.EndScrollView();
            }

            if (Widgets.ButtonText(new Rect(0f, inRect.height - 32f, 150f, 30f), "다시 검사"))
            {
                items.Clear();
                items.AddRange(FindMissingItems());
                missingActivePackageIds.Clear();
                missingActivePackageIds.AddRange(FindMissingActivePackageIds());
                scrollPosition = Vector2.zero;
            }
            if (Widgets.ButtonText(new Rect(inRect.width - 150f, inRect.height - 32f, 150f, 30f), "닫기"))
                Close();
        }

        private static void DrawDiagnosticRowBackground(Rect row, int index)
        {
            Widgets.DrawBoxSolid(row, index % 2 == 0
                ? new Color(0.12f, 0.12f, 0.12f, 1f)
                : new Color(0.08f, 0.08f, 0.08f, 1f));
        }

        private static List<WorkshopMissingItem> FindMissingItems()
        {
            List<WorkshopMissingItem> result = new List<WorkshopMissingItem>();
            try
            {
                string contentPath = FindWorkshopContentPath();
                if (contentPath.NullOrEmpty() || !Directory.Exists(contentPath))
                    return result;

                DirectoryInfo workshopRootDirectory = Directory.GetParent(contentPath)?.Parent;
                if (workshopRootDirectory == null)
                    return result;

                string acfPath = Path.Combine(workshopRootDirectory.FullName, "appworkshop_294100.acf");
                if (!File.Exists(acfPath))
                    return result;

                string acf = File.ReadAllText(acfPath);
                HashSet<string> itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectIdsFromSection(acf, "WorkshopItemsInstalled", itemIds);
                CollectIdsFromSection(acf, "WorkshopItemDetails", itemIds);

                foreach (string id in itemIds.OrderBy(value => value, StringComparer.Ordinal))
                {
                    if (!Regex.IsMatch(id, @"^\d{6,}$"))
                        continue;
                    if (Directory.Exists(Path.Combine(contentPath, id)))
                        continue;
                    result.Add(new WorkshopMissingItem { Id = id });
                }
            }
            catch (Exception exception)
            {
                Log.Warning("[Lilly Mod Manager] Workshop missing-item scan failed: " + exception.Message);
            }
            return result;
        }

        private static List<string> FindMissingActivePackageIds()
        {
            List<string> result = new List<string>();
            try
            {
                string configPath = Path.Combine(GenFilePaths.ConfigFolderPath, "ModsConfig.xml");
                if (!File.Exists(configPath))
                    return result;

                HashSet<string> installedPackageIds = new HashSet<string>(
                    ModLister.AllInstalledMods
                        .Where(mod => mod != null && !mod.PackageId.NullOrEmpty())
                        .Select(mod => mod.PackageId),
                    StringComparer.OrdinalIgnoreCase);

                System.Xml.XmlDocument document = new System.Xml.XmlDocument();
                document.Load(configPath);
                System.Xml.XmlNodeList activeMods = document.SelectNodes("//activeMods/li");
                if (activeMods == null)
                    return result;

                foreach (System.Xml.XmlNode node in activeMods)
                {
                    string packageId = node == null ? null : node.InnerText.Trim();
                    if (!packageId.NullOrEmpty() && !installedPackageIds.Contains(packageId) &&
                        !result.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                        result.Add(packageId);
                }
            }
            catch (Exception exception)
            {
                Log.Warning("[Lilly Mod Manager] Missing active mod scan failed: " + exception.Message);
            }
            return result;
        }

        private static string FindWorkshopContentPath()
        {
            foreach (ModMetaData mod in ModLister.AllInstalledMods)
            {
                if (mod == null || !mod.OnSteamWorkshop || mod.RootDir == null)
                    continue;
                try
                {
                    DirectoryInfo modDirectory = new DirectoryInfo(mod.RootDir.ToString());
                    DirectoryInfo appIdDirectory = modDirectory.Parent;
                    if (appIdDirectory != null &&
                        string.Equals(appIdDirectory.Name, "294100", StringComparison.OrdinalIgnoreCase))
                        return appIdDirectory.FullName;
                }
                catch (Exception)
                {
                    // Continue looking for another installed Workshop mod.
                }
            }
            return null;
        }

        private static void CollectIdsFromSection(string text, string sectionName, HashSet<string> ids)
        {
            int sectionIndex = text.IndexOf("\"" + sectionName + "\"", StringComparison.OrdinalIgnoreCase);
            if (sectionIndex < 0)
                return;
            int openBrace = text.IndexOf('{', sectionIndex);
            if (openBrace < 0)
                return;

            int depth = 0;
            int closeBrace = -1;
            for (int i = openBrace; i < text.Length; i++)
            {
                if (text[i] == '{')
                    depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeBrace = i;
                        break;
                    }
                }
            }
            if (closeBrace <= openBrace)
                return;

            string section = text.Substring(openBrace + 1, closeBrace - openBrace - 1);
            MatchCollection matches = Regex.Matches(section, @"(?m)^\s*""(?<id>\d{6,})""\s*\{");
            foreach (Match match in matches)
                ids.Add(match.Groups["id"].Value);
        }

        private static void OpenWorkshopPage(string id)
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    "https://steamcommunity.com/sharedfiles/filedetails/?id=" + id)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                Log.Warning("[Lilly Mod Manager] Could not open Workshop page: " + exception.Message);
                Messages.Message("창작마당 페이지를 열지 못했습니다.", MessageTypeDefOf.RejectInput);
            }
        }
    }
}
