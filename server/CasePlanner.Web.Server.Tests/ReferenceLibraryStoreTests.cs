using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Persistence;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

public sealed class ReferenceLibraryStoreTests
{
    [Fact]
    public async Task DeletingBuiltInReferenceCreatesTombstoneUntilExplicitlySaved()
    {
        var root = Path.Combine(Path.GetTempPath(), "caseplanner-reference-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "server", "tests"));
        try
        {
            var reference = Path.Combine(root, "templates", "reference");
            Directory.CreateDirectory(reference);
            await File.WriteAllTextAsync(Path.Combine(reference, "jury_instructions.txt"), "Initial text");
            var store = new FileReferenceLibraryStore(new PathService(new TestHostEnvironment(Path.Combine(root, "server", "tests"))));

            await store.DeleteAsync("jury_instructions");
            Assert.DoesNotContain(await store.GetAsync(), item => item.Key == "jury_instructions");

            await store.SaveAsync(new ReferenceDocumentUpdate { Key = "jury_instructions", Title = "Restored", Text = "Updated text" });
            var restored = Assert.Single((await store.GetAsync()).Where(item => item.Key == "jury_instructions"));
            Assert.Equal("Restored", restored.Title);
            Assert.Equal("Updated text", restored.Text);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
