#if UNITY_EDITOR
using System;
using System.IO;
using NUnit.Framework;

public sealed class ServiceProviderArchitectureTests
{
    [Test]
    public void PronunciationEndpointResolver_UsesExplicitModeAndNormalizesTrailingSlash()
    {
        var resolver = new PronunciationAnalysisEndpointResolver();

        string local = resolver.Resolve(new PronunciationAnalysisEndpointOptions(
            WavLmEndpointMode.LocalDemo,
            " http://127.0.0.1:8080/ ",
            "https://speech.example.com/",
            "https://custom.example.com/",
            false));
        string cloud = resolver.Resolve(new PronunciationAnalysisEndpointOptions(
            WavLmEndpointMode.CloudProduction,
            "http://127.0.0.1:8080",
            "https://speech.example.com/",
            "https://custom.example.com/",
            true));

        Assert.AreEqual("http://127.0.0.1:8080", local);
        Assert.AreEqual("https://speech.example.com", cloud);
    }

    [Test]
    public void PronunciationEndpointResolver_AutoModeHonorsEnvironmentPreferenceAndFallback()
    {
        var resolver = new PronunciationAnalysisEndpointResolver();

        string editorEndpoint = resolver.Resolve(new PronunciationAnalysisEndpointOptions(
            WavLmEndpointMode.Auto,
            "http://localhost:8080",
            "https://speech.example.com",
            "https://custom.example.com",
            true));
        string releaseFallback = resolver.Resolve(new PronunciationAnalysisEndpointOptions(
            WavLmEndpointMode.Auto,
            "http://localhost:8080",
            "",
            "https://custom.example.com",
            false));

        Assert.AreEqual("http://localhost:8080", editorEndpoint);
        Assert.AreEqual("https://custom.example.com", releaseFallback);
    }

    [Test]
    public void LocalSpeechModelResolver_PrefersDeploymentOverrideWithoutHardWiringProvider()
    {
        string root = Path.Combine(Path.GetTempPath(), "the-script-model-tests");
        string overrideRoot = Path.Combine(root, "deployment");
        string expected = Path.GetFullPath(Path.Combine(overrideRoot, "CustomVosk"));
        var configuration = new LocalSpeechModelConfiguration
        {
            voskDirectoryName = "CustomVosk",
        };
        var resolver = new ConfigurableLocalSpeechModelPathResolver(
            configuration,
            Path.Combine(root, "streaming"),
            Path.Combine(root, "project"),
            overrideRoot,
            path => string.Equals(path, expected, StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual(expected, resolver.ResolvePath(LocalSpeechModelKind.VoskRecognition));
    }

    [Test]
    public void LocalSpeechModelConfiguration_RejectsRootedAndTraversalPaths()
    {
        Assert.AreEqual(
            "VoskModel",
            ConfigurableLocalSpeechModelPathResolver.SanitizeDirectoryName("../private-model", "VoskModel"));
        Assert.AreEqual(
            Path.Combine("ContentSource", "SpeechModels"),
            ConfigurableLocalSpeechModelPathResolver.SanitizeRelativePath(
                "../../outside",
                Path.Combine("ContentSource", "SpeechModels")));
    }

    [Test]
    public void LocalSpeechModelResolver_PreservesPackagedStreamingAssetUris()
    {
        var resolver = new ConfigurableLocalSpeechModelPathResolver(
            new LocalSpeechModelConfiguration(),
            "jar:file:///data/app/game.apk!/assets",
            "",
            "",
            _ => false);

        string path = resolver.ResolvePath(LocalSpeechModelKind.ZipaPronunciation, false);

        Assert.AreEqual("jar:file:///data/app/game.apk!/assets/Zipa", path);
    }

    [Test]
    public void DefaultCurriculumProviderFactory_KeepsOfflineModeAvailable()
    {
        var factory = new DefaultCurriculumProviderFactory();
        ICurriculumAccessProvider provider = factory.Create(new CurriculumProviderContext(
            CurriculumProviderMode.LocalDemo,
            "",
            "",
            "",
            "",
            "demo-school",
            "demo-class",
            ""));

        Assert.IsInstanceOf<LocalDemoCurriculumProvider>(provider);
    }
}
#endif
