using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal static class DesktopShortcut
    {
        private static string modRoot;

        internal static void Initialize(string rootDirectory)
        {
            modRoot = rootDirectory;

            // EXE가 아직 빌드되지 않았다면 다음 실행 또는 옵션 버튼에서 다시 시도한다.
            if (!File.Exists(ExecutablePath) || File.Exists(ShortcutPath))
                return;

            TryCreate(showResult: false);
        }

        internal static void CreateFromOptions()
        {
            if (!File.Exists(ExecutablePath))
            {
                Messages.Message(ManagerTranslation.Get("LMM_0210"),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            if (TryCreate(showResult: true))
                Messages.Message(ManagerTranslation.Get("LMM_0211"),
                    MessageTypeDefOf.PositiveEvent);
        }

        private static string ExecutablePath
        {
            get { return Path.Combine(modRoot ?? "", "Lilly.ModManager.exe"); }
        }

        private static string ShortcutPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "Lilly - Mod Manager.lnk");
            }
        }

        private static bool TryCreate(bool showResult)
        {
            object shell = null;
            object shortcut = null;

            try
            {
                if (String.IsNullOrEmpty(modRoot) || !File.Exists(ExecutablePath))
                    return false;

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    throw new InvalidOperationException("Windows Script Host is unavailable.");

                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { ShortcutPath });

                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty,
                    null, shortcut, new object[] { ExecutablePath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty,
                    null, shortcut, new object[] { modRoot });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty,
                    null, shortcut, new object[] { "Lilly - Mod Manager" });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty,
                    null, shortcut, new object[] { ExecutablePath + ",0" });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod,
                    null, shortcut, null);

                return true;
            }
            catch (Exception exception)
            {
                Log.Warning("[Lilly Mod Manager] 바탕화면 바로가기 생성 실패: " + exception);
                if (showResult)
                    Messages.Message(ManagerTranslation.Get("LMM_0212"),
                        MessageTypeDefOf.RejectInput);
                return false;
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                    Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell))
                    Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}