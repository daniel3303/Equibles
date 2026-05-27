using Equibles.Web.FlashMessage;
using Equibles.Web.FlashMessage.Contracts;

namespace Equibles.UnitTests.Web;

public class JsonFlashMessageSerializerRoundTripTests
{
    // JsonFlashMessageSerializer is the on-the-wire format for flash messages
    // persisted across a redirect (cookie or TempData). Every IFlashMessage.*
    // call eventually round-trips through Serialize -> Deserialize, so the
    // documented contract is: a deserialised list of messages is
    // indistinguishable from the originals on every interface property.
    //
    // The implementation serialises through `IList<IFlashMessageModel>` —
    // System.Text.Json serialises using the *declared* type, so any property
    // defined only on the concrete `FlashMessageModel` and not on the
    // `IFlashMessageModel` interface is silently dropped. A refactor that
    // added a non-default property to the interface without including it in
    // the concrete class (or vice versa) would silently lose flash data
    // across a single redirect; the user would see the wrong message type
    // styling, the wrong title, or no message at all.
    //
    // Pin: a fully-populated message survives a Serialize -> Deserialize
    // cycle byte-for-byte across all four interface properties (IsHtml,
    // Title, Message, Type). Pick non-default values for every property —
    // IsHtml=true, Type=Error — so a regression that silently defaults a
    // property on the round-trip path surfaces here.
    [Fact]
    public void SerializeThenDeserialize_FullyPopulatedMessage_PreservesEveryInterfaceProperty()
    {
        var sut = new JsonFlashMessageSerializer();
        var original = new FlashMessageModel
        {
            IsHtml = true,
            Title = "Backup complete",
            Message = "Restored 3 records",
            Type = FlashMessageType.Error,
        };

        var json = sut.Serialize(new List<IFlashMessageModel> { original });
        var roundTripped = sut.Deserialize(json);

        roundTripped.Should().ContainSingle();
        var copy = roundTripped[0];
        copy.IsHtml.Should().Be(original.IsHtml);
        copy.Title.Should().Be(original.Title);
        copy.Message.Should().Be(original.Message);
        copy.Type.Should().Be(original.Type);
    }
}
