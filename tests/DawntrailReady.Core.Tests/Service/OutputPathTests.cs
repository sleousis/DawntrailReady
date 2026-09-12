using DawntrailReady.Core.Service;

namespace DawntrailReady.Core.Tests.Service;

public sealed class OutputPathTests
{
    [Fact]
    public void The_converted_file_goes_next_to_the_original_and_never_replaces_one()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dtready-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "Cool Hat.ttmp2");
            Assert.Equal(Path.Combine(dir, "Cool Hat (Dawntrail).pmp"), DawntrailBackend.OutputPathFor(input));

            File.WriteAllText(Path.Combine(dir, "Cool Hat (Dawntrail).pmp"), "");
            Assert.Equal(Path.Combine(dir, "Cool Hat (Dawntrail) (2).pmp"), DawntrailBackend.OutputPathFor(input));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
