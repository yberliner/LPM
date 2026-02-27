using Microsoft.AspNetCore.Components;

namespace LPM.Shared
{
    /// <summary>
    ///  Functionality that all accordion-style controls (Motor, LED …) need.
    /// </summary>
    public abstract class AccordionInputBase : ComponentBase
    {
        /* ----------   Collapsing ---------- */

        protected bool IsExpanded { get; set; } = true;

        protected void ToggleCollapse() => IsExpanded = !IsExpanded;

        /* ----------   Input helpers  ------- */

    }
}
