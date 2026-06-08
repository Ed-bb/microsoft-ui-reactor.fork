namespace Reactor.VisualStudio.ToolWindows
{
    using System;
    using System.Runtime.InteropServices;
    using Microsoft.VisualStudio.Shell;

    /// <summary>
    /// The Visual Studio tool window hosting the list of Reactor Components.
    /// </summary>
    [Guid("8e51b14c-4e89-4b6a-9fb8-ffc3d6ff472e")]
    public class ComponentsToolWindow : ToolWindowPane
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ComponentsToolWindow"/> class.
        /// </summary>
        public ComponentsToolWindow()
            : base(null)
        {
            ReactorInProcPackage.Log("ComponentsToolWindow: Constructor started.");

            this.Caption = "Reactor Components";

            this.Content = new ComponentsToolWindowControl();

            ReactorInProcPackage.Log("ComponentsToolWindow: Constructor completed.");
        }
    }
}
