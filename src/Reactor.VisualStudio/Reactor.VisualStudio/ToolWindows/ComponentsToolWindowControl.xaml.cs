namespace Reactor.VisualStudio.ToolWindows
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using EnvDTE;
    using Microsoft.VisualStudio.Shell;
    using Reactor.VisualStudio.Services;

    /// <summary>
    /// Interaction logic for ComponentsToolWindowControl.xaml.
    /// Lists core elements and user-defined components, supporting double-click navigation.
    /// </summary>
    public partial class ComponentsToolWindowControl : UserControl
    {
        /// <summary>
        /// Gets the collection of components bound to the list view.
        /// </summary>
        public ObservableCollection<ComponentItem> Items { get; } = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="ComponentsToolWindowControl"/> class.
        /// </summary>
        public ComponentsToolWindowControl()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ReactorInProcPackage.Log("ComponentsToolWindowControl: Constructor started.");

            try
            {
                InitializeComponent();

                this.DataContext = this;

                this.SetResourceReference(UserControl.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
                ComponentsListView.SetResourceReference(ListView.ForegroundProperty, VsBrushes.ToolWindowTextKey);

                ReactorInProcPackage.Log("ComponentsToolWindowControl: Constructor UI elements initialized.");
            }
            catch (Exception ex)
            {
                ReactorInProcPackage.Log($"ComponentsToolWindowControl: Critical exception in constructor: {ex}");

                throw;
            }

            ReactorInProcPackage.Log("ComponentsToolWindowControl: Constructor completed.");
        }

        /// <summary>
        /// Rebuilds the static core component list.
        /// </summary>
        public void RefreshList()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ReactorInProcPackage.Log("ComponentsToolWindowControl: RefreshList started.");

            try
            {
                Items.Clear();

                AddCoreElements();

                ReactorInProcPackage.Log($"ComponentsToolWindowControl: RefreshList completed successfully. Items count: {Items.Count}");
            }
            catch (Exception ex)
            {
                ReactorInProcPackage.Log($"ComponentsToolWindowControl: RefreshList failed: {ex}");
            }
        }

        private void AddCoreElements()
        {
            var coreNames = new[]
            {
                "TextBlock", "Button", "CheckBox", "TextBox", "Image",
                "Slider", "ComboBox", "VStack", "HStack", "Grid",
                "ListView", "ItemsRepeater", "WebView2", "NavigationView",
                "Expander", "ToggleSwitch", "Border", "Canvas", "RelativePanel"
            };

            foreach (var name in coreNames)
            {
                Items.Add(new ComponentItem
                {
                    Name       = name,
                    TypeGroup  = "Core elements",
                    SourceFile = ""
                });
            }
        }
    }

    /// <summary>
    /// Represents a single component item in the list control.
    /// </summary>
    public class ComponentItem
    {
        /// <summary>
        /// Gets or sets the name of the component.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Gets or sets the type grouping ("Core elements" or "User Components").
        /// </summary>
        public string TypeGroup { get; set; } = "";

        /// <summary>
        /// Gets or sets the file path for custom components.
        /// </summary>
        public string SourceFile { get; set; } = "";
    }
}
