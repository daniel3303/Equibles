namespace Equibles.Data.Contracts;

public interface ISortable {
    public Guid Id { get; }
    public int Order { get; set; }
}