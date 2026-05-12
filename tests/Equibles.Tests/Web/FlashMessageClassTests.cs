using Equibles.Web.FlashMessage;
using Equibles.Web.FlashMessage.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace Equibles.Tests.Web;

public class FlashMessageClassTests {
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
