using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

public interface IHtmlNormalizationStep {
    void Execute(IHtmlDocument document);
}
