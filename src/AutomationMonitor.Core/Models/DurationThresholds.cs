using System;
using System.Globalization;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// Duration limits an operator wants applied to runs. This is <strong>configuration
    /// carriage only</strong>: M4 stores it and inherits it, M5's rule evaluator consumes it.
    /// Nothing in this milestone highlights, filters or alerts on the strength of a threshold,
    /// and no second rule engine may be built on top of it.
    ///
    /// <para>
    /// A null value means "not set at this level", which is what makes the documented inheritance
    /// <c>global defaults -&gt; watchlist -&gt; workflow</c> expressible: a level stores only what
    /// it actually overrides. An unset threshold is never treated as zero, because zero would mean
    /// "every completed run is slow".
    /// </para>
    /// </summary>
    public sealed class DurationThresholds
    {
        /// <summary>One week, in seconds. Anything longer is a mistake, not a threshold.</summary>
        public const long MaximumSeconds = 7 * 24 * 60 * 60;

        public DurationThresholds(long? slowCompletedSeconds, long? maximumRunningSeconds)
        {
            SlowCompletedSeconds = slowCompletedSeconds;
            MaximumRunningSeconds = maximumRunningSeconds;
        }

        /// <summary>Nothing set at this level.</summary>
        public static DurationThresholds Empty
        {
            get { return new DurationThresholds(null, null); }
        }

        /// <summary>A completed run longer than this is "slow". Null means not set here.</summary>
        public long? SlowCompletedSeconds { get; }

        /// <summary>A run still going longer than this is overdue. Null means not set here.</summary>
        public long? MaximumRunningSeconds { get; }

        /// <summary>True when this level overrides nothing, so the next level up decides.</summary>
        public bool IsEmpty
        {
            get { return SlowCompletedSeconds == null && MaximumRunningSeconds == null; }
        }

        public DurationThresholds WithSlowCompleted(long? slowCompletedSeconds)
        {
            return new DurationThresholds(slowCompletedSeconds, MaximumRunningSeconds);
        }

        public DurationThresholds WithMaximumRunning(long? maximumRunningSeconds)
        {
            return new DurationThresholds(SlowCompletedSeconds, maximumRunningSeconds);
        }

        /// <summary>
        /// Validates stored values. A negative or absurd threshold is a data defect: it is reported
        /// rather than clamped, because a silently clamped limit is exactly the kind of rule the
        /// operator would later be unable to explain.
        /// </summary>
        public static bool TryCreate(long? slowCompletedSeconds, long? maximumRunningSeconds, out DurationThresholds thresholds, out string? problem)
        {
            thresholds = Empty;
            problem = null;

            if (slowCompletedSeconds.HasValue && (slowCompletedSeconds.Value < 0 || slowCompletedSeconds.Value > MaximumSeconds))
            {
                problem = "The stored slow-run threshold must be between 0 and "
                    + MaximumSeconds.ToString(CultureInfo.InvariantCulture) + " second(s).";
                return false;
            }

            if (maximumRunningSeconds.HasValue && (maximumRunningSeconds.Value < 0 || maximumRunningSeconds.Value > MaximumSeconds))
            {
                problem = "The stored maximum-running threshold must be between 0 and "
                    + MaximumSeconds.ToString(CultureInfo.InvariantCulture) + " second(s).";
                return false;
            }

            thresholds = new DurationThresholds(slowCompletedSeconds, maximumRunningSeconds);
            return true;
        }

        /// <summary>
        /// The value that applies: this level's override when set, otherwise the value inherited
        /// from above. Inheritance direction only - the decision of what a threshold means is M5's.
        /// </summary>
        public long? EffectiveSlowCompleted(DurationThresholds? inherited)
        {
            if (SlowCompletedSeconds.HasValue)
            {
                return SlowCompletedSeconds;
            }

            return inherited == null ? null : inherited.SlowCompletedSeconds;
        }

        public long? EffectiveMaximumRunning(DurationThresholds? inherited)
        {
            if (MaximumRunningSeconds.HasValue)
            {
                return MaximumRunningSeconds;
            }

            return inherited == null ? null : inherited.MaximumRunningSeconds;
        }

        public string Describe()
        {
            if (IsEmpty)
            {
                return "no duration thresholds set";
            }

            string slow = SlowCompletedSeconds.HasValue
                ? SlowCompletedSeconds.Value.ToString(CultureInfo.InvariantCulture) + "s"
                : "not set";
            string running = MaximumRunningSeconds.HasValue
                ? MaximumRunningSeconds.Value.ToString(CultureInfo.InvariantCulture) + "s"
                : "not set";

            return "slow completed run: " + slow + "; overdue while running: " + running;
        }

        public override string ToString()
        {
            return Describe();
        }
    }
}
