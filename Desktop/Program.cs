using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;
using System.Windows.Forms;

namespace Lilly.ModManager.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportStartupError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
                ReportStartupError(exception);
        };

        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            ReportStartupError(ex);
        }
    }

    private static void ReportStartupError(Exception exception)
    {
        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lilly Mod Manager");
        string logPath = Path.Combine(logDirectory, "startup.log");

        try
        {
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Application error{Environment.NewLine}" +
                exception + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
            // Still show the original exception if the diagnostic log cannot be written.
        }

        try
        {
            MessageBox.Show(
                exception + Environment.NewLine + Environment.NewLine +
                $"진단 로그: {logPath}",
                "Lilly Mod Manager 실행 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // MessageBox may be unavailable during process shutdown.
        }
    }
}

internal sealed class ModEntry
{
    public string PackageId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public List<string> SupportedVersions { get; set; } = [];
    public string Path { get; set; } = "";
    public bool Workshop { get; set; }
    public bool Official { get; set; }
    public bool Active { get; set; }
    public string CategoryId { get; set; } = "normal";
    public List<string> LoadAfter { get; set; } = [];
    public List<string> LoadBefore { get; set; } = [];
    public List<string> Required { get; set; } = [];
    public string Issue { get; set; } = "";
    public bool HasMissingFolder { get; set; }
    public bool HasMissingRequiredMod { get; set; }
    public bool HasLoadOrderIssue { get; set; }
    public bool HasVersionMismatch { get; set; }
}

internal sealed class FolderData
{
    public string Id { get; set; } = "uncategorized";
    public string Name { get; set; } = "미분류";
    public bool Collapsed { get; set; }
    public List<string> ModIds { get; set; } = [];
}

internal sealed class CategoryData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string? ColorHex { get; set; }
    public List<string> ModIds { get; set; } = [];
    public List<FolderData> Folders { get; set; } = [];
}

internal sealed class TagData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsSystem { get; set; }
    public string? ColorHex { get; set; }
    public List<FolderData> Folders { get; set; } = [];
}

internal sealed class ModState
{
    public string PackageId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string? DisplayName { get; set; }
    public string? ColorHex { get; set; }
    public string? InstalledVersion { get; set; }
    public List<string> TagIds { get; set; } = [];
}

internal sealed class PresetData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<CategoryData> Categories { get; set; } = [];
    public List<TagData> Tags { get; set; } = [];
    public List<ModState> Mods { get; set; } = [];
}

internal sealed class GamePaths
{
    public string GameRoot { get; set; } = "";
    public string ConfigRoot { get; set; } = "";
    public string ModsConfig => System.IO.Path.Combine(ConfigRoot, "ModsConfig.xml");
    public string ManagerSettings
    {
        get
        {
            string[] candidates =
            [
                System.IO.Path.Combine(ConfigRoot, "Mod_Lilly.ModManager_IntegratedModManagerSettings.xml"),
                System.IO.Path.Combine(ConfigRoot, "ModSettings", "Lilly.ModManager.xml"),
                System.IO.Path.Combine(ConfigRoot, "Mod_Lilly.ModManager.xml"),
                System.IO.Path.Combine(ConfigRoot, "Lilly.ModManager.xml")
            ];
            foreach (string candidate in candidates)
                if (File.Exists(candidate) && ContainsManagerData(candidate)) return candidate;

            if (Directory.Exists(ConfigRoot))
            {
                foreach (string file in Directory.GetFiles(ConfigRoot, "*.xml", SearchOption.AllDirectories))
                    if (ContainsManagerData(file)) return file;
            }

            // RimWorld 1.7 uses a package ID + settings-class filename.
            return System.IO.Path.Combine(ConfigRoot, "Mod_Lilly.ModManager_IntegratedModManagerSettings.xml");
        }
    }

    private static bool ContainsManagerData(string file)
    {
        try
        {
            XDocument doc = XDocument.Load(file);
            return FindManagerSettingsNode(doc.Root) != null;
        }
        catch { return false; }
    }

    internal static XElement? FindManagerSettingsNode(XElement? root)
    {
        if (root == null) return null;

        // RimWorld ModSettings.Write stores the settings object under <settings Class="...">.
        // Match the distinctive fields used by IntegratedModManagerSettings rather than a
        // made-up wrapper element so existing in-game data is read and updated in place.
        return root.DescendantsAndSelf().FirstOrDefault(element =>
        {
            string[] names = element.Elements().Select(child => child.Name.LocalName).ToArray();
            bool hasCollections = names.Contains("categories") &&
                names.Contains("tags") && names.Contains("mods");
            bool hasManagerOptions = names.Contains("autoTagNewMods") ||
                names.Contains("autoTagUpdatedMods") ||
                names.Contains("favoriteFolderKeys") ||
                names.Contains("excludedPackageIds");
            string? className = element.Attribute("Class")?.Value;
            bool hasManagerClass = className?.Contains(
                "IntegratedModManagerSettings", StringComparison.OrdinalIgnoreCase) == true;
            return hasCollections && (hasManagerOptions || hasManagerClass);
        });
    }
    public string PresetDirectory => System.IO.Path.Combine(ConfigRoot, "Lilly Mod Manager Presets");
    public string WorkshopDirectory => System.IO.Path.Combine(GameRoot, "..", "..", "workshop", "content", "294100");
}

internal sealed class ModStore
{
    public GamePaths Paths { get; private set; }
    public List<ModEntry> Mods { get; private set; } = [];
    public List<CategoryData> Categories { get; private set; } = [];
    public List<TagData> Tags { get; private set; } = [];
    public List<ModState> States { get; private set; } = [];
    public List<string> ExcludedPackageIds { get; private set; } = [];
    public List<string> FavoriteFolderKeys { get; private set; } = [];
    public List<string> RecentFolderKeys { get; private set; } = [];
    private Dictionary<string, bool> persistedEnabledStates = new(StringComparer.OrdinalIgnoreCase);
    public bool AutoTagNewMods { get; set; } = true;
    public bool AutoTagUpdatedMods { get; set; } = true;
    public bool MetadataChanged { get; private set; }
    public List<ModEntry> OrderedMods { get; private set; } = [];
    public Version? GameVersion { get; private set; }

    public ModStore(GamePaths paths) => Paths = paths;

    public static GamePaths DiscoverPaths()
    {
        string configBase = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? userProfile = Directory.GetParent(configBase)?.FullName;
        string defaultConfig = System.IO.Path.Combine(userProfile ?? configBase, "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");
        return new GamePaths { GameRoot = FindGameRoot(), ConfigRoot = defaultConfig };
    }

