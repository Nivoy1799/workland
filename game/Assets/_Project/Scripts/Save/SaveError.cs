namespace Worktown.Game.Save
{
    /// <summary>Spec 053: why a save could not be loaded (<see cref="ShopSaveCodec.TryParse"/>, <see cref="SaveStore.Load"/>).</summary>
    public enum SaveError : byte
    {
        None,
        /// <summary>More than <see cref="ShopSaveCodec.MaxBytes"/>.</summary>
        TooLarge,
        /// <summary>Empty, cut off or not JSON (also a file that could not be read).</summary>
        NotJson,
        /// <summary>JSON, but not a Worktown shop save.</summary>
        WrongFormat,
        /// <summary>Written by a newer build (a higher formatVersion than this build knows).</summary>
        NewerVersion,
        /// <summary>A shop save whose identity is broken (its id or its times).</summary>
        Invalid
    }
}
