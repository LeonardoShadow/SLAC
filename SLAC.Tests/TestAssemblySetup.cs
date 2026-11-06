using System;
using System.Runtime.CompilerServices;

namespace SLAC.Tests;

public static class TestAssemblySetup
{
    public const string TestSigningKeyPem = """
    -----BEGIN EC PRIVATE KEY-----
    MHcCAQEEIGw24oSlnyUR+1OhqPdu2DspZ8bdjWzNkY2QQBT34EkRoAoGCCqGSM49
    AwEHoUQDQgAEGXKnE8T1NHE68eOrjkccTSNn6EZNdMzhz1XGdF7FseuWlZMd6OZd
    OFdESPNEb+KgNAVcYloI9pSMdFzL32Ppww==
    -----END EC PRIVATE KEY-----
    """;

    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("SLAC_SIGNING_KEY_PEM", TestSigningKeyPem);
    }
}
