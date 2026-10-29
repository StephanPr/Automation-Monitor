using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace AutomationMonitor.Plugin
{
    [Export(typeof(IXrmToolBoxPlugin))]
    [ExportMetadata("Name", "Automation Monitor")]
    [ExportMetadata("Description", "Monitor Power Automate flows and Azure Logic Apps")]
    [ExportMetadata("SmallImageBase64", null)]
    [ExportMetadata("BigImageBase64", null)]
    [ExportMetadata("BackgroundColor", "White")]
    [ExportMetadata("PrimaryFontColor", "Black")]
    [ExportMetadata("SecondaryFontColor", "Gray")]
    public class AutomationMonitorPlugin : PluginBase
    {
        public override IXrmToolBoxPluginControl GetControl()
        {
            return new AutomationMonitorControl();
        }
    }
}
