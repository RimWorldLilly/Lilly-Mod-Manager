
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal static class ManagerPresetStorage
    {
        private const string FolderName = "Lilly Mod Manager Presets";

        private static string PresetDirectory
        {
            get
            {
                return Path.Combine(
                    GenFilePaths.ConfigFolderPath,
                    FolderName);
            }
        }

        private static void EnsureDirectory()
        {
            Directory.CreateDirectory(PresetDirectory);
        }

        private static string SanitizeFileName(string name)
        {
            if (name.NullOrEmpty())
                return "";

            char[] invalid = Path.GetInvalidFileNameChars();

            string result = new string(
                name.Trim()
                    .Select(c => invalid.Contains(c) ? '_' : c)
                    .ToArray());

            return result.Trim().Trim('.');
        }

        private static string GetPresetPath(string name)
        {
            string safeName = SanitizeFileName(name);

            if (safeName.NullOrEmpty())
                throw new InvalidOperationException(
                    "프리셋 이름이 올바르지 않습니다.");

            return Path.Combine(
                PresetDirectory,
                safeName + ".xml");
        }

        private static void SaveToPath(
            ManagerPresetData preset,
            string path)
        {
            ManagerPresetData copy = preset.Copy();

            Scribe.saver.InitSaving(
                path,
                "LillyModManagerPreset");

            Scribe_Deep.Look(ref copy, "preset");

            Scribe.saver.FinalizeSaving();
        }

        private static ManagerPresetData LoadFromPath(string path)
        {
            ManagerPresetData preset = null;

            try
            {
                Scribe.loader.InitLoading(path);

                Scribe_Deep.Look(ref preset, "preset");

                Scribe.loader.FinalizeLoading();

                return preset;
            }
            catch
            {
                Log.Warning(
                    "[Lilly Mod Manager] 프리셋 읽기 실패: " + path);

                return null;
            }
        }

        internal static List<ManagerPresetData> LoadAndMigrate(
            IntegratedModManagerSettings settings)
        {
            EnsureDirectory();

            // 기존 설정 파일에 들어 있던 프리셋을 개별 파일로 분리
            if (settings.Presets != null && settings.Presets.Count > 0)
            {
                bool migrationSucceeded = true;

                foreach (ManagerPresetData oldPreset in settings.Presets)
                {
                    if (oldPreset == null ||
                        oldPreset.Name.NullOrEmpty())
                        continue;

                    try
                    {
                        string path = GetPresetPath(oldPreset.Name);

                        // 같은 이름의 파일이 이미 있으면 덮어쓰지 않음
                        if (!File.Exists(path))
                            SaveToPath(oldPreset, path);
                    }
                    catch (Exception exception)
                    {
                        migrationSucceeded = false;

                        Log.Warning(
                            "[Lilly Mod Manager] 기존 프리셋 분리 실패: " +
                            oldPreset.Name + "\n" + exception);
                    }
                }

                // 분리가 성공했을 때만 기존 설정의 프리셋 제거
                if (migrationSucceeded)
                {
                    settings.Presets.Clear();

                    if (IntegratedModManagerMod.Instance != null)
                        IntegratedModManagerMod.Instance.WriteSettings();
                }
            }

            return LoadAll();
        }

        internal static List<ManagerPresetData> LoadAll()
        {
            EnsureDirectory();

            List<ManagerPresetData> result =
                new List<ManagerPresetData>();

            foreach (string path in Directory.GetFiles(
                PresetDirectory,
                "*.xml",
                SearchOption.TopDirectoryOnly))
            {
                ManagerPresetData preset = LoadFromPath(path);

                if (preset == null ||
                    preset.Id.NullOrEmpty() ||
                    preset.Name.NullOrEmpty())
                    continue;

                result.Add(preset);
            }

            return result
                .OrderBy(preset => preset.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static void Save(ManagerPresetData preset)
        {
            if (preset == null)
                return;

            EnsureDirectory();

            SaveToPath(
                preset,
                GetPresetPath(preset.Name));
        }

        internal static void Delete(ManagerPresetData preset)
        {
            if (preset == null)
                return;

            string path = GetPresetPath(preset.Name);

            if (File.Exists(path))
                File.Delete(path);
        }

        internal static void SaveAll(
            IEnumerable<ManagerPresetData> presets)
        {
            EnsureDirectory();

            foreach (ManagerPresetData preset in presets)
            {
                if (preset == null)
                    continue;

                Save(preset);
            }
        }
    }
}
