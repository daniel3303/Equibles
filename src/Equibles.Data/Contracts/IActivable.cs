namespace Equibles.Data.Contracts;

public interface IActivable {
    public Guid Id { get; }
    public bool Active { get; set; }
}