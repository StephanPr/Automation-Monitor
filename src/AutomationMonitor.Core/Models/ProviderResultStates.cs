using System;
using System.Collections.Generic;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// The one ranking of <see cref="ProviderResultState"/>. Combining two reads - a watchlist
    /// built from several solution reads, or a run query spanning many workflows - must degrade in
    /// the same direction everywhere, so the ordering lives here instead of in each service.
    /// </summary>
    public static class ProviderResultStates
    {
        /// <summary>How far a state is from "everything was read". Complete is 0, worst is highest.</summary>
        public static int Severity(ProviderResultState state)
        {
            switch (state)
            {
                case ProviderResultState.Complete:
                    return 0;
                case ProviderResultState.Stale:
                    return 1;
                case ProviderResultState.Partial:
                    return 2;
                case ProviderResultState.Unsupported:
                    return 3;
                case ProviderResultState.Unauthorized:
                    return 4;
                default:
                    return 5;
            }
        }

        /// <summary>The worse of two states. An unrecognised state is treated as the worst case.</summary>
        public static ProviderResultState Worst(ProviderResultState left, ProviderResultState right)
        {
            return Severity(right) > Severity(left) ? right : left;
        }

        /// <summary>The worse of any number of states; <see cref="ProviderResultState.Complete"/> when none.</summary>
        public static ProviderResultState Worst(IEnumerable<ProviderResultState> states)
        {
            ProviderResultState worst = ProviderResultState.Complete;
            if (states == null)
            {
                return worst;
            }

            foreach (ProviderResultState state in states)
            {
                worst = Worst(worst, state);
            }

            return worst;
        }
    }
}