    private static string FindGameRoot()
    {
        var candidates = new List<string>();
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Valve\Steam") ?? baseKey.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                string? path = key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(path)) candidates.Add(path);
            }
            catch { }
        }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string? path = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(path)) candidates.Add(path.Replace('/', '\\'));
        }
        catch { }
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(pf86)) candidates.Add(System.IO.Path.Combine(pf86, "Steam"));

        foreach (string steam in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var libraries = new List<string> { steam };
            string vdf = System.IO.Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                string text = File.ReadAllText(vdf);
                foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                    libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
            }
            foreach (string library in libraries)
            {
                string root = System.IO.Path.Combine(library, "steamapps", "common", "RimWorld");
                if (Directory.Exists(System.IO.Path.Combine(root, "Mods")) || Directory.Exists(System.IO.Path.Combine(root, "Data", "Core"))) return root;
            }
        }
        return "";
    }

    public void Load()
    {
        if (string.IsNullOrWhiteSpace(Paths.ConfigRoot) || !Directory.Exists(Paths.ConfigRoot))
            throw new DirectoryNotFoundException("림월드 Config 폴더를 찾지 못했습니다. Config 폴더를 직접 지정하세요.");
        if (string.IsNullOrWhiteSpace(Paths.GameRoot) || !Directory.Exists(Paths.GameRoot))
            throw new DirectoryNotFoundException("림월드 설치 폴더를 찾지 못했습니다. 게임 설치 폴더를 직접 지정하세요.");
        if (!File.Exists(Paths.ModsConfig)) throw new FileNotFoundException("ModsConfig.xml 파일이 없습니다.", Paths.ModsConfig);

        GameVersion = ReadGameVersion(Paths.GameRoot);
        List<string> activeIds = ReadActiveMods();
        var activeSet = new HashSet<string>(activeIds, StringComparer.OrdinalIgnoreCase);
        Mods = ScanMods().GroupBy(m => m.PackageId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        foreach (var mod in Mods) mod.Active = activeSet.Contains(mod.PackageId) || mod.Official;
        OrderedMods = new List<ModEntry>();
        foreach (string id in activeIds)
        {
            ModEntry? installed = Mods.FirstOrDefault(m => m.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase));
            // Missing active entries remain visible so applying changes cannot silently remove them.
            OrderedMods.Add(installed ?? new ModEntry
            {
                PackageId = id,
                Name = id,
                Active = true,
                Path = "",
                Issue = "설치된 모드 폴더 없음"
            });
        }
        OrderedMods.AddRange(Mods.Where(m => !activeSet.Contains(m.PackageId)).OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase));
        LoadManagerData();
        ApplyCategoryAndTagState();
        RebuildIssues();
    }

    private static Version? ReadGameVersion(string gameRoot)
    {
        string[] candidates =
        {
            System.IO.Path.Combine(gameRoot, "Version.txt"),
            System.IO.Path.Combine(gameRoot, "Data", "Version.txt")
        };
        foreach (string path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                Match match = Regex.Match(File.ReadAllText(path), @"(?<!\d)(\d+)\.(\d+)(?:\.(\d+))?");
                if (!match.Success) continue;
                int major = int.Parse(match.Groups[1].Value);
                int minor = int.Parse(match.Groups[2].Value);
                int build = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
                return new Version(major, minor, build);
            }
            catch { }
        }
        return null;
    }

    private List<string> ReadActiveMods()
    {
        XDocument doc = XDocument.Load(Paths.ModsConfig);
        XElement? list = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "activeMods");
        return list?.Elements().Where(e => e.Name.LocalName == "li").Select(e => e.Value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
    }

    private IEnumerable<ModEntry> ScanMods()
    {
        var directories = new List<(string Path, bool Workshop, bool Official)>();
        string modsDir = System.IO.Path.Combine(Paths.GameRoot, "Mods");
        if (Directory.Exists(modsDir))
            foreach (string dir in Directory.GetDirectories(modsDir)) directories.Add((dir, false, false));

        string dataDir = System.IO.Path.Combine(Paths.GameRoot, "Data");
        if (Directory.Exists(dataDir))
            foreach (string dir in Directory.GetDirectories(dataDir))
                if (File.Exists(System.IO.Path.Combine(dir, "About", "About.xml"))) directories.Add((dir, false, true));
        string workshopDir = Paths.WorkshopDirectory;
        if (Directory.Exists(workshopDir))
            foreach (string dir in Directory.GetDirectories(workshopDir)) directories.Add((dir, true, false));

        foreach (var item in directories)
        {
            string aboutPath = System.IO.Path.Combine(item.Path, "About", "About.xml");
            if (!File.Exists(aboutPath)) continue;
            XDocument doc;
            try { doc = XDocument.Load(aboutPath); } catch { continue; }
            XElement? root = doc.Root;
            if (root == null) continue;
            string packageId = Value(root, "packageId");
            if (packageId.Length == 0) continue;
            var m = new ModEntry
            {
                PackageId = packageId,
                Name = Value(root, "name") is { Length: > 0 } name ? name : packageId,
                Author = Value(root, "author"),
                Version = Value(root, "modVersion"),
                Description = Value(root, "description"),
                Url = Value(root, "url"),
                SupportedVersions = ListValues(root, "supportedVersions"),
                Path = item.Path,
                Workshop = item.Workshop,
                Official = item.Official || packageId.StartsWith("ludeon.rimworld", StringComparison.OrdinalIgnoreCase)
            };
            m.LoadAfter = ListValues(root, "loadAfter");
            m.LoadBefore = ListValues(root, "loadBefore");
            XElement? dependencies = root.Elements().FirstOrDefault(e => e.Name.LocalName == "modDependencies");
            m.Required = dependencies?.Elements().Where(e => e.Name.LocalName == "li")
                .Where(e => !bool.TryParse(Value(e, "MayRequire"), out bool optional) || !optional)
                .Select(e => Value(e, "packageId")).Where(x => x.Length > 0).ToList() ?? [];
            yield return m;
        }
    }

    private static XElement? Child(XElement? parent, string name) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static string Value(XElement parent, string name) => Child(parent, name)?.Value.Trim() ?? "";
    private static List<string> ListValues(XElement parent, string listName)
    {
        XElement? list = parent.Elements().FirstOrDefault(e => e.Name.LocalName == listName);
        return list?.Elements().Where(e => e.Name.LocalName == "li").Select(e => e.Value.Trim()).Where(s => s.Length > 0).ToList() ?? [];
    }

    private void LoadManagerData()
    {
        Categories = [];
        Tags = [];
        States = [];
        ExcludedPackageIds = [];
        FavoriteFolderKeys = [];
        RecentFolderKeys = [];
        persistedEnabledStates.Clear();
        AutoTagNewMods = true;
        AutoTagUpdatedMods = true;
        MetadataChanged = false;
        if (File.Exists(Paths.ManagerSettings))
        {
            try
            {
                XDocument doc = XDocument.Load(Paths.ManagerSettings);
                XElement? managerSettings = GamePaths.FindManagerSettingsNode(doc.Root);
                if (managerSettings != null)
                {
                    Categories = ParseCategories(Child(managerSettings, "categories"));
                    Tags = ParseTags(Child(managerSettings, "tags"));
                    States = ParseStates(Child(managerSettings, "mods"));
                    ExcludedPackageIds = ListValues(managerSettings, "excludedPackageIds");
                    FavoriteFolderKeys = ListValues(managerSettings, "favoriteFolderKeys");
                    RecentFolderKeys = ListValues(managerSettings, "recentFolderKeys");
                    AutoTagNewMods = BoolValue(managerSettings, "autoTagNewMods", true);
                    AutoTagUpdatedMods = BoolValue(managerSettings, "autoTagUpdatedMods", true);
                }
            }
            catch { }
        }
        if (Categories.Count == 0)
        {
            Categories =
            [
                NewCategory("before", "선행 모드"),
                NewCategory("core", "핵심 모드"),
                NewCategory("normal", "일반 모드"),
                NewCategory("after", "후순위 모드")
            ];
        }
        if (!Tags.Any(t => t.Id.Equals("untagged", StringComparison.OrdinalIgnoreCase)))
        {
            Tags.Insert(0, new TagData { Id = "untagged", Name = "태그 없음", IsSystem = true, Folders = [NewFolder()] });
        }

        var excluded = new HashSet<string>(ExcludedPackageIds, StringComparer.OrdinalIgnoreCase);
        Mods.RemoveAll(mod => excluded.Contains(mod.PackageId));
        OrderedMods.RemoveAll(mod => excluded.Contains(mod.PackageId));
        foreach (CategoryData category in Categories)
        {
            category.ModIds.RemoveAll(excluded.Contains);
            foreach (FolderData folder in category.Folders)
                folder.ModIds.RemoveAll(excluded.Contains);
        }
        foreach (TagData tag in Tags)
            foreach (FolderData folder in tag.Folders)
                folder.ModIds.RemoveAll(excluded.Contains);
        States.RemoveAll(state => excluded.Contains(state.PackageId));

        // An empty state list is the initial baseline, not a batch of newly installed mods.
        // Once at least one saved state exists, missing installed package IDs are genuinely new.
        States = States
            .Where(state => !string.IsNullOrWhiteSpace(state.PackageId))
            .GroupBy(state => state.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        bool hasExistingBaseline = States.Count > 0;
        var stateById = States.ToDictionary(state => state.PackageId, StringComparer.OrdinalIgnoreCase);
        foreach (ModEntry mod in OrderedMods)
        {
            if (!stateById.TryGetValue(mod.PackageId, out ModState? state))
            {
                state = new ModState
                {
                    PackageId = mod.PackageId,
                    Enabled = mod.Active,
                    InstalledVersion = NullIfEmpty(mod.Version)
                };
                States.Add(state);
                stateById[mod.PackageId] = state;
                MetadataChanged = true;

                if (hasExistingBaseline && !string.IsNullOrWhiteSpace(mod.Path) && AutoTagNewMods)
                    AddAutomaticTag(state, "auto-new", "신규");
                continue;
            }

            if (string.IsNullOrWhiteSpace(mod.Path) || string.IsNullOrWhiteSpace(mod.Version))
                continue;

            if (!string.IsNullOrWhiteSpace(state.InstalledVersion) &&
                !state.InstalledVersion.Equals(mod.Version, StringComparison.OrdinalIgnoreCase))
            {
                if (AutoTagUpdatedMods)
                    AddAutomaticTag(state, "auto-updated", "변경됨");
                MetadataChanged = true;
            }

            if (!string.Equals(state.InstalledVersion, mod.Version, StringComparison.OrdinalIgnoreCase))
            {
                state.InstalledVersion = mod.Version;
                MetadataChanged = true;
            }
        }
        AssignUnclassifiedMods();
        persistedEnabledStates = States.ToDictionary(
            state => state.PackageId, state => state.Enabled, StringComparer.OrdinalIgnoreCase);
    }

    private void AddAutomaticTag(ModState state, string tagId, string tagName)
    {
        TagData? tag = Tags.FirstOrDefault(candidate =>
            candidate.Id.Equals(tagId, StringComparison.OrdinalIgnoreCase));
        tag ??= Tags.FirstOrDefault(candidate =>
            candidate.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        if (tag == null)
        {
            tag = new TagData
            {
                Id = tagId,
                Name = tagName,
                Folders = [NewFolder()]
            };
            Tags.Add(tag);
            MetadataChanged = true;
        }
        if (!state.TagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase))
        {
            state.TagIds.Add(tag.Id);
            MetadataChanged = true;
        }
    }

    private static CategoryData NewCategory(string id, string name) => new() { Id = id, Name = name, Folders = [NewFolder()] };
    private static FolderData NewFolder() => new() { Id = "uncategorized", Name = "미분류" };

    private static List<CategoryData> ParseCategories(XElement? list) => list?.Elements().Where(e => e.Name.LocalName == "li").Select(e => new CategoryData
    {
        Id = Value(e, "id"), Name = Value(e, "name"), Enabled = BoolValue(e, "enabled", true), ColorHex = NullIfEmpty(Value(e, "colorHex")),
        ModIds = ListValues(e, "mods"), Folders = ParseFolders(Child(e, "folders"))
    }).Where(c => c.Id.Length > 0).ToList() ?? [];
    private static List<TagData> ParseTags(XElement? list) => list?.Elements().Where(e => e.Name.LocalName == "li").Select(e => new TagData
    {
        Id = Value(e, "id"), Name = Value(e, "name"), IsSystem = BoolValue(e, "isSystem", false), ColorHex = NullIfEmpty(Value(e, "colorHex")), Folders = ParseFolders(Child(e, "folders"))
    }).Where(t => t.Id.Length > 0).ToList() ?? [];
    private static List<ModState> ParseStates(XElement? list) => list?.Elements().Where(e => e.Name.LocalName == "li").Select(e => new ModState
    {
        PackageId = Value(e, "packageId"), Enabled = BoolValue(e, "enabled", true), DisplayName = NullIfEmpty(Value(e, "displayName")), ColorHex = NullIfEmpty(Value(e, "colorHex")), InstalledVersion = NullIfEmpty(Value(e, "installedVersion")), TagIds = ListValues(e, "tags")
    }).Where(s => s.PackageId.Length > 0).ToList() ?? [];
    private static List<FolderData> ParseFolders(XElement? list) => list?.Elements().Where(e => e.Name.LocalName == "li").Select(e => new FolderData
    {
        Id = Value(e, "id"), Name = Value(e, "name"), Collapsed = BoolValue(e, "collapsed", false), ModIds = ListValues(e, "mods")
    }).Where(f => f.Id.Length > 0).ToList() ?? [];
    private static bool BoolValue(XElement parent, string name, bool fallback) => bool.TryParse(Value(parent, name), out bool v) ? v : fallback;
    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private void AssignUnclassifiedMods()
    {
        var assigned = new HashSet<string>(Categories.SelectMany(c => c.ModIds), StringComparer.OrdinalIgnoreCase);
        int firstOfficial = OrderedMods.FindIndex(m => m.Official);
        foreach (ModEntry mod in OrderedMods)
        {
            if (!assigned.Add(mod.PackageId)) continue;
            string id = mod.Official ? "core" : "normal";
            if (!mod.Official && firstOfficial > 0 && OrderedMods.IndexOf(mod) < firstOfficial) id = "before";
            CategoryData category = Categories.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? Categories.Last();
            category.ModIds.Add(mod.PackageId);
        }

        foreach (CategoryData category in Categories)
        {
            FolderData unfiled = EnsureFolder(category.Folders);
            var folderIds = new HashSet<string>(category.Folders.SelectMany(folder => folder.ModIds), StringComparer.OrdinalIgnoreCase);
            foreach (string id in category.ModIds)
                if (folderIds.Add(id))
                    unfiled.ModIds.Add(id);
        }

        foreach (TagData tag in Tags)
        {
            FolderData unfiled = EnsureFolder(tag.Folders);
            var folderIds = new HashSet<string>(tag.Folders.SelectMany(folder => folder.ModIds), StringComparer.OrdinalIgnoreCase);
            foreach (ModState state in States)
            {
                bool belongs = tag.IsSystem
                    ? state.TagIds.Count == 0
                    : state.TagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase);
                if (belongs && folderIds.Add(state.PackageId))
                    unfiled.ModIds.Add(state.PackageId);
            }
        }
    }

    private static FolderData EnsureFolder(List<FolderData> folders)
    {
        FolderData? folder = folders.FirstOrDefault(f => f.Id == "uncategorized");
        if (folder == null) { folder = NewFolder(); folders.Insert(0, folder); }
        return folder;
    }

    private void ApplyCategoryAndTagState()
    {
        foreach (ModEntry mod in OrderedMods)
        {
            CategoryData? category = Categories.FirstOrDefault(item =>
                item.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase));
            mod.CategoryId = category?.Id ?? "normal";

            ModState? state = FindState(mod.PackageId);
            if (state == null)
                continue;

            // The in-game manager stores per-mod Enabled separately from category.Enabled.
            // The category checkbox is a master switch; never overwrite the saved per-mod choice
            // with the current ModsConfig.xml state during startup.
            bool categoryEnabled = category?.Enabled ?? true;
            mod.Active = mod.Official || (categoryEnabled && state.Enabled);
        }
    }

    public void RefreshIssues() => RebuildIssues();

    private void RebuildIssues()
    {
        var installedIds = new HashSet<string>(Mods.Select(mod => mod.PackageId), StringComparer.OrdinalIgnoreCase);
        List<ModEntry> active = OrderedMods.Where(mod => mod.Active || mod.Official).ToList();
        var activeIds = new HashSet<string>(active.Select(mod => mod.PackageId), StringComparer.OrdinalIgnoreCase);
        var activePositions = active.Select((mod, index) => (mod.PackageId, index))
            .ToDictionary(item => item.PackageId, item => item.index, StringComparer.OrdinalIgnoreCase);

        foreach (ModEntry mod in OrderedMods)
        {
            var problems = new List<string>();
            mod.HasMissingFolder = string.IsNullOrWhiteSpace(mod.Path);
            mod.HasMissingRequiredMod = false;
            mod.HasLoadOrderIssue = false;
            mod.HasVersionMismatch = false;
            if (mod.HasMissingFolder)
                problems.Add("설치된 모드 폴더 없음");

            if (GameVersion != null && mod.SupportedVersions.Count > 0)
            {
                bool supportsCurrentOrNewer = mod.SupportedVersions
                    .Select(value => Regex.Match(value ?? "", @"(?<!d)(d+).(d+)(?:.(d+))?"))
                    .Where(match => match.Success)
                    .Select(match => new Version(
                        int.Parse(match.Groups[1].Value),
                        int.Parse(match.Groups[2].Value),
                        match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0))
                    .Any(version => version.Major > GameVersion.Major ||
                        (version.Major == GameVersion.Major && version.Minor >= GameVersion.Minor));
                if (!supportsCurrentOrNewer)
                {
                    mod.HasVersionMismatch = true;
                    problems.Add($"게임 버전 불일치: 현재 {GameVersion.Major}.{GameVersion.Minor}");
                }
            }

            if (mod.Active || mod.Official)
            {
                foreach (string dependency in mod.Required)
                {
                    if (activeIds.Contains(dependency)) continue;
                    string state = installedIds.Contains(dependency) ? "비활성" : "미설치";
                    mod.HasMissingRequiredMod = true;
                    problems.Add("필수 모드 확인: " + dependency + " (" + state + ")");
                }

                int position = activePositions.TryGetValue(mod.PackageId, out int found) ? found : -1;
                if (position >= 0)
                {
                    foreach (string dependency in mod.LoadAfter)
                        if (activePositions.TryGetValue(dependency, out int dependencyPosition) &&
                            position < dependencyPosition)
                        {
                            mod.HasLoadOrderIssue = true;
                            problems.Add("불러오기 순서: " + dependency + "보다 뒤에 있어야 함");
                        }

                    foreach (string dependency in mod.LoadBefore)
                        if (activePositions.TryGetValue(dependency, out int dependencyPosition) &&
                            position > dependencyPosition)
                        {
                            mod.HasLoadOrderIssue = true;
                            problems.Add("불러오기 순서: " + dependency + "보다 앞에 있어야 함");
                        }
                }
            }

            mod.Issue = string.Join(" / ", problems.Distinct(StringComparer.CurrentCultureIgnoreCase));
        }
    }

    public ModState GetState(string packageId)
    {
        ModState? state = FindState(packageId);
        if (state != null) return state;
        state = new ModState { PackageId = packageId };
        States.Add(state);
        return state;
    }
    private ModState? FindState(string id) => States.FirstOrDefault(s => s.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void AssignCategory(IEnumerable<string> packageIds, string categoryId)
    {
        CategoryData? target = Categories.FirstOrDefault(c => c.Id == categoryId);
        if (target == null) return;
        foreach (string id in packageIds)
        {
            foreach (CategoryData c in Categories)
            {
                c.ModIds.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
                foreach (FolderData f in c.Folders) f.ModIds.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
            }
            target.ModIds.Add(id);
            EnsureFolder(target.Folders).ModIds.Add(id);
            ModEntry? mod = OrderedMods.FirstOrDefault(m => m.PackageId == id);
            if (mod != null) mod.CategoryId = target.Id;
        }
    }

    public void AssignTag(IEnumerable<string> packageIds, TagData tag, bool add)
    {
        foreach (string id in packageIds)
        {
            ModState state = GetState(id);
            if (add && !state.TagIds.Contains(tag.Id)) state.TagIds.Add(tag.Id);
            if (!add) state.TagIds.RemoveAll(t => t.Equals(tag.Id, StringComparison.OrdinalIgnoreCase));
            if (add && !tag.Folders.Any(folder => folder.ModIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
                EnsureFolder(tag.Folders).ModIds.Add(id);
            else if (!add)
                foreach (var folder in tag.Folders) folder.ModIds.RemoveAll(m => m.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void RemoveTag(TagData tag)
    {
        if (tag.IsSystem) return;
        Tags.Remove(tag);
        foreach (ModState state in States)
            state.TagIds.RemoveAll(id => id.Equals(tag.Id, StringComparison.OrdinalIgnoreCase));
    }

    public bool RemoveFromManager(string packageId)
    {
        ModEntry? mod = OrderedMods.FirstOrDefault(item => item.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        if (mod == null || mod.Active || mod.Official) return false;

        if (!ExcludedPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
            ExcludedPackageIds.Add(packageId);

        Mods.RemoveAll(item => item.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        OrderedMods.RemoveAll(item => item.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        foreach (CategoryData category in Categories)
        {
            category.ModIds.RemoveAll(id => id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
            foreach (FolderData folder in category.Folders)
                folder.ModIds.RemoveAll(id => id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        }
        foreach (TagData tag in Tags)
            foreach (FolderData folder in tag.Folders)
                folder.ModIds.RemoveAll(id => id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        States.RemoveAll(state => state.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    public void AssignToCategoryFolder(IEnumerable<string> packageIds, CategoryData category, FolderData target)
    {
        foreach (string id in packageIds)
        {
            foreach (CategoryData existingCategory in Categories)
            {
                existingCategory.ModIds.RemoveAll(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
                foreach (FolderData folder in existingCategory.Folders)
                    folder.ModIds.RemoveAll(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
            }
            category.ModIds.Add(id);
            target.ModIds.Add(id);
            ModEntry? mod = OrderedMods.FirstOrDefault(m => m.PackageId == id);
            if (mod != null) mod.CategoryId = category.Id;
        }
    }

    public void AssignToTagFolder(IEnumerable<string> packageIds, TagData tag, FolderData target)
    {
        foreach (string id in packageIds)
        {
            foreach (FolderData folder in tag.Folders)
                folder.ModIds.RemoveAll(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
            target.ModIds.Add(id);
            ModState state = GetState(id);
            if (!state.TagIds.Contains(tag.Id)) state.TagIds.Add(tag.Id);
        }
    }

    public void SaveAll()
    {
        foreach (ModEntry mod in OrderedMods)
            GetState(mod.PackageId).Enabled = mod.Active || mod.Official;
        foreach (CategoryData category in Categories)
        {
            if (OrderedMods.Any(mod => (mod.Active || mod.Official) && category.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase)))
                category.Enabled = true;
        }
        SaveModsConfig();
        SaveManagerSettings(persistActivationDraft: true);
    }

    private void SaveModsConfig()
    {
        XDocument doc = XDocument.Load(Paths.ModsConfig);
        XElement? active = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "activeMods");
        if (active == null) throw new InvalidDataException("ModsConfig.xml에 activeMods 목록을 찾지 못했습니다.");
        active.RemoveNodes();
        foreach (ModEntry mod in OrderedMods.Where(m => m.Active || m.Official)) active.Add(new XElement("li", mod.PackageId));
        BackupFile(Paths.ModsConfig);
        SaveAtomic(doc, Paths.ModsConfig);
    }

    public void SaveManagerDataImmediately()
    {
        SaveManagerSettings(persistActivationDraft: false);
    }

    private void SaveManagerSettings(bool persistActivationDraft)
    {
        Dictionary<string, bool>? draftEnabledStates = null;
        if (!persistActivationDraft)
        {
            draftEnabledStates = States.ToDictionary(
                state => state.PackageId, state => state.Enabled, StringComparer.OrdinalIgnoreCase);
            foreach (ModState state in States)
            {
                if (persistedEnabledStates.TryGetValue(state.PackageId, out bool savedEnabled))
                {
                    state.Enabled = savedEnabled;
                    continue;
                }

                ModEntry? installed = OrderedMods.FirstOrDefault(mod =>
                    mod.PackageId.Equals(state.PackageId, StringComparison.OrdinalIgnoreCase));
                if (installed != null)
                    state.Enabled = installed.Active || installed.Official;
            }
        }

        try
        {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Paths.ManagerSettings)!);
        XDocument doc = File.Exists(Paths.ManagerSettings) ? XDocument.Load(Paths.ManagerSettings) : new XDocument(new XElement("ModSettings"));
        XElement root = doc.Root ?? throw new InvalidDataException("ModSettings XML root is missing.");
        XElement? managerSettings = GamePaths.FindManagerSettingsNode(root);
        if (managerSettings == null)
        {
            managerSettings = new XElement("settings",
                new XAttribute("Class", "Lilly.IntegratedModManager.IntegratedModManagerSettings"));
            root.Add(managerSettings);
        }
        ReplaceCollection(managerSettings, "categories", Categories.Select(WriteCategory));
        ReplaceCollection(managerSettings, "tags", Tags.Select(WriteTag));
        ReplaceCollection(managerSettings, "mods", States.Select(WriteState));
        ReplaceCollection(managerSettings, "excludedPackageIds", ExcludedPackageIds.Select(id => new XElement("li", id)));
        ReplaceCollection(managerSettings, "favoriteFolderKeys", FavoriteFolderKeys.Select(key => new XElement("li", key)));
        ReplaceCollection(managerSettings, "recentFolderKeys", RecentFolderKeys.Select(key => new XElement("li", key)));
        SetValue(managerSettings, "autoTagNewMods", AutoTagNewMods);
        SetValue(managerSettings, "autoTagUpdatedMods", AutoTagUpdatedMods);
        BackupFile(Paths.ManagerSettings);
        SaveAtomic(doc, Paths.ManagerSettings);
        persistedEnabledStates = States.ToDictionary(
            state => state.PackageId, state => state.Enabled, StringComparer.OrdinalIgnoreCase);
        MetadataChanged = false;
        }
        finally
        {
            if (!persistActivationDraft && draftEnabledStates != null)
            {
                foreach (ModState state in States)
                    if (draftEnabledStates.TryGetValue(state.PackageId, out bool draftEnabled))
                        state.Enabled = draftEnabled;
            }
        }
    }

    private static void ReplaceCollection(XElement parent, string name, IEnumerable<XElement> items)
    {
        XElement collection = new(name, items);
        XElement? existing = parent.Elements().FirstOrDefault(e => e.Name.LocalName == name);
        if (existing == null) parent.Add(collection); else existing.ReplaceWith(collection);
    }

    private static void SetValue(XElement parent, string name, bool value)
    {
        XElement? existing = Child(parent, name);
        string text = value.ToString().ToLowerInvariant();
        if (existing == null) parent.Add(new XElement(name, text));
        else existing.Value = text;
    }
    private static XElement WriteCategory(CategoryData c) => new("li", new XElement("id", c.Id), new XElement("name", c.Name), new XElement("enabled", c.Enabled.ToString().ToLowerInvariant()), HexElement("colorHex", c.ColorHex), StringList("mods", c.ModIds), new XElement("folders", c.Folders.Select(WriteFolder)));
    private static XElement WriteTag(TagData t) => new("li", new XElement("id", t.Id), new XElement("name", t.Name), new XElement("isSystem", t.IsSystem.ToString().ToLowerInvariant()), HexElement("colorHex", t.ColorHex), new XElement("folders", t.Folders.Select(WriteFolder)));
    private static XElement WriteState(ModState s) => new("li", new XElement("packageId", s.PackageId), new XElement("enabled", s.Enabled.ToString().ToLowerInvariant()), HexElement("displayName", s.DisplayName), HexElement("colorHex", s.ColorHex), HexElement("installedVersion", s.InstalledVersion), StringList("tags", s.TagIds));
    private static XElement WriteFolder(FolderData f) => new("li", new XElement("id", f.Id), new XElement("name", f.Name), new XElement("collapsed", f.Collapsed.ToString().ToLowerInvariant()), StringList("mods", f.ModIds));
    private static XElement HexElement(string name, string? value) => new(name, value ?? "");
    private static XElement StringList(string name, IEnumerable<string> values) => new(name, values.Select(v => new XElement("li", v)));

    private static void BackupFile(string path)
    {
        if (!File.Exists(path)) return;
        string backup = path + ".backup." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        File.Copy(path, backup, false);
    }
    private static void SaveAtomic(XDocument doc, string path)
    {
        string temp = path + ".tmp";
        doc.Save(temp);
        File.Move(temp, path, true);
    }

    public List<PresetData> LoadPresets()
    {
        Directory.CreateDirectory(Paths.PresetDirectory);
        var result = new List<PresetData>();
        foreach (string file in Directory.GetFiles(Paths.PresetDirectory, "*.xml"))
        {
            try
            {
                XDocument doc = XDocument.Load(file);
                XElement? p = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "preset");
                if (p == null) continue;
                var preset = new PresetData { Id = Value(p, "id"), Name = Value(p, "name"), Categories = ParseCategories(p.Element("categories")), Tags = ParseTags(p.Element("tags")), Mods = ParseStates(p.Element("mods")) };
                if (preset.Id.Length > 0 && preset.Name.Length > 0) result.Add(preset);
            }
            catch { }
        }
        return result.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void SavePreset(string name)
    {
        string safe = new string(name.Trim().Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim().Trim('.');
        if (safe.Length == 0) throw new InvalidOperationException("프리셋 이름이 올바르지 않습니다.");
        Directory.CreateDirectory(Paths.PresetDirectory);
        var preset = new PresetData { Id = Guid.NewGuid().ToString("N"), Name = safe, Categories = CloneCategories(), Tags = CloneTags(), Mods = CloneStates() };
        XElement p = new("preset", new XElement("id", preset.Id), new XElement("name", preset.Name), new XElement("categories", preset.Categories.Select(WriteCategory)), new XElement("tags", preset.Tags.Select(WriteTag)), new XElement("mods", preset.Mods.Select(WriteState)));
        string path = System.IO.Path.Combine(Paths.PresetDirectory, safe + ".xml");
        if (File.Exists(path)) BackupFile(path);
        SaveAtomic(new XDocument(new XElement("LillyModManagerPreset", p)), path);
    }

    public void LoadPreset(PresetData preset)
    {
        var previousStates = States.ToDictionary(state => state.PackageId, Clone, StringComparer.OrdinalIgnoreCase);
        Categories = preset.Categories.Select(Clone).ToList();
        Tags = preset.Tags.Select(Clone).ToList();
        States = preset.Mods
            .Where(state => state != null && !string.IsNullOrWhiteSpace(state.PackageId))
            .GroupBy(state => state.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => Clone(group.First()))
            .ToList();

        if (!Tags.Any(tag => tag.Id.Equals("untagged", StringComparison.OrdinalIgnoreCase)))
            Tags.Insert(0, new TagData { Id = "untagged", Name = "태그 없음", IsSystem = true, Folders = [NewFolder()] });

        var stateIds = new HashSet<string>(States.Select(state => state.PackageId), StringComparer.OrdinalIgnoreCase);
        foreach (ModEntry mod in Mods)
        {
            if (!stateIds.Add(mod.PackageId))
                continue;

            ModState preserved;
            if (previousStates.TryGetValue(mod.PackageId, out ModState? oldState))
                preserved = Clone(oldState);
            else
                preserved = new ModState { PackageId = mod.PackageId, Enabled = mod.Active, InstalledVersion = mod.Version };
            States.Add(preserved);
        }

        AssignUnclassifiedMods();

        // A manager preset stores category and folder membership in display order.
        // Rebuild the order from those folders, then append any remaining installed mods.
        var reordered = new List<ModEntry>();
        var reorderedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CategoryData category in Categories)
            foreach (FolderData folder in category.Folders)
                foreach (string id in folder.ModIds)
                {
                    ModEntry? item = OrderedMods.FirstOrDefault(mod => mod.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (item != null && reorderedIds.Add(id))
                        reordered.Add(item);
                }
        foreach (ModEntry mod in OrderedMods)
            if (reorderedIds.Add(mod.PackageId))
                reordered.Add(mod);
        OrderedMods = reordered;

        var enabled = new HashSet<string>(States.Where(state => state.Enabled).Select(state => state.PackageId), StringComparer.OrdinalIgnoreCase);
        var disabledCategories = new HashSet<string>(Categories.Where(category => !category.Enabled).Select(category => category.Id));
        foreach (ModEntry mod in Mods)
        {
            string categoryId = Categories.FirstOrDefault(category => category.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase))?.Id ?? "normal";
            mod.Active = mod.Official || (enabled.Contains(mod.PackageId) && !disabledCategories.Contains(categoryId));
        }
        ApplyCategoryAndTagState();
        RebuildIssues();
    }

    public void DeletePreset(PresetData preset)
    {
        string path = System.IO.Path.Combine(Paths.PresetDirectory, preset.Name + ".xml");
        if (File.Exists(path)) File.Delete(path);
    }

    private List<CategoryData> CloneCategories() => Categories.Select(Clone).ToList();
    private List<TagData> CloneTags() => Tags.Select(Clone).ToList();
    private List<ModState> CloneStates() => States.Select(Clone).ToList();
    private static CategoryData Clone(CategoryData c) => new() { Id = c.Id, Name = c.Name, Enabled = c.Enabled, ColorHex = c.ColorHex, ModIds = [.. c.ModIds], Folders = c.Folders.Select(Clone).ToList() };
    private static TagData Clone(TagData t) => new() { Id = t.Id, Name = t.Name, IsSystem = t.IsSystem, ColorHex = t.ColorHex, Folders = t.Folders.Select(Clone).ToList() };
    private static ModState Clone(ModState s) => new() { PackageId = s.PackageId, Enabled = s.Enabled, DisplayName = s.DisplayName, ColorHex = s.ColorHex, InstalledVersion = s.InstalledVersion, TagIds = [.. s.TagIds] };
    private static FolderData Clone(FolderData f) => new() { Id = f.Id, Name = f.Name, Collapsed = f.Collapsed, ModIds = [.. f.ModIds] };

    public void AutoSort()
    {
        var candidates = OrderedMods.Where(m => m.Active || m.Official).ToList();
        var rank = candidates.Select((m, i) => (m.PackageId, i)).ToDictionary(x => x.PackageId, x => x.i, StringComparer.OrdinalIgnoreCase);
        var byId = candidates.ToDictionary(m => m.PackageId, StringComparer.OrdinalIgnoreCase);
        var edges = candidates.ToDictionary(m => m.PackageId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var indegree = candidates.ToDictionary(m => m.PackageId, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (ModEntry m in candidates)
        {
            foreach (string dep in m.LoadAfter) AddEdge(dep, m.PackageId);
            foreach (string dep in m.LoadBefore) AddEdge(m.PackageId, dep);
        }
        void AddEdge(string from, string to)
        {
            if (from.Equals(to, StringComparison.OrdinalIgnoreCase) || !byId.ContainsKey(from) || !byId.ContainsKey(to)) return;
            if (edges[from].Add(to)) indegree[to]++;
        }
        var ready = new SortedSet<(int Rank, string Id)>(Comparer<(int Rank, string Id)>.Create((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id)));
        foreach (var m in candidates) if (indegree[m.PackageId] == 0) ready.Add((rank[m.PackageId], m.PackageId));
        var sorted = new List<ModEntry>();
        while (ready.Count > 0)
        {
            var first = ready.Min; ready.Remove(first);
            sorted.Add(byId[first.Id]);
            foreach (string next in edges[first.Id]) if (--indegree[next] == 0) ready.Add((rank[next], next));
        }
        if (sorted.Count < candidates.Count)
        {
            var done = new HashSet<string>(sorted.Select(m => m.PackageId), StringComparer.OrdinalIgnoreCase);
            sorted.AddRange(candidates.Where(m => !done.Contains(m.PackageId)));
        }
        OrderedMods = sorted.Concat(OrderedMods.Where(m => !m.Active && !m.Official)).ToList();
    }
}

internal sealed partial class MainForm : Form
{
    private ModStore? store;
    private readonly TextBox search = new() { Width = 190, PlaceholderText = "모드 이름 / 제작자 / PackageId 검색" };
    private readonly ComboBox categoryFilter = new() { Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox tagFilter = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox activeFilter = new() { Width = 95, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox sourceFilter = new() { Width = 115, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox issueFilter = new() { Width = 135, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly RichTextBox details = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(28, 31, 37), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };

    public MainForm()
    {
        Text = "Lilly - Mod Manager";
        Width = 1450; Height = 900; MinimumSize = new Size(1100, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(23, 26, 31); ForeColor = Color.Gainsboro;
        Icon = LoadIcon();
        BuildUi();
        Shown += (_, _) => TryInitialLoad();
    }

    private Icon? LoadIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { return null; }
    }

    private void BuildUi() => BuildGameLikeUi();

    private static void AddToolbarLabel(FlowLayoutPanel panel, string text) => panel.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(5, 9, 1, 0), ForeColor = Color.Silver });
    private static void AddButton(FlowLayoutPanel panel, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 29, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(54, 63, 74), ForeColor = Color.White, Margin = new Padding(3, 3, 3, 3) };
        button.FlatAppearance.BorderColor = Color.FromArgb(83, 94, 109); button.Click += (_, _) => action(); panel.Controls.Add(button);
    }

    private void TryInitialLoad()
    {
        GamePaths paths = ModStore.DiscoverPaths(); store = new ModStore(paths);
        if (Directory.Exists(paths.GameRoot) && File.Exists(paths.ModsConfig) && Directory.Exists(paths.ConfigRoot)) Reload();
        else ChoosePaths();
    }

    private void ChoosePaths()
    {
        string game = store?.Paths.GameRoot ?? "";
        using (var dialog = new FolderBrowserDialog { Description = "RimWorld 설치 폴더를 선택하세요.", SelectedPath = Directory.Exists(game) ? game : "" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string config = store?.Paths.ConfigRoot ?? ModStore.DiscoverPaths().ConfigRoot;
            using var configDialog = new FolderBrowserDialog { Description = "RimWorld Config 폴더를 선택하세요.", SelectedPath = Directory.Exists(config) ? config : "" };
            if (configDialog.ShowDialog(this) != DialogResult.OK) return;
            store = new ModStore(new GamePaths { GameRoot = dialog.SelectedPath, ConfigRoot = configDialog.SelectedPath });
            Reload();
        }
    }

    private void Reload()
    {
        try
        {
            store!.Load();
            SyncAutoTagOptionControls();
            bool metadataChanged = store.MetadataChanged;
            if (metadataChanged)
                PersistManagerSettings("신규 모드/버전 변경 정보 갱신");
            RefreshFilters();
            RefreshGrid();
            string statusText = $"모드 {store.Mods.Count}개 검색 · 활성 {store.Mods.Count(m => m.Active)}개";
            if (metadataChanged && GameIsRunning())
                statusText += " · 자동 태그/버전 정보는 게임 종료 후 저장하세요.";
            SetStatus(statusText);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "모드 목록을 불러오지 못했습니다.",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus(ex.Message);
        }
    }

    private void RefreshFilters()
    {
        if (store == null) return;
        string oldCat = categoryFilter.SelectedItem?.ToString() ?? "전체";
        string oldTag = tagFilter.SelectedItem?.ToString() ?? "모든 태그";
        categoryFilter.Items.Clear();
        categoryFilter.Items.Add("전체");
        foreach (var category in store.Categories) categoryFilter.Items.Add(category.Name);
        categoryFilter.SelectedItem = categoryFilter.Items.Contains(oldCat) ? oldCat : "전체";
        tagFilter.Items.Clear();
        tagFilter.Items.Add("모든 태그");
        foreach (var tag in store.Tags.Where(tag => !tag.IsSystem)) tagFilter.Items.Add(tag.Name);
        tagFilter.SelectedItem = tagFilter.Items.Contains(oldTag) ? oldTag : "모든 태그";

        if (sidebarSelectedCategoryId != null &&
            !store.Categories.Any(category => category.Id == sidebarSelectedCategoryId))
            sidebarSelectedCategoryId = null;
        if (sidebarSelectedTagId != null &&
            !store.Tags.Any(tag => tag.Id == sidebarSelectedTagId))
            sidebarSelectedTagId = null;
        RefreshSidebar();
    }

    private void RefreshGrid()
    {
        RefreshSidebar();
        RefreshGameLikeList();
    }

    private void UpdateDetails()
    {
        ModEntry? mod = GetPrimarySelectedMod();
        if (mod == null)
        {
            details.Clear();
            UpdateDetailsPane();
            return;
        }
        ModState state = store?.GetState(mod.PackageId) ?? new ModState();
        string displayName = string.IsNullOrWhiteSpace(state.DisplayName) ? mod.Name : state.DisplayName;
        CategoryData? category = store?.Categories.FirstOrDefault(item =>
            item.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase));
        string folderName = category?.Folders.FirstOrDefault(folder =>
            folder.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase))?.Name ?? "미분류";
        string tagNames = store == null ? "" : string.Join(", ", state.TagIds
            .Select(id => store.Tags.FirstOrDefault(tag => tag.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name)));
        details.Text =
            $"{displayName}\n{mod.PackageId}\n제작자: {mod.Author}\n버전: {mod.Version}\n" +
            $"상태: {(mod.Active || mod.Official ? "활성" : "비활성")}\n" +
            $"카테고리: {category?.Name ?? "미분류"} / 폴더: {folderName}\n" +
            $"태그: {(tagNames.Length == 0 ? "없음" : tagNames)}\n" +
            $"설치 위치: {(mod.Official ? "공식" : mod.Workshop ? "Steam 창작마당" : "로컬")}\n" +
            $"지원 버전: {(mod.SupportedVersions.Count == 0 ? "정보 없음" : string.Join(", ", mod.SupportedVersions))}\n" +
            $"경로: {mod.Path}\n" +
            (string.IsNullOrWhiteSpace(mod.Url) ? "" : $"프로젝트 페이지: {mod.Url}\n") +
            $"\n{mod.Description}\n\n" +
            $"필수 모드: {(mod.Required.Count == 0 ? "없음" : string.Join(", ", mod.Required))}\n" +
            $"LoadAfter: {(mod.LoadAfter.Count == 0 ? "없음" : string.Join(", ", mod.LoadAfter))}\n" +
            $"LoadBefore: {(mod.LoadBefore.Count == 0 ? "없음" : string.Join(", ", mod.LoadBefore))}\n\n" +
            $"검사 결과: {(string.IsNullOrWhiteSpace(mod.Issue) ? "문제 없음" : mod.Issue)}";
        UpdateDetailsPane();
    }

    private List<string> SelectedIds() => visibleMods.Where(mod => selectedPackageIds.Contains(mod.PackageId)).Select(mod => mod.PackageId).ToList();

    private void ShowModContextMenu(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || store == null) return;
        var menu = new ContextMenuStrip();
        var categoryMenu = new ToolStripMenuItem("카테고리로 이동");
        foreach (CategoryData category in store.Categories)
        {
            CategoryData captured = category;
            categoryMenu.DropDownItems.Add(category.Name, null, (_, _) =>
            {
                store.AssignCategory(SelectedIds(), captured.Id);
                PersistManagerSettings("카테고리 이동");
                RefreshGrid();
            });
        }
        menu.Items.Add("선택 모드 활성화", null, (_, _) => SetSelectedActive(true));
        menu.Items.Add("선택 모드 비활성화", null, (_, _) => SetSelectedActive(false));
        menu.Items.Add(categoryMenu);
        var tagMenu = new ToolStripMenuItem("태그 지정 / 해제");
        foreach (TagData tag in store.Tags.Where(t => !t.IsSystem))
        {
            TagData captured = tag;
            var item = new ToolStripMenuItem(tag.Name) { CheckOnClick = true, Checked = SelectedIds().Any(id => store.GetState(id).TagIds.Contains(captured.Id)) };
            item.Click += (_, _) =>
            {
                store.AssignTag(SelectedIds(), captured, item.Checked);
                PersistManagerSettings("태그 지정");
                RefreshGrid();
            };
            tagMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(tagMenu);

        var folderMenu = new ToolStripMenuItem("카테고리 폴더로 이동");
        ModEntry? currentMod = contextMenuAnchor?.Tag as ModEntry ??
            GetPrimarySelectedMod();
        CategoryData? currentCategory = currentMod == null ? null : store.Categories.FirstOrDefault(c => c.Id == currentMod.CategoryId);
        if (currentCategory != null)
        {
            foreach (FolderData folder in currentCategory.Folders)
            {
                FolderData targetFolder = folder;
                CategoryData targetCategory = currentCategory;
                folderMenu.DropDownItems.Add(folder.Name, null, (_, _) =>
                {
                    store.AssignToCategoryFolder(SelectedIds(), targetCategory, targetFolder);
                    PersistManagerSettings("카테고리 폴더 이동");
                    RefreshGrid();
                });
            }
        }
        menu.Items.Add(folderMenu);

        var tagFolderMenu = new ToolStripMenuItem("선택 태그 폴더로 이동");
        TagData? currentTag = SelectedTag();
        if (currentTag != null)
        {
            foreach (FolderData folder in currentTag.Folders)
            {
                FolderData targetFolder = folder;
                TagData targetTag = currentTag;
                tagFolderMenu.DropDownItems.Add(folder.Name, null, (_, _) =>
                {
                    store.AssignToTagFolder(SelectedIds(), targetTag, targetFolder);
                    PersistManagerSettings("태그 폴더 이동");
                    RefreshGrid();
                });
            }
        }
        menu.Items.Add(tagFolderMenu);
        menu.Items.Add("선택 항목으로 폴더 만들기", null, (_, _) => CreateFolderForSelected());
        menu.Items.Add("선택 항목으로 태그 폴더 만들기", null, (_, _) => CreateTagFolderForSelected());
        menu.Items.Add("현재 카테고리 폴더 이름 변경", null, (_, _) => RenameSelectedFolder());
        menu.Items.Add("현재 카테고리 폴더 삭제", null, (_, _) => DeleteSelectedFolder());
        menu.Items.Add("현재 태그 폴더 이름 변경", null, (_, _) => RenameSelectedTagFolder());
        menu.Items.Add("현재 태그 폴더 삭제", null, (_, _) => DeleteSelectedTagFolder());
        menu.Items.Add("관리 목록에서 제거", null, (_, _) => RemoveSelectedFromManager());
        menu.Items.Add("로컬 모드 폴더 삭제", null, (_, _) => DeleteSelectedLocalModFolder());
        menu.Items.Add("표시 이름 변경", null, (_, _) => RenameSelectedMod());
        menu.Items.Add("표시 이름 초기화", null, (_, _) => ResetSelectedModName());
        menu.Items.Add("모드 폴더 열기", null, (_, _) => OpenSelectedFolder());
        menu.Items.Add("창작마당 페이지 열기", null, (_, _) => OpenWorkshopPage());
        menu.Show(contextMenuAnchor ?? (Control)modRows, contextMenuAnchor == null ? e.Location : contextMenuPoint);
    }

    private static Color? ChooseColor(string? current)
    {
        using var dialog = new ColorDialog { FullOpen = true, AnyColor = true };
        if (!string.IsNullOrWhiteSpace(current))
        {
            try { dialog.Color = ColorTranslator.FromHtml(current); } catch { }
        }
        return dialog.ShowDialog() == DialogResult.OK ? dialog.Color : null;
    }

    private void PickCategoryColor()
    {
        CategoryData? category = SelectedCategory();
        if (category == null) { MessageBox.Show(this, "카테고리 필터에서 카테고리를 선택하세요."); return; }
        Color? color = ChooseColor(category.ColorHex);
        if (color.HasValue)
        {
            category.ColorHex = ColorTranslator.ToHtml(color.Value);
            PersistManagerSettings("카테고리 색상 변경");
            RefreshSidebar();
            RefreshGrid();
        }
    }

    private void PickTagColor()
    {
        TagData? tag = SelectedTag();
        if (tag == null || tag.IsSystem) { MessageBox.Show(this, "태그 필터에서 사용자 태그를 선택하세요."); return; }
        Color? color = ChooseColor(tag.ColorHex);
        if (color.HasValue)
        {
            tag.ColorHex = ColorTranslator.ToHtml(color.Value);
            PersistManagerSettings("태그 색상 변경");
            RefreshSidebar();
            RefreshGrid();
        }
    }

    private void PickModColor()
    {
        if (store == null || GetPrimarySelectedMod() is not ModEntry mod) return;
        ModState state = store.GetState(mod.PackageId);
        Color? color = ChooseColor(state.ColorHex);
        if (color.HasValue)
        {
            state.ColorHex = ColorTranslator.ToHtml(color.Value);
            PersistManagerSettings("모드 색상 변경");
            RefreshGrid();
        }
    }

    private void ClearModColor()
    {
        if (store == null || GetPrimarySelectedMod() is not ModEntry mod) return;
        store.GetState(mod.PackageId).ColorHex = null;
        PersistManagerSettings("모드 색상 초기화");
        RefreshGrid();
    }

    private void DeleteSelectedLocalModFolder()
    {
        if (store == null || GetPrimarySelectedMod() is not ModEntry mod) return;
        if (GameIsRunning())
        {
            MessageBox.Show(this, "림월드가 실행 중일 때는 모드 폴더를 삭제할 수 없습니다.");
            return;
        }
        if (mod.Official || mod.Workshop)
        {
            MessageBox.Show(this, "이 작업은 로컬 모드 폴더에만 사용할 수 있습니다. 창작마당 파일은 Steam에서 관리하세요.");
            return;
        }
        if (mod.Active)
        {
            MessageBox.Show(this, "활성화된 모드는 먼저 비활성화하고 적용한 뒤 삭제하세요.");
            return;
        }
        if (!Directory.Exists(mod.Path))
        {
            MessageBox.Show(this, "모드 폴더가 이미 존재하지 않습니다.");
            return;
        }
        if (MessageBox.Show(this,
            $"'{mod.Name}'의 로컬 모드 폴더와 모든 파일을 삭제할까요?\n이 작업은 되돌릴 수 없습니다.",
            "로컬 모드 폴더 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            Directory.Delete(mod.Path, true);
            store.RemoveFromManager(mod.PackageId);
            // Removing files is irreversible, so persist the matching exclusion immediately.
            store.SaveManagerDataImmediately();
            RefreshFilters();
            RefreshGrid();
            SetStatus("로컬 모드 폴더를 삭제했습니다.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "모드 폴더 삭제 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RemoveSelectedFromManager()
    {
        if (store == null || GetPrimarySelectedMod() is not ModEntry mod) return;
        if (mod.Active || mod.Official)
        {
            MessageBox.Show(this, "활성 모드와 Core/DLC는 관리 목록에서 제외할 수 없습니다.");
            return;
        }
        if (MessageBox.Show(this,
            $"'{mod.Name}' 모드를 관리 목록에서 제외할까요?\n설치된 모드 파일과 Steam 구독 상태는 변경되지 않습니다.",
            "관리 목록에서 제거", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (store.RemoveFromManager(mod.PackageId))
        {
            PersistManagerSettings("관리 목록에서 모드 제거");
            RefreshFilters();
            RefreshGrid();
            SetStatus("모드를 관리 목록에서 제외했습니다. 설치 파일과 구독 상태는 유지됩니다.");
        }
    }

    private void SetSelectedActive(bool active)
    {
        if (store == null) return;
        foreach (string id in SelectedIds())
        {
            ModEntry? mod = store.OrderedMods.FirstOrDefault(m => m.PackageId == id);
            if (mod == null || mod.Official) continue;
            mod.Active = active;
            store.GetState(id).Enabled = active;
        }
        RefreshGrid();
        SetStatus(active ? "선택한 모드를 활성화할 예정입니다." : "선택한 모드를 비활성화할 예정입니다.");
    }

    private void RenameSelectedMod()
    {
        if (store == null || GetPrimarySelectedMod() is not ModEntry mod) return;
        ModState state = store.GetState(mod.PackageId);
        string? name = Prompt.Show(this, "목록에 표시할 모드 이름", "표시 이름 변경");
        if (name == null) return;
        state.DisplayName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        PersistManagerSettings("모드 표시 이름 변경");
        RefreshGrid();
    }

    private void ResetSelectedModName()
    {
        if (store == null || GetPrimarySelectedMod() is not ModEntry mod) return;
        store.GetState(mod.PackageId).DisplayName = null;
        PersistManagerSettings("모드 표시 이름 초기화");
        RefreshGrid();
    }

    private void Apply()
    {
        if (store == null) return;
        if (GameIsRunning())
        {
            MessageBox.Show(this, "림월드가 실행 중일 때는 설정 파일을 덮어쓸 수 없습니다. 게임을 종료한 뒤 적용하세요.",
                "적용할 수 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        List<string> warnings = GetApplyWarnings();
        if (warnings.Count > 0)
        {
            string preview = string.Join("\n", warnings.Take(12));
            if (warnings.Count > 12) preview += $"\n외 {warnings.Count - 12}개 항목";
            if (MessageBox.Show(this,
                "모드 의존성 또는 불러오기 순서 문제가 발견되었습니다.\n\n" + preview +
                "\n\n문제를 확인하지 않고 계속 적용할까요?",
                "모드 구성 검사", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
        }

        if (MessageBox.Show(this,
            "활성화 상태와 모드 순서, 카테고리 및 태그 설정을 저장할까요?\n기존 설정 파일은 백업됩니다.",
            "적용 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            store.SaveAll();
            SetStatus("설정을 저장했습니다. 게임을 다시 시작하면 활성 모드 구성이 적용됩니다.");
            MessageBox.Show(this, "설정을 저장했습니다. 게임을 다시 시작하면 활성 모드 구성이 적용됩니다.",
                "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private List<string> GetApplyWarnings()
    {
        if (store == null) return new List<string>();
        var warnings = new List<string>();
        var installed = new HashSet<string>(store.Mods.Select(mod => mod.PackageId), StringComparer.OrdinalIgnoreCase);
        List<ModEntry> active = store.OrderedMods.Where(mod => mod.Active || mod.Official).ToList();
        var activePositions = active.Select((mod, index) => (mod.PackageId, index))
            .ToDictionary(item => item.PackageId, item => item.index, StringComparer.OrdinalIgnoreCase);
        var activeIds = new HashSet<string>(active.Select(mod => mod.PackageId), StringComparer.OrdinalIgnoreCase);

        foreach (ModEntry mod in active)
        {
            foreach (string dependency in mod.Required)
            {
                if (activeIds.Contains(dependency)) continue;
                string state = installed.Contains(dependency) ? "비활성" : "미설치";
                warnings.Add($"{mod.Name}: 필수 모드 {dependency} ({state})");
            }

            foreach (string dependency in mod.LoadAfter)
            {
                if (!activePositions.TryGetValue(dependency, out int dependencyIndex)) continue;
                if (activePositions[mod.PackageId] < dependencyIndex)
                    warnings.Add($"{mod.Name}: {dependency}보다 뒤에 있어야 합니다 (LoadAfter)");
            }

            foreach (string dependency in mod.LoadBefore)
            {
                if (!activePositions.TryGetValue(dependency, out int dependencyIndex)) continue;
                if (activePositions[mod.PackageId] > dependencyIndex)
                    warnings.Add($"{mod.Name}: {dependency}보다 앞에 있어야 합니다 (LoadBefore)");
            }

            if (mod.HasVersionMismatch)
                warnings.Add($"{mod.Name}: 현재 게임 버전 {store.GameVersion?.Major}.{store.GameVersion?.Minor}을 지원하지 않을 수 있습니다.");
        }

        return warnings.Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void DiscardChanges()
    {
        if (store == null) return;
        if (MessageBox.Show(this,
            "저장하지 않은 변경 사항을 버리고 디스크에 저장된 모드 설정을 다시 불러올까요?",
            "변경 취소", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Reload();
    }

    private static bool GameIsRunning() => new[] { "RimWorldWin64", "RimWorld", "RimWorldLinux" }.Any(name => Process.GetProcessesByName(name).Length > 0);

    private void SavePreset()
    {
        if (store == null) return;
        string? name = Prompt.Show(this, "저장할 프리셋 이름을 입력하세요.", "프리셋 저장");
        if (string.IsNullOrWhiteSpace(name)) return;
        try { store.SavePreset(name); SetStatus("프리셋을 저장했습니다: " + name); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "프리셋 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LoadPreset()
    {
        if (store == null) return;
        List<PresetData> presets = store.LoadPresets();
        if (presets.Count == 0) { MessageBox.Show(this, "저장된 프리셋이 없습니다."); return; }
        using var dialog = new PresetDialog(presets);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Selected == null) return;
        if (dialog.DeleteSelected) { store.DeletePreset(dialog.Selected); SetStatus("프리셋을 삭제했습니다."); return; }
        store.LoadPreset(dialog.Selected); RefreshFilters(); RefreshGrid(); SetStatus("프리셋을 편집 상태로 불러왔습니다. '적용 / 저장'을 눌러 게임 설정에 반영하세요.");
    }

    private void PersistManagerSettings(string operation)
    {
        if (store == null) return;
        if (GameIsRunning())
        {
            SetStatus($"{operation}: 림월드 실행 중에는 설정 파일을 덮어쓰지 않습니다. 게임 종료 후 '적용 / 저장'하세요.");
            return;
        }
        try { store.SaveManagerDataImmediately(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, operation + "\n" + ex.Message,
                "관리자 설정 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddCategory()
    {
        if (store == null) return;
        string? name = Prompt.Show(this, "새 카테고리 이름", "카테고리 추가");
        if (string.IsNullOrWhiteSpace(name)) return;
        string trimmedName = name.Trim();
        if (store.Categories.Any(category => category.Name.Equals(trimmedName, StringComparison.CurrentCultureIgnoreCase)))
        {
            MessageBox.Show(this, "같은 이름의 카테고리가 이미 있습니다.");
            return;
        }
        string id = "custom-" + Guid.NewGuid().ToString("N");
        store.Categories.Add(new CategoryData { Id = id, Name = trimmedName, Folders = [new FolderData()] });
        PersistManagerSettings("카테고리 추가");
        RefreshFilters();
    }

    private void AddTag()
    {
        if (store == null) return;
        string? name = Prompt.Show(this, "새 태그 이름", "태그 추가"); if (string.IsNullOrWhiteSpace(name)) return;
        if (store.Tags.Any(t => t.Name.Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase))) { MessageBox.Show(this, "같은 이름의 태그가 이미 있습니다."); return; }
        store.Tags.Add(new TagData { Id = "tag-" + Guid.NewGuid().ToString("N"), Name = name.Trim(), Folders = [new FolderData()] });
        PersistManagerSettings("태그 추가");
        RefreshFilters();
    }

    private CategoryData? SelectedCategory()
    {
        if (store == null || sidebarTagMode) return null;
        if (!string.IsNullOrEmpty(sidebarSelectedCategoryId))
            return store.Categories.FirstOrDefault(category => category.Id == sidebarSelectedCategoryId);
        string name = categoryFilter.SelectedItem?.ToString() ?? "전체";
        return name == "전체" ? null : store.Categories.FirstOrDefault(category => category.Name == name);
    }

    private TagData? SelectedTag()
    {
        if (store == null) return null;
        if (!string.IsNullOrEmpty(sidebarSelectedTagId))
            return store.Tags.FirstOrDefault(tag => tag.Id == sidebarSelectedTagId);
        string name = tagFilter.SelectedItem?.ToString() ?? "모든 태그";
        return name == "모든 태그" ? null : store.Tags.FirstOrDefault(tag => tag.Name == name);
    }

    private void RenameCategory()
    {
        CategoryData? category = SelectedCategory();
        if (category == null) { MessageBox.Show(this, "카테고리 필터에서 변경할 카테고리를 먼저 선택하세요."); return; }
        string? name = Prompt.Show(this, "새 카테고리 이름", "카테고리 이름 변경");
        if (string.IsNullOrWhiteSpace(name)) return;
        string trimmedName = name.Trim();
        if (store!.Categories.Any(other => other != category && other.Name.Equals(trimmedName, StringComparison.CurrentCultureIgnoreCase)))
        {
            MessageBox.Show(this, "같은 이름의 카테고리가 이미 있습니다.");
            return;
        }
        category.Name = trimmedName;
        PersistManagerSettings("카테고리 이름 변경");
        RefreshFilters(); RefreshGrid();
    }

    private void DeleteCategory()
    {
        if (store == null) return;
        CategoryData? category = SelectedCategory();
        if (category == null) { MessageBox.Show(this, "카테고리 필터에서 삭제할 카테고리를 먼저 선택하세요."); return; }
        if (new[] { "before", "core", "normal", "after" }.Contains(category.Id))
        {
            MessageBox.Show(this, "기본 카테고리는 삭제할 수 없습니다."); return;
        }
        if (MessageBox.Show(this, "이 카테고리를 삭제하고 포함된 모드를 '일반 모드'로 이동할까요?", "카테고리 삭제", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        store.AssignCategory(category.ModIds.ToList(), "normal");
        store.Categories.Remove(category);
        PersistManagerSettings("카테고리 삭제");
        RefreshFilters(); RefreshGrid();
    }

    private void RenameTag()
    {
        TagData? tag = SelectedTag();
        if (tag == null || tag.IsSystem) { MessageBox.Show(this, "태그 필터에서 사용자 태그를 선택하세요."); return; }
        string? name = Prompt.Show(this, "새 태그 이름", "태그 이름 변경");
        if (string.IsNullOrWhiteSpace(name)) return;
        string trimmedName = name.Trim();
        if (store!.Tags.Any(other => other != tag && other.Name.Equals(trimmedName, StringComparison.CurrentCultureIgnoreCase)))
        {
            MessageBox.Show(this, "같은 이름의 태그가 이미 있습니다.");
            return;
        }
        tag.Name = trimmedName;
        PersistManagerSettings("태그 이름 변경");
        RefreshFilters(); RefreshGrid();
    }

    private void DeleteTag()
    {
        if (store == null) return;
        TagData? tag = SelectedTag();
        if (tag == null || tag.IsSystem) { MessageBox.Show(this, "태그 필터에서 삭제할 사용자 태그를 선택하세요."); return; }
        if (MessageBox.Show(this, "이 태그를 삭제하고 모든 모드에서 연결을 해제할까요?", "태그 삭제", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        store.RemoveTag(tag);
        PersistManagerSettings("태그 삭제");
        RefreshFilters(); RefreshGrid();
    }

    private void CreateFolderForSelected()
    {
        if (store == null) return;
        List<string> ids = SelectedIds(); if (ids.Count == 0) return;
        ModEntry? first = store.OrderedMods.FirstOrDefault(m => m.PackageId == ids[0]); if (first == null) return;
        CategoryData? category = store.Categories.FirstOrDefault(c => c.Id == first.CategoryId); if (category == null) return;
        string? name = Prompt.Show(this, "새 폴더 이름", "폴더 추가"); if (string.IsNullOrWhiteSpace(name)) return;
        string folderId = "folder-" + Guid.NewGuid().ToString("N");
        string folderName = name.Trim();
        if (category.Folders.Any(existing => existing.Name.Equals(folderName, StringComparison.CurrentCultureIgnoreCase)))
        {
            MessageBox.Show(this, "같은 이름의 폴더가 이미 있습니다.");
            return;
        }
        var folder = new FolderData { Id = folderId, Name = folderName, ModIds = [] };
        category.Folders.Add(folder);
        store.AssignToCategoryFolder(ids, category, folder);
        PersistManagerSettings("폴더 추가 및 모드 이동");
        RefreshGrid();
    }

    private void CreateTagFolderForSelected()
    {
        if (store == null) return;
        TagData? tag = SelectedTag();
        List<string> ids = SelectedIds();
        if (tag == null || tag.IsSystem || ids.Count == 0)
        {
            MessageBox.Show(this, "태그 필터에서 사용자 태그를 선택한 뒤 모드를 선택하세요.");
            return;
        }
        string? name = Prompt.Show(this, "새 태그 폴더 이름", "태그 폴더 추가");
        if (string.IsNullOrWhiteSpace(name)) return;
        string folderName = name.Trim();
        FolderData? folder = tag.Folders.FirstOrDefault(existing =>
            existing.Name.Equals(folderName, StringComparison.CurrentCultureIgnoreCase));
        if (folder == null)
        {
            folder = new FolderData { Id = "folder-" + Guid.NewGuid().ToString("N"), Name = folderName, ModIds = [] };
            tag.Folders.Add(folder);
        }
        store.AssignToTagFolder(ids, tag, folder);
        PersistManagerSettings("태그 폴더 추가 및 모드 이동");
        RefreshSidebar();
        RefreshGrid();
    }

    private FolderData? GetCurrentCategoryFolder(out CategoryData? category)
    {
        category = null;
        if (store == null || GetPrimarySelectedMod() is not ModEntry mod) return null;
        category = store.Categories.FirstOrDefault(c => c.Id == mod.CategoryId);
        return category?.Folders.FirstOrDefault(f => f.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase));
    }

    private void RenameSelectedFolder()
    {
        FolderData? folder = GetCurrentCategoryFolder(out CategoryData? category);
        if (folder == null || category == null || folder.Id == "uncategorized")
        {
            MessageBox.Show(this, "이름을 변경할 사용자 폴더를 선택하세요.");
            return;
        }
        string? name = Prompt.Show(this, "새 폴더 이름", "폴더 이름 변경");
        if (string.IsNullOrWhiteSpace(name)) return;
        string folderName = name.Trim();
        if (category!.Folders.Any(other => other != folder && other.Name.Equals(folderName, StringComparison.CurrentCultureIgnoreCase)))
        {
            MessageBox.Show(this, "같은 이름의 폴더가 이미 있습니다.");
            return;
        }
        folder.Name = folderName;
        PersistManagerSettings("폴더 이름 변경");
        RefreshGrid();
    }

    private void DeleteSelectedFolder()
    {
        FolderData? folder = GetCurrentCategoryFolder(out CategoryData? category);
        if (folder == null || category == null || folder.Id == "uncategorized")
        {
            MessageBox.Show(this, "삭제할 사용자 폴더를 선택하세요.");
            return;
        }
        if (MessageBox.Show(this, "폴더를 삭제하고 포함된 모드를 '미분류'로 이동할까요?", "폴더 삭제", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        FolderData? target = category.Folders.FirstOrDefault(f => f.Id == "uncategorized");
        if (target == null)
        {
            target = new FolderData();
            category.Folders.Insert(0, target);
        }
        foreach (string id in folder.ModIds.ToList())
            if (!target.ModIds.Contains(id, StringComparer.OrdinalIgnoreCase)) target.ModIds.Add(id);
        category.Folders.Remove(folder);
        PersistManagerSettings("폴더 삭제");
        RefreshGrid();
    }

    private FolderData? GetCurrentTagFolder(out TagData? tag)
    {
        tag = SelectedTag();
        if (tag == null || GetPrimarySelectedMod() is not ModEntry mod)
            return null;
        return tag.Folders.FirstOrDefault(folder =>
            folder.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase));
    }

    private void RenameSelectedTagFolder()
    {
        FolderData? folder = GetCurrentTagFolder(out TagData? tag);
        if (folder == null || tag == null || folder.Id == "uncategorized")
        {
            MessageBox.Show(this, "이름을 변경할 태그 폴더를 선택하세요.");
            return;
        }
        string? name = Prompt.Show(this, "새 태그 폴더 이름", "태그 폴더 이름 변경");
        if (string.IsNullOrWhiteSpace(name)) return;
        string folderName = name.Trim();
        if (tag!.Folders.Any(other => other != folder && other.Name.Equals(folderName, StringComparison.CurrentCultureIgnoreCase)))
        {
            MessageBox.Show(this, "같은 이름의 폴더가 이미 있습니다.");
            return;
        }
        folder.Name = folderName;
        PersistManagerSettings("태그 폴더 이름 변경");
        RefreshGrid();
    }

    private void DeleteSelectedTagFolder()
    {
        FolderData? folder = GetCurrentTagFolder(out TagData? tag);
        if (folder == null || tag == null || folder.Id == "uncategorized")
        {
            MessageBox.Show(this, "삭제할 태그 폴더를 선택하세요.");
            return;
        }
        if (MessageBox.Show(this, "태그 폴더를 삭제하고 포함된 모드를 '미분류'로 이동할까요?",
            "태그 폴더 삭제", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        FolderData? target = tag.Folders.FirstOrDefault(item => item.Id == "uncategorized");
        if (target == null)
        {
            target = new FolderData();
            tag.Folders.Insert(0, target);
        }
        foreach (string id in folder.ModIds.ToList())
            if (!target.ModIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                target.ModIds.Add(id);
        tag.Folders.Remove(folder);
        PersistManagerSettings("태그 폴더 삭제");
        RefreshGrid();
    }

    private void AutoSort()
    {
        if (store == null) return;
        store.AutoSort(); RefreshGrid(); SetStatus("선행/후행 조건을 기준으로 자동 정렬했습니다. 적용 전 결과를 확인하세요.");
    }

    private void MoveSelected(int direction)
    {
        if (store == null || selectedPackageIds.Count == 0) return;
        var selected = SelectedIds()
            .Select(id => store.OrderedMods.FindIndex(mod => mod.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(index => index >= 0 && !store.OrderedMods[index].Official)
            .OrderBy(index => index).ToList();
        var selectedSet = new HashSet<int>(selected);
        if (direction < 0)
        {
            foreach (int i in selected) if (i > 0 && !selectedSet.Contains(i - 1)) { (store.OrderedMods[i - 1], store.OrderedMods[i]) = (store.OrderedMods[i], store.OrderedMods[i - 1]); selectedSet.Remove(i); selectedSet.Add(i - 1); }
        }
        else
        {
            foreach (int i in selected.OrderByDescending(x => x)) if (i < store.OrderedMods.Count - 1 && !selectedSet.Contains(i + 1)) { (store.OrderedMods[i + 1], store.OrderedMods[i]) = (store.OrderedMods[i], store.OrderedMods[i + 1]); selectedSet.Remove(i); selectedSet.Add(i + 1); }
        }
        RefreshGrid();
    }

    private void OpenSelectedFolder()
    {
        if (GetPrimarySelectedMod() is not ModEntry mod) return;
        if (!Directory.Exists(mod.Path)) { MessageBox.Show(this, "이 모드의 설치 폴더를 찾지 못했습니다."); return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + mod.Path + "\"") { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    private void OpenWorkshopPage()
    {
        if (GetPrimarySelectedMod() is not ModEntry mod || !mod.Workshop) return;
        string id = new DirectoryInfo(mod.Path).Name;
        try { Process.Start(new ProcessStartInfo("https://steamcommunity.com/sharedfiles/filedetails/?id=" + Uri.EscapeDataString(id)) { UseShellExecute = true }); } catch { }
    }

    private void LaunchGame()
    {
        if (store == null) return;
        string exe = System.IO.Path.Combine(store.Paths.GameRoot, "RimWorldWin64.exe");
        try
        {
            if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = store.Paths.GameRoot, UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("steam://run/294100") { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "게임 실행 실패"); }
    }

    private void SetStatus(string message) => status.Text = "  " + message;
}

internal sealed class PresetDialog : Form
{
    private readonly ListBox list = new() { Dock = DockStyle.Fill };
    public PresetData? Selected => list.SelectedItem as PresetData;
    public bool DeleteSelected { get; private set; }
    public PresetDialog(List<PresetData> presets)
    {
        Text = "프리셋 선택"; Width = 420; Height = 500; StartPosition = FormStartPosition.CenterParent;
        list.DataSource = presets; list.DisplayMember = "Name";
        var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft };
        var load = new Button { Text = "불러오기", DialogResult = DialogResult.OK, Width = 90 };
        var delete = new Button { Text = "삭제", Width = 90 };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Width = 90 };
        delete.Click += (_, _) => { if (Selected != null && MessageBox.Show(this, "이 프리셋을 삭제할까요?", "삭제 확인", MessageBoxButtons.YesNo) == DialogResult.Yes) { DeleteSelected = true; DialogResult = DialogResult.OK; Close(); } };
        panel.Controls.Add(cancel); panel.Controls.Add(delete); panel.Controls.Add(load); Controls.Add(list); Controls.Add(panel); AcceptButton = load; CancelButton = cancel;
    }
}

internal static class Prompt
{
    public static string? Show(IWin32Window owner, string label, string title)
    {
        using var form = new Form { Text = title, Width = 390, Height = 145, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var text = new TextBox { Left = 14, Top = 18, Width = 345 };
        var ok = new Button { Text = "확인", Left = 190, Top = 56, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "취소", Left = 280, Top = 56, Width = 80, DialogResult = DialogResult.Cancel };
        form.Controls.Add(new Label { Text = label, Left = 14, Top = 0, Width = 345 }); form.Controls.Add(text); form.Controls.Add(ok); form.Controls.Add(cancel); form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK ? text.Text : null;
    }
}