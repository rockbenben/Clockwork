using Clockwork.Core;
using System;
using System.IO;
using Xunit;

// 换标识之后，操作中心里的品牌图标必须跟着换。老判据「文件在且非空就复用」让它永远不换，
// 所以这三条各钉住判据的一面：缺了要写、旧了要重写、一样时不许动。
public class BrandIconCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cwbrand_" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    public BrandIconCacheTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "logo.ico");
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Writes_the_icon_when_it_is_absent()
    {
        Assert.Equal(_path, BrandIconCache.Refresh(_path, new byte[] { 1, 2, 3 }));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(_path));
    }

    [Fact]
    public void Replaces_a_stale_icon_left_by_the_previous_mark()
    {
        File.WriteAllBytes(_path, new byte[] { 1, 2, 9 });        // the old logo, already cached
        BrandIconCache.Refresh(_path, new byte[] { 1, 2, 3 });   // same length on purpose: only a
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(_path));   // content check catches it
    }

    [Fact]
    public void Leaves_an_identical_file_alone()
    {
        File.WriteAllBytes(_path, new byte[] { 1, 2, 3 });
        var stamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Local);
        File.SetLastWriteTime(_path, stamp);

        BrandIconCache.Refresh(_path, new byte[] { 1, 2, 3 });

        Assert.Equal(stamp, File.GetLastWriteTime(_path));      // rewritten => it jumped to now
    }

    [Fact]
    public void Returns_the_path_instead_of_throwing_when_the_write_cannot_happen()
    {
        var missing = Path.Combine(_dir, "no-such-dir", "logo.ico");
        Assert.Equal(missing, BrandIconCache.Refresh(missing, new byte[] { 1 }));
        Assert.False(File.Exists(missing));
    }
}
