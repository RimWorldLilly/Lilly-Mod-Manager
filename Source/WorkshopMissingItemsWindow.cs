using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class WorkshopMissingItem
    {
        public string Id;
        public string Name;
        public string FolderPath;
    }

    internal sealed class WorkshopMissingItemsWindow : Window
    {
        private readonly List<WorkshopMissingItem> items;
        private Vector2 scrollPosition;

        public override Vector2 InitialSize
        {
            get { return new Vector2(Mathf.Min(760f, UI.screenWidth - 80f), Mathf.Min(620f, UI.screenHeight - 80f)); }
        }

        public WorkshopMissingItemsWindow()
        {
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            items = FindMissingItems();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "Steam 창작마당 누락 항목 검사");
            Text.Font = GameFont.Small;

            Widgets.Label(new Rect(0f, 36f, inRect.width, 42f),
                "Steam의 appworkshop_294100.acf 기록과 실제 폴더를 비교합니다. 폴더가 없다는 사실만으로 창작마당에서 삭제되었다고 확정할 수는 없습니다.");
            Widgets.Label(new Rect(0f, 80f, inRect.width, 26f),
                "발견된 누락 항목: " + items.Count + "개");

            Rect listRect = new Rect(0f, 110f, inRect.width, inRect.height - 150f);
            float rowHeight = 58f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 18f, items.Count * rowHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            for (int i = 0; i < items.Count; i++)
            {
                WorkshopMissingItem item = items[i];
                Rect row = new Rect(0f, i * rowHeight, viewRect.width, rowHeight - 4f);
                Widgets.DrawBoxSolid(row, i % 2 == 0
                    ? new Color(0.12f, 0.12f, 0.12f, 1f)
                    : new Color(0.08f, 0.08f, 0.08f, 1f));
                Widgets.Label(new Rect(row.x + 8f, row.y + 4f, row.width - 190f, 22f),
                    (item.Name.NullOrEmpty() ? "이름 확인 필요" : item.Name) + "  [" + item.Id + "]");
                Widgets.Label(new Rect(row.x + 8f, row.y + 27f, row.width - 190f, 20f),
                    "로컬 폴더 없음 · 삭제/다운로드 실패/Steam 캐시 잔존 가능");
                if (Widgets.ButtonText(new Rect(row.xMax - 174f, row.y + 12f, 166f, 30f), "창작마당 페이지 열기"))
                    OpenWorkshopPage(item.Id);
            }
            Widgets.EndScrollView();

            if (items.Count == 0)
                Widgets.Label(new Rect(0f, 116f, inRect.width, 30f),
                    "현재 확인 가능한 누락 항목이 없습니다. Steam 설치 경로 또는 창작마당 기록 파일을 찾지 못한 경우에도 이 목록은 비어 있을 수 있습니다.");

            if (Widgets.ButtonText(new Rect(0f, inRect.height - 32f, 150f, 30f), "다시 검사"))
            {
                items.Clear();
                items.AddRange(FindMissingItems());
                scrollPosition = Vector2.zero;
            }
            if (Widgets.ButtonText(new Rect(inRect.width - 150f, inRect.height - 32f, 150f, 30f), "닫기"))
                Close();
        }

        private static List<WorkshopMissingItem> FindMissingItems()
        {
            List<WorkshopMissingItem> result = new List<WorkshopMissingItem>();
            try
            {
                string contentPath = FindWorkshopContentPath();
                if (contentPath.NullOrEmpty() || !Directory.Exists(contentPath))
                    return result;

                string workshopRoot = Directory.GetParent(contentPath)?.Parent?.FullName;
                if (workshopRoot.NullOrEmpty())
                    return result;

                string acfPath = Path.Combine(workshopRoot, "appworkshop_294100.acf");
                if (!File.Exists(acfPath))
                    return result;

                string acf = File.ReadAllText(acfPath);
                HashSet<string> itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectIdsFromSection(acf, "WorkshopItemsInstalled", itemIds);
                CollectIdsFromSection(acf, "WorkshopItemDetails", itemIds);

                Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (ModMetaData mod in ModLister.AllInstalledMods)
                {
                    if (mod == null || !mod.OnSteamWorkshop)
                        continue;
                    string id = Path.GetFileName(mod.RootDir.ToString().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (!id.NullOrEmpty() && !names.ContainsKey(id))
                        names[id] = mod.Name;
                }

                foreach (string id in itemIds.OrderBy(value => value, StringComparer.Ordinal))
                {
                    if (!Regex.IsMatch(id, @"^\d{6,}$"))
                        continue;
                    string folder = Path.Combine(contentPath, id);
                    if (Directory.Exists(folder))
                        continue;
                    string name;
                    names.TryGetValue(id, out name);
                    result.Add(new WorkshopMissingItem { Id = id, Name = name, FolderPath = folder });
                }
            }
            catch (Exception exception)
            {
                Log.Warning("[Lilly Mod Manager] Workshop missing-item scan failed: " + exception.Message);
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
            MatchCollection matches = Regex.Matches(section, @"(?m)^\s*\""(?<id>\d{6,})\""\s*\{");
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
