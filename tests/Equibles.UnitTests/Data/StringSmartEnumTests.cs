using Equibles.Data;

namespace Equibles.UnitTests.Data;

public class StringSmartEnumTests
{
    private sealed class Stage : StringSmartEnum<Stage>
    {
        public static readonly Stage Queued = new("queued", "Queued");
        public static readonly Stage Running = new("running");
        public static readonly Stage Done = new("done", "Done");

        private static readonly IReadOnlyCollection<Stage> All = Register(Queued, Running, Done);

        private Stage(string value, string displayName = null)
            : base(value, displayName) { }
    }

    private sealed class Clash : StringSmartEnum<Clash>
    {
        public static readonly Clash Lower = new("open");
        public static readonly Clash Upper = new("OPEN");

        private static readonly IReadOnlyCollection<Clash> All = Register(Lower, Upper);

        private Clash(string value)
            : base(value) { }
    }

    [Fact]
    public void FromValue_BeforeTheDerivedTypeIsTouched_StillFindsTheSingleton()
    {
        Stage.FromValue("RUNNING").Should().BeSameAs(Stage.Running);
    }

    [Fact]
    public void FromValue_UnknownOrNull_ReturnsNull()
    {
        Stage.FromValue("cancelled").Should().BeNull();
        Stage.FromValue(null).Should().BeNull();
    }

    [Fact]
    public void GetAll_ReturnsTheSingletonsInDeclarationOrder()
    {
        Stage.GetAll().Should().Equal(Stage.Queued, Stage.Running, Stage.Done);
    }

    [Fact]
    public void DisplayName_DefaultsToTheValue()
    {
        Stage.Running.DisplayName.Should().Be("running");
        Stage.Done.ToString().Should().Be("done");
    }

    [Fact]
    public void Register_ValuesThatDifferOnlyByCase_FailsAtTypeInitialization()
    {
        var act = () => Clash.GetAll();

        act.Should()
            .Throw<TypeInitializationException>()
            .WithInnerException<ArgumentException>()
            .WithMessage("*Duplicate Clash value 'OPEN'*");
    }
}
