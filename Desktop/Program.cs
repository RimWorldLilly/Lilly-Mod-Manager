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
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class ModEntry
{
    public string PackageId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Workshop { get; set; }
    public bool Official { get; set; }
    public bool Active { get; set; }
    public string CategoryId { get; set; } = "normal";
    public List<string> LoadAfter { get; set; } = [];
    public List<string> LoadBefore { get; set; } = [];
    public List<string> Required { get; set; } = [];
    public string Issue { get; set; } = "";
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
    public string ManagerSettings => System.IO.Path.Combine(ConfigRoot, "ModSettings", "Lilly.ModManager.xml");
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
    public List<ModEntry> OrderedMods { get; private set; } = [];

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
                foreach (Match match in Regex.Matches(text, "\"path\"\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
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

    private List<string> ReadActiveMods()
    {
        XDocument doc = XDocument.Load(Paths.ModsConfig);
        XElement? list = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "activeMods");
        return list?.Elements().Where(e => e.Name.LocalName == "li").Select(e => e.Value.Trim()).Where(s => s.Length > 0).ToList() ?? [];
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

    private static string Value(XElement parent, string name) => parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";
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
        if (File.Exists(Paths.ManagerSettings))
        {
            try
            {
                XDocument doc = XDocument.Load(Paths.ManagerSettings);
                XElement manager = doc.Root?.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "Lilly.ModManager") ?? doc.Root ?? new XElement("Lilly.ModManager");
                Categories = ParseCategories(manager.Element("categories"));
                Tags = ParseTags(manager.Element("tags"));
                States = ParseStates(manager.Element("mods"));
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
        var stateIds = new HashSet<string>(States.Select(s => s.PackageId), StringComparer.OrdinalIgnoreCase);
        foreach (ModEntry mod in OrderedMods)
        {
            if (stateIds.Contains(mod.PackageId)) continue;
            States.Add(new ModState { PackageId = mod.PackageId, Enabled = mod.Active, InstalledVersion = mod.Version });
        }
        AssignUnclassifiedMods();
    }

    private static CategoryData NewCategory(string id, string name) => new() { Id = id, Name = name, Folders = [NewFolder()] };
    private static FolderData NewFolder() => new() { Id = "uncategorized", Name = "미분류" };

    private static List<CategoryData> ParseCategories(XElement? list) => list?.Elements("li").Select(e => new CategoryData
    {
        Id = Value(e, "id"), Name = Value(e, "name"), Enabled = BoolValue(e, "enabled", true), ColorHex = NullIfEmpty(Value(e, "colorHex")),
        ModIds = ListValues(e, "mods"), Folders = ParseFolders(e.Element("folders"))
    }).Where(c => c.Id.Length > 0).ToList() ?? [];
    private static List<TagData> ParseTags(XElement? list) => list?.Elements("li").Select(e => new TagData
    {
        Id = Value(e, "id"), Name = Value(e, "name"), IsSystem = BoolValue(e, "isSystem", false), ColorHex = NullIfEmpty(Value(e, "colorHex")), Folders = ParseFolders(e.Element("folders"))
    }).Where(t => t.Id.Length > 0).ToList() ?? [];
    private static List<ModState> ParseStates(XElement? list) => list?.Elements("li").Select(e => new ModState
    {
        PackageId = Value(e, "packageId"), Enabled = BoolValue(e, "enabled", true), DisplayName = NullIfEmpty(Value(e, "displayName")), ColorHex = NullIfEmpty(Value(e, "colorHex")), InstalledVersion = NullIfEmpty(Value(e, "installedVersion")), TagIds = ListValues(e, "tags")
    }).Where(s => s.PackageId.Length > 0).ToList() ?? [];
    private static List<FolderData> ParseFolders(XElement? list) => list?.Elements("li").Select(e => new FolderData
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
            EnsureFolder(category.Folders).ModIds.Add(mod.PackageId);
        }
        foreach (var c in Categories) EnsureFolder(c.Folders);
        foreach (var t in Tags) EnsureFolder(t.Folders);
    }

    private static FolderData EnsureFolder(List<FolderData> folders)
    {
        FolderData? folder = folders.FirstOrDefault(f => f.Id == "uncategorized");
        if (folder == null) { folder = NewFolder(); folders.Insert(0, folder); }
        return folder;
    }

    private void ApplyCategoryAndTagState()
    {
        foreach (ModEntry mod in Mods)
        {
            CategoryData? category = Categories.FirstOrDefault(c => c.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase));
            mod.CategoryId = category?.Id ?? "normal";
        }
        foreach (ModEntry mod in Mods)
        {
            ModState? state = FindState(mod.PackageId);
            if (state != null) state.Enabled = mod.Active;
        }
    }

    private void RebuildIssues()
    {
        var ids = new HashSet<string>(Mods.Select(m => m.PackageId), StringComparer.OrdinalIgnoreCase);
        foreach (ModEntry mod in OrderedMods)
        {
            if (string.IsNullOrWhiteSpace(mod.Path))
            {
                mod.Issue = "설치된 모드 폴더 없음";
                continue;
            }
            var missing = mod.Required.Where(id => !ids.Contains(id)).ToList();
            mod.Issue = missing.Count > 0 ? "필수 모드 누락: " + string.Join(", ", missing) : "";
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
            ModEntry? mod = Mods.FirstOrDefault(m => m.PackageId == id);
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
            if (add) EnsureFolder(tag.Folders).ModIds.Add(id);
            else foreach (var folder in tag.Folders) folder.ModIds.RemoveAll(m => m.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void RemoveTag(TagData tag)
    {
        if (tag.IsSystem) return;
        Tags.Remove(tag);
        foreach (ModState state in States)
            state.TagIds.RemoveAll(id => id.Equals(tag.Id, StringComparison.OrdinalIgnoreCase));
    }

    public void AssignToCategoryFolder(IEnumerable<string> packageIds, CategoryData category, FolderData target)
    {
        foreach (string id in packageIds)
        {
            foreach (FolderData folder in category.Folders)
                folder.ModIds.RemoveAll(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
            target.ModIds.Add(id);
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
        foreach (ModEntry mod in OrderedMods) GetState(mod.PackageId).Enabled = mod.Active || mod.Official;
        SaveModsConfig();
        SaveManagerSettings();
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

    private void SaveManagerSettings()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Paths.ManagerSettings)!);
        XDocument doc = File.Exists(Paths.ManagerSettings) ? XDocument.Load(Paths.ManagerSettings) : new XDocument(new XElement("ModSettingsData"));
        XElement root = doc.Root ?? throw new InvalidDataException("ModSettings XML root is missing.");
        XElement manager = root.Name.LocalName == "Lilly.ModManager" ? root : root.Elements().FirstOrDefault(e => e.Name.LocalName == "Lilly.ModManager") ?? new XElement("Lilly.ModManager");
        if (manager.Parent == null) root.Add(manager);
        ReplaceCollection(manager, "categories", Categories.Select(WriteCategory));
        ReplaceCollection(manager, "tags", Tags.Select(WriteTag));
        ReplaceCollection(manager, "mods", States.Select(WriteState));
        BackupFile(Paths.ManagerSettings);
        SaveAtomic(doc, Paths.ManagerSettings);
    }

    private static void ReplaceCollection(XElement parent, string name, IEnumerable<XElement> items)
    {
        XElement collection = new(name, items);
        XElement? existing = parent.Elements().FirstOrDefault(e => e.Name.LocalName == name);
        if (existing == null) parent.Add(collection); else existing.ReplaceWith(collection);
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
        Categories = preset.Categories.Select(Clone).ToList();
        Tags = preset.Tags.Select(Clone).ToList();
        States = preset.Mods.Select(Clone).ToList();
        var enabled = new HashSet<string>(States.Where(s => s.Enabled).Select(s => s.PackageId), StringComparer.OrdinalIgnoreCase);
        var disabledCategories = new HashSet<string>(Categories.Where(c => !c.Enabled).Select(c => c.Id));
        foreach (ModEntry mod in Mods)
        {
            string categoryId = Categories.FirstOrDefault(c => c.ModIds.Contains(mod.PackageId))?.Id ?? "";
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

internal sealed class MainForm : Form
{
    private ModStore? store;
    private readonly TextBox search = new() { Width = 190, PlaceholderText = "모드 이름 / 제작자 / PackageId 검색" };
    private readonly ComboBox categoryFilter = new() { Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox tagFilter = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false, AutoGenerateColumns = false, BackgroundColor = Color.FromArgb(28, 31, 37), BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false };
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
        try
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LillyModManager.ico");
            return File.Exists(path) ? new Icon(path) : null;
        }
        catch { return null; }
    }

    private void BuildUi()
    {
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(8, 6, 4, 4), WrapContents = true, BackColor = Color.FromArgb(35, 39, 46) };
        AddToolbarLabel(toolbar, "검색"); toolbar.Controls.Add(search);
        AddToolbarLabel(toolbar, "카테고리"); toolbar.Controls.Add(categoryFilter);
        AddToolbarLabel(toolbar, "태그"); toolbar.Controls.Add(tagFilter);
        AddButton(toolbar, "새로고침", Reload);
        AddButton(toolbar, "적용 / 저장", Apply);
        AddButton(toolbar, "프리셋 저장", SavePreset);
        AddButton(toolbar, "프리셋 불러오기", LoadPreset);
        AddButton(toolbar, "자동 정렬", AutoSort);
        AddButton(toolbar, "위로", () => MoveSelected(-1));
        AddButton(toolbar, "아래로", () => MoveSelected(1));
        AddButton(toolbar, "카테고리 추가", AddCategory);
        AddButton(toolbar, "카테고리 이름 변경", RenameCategory);
        AddButton(toolbar, "카테고리 삭제", DeleteCategory);
        AddButton(toolbar, "태그 추가", AddTag);
        AddButton(toolbar, "태그 이름 변경", RenameTag);
        AddButton(toolbar, "태그 삭제", DeleteTag);
        AddButton(toolbar, "경로 설정", ChoosePaths);
        AddButton(toolbar, "게임 실행", LaunchGame);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 610, BackColor = Color.FromArgb(23, 26, 31), Panel1MinSize = 240, Panel2MinSize = 100 };
        split.Panel1.Controls.Add(grid); split.Panel2.Controls.Add(details);
        Controls.Add(split); Controls.Add(toolbar); Controls.Add(status);

        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "active", HeaderText = "활성", Width = 48 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "모드 이름", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "category", HeaderText = "카테고리", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "tags", HeaderText = "태그", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "version", HeaderText = "버전", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "source", HeaderText = "설치 위치", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "issue", HeaderText = "검사", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "package", Visible = false });
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(47, 53, 62); grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.DefaultCellStyle.BackColor = Color.FromArgb(31, 35, 42); grid.DefaultCellStyle.ForeColor = Color.Gainsboro;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(44, 91, 112); grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(36, 40, 48);
        grid.RowTemplate.Height = 28; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns[0].FillWeight = 35; grid.Columns[1].FillWeight = 190; grid.Columns[2].FillWeight = 85; grid.Columns[3].FillWeight = 120; grid.Columns[4].FillWeight = 60; grid.Columns[5].FillWeight = 55; grid.Columns[6].FillWeight = 115;
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += GridCellValueChanged; grid.SelectionChanged += (_, _) => UpdateDetails();
        grid.CellMouseDown += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !grid.Rows[e.RowIndex].Selected) { grid.ClearSelection(); grid.Rows[e.RowIndex].Selected = true; } };
        grid.MouseUp += GridMouseUp;
        search.TextChanged += (_, _) => RefreshGrid(); categoryFilter.SelectedIndexChanged += (_, _) => RefreshGrid(); tagFilter.SelectedIndexChanged += (_, _) => RefreshGrid();
    }

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
        try { store!.Load(); RefreshFilters(); RefreshGrid(); SetStatus($"모드 {store.Mods.Count}개 검색 · 활성 {store.Mods.Count(m => m.Active)}개"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "모드 목록을 불러오지 못했습니다.", MessageBoxButtons.OK, MessageBoxIcon.Error); SetStatus(ex.Message); }
    }

    private void RefreshFilters()
    {
        if (store == null) return;
        string oldCat = categoryFilter.SelectedItem?.ToString() ?? "전체";
        string oldTag = tagFilter.SelectedItem?.ToString() ?? "모든 태그";
        categoryFilter.Items.Clear(); categoryFilter.Items.Add("전체");
        foreach (var c in store.Categories) categoryFilter.Items.Add(c.Name);
        categoryFilter.SelectedItem = categoryFilter.Items.Contains(oldCat) ? oldCat : "전체";
        tagFilter.Items.Clear(); tagFilter.Items.Add("모든 태그");
        foreach (var t in store.Tags.Where(t => !t.IsSystem)) tagFilter.Items.Add(t.Name);
        tagFilter.SelectedItem = tagFilter.Items.Contains(oldTag) ? oldTag : "모든 태그";
    }

    private void RefreshGrid()
    {
        if (store == null) return;
        grid.Rows.Clear();
        string q = search.Text.Trim();
        string catName = categoryFilter.SelectedItem?.ToString() ?? "전체";
        string tagName = tagFilter.SelectedItem?.ToString() ?? "모든 태그";
        foreach (ModEntry mod in store.OrderedMods)
        {
            CategoryData? cat = store.Categories.FirstOrDefault(c => c.Id == mod.CategoryId);
            ModState state = store.GetState(mod.PackageId);
            List<string> names = state.TagIds.Select(id => store.Tags.FirstOrDefault(t => t.Id == id)?.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Cast<string>().ToList();
            if (catName != "전체" && cat?.Name != catName) continue;
            if (tagName != "모든 태그" && !names.Contains(tagName)) continue;
            if (q.Length > 0 && !string.Join(" ", mod.Name, mod.PackageId, mod.Author, mod.Description).Contains(q, StringComparison.CurrentCultureIgnoreCase)) continue;
            int index = grid.Rows.Add(mod.Active || mod.Official, mod.Name, cat?.Name ?? "미분류", string.Join(", ", names), mod.Version, mod.Official ? "공식" : mod.Workshop ? "창작마당" : "로컬", mod.Issue, mod.PackageId);
            if (mod.Issue.Length > 0) grid.Rows[index].DefaultCellStyle.ForeColor = Color.Salmon;
            grid.Rows[index].Tag = mod;
        }
        UpdateDetails();
    }

    private void GridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (store == null || e.RowIndex < 0 || e.ColumnIndex != 0) return;
        if (grid.Rows[e.RowIndex].Tag is not ModEntry mod) return;
        mod.Active = mod.Official || Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[0].Value);
        store.GetState(mod.PackageId).Enabled = mod.Active;
        SetStatus($"{mod.Name}: {(mod.Active ? "활성화" : "비활성화")} 예정 · '적용 / 저장'을 눌러 반영하세요.");
    }

    private void UpdateDetails()
    {
        if (grid.SelectedRows.Count == 0) { details.Clear(); return; }
        var mod = grid.SelectedRows[0].Tag as ModEntry;
        if (mod == null) return;
        details.Text = $"{mod.Name}\n{mod.PackageId}\n제작자: {mod.Author}\n버전: {mod.Version}\n경로: {mod.Path}\n\n{mod.Description}\n\n{mod.Issue}";
    }

    private List<string> SelectedIds() => grid.SelectedRows.Cast<DataGridViewRow>().Select(r => (r.Tag as ModEntry)?.PackageId).Where(x => !string.IsNullOrEmpty(x)).Cast<string>().ToList();

    private void GridMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || store == null) return;
        var menu = new ContextMenuStrip();
        var categoryMenu = new ToolStripMenuItem("카테고리로 이동");
        foreach (CategoryData category in store.Categories)
        {
            CategoryData captured = category;
            categoryMenu.DropDownItems.Add(category.Name, null, (_, _) => { store.AssignCategory(SelectedIds(), captured.Id); RefreshGrid(); });
        }
        menu.Items.Add(categoryMenu);
        var tagMenu = new ToolStripMenuItem("태그 지정 / 해제");
        foreach (TagData tag in store.Tags.Where(t => !t.IsSystem))
        {
            TagData captured = tag;
            var item = new ToolStripMenuItem(tag.Name) { CheckOnClick = true, Checked = SelectedIds().Any(id => store.GetState(id).TagIds.Contains(captured.Id)) };
            item.Click += (_, _) => { store.AssignTag(SelectedIds(), captured, item.Checked); RefreshGrid(); };
            tagMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(tagMenu);
        menu.Items.Add("선택 항목으로 폴더 만들기", null, (_, _) => CreateFolderForSelected());
        menu.Items.Add("모드 폴더 열기", null, (_, _) => OpenSelectedFolder());
        menu.Items.Add("창작마당 페이지 열기", null, (_, _) => OpenWorkshopPage());
        menu.Show(grid, e.Location);
    }

    private void Apply()
    {
        if (store == null) return;
        if (GameIsRunning()) { MessageBox.Show(this, "림월드가 실행 중일 때는 설정 파일을 덮어쓸 수 없습니다. 게임을 종료한 뒤 적용하세요.", "적용할 수 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (MessageBox.Show(this, "활성화 상태와 모드 순서, 카테고리 및 태그 설정을 저장할까요?\n기존 설정 파일은 백업됩니다.", "적용 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { store.SaveAll(); SetStatus("설정을 저장했습니다. 게임을 다시 시작하면 활성 모드 구성이 적용됩니다."); MessageBox.Show(this, "설정을 저장했습니다. 게임을 다시 시작하면 활성 모드 구성이 적용됩니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        catch (Exception ex) { MessageBox.Show(this, ex.ToString(), "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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

    private void AddCategory()
    {
        if (store == null) return;
        string? name = Prompt.Show(this, "새 카테고리 이름", "카테고리 추가"); if (string.IsNullOrWhiteSpace(name)) return;
        string id = "custom-" + Guid.NewGuid().ToString("N"); store.Categories.Add(new CategoryData { Id = id, Name = name.Trim(), Folders = [new FolderData()] }); RefreshFilters();
    }

    private void AddTag()
    {
        if (store == null) return;
        string? name = Prompt.Show(this, "새 태그 이름", "태그 추가"); if (string.IsNullOrWhiteSpace(name)) return;
        if (store.Tags.Any(t => t.Name.Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase))) { MessageBox.Show(this, "같은 이름의 태그가 이미 있습니다."); return; }
        store.Tags.Add(new TagData { Id = "tag-" + Guid.NewGuid().ToString("N"), Name = name.Trim(), Folders = [new FolderData()] }); RefreshFilters();
    }

    private CategoryData? SelectedCategory()
    {
        if (store == null) return null;
        string name = categoryFilter.SelectedItem?.ToString() ?? "전체";
        return name == "전체" ? null : store.Categories.FirstOrDefault(c => c.Name == name);
    }

    private TagData? SelectedTag()
    {
        if (store == null) return null;
        string name = tagFilter.SelectedItem?.ToString() ?? "모든 태그";
        return name == "모든 태그" ? null : store.Tags.FirstOrDefault(t => t.Name == name);
    }

    private void RenameCategory()
    {
        CategoryData? category = SelectedCategory();
        if (category == null) { MessageBox.Show(this, "카테고리 필터에서 변경할 카테고리를 먼저 선택하세요."); return; }
        string? name = Prompt.Show(this, "새 카테고리 이름", "카테고리 이름 변경");
        if (string.IsNullOrWhiteSpace(name)) return;
        category.Name = name.Trim(); RefreshFilters(); RefreshGrid();
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
        store.Categories.Remove(category); RefreshFilters(); RefreshGrid();
    }

    private void RenameTag()
    {
        TagData? tag = SelectedTag();
        if (tag == null || tag.IsSystem) { MessageBox.Show(this, "태그 필터에서 사용자 태그를 선택하세요."); return; }
        string? name = Prompt.Show(this, "새 태그 이름", "태그 이름 변경");
        if (string.IsNullOrWhiteSpace(name)) return;
        tag.Name = name.Trim(); RefreshFilters(); RefreshGrid();
    }

    private void DeleteTag()
    {
        if (store == null) return;
        TagData? tag = SelectedTag();
        if (tag == null || tag.IsSystem) { MessageBox.Show(this, "태그 필터에서 삭제할 사용자 태그를 선택하세요."); return; }
        if (MessageBox.Show(this, "이 태그를 삭제하고 모든 모드에서 연결을 해제할까요?", "태그 삭제", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        store.RemoveTag(tag); RefreshFilters(); RefreshGrid();
    }

    private void CreateFolderForSelected()
    {
        if (store == null) return;
        List<string> ids = SelectedIds(); if (ids.Count == 0) return;
        ModEntry? first = store.Mods.FirstOrDefault(m => m.PackageId == ids[0]); if (first == null) return;
        CategoryData? category = store.Categories.FirstOrDefault(c => c.Id == first.CategoryId); if (category == null) return;
        string? name = Prompt.Show(this, "새 폴더 이름", "폴더 추가"); if (string.IsNullOrWhiteSpace(name)) return;
        string folderId = "folder-" + Guid.NewGuid().ToString("N");
        var folder = new FolderData { Id = folderId, Name = name.Trim(), ModIds = ids };
        category.Folders.Add(folder);
        foreach (string id in ids) { foreach (var f in category.Folders.Where(f => f.Id != folderId)) f.ModIds.Remove(id); }
        RefreshGrid();
    }

    private void AutoSort()
    {
        if (store == null) return;
        store.AutoSort(); RefreshGrid(); SetStatus("선행/후행 조건을 기준으로 자동 정렬했습니다. 적용 전 결과를 확인하세요.");
    }

    private void MoveSelected(int direction)
    {
        if (store == null || grid.SelectedRows.Count == 0) return;
        var selected = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => store.OrderedMods.IndexOf((ModEntry)r.Tag!)).Where(i => i >= 0).OrderBy(i => i).ToList();
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
        if (grid.SelectedRows.Count == 0 || grid.SelectedRows[0].Tag is not ModEntry mod) return;
        if (!Directory.Exists(mod.Path)) { MessageBox.Show(this, "이 모드의 설치 폴더를 찾지 못했습니다."); return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + mod.Path + "\"") { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    private void OpenWorkshopPage()
    {
        if (grid.SelectedRows.Count == 0 || grid.SelectedRows[0].Tag is not ModEntry mod || !mod.Workshop) return;
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