namespace Reactor.VisualStudio.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Renders parsed AstElement trees into styled HTML/CSS.
    /// </summary>
    public static class HtmlRenderer
    {
        /// <summary>
        /// Regular expression to match and extract resource keys and values from Set calls.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex ResourceSetRegex = new(
            @"\.Set\s*\(\s*""([^""]+)""\s*,\s*([^)]+)\s*\)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Regular expression that matches 4-digit Unicode escape sequences (e.g. "\u00E9").
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex Unicode4Regex = new(
            @"\\u([0-9a-fA-F]{4})",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Regular expression that matches 8-digit Unicode escape sequences (e.g. "\U0001F600").
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex Unicode8Regex = new(
            @"\\U([0-9a-fA-F]{8})",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Regular expression that matches simple escape sequences (e.g. \" \\ \n) and captures the escaped character.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex EscapeSeqRegex = new(
            @"\\(.)",
            System.Text.RegularExpressions.RegexOptions.Compiled);
        /// <summary>
        /// Generates the complete HTML document representing the preview of a component.
        /// </summary>
        /// <param name="astTree">The parsed AST syntax tree.</param>
        /// <param name="errors">List of compilation or parsing errors.</param>
        /// <param name="componentName">The name of the active component.</param>
        /// <returns>A formatted HTML page as a string.</returns>
        public static string GeneratePreviewHtml(AstElement? astTree, List<string> errors, string componentName)
        {
            var bodyBuilder = new StringBuilder();

            bodyBuilder.AppendLine("<div class=\"header\">");

            if (errors.Count > 0)
            {
                bodyBuilder.AppendLine("  <span class=\"badge warning\">Component Preview (with errors)</span>");
                bodyBuilder.AppendLine($"  <span class=\"title\">{componentName}</span>");
                bodyBuilder.AppendLine("</div>");

                bodyBuilder.AppendLine("<div class=\"console\">");
                bodyBuilder.AppendLine("  <div class=\"console-title\">Diagnostics</div>");
                bodyBuilder.AppendLine("  <ul class=\"console-errors\">");

                foreach (var err in errors)
                {
                    bodyBuilder.AppendLine($"    <li>{EscapeHtml(err)}</li>");
                }

                bodyBuilder.AppendLine("  </ul>");
                bodyBuilder.AppendLine("</div>");
            }
            else
            {
                bodyBuilder.AppendLine("  <span class=\"badge success\">Component Preview</span>");
                bodyBuilder.AppendLine($"  <span class=\"title\">{componentName}</span>");
                bodyBuilder.AppendLine("</div>");
            }

            bodyBuilder.AppendLine("<div class=\"preview-area\">");

            if (astTree != null)
            {
                bodyBuilder.AppendLine(RenderAstElement(astTree));
            }
            else
            {
                bodyBuilder.AppendLine("<div class=\"empty-state\">No renderable content found. Ensure the component has a valid Render method.</div>");
            }

            bodyBuilder.AppendLine("</div>");

            return GetBaseHtmlTemplate(bodyBuilder.ToString());
        }

        /// <summary>
        /// Renders a single AST element and its children into HTML.
        /// </summary>
        /// <param name="ast">The AST element to render. May be null.</param>
        /// <returns>HTML fragment representing the element and its children.</returns>
        private static string RenderAstElement(AstElement? ast)
        {
            if (ast == null)
            {
                return string.Empty;
            }

            var classes = new List<string> { "control" };
            var styles = new List<string>();

            foreach (var prop in ast.Properties)
            {
                ApplyPropertyStyles(prop, styles);
            }

            if (ast.Properties.TryGetValue("RequestedTheme", out var themeVal))
            {
                var lowerTheme = themeVal.ToLowerInvariant();

                if (lowerTheme.Contains("light"))
                {
                    classes.Add("theme-override-light");
                }
                else if (lowerTheme.Contains("dark"))
                {
                    classes.Add("theme-override-dark");
                }
            }

            if (ast.Properties.TryGetValue("Backdrop", out var backdropVal))
            {
                var lowerBackdrop = backdropVal.ToLowerInvariant();

                if (lowerBackdrop.Contains("mica"))
                {
                    classes.Add("backdrop-mica");
                }
                else if (lowerBackdrop.Contains("acrylic"))
                {
                    classes.Add("backdrop-acrylic");
                }
            }

            var customId = "elem_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var resources = new Dictionary<string, string>();
            var hasResources = false;

            if (ast.Properties.TryGetValue("Resources", out var resRaw))
            {
                var matches = ResourceSetRegex.Matches(resRaw);

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var key = match.Groups[1].Value;
                    var val = match.Groups[2].Value.Trim();
                    var parsedColor = ParseColor(val);

                    if (parsedColor != null)
                    {
                        val = parsedColor;
                    }
                    else
                    {
                        val = CleanResourceValue(val);
                    }

                    resources[key] = val;
                }

                if (resources.Count > 0)
                {
                    hasResources = true;
                }
            }

            var lowerName = ast.Name.ToLower();

            if (lowerName.Contains("text") || lowerName.Contains("label") || lowerName == "heading" || lowerName == "subheading" || lowerName == "caption")
            {
                return RenderTextBlock(ast, classes, styles, lowerName);
            }

            if (lowerName == "sectionheader")
            {
                return RenderSectionHeader(ast, classes, styles);
            }

            if (lowerName == "titlebar")
            {
                return RenderTitleBar(ast, classes, styles);
            }

            if (lowerName == "tabview")
            {
                return RenderTabView(ast, classes, styles);
            }

            if (lowerName == "tab")
            {
                return RenderTab(ast, classes, styles);
            }

            if (lowerName == "scrollview")
            {
                return RenderScrollView(ast, classes, styles);
            }

            if (lowerName == "component")
            {
                return RenderComponent(ast, classes, styles);
            }

            if (lowerName == "control")
            {
                return RenderWinRTControl(ast, classes, styles);
            }

            if (lowerName.Contains("button"))
            {
                return RenderButton(ast, classes, styles, customId, resources, hasResources);
            }

            if (lowerName.Contains("stack") || lowerName.Contains("flex") || lowerName.Contains("wrap"))
            {
                return RenderStack(ast, classes, styles, lowerName, customId, resources, hasResources);
            }

            if (lowerName.Contains("border"))
            {
                return RenderBorder(ast, classes, styles, customId, resources, hasResources);
            }

            if (lowerName.Contains("grid"))
            {
                return RenderGrid(ast, classes, styles, customId, resources, hasResources);
            }

            if (lowerName.Contains("textbox") || lowerName.Contains("input"))
            {
                return RenderTextBox(ast, classes, styles);
            }

            if (lowerName.Contains("backdrop"))
            {
                return RenderBackdrop(ast, classes, styles);
            }

            if (lowerName.Contains("checkbox") || lowerName == "toggleswitch")
            {
                return RenderCheckboxOrToggle(ast, classes, styles, lowerName);
            }

            if (lowerName == "empty")
            {
                return string.Empty;
            }

            if (lowerName == "codeexpressionplaceholder")
            {
                return RenderCodeExpressionPlaceholder(ast, classes, styles);
            }

            return RenderUnknownElement(ast, classes, styles, lowerName);
        }

        /// <summary>
        /// Cleans a resource value expression by removing type/new/constructor noise and normalizing numeric values.
        /// </summary>
        /// <param name="val">Raw resource expression.</param>
        /// <returns>Cleaned value suitable for CSS usage.</returns>
        private static string CleanResourceValue(string val)
        {
            var cleaned = val.Replace("new Microsoft.UI.Xaml.CornerRadius", "")
                             .Replace("new CornerRadius", "")
                             .Replace("Thickness", "")
                             .Replace("new", "")
                             .Replace("(", "")
                             .Replace(")", "")
                             .Trim();

            if (double.TryParse(cleaned, out _))
            {
                return cleaned + "px";
            }

            return cleaned;
        }

        /// <summary>
        /// Renders a placeholder for a code expression within the preview (non-evaluated).
        /// </summary>
        /// <param name="ast">The AST element representing the expression.</param>
        /// <param name="classes">CSS classes to apply.</param>
        /// <param name="styles">Inline styles to apply.</param>
        /// <returns>HTML for the code expression placeholder.</returns>
        private static string RenderCodeExpressionPlaceholder(AstElement ast, List<string> classes, List<string> styles)
        {
            classes.Add("code-expression-placeholder");
            styles.Add("border: 1px dashed rgba(255, 255, 255, 0.2); border-radius: 6px; padding: 12px 16px; background: rgba(255, 255, 255, 0.02); color: rgba(255, 255, 255, 0.5); font-family: monospace; font-size: 11px; margin: 4px 0; width: 100%; word-break: break-all;");

            var exprText = EscapeHtml(ast.Content ?? "Code Expression");
            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";

            return $@"<div{classAttr}{styleAttr}{titleAttr}>
                <div style=""font-weight: 600; font-size: 10px; color: var(--accent-color); text-transform: uppercase; margin-bottom: 4px; letter-spacing: 0.5px;"">Code Execution Required</div>
                <div>{exprText}</div>
            </div>";
        }

        /// <summary>
        /// Renders text-like elements such as TextBlock, Heading, Subheading and Caption.
        /// </summary>
        /// <param name="ast">The AST element.</param>
        /// <param name="classes">Mutable list of CSS classes to which this method may add.</param>
        /// <param name="styles">Mutable list of inline styles to which this method may add.</param>
        /// <param name="lowerName">Lower-cased name of the element for quick checks.</param>
        /// <returns>HTML for the text element.</returns>
        private static string RenderTextBlock(AstElement ast, List<string> classes, List<string> styles, string lowerName)
        {
            var tag = "span";

            classes.Add("textblock");

            if (ast.Properties.ContainsKey("Bold") || ast.Properties.ContainsKey("FontWeight") || lowerName == "heading" || lowerName == "subheading")
            {
                styles.Add("font-weight: bold;");
            }

            if (lowerName == "heading")
            {
                styles.Add("font-size: 20px; display: block; margin-bottom: 8px;");
            }
            else if (lowerName == "subheading")
            {
                styles.Add("font-size: 16px; font-weight: 600; display: block; margin-top: 16px; margin-bottom: 8px; color: #fff;");
            }
            else if (lowerName == "caption")
            {
                styles.Add("font-size: 12px; color: rgba(255, 255, 255, 0.6);");
            }

            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";

            return $"<{tag}{classAttr}{styleAttr}{titleAttr}>{EscapeHtml(ast.Content ?? "TextBlock")}</{tag}>";
        }

        /// <summary>
        /// Renders a section header element that contains a title and optional description.
        /// </summary>
        /// <param name="ast">The AST element.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <returns>HTML fragment for the section header.</returns>
        private static string RenderSectionHeader(AstElement ast, List<string> classes, List<string> styles)
        {
            var rawDesc = string.Empty;

            ast.Properties.TryGetValue("Description", out rawDesc);

            classes.Add("section-header");
            styles.Add("margin-bottom: 16px; width: 100%;");

            var titleText = EscapeHtml(ast.Content ?? string.Empty);
            var descriptionText = EscapeHtml(rawDesc ?? string.Empty);
            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";

            return $@"<div{classAttr}{styleAttr}{titleAttr}>
                <div class=""section-title"" style=""font-size: 24px; font-weight: bold; margin-bottom: 4px; color: #fff;"">{titleText}</div>
                <div class=""section-description"" style=""font-size: 13px; color: #a6a6a6;"">{descriptionText}</div>
            </div>";
        }

        /// <summary>
        /// Renders a title bar element used for headers with optional subtitle and window controls.
        /// </summary>
        /// <param name="ast">The AST element.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <returns>HTML fragment for the title bar.</returns>
        private static string RenderTitleBar(AstElement ast, List<string> classes, List<string> styles)
        {
            var rawSubtitle = string.Empty;

            ast.Properties.TryGetValue("Subtitle", out rawSubtitle);

            classes.Add("title-bar");

            var titleText = EscapeHtml(ast.Content ?? string.Empty);
            var subtitleText = EscapeHtml(rawSubtitle ?? string.Empty);
            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";

            return $@"<div{classAttr}{styleAttr}{titleAttr} style=""display: flex; justify-content: space-between; align-items: center; width: 100%; height: 32px; padding: 0 8px; margin-bottom: 16px;"">
                <div style=""display: flex; align-items: baseline; gap: 8px;"">
                    <span style=""font-size: 13px; font-weight: 700; color: #fff;"">{titleText}</span>
                    <span style=""font-size: 11px; color: #a6a6a6;"">{subtitleText}</span>
                </div>
                <div style=""display: flex; gap: 14px; font-size: 11px; color: #ffffff; cursor: pointer; user-select: none;"">
                    <span>&#8212;</span>
                    <span>&#9634;</span>
                    <span>&#x2715;</span>
                </div>
            </div>";
        }

        /// <summary>
        /// Renders a TabView element and its child tabs into an interactive tabbed HTML UI.
        /// </summary>
        /// <param name="ast">The AST element representing the TabView.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <returns>HTML representing the TabView and its content panels.</returns>
        private static string RenderTabView(AstElement ast, List<string> classes, List<string> styles)
        {
            var tabGroupId = "tabgroup_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var headerStrip = new StringBuilder();
            var panelsHtml = new StringBuilder();

            classes.Add("tabview-container");
            styles.Add("width: 100%; display: flex; flex-direction: column;");
            headerStrip.Append("<div class=\"tabview-header-strip\">");

            for (int i = 0; i < ast.Children.Count; i++)
            {
                var child = ast.Children[i];
                var tabTitle = child.Content ?? "Tab";
                var tabPanelId = tabGroupId + "_panel_" + i;

                headerStrip.Append($@"<button class=""tabview-header-btn{(i == 0 ? " active" : "")}"" onclick=""switchTab(this, '{tabPanelId}', '{tabGroupId}')"">{EscapeHtml(tabTitle)}</button>");

                panelsHtml.Append($@"<div id=""{tabPanelId}"" class=""{tabGroupId} tabview-content-panel"" style=""{(i == 0 ? "display: block;" : "display: none;")}"">");
                panelsHtml.Append(RenderAstElement(child));
                panelsHtml.Append("</div>");
            }

            headerStrip.Append("</div>");

            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";

            return $"<div{classAttr}{styleAttr}{titleAttr}>{headerStrip}{panelsHtml}</div>";
        }

        /// <summary>
        /// Renders an individual Tab element and its children.
        /// </summary>
        /// <param name="ast">The AST element for the Tab.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <returns>HTML for the tab content.</returns>
        private static string RenderTab(AstElement ast, List<string> classes, List<string> styles)
        {
            var content = new StringBuilder();

            classes.Add("tab-item");
            styles.Add("width: 100%;");

            foreach (var child in ast.Children)
            {
                content.Append(RenderAstElement(child));
            }

            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";

            return $"<div{classAttr}{styleAttr}{titleAttr}>{content}</div>";
        }

        /// <summary>
        /// Renders a ScrollView container and its children.
        /// </summary>
        /// <param name="ast">The AST element for the ScrollView.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <returns>HTML for the scrollable container.</returns>
        private static string RenderScrollView(AstElement ast, List<string> classes, List<string> styles)
        {
            var content = new StringBuilder();

            classes.Add("scrollview-container");

            foreach (var child in ast.Children)
            {
                content.Append(RenderAstElement(child));
            }

            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";

            return $"<div{classAttr}{styleAttr}{titleAttr}>{content}</div>";
        }

        /// <summary>
        /// Renders a custom component element and its children, including a component label for generics.
        /// </summary>
        /// <param name="ast">The AST element representing the component.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <returns>HTML for the component container.</returns>
        private static string RenderComponent(AstElement ast, List<string> classes, List<string> styles)
        {
            var genericType = string.Empty;
            var content = new StringBuilder();

            ast.Properties.TryGetValue("GenericType", out genericType);

            classes.Add("component-container");

            foreach (var child in ast.Children)
            {
                content.Append(RenderAstElement(child));
            }

            var componentLabel = string.IsNullOrEmpty(genericType) ? "Component" : $"Component &lt;{EscapeHtml(genericType)}&gt;";
            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";

            return $"<div{classAttr}{styleAttr}{titleAttr}><div class=\"component-label\">{componentLabel}</div>{content}</div>";
        }

        /// <summary>
        /// Renders a native WinRT control container and its children.
        /// </summary>
        /// <param name="ast">The AST element representing the WinRT control wrapper.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <returns>HTML for the WinRT control container.</returns>
        private static string RenderWinRTControl(AstElement ast, List<string> classes, List<string> styles)
        {
            var genericType = string.Empty;
            ast.Properties.TryGetValue("GenericType", out genericType);

            classes.Add("winrt-control-container");
            styles.Add("border: 1px solid rgba(147, 51, 234, 0.3); border-radius: 6px; padding: 12px 16px; background: rgba(147, 51, 234, 0.03); margin: 6px 0; width: 100%; display: flex; flex-direction: column;");

            var content = new StringBuilder();
            foreach (var child in ast.Children)
            {
                content.Append(RenderAstElement(child));
            }

            var label = string.IsNullOrEmpty(genericType) ? "WinRT Control" : $"WinRT Control &lt;{EscapeHtml(genericType)}&gt;";
            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";

            return $@"<div{classAttr}{styleAttr}{titleAttr}>
                <div class=""winrt-control-label"" style=""font-weight: 700; font-size: 10px; color: #a855f7; text-transform: uppercase; margin-bottom: 6px; letter-spacing: 0.5px;"">{label}</div>
                {content}
            </div>";
        }

        /// <summary>
        /// Renders a button element, applying any resources as inline styles and optional resource-based stylesheet blocks.
        /// </summary>
        /// <param name="ast">The AST element representing the button.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <param name="elementId">Unique id used when emitting resource styles.</param>
        /// <param name="resources">Resource dictionary parsed from the element.</param>
        /// <param name="hasResources">True when resources are present and resource styles should be emitted.</param>
        /// <returns>HTML for the button.</returns>
        private static string RenderButton(AstElement ast, List<string> classes, List<string> styles, string elementId, Dictionary<string, string> resources, bool hasResources)
        {
            var content = new StringBuilder();
            var localStyles = string.Empty;

            classes.Add("button");
            content.Append(EscapeHtml(ast.Content ?? "Button"));

            if (hasResources)
            {
                localStyles = GenerateResourcesStyles(elementId, resources, true);
            }

            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";
            var idAttr = hasResources ? $" id=\"{elementId}\"" : "";

            return $"{localStyles}<button{idAttr}{classAttr}{styleAttr}{titleAttr}>{content}</button>";
        }

        /// <summary>
        /// Renders layout stack elements (horizontal/vertical/wrap) and their children.
        /// </summary>
        /// <param name="ast">The AST element representing the stack.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <param name="lowerName">Lower-cased element name for layout heuristics.</param>
        /// <param name="elementId">Unique id used when emitting resource styles.</param>
        /// <param name="resources">Resource dictionary parsed from the element.</param>
        /// <param name="hasResources">True when resources are present and resource styles should be emitted.</param>
        /// <returns>HTML for the stack container and its children.</returns>
        private static string RenderStack(AstElement ast, List<string> classes, List<string> styles, string lowerName, string elementId, Dictionary<string, string> resources, bool hasResources)
        {
            var content = new StringBuilder();
            var localStyles = string.Empty;

            if (lowerName.Contains("wrap"))
            {
                classes.Add("wrap-grid");
                styles.Add("display: flex; flex-direction: row; flex-wrap: wrap;");
            }
            else
            {
                classes.Add("stack");

                var isHorizontal = lowerName.Contains("hstack") || ast.Properties.ContainsValue("Horizontal");

                if (isHorizontal)
                {
                    styles.Add("display: flex; flex-direction: row;");
                }
                else
                {
                    styles.Add("display: flex; flex-direction: column;");
                }
            }

            if (!ast.Properties.ContainsKey("Spacing"))
            {
                styles.Add("gap: 8px;");
            }

            foreach (var child in ast.Children)
            {
                content.Append(RenderAstElement(child));
            }

            if (hasResources)
            {
                localStyles = GenerateResourcesStyles(elementId, resources, false);
            }

            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";
            var idAttr = hasResources ? $" id=\"{elementId}\"" : "";

            return $"{localStyles}<div{idAttr}{classAttr}{styleAttr}{titleAttr}>{content}</div>";
        }

        /// <summary>
        /// Renders a border container with optional padding, corner radius and border styles.
        /// </summary>
        /// <param name="ast">The AST element representing the border.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <param name="elementId">Unique id used when emitting resource styles.</param>
        /// <param name="resources">Resource dictionary parsed from the element.</param>
        /// <param name="hasResources">True when resources are present and resource styles should be emitted.</param>
        /// <returns>HTML for the border container.</returns>
        private static string RenderBorder(AstElement ast, List<string> classes, List<string> styles, string elementId, Dictionary<string, string> resources, bool hasResources)
        {
            var content = new StringBuilder();
            var localStyles = string.Empty;

            classes.Add("border");

            if (!ast.Properties.ContainsKey("BorderThickness") && 
                !ast.Properties.ContainsKey("BorderBrush") && 
                !ast.Properties.ContainsKey("WithBorder"))
            {
                styles.Add("border: 1px solid rgba(255, 255, 255, 0.15);");
            }

            if (!ast.Properties.ContainsKey("CornerRadius"))
            {
                styles.Add("border-radius: 6px;");
            }

            if (!ast.Properties.ContainsKey("Padding"))
            {
                styles.Add("padding: 8px;");
            }

            foreach (var child in ast.Children)
            {
                content.Append(RenderAstElement(child));
            }

            if (hasResources)
            {
                localStyles = GenerateResourcesStyles(elementId, resources, false);
            }

            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";
            var idAttr = hasResources ? $" id=\"{elementId}\"" : "";

            return $"{localStyles}<div{idAttr}{classAttr}{styleAttr}{titleAttr}>{content}</div>";
        }

        /// <summary>
        /// Renders a grid container and its children into a CSS grid layout.
        /// </summary>
        /// <param name="ast">The AST element representing the grid.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <param name="elementId">Unique id used when emitting resource styles.</param>
        /// <param name="resources">Resource dictionary parsed from the element.</param>
        /// <param name="hasResources">True when resources are present and resource styles should be emitted.</param>
        /// <returns>HTML for the grid container.</returns>
        private static string RenderGrid(AstElement ast, List<string> classes, List<string> styles, string elementId, Dictionary<string, string> resources, bool hasResources)
        {
            var content = new StringBuilder();
            var localStyles = string.Empty;

            classes.Add("grid");
            styles.Add("display: grid; gap: 8px;");

            foreach (var child in ast.Children)
            {
                content.Append(RenderAstElement(child));
            }

            if (hasResources)
            {
                localStyles = GenerateResourcesStyles(elementId, resources, false);
            }

            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";
            var idAttr = hasResources ? $" id=\"{elementId}\"" : "";

            return $"{localStyles}<div{idAttr}{classAttr}{styleAttr}{titleAttr}>{content}</div>";
        }

        /// <summary>
        /// Renders a read-only text input element representing TextBox/Input in the preview.
        /// </summary>
        /// <param name="ast">The AST element representing the text input.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <returns>HTML for the input element.</returns>
        private static string RenderTextBox(AstElement ast, List<string> classes, List<string> styles)
        {
            classes.Add("textbox");
            styles.Add("background: #2a2a2a; border: 1px solid #444; color: #fff; padding: 4px 8px; border-radius: 4px;");

            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";

            return $"<input class=\"{string.Join(" ", classes)}\" style=\"{string.Join(" ", styles)}\" title=\"{ast.Name}\" value=\"{EscapeHtml(ast.Content ?? string.Empty)}\" readonly />";
        }

        /// <summary>
        /// Renders a checkbox or ToggleSwitch control based on the element name and properties.
        /// </summary>
        /// <param name="ast">The AST element representing the control.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <param name="lowerName">Lower-cased element name for decision logic.</param>
        /// <returns>HTML for the checkbox or toggle control.</returns>
        private static string RenderCheckboxOrToggle(AstElement ast, List<string> classes, List<string> styles, string lowerName)
        {
            var content = new StringBuilder();

            if (lowerName == "toggleswitch")
            {
                var isOnStr = string.Empty;

                ast.Properties.TryGetValue("IsOn", out isOnStr);

                var isOn = isOnStr.Equals("true", StringComparison.OrdinalIgnoreCase);
                var onText = string.Empty;

                ast.Properties.TryGetValue("onContent", out onText);

                var offText = string.Empty;

                ast.Properties.TryGetValue("offContent", out offText);

                var header = string.Empty;

                ast.Properties.TryGetValue("Header", out header);

                var stateLabel = isOn ? (onText ?? string.Empty) : (offText ?? string.Empty);

                if (string.IsNullOrEmpty(stateLabel))
                {
                    stateLabel = string.IsNullOrEmpty(onText) ? offText : onText;
                }

                if (string.IsNullOrEmpty(stateLabel))
                {
                    stateLabel = header;
                }

                if (string.IsNullOrEmpty(stateLabel))
                {
                    stateLabel = "ToggleSwitch";
                }

                content.Append($@"
                    <span class=""toggleswitch-track"">
                        <span class=""toggleswitch-thumb""></span>
                    </span>
                    <label>{EscapeHtml(stateLabel)}</label>
                ");

                classes.Add("toggleswitch-container");

                if (isOn)
                {
                    classes.Add("active");
                }
            }
            else
            {
                var label = ast.Content ?? string.Empty;

                content.Append($@"
                    <input type=""checkbox"" disabled>
                    <label>{EscapeHtml(label)}</label>
                ");

                classes.Add("checkbox-container");
            }

            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";

            return $"<div{classAttr}{styleAttr}{titleAttr}>{content}</div>";
        }

        /// <summary>
        /// Renders an unknown element type as a placeholder showing the element name and children.
        /// </summary>
        /// <param name="ast">The AST element.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <param name="lowerName">Lower-cased element name.</param>
        /// <returns>HTML placeholder for the unknown element and its children.</returns>
        private static string RenderUnknownElement(AstElement ast, List<string> classes, List<string> styles, string lowerName)
        {
            var content = new StringBuilder();

            classes.Add(lowerName);
            content.Append($"<div class=\"unknown-element\">{ast.Name}</div>");

            foreach (var child in ast.Children)
            {
                content.Append(RenderAstElement(child));
            }

            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";
            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
            var titleAttr = $" title=\"{ast.Name}\"";

            return $"<div{classAttr}{styleAttr}{titleAttr}>{content}</div>";
        }

        /// <summary>
        /// Renders a backdrop container and its children into a CSS-styled backdrop layer.
        /// </summary>
        /// <param name="ast">The AST element representing the backdrop.</param>
        /// <param name="classes">CSS classes collection.</param>
        /// <param name="styles">Inline styles collection.</param>
        /// <returns>HTML for the backdrop container.</returns>
        private static string RenderBackdrop(AstElement ast, List<string> classes, List<string> styles)
        {
            var content = new StringBuilder();

            classes.Add("backdrop-container");

            var backdropVal = string.Empty;

            if (ast.Properties.TryGetValue("Backdrop", out var val))
            {
                backdropVal = val;
            }
            else if (!string.IsNullOrEmpty(ast.Content))
            {
                backdropVal = ast.Content;
            }

            var lowerVal = (backdropVal ?? string.Empty).ToLowerInvariant();

            if (lowerVal.Contains("acrylic"))
            {
                classes.Add("backdrop-acrylic");
            }
            else
            {
                classes.Add("backdrop-mica");
            }

            styles.Add("width: 100%; min-height: 100px; display: flex; flex-direction: column; padding: 12px; border-radius: 8px;");

            foreach (var child in ast.Children)
            {
                content.Append(RenderAstElement(child));
            }

            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";

            var styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";

            var titleAttr = $" title=\"{ast.Name}\"";

            return $"<div{classAttr}{styleAttr}{titleAttr}>{content}</div>";
        }

        /// <summary>
        /// Generates a block of CSS styles derived from element resources. When resources are present a
        /// <style> block is emitted that targets the element by id and its interactive states.
        /// </summary>
        /// <param name="elementId">The id assigned to the element.</param>
        /// <param name="resources">Parsed resource dictionary mapping keys to CSS values.</param>
        /// <param name="isButton">True when the styles are for a button element (enables additional selectors).</param>
        /// <returns>A string containing a <style> block or an empty string.</returns>
        private static string GenerateResourcesStyles(string elementId, Dictionary<string, string> resources, bool isButton)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<style>");

            if (isButton)
            {
                sb.AppendLine($"  #{elementId} {{");

                if (resources.TryGetValue("ButtonBackground", out var bg))
                {
                    sb.AppendLine($"    background-color: {bg} !important;");
                }

                if (resources.TryGetValue("ButtonForeground", out var fg))
                {
                    sb.AppendLine($"    color: {fg} !important;");
                }

                if (resources.TryGetValue("ButtonBorderBrush", out var bc))
                {
                    sb.AppendLine($"    border-color: {bc} !important;");
                }

                if (resources.TryGetValue("ButtonBorderThemeThickness", out var bt))
                {
                    sb.AppendLine($"    border-width: {bt} !important; border-style: solid;");
                }

                if (resources.TryGetValue("ControlCornerRadius", out var cr))
                {
                    sb.AppendLine($"    border-radius: {cr} !important;");
                }

                sb.AppendLine("  }");

                if (resources.ContainsKey("ButtonBackgroundPointerOver") || resources.ContainsKey("ButtonForegroundPointerOver") || resources.ContainsKey("ButtonBorderBrushPointerOver"))
                {
                    sb.AppendLine($"  #{elementId}:hover {{");

                    if (resources.TryGetValue("ButtonBackgroundPointerOver", out var bgh))
                    {
                        sb.AppendLine($"    background-color: {bgh} !important;");
                    }

                    if (resources.TryGetValue("ButtonForegroundPointerOver", out var fgh))
                    {
                        sb.AppendLine($"    color: {fgh} !important;");
                    }

                    if (resources.TryGetValue("ButtonBorderBrushPointerOver", out var bch))
                    {
                        sb.AppendLine($"    border-color: {bch} !important;");
                    }

                    sb.AppendLine("  }");
                }

                if (resources.ContainsKey("ButtonBackgroundPressed") || resources.ContainsKey("ButtonForegroundPressed") || resources.ContainsKey("ButtonBorderBrushPressed"))
                {
                    sb.AppendLine($"  #{elementId}:active {{");

                    if (resources.TryGetValue("ButtonBackgroundPressed", out var bgp))
                    {
                        sb.AppendLine($"    background-color: {bgp} !important;");
                    }

                    if (resources.TryGetValue("ButtonForegroundPressed", out var fgp))
                    {
                        sb.AppendLine($"    color: {fgp} !important;");
                    }

                    if (resources.TryGetValue("ButtonBorderBrushPressed", out var bcp))
                    {
                        sb.AppendLine($"    border-color: {bcp} !important;");
                    }

                    sb.AppendLine("  }");
                }
            }

            sb.AppendLine($"  #{elementId} button, #{elementId} .button {{");

            if (resources.TryGetValue("ButtonBackground", out var cbg))
            {
                sb.AppendLine($"    background-color: {cbg} !important;");
            }

            if (resources.TryGetValue("ButtonForeground", out var cfg))
            {
                sb.AppendLine($"    color: {cfg} !important;");
            }

            if (resources.TryGetValue("ButtonBorderBrush", out var cbc))
            {
                sb.AppendLine($"    border-color: {cbc} !important;");
            }

            if (resources.TryGetValue("ButtonBorderThemeThickness", out var cbt))
            {
                sb.AppendLine($"    border-width: {cbt} !important; border-style: solid;");
            }

            if (resources.TryGetValue("ControlCornerRadius", out var ccr))
            {
                sb.AppendLine($"    border-radius: {ccr} !important;");
            }

            sb.AppendLine("  }");

            if (resources.ContainsKey("ButtonBackgroundPointerOver") || resources.ContainsKey("ButtonForegroundPointerOver") || resources.ContainsKey("ButtonBorderBrushPointerOver"))
            {
                sb.AppendLine($"  #{elementId} button:hover, #{elementId} .button:hover {{");

                if (resources.TryGetValue("ButtonBackgroundPointerOver", out var cbgh))
                {
                    sb.AppendLine($"    background-color: {cbgh} !important;");
                }

                if (resources.TryGetValue("ButtonForegroundPointerOver", out var cfgh))
                {
                    sb.AppendLine($"    color: {cfgh} !important;");
                }

                if (resources.TryGetValue("ButtonBorderBrushPointerOver", out var cbch))
                {
                    sb.AppendLine($"    border-color: {cbch} !important;");
                }

                sb.AppendLine("  }");
            }

            if (resources.ContainsKey("ButtonBackgroundPressed") || resources.ContainsKey("ButtonForegroundPressed") || resources.ContainsKey("ButtonBorderBrushPressed"))
            {
                sb.AppendLine($"  #{elementId} button:active, #{elementId} .button:active {{");

                if (resources.TryGetValue("ButtonBackgroundPressed", out var cbgp))
                {
                    sb.AppendLine($"    background-color: {cbgp} !important;");
                }

                if (resources.TryGetValue("ButtonForegroundPressed", out var cfgp))
                {
                    sb.AppendLine($"    color: {cfgp} !important;");
                }

                if (resources.TryGetValue("ButtonBorderBrushPressed", out var cbcp))
                {
                    sb.AppendLine($"    border-color: {cbcp} !important;");
                }

                sb.AppendLine("  }");
            }

            sb.AppendLine("</style>");

            return sb.ToString();
        }

        /// <summary>
        /// Decodes escaped Unicode sequences and common escape sequences in a string.
        /// </summary>
        /// <param name="input">Input string that may contain escape sequences.</param>
        /// <returns>Decoded string with escape sequences replaced by their character equivalents.</returns>
        private static string DecodeUnicodeSequences(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            var decoded = Unicode8Regex.Replace(input, match =>
            {
                var codeVal = match.Groups[1].Value;

                if (int.TryParse(codeVal, System.Globalization.NumberStyles.HexNumber, null, out var code))
                {
                    try
                    {
                        return char.ConvertFromUtf32(code);
                    }
                    catch
                    {
                        // Fallback.
                    }
                }

                return match.Value;
            });

            decoded = Unicode4Regex.Replace(decoded, match =>
            {
                var codeVal = match.Groups[1].Value;

                if (int.TryParse(codeVal, System.Globalization.NumberStyles.HexNumber, null, out var code))
                {
                    return ((char)code).ToString();
                }

                return match.Value;
            });

            decoded = EscapeSeqRegex.Replace(decoded, match =>
            {
                var val = match.Groups[1].Value;

                return val switch
                {
                    "\"" => "\"",
                    "\\" => "\\",
                    "n" => "\n",
                    "r" => "\r",
                    "t" => "\t",
                    "'" => "'",
                    _ => match.Value
                };
            });

            return decoded;
        }

        /// <summary>
        /// Escapes text for safe insertion into HTML and decodes escape sequences first.
        /// </summary>
        /// <param name="s">The raw string to escape. May be null.</param>
        /// <returns>HTML-escaped string safe for embedding in HTML.</returns>
        private static string EscapeHtml(string? s)
        {
            if (s == null || s.Length == 0)
            {
                return string.Empty;
            }

            var decoded = DecodeUnicodeSequences(s);

            return decoded.Replace("&", "&amp;")
                          .Replace("<", "&lt;")
                          .Replace(">", "&gt;")
                          .Replace("\"", "&quot;")
                          .Replace("'", "&#x27;");
        }

        /// <summary>
        /// Applies layout, size, and color properties from AST elements to the HTML style collection.
        /// </summary>
        /// <param name="prop">The property key-value pair.</param>
        /// <param name="styles">The style collection to append to.</param>
        /// <summary>
        /// Applies known layout, sizing and color properties from an AST property to the CSS styles list.
        /// </summary>
        /// <param name="prop">Key/value pair representing the property name and value.</param>
        /// <param name="styles">The styles list to append CSS declarations to.</param>
        private static void ApplyPropertyStyles(KeyValuePair<string, string> prop, List<string> styles)
        {
            if (prop.Key == "Margin")
            {
                styles.Add($"margin: {CleanThickness(prop.Value)};");
            }
            else if (prop.Key == "Padding")
            {
                styles.Add($"padding: {CleanThickness(prop.Value)};");
            }
            else if (prop.Key == "Width")
            {
                styles.Add($"width: {CleanSize(prop.Value)};");
            }
            else if (prop.Key == "Height")
            {
                styles.Add($"height: {CleanSize(prop.Value)};");
            }
            else if (prop.Key == "MinWidth")
            {
                styles.Add($"min-width: {CleanSize(prop.Value)};");
            }
            else if (prop.Key == "MinHeight")
            {
                styles.Add($"min-height: {CleanSize(prop.Value)};");
            }
            else if (prop.Key == "MaxWidth")
            {
                styles.Add($"max-width: {CleanSize(prop.Value)};");
            }
            else if (prop.Key == "MaxHeight")
            {
                styles.Add($"max-height: {CleanSize(prop.Value)};");
            }
            else if (prop.Key == "FontSize")
            {
                styles.Add($"font-size: {CleanSize(prop.Value)};");
            }
            else if (prop.Key == "Background")
            {
                var color = ParseColor(prop.Value);

                if (color != null)
                {
                    styles.Add($"background-color: {color};");
                }
            }
            else if (prop.Key == "Foreground")
            {
                var color = ParseColor(prop.Value);

                if (color != null)
                {
                    styles.Add($"color: {color};");
                }
            }
            else if (prop.Key == "BorderBrush" || prop.Key == "WithBorder")
            {
                var color = ParseColor(prop.Value);

                if (color != null)
                {
                    styles.Add($"border-color: {color}; border-style: solid; border-width: 1px;");
                }
            }
            else if (prop.Key == "BorderThickness")
            {
                styles.Add($"border-width: {CleanThickness(prop.Value)}; border-style: solid;");
            }
            else if (prop.Key == "CornerRadius")
            {
                styles.Add($"border-radius: {CleanThickness(prop.Value)};");
            }
            else if (prop.Key == "HorizontalAlignment")
            {
                ApplyHorizontalAlignment(prop.Value, styles);
            }
            else if (prop.Key == "VerticalAlignment")
            {
                ApplyVerticalAlignment(prop.Value, styles);
            }
            else if (prop.Key == "Spacing")
            {
                styles.Add($"gap: {CleanSize(prop.Value)};");
            }
            else if (prop.Key == "Bold")
            {
                styles.Add("font-weight: bold;");
            }
            else if (prop.Key == "SemiBold")
            {
                styles.Add("font-weight: 600;");
            }
            else if (prop.Key == "Italic")
            {
                styles.Add("font-style: italic;");
            }
            else if (prop.Key == "Underline")
            {
                styles.Add("text-decoration: underline;");
            }
        }

        /// <summary>
        /// Translates horizontal alignment settings to CSS styles.
        /// </summary>
        /// <param name="value">The alignment value string.</param>
        /// <param name="styles">The styles list.</param>
        /// <summary>
        /// Converts a horizontal alignment token to CSS flex alignment and text alignment declarations.
        /// </summary>
        /// <param name="value">Raw alignment value token(s).</param>
        /// <param name="styles">The styles list to append declarations to.</param>
        private static void ApplyHorizontalAlignment(string value, List<string> styles)
        {
            if (value.Contains("Left"))
            {
                styles.Add("align-self: flex-start; text-align: left;");
            }
            else if (value.Contains("Right"))
            {
                styles.Add("align-self: flex-end; text-align: right;");
            }
            else if (value.Contains("Center"))
            {
                styles.Add("align-self: center; text-align: center;");
            }
            else if (value.Contains("Stretch"))
            {
                styles.Add("align-self: stretch; width: 100%;");
            }
        }

        /// <summary>
        /// Translates vertical alignment settings to CSS styles.
        /// </summary>
        /// <param name="value">The alignment value string.</param>
        /// <param name="styles">The styles list.</param>
        /// <summary>
        /// Converts a vertical alignment token to CSS flex alignment declarations.
        /// </summary>
        /// <param name="value">Raw alignment value token(s).</param>
        /// <param name="styles">The styles list to append declarations to.</param>
        private static void ApplyVerticalAlignment(string value, List<string> styles)
        {
            if (value.Contains("Top"))
            {
                styles.Add("align-self: flex-start;");
            }
            else if (value.Contains("Bottom"))
            {
                styles.Add("align-self: flex-end;");
            }
            else if (value.Contains("Center"))
            {
                styles.Add("align-self: center;");
            }
            else if (value.Contains("Stretch"))
            {
                styles.Add("align-self: stretch; height: 100%;");
            }
        }

        /// <summary>
        /// Standardizes Thickness expressions (e.g. Thickness(12)) into CSS values.
        /// </summary>
        /// <param name="value">The raw Thickness value.</param>
        /// <returns>A formatted CSS margin/padding string.</returns>
        /// <summary>
        /// Normalizes a Thickness-like expression into CSS space-separated values with px units.
        /// </summary>
        /// <param name="value">Raw Thickness expression (e.g. "Thickness(4,8)").</param>
        /// <returns>CSS-ready margin/padding string.</returns>
        private static string CleanThickness(string value)
        {
            var cleaned = value.Replace("Thickness", "")
                               .Replace("new", "")
                               .Replace("(", "")
                               .Replace(")", "")
                               .Trim();

            var parts = cleaned.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var pxParts = parts.Select(p => p.EndsWith("px") ? p : p + "px");

            return string.Join(" ", pxParts);
        }

        /// <summary>
        /// Cleans a size representation, appending px if numeric.
        /// </summary>
        /// <param name="value">The raw size value.</param>
        /// <returns>A CSS-compliant size string.</returns>
        /// <summary>
        /// Normalizes a size value and appends "px" for numeric sizes.
        /// </summary>
        /// <param name="value">Raw size value (numeric or CSS token).</param>
        /// <returns>CSS-ready size string.</returns>
        private static string CleanSize(string value)
        {
            var trimmed = value.Trim();

            if (double.TryParse(trimmed, out _))
            {
                return trimmed + "px";
            }

            return trimmed;
        }

        /// <summary>
        /// Parses a brush or color expression (e.g. Colors.Red, FromArgb) into CSS color.
        /// </summary>
        /// <param name="value">The color expression string.</param>
        /// <returns>A CSS-compliant color string, or null.</returns>
        /// <summary>
        /// Attempts to parse a brush or color expression into a CSS color token.
        /// </summary>
        /// <param name="value">Raw color/brush expression.</param>
        /// <returns>CSS color string (hex, rgb(a), or CSS variable) or null if not recognized.</returns>
        private static string? ParseColor(string value)
        {
            var lower = value.ToLowerInvariant();

            if (lower.Contains("#"))
            {
                var index = lower.IndexOf('#');
                var hex = lower.Substring(index).TrimEnd(')', ';', '"', '\'');

                if (hex.Length >= 4)
                {
                    return hex;
                }
            }

            if (lower.Contains("fromargb"))
            {
                var start = lower.IndexOf('(');
                var end = lower.LastIndexOf(')');

                if (start >= 0 && end > start)
                {
                    var args = lower.Substring(start + 1, end - start - 1).Split(',');

                    if (args.Length == 4)
                    {
                        var a = double.Parse(args[0].Trim()) / 255.0;
                        var r = args[1].Trim();
                        var g = args[2].Trim();
                        var b = args[3].Trim();

                        return $"rgba({r}, {g}, {b}, {a:F2})";
                    }
                    else if (args.Length == 3)
                    {
                        var r = args[0].Trim();
                        var g = args[1].Trim();
                        var b = args[2].Trim();

                        return $"rgb({r}, {g}, {b})";
                    }
                }
            }

            var lastDot = value.LastIndexOf('.');
            var potentialName = value;

            if (lastDot >= 0 && lastDot < value.Length - 1)
            {
                potentialName = value.Substring(lastDot + 1).Trim().TrimEnd(')');
            }

            var nameLower = potentialName.ToLowerInvariant();

            // Map custom theme tokens to actual CSS color codes.
            var mappedColor = nameLower switch
            {
                "primarytext" or "primary" => "var(--primary-text)",
                "secondarytext" or "secondary" => "var(--secondary-text)",
                "tertiarytext" or "tertiary" => "var(--tertiary-text)",
                "disabledtext" or "disabled" => "var(--disabled-text)",
                "accentcolor" or "accenttext" or "accent" => "var(--accent-color)",
                "accentsecondary" => "var(--accent-secondary)",
                "accenttertiary" => "var(--accent-tertiary)",
                "accentdisabled" => "var(--accent-disabled)",
                "solidbackground" => "var(--solid-background)",
                "cardbackground" or "cardbg" => "var(--card-background)",
                "subtlefill" => "var(--subtle-fill)",
                "layerfill" => "var(--layer-fill)",
                "attention" or "systemattention" => "var(--attention-color)",
                "success" or "systemsuccess" => "var(--success-color)",
                "caution" or "systemcaution" => "var(--caution-color)",
                "critical" or "systemcritical" => "var(--critical-color)",
                "cardstroke" => "var(--card-stroke)",
                "surfacestroke" => "var(--surface-stroke)",
                "dividerstroke" => "var(--divider-stroke)",
                "controlstroke" => "var(--control-stroke)",
                "background" => "var(--solid-background)",
                "foreground" => "var(--primary-text)",
                "border" or "borderbrush" => "var(--control-stroke)",
                _ => null
            };

            if (mappedColor != null)
            {
                return mappedColor;
            }

            if (!potentialName.Contains(" ") && potentialName.Length > 0)
            {
                return potentialName;
            }

            var trimmed = value.Trim().TrimEnd(')');

            if (!trimmed.Contains(" ") && trimmed.Length > 0)
            {
                return trimmed;
            }

            return null;
        }

        /// <summary>
        /// Wraps the generated body content in a complete HTML document with styles and helper scripts.
        /// </summary>
        /// <param name="bodyContent">Inner HTML for the document body.</param>
        /// <returns>A complete HTML document string.</returns>
        private static string GetBaseHtmlTemplate(string bodyContent)
        {
            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <style>
    :root {{
      --primary-text: #ffffff;
      --secondary-text: #cccccc;
      --tertiary-text: #8a8a8a;
      --disabled-text: #5c5c5c;
      --accent-text: #60cdff;
      --accent-color: #60cdff;
      --accent-secondary: #4cc2f1;
      --accent-tertiary: #3a9ad9;
      --accent-disabled: #3a3a3a;
      --solid-background: #202020;
      --card-background: #2d2d2d;
      --subtle-fill: #272727;
      --layer-fill: #303030;
      --attention-color: #60cdff;
      --success-color: #6ccb5f;
      --caution-color: #fce100;
      --critical-color: #ff99a0;
      --card-stroke: rgba(255, 255, 255, 0.08);
      --surface-stroke: rgba(255, 255, 255, 0.1);
      --divider-stroke: rgba(255, 255, 255, 0.08);
      --control-stroke: rgba(255, 255, 255, 0.12);
    }}

    .theme-override-light {{
      --primary-text: #1c1c1c;
      --secondary-text: rgba(0, 0, 0, 0.6);
      --tertiary-text: rgba(0, 0, 0, 0.4);
      --disabled-text: rgba(0, 0, 0, 0.3);
      --accent-text: #0078d4;
      --accent-color: #0078d4;
      --accent-secondary: #106ebe;
      --accent-tertiary: #005a9e;
      --accent-disabled: rgba(0, 120, 212, 0.15);
      --solid-background: #f3f3f3;
      --card-background: rgba(0, 0, 0, 0.03);
      --subtle-fill: rgba(0, 0, 0, 0.04);
      --layer-fill: rgba(0, 0, 0, 0.02);
      --card-stroke: rgba(0, 0, 0, 0.08);
      --surface-stroke: rgba(0, 0, 0, 0.06);
      --divider-stroke: rgba(0, 0, 0, 0.08);
      --control-stroke: rgba(0, 0, 0, 0.1);
    }}

    .theme-override-dark {{
      --primary-text: #ffffff;
      --secondary-text: #cccccc;
      --tertiary-text: #8a8a8a;
      --disabled-text: #5c5c5c;
      --accent-text: #60cdff;
      --accent-color: #60cdff;
      --accent-secondary: #4cc2f1;
      --accent-tertiary: #3a9ad9;
      --accent-disabled: #3a3a3a;
      --solid-background: #202020;
      --card-background: #2d2d2d;
      --subtle-fill: #272727;
      --layer-fill: #303030;
      --card-stroke: rgba(255, 255, 255, 0.08);
      --surface-stroke: rgba(255, 255, 255, 0.1);
      --divider-stroke: rgba(255, 255, 255, 0.08);
      --control-stroke: rgba(255, 255, 255, 0.12);
    }}

    * {{ margin: 0; padding: 0; box-sizing: border-box; }}
    body {{
      background: #1f1f1f;
      color: var(--primary-text);
      font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, Helvetica, Arial, sans-serif;
      padding: 24px;
      min-height: 100vh;
      overflow-x: hidden;
      overflow-y: auto;
    }}

    /* Backdrop Effects */
    .backdrop-mica {{
      background: radial-gradient(circle at top, #2b2b35 0%, #1f1f24 100%) !important;
    }}
    .theme-override-light .backdrop-mica,
    .theme-override-light.backdrop-mica {{
      background: radial-gradient(circle at top, #f3f3f7 0%, #e8e8ec 100%) !important;
    }}

    .backdrop-acrylic {{
      background: rgba(32, 32, 32, 0.65) !important;
      backdrop-filter: blur(20px) saturate(125%) !important;
    }}
    .theme-override-light .backdrop-acrylic,
    .theme-override-light.backdrop-acrylic {{
      background: rgba(243, 243, 243, 0.65) !important;
      backdrop-filter: blur(20px) saturate(125%) !important;
    }}

    .header {{
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 20px;
      padding-bottom: 12px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.08);
    }}
    .badge {{
      font-size: 10px;
      text-transform: uppercase;
      font-weight: 700;
      padding: 3px 8px;
      border-radius: 12px;
      letter-spacing: 0.5px;
    }}
    .badge.success {{
      background: rgba(34, 197, 94, 0.15);
      color: #4ade80;
      border: 1px solid rgba(34, 197, 94, 0.3);
    }}
    .badge.warning {{
      background: rgba(245, 158, 11, 0.15);
      color: #fbbf24;
      border: 1px solid rgba(245, 158, 11, 0.3);
    }}
    .title {{
      font-size: 14px;
      font-weight: 600;
      color: var(--primary-text);
    }}
    .console {{
      background: rgba(239, 68, 68, 0.08);
      border: 1px solid rgba(239, 68, 68, 0.25);
      border-radius: 8px;
      padding: 12px;
      margin-bottom: 20px;
      backdrop-filter: blur(10px);
    }}
    .console-title {{
      font-size: 12px;
      font-weight: 700;
      color: #ef4444;
      margin-bottom: 8px;
      text-transform: uppercase;
    }}
    .console-errors {{
      list-style-type: none;
      font-family: 'Consolas', 'Courier New', monospace;
      font-size: 11px;
      color: #fca5a5;
    }}
    .console-errors li {{
      margin-bottom: 4px;
      word-break: break-all;
    }}
    .preview-area {{
      display: flex;
      flex-direction: column;
      align-items: stretch;
      justify-content: flex-start;
      padding: 24px;
      background: #1f1f1f;
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 8px;
      min-height: 200px;
      box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4);
    }}
    .empty-state {{
      opacity: 0.5;
      font-size: 13px;
      text-align: center;
      width: 100%;
      margin: auto 0;
    }}
    /* Elements standard rendering */
    .control {{
      display: inline-block;
    }}
    .textblock {{
      color: var(--primary-text);
      font-size: 14px;
    }}
    .button {{
      background: #333333;
      border: 1px solid #444444;
      color: var(--primary-text);
      padding: 6px 16px;
      font-size: 13px;
      border-radius: 4px;
      cursor: pointer;
      font-weight: 500;
      transition: all 0.15s ease;
      display: inline-flex;
      align-items: center;
      justify-content: center;
    }}
    .button:hover {{
      background: #3c3c3c;
      border-color: #555555;
    }}
    .button:active {{
      background: #2b2b2b;
      color: rgba(255, 255, 255, 0.7);
    }}
    .stack {{
      width: 100%;
    }}
    .border {{
      padding: 12px;
      width: 100%;
    }}
    .unknown-element {{
      font-size: 10px;
      color: rgba(255,255,255,0.4);
      border: 1px dashed rgba(255,255,255,0.1);
      padding: 2px 4px;
      border-radius: 3px;
      margin-bottom: 4px;
    }}
    /* Checkbox */
    .checkbox-container {{
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      color: var(--primary-text);
      cursor: pointer;
      margin: 4px 0;
    }}
    .checkbox-container input[type=""checkbox""] {{
      appearance: none;
      width: 18px;
      height: 18px;
      border: 1px solid var(--control-stroke);
      border-radius: 4px;
      background: rgba(255, 255, 255, 0.05);
      position: relative;
      cursor: pointer;
      transition: all 0.15s ease;
      display: inline-flex;
      align-items: center;
      justify-content: center;
    }}
    .checkbox-container input[type=""checkbox""]:checked {{
      background: var(--accent-color);
      border-color: var(--accent-color);
    }}
    .checkbox-container input[type=""checkbox""]:checked::after {{
      content: ""✓"";
      color: #fff;
      font-size: 12px;
      font-weight: bold;
    }}
    
    /* ToggleSwitch styling */
    .toggleswitch-container {{
      display: inline-flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      color: var(--primary-text);
      cursor: pointer;
      margin: 4px 0;
      user-select: none;
    }}
    .toggleswitch-track {{
      width: 32px;
      height: 16px;
      background: #555;
      border: 1px solid var(--control-stroke);
      border-radius: 8px;
      position: relative;
      transition: background 0.15s;
      display: inline-block;
    }}
    .toggleswitch-thumb {{
      width: 10px;
      height: 10px;
      background: #fff;
      border-radius: 50%;
      position: absolute;
      top: 2px;
      left: 2px;
      transition: left 0.15s;
    }}
    .toggleswitch-container.active .toggleswitch-track {{
      background: var(--accent-color);
      border-color: var(--accent-color);
    }}
    .toggleswitch-container.active .toggleswitch-thumb {{
      left: 18px;
    }}

    /* Interactive TabView */
    .tabview-container {{
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 8px;
      background: rgba(30, 30, 45, 0.4);
      backdrop-filter: blur(10px);
      width: 100%;
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }}
    .tabview-header-strip {{
      display: flex;
      background: rgba(0, 0, 0, 0.15);
      border-bottom: 1px solid rgba(255, 255, 255, 0.08);
      padding: 4px 4px 0 4px;
      gap: 2px;
    }}
    .tabview-header-btn {{
      padding: 6px 16px;
      font-size: 12px;
      font-weight: normal;
      border: 1px solid transparent;
      border-bottom: none;
      background: transparent;
      cursor: pointer;
      color: #a6a6a6;
      border-top-left-radius: 4px;
      border-top-right-radius: 4px;
      transition: all 0.1s ease;
      margin-bottom: -1px;
    }}
    .tabview-header-btn:hover {{
      color: #ffffff;
      background: rgba(255, 255, 255, 0.03);
    }}
    .tabview-header-btn.active {{
      background: #2b2b2b !important;
      color: #ffffff !important;
      border: 1px solid rgba(255, 255, 255, 0.08) !important;
      border-bottom: 1px solid #2b2b2b !important;
      font-weight: 600;
    }}
    .tabview-content-panel {{
      width: 100%;
    }}
    .tab-item {{
      width: 100%;
      background: transparent;
      padding: 0;
      border: none;
    }}
    
    /* ScrollView and custom scrollbar */
    .scrollview-container {{
      width: 100%;
      overflow-y: auto;
      max-height: 600px;
      padding-right: 4px;
    }}
    .scrollview-container::-webkit-scrollbar {{
      width: 6px;
    }}
    .scrollview-container::-webkit-scrollbar-track {{
      background: transparent;
    }}
    .scrollview-container::-webkit-scrollbar-thumb {{
      background: rgba(255, 255, 255, 0.12);
      border-radius: 3px;
    }}
    .scrollview-container::-webkit-scrollbar-thumb:hover {{
      background: rgba(255, 255, 255, 0.24);
    }}
    
    /* WrapGrid and Stacks */
    .wrap-grid {{
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      width: 100%;
    }}
    
    /* Custom Components container */
    .component-container {{
      border: 1px solid rgba(0, 120, 212, 0.25);
      border-radius: 6px;
      padding: 16px;
      background: rgba(0, 120, 212, 0.03);
      width: 100%;
      margin: 8px 0;
    }}
    .component-label {{
      font-size: 12px;
      font-weight: 700;
      color: var(--accent-color);
      text-transform: uppercase;
      letter-spacing: 0.5px;
      margin-bottom: 12px;
    }}
  </style>
</head>
<body>
  {bodyContent}
  
  <script>
    function switchTab(btn, tabId, groupClass) {{
      var panels = document.getElementsByClassName(groupClass);
      for (var i = 0; i < panels.length; i++) {{
        panels[i].style.display = 'none';
      }}
      
      document.getElementById(tabId).style.display = 'block';
      
      var buttons = btn.parentNode.children;
      for (var i = 0; i < buttons.length; i++) {{
        buttons[i].classList.remove('active');
      }}
      
      btn.classList.add('active');
    }}
  </script>
</body>
</html>";
        }
    }
}
