namespace Equibles.Sec.BusinessLogic;

public interface IPdfTextExtractor {
    string Extract(byte[] pdfBytes);
}
