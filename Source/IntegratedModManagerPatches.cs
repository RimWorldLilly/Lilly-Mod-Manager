
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Lilly.IntegratedModManager
{
    [StaticConstructorOnStartup]
    internal static class IntegratedModManagerStartup
    {
        static IntegratedModManagerStartup()
        {
            new Harmony("Lilly.IntegratedModManager")
                .PatchAll(Assembly.GetExecutingAssembly());
        }

        // 림월드의 설치된 모드 목록 갱신
        internal static bool RefreshModList()
        {
            try
            {
                const BindingFlags flags =
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic;

                // 창작마당 설치 목록 갱신
                Type workshopItemsType =
                    typeof(ModLister).Assembly.GetType(
                        "Verse.Steam.WorkshopItems");

                MethodInfo rebuildWorkshopMethod =
                    workshopItemsType == null
                        ? null
                        : workshopItemsType.GetMethod(
                            "RebuildItemsList", flags);

                if (rebuildWorkshopMethod != null)
                {
                    rebuildWorkshopMethod.Invoke(null, null);
                    return true;
                }

                // 창작마당 갱신 함수를 찾지 못하면
                // 로컬 모드 목록 갱신
                MethodInfo rebuildModMethod =
                    typeof(ModLister).GetMethod(
                        "RebuildModList", flags);

                if (rebuildModMethod != null)
                {
                    rebuildModMethod.Invoke(null, null);
                    return true;
                }

                Log.Warning(
                    "[Lilly Mod Manager] 모드 목록 갱신 함수를 찾지 못했습니다.");

                return false;
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[Lilly Mod Manager] 모드 목록 갱신 실패: " +
                    exception);

                return false;
            }
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add),
        new[] { typeof(Window) })]
    internal static class WindowStack_Add_ModManager_Patch
    {
        private static void Prefix(ref Window __0)
        {
            if (__0 is Page_ModsConfig)
            {
                // 모드 관리자 창을 열기 전에 모드 목록 갱신
                IntegratedModManagerStartup.RefreshModList();

                __0 = new IntegratedModManagerPage();
            }
        }
    }
}
