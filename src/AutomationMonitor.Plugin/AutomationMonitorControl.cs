using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk.Query;
using XrmToolBox.Extensibility;

namespace AutomationMonitor.Plugin
{
    public class AutomationMonitorControl : PluginControlBase
    {
        private readonly Button refreshButton;
        private readonly DataGridView flowGrid;
        private readonly Label statusLabel;

        public AutomationMonitorControl()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 54 };
            refreshButton = new Button
            {
                Text = "Load cloud flows",
                Dock = DockStyle.Left,
                Width = 175,
                Height = 36
            };
            statusLabel = new Label
            {
                Text = "Connect to a Dataverse environment to begin.",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0)
            };
            header.Controls.Add(statusLabel);
            header.Controls.Add(refreshButton);

            flowGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            Controls.Add(flowGrid);
            Controls.Add(header);
            refreshButton.Click += (sender, args) => ExecuteMethod(LoadCloudFlows);
        }

        private void LoadCloudFlows()
        {
            // ExecuteMethod ensures the XrmToolBox Dataverse connection is available.
            var service = Service;
            refreshButton.Enabled = false;
            statusLabel.Text = "Loading flows...";

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading solution-aware cloud flows...",
                Work = (worker, args) =>
                {
                    var query = new QueryExpression("workflow")
                    {
                        ColumnSet = new ColumnSet("workflowid", "name"),
                        PageInfo = new PagingInfo { Count = 250, PageNumber = 1 }
                    };
                    query.Criteria.AddCondition(
                        "category", ConditionOperator.Equal, 5);

                    // M0/M1 starter only: retrieves the first page; add paging in M2.
                    var response = service.RetrieveMultiple(query);
                    var rows = new List<FlowRow>();
                    foreach (var entity in response.Entities)
                    {
                        rows.Add(new FlowRow
                        {
                            Id = entity.Id,
                            Name = entity.GetAttributeValue<string>("name") ?? "(unnamed)"
                        });
                    }
                    args.Result = rows;
                },
                PostWorkCallBack = args =>
                {
                    refreshButton.Enabled = true;
                    if (args.Error != null)
                    {
                        statusLabel.Text = "Failed to load flows.";
                        ShowErrorDialog(args.Error);
                        return;
                    }

                    var rows = (List<FlowRow>)args.Result;
                    flowGrid.DataSource = rows;
                    statusLabel.Text = string.Format(
                        "Loaded {0} flow(s) (first page only).", rows.Count);
                }
            });
        }

        public sealed class FlowRow
        {
            public string Name { get; set; }
            public Guid Id { get; set; }
        }
    }
}
