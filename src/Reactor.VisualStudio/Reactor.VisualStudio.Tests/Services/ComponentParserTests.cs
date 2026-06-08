namespace Reactor.VisualStudio.Services
{
    using NUnit.Framework;
    using Shouldly;

    /// <summary>
    /// Unit tests for the Roslyn-based <see cref="ComponentParser"/> class.
    /// </summary>
    [TestFixture]
    public class ComponentParserTests
    {
        /// <summary>
        /// Verifies that the parser successfully extracts standard Component class names.
        /// </summary>
        [Test]
        public void ParseComponents_ShouldFindStandardComponent()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyWidget : Component
                    {
                        public override VisualNode Render() => TextBlock(""Hello"");
                    }
                }";

            var result = ComponentParser.ParseComponents(code);

            result.ShouldContain("MyWidget");
            result.Count.ShouldBe(1);
        }

        /// <summary>
        /// Verifies that the parser successfully extracts generic Component class names.
        /// </summary>
        [Test]
        public void ParseComponents_ShouldFindGenericComponent()
        {
            var code = @"
                using Microsoft.UI.Reactor;
                namespace MyApp
                {
                    public class MyStatefulWidget : Component<MyState>
                    {
                        public override VisualNode Render() => TextBlock(""Hello"");
                    }
                }";

            var result = ComponentParser.ParseComponents(code);

            result.ShouldContain("MyStatefulWidget");
            result.Count.ShouldBe(1);
        }

        /// <summary>
        /// Verifies that the parser ignores classes that do not derive from Component.
        /// </summary>
        [Test]
        public void ParseComponents_ShouldIgnoreNonComponents()
        {
            var code = @"
                namespace MyApp
                {
                    public class PlainClass
                    {
                        public void DoWork() {}
                    }
                }";

            var result = ComponentParser.ParseComponents(code);

            result.ShouldBeEmpty();
        }
    }
}
