using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// The server-side request behind one page of <c>flowrun</c> rows: one flow, one half-open UTC
    /// window, and optionally a status restriction. Built from a <see cref="MonitoringQuery"/> so
    /// the criteria the operator asked for are what the source is asked for, and so the plugin's
    /// query text lives in exactly one place.
    /// </summary>
    public sealed class RunQueryCriteria
    {
        public RunQueryCriteria(
            WorkflowKey workflow,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            IEnumerable<RunStatus>? statuses,
            int pageSize)
        {
            if (workflow == null)
            {
                throw new ArgumentNullException(nameof(workflow));
            }

            if (toUtc <= fromUtc)
            {
                throw new ArgumentException("The run window must move forward in time.", nameof(toUtc));
            }

            Workflow = workflow;
            FromUtc = fromUtc.ToUniversalTime();
            ToUtc = toUtc.ToUniversalTime();
            PageSize = pageSize < 1 ? PageReader.DefaultPageSize : pageSize;

            List<string> values = new List<string>();
            if (statuses != null)
            {
                foreach (RunStatus status in statuses)
                {
                    foreach (string value in StatusNormalizer.ProviderValuesFor(status))
                    {
                        if (!values.Contains(value))
                        {
                            values.Add(value);
                        }
                    }
                }
            }

            // The raw column is free text, so an Unknown selection can only mean "everything the
            // status filter would otherwise exclude"; it is expressed as no restriction rather
            // than a match on an empty string.
            StatusValues = values.ToArray();
        }

        /// <summary>The flow whose runs are wanted; its id becomes the server-side filter.</summary>
        public WorkflowKey Workflow { get; }

        public DateTimeOffset FromUtc { get; }

        public DateTimeOffset ToUtc { get; }

        /// <summary>Provider status texts to keep. Empty means every status, including unknown text.</summary>
        public IReadOnlyList<string> StatusValues { get; }

        public int PageSize { get; }

        /// <summary>
        /// How far outside the requested window the server-side read is widened. The
        /// <c>starttime</c> column is declared with <c>DateTimeBehavior UserLocal</c>, so a bound
        /// given in UTC may be interpreted in the connecting user's zone; the offsets in play reach
        /// +/- 14 hours. Widening the query cannot add a run to the answer (the core re-applies the
        /// exact half-open window), while a server that narrowed it would silently hide runs.
        /// </summary>
        public static readonly TimeSpan ServerSkewAllowance = TimeSpan.FromDays(1);

        /// <summary>Window start actually sent to the source: <see cref="FromUtc"/> widened.</summary>
        public DateTimeOffset ServerFromUtc
        {
            get
            {
                DateTimeOffset widened = FromUtc - ServerSkewAllowance;
                return widened < DateTimeOffset.MinValue ? DateTimeOffset.MinValue : widened;
            }
        }

        /// <summary>Window end actually sent to the source: <see cref="ToUtc"/> widened.</summary>
        public DateTimeOffset ServerToUtc
        {
            get
            {
                DateTimeOffset widened = ToUtc + ServerSkewAllowance;
                return widened > DateTimeOffset.MaxValue ? DateTimeOffset.MaxValue : widened;
            }
        }

        /// <summary>Derive the request for one flow straight from the operator's query.</summary>
        public static RunQueryCriteria From(MonitoringQuery query, WorkflowKey workflow)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            return new RunQueryCriteria(workflow, query.FromUtc, query.ToUtc, query.HasStatusFilter ? query.Statuses : null, PageReader.DefaultPageSize);
        }
    }
}
