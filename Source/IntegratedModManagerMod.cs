using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Lilly.IntegratedModManager
{
    public sealed class IntegratedModManagerMod : Mod
    {
        internal static IntegratedModManagerMod Instance { get; private set; }

        internal IntegratedModManagerSettings Settings { get; private set; }

        public IntegratedModManagerMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<IntegratedModManagerSettings>();
        }

        public override string SettingsCategory()
        {
            return "Lilly - Integrated Mod Manager";
        }
    }

    public sealed class IntegratedModManagerSettings : ModSettings
    {
        public List<ManagerCategoryData> Categories = new List<ManagerCategoryData>();
        public List<ManagerTagData> Tags = new List<ManagerTagData>();
        public List<ManagerModData> Mods = new List<ManagerModData>();
        public List<ManagerPresetData> Presets = new List<ManagerPresetData>();
        public List<string> FavoriteFolderKeys = new List<string>();
        public List<string> RecentFolderKeys = new List<string>();
        public List<string> ExcludedPackageIds = new List<string>();
        public bool AutoTagNewMods = true;
        public bool AutoTagUpdatedMods = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref AutoTagNewMods, "autoTagNewMods", true);
            Scribe_Values.Look(ref AutoTagUpdatedMods, "autoTagUpdatedMods", true);
            Scribe_Collections.Look(ref Categories, "categories", LookMode.Deep);
            Scribe_Collections.Look(ref Tags, "tags", LookMode.Deep);
            Scribe_Collections.Look(ref Mods, "mods", LookMode.Deep);
            Scribe_Collections.Look(ref Presets, "presets", LookMode.Deep);
            Scribe_Collections.Look(ref FavoriteFolderKeys, "favoriteFolderKeys", LookMode.Value);
            Scribe_Collections.Look(ref RecentFolderKeys, "recentFolderKeys", LookMode.Value);
            Scribe_Collections.Look(ref ExcludedPackageIds, "excludedPackageIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                EnsureInitialized();
        }

        internal void EnsureInitialized()
        {
            if (Categories == null)
                Categories = new List<ManagerCategoryData>();
            if (Mods == null)
                Mods = new List<ManagerModData>();
            if (Tags == null)
                Tags = new List<ManagerTagData>();
            if (Presets == null)
                Presets = new List<ManagerPresetData>();
            if (FavoriteFolderKeys == null)
                FavoriteFolderKeys = new List<string>();
            if (RecentFolderKeys == null)
                RecentFolderKeys = new List<string>();
            if (ExcludedPackageIds == null)
                ExcludedPackageIds = new List<string>();
            for (int i = Presets.Count - 1; i >= 0; i--)
            {
                if (Presets[i] == null || Presets[i].Id.NullOrEmpty() ||
                    Presets[i].Name.NullOrEmpty())
                    Presets.RemoveAt(i);
            }

            if (Categories.Count == 0)
            {
                Categories.Add(new ManagerCategoryData("before", ManagerTranslation.Get("LMM_0058")));
                Categories.Add(new ManagerCategoryData("core", ManagerTranslation.Get("LMM_0116")));
                Categories.Add(new ManagerCategoryData("normal", ManagerTranslation.Get("LMM_0065")));
                Categories.Add(new ManagerCategoryData("after", ManagerTranslation.Get("LMM_0121")));
            }

            bool hasUntagged = false;
            for (int i = 0; i < Tags.Count; i++)
            {
                ManagerTagData tag = Tags[i];
                if (tag == null)
                    continue;
                if (string.Equals(tag.Id, "untagged", StringComparison.OrdinalIgnoreCase))
                {
                    hasUntagged = true;
                    tag.IsSystem = true;
                    if (tag.Name.NullOrEmpty())
                        tag.Name = ManagerTranslation.Get("LMM_0082");
                }
                if (!ManagerTagColors.IsValidHex(tag.ColorHex))
                    tag.ColorHex = ManagerTagColors.RandomHex();
            }
            if (!hasUntagged)
                Tags.Insert(0, new ManagerTagData("untagged", ManagerTranslation.Get("LMM_0082")));

            ManagerCategoryData coreCategory = FindCategory("core") ?? Categories[0];
            ManagerCategoryData beforeCategory = FindCategory("before") ?? coreCategory;
            ManagerCategoryData normalCategory = FindCategory("normal") ?? Categories[Categories.Count - 1];
            int firstCoreLoadIndex = int.MaxValue;
            int activeLoadIndex = 0;
            Dictionary<string, int> activeOrderByPackageId =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (ModMetaData activeMod in ModsConfig.ActiveModsInLoadOrder)
            {
                if (activeMod != null && !activeMod.PackageId.NullOrEmpty())
                    activeOrderByPackageId[activeMod.PackageId] = activeLoadIndex;
                if (activeMod != null && (activeMod.IsCoreMod || activeMod.Official))
                {
                    firstCoreLoadIndex = activeLoadIndex;
                    break;
                }
                activeLoadIndex++;
            }
            HashSet<string> assignedPackageIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> excludedPackageIds = new HashSet<string>(ExcludedPackageIds,
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < Categories.Count; i++)
            {
                ManagerCategoryData category = Categories[i];
                if (category == null)
                {
                    Categories.RemoveAt(i--);
                    continue;
                }
// 기존 저장 데이터에 색상이 없으면 자동 생성
// if (!ManagerTagColors.IsValidHex(category.ColorHex))
//     category.ColorHex = ManagerTagColors.RandomHex();

                if (category.ModIds == null)
                    category.ModIds = new List<string>();

                category.ModIds.RemoveAll(excludedPackageIds.Contains);
                if (category.Folders == null)
                    category.Folders = new List<ManagerFolderData>();
                if (category.Folders.Count == 0)
                    category.Folders.Add(new ManagerFolderData(
                        "uncategorized", ManagerTranslation.Get("LMM_0040")));

                HashSet<string> folderPackageIds =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int folderIndex = category.Folders.Count - 1; folderIndex >= 0; folderIndex--)
                {
                    ManagerFolderData folder = category.Folders[folderIndex];
                    if (folder == null)
                    {
                        category.Folders.RemoveAt(folderIndex);
                        continue;
                    }

                    if (folder.ModIds == null)
                        folder.ModIds = new List<string>();
                    folder.ModIds.RemoveAll(excludedPackageIds.Contains);
                    for (int modIndex = folder.ModIds.Count - 1; modIndex >= 0; modIndex--)
                    {
                        string packageId = folder.ModIds[modIndex];
                        if (packageId.NullOrEmpty() ||
                            !category.ModIds.Contains(packageId) ||
                            !folderPackageIds.Add(packageId))
                            folder.ModIds.RemoveAt(modIndex);
                    }
                }

                ManagerFolderData uncategorized = FindFolder(category, "uncategorized");
                if (uncategorized == null)
                {
                    uncategorized = new ManagerFolderData("uncategorized", ManagerTranslation.Get("LMM_0040"));
                    category.Folders.Insert(0, uncategorized);
                }

                for (int modIndex = 0; modIndex < category.ModIds.Count; modIndex++)
                {
                    string packageId = category.ModIds[modIndex];
                    if (folderPackageIds.Add(packageId))
                        uncategorized.ModIds.Add(packageId);
                }

                for (int j = category.ModIds.Count - 1; j >= 0; j--)
                {
                    string packageId = category.ModIds[j];
                    if (packageId.NullOrEmpty() || !assignedPackageIds.Add(packageId))
                        category.ModIds.RemoveAt(j);
                }
            }

            // ModsConfig.ActiveModsInLoadOrder는 ModsConfig.xml에 기록된 활성 모드 순서를 유지한다.
            // 최초 목록 생성 시 이 순서를 먼저 반영하고, 나머지 설치 모드는 뒤에 보충한다.
            foreach (ModMetaData mod in ModsConfig.ActiveModsInLoadOrder.Concat(ModLister.AllInstalledMods))
            {
                if (mod == null || mod.PackageId.NullOrEmpty())
                    continue;

                string packageId = mod.PackageId;
                if (excludedPackageIds.Contains(packageId))
                    continue;

                // 초기 설정 생성 시에는 현재 설치된 모드를 기존 목록의 기준 데이터로 등록한다.
                // 신규 태그는 이후 목록 갱신에서 새로 발견된 모드에만 부여한다.
                GetOrCreateModData(mod);

                if (assignedPackageIds.Add(packageId))
                {
                    ManagerCategoryData category = coreCategory;
                    if (!mod.IsCoreMod && !mod.Official)
                    {
                        int modLoadIndex;
                        if (!activeOrderByPackageId.TryGetValue(packageId, out modLoadIndex))
                            modLoadIndex = -1;
                        category = modLoadIndex >= 0 && modLoadIndex < firstCoreLoadIndex
                            ? beforeCategory
                            : normalCategory;
                    }
                    category.ModIds.Add(packageId);
                    ManagerFolderData uncategorized = FindFolder(category, "uncategorized");
                    if (uncategorized == null)
                    {
                        uncategorized = new ManagerFolderData("uncategorized", ManagerTranslation.Get("LMM_0040"));
                        category.Folders.Insert(0, uncategorized);
                    }
                    uncategorized.ModIds.Add(packageId);
                }
            }

            Mods.RemoveAll(modData => modData == null ||
                excludedPackageIds.Contains(modData.PackageId));

            for (int tagIndex = Tags.Count - 1; tagIndex >= 0; tagIndex--)
            {
                ManagerTagData tag = Tags[tagIndex];
                if (tag == null || tag.Id.NullOrEmpty())
                {
                    Tags.RemoveAt(tagIndex);
                    continue;
                }

                if (tag.Folders == null)
                    tag.Folders = new List<ManagerFolderData>();
                if (tag.Folders.Count == 0)
                    tag.Folders.Add(new ManagerFolderData("uncategorized", ManagerTranslation.Get("LMM_0040")));

                HashSet<string> linkedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int folderIndex = tag.Folders.Count - 1; folderIndex >= 0; folderIndex--)
                {
                    ManagerFolderData folder = tag.Folders[folderIndex];
                    if (folder == null)
                    {
                        tag.Folders.RemoveAt(folderIndex);
                        continue;
                    }

                    if (folder.ModIds == null)
                        folder.ModIds = new List<string>();
                    for (int modIndex = folder.ModIds.Count - 1; modIndex >= 0; modIndex--)
                    {
                        string packageId = folder.ModIds[modIndex];
                        ManagerModData modData = FindModData(packageId);
                        bool belongsToTag = modData != null &&
                            (tag.IsSystem
                                ? modData.TagIds == null || modData.TagIds.Count == 0
                                : modData.TagIds != null && modData.TagIds.Contains(tag.Id));
                        if (packageId.NullOrEmpty() || modData == null ||
                            !belongsToTag || !linkedIds.Add(packageId))
                            folder.ModIds.RemoveAt(modIndex);
                    }
                }

                ManagerFolderData unfiled = FindTagFolder(tag, "uncategorized");
                if (unfiled == null)
                {
                    unfiled = new ManagerFolderData("uncategorized", ManagerTranslation.Get("LMM_0040"));
                    tag.Folders.Insert(0, unfiled);
                }

                for (int modIndex = 0; modIndex < Mods.Count; modIndex++)
                {
                    ManagerModData modData = Mods[modIndex];
                    bool belongsToTag = modData != null &&
                        (tag.IsSystem
                            ? modData.TagIds == null || modData.TagIds.Count == 0
                            : modData.TagIds != null && modData.TagIds.Contains(tag.Id));
                    if (belongsToTag && linkedIds.Add(modData.PackageId))
                        unfiled.ModIds.Add(modData.PackageId);
                }
            }
        }

        private static ManagerFolderData FindFolder(ManagerCategoryData category, string id)
        {
            if (category.Folders == null)
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

        private static ManagerFolderData FindTagFolder(ManagerTagData tag, string id)
        {
            if (tag.Folders == null)
                return null;
            for (int i = 0; i < tag.Folders.Count; i++)
            {
                if (tag.Folders[i] != null &&
                    string.Equals(tag.Folders[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return tag.Folders[i];
            }
            return null;
        }

        internal ManagerCategoryData FindCategory(string id)
        {
            if (Categories == null)
                return null;

            for (int i = 0; i < Categories.Count; i++)
            {
                if (Categories[i] != null &&
                    string.Equals(Categories[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return Categories[i];
            }
            return null;
        }

        internal ManagerModData FindModData(string packageId)
        {
            if (Mods == null)
                return null;

            for (int i = 0; i < Mods.Count; i++)
            {
                if (Mods[i] != null &&
                    string.Equals(Mods[i].PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                    return Mods[i];
            }
            return null;
        }

        internal ManagerTagData FindTag(string id)
        {
            if (Tags == null)
                return null;
            for (int i = 0; i < Tags.Count; i++)
            {
                if (Tags[i] != null &&
                    string.Equals(Tags[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return Tags[i];
            }
            return null;
        }

        internal ManagerModData GetOrCreateModData(ModMetaData mod)
        {
            ManagerModData data = FindModData(mod.PackageId);
            if (data != null)
                return data;

            data = new ManagerModData(mod.PackageId, mod.Active);
            Mods.Add(data);
            return data;
        }
    }

    public sealed class ManagerCategoryData : IExposable
    {
        public string Id;
        public string Name;
        public bool Enabled = true;

    // 카테고리 배경색
    public string ColorHex;
    
        public List<string> ModIds = new List<string>();
        public List<ManagerFolderData> Folders = new List<ManagerFolderData>();

        public ManagerCategoryData()
        {
        }

        public ManagerCategoryData(string id, string name)
        {
            Id = id;
            Name = name;

              // 기본 색상 자동 지정
    ColorHex = ColorHex = null;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref Name, "name");
            Scribe_Values.Look(ref Enabled, "enabled", true);


    // 카테고리 색상 저장 및 불러오기
    Scribe_Values.Look(ref ColorHex, "colorHex", null);
    
            Scribe_Collections.Look(ref ModIds, "mods", LookMode.Value);
            Scribe_Collections.Look(ref Folders, "folders", LookMode.Deep);
        }

        internal ManagerCategoryData Copy()
        {
            ManagerCategoryData copy = new ManagerCategoryData(Id, Name);

            copy.Enabled = Enabled;

            // 색상 복사
            copy.ColorHex = ManagerTagColors.IsValidHex(ColorHex)
                ? ColorHex
                : null;

            copy.ModIds = ModIds == null
                ? new List<string>()
                : new List<string>(ModIds);

            copy.Folders = Folders == null
                ? new List<ManagerFolderData>()
                : Folders.Select(folder => folder.Copy()).ToList();

            return copy;
        }

    }

    public sealed class ManagerFolderData : IExposable
    {
        public string Id;
        public string Name;
        public bool Collapsed;
        public List<string> ModIds = new List<string>();

        public ManagerFolderData()
        {
        }

        public ManagerFolderData(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref Name, "name");
            Scribe_Values.Look(ref Collapsed, "collapsed", false);
            Scribe_Collections.Look(ref ModIds, "mods", LookMode.Value);
        }

        internal ManagerFolderData Copy()
        {
            ManagerFolderData copy = new ManagerFolderData(Id, Name);
            copy.Collapsed = Collapsed;
            copy.ModIds = ModIds == null
                ? new List<string>()
                : new List<string>(ModIds);
            return copy;
        }
    }

    public sealed class ManagerTagData : IExposable
    {
        public string Id;
        public string Name;
        public bool IsSystem;
        public string ColorHex;
        public List<ManagerFolderData> Folders = new List<ManagerFolderData>();

        public ManagerTagData()
        {
        }

        public ManagerTagData(string id, string name)
        {
            Id = id;
            Name = name;
            IsSystem = string.Equals(id, "untagged", StringComparison.OrdinalIgnoreCase);
            ColorHex = ManagerTagColors.RandomHex();
            Folders.Add(new ManagerFolderData("uncategorized", ManagerTranslation.Get("LMM_0040")));
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref Name, "name");
            Scribe_Values.Look(ref IsSystem, "isSystem", false);
            Scribe_Values.Look(ref ColorHex, "colorHex", null);
            Scribe_Collections.Look(ref Folders, "folders", LookMode.Deep);
        }

        internal ManagerTagData Copy()
        {
            ManagerTagData copy = new ManagerTagData(Id, Name);
            copy.IsSystem = IsSystem;
            copy.ColorHex = ColorHex;
            copy.Folders = Folders == null
                ? new List<ManagerFolderData>()
                : Folders.Select(folder => folder.Copy()).ToList();
            return copy;
        }
    }

    public sealed class ManagerModData : IExposable
    {
        public string PackageId;
        public bool Enabled = true;
        public string DisplayName;
        public string ColorHex;
        public string InstalledVersion;
        public List<string> TagIds = new List<string>();

        public ManagerModData()
        {
        }

        public ManagerModData(string packageId, bool enabled)
        {
            PackageId = packageId;
            Enabled = enabled;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref PackageId, "packageId");
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref DisplayName, "displayName", null);
            Scribe_Values.Look(ref ColorHex, "colorHex", null);
            Scribe_Values.Look(ref InstalledVersion, "installedVersion", null);
            Scribe_Collections.Look(ref TagIds, "tags", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && TagIds == null)
                TagIds = new List<string>();
        }

        internal ManagerModData Copy()
        {
            ManagerModData copy = new ManagerModData(PackageId, Enabled);
            copy.DisplayName = DisplayName;
            copy.ColorHex = ColorHex;
            copy.InstalledVersion = InstalledVersion;
            copy.TagIds = TagIds == null ? new List<string>() : new List<string>(TagIds);
            return copy;
        }
    }

    public sealed class ManagerPresetData : IExposable
    {
        public string Id;
        public string Name;
        public List<ManagerCategoryData> Categories = new List<ManagerCategoryData>();
        public List<ManagerTagData> Tags = new List<ManagerTagData>();
        public List<ManagerModData> Mods = new List<ManagerModData>();

        public ManagerPresetData()
        {
        }

        public ManagerPresetData(
            string id,
            string name,
            IEnumerable<ManagerCategoryData> categories,
            IEnumerable<ManagerTagData> tags,
            IEnumerable<ManagerModData> mods)
        {
            Id = id;
            Name = name;
            Categories = categories.Select(category => category.Copy()).ToList();
            Tags = tags.Select(tag => tag.Copy()).ToList();
            Mods = mods.Select(mod => mod.Copy()).ToList();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref Name, "name");
            Scribe_Collections.Look(ref Categories, "categories", LookMode.Deep);
            Scribe_Collections.Look(ref Tags, "tags", LookMode.Deep);
            Scribe_Collections.Look(ref Mods, "mods", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Categories == null)
                    Categories = new List<ManagerCategoryData>();
                if (Tags == null)
                    Tags = new List<ManagerTagData>();
                if (Mods == null)
                    Mods = new List<ManagerModData>();
            }
        }

        internal ManagerPresetData Copy()
        {
            return new ManagerPresetData(Id, Name, Categories, Tags, Mods);
        }
    }
}
