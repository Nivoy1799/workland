namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 041: what one <see cref="HandInput"/> means now (<see cref="ControlRules.IntentFor"/>).</summary>
    public enum HandIntent : byte
    {
        /// <summary>Nothing.</summary>
        None,
        /// <summary>Do the obvious thing with the target: the click's <see cref="InteractionAction"/> (first person), E's action (third).</summary>
        Act,
        /// <summary>A held action: squeeze, pour, scrub, shave, wipe (the press starts it, the release ends it).</summary>
        Hold,
        /// <summary>The knife's drag over a board with food (strokes).</summary>
        Stroke,
        /// <summary>Put what the hands hold on the aimed free spot (the ghost shows where).</summary>
        PutDown,
        /// <summary>Drop what the hands hold at your feet.</summary>
        Drop,
        /// <summary>Turn the food on the board (the knife's turn).</summary>
        TurnFood,
        /// <summary>Turn the held item's rest rotation by one <see cref="PlaceTurn.StepDegrees"/> step before it is put down.</summary>
        TurnRest,
        /// <summary>Third person: the camera's zoom.</summary>
        Zoom,
    }
}
