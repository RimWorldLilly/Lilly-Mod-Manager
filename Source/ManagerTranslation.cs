using Verse;

namespace Lilly.IntegratedModManager
{
    internal static class ManagerTranslation
    {
        internal static string Get(string key)
        {
            return key.Translate().ToString();
        }
    }
}
