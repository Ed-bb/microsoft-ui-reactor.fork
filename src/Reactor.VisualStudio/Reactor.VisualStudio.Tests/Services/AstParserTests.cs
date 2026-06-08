namespace Reactor.VisualStudio.Services
{
    using NUnit.Framework;
    using Shouldly;

    /// <summary>
    /// Unit tests for the <see cref="AstParser"/> and <see cref="HtmlRenderer"/> classes.
    /// </summary>
    [TestFixture]
    public class AstParserTests
    {
        /// <summary>
        /// Verifies that SectionHeader maps the title argument correctly.
        /// </summary>
        [Test]
        public void ParseAst_SectionHeader_ShouldMapTitle()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() => SectionHeader(""Theme Tokens"", ""All colors resolve from WinUI's resource system."");
                    }
                }";
            var result = AstParser.ParseAst(code, "MyWidget");

            result.ShouldNotBeNull();
            result.Name.ShouldBe("SectionHeader");
            result.Content.ShouldBe("Theme Tokens");
        }

        /// <summary>
        /// Verifies that CheckBox maps the label argument at index 2 correctly.
        /// </summary>
        [Test]
        public void ParseAst_CheckBox_ShouldMapLabel()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() => CheckBox(true, null, ""Accept terms"");
                    }
                }";
            var result = AstParser.ParseAst(code, "MyWidget");

            result.ShouldNotBeNull();
            result.Name.ShouldBe("CheckBox");
            result.Content.ShouldBe("Accept terms");
        }

        /// <summary>
        /// Verifies that CheckBox named label parameter is mapped correctly.
        /// </summary>
        [Test]
        public void ParseAst_CheckBoxNamed_ShouldMapLabel()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() => CheckBox(true, label: ""Accept terms"");
                    }
                }";
            var result = AstParser.ParseAst(code, "MyWidget");

            result.ShouldNotBeNull();
            result.Name.ShouldBe("CheckBox");
            result.Content.ShouldBe("Accept terms");
        }

        /// <summary>
        /// Verifies that HtmlRenderer.GeneratePreviewHtml includes type name tooltips (title attribute).
        /// </summary>
        [Test]
        public void GeneratePreviewHtml_ShouldIncludeTitleTooltips()
        {
            var element = new AstElement
            {
                Name    = "TextBlock",
                Content = "Hello Tooltip"
            };

            var html = HtmlRenderer.GeneratePreviewHtml(element, new System.Collections.Generic.List<string>(), "MyWidget");

            html.ShouldContain("title=\"TextBlock\"");
            html.ShouldContain("Hello Tooltip");
        }

        /// <summary>
        /// Verifies that SubHeading is parsed and rendered with its content as text block.
        /// </summary>
        [Test]
        public void ParseAndRender_SubHeading_ShouldShowContent()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() => SubHeading(""Heading Text"");
                    }
                }";

            var ast = AstParser.ParseAst(code, "MyWidget");

            ast.ShouldNotBeNull();

            ast.Name.ShouldBe("SubHeading");

            ast.Content.ShouldBe("Heading Text");

            var html = HtmlRenderer.GeneratePreviewHtml(ast, new System.Collections.Generic.List<string>(), "MyWidget");

            html.ShouldContain("Heading Text");

            html.ShouldContain("font-size: 16px;");
        }

        /// <summary>
        /// Verifies that SectionHeader is parsed and rendered with title and description.
        /// </summary>
        [Test]
        public void ParseAndRender_SectionHeader_ShouldShowTitleAndDescription()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() => SectionHeader(""Title Text"", ""Description Text"");
                    }
                }";

            var ast = AstParser.ParseAst(code, "MyWidget");

            ast.ShouldNotBeNull();

            ast.Name.ShouldBe("SectionHeader");

            ast.Content.ShouldBe("Title Text");

            ast.Properties.ContainsKey("Description").ShouldBeTrue();

            ast.Properties["Description"].ShouldBe("Description Text");

            var html = HtmlRenderer.GeneratePreviewHtml(ast, new System.Collections.Generic.List<string>(), "MyWidget");

            html.ShouldContain("Title Text");

            html.ShouldContain("Description Text");

            html.ShouldContain("section-title");

            html.ShouldContain("section-description");
        }

        /// <summary>
        /// Verifies that layout, color, size, and styling properties are correctly parsed and applied to HTML output.
        /// </summary>
        [Test]
        public void Render_WithCustomModifiers_ShouldApplyStyles()
        {
            var element = new AstElement
            {
                Name = "Border"
            };

            element.Properties["Padding"]             = "Thickness(12, 24)";
            element.Properties["Background"]          = "Colors.Red";
            element.Properties["Foreground"]          = "Microsoft.UI.Colors.White";
            element.Properties["Width"]               = "200";
            element.Properties["Height"]              = "100";
            element.Properties["BorderThickness"]     = "2";
            element.Properties["CornerRadius"]        = "8";
            element.Properties["HorizontalAlignment"] = "HorizontalAlignment.Center";
            element.Properties["Bold"]                = "";
            element.Properties["Italic"]              = "";

            var html = HtmlRenderer.GeneratePreviewHtml(element, new System.Collections.Generic.List<string>(), "MyWidget");

            html.ShouldContain("padding: 12px 24px;");

            html.ShouldContain("background-color: Red;");

            html.ShouldContain("color: White;");

            html.ShouldContain("width: 200px;");

            html.ShouldContain("height: 100px;");

            html.ShouldContain("border-width: 2px;");

            html.ShouldContain("border-radius: 8px;");

            html.ShouldContain("align-self: center;");

            html.ShouldContain("font-weight: bold;");

            html.ShouldContain("font-style: italic;");
        }

        /// <summary>
        /// Verifies that string concatenations (e.g. "text 1 " + "text 2") are parsed as a single concatenated string.
        /// </summary>
        [Test]
        public void ParseAst_StringConcatenation_ShouldConcatenateStrings()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() => TextBlock(""Hello "" + ""World"" + ""!"");
                    }
                }";

            var result = AstParser.ParseAst(code, "MyWidget");

            result.ShouldNotBeNull();

            result.Name.ShouldBe("TextBlock");

            result.Content.ShouldBe("Hello World!");
        }

        /// <summary>
        /// Verifies that elements inside with expressions are parsed correctly, including their initializers.
        /// </summary>
        [Test]
        public void ParseAst_WithExpressions_ShouldParseCorrectly()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() =>
                            Grid(
                                columns: [GridSize.Star()], rows: [GridSize.Auto, GridSize.Star()],
                                (TitleBar(""Styling Gallery"") with
                                {
                                    Subtitle = ""Theme tokens · RequestedTheme · Lightweight styling"",
                                }).Grid(row: 0),
                                TabView(
                                    Tab(""Theme Tokens"", ScrollView(Component<ThemeTokensDemo>())) with { IsClosable = false }
                                ).Grid(row: 1)
                            ).Backdrop(BackdropKind.Mica);
                    }
                }";

            var result = AstParser.ParseAst(code, "MyWidget");

            result.ShouldNotBeNull();

            result.Name.ShouldBe("Grid");

            result.Properties.ContainsKey("Backdrop").ShouldBeTrue();

            result.Properties["Backdrop"].ShouldBe("BackdropKind.Mica");

            result.Children.Count.ShouldBe(2);

            var titleBar = result.Children[0];

            titleBar.Name.ShouldBe("TitleBar");

            titleBar.Content.ShouldBe("Styling Gallery");

            titleBar.Properties.ContainsKey("Subtitle").ShouldBeTrue();

            titleBar.Properties["Subtitle"].ShouldBe("Theme tokens · RequestedTheme · Lightweight styling");

            titleBar.Properties.ContainsKey("Grid").ShouldBeTrue();

            titleBar.Properties["Grid"].ShouldBe("0");

            var tabView = result.Children[1];

            tabView.Name.ShouldBe("TabView");

            tabView.Properties.ContainsKey("Grid").ShouldBeTrue();

            tabView.Properties["Grid"].ShouldBe("1");

            tabView.Children.Count.ShouldBe(1);

            var tab = tabView.Children[0];

            tab.Name.ShouldBe("Tab");

            tab.Content.ShouldBe("Theme Tokens");

            tab.Properties.ContainsKey("IsClosable").ShouldBeTrue();

            tab.Properties["IsClosable"].ShouldBe("false");

            tab.Children.Count.ShouldBe(1);

            var scrollView = tab.Children[0];

            scrollView.Name.ShouldBe("ScrollView");

            scrollView.Children.Count.ShouldBe(1);

            var component = scrollView.Children[0];

            component.Name.ShouldBe("Component");
        }

        /// <summary>
        /// Verifies that custom containers (TitleBar, TabView, Tab, ScrollView, Component) render with the correct HTML/CSS tags, styles, and labels.
        /// </summary>
        [Test]
        public void Render_CustomContainers_ShouldRenderStyledHtml()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() =>
                            Grid(
                                (TitleBar(""Styling Gallery"") with
                                {
                                    Subtitle = ""Theme tokens · RequestedTheme · Lightweight styling"",
                                }),
                                TabView(
                                    Tab(""Theme Tokens"", ScrollView(Component<ThemeTokensDemo>()))
                                )
                            );
                    }
                }";

            var ast = AstParser.ParseAst(code, "MyWidget");

            ast.ShouldNotBeNull();

            var html = HtmlRenderer.GeneratePreviewHtml(ast, new System.Collections.Generic.List<string>(), "MyWidget");

            html.ShouldContain("title-bar");

            html.ShouldContain("Styling Gallery");

            html.ShouldContain("Theme tokens · RequestedTheme · Lightweight styling");

            html.ShouldContain("tabview-container");

            html.ShouldContain("tabview-header-strip");

            html.ShouldContain("tab-item");

            html.ShouldContain("Theme Tokens");

            html.ShouldContain("scrollview-container");

            html.ShouldContain("component-container");

            html.ShouldContain("Component &lt;ThemeTokensDemo&gt;");
        }

        /// <summary>
        /// Verifies that theme color tokens like Theme.PrimaryText are correctly mapped to hex or rgba values in generated CSS styles.
        /// </summary>
        [Test]
        public void Render_ThemeColorTokens_ShouldMapToHexOrRgba()
        {
            var element = new AstElement
            {
                Name = "TextBlock"
            };

            element.Properties["Foreground"] = "Theme.PrimaryText";

            var html = HtmlRenderer.GeneratePreviewHtml(element, new System.Collections.Generic.List<string>(), "MyWidget");

            html.ShouldContain("color: var(--primary-text);");
        }

        /// <summary>
        /// Verifies that ToggleSwitch elements parse their isOn state and named onContent/offContent parameters, and render as checkboxes.
        /// </summary>
        [Test]
        public void ParseAndRender_ToggleSwitch_ShouldRenderCheckboxWithCorrectStateAndLabel()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() =>
                            ToggleSwitch(true, null, onContent: ""ActiveText"", offContent: ""InactiveText"");
                    }
                }";

            var ast = AstParser.ParseAst(code, "MyWidget");

            ast.ShouldNotBeNull();

            ast.Name.ShouldBe("ToggleSwitch");

            ast.Properties.ContainsKey("IsOn").ShouldBeTrue();

            ast.Properties["IsOn"].ShouldBe("true");

            ast.Properties.ContainsKey("onContent").ShouldBeTrue();

            ast.Properties["onContent"].ShouldBe("ActiveText");

            var html = HtmlRenderer.GeneratePreviewHtml(ast, new System.Collections.Generic.List<string>(), "MyWidget");

            html.ShouldContain("toggleswitch-track");
            html.ShouldContain("toggleswitch-thumb");
            html.ShouldContain("ActiveText");
        }

        /// <summary>
        /// Verifies that a button with .Resources(...) parses its resources and outputs a customized local stylesheet block.
        /// </summary>
        [Test]
        public void ParseAndRender_ButtonWithResources_ShouldRenderCustomStyles()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() =>
                            Button(""Branded Action"", () => { })
                                .Resources(r => r
                                    .Set(""ButtonBackground"", ""#0078D4"")
                                    .Set(""ButtonForeground"", ""#FFFFFF"")
                                );
                    }
                }";

            var ast = AstParser.ParseAst(code, "MyWidget");

            ast.ShouldNotBeNull();

            ast.Name.ShouldBe("Button");

            ast.Properties.ContainsKey("Resources").ShouldBeTrue();

            var html = HtmlRenderer.GeneratePreviewHtml(ast, new System.Collections.Generic.List<string>(), "MyWidget");

            html.ShouldContain("<style>");

            html.ShouldContain("background-color: #0078D4");

            html.ShouldContain("color: #FFFFFF");
        }

        /// <summary>
        /// Verifies that elements like WrapGrid containing expressions that cannot be evaluated at design time render a placeholder box.
        /// </summary>
        [Test]
        public void ParseAndRender_WrapGridWithExpression_ShouldRenderPlaceholder()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() =>
                            WrapGrid(
                                Enumerable.Range(0, 5).Select(i => TextBlock(i.ToString())).ToArray()
                            );
                    }
                }";

            var ast = AstParser.ParseAst(code, "MyWidget");

            ast.ShouldNotBeNull();

            ast.Name.ShouldBe("WrapGrid");

            ast.Children.Count.ShouldBe(1);

            ast.Children[0].Name.ShouldBe("CodeExpressionPlaceholder");

            ast.Children[0].Content.ShouldBe("Enumerable.Range(0, 5).Select(i => TextBlock(i.ToString())).ToArray()");

            var html = HtmlRenderer.GeneratePreviewHtml(ast, new System.Collections.Generic.List<string>(), "MyWidget");

            html.ShouldContain("code-expression-placeholder");

            html.ShouldContain("Code Execution Required");

            html.ShouldContain("Enumerable.Range(0, 5).Select(i =&gt; TextBlock(i.ToString())).ToArray()");
        }

        /// <summary>
        /// Verifies that strings containing unicode escape sequences (like \U0001F319 or \u2600) are decoded into unicode characters.
        /// </summary>
        [Test]
        public void ParseAndRender_UnicodeEscapeSequences_ShouldDecodeInHtml()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() =>
                            TextBlock(isDark ? ""\U0001F319"" : ""\u2600"");
                    }
                }";

            var ast = AstParser.ParseAst(code, "MyWidget");

            ast.ShouldNotBeNull();

            ast.Name.ShouldBe("TextBlock");

            var html = HtmlRenderer.GeneratePreviewHtml(ast, new System.Collections.Generic.List<string>(), "MyWidget");

            // \U0001F319 converts to crescent moon 🌙 (Unicode: \uD83C\uDF19)
            html.ShouldContain("\uD83C\uDF19");

            // \u2600 converts to black sun with rays ☀
            html.ShouldContain("\u2600");
        }

        /// <summary>
        /// Verifies that helper class methods returning elements are expanded inline with parameter substitution.
        /// </summary>
        [Test]
        public void ParseAst_HelperMethodInvocation_ShouldExpandInlineWithParameters()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() =>
                            HStack(8,
                                ColorSwatch(""Accent"", Theme.Accent),
                                ThemeBox(""Dark"", ElementTheme.Dark)
                            );

                        static Element ColorSwatch(string label, ThemeRef color) =>
                            VStack(4,
                                Border(Empty()).Background(color),
                                TextBlock(label)
                            );

                        static Element ThemeBox(string label, ElementTheme theme) =>
                            Border(
                                TextBlock(label)
                            ).RequestedTheme(theme);
                    }
                }";

            var ast = AstParser.ParseAst(code, "MyWidget");

            ast.ShouldNotBeNull();

            ast.Name.ShouldBe("HStack");

            ast.Children.Count.ShouldBe(2);

            var colorSwatch = ast.Children[0];

            colorSwatch.Name.ShouldBe("VStack");

            colorSwatch.Children.Count.ShouldBe(2);

            var border = colorSwatch.Children[0];

            border.Name.ShouldBe("Border");

            border.Properties.ContainsKey("Background").ShouldBeTrue();

            border.Properties["Background"].ShouldBe("Theme.Accent");

            var textBlock1 = colorSwatch.Children[1];

            textBlock1.Name.ShouldBe("TextBlock");

            textBlock1.Content.ShouldBe("Accent");

            var themeBox = ast.Children[1];

            themeBox.Name.ShouldBe("Border");

            themeBox.Properties.ContainsKey("RequestedTheme").ShouldBeTrue();

            themeBox.Properties["RequestedTheme"].ShouldBe("ElementTheme.Dark");

            themeBox.Children.Count.ShouldBe(1);

            var textBlock2 = themeBox.Children[0];

            textBlock2.Name.ShouldBe("TextBlock");

            textBlock2.Content.ShouldBe("Dark");
        }

        /// <summary>
        /// Verifies that elements with Backdrop modifiers and standalone Backdrop controls are parsed and styled.
        /// </summary>
        [Test]
        public void ParseAndRender_Backdrop_ShouldApplyBackdropStyling()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() =>
                            Grid(
                                Backdrop(BackdropKind.Acrylic)
                            ).Backdrop(BackdropKind.Mica);
                    }
                }";

            var ast = AstParser.ParseAst(code, "MyWidget");

            ast.ShouldNotBeNull();

            ast.Name.ShouldBe("Grid");

            ast.Properties.ContainsKey("Backdrop").ShouldBeTrue();

            ast.Properties["Backdrop"].ShouldBe("BackdropKind.Mica");

            ast.Children.Count.ShouldBe(1);

            var child = ast.Children[0];

            child.Name.ShouldBe("Backdrop");

            child.Content.ShouldBe("BackdropKind.Acrylic");

            var html = HtmlRenderer.GeneratePreviewHtml(ast, new System.Collections.Generic.List<string>(), "MyWidget");

            // Verify grid has the mica backdrop class
            html.ShouldContain("backdrop-mica");

            // Verify child backdrop container has acrylic backdrop class
            html.ShouldContain("backdrop-container backdrop-acrylic");
        }
    }
}
