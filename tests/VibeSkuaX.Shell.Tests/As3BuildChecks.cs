using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace VibeSkuaX.Shell.Tests;

internal static class As3BuildChecks
{
    public static void Run()
    {
        // Run checks from the repository root, as documented in README.md.
        string root = Directory.GetCurrentDirectory();
        using var bridge = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Skua.AS3/skua.swf.manifest.json")));
        string sources = Path.Combine(root, "Skua.AS3/skua/src");
        var hashes = bridge.RootElement.GetProperty("Sources");
        Program.Check(Directory.GetFiles(sources, "*.as", SearchOption.AllDirectories).Length == hashes.EnumerateObject().Count(), "AS3 sources changed; rebuild skua.swf.");
        foreach (var source in hashes.EnumerateObject()) Verify(Path.Combine(sources, source.Name), source.Value.GetString()!, text: true);
        Verify(Path.Combine(root, "Skua.AS3/skua/bin/skua.swf"), bridge.RootElement.GetProperty("SwfSha256").GetString()!);
        // Also prove the app output being tested contains that binary.
        Verify(Path.Combine(AppContext.BaseDirectory, "skua.swf"), bridge.RootElement.GetProperty("SwfSha256").GetString()!);
        string fixtures = Path.Combine(root, "tests/VibeSkuaX.Shell.Tests/Fixtures");
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "InventoryHarness.manifest.json")));
        Verify(Path.Combine(sources, "skua/api/Inventory.as"), fixture.RootElement.GetProperty("InventorySha256").GetString()!, text: true);
        Verify(Path.Combine(fixtures, "InventoryHarness.as"), fixture.RootElement.GetProperty("HarnessSha256").GetString()!, text: true);
        Verify(Path.Combine(AppContext.BaseDirectory, "Fixtures/InventoryHarness.swf"), fixture.RootElement.GetProperty("SwfSha256").GetString()!);
        string category = Path.Combine(root, "references/decompiled game/Game4000r5/scripts/InvCat.as");
        if (File.Exists(category)) Verify(category, fixture.RootElement.GetProperty("InvCatSha256").GetString()!, text: true);
        Console.WriteLine("PASS: bundled AS3 and inventory fixture hashes match their sources and build outputs.");
    }

    private static void Verify(string path, string expected, bool text = false)
    {
        byte[] bytes = text ? Encoding.UTF8.GetBytes(File.ReadAllText(path).ReplaceLineEndings("\n")) : File.ReadAllBytes(path);
        string actual = Convert.ToHexString(SHA256.HashData(bytes));
        Program.Check(actual == expected, $"AS3 binary/source mismatch: {path}. Rebuild the corresponding SWF.");
    }
}
