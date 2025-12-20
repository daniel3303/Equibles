namespace Equibles.Sec.BusinessLogic;

public interface ISecDocumentHtmlToMarkdownConverter {
    string Convert(string html);
}