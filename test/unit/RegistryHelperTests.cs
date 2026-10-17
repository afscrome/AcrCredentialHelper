namespace AlexCrome.Acr.CredentialHelper.Tests;

public class RegistryHelperTests
{
    [Test]
    [Arguments("REGISTRY.azurecr.io", "registry.azurecr.io")]
    [Arguments(" https://REGISTRY.azurecr.io/v2/ ", "registry.azurecr.io")]
    [Arguments("registry.example.com/repository", "registry.example.com")]
    [Arguments("registry.example.com:443/v2/", "registry.example.com:443")]
    [Arguments("https://registry.example.com:443/v2/", "registry.example.com:443")]
    [Arguments("REGISTRY.example.com:5000/repository", "registry.example.com:5000")]
    [Arguments("https://localhost:5000/v2/", "localhost:5000")]
    [Arguments("[::1]:5000", "[::1]:5000")]
    [Arguments("registry.privatelink.azurecr.io", "registry.privatelink.azurecr.io")]
    public async Task NormalizesRegistryHost(string registry, string expected)
    {
        await Assert.That(RegistryHelper.NormalizeHost(registry)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task RejectsEmptyRegistry(string registry)
    {
        await Assert.That(() => RegistryHelper.NormalizeHost(registry)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(" https://REGISTRY.example.com/v2/ \r\n", "registry.example.com")]
    [Arguments("REGISTRY.example.com:443/repository\n", "registry.example.com:443")]
    [Arguments("registry.privatelink.azurecr.io\n", "registry.privatelink.azurecr.io")]
    public async Task ReadsAndNormalizesInput(string value, string expected)
    {
        using var input = new StringReader(value);
        await Assert.That(await RegistryHelper.ReadFromInput(input)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments(" \r\n")]
    [Arguments("not a hostname")]
    [Arguments("https://")]
    [Arguments("ftp://registry.example.com")]
    [Arguments("https://user:password@registry.example.com")]
    [Arguments("registry.example.com:invalid")]
    public async Task RejectsInvalidInput(string value)
    {
        using var input = new StringReader(value);
        await Assert.That(async () => await RegistryHelper.ReadFromInput(input)).Throws<ArgumentException>();
    }
}
