using System.Text.Json;
using MyTui.Persistence;
using MyTui.Sessions;

namespace MyTui.Tests;

public class TemplateStoreTests
{
    [Fact]
    public void RoundTrip_KeepsExplicitEnvAndDoesNotStart()
    {
        string directory = Directory.CreateTempSubdirectory("my-tui-templates").FullName;
        string templatesPath = Path.Combine(directory, "templates.json");
        string statePath = Path.Combine(directory, "state.json");
        string sentinel = "MYTUI_INHERITED_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(sentinel, "nope");
        try
        {
            var store = new TemplateStore(templatesPath);
            store.Upsert(new SessionTemplate
            {
                Name = "pi",
                Cwd = directory,
                Command = "pi",
                Args = ["--no-session", "--offline"],
                Env = new Dictionary<string, string> { ["ONLY_THIS"] = "yes", ["BAD=KEY"] = "no" },
            });

            string json = File.ReadAllText(templatesPath);
            Assert.DoesNotContain(sentinel, json);
            Assert.DoesNotContain("BAD=KEY", json);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement saved = Assert.Single(document.RootElement.GetProperty("templates").EnumerateArray());
            Assert.Equal("pi", saved.GetProperty("name").GetString());
            Assert.Equal("--offline", saved.GetProperty("args")[1].GetString());
            Assert.Equal("yes", saved.GetProperty("env").GetProperty("ONLY_THIS").GetString());

            SessionTemplate loaded = Assert.Single(store.Load());
            var manager = SessionManager.Load(statePath);
            Session session = manager.Create(loaded.Name, loaded.Cwd, loaded.Command, loaded.Args, loaded.Env);
            Assert.Null(session.Terminal);
            Assert.False(session.IsRunning);
            Assert.Equal("yes", session.Env["ONLY_THIS"]);
            Assert.False(File.Exists(statePath) && File.ReadAllText(statePath).Contains("\"pid\""));
        }
        finally
        {
            Environment.SetEnvironmentVariable(sentinel, null);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CorruptFile_IsParkedAndLeavesSessionsAlone()
    {
        string directory = Directory.CreateTempSubdirectory("my-tui-templates-bad").FullName;
        string templatesPath = Path.Combine(directory, "templates.json");
        string statePath = Path.Combine(directory, "state.json");
        File.WriteAllText(templatesPath, "{");
        File.WriteAllText(statePath, "{\"sessions\":[]}");
        try
        {
            var store = new TemplateStore(templatesPath);
            Assert.Empty(store.Load());
            Assert.False(File.Exists(templatesPath));
            string backup = Assert.Single(Directory.GetFiles(directory, "templates.json.bad-*"));
            Assert.Equal("{", File.ReadAllText(backup));
            Assert.Equal("{\"sessions\":[]}", File.ReadAllText(statePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
