namespace Reactor.VisualStudio.ToolWindows
{
    using System;
    using Microsoft.VisualStudio.Text;
    using Microsoft.VisualStudio.Text.Editor;
    using Microsoft.VisualStudio.Text.Editor.DragDrop;
    using Moq;
    using NUnit.Framework;
    using Shouldly;

    /// <summary>
    /// Unit tests for the <see cref="ReactorComponentDropHandler"/> class.
    /// </summary>
    [TestFixture]
    public class ReactorComponentDropHandlerTests
    {
        /// <summary>
        /// Verifies that HandleDragStarted returns Copy.
        /// </summary>
        [Test]
        public void HandleDragStarted_ShouldReturnCopy()
        {
            var mockTextView = new Mock<IWpfTextView>();
            var handler      = new ReactorComponentDropHandler(mockTextView.Object);
            var dragDropInfo = new DragDropInfo(
                new System.Windows.Point(0, 0),
                System.Windows.DragDropKeyStates.None,
                null!,
                false,
                new object(),
                System.Windows.DragDropEffects.Copy,
                default
            );

            var result = handler.HandleDragStarted(dragDropInfo);

            result.ShouldBe(DragDropPointerEffects.Copy);
        }

        /// <summary>
        /// Verifies that HandleDataDropped returns None when data is not present.
        /// </summary>
        [Test]
        public void HandleDataDropped_WhenNoDataPresent_ShouldReturnNone()
        {
            var mockTextView = new Mock<IWpfTextView>();
            var handler      = new ReactorComponentDropHandler(mockTextView.Object);
            var mockData     = new Mock<System.Windows.IDataObject>();

            mockData.Setup(d => d.GetDataPresent(ComponentsToolWindowControl.DragDropFormat))
                    .Returns(false);

            var dragDropInfo = new DragDropInfo(
                new System.Windows.Point(0, 0),
                System.Windows.DragDropKeyStates.None,
                mockData.Object,
                false,
                new object(),
                System.Windows.DragDropEffects.Copy,
                default
            );

            var result = handler.HandleDataDropped(dragDropInfo);

            result.ShouldBe(DragDropPointerEffects.None);
        }

        /// <summary>
        /// Verifies that InsertCodeAtOffset inserts simple snippet when no element is under cursor.
        /// </summary>
        [Test]
        public void InsertCodeAtOffset_NoElementUnderCursor_ShouldInsertSimpleSnippet()
        {
            var mockBuffer   = new Mock<ITextBuffer>();
            var mockSnapshot = new Mock<ITextSnapshot>();

            mockSnapshot.Setup(s => s.GetText()).Returns("class MyComponent : Component { public override VisualNode Render() { return ; } }");
            mockBuffer.Setup(b => b.CurrentSnapshot).Returns(mockSnapshot.Object);

            var item = new ComponentItem
            {
                Name          = "TextBlock",
                FactoryParams = new[] { "content: \"Hello\"" }
            };

            ComponentsToolWindowControl.InsertCodeAtOffset(mockBuffer.Object, item, 76);

            mockBuffer.Verify(b => b.Insert(76, "TextBlock(content: \"Hello\")"), Times.Once);
        }

        /// <summary>
        /// Verifies that InsertCodeAtOffset wraps element under cursor if component has element parameter.
        /// </summary>
        [Test]
        public void InsertCodeAtOffset_ElementUnderCursor_ShouldWrapIfElementParamExists()
        {
            var mockBuffer   = new Mock<ITextBuffer>();
            var mockSnapshot = new Mock<ITextSnapshot>();

            mockSnapshot.Setup(s => s.GetText()).Returns("class MyComponent : Component { public override VisualNode Render() { return TextBlock(\"inner\"); } }");
            mockBuffer.Setup(b => b.CurrentSnapshot).Returns(mockSnapshot.Object);

            var item = new ComponentItem
            {
                Name             = "Border",
                ElementParamName = "child",
                FactoryParams    = new[] { "child: null" }
            };

            ComponentsToolWindowControl.InsertCodeAtOffset(mockBuffer.Object, item, 77);

            mockBuffer.Verify(b => b.Replace(
                It.Is<Span>(s => s.Start == 77 && s.Length == 18),
                "Border(child: TextBlock(\"inner\"))"
            ), Times.Once);
        }

