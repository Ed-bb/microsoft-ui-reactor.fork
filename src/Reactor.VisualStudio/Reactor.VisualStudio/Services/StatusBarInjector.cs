namespace Reactor.VisualStudio.Services
{
    using System;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using Microsoft.VisualStudio.Shell;

    /// <summary>
    /// Injects custom WPF controls into the Visual Studio status bar by walking the visual tree.
    /// </summary>
    internal static class StatusBarInjector
    {
        private const string StatusBarPanelName = "StatusBarPanel";
        private const int StatusBarRetryDelayMilliseconds = 5000;

        private static DockPanel? s_panel;

        /// <summary>
        /// Injects a WPF framework element into the status bar.
        /// </summary>
        /// <param name="element">The control to inject.</param>
        /// <returns>A task that completes when the control is injected.</returns>
        public static async Task InjectControlAsync(FrameworkElement element)
        {
            if (element == null)
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            await EnsureUiAsync();

            if (s_panel == null)
            {
                return;
            }

            element.SetValue(DockPanel.DockProperty, Dock.Right);

            s_panel.Children.Add(element);
        }

        private static async Task EnsureUiAsync()
        {
            while (s_panel == null)
            {
                s_panel = FindChild(Application.Current?.MainWindow, StatusBarPanelName) as DockPanel;

                if (s_panel == null)
                {
                    await Task.Delay(StatusBarRetryDelayMilliseconds);
                }
            }
        }

        private static DependencyObject? FindChild(DependencyObject? parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childrenCount; i++)
            {
                DependencyObject? child = VisualTreeHelper.GetChild(parent, i);

                if (child is FrameworkElement frameworkElement && frameworkElement.Name == childName)
                {
                    return frameworkElement;
                }

                child = FindChild(child, childName);

                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
