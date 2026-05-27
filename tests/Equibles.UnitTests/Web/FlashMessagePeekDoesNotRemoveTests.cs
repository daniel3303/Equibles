using Equibles.Web.FlashMessage;
using Equibles.Web.FlashMessage.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class FlashMessagePeekDoesNotRemoveTests
{
    // FlashMessage.Peek() must NOT consume the TempData entry — it reads
    // through `TempData.Peek(KeyName)` specifically (NOT the indexer
    // `TempData[KeyName]`) because the indexer marks the key for deletion
    // at the end of the request. Layout/_FlashMessages.cshtml calls Peek
    // on every view render to show the toasts; Retrieve is the single
    // consumer that intentionally removes them.
    //
    // The risk this pin catches and the existing Retrieve / Clear pins
    // cannot:
    //   • Swap `TempData.Peek(KeyName)` for the indexer `TempData[KeyName]`
    //     ("simplify — both read the value"). The indexer marks the key
    //     for deletion, so flash messages would silently vanish after
    //     the first view render that calls Peek — Retrieve would then
    //     return empty and the user never sees the toast.
    //   • Inject a defensive Remove inside Peek (a copy-paste from
    //     Retrieve's `TempData.Remove(KeyName)` line). Same observable
    //     symptom: every Peek consumes the entry.
    //
    // The existing Retrieve_WhenEntryMissing pin asserts no-Remove only on
    // the null-guard short-circuit path; the existing Clear pin asserts
    // no-Remove only on the broad-Clear call. Neither exercises Peek's
    // non-null payload path. This pin closes that gap.
    [Fact]
    public void Peek_WhenEntryExists_DoesNotRemoveItFromTempData()
    {
        var serializer = Substitute.For<IFlashMessageSerializer>();
        serializer
            .Deserialize(Arg.Any<string>())
            .Returns(new List<IFlashMessageModel> { new FlashMessageModel { Message = "hi" } });
        var tempData = Substitute.For<ITempDataDictionary>();
        tempData.Peek(FlashMessage.KeyName).Returns("any-serialized-payload");
        var factory = Substitute.For<ITempDataDictionaryFactory>();
        factory.GetTempData(Arg.Any<HttpContext>()).Returns(tempData);
        var sut = new FlashMessage(factory, Substitute.For<IHttpContextAccessor>(), serializer);

        var result = sut.Peek();

        result.Should().ContainSingle();
        result[0].Message.Should().Be("hi");
        tempData.Received(1).Peek(FlashMessage.KeyName);
        tempData.DidNotReceiveWithAnyArgs().Remove(default);
    }
}
