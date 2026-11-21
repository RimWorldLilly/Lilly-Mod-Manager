
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
    bool refreshed = false;

    try
    {
        const BindingFlags flags =
            BindingFlags.Static |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        // 1. 창작마당 구독 모드 목록 갱신
        Type workshopItemsType =
            typeof(ModLister).Assembly.GetType(
                "Verse.Steam.WorkshopItems");

        MethodInfo rebuildWorkshopMethod =
            workshopItemsType == null
                ? null
                : Array.Find(
                    workshopItemsType.GetMethods(flags),
                    method => method.Name == "RebuildItemsList" &&
                        method.GetParameters().Length == 0);

        if (rebuildWorkshopMethod != null)
        {
            rebuildWorkshopMethod.Invoke(null, null);
            refreshed = true;
        }

        // 2. 림월드의 실제 설치 모드 목록 갱신
        MethodInfo rebuildModMethod =
            Array.Find(
                typeof(ModLister).GetMethods(flags),
                method => method.Name == "RebuildModList" &&
                    method.GetParameters().Length == 0);

        if (rebuildModMethod != null)
        {
            rebuildModMethod.Invoke(null, null);
            refreshed = true;
        }

        if (!refreshed)
        {
            Log.Warning(
                "[Lilly Mod Manager] 모드 목록 갱신 함수를 찾지 못했습니다.");
        }

        return refreshed;
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
