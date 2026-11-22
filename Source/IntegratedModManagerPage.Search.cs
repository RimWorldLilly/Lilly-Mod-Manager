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
        private void DrawSearchField(Rect rect)
                {
                    const float labelWidth = 42f;
                    const float clearButtonWidth = 24f;
                    const float searchScopeWidth = 82f;
                    const float gap = 3f;
                    const float checkboxHeight = 28f;
        
                    Widgets.Label(new Rect(rect.x, rect.y, labelWidth, checkboxHeight),
                        ManagerTranslation.Get("LMM_0021"));
        
                    // 각 항목에 반복되던 "필터:" 접두사를 제거해 체크 필터에 더 많은 공간을 확보한다.
                    string ShortFilterLabel(string label) => label
                        .Replace("필터: ", string.Empty)
                        .Replace("Filter: ", string.Empty);
        
                    string[] filterLabels =
                    {
                        ShortFilterLabel(ManagerTranslation.Get("LMM_0115")),
                        ShortFilterLabel(ManagerTranslation.Get("LMM_0112")),
                        ShortFilterLabel(ManagerTranslation.Get("LMM_0110")),
                        ShortFilterLabel(ManagerTranslation.Get("LMM_0114")),
                        ShortFilterLabel(ManagerTranslation.Get("LMM_0111")),
                        ManagerTranslation.Get("LMM_0175"),
                        ManagerTranslation.Get("LMM_0176"),
                        ManagerTranslation.Get("LMM_0177"),
                        ManagerTranslation.Get("LMM_0178"),
                        ManagerTranslation.Get("LMM_0179"),
                        ManagerTranslation.Get("LMM_0180"),
                        ManagerTranslation.Get("LMM_0181")
                    };
                    // CheckboxLabeled의 체크 표시 영역과 텍스트 여백을 충분히 확보한다.
                    float[] filterWidths = filterLabels
                        .Select(label => Text.CalcSize(label).x + 30f).ToArray();
        
                    float andWidth = Text.CalcSize(ManagerTranslation.Get("LMM_0140")).x + 18f;
                    float orWidth = Text.CalcSize(ManagerTranslation.Get("LMM_0141")).x + 18f;
                    float filterGroupWidth = filterWidths.Sum() +
                        gap * (filterWidths.Length - 1) + gap + andWidth + orWidth;
                    float searchX = rect.x + labelWidth;
                    // 검색어 유무와 관계없이 TextField의 Rect를 고정해 IMGUI control ID가 바뀌지 않게 한다.
                    float searchWidth = Mathf.Min(300f, Mathf.Max(60f,
                        rect.width - labelWidth - clearButtonWidth - searchScopeWidth -
                        gap * 3f - filterGroupWidth));
                    GUI.SetNextControlName("LillyIntegratedModManagerSearch");
                    string newSearchText = Widgets.TextField(
                        new Rect(searchX, rect.y, searchWidth, checkboxHeight), searchText);
                    if (!string.Equals(newSearchText, searchText, StringComparison.Ordinal))
                    {
                        searchText = newSearchText;
                        selectedPackageIds.Clear();
                        primarySelectedPackageId = null;
                        lastSelectedIndex = -1;
                        RefreshVisibleMods();
                    }
        
                    float clearX = searchX + searchWidth + gap;
                    if (!searchText.NullOrEmpty() &&
                        Widgets.ButtonText(new Rect(clearX, rect.y, clearButtonWidth, checkboxHeight), "×"))
                    {
                        searchText = "";
                        selectedPackageIds.Clear();
                        primarySelectedPackageId = null;
                        lastSelectedIndex = -1;
                        RefreshVisibleMods();
                        GUI.FocusControl("LillyIntegratedModManagerSearch");
                    }
        
                    float scopeX = clearX + clearButtonWidth + gap;
                    if (Widgets.ButtonText(new Rect(scopeX, rect.y, searchScopeWidth, checkboxHeight),
                        GetSearchScopeLabel(searchScope) + " ▼"))
                        OpenSearchScopeMenu();
                    
                    float filterX = scopeX + searchScopeWidth + gap;
                    bool filtersChanged = false;
                    bool activeChanged = DrawFilterCheckbox(ref filterActive, filterLabels[0],
                        new Rect(filterX, rect.y, filterWidths[0], checkboxHeight));
                    filtersChanged |= activeChanged;
                    filterX += filterWidths[0] + gap;
                    bool inactiveChanged = DrawFilterCheckbox(ref filterInactive, filterLabels[1],
                        new Rect(filterX, rect.y, filterWidths[1], checkboxHeight));
                    filtersChanged |= inactiveChanged;
                    filterX += filterWidths[1] + gap;
                    filtersChanged |= DrawFilterCheckbox(ref filterSubscribed, filterLabels[2],
                        new Rect(filterX, rect.y, filterWidths[2], checkboxHeight));
                    filterX += filterWidths[2] + gap;
                    filtersChanged |= DrawFilterCheckbox(ref filterUntagged, filterLabels[3],
                        new Rect(filterX, rect.y, filterWidths[3], checkboxHeight));
                    filterX += filterWidths[3] + gap;
                    filtersChanged |= DrawFilterCheckbox(ref filterUncategorized, filterLabels[4],
                        new Rect(filterX, rect.y, filterWidths[4], checkboxHeight));
                    filterX += filterWidths[4] + gap;
                    filtersChanged |= DrawFilterCheckbox(ref filterLoadOrderIssue, filterLabels[5],
                        new Rect(filterX, rect.y, filterWidths[5], checkboxHeight));
                    filterX += filterWidths[5] + gap;
                    filtersChanged |= DrawFilterCheckbox(ref filterMissingRequired, filterLabels[6],
                        new Rect(filterX, rect.y, filterWidths[6], checkboxHeight));
                    filterX += filterWidths[6] + gap;
                    filtersChanged |= DrawFilterCheckbox(ref filterVersionMismatch, filterLabels[7],
                        new Rect(filterX, rect.y, filterWidths[7], checkboxHeight));
                    filterX += filterWidths[7] + gap;
                    filtersChanged |= DrawFilterCheckbox(ref filterLocal, filterLabels[8],
                        new Rect(filterX, rect.y, filterWidths[8], checkboxHeight));
                    filterX += filterWidths[8] + gap;
                    filtersChanged |= DrawFilterCheckbox(ref filterWorkshop, filterLabels[9],
                        new Rect(filterX, rect.y, filterWidths[9], checkboxHeight));
                    filterX += filterWidths[9] + gap;
                    filtersChanged |= DrawFilterCheckbox(ref filterOfficial, filterLabels[10],
                        new Rect(filterX, rect.y, filterWidths[10], checkboxHeight));
                    filterX += filterWidths[10] + gap;
                    filtersChanged |= DrawFilterCheckbox(ref filterNoFolder, filterLabels[11],
                        new Rect(filterX, rect.y, filterWidths[11], checkboxHeight));
                    filterX += filterWidths[11] + gap;
        
                    bool priorFilterUseAnd = filterUseAnd;
                    if (Widgets.RadioButtonLabeled(new Rect(filterX, rect.y, andWidth, checkboxHeight),
                        ManagerTranslation.Get("LMM_0140"), filterUseAnd))
                        filterUseAnd = true;
                    filterX += andWidth;
                    if (Widgets.RadioButtonLabeled(new Rect(filterX, rect.y, orWidth, checkboxHeight),
                        ManagerTranslation.Get("LMM_0141"), !filterUseAnd))
                        filterUseAnd = false;
                    filtersChanged |= priorFilterUseAnd != filterUseAnd;
        
                    if (filtersChanged)
                    {
                        if (activeChanged && filterActive)
                            filterInactive = false;
                        if (inactiveChanged && filterInactive)
                            filterActive = false;
                        RefreshVisibleMods();
                    }
                }
        
        private static string GetSearchScopeLabel(SearchScope scope)
                {
                    switch (scope)
                    {
                        case SearchScope.Name: return ManagerTranslation.Get("LMM_0182");
                        case SearchScope.Description: return ManagerTranslation.Get("LMM_0183");
                        case SearchScope.Author: return ManagerTranslation.Get("LMM_0184");
                        case SearchScope.PackageId: return "PackageId";
                        default: return ManagerTranslation.Get("LMM_0190");
                    }
                }
        
        private void OpenSearchScopeMenu()
                {
                    SearchScope[] scopes =
                    {
                        SearchScope.All,
                        SearchScope.Name,
                        SearchScope.Description,
                        SearchScope.Author,
                        SearchScope.PackageId
                    };
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    for (int i = 0; i < scopes.Length; i++)
                    {
                        SearchScope scope = scopes[i];
                        options.Add(new FloatMenuOption(GetSearchScopeLabel(scope),
                            () =>
                            {
                                if (searchScope == scope)
                                    return;
                                searchScope = scope;
                                selectedPackageIds.Clear();
                                primarySelectedPackageId = null;
                                lastSelectedIndex = -1;
                                RefreshVisibleMods();
                            }));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
        
        private static Color GetReadableTextColor(Color background)
                {
                    // 색상 선택기와 동일한 선형 sRGB 상대 휘도 기준으로 글자 대비를 계산한다.
                    return ManagerTagColors.Foreground(background);
                }
        
        private static bool DrawFilterCheckbox(ref bool value, string label, Rect rect)
                {
                    bool previous = value;
                    Widgets.CheckboxLabeled(rect, label, ref value);
                    return previous != value;
                }
    }
}
