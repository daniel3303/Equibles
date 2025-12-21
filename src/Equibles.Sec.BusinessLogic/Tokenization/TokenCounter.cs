using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.Tokenizers;

namespace Equibles.Sec.BusinessLogic.Tokenization;

[Service(ServiceLifetime.Singleton)]
public class TokenCounter {
    private readonly TiktokenTokenizer _tokenizer = TiktokenTokenizer.CreateForEncoding("o200k_base");

    public TiktokenTokenizer Tokenizer => _tokenizer;

    public int CountTokens(string text) {
        if (string.IsNullOrEmpty(text))
            return 0;

        return _tokenizer.CountTokens(text);
    }
}
