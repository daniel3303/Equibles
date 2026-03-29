using System.Text.Json;
using Equibles.Core.Extensions;
using Equibles.Web.FlashMessage;
using Equibles.Web.FlashMessage.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Tests.Web;

public class FlashMessageModelTests {
    [Fact]
    public void Constructor_DefaultType_IsSuccess() {
        var model = new FlashMessageModel();

        model.Type.Should().Be(FlashMessageType.Success);
    }

    [Fact]
    public void Constructor_DefaultIsHtml_IsFalse() {
        var model = new FlashMessageModel();

        model.IsHtml.Should().BeFalse();
    }

    [Fact]
    public void Constructor_DefaultTitle_IsNull() {
        var model = new FlashMessageModel();

        model.Title.Should().BeNull();
    }

    [Fact]
    public void Constructor_DefaultMessage_IsNull() {
        var model = new FlashMessageModel();

        model.Message.Should().BeNull();
    }

    [Fact]
    public void Properties_CanSetTitle() {
        var model = new FlashMessageModel { Title = "Test Title" };

        model.Title.Should().Be("Test Title");
    }

    [Fact]
    public void Properties_CanSetMessage() {
        var model = new FlashMessageModel { Message = "Something happened" };

        model.Message.Should().Be("Something happened");
    }

    [Fact]
    public void Properties_CanSetType() {
        var model = new FlashMessageModel { Type = FlashMessageType.Error };

        model.Type.Should().Be(FlashMessageType.Error);
    }

    [Fact]
    public void Properties_CanSetIsHtml() {
        var model = new FlashMessageModel { IsHtml = true };

        model.IsHtml.Should().BeTrue();
    }

    [Fact]
    public void Properties_CanSetAllAtOnce() {
        var model = new FlashMessageModel {
            Title = "Alert",
            Message = "<b>Bold</b>",
            Type = FlashMessageType.Warning,
            IsHtml = true
        };

        model.Title.Should().Be("Alert");
        model.Message.Should().Be("<b>Bold</b>");
        model.Type.Should().Be(FlashMessageType.Warning);
        model.IsHtml.Should().BeTrue();
    }
}

public class FlashMessageTypeTests {
    [Theory]
    [InlineData(FlashMessageType.Info, "Information")]
    [InlineData(FlashMessageType.Warning, "Warning")]
    [InlineData(FlashMessageType.Error, "Error")]
    [InlineData(FlashMessageType.Success, "Success")]
    public void NameForHumans_ReturnsDisplayAttributeName(FlashMessageType type, string expected) {
        type.NameForHumans().Should().Be(expected);
    }

