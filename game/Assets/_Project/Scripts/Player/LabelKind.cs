namespace Worktown.Game.Player
{
    /// <summary>Spec 027d: the kinds of world text, each with its own rules in <see cref="LabelRules"/>.</summary>
    public enum LabelKind
    {
        /// <summary>A station's or crate's name on its front: small, always there, one-sided.</summary>
        Sign,
        /// <summary>An item's or cookware's name and status: only for the aimed or held one.</summary>
        Item,
        /// <summary>A customer's order: within range, full text only at the head of the queue.</summary>
        Bubble,
        /// <summary>The ticket rail's title and cards: one-sided.</summary>
        Rail,
        /// <summary>A stove button's caption: close up, one-sided.</summary>
        Button
    }
}
