using System.Collections.Generic;

namespace Worktown.Game.Art
{
    /// <summary>Spec 027d: the customer looks, in variant order (<see cref="ArtCatalogRules.CustomerCharacterKeys"/>). Palette colours only.</summary>
    public static class CustomerLooks
    {
        private const string Male = "skaterMaleA";
        private const string Female = "skaterFemaleA";

        public static IReadOnlyList<CustomerLook> All { get; } = new[]
        {
            new CustomerLook(ArtKey.CharacterCustomerMale, Male, false, null, null, null),
            new CustomerLook(ArtKey.CharacterCustomerFemale, Female, true, null, null, null),
            new CustomerLook(ArtKey.CharacterCustomerMale2, Male, false, Palette.DeepBlue, Palette.WoodDark, Palette.Mustard),
            new CustomerLook(ArtKey.CharacterCustomerFemale2, Female, true, Palette.Mustard, Palette.DeepBlue, Palette.Terracotta),
            new CustomerLook(ArtKey.CharacterCustomerMale3, Male, false, Palette.Sage, Palette.Charcoal, Palette.Ink),
            new CustomerLook(ArtKey.CharacterCustomerFemale3, Female, true, Palette.SoftBlue, Palette.Terracotta, Palette.Mustard)
        };

        public static CustomerLook Of(ArtKey key)
        {
            foreach (CustomerLook look in All) if (look.Key == key) return look;
            return null;
        }

        /// <summary>The look of the customer with this network object id (stable: the same id, the same look).</summary>
        public static CustomerLook ForCustomer(int objectId) => Of(ArtCatalogRules.CustomerCharacterKey(objectId));
    }
}
