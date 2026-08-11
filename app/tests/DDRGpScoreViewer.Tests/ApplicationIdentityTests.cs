using System.Reflection;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class ApplicationIdentityTests
{
    [Fact]
    public void Assembly_metadata_uses_the_public_application_identity()
    {
        var assembly = typeof(DDRGpScoreViewer.Program).Assembly;

        Assert.Equal(
            "2ten.",
            assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
        Assert.Equal(
            "GP Score Log",
            assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
        Assert.Equal(
            "GP Score Log",
            assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title);
    }

    [Fact]
    public void Application_icon_contains_the_required_windows_shell_sizes()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "GPScoreLog.ico");
        Assert.True(File.Exists(iconPath), $"Application icon was not copied to {iconPath}");

        using var stream = File.OpenRead(iconPath);
        using var reader = new BinaryReader(stream);
        Assert.Equal((ushort)0, reader.ReadUInt16());
        Assert.Equal((ushort)1, reader.ReadUInt16());
        var imageCount = reader.ReadUInt16();
        Assert.True(imageCount >= 4);

        var sizes = new HashSet<int>();
        for (var index = 0; index < imageCount; index++)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            reader.ReadBytes(6);
            var imageLength = reader.ReadUInt32();
            var imageOffset = reader.ReadUInt32();
            sizes.Add(width == 0 ? 256 : width);
            Assert.Equal(width, height);
            Assert.True(imageLength > 0);
            Assert.True(imageOffset + imageLength <= stream.Length);
        }

        Assert.Contains(16, sizes);
        Assert.Contains(24, sizes);
        Assert.Contains(32, sizes);
        Assert.Contains(48, sizes);
    }
}
