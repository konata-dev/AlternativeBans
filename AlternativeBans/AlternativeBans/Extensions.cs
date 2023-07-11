using Terraria;

namespace AlternativeBans
{
    public static class Extensions
    {
        public static string Color(this object obj, Microsoft.Xna.Framework.Color color)
        {
            return string.Format("[c/{0}:{1}]", color.Hex3(), obj);
        }
    }
}
