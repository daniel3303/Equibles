using Equibles.Web.FlashMessage;
using Equibles.Web.FlashMessage.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class FlashMessageClassTests {
    [Fact]
    public void Error_QueuesMessageWithErrorTypeNotDefaultSuccess() {
        // FlashMessageModel's default `Type` is `FlashMessageType.Success` — pinned by
        // FlashMessageModelTests.Constructor_DefaultType_IsSuccess. Every convenience
        // helper (Success / Error / Info / Warning) constructs a FlashMessageModel and
        // sets `Type = ...` explicitly to override that default. The risk this test
        // pins: a copy-paste regression that drops the explicit `Type =` assignment
        // from `FlashMessage.Error`, or that mistakenly sets it to the wrong enum
        // value, would silently render every error toast as a green success toast.
        // The UI renders toast color/icon strictly from this enum value, so the
        // visual error (forms claiming success on failure, error pages showing the
        // success affordance) is invisible to compilers and integration tests.
        //
        // Distinguish "intentional Error" from "default Success" by capturing the
        // serialized payload from TempData[KeyName] and asserting Type=Error after
        // round-tripping through the real serializer — a test that only checks
        // "some message was queued" wouldn't catch the type-swap regression.
        var serializer = new JsonFlashMessageSerializer();
        string captured = null;
        var tempData = Substitute.For<ITempDataDictionary>();
        tempData.WhenForAnyArgs(t => t[FlashMessage.KeyName] = default)
            .Do(callInfo => captured = (string)callInfo.Arg<object>());
        var factory = Substitute.For<ITempDataDictionaryFactory>();
        factory.GetTempData(Arg.Any<HttpContext>()).Returns(tempData);
        var sut = new FlashMessage(factory, Substitute.For<IHttpContextAccessor>(), serializer);

        sut.Error("Something broke");

        captured.Should().NotBeNull();
        var roundTripped = serializer.Deserialize(captured);
        roundTripped.Should().ContainSingle()
            .Which.Type.Should().Be(FlashMessageType.Error);
    }

    [Fact]
    public void Retrieve_WhenEntryExists_RemovesItFromTempData() {
        var serializer = new JsonFlashMessageSerializer();
        var serialized = serializer.Serialize(new List<IFlashMessageModel> {
            new FlashMessageModel { Message = "Saved", Type = FlashMessageType.Success },
        });

        var tempData = Substitute.For<ITempDataDictionary>();
        tempData[FlashMessage.KeyName].Returns(serialized);

        var factory = Substitute.For<ITempDataDictionaryFactory>();
        factory.GetTempData(Arg.Any<HttpContext>()).Returns(tempData);
        var sut = new FlashMessage(factory, Substitute.For<IHttpContextAccessor>(), serializer);

        var result = sut.Retrieve();

        result.Should().ContainSingle().Which.Message.Should().Be("Saved");
        tempData.Received(1).Remove(FlashMessage.KeyName);
    }
}
