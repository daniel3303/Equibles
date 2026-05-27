using Equibles.Web.FlashMessage;
using Equibles.Web.FlashMessage.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class FlashMessageTitlePlumbingTests
{
    // The four FlashMessage convenience helpers (Success / Error / Info /
    // Warning) all accept an optional `title` parameter and forward it
    // through the shared private QueueOfType helper:
    //   QueueOfType(type, message, title, isHtml) =>
    //       Queue(new FlashMessageModel { Message=msg, Title=title, ... });
    //
    // Every existing sibling pin in this file passes a single positional
    // argument (`sut.Error("Something broke")`) — the `title` parameter
    // defaults to null in those calls, so its plumbing through QueueOfType
    // is unreachable from any current test. A "harmonize all helpers to
    // null titles" cleanup that dropped the `Title = title` line in
    // QueueOfType would compile, pass every existing Error/Warning/Info/
    // Success pin, and silently strip the title from every flash message
    // emitted by a controller that uses the title overload (the
    // _FlashMessages.cshtml partial renders the title as a bold heading
    // above the message — losing it doesn't 500, it just produces a
    // titleless toast that's still readable).
    //
    // Pin: pass an explicit non-null title via `sut.Error(message, title)`,
    // capture the serialized payload, round-trip through the real
    // JsonFlashMessageSerializer, assert the deserialised model carries
    // the exact title. Distinguishes from the existing Error sibling
    // because that pin asserts `Type` only and uses the default-null
    // title — a regression that drops the Title assignment would still
    // pass the Type pin.
    [Fact]
    public void Error_TitleArgumentSupplied_PlumbsThroughQueueOfTypeIntoFlashMessageModel()
    {
        var serializer = new JsonFlashMessageSerializer();
        string captured = null;
        var tempData = Substitute.For<ITempDataDictionary>();
        tempData
            .WhenForAnyArgs(t => t[FlashMessage.KeyName] = default)
            .Do(callInfo => captured = (string)callInfo.Arg<object>());
        var factory = Substitute.For<ITempDataDictionaryFactory>();
        factory.GetTempData(Arg.Any<HttpContext>()).Returns(tempData);
        var sut = new FlashMessage(factory, Substitute.For<IHttpContextAccessor>(), serializer);

        sut.Error("Could not save", "Form submission failed");

        captured.Should().NotBeNull();
        var roundTripped = serializer.Deserialize(captured);
        roundTripped.Should().ContainSingle();
        roundTripped[0].Title.Should().Be("Form submission failed");
        roundTripped[0].Message.Should().Be("Could not save");
    }
}
