using FishNet.Serializing;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 017: explicit FishNet writer and reader for <see cref="FoodLook"/> (6 bytes), so the wire
    /// format does not depend on codegen picking up the struct. FishNet finds these by signature.
    /// </summary>
    public static class FoodLookSerializer
    {
        public static void WriteFoodLook(this Writer writer, FoodLook value)
        {
            writer.WriteUInt8Unpacked(value.Ingredient);
            writer.WriteUInt8Unpacked(value.Shape);
            writer.WriteUInt8Unpacked(value.Doneness);
            writer.WriteUInt8Unpacked(value.Heat);
            writer.WriteUInt8Unpacked(value.Kind);
            writer.WriteUInt8Unpacked(value.PartCount);
        }

        public static FoodLook ReadFoodLook(this Reader reader)
        {
            byte ingredient = reader.ReadUInt8Unpacked();
            byte shape = reader.ReadUInt8Unpacked();
            byte doneness = reader.ReadUInt8Unpacked();
            byte heat = reader.ReadUInt8Unpacked();
            byte kind = reader.ReadUInt8Unpacked();
            byte partCount = reader.ReadUInt8Unpacked();
            return new FoodLook(ingredient, shape, doneness, heat, kind, partCount);
        }
    }
}
