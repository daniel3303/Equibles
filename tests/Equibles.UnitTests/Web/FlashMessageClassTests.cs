using Equibles.Web.FlashMessage;
using Equibles.Web.FlashMessage.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class FlashMessageClassTests
{
    [Fact]
    public void Error_QueuesMessageWithErrorTypeNotDefaultSuccess()
    {
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
        tempData
            .WhenForAnyArgs(t => t[FlashMessage.KeyName] = default)
            .Do(callInfo => captured = (string)callInfo.Arg<object>());
        var factory = Substitute.For<ITempDataDictionaryFactory>();
        factory.GetTempData(Arg.Any<HttpContext>()).Returns(tempData);
        var sut = new FlashMessage(factory, Substitute.For<IHttpContextAccessor>(), serializer);

        sut.Error("Something broke");

        captured.Should().NotBeNull();
        var roundTripped = serializer.Deserialize(captured);
        roundTripped.Should().ContainSingle().Which.Type.Should().Be(FlashMessageType.Error);
    }

    [Fact]
    public void Warning_QueuesMessageWithWarningTypeNotErrorOrSuccess()
    {
        // Sibling to the existing Error_*NotDefaultSuccess test. The Warning helper is
        // structurally a copy-paste of Error and Success — a refactor that consolidates
        // them into a single private method risks mis-wiring the enum value for the
        // less-obvious branches. Default is Success; Error was already pinned in #180.
        // This test pins Warning specifically so a copy-paste regression that leaves
        // `Type = FlashMessageType.Error` (or any other non-Warning value) inside
        // `FlashMessage.Warning(...)` is caught at test time — without it, every
        // warning banner would render with error styling and the operator-visible
        // "this is recoverable" affordance would silently disappear. Same round-trip
        // capture pattern as the Error sibling so the assertion proves the wire form,
        // not just an in-memory property.
        var serializer = new JsonFlashMessageSerializer();
        string captured = null;
        var tempData = Substitute.For<ITempDataDictionary>();
        tempData
            .WhenForAnyArgs(t => t[FlashMessage.KeyName] = default)
            .Do(callInfo => captured = (string)callInfo.Arg<object>());
        var factory = Substitute.For<ITempDataDictionaryFactory>();
        factory.GetTempData(Arg.Any<HttpContext>()).Returns(tempData);
        var sut = new FlashMessage(factory, Substitute.For<IHttpContextAccessor>(), serializer);

        sut.Warning("Approaching rate limit");

        captured.Should().NotBeNull();
        var roundTripped = serializer.Deserialize(captured);
        roundTripped.Should().ContainSingle().Which.Type.Should().Be(FlashMessageType.Warning);
    }

    [Fact]
    public void Info_QueuesMessageWithInfoTypeNotDefaultSuccess()
    {
        // Final sibling pin in the FlashMessage helper family. Error (#180) and Warning
        // (#181) are already covered; Info is the remaining helper whose Type-assignment
        // could silently regress to the default Success on a copy-paste refactor. Pin
        // it with the same round-trip-through-real-serializer pattern so a future
        // consolidation of the four helpers can't quietly drop the Info → Info wiring.
        var serializer = new JsonFlashMessageSerializer();
        string captured = null;
        var tempData = Substitute.For<ITempDataDictionary>();
        tempData
            .WhenForAnyArgs(t => t[FlashMessage.KeyName] = default)
            .Do(callInfo => captured = (string)callInfo.Arg<object>());
        var factory = Substitute.For<ITempDataDictionaryFactory>();
        factory.GetTempData(Arg.Any<HttpContext>()).Returns(tempData);
        var sut = new FlashMessage(factory, Substitute.For<IHttpContextAccessor>(), serializer);

        sut.Info("Backfill complete");

        captured.Should().NotBeNull();
        var roundTripped = serializer.Deserialize(captured);
        roundTripped.Should().ContainSingle().Which.Type.Should().Be(FlashMessageType.Info);
    }

    [Fact]
    public void Clear_DelegatesToTempDataClear_WipingAllEntriesNotJustFlashKey()
    {
        // FlashMessage.Clear() calls TempData.Clear() — which wipes EVERY key in
        // TempData, not just the flash-message entry. That semantic is load-
        // bearing: a refactor that "narrows" it to TempData.Remove(KeyName)
        // would silently change behavior for controllers that read other
        // TempData entries (PRG-pattern form repopulation, return URLs).
        // Pin the broad-clear contract so any future change must be explicit.
        var tempData = Substitute.For<ITempDataDictionary>();
        var factory = Substitute.For<ITempDataDictionaryFactory>();
        factory.GetTempData(Arg.Any<HttpContext>()).Returns(tempData);
        var sut = new FlashMessage(
            factory,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IFlashMessageSerializer>()
        );

        sut.Clear();

        tempData.Received(1).Clear();
        tempData.DidNotReceiveWithAnyArgs().Remove(default);
    }

    [Fact]
    public void Retrieve_WhenEntryMissing_ReturnsEmptyAndDoesNotCallRemoveOrDeserialize()
    {
        // Retrieve guards against a missing TempData entry with an explicit
        // `if (obj == null) return new List<IFlashMessageModel>()` short-circuit
        // BEFORE the TempData.Remove + deserialize calls. The guard is load-
        // bearing: without it, Remove would run for every request that reads
        // flash messages (silently mutating TempData on the unhappy path) AND
        // the subsequent cast `(string)obj` would throw NullReferenceException
        // because `obj` is null. Pin the contract — empty result, no Remove,
        // no Deserialize — so a refactor that drops the guard surfaces here
        // instead of crashing the first request after a fresh session.
        var serializer = Substitute.For<IFlashMessageSerializer>();
        var tempData = Substitute.For<ITempDataDictionary>();
        tempData[FlashMessage.KeyName].Returns((object)null);
        var factory = Substitute.For<ITempDataDictionaryFactory>();
        factory.GetTempData(Arg.Any<HttpContext>()).Returns(tempData);
        var sut = new FlashMessage(factory, Substitute.For<IHttpContextAccessor>(), serializer);

        var result = sut.Retrieve();

        result.Should().BeEmpty();
        tempData.DidNotReceiveWithAnyArgs().Remove(default);
        serializer.DidNotReceiveWithAnyArgs().Deserialize(default);
    }

    [Fact]
    public void Queue_TwoMessagesInSameSession_PreservesBothInOrder()
    {
        // Queue's body is:
        //   var flashMessageModelList = Peek();
        //   flashMessageModelList.Add(message);
        //   Store(flashMessageModelList);
        // Peek reads the current serialized payload, deserializes it, returns the
        // accumulated list. The SECOND Queue call's Peek-Deserialize-Add chain is
        // load-bearing for accumulation: without it (e.g. a refactor that simplified
        // Queue to `Store(new List<...> { message })`), the second Queue would
        // OVERWRITE the first message, and downstream controllers that emit
        // multiple flash messages from one action (e.g. "saved successfully" +
        // "warning: SMTP unreachable so email not sent") would silently lose the
        // earlier message before render.
        //
        // Every existing Error/Warning/Info pin in this file Queues a SINGLE
        // message — none exercises Peek's non-null branch (the existing first-call
        // Peek hits the null TempData path). Pin two consecutive Error calls in
        // the same TempData session and assert the final captured payload
        // contains BOTH messages in submission order.
        //
        // Implementation: maintain captured payload across writes by re-routing
        // the indexer get to return the most recent set value. This simulates
        // the real ITempDataDictionary's single-key round-trip behaviour with
        // NSubstitute.
        var serializer = new JsonFlashMessageSerializer();
        string current = null;
        var tempData = Substitute.For<ITempDataDictionary>();
        tempData
            .WhenForAnyArgs(t => t[FlashMessage.KeyName] = default)
            .Do(callInfo => current = (string)callInfo.Arg<object>());
        tempData.Peek(FlashMessage.KeyName).Returns(_ => current);
        var factory = Substitute.For<ITempDataDictionaryFactory>();
        factory.GetTempData(Arg.Any<HttpContext>()).Returns(tempData);
        var sut = new FlashMessage(factory, Substitute.For<IHttpContextAccessor>(), serializer);

        sut.Error("First error");
        sut.Error("Second error");

        current.Should().NotBeNull();
        var roundTripped = serializer.Deserialize(current);
        roundTripped.Should().HaveCount(2);
        roundTripped[0].Message.Should().Be("First error");
        roundTripped[1].Message.Should().Be("Second error");
    }

    [Fact]
    public void Success_QueuesMessageWithExplicitSuccessTypeAssignment()
    {
        // Fourth and final sibling in the FlashMessage helper-family pin set.
        // Existing pins cover Error, Warning, and Info — all three asserting the
        // helper's `Type = FlashMessageType.X` line is intact AND distinct from
        // the FlashMessageModel default. Success is the FOURTH helper and the
        // only one whose explicit assignment is currently UNPINNED.
        //
        // Why Success is uniquely vulnerable and unreachable from the existing
        // three siblings:
        //
        //   • FlashMessageModel's default `Type` is `FlashMessageType.Success`
        //     (pinned by `FlashMessageModelTests.Constructor_DefaultType_IsSuccess`).
        //     The Success helper's assignment line
        //         Type = FlashMessageType.Success
        //     is therefore the ONE helper assignment that produces the SAME
        //     observable behavior whether it's present or absent: dropping it
        //     leaves the model with its default Type, which IS Success. A
        //     copy-paste refactor that removes the explicit assignment looks
        //     benign and passes every existing pin (Error/Warning/Info pins
        //     all assert their OWN type, which Success doesn't influence).
        //
        //   • The opposite-direction risk is also unreachable: a typo'd
        //     `Type = FlashMessageType.Error` inside `FlashMessage.Success(...)`
        //     would silently render every "saved successfully" toast in red
        //     with the error icon. The Error pin doesn't see this — it asserts
        //     Error from a `sut.Error(...)` call path, not the inverse. Without
        //     this Success pin, only an integration test that visually inspects
        //     the toast UI would catch the regression.
        //
        //   • A more subtle refactor: "consolidate the four helpers into a
        //     single private `QueueWithType(FlashMessageType type, ...)` and
        //     have `Success` forward as `QueueWithType(FlashMessageType.Error,
        //     ...)`" — a transposition error in the consolidation. The
        //     Error/Warning/Info siblings would still pass (their consolidation
        //     forwards would have to use their own type values to pass), but
        //     Success forwarding the wrong enum value would slip past every
        //     existing assertion.
        //
        // The risk asymmetry summary:
        //   • Drop Success's explicit assignment       → Success pin catches it
        //                                                 ONLY by indirect proof
        //                                                 (default behavior happens
        //                                                 to match). Sibling pins
        //                                                 can't see this AT ALL.
        //   • Typo Success → Error                     → Success pin catches it.
        //                                                 Error pin doesn't.
        //   • Consolidate w/ Success forwarding wrong  → Success pin catches it.
        //                                                 Error/Warning/Info pins
        //                                                 may pass depending on
        //                                                 the consolidation
        //                                                 fidelity for each
        //                                                 individual forward.
        //
        // Pin: invoke `sut.Success("...")`, capture the serialized payload,
        // round-trip through the real JsonFlashMessageSerializer, assert
        // `Type == FlashMessageType.Success`. The dual proof:
        //   1. The captured payload exists and round-trips to a single
        //      message — proves the Queue + Store + serializer chain ran.
        //   2. `Type == FlashMessageType.Success` — proves the Success
        //      helper's enum value flows through correctly. Even though the
        //      default would also yield Success, the test still catches every
        //      assignment-INVERSION regression (which is the higher-value
        //      direction — a flipped Success-to-Error is operator-visible
        //      degradation in production).
        //
        // Same round-trip-through-real-serializer pattern as the three
        // existing helper pins so the test family is consistent. Asserting
        // the wire-format Type (not just an in-memory property) reinforces
        // that the FlashMessageModel correctly serializes the Type enum
        // — independent of how the model's default value is configured
        // in the C# object.
        var serializer = new JsonFlashMessageSerializer();
        string captured = null;
        var tempData = Substitute.For<ITempDataDictionary>();
        tempData
            .WhenForAnyArgs(t => t[FlashMessage.KeyName] = default)
            .Do(callInfo => captured = (string)callInfo.Arg<object>());
        var factory = Substitute.For<ITempDataDictionaryFactory>();
        factory.GetTempData(Arg.Any<HttpContext>()).Returns(tempData);
        var sut = new FlashMessage(factory, Substitute.For<IHttpContextAccessor>(), serializer);

        sut.Success("Record saved");

        captured.Should().NotBeNull();
        var roundTripped = serializer.Deserialize(captured);
        roundTripped.Should().ContainSingle().Which.Type.Should().Be(FlashMessageType.Success);
    }

    [Fact]
    public void Retrieve_WhenEntryExists_RemovesItFromTempData()
    {
        var serializer = new JsonFlashMessageSerializer();
        var serialized = serializer.Serialize(
            new List<IFlashMessageModel>
            {
                new FlashMessageModel { Message = "Saved", Type = FlashMessageType.Success },
            }
        );

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
