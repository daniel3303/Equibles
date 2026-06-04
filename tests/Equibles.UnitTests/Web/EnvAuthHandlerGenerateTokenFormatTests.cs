using Equibles.Web.Authentication;

namespace Equibles.UnitTests.Web;

public class EnvAuthHandlerGenerateTokenFormatTests
{
    // The session token is base64(SHA-256("<username>:<secret>")) and is persisted
    // in users' cookies — its exact format is a stability contract: any drift (order
    // swap, dropped colon, different hash, hex vs base64) logs out every existing
    // session. The existing diff-username / diff-secret pins still pass under such a
    // change; only a fixed golden value catches it. Constant computed independently
    // from "admin:s3cr3t" via `printf | openssl dgst -sha256 -binary | openssl base64`.
    [Fact]
    public void GenerateToken_KnownUsernameAndSecret_MatchesFrozenBase64Sha256()
    {
        var token = EnvAuthHandler.GenerateToken("admin", "s3cr3t");

        token.Should().Be("hU8j8VHJWK7+rXnYOoB48UX5Bvnnad1vrOiYkECxYtU=");
    }
}