    [Fact]
    public void AllValues_HaveDisplayAttribute() {
        foreach (var value in Enum.GetValues<FlashMessageType>()) {
            var act = () => value.NameForHumans();
            act.Should().NotThrow();
            value.NameForHumans().Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void Enum_HasFourValues() {
        Enum.GetValues<FlashMessageType>().Should().HaveCount(4);
    }
}

public class JsonFlashMessageSerializerTests {
    private readonly JsonFlashMessageSerializer _serializer = new();

    [Fact]
    public void Serialize_SingleMessage_ProducesValidJson() {
        var messages = new List<IFlashMessageModel> {
            new FlashMessageModel {
                Title = "Done",
                Message = "Saved successfully",
                Type = FlashMessageType.Success,
                IsHtml = false
            }
        };

        var json = _serializer.Serialize(messages);

        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    [Fact]
    public void Serialize_SingleMessage_ContainsExpectedProperties() {
        var messages = new List<IFlashMessageModel> {
            new FlashMessageModel {
                Title = "Done",
                Message = "Saved successfully",
                Type = FlashMessageType.Success,
                IsHtml = false
            }
        };

        var json = _serializer.Serialize(messages);

        json.Should().Contain("Done");
        json.Should().Contain("Saved successfully");
    }

    [Fact]
    public void Serialize_EmptyList_ProducesEmptyJsonArray() {
        var messages = new List<IFlashMessageModel>();

        var json = _serializer.Serialize(messages);

        json.Should().Be("[]");
    }

    [Fact]
    public void Deserialize_RoundTrip_PreservesAllProperties() {
        var original = new FlashMessageModel {
            Title = "Alert",
            Message = "Something went wrong",
            Type = FlashMessageType.Error,
            IsHtml = true
        };
        var messages = new List<IFlashMessageModel> { original };

        var json = _serializer.Serialize(messages);
        var result = _serializer.Deserialize(json);

        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Alert");
        result[0].Message.Should().Be("Something went wrong");
        result[0].Type.Should().Be(FlashMessageType.Error);
        result[0].IsHtml.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_MultipleMessages_PreservesOrder() {
        var messages = new List<IFlashMessageModel> {
            new FlashMessageModel { Message = "First", Type = FlashMessageType.Info },
            new FlashMessageModel { Message = "Second", Type = FlashMessageType.Warning },
            new FlashMessageModel { Message = "Third", Type = FlashMessageType.Error }
        };

        var json = _serializer.Serialize(messages);
        var result = _serializer.Deserialize(json);

        result.Should().HaveCount(3);
        result[0].Message.Should().Be("First");
        result[0].Type.Should().Be(FlashMessageType.Info);
        result[1].Message.Should().Be("Second");
        result[1].Type.Should().Be(FlashMessageType.Warning);
        result[2].Message.Should().Be("Third");
        result[2].Type.Should().Be(FlashMessageType.Error);
    }

    [Fact]
    public void Deserialize_RoundTrip_PreservesIsHtmlFalse() {
        var messages = new List<IFlashMessageModel> {
            new FlashMessageModel { Message = "Plain text", IsHtml = false }
        };

        var json = _serializer.Serialize(messages);
        var result = _serializer.Deserialize(json);

        result[0].IsHtml.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_RoundTrip_PreservesNullTitle() {
        var messages = new List<IFlashMessageModel> {
            new FlashMessageModel { Message = "No title" }
        };

        var json = _serializer.Serialize(messages);
        var result = _serializer.Deserialize(json);

        result[0].Title.Should().BeNull();
    }

    [Fact]
    public void Deserialize_EmptyJsonArray_ReturnsEmptyList() {
        var result = _serializer.Deserialize("[]");

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void Deserialize_AllFlashMessageTypes_RoundTripCorrectly() {
        var messages = Enum.GetValues<FlashMessageType>()
            .Select(t => (IFlashMessageModel)new FlashMessageModel {
                Message = $"Type: {t}",
                Type = t
            })
            .ToList();

        var json = _serializer.Serialize(messages);
        var result = _serializer.Deserialize(json);

        result.Should().HaveCount(messages.Count);
        for (var i = 0; i < messages.Count; i++) {
            result[i].Type.Should().Be(messages[i].Type);
            result[i].Message.Should().Be(messages[i].Message);
        }
    }

    [Fact]
    public void Deserialize_HtmlContent_IsPreserved() {
        var messages = new List<IFlashMessageModel> {
            new FlashMessageModel {
                Message = "<strong>Important</strong> <a href=\"/link\">click here</a>",
                IsHtml = true
            }
        };

        var json = _serializer.Serialize(messages);
        var result = _serializer.Deserialize(json);

        result[0].Message.Should().Contain("<strong>");
        result[0].Message.Should().Contain("<a href=");
    }
}

public class FlashMessageExtensionsTests {
    [Fact]
    public void AddFlashMessage_RegistersIFlashMessage() {
        var services = new ServiceCollection();

        services.AddFlashMessage();

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IFlashMessage) &&
            sd.ImplementationType == typeof(Equibles.Web.FlashMessage.FlashMessage) &&
            sd.Lifetime == ServiceLifetime.Transient);
    }

    [Fact]
    public void AddFlashMessage_RegistersIFlashMessageSerializer() {
        var services = new ServiceCollection();

        services.AddFlashMessage();

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IFlashMessageSerializer) &&
            sd.ImplementationType == typeof(JsonFlashMessageSerializer) &&
            sd.Lifetime == ServiceLifetime.Transient);
    }

    [Fact]
    public void AddFlashMessage_RegistersExactlyTwoServices() {
        var services = new ServiceCollection();

        services.AddFlashMessage();

        services.Should().HaveCount(2);
    }

    [Fact]
    public void AddFlashMessage_CalledTwice_RegistersDuplicates() {
        var services = new ServiceCollection();

        services.AddFlashMessage();
        services.AddFlashMessage();

        services.Should().HaveCount(4);
    }
}
