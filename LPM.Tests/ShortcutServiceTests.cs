using LPM.Services;
using LPM.Tests.Helpers;
using Xunit;

namespace LPM.Tests;

public class ShortcutServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ShortcutService _svc;

    public ShortcutServiceTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new ShortcutService(TestConfig.For(_dbPath));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // ── GetShortcuts ──────────────────────────────────────────────────────

    [Fact]
    public void GetShortcuts_EmptyWhenNoRows()
    {
        Assert.Empty(_svc.GetShortcuts());
    }

    [Fact]
    public void GetShortcuts_ReturnsAllRows()
    {
        _svc.SaveShortcut("a", "Alpha");
        _svc.SaveShortcut("b", "Beta");

        var result = _svc.GetShortcuts();

        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result["a"]);
        Assert.Equal("Beta",  result["b"]);
    }

    [Fact]
    public void GetShortcuts_DictionaryIsCaseInsensitive()
    {
        _svc.SaveShortcut("A", "Hello");

        var result = _svc.GetShortcuts();

        Assert.True(result.ContainsKey("a"));
        Assert.True(result.ContainsKey("A"));
        Assert.Equal("Hello", result["a"]);
        Assert.Equal("Hello", result["A"]);
    }

    // ── SaveShortcut — insert ─────────────────────────────────────────────

    [Fact]
    public void SaveShortcut_InsertsNewRow()
    {
        _svc.SaveShortcut("x", "My text");

        var result = _svc.GetShortcuts();

        Assert.Single(result);
        Assert.Equal("My text", result["x"]);
    }

    [Fact]
    public void SaveShortcut_MultipleKeys_StoredIndependently()
    {
        _svc.SaveShortcut("a", "Alpha");
        _svc.SaveShortcut("b", "Beta");
        _svc.SaveShortcut("c", "Gamma");

        var result = _svc.GetShortcuts();

        Assert.Equal(3, result.Count);
        Assert.Equal("Alpha", result["a"]);
        Assert.Equal("Beta",  result["b"]);
        Assert.Equal("Gamma", result["c"]);
    }

    // ── SaveShortcut — update ─────────────────────────────────────────────

    [Fact]
    public void SaveShortcut_UpdatesExistingRow()
    {
        _svc.SaveShortcut("a", "First");
        _svc.SaveShortcut("a", "Updated");

        var result = _svc.GetShortcuts();

        Assert.Single(result);
        Assert.Equal("Updated", result["a"]);
    }

    [Fact]
    public void SaveShortcut_UpdateDoesNotCreateDuplicate()
    {
        _svc.SaveShortcut("a", "V1");
        _svc.SaveShortcut("a", "V2");
        _svc.SaveShortcut("a", "V3");

        Assert.Single(_svc.GetShortcuts());
    }

    // ── SaveShortcut — delete ─────────────────────────────────────────────

    [Fact]
    public void SaveShortcut_EmptyText_DeletesRow()
    {
        _svc.SaveShortcut("a", "Hello");
        _svc.SaveShortcut("a", "");

        Assert.Empty(_svc.GetShortcuts());
    }

    [Fact]
    public void SaveShortcut_EmptyText_OnNonExistentKey_DoesNotThrow()
    {
        var ex = Record.Exception(() => _svc.SaveShortcut("z", ""));
        Assert.Null(ex);
        Assert.Empty(_svc.GetShortcuts());
    }

    [Fact]
    public void SaveShortcut_DeleteOneKey_LeavesOthersIntact()
    {
        _svc.SaveShortcut("a", "Alpha");
        _svc.SaveShortcut("b", "Beta");
        _svc.SaveShortcut("a", ""); // delete "a"

        var result = _svc.GetShortcuts();

        Assert.Single(result);
        Assert.Equal("Beta", result["b"]);
    }
}