        /// <summary>
        /// Verifies that IsValidInsertionLocation returns true for locations inside an Element-producing method.
        /// </summary>
        [Test]
        public void IsValidInsertionLocation_InsideElementProducingMethod_ShouldReturnTrue()
        {
            var code = "class MyComponent { public VisualNode Render() { return TextBlock(\"hello\"); } }";

            var result = ComponentsToolWindowControl.IsValidInsertionLocation(code, 56);

            result.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies that IsValidInsertionLocation returns false for locations inside a void method.
        /// </summary>
        [Test]
        public void IsValidInsertionLocation_InsideVoidMethod_ShouldReturnFalse()
        {
            var code = "class MyComponent { public void Init() { var x = 1; } }";

            var result = ComponentsToolWindowControl.IsValidInsertionLocation(code, 48);

            result.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies that IsValidInsertionLocation returns false for locations outside any class member.
        /// </summary>
        [Test]
        public void IsValidInsertionLocation_OutsideClassMember_ShouldReturnFalse()
        {
            var code = "class MyComponent {  }";

            var result = ComponentsToolWindowControl.IsValidInsertionLocation(code, 20);

            result.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies that FindAppDescription returns the literal string parameter from ReactorApp.Run.
        /// </summary>
        [Test]
        public void FindAppDescription_WithLiteralString_ShouldReturnLiteralValue()
        {
            var code = "class App : Component { } void Main() { ReactorApp.Run<App>(\"My Visual Studio Extension App\"); }";

            var result = ComponentsToolWindowControl.FindAppDescription(code);

            result.ShouldBe("My Visual Studio Extension App");
        }

        /// <summary>
        /// Verifies that FindAppDescription returns resolved static text from interpolated strings.
        /// </summary>
        [Test]
        public void FindAppDescription_WithInterpolatedString_ShouldReturnStaticText()
        {
            var code = "class App : Component { } void Main() { ReactorApp.Run<App>($\"My Application Version {1.0}\"); }";

            var result = ComponentsToolWindowControl.FindAppDescription(code);

            result.ShouldBe("My Application Version ");
        }

        /// <summary>
        /// Verifies that FindAppDescription returns null when no ReactorApp.Run call exists.
        /// </summary>
        [Test]
        public void FindAppDescription_NoRunCall_ShouldReturnNull()
        {
            var code = "class App : Component { }";

            var result = ComponentsToolWindowControl.FindAppDescription(code);

            result.ShouldBeNull();
        }

        /// <summary>
        /// Verifies that ComponentItem DisplayName appends the description only for the App component.
        /// </summary>
        [Test]
        public void ComponentItem_DisplayName_ShouldFormatAppWithDescription()
        {
            var appItem = new ComponentItem
            {
                Name        = "App",
                Description = "My App Title"
            };

            var otherItem = new ComponentItem
            {
                Name        = "TextBlock",
                Description = "Some description"
            };

            appItem.DisplayName.ShouldBe("App (My App Title)");
            otherItem.DisplayName.ShouldBe("TextBlock");
        }
    }

    /// <summary>
    /// Unit tests for the <see cref="ReactorComponentDropHandlerProvider"/> class.
    /// </summary>
    [TestFixture]
    public class ReactorComponentDropHandlerProviderTests
    {
        /// <summary>
        /// Verifies that GetAssociatedDropHandler returns a non-null drop handler.
        /// </summary>
        [Test]
        public void GetAssociatedDropHandler_ShouldReturnHandler()
        {
            var provider     = new ReactorComponentDropHandlerProvider();
            var mockTextView = new Mock<IWpfTextView>();

            var result = provider.GetAssociatedDropHandler(mockTextView.Object);

            result.ShouldNotBeNull();
            result.ShouldBeOfType<ReactorComponentDropHandler>();
        }
    }
}
