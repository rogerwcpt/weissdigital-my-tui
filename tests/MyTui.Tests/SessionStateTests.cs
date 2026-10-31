using System.Text.Json;
using MyTui.Sessions;

namespace MyTui.Tests;

public class SessionStateTests
{
    [Fact]
    public void RoundTrip_KeepsOrderFocusAndExplicitEnv()
    {
        string directory = Directory.CreateTempSubdirectory("my-tui-state").FullName;
        string path = Path.Combine(directory, "state.json");
        string sentinel = "MYTUI_INHERITED_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(sentinel, "nope");
        try
        {
            var manager = SessionManager.Load(path);
            Session first = manager.Create("one", directory, "/bin/zsh", ["-l"], new Dictionary<string, string> { ["ONLY_THIS"] = "yes" });
            Session second = manager.Create("two", directory, "pi", ["--offline"]);
            manager.SetTab(second, SessionTab.Preview);
            manager.Focus(0);
            Assert.True(manager.MoveFocused(1));

            string json = File.ReadAllText(path);
            Assert.DoesNotContain(sentinel, json);
            AssertNoRuntimeFields(json);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement saved = document.RootElement.GetProperty("sessions").EnumerateArray()
                .Single(item => item.GetProperty("name").GetString() == "one");
            JsonElement env = saved.GetProperty("env");
            JsonProperty only = Assert.Single(env.EnumerateObject());
            Assert.Equal("ONLY_THIS", only.Name);
            Assert.Equal("yes", only.Value.GetString());
            Assert.Equal("terminal", document.RootElement.GetProperty("lastFocusedTab").GetString());

            var again = SessionManager.Load(path);
            Assert.Equal(2, again.Sessions.Count);
            Assert.Equal("two", again.Sessions[0].Name);
            Assert.Equal("one", again.Sessions[1].Name);
            Assert.Equal(1, again.FocusedIndex);
            Assert.Equal(first.Id, again.Focused!.Id);
            Assert.Equal(SessionTab.Preview, again.Sessions[0].Tab);
            Assert.Equal(["-l"], again.Focused.Args);
            Assert.Equal("yes", again.Focused.Env["ONLY_THIS"]);
            Assert.Equal(first.Created, again.Focused.Created);
            Assert.Null(again.Focused.Terminal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(sentinel, null);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CorruptFile_IsParkedAndLoadStartsEmpty()
    {
        string directory = Directory.CreateTempSubdirectory("my-tui-bad").FullName;
        string path = Path.Combine(directory, "state.json");
        File.WriteAllText(path, "{");
        try
        {
            var manager = SessionManager.Load(path);
            Assert.Empty(manager.Sessions);
            Assert.False(File.Exists(path));
            string backup = Assert.Single(Directory.GetFiles(directory, "state.json.bad-*"));
            Assert.Equal("{", File.ReadAllText(backup));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [UnixFact(Timeout = 15000)]
    public async Task RunningProcess_IsNotWrittenToState()
    {
        string directory = Directory.CreateTempSubdirectory("my-tui-live").FullName;
        string path = Path.Combine(directory, "state.json");
        var manager = SessionManager.Load(path);
        Session session = manager.Create("shell", directory, "/bin/sh", []);
        try
        {
            session.Start(80, 24);
            Assert.True(session.IsRunning);
            manager.SetTab(session, SessionTab.Files);
            string json = File.ReadAllText(path);
            AssertNoRuntimeFields(json);
            Assert.True(session.Terminal!.Pid > 1);
            var again = SessionManager.Load(path);
            Assert.Null(again.Focused!.Terminal);
            Assert.Equal(SessionTab.Files, again.Focused.Tab);
        }
        finally
        {
            session.Stop();
            Directory.Delete(directory, recursive: true);
        }

        await Task.CompletedTask;
    }

    private static void AssertNoRuntimeFields(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Walk(document.RootElement);
    }

    private static void Walk(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                Assert.DoesNotContain(property.Name, new[] { "pid", "exitCode", "running", "terminal" });
                Walk(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                Walk(item);
            }
        }
    }

    private sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute()
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            {
                Skip = "The PTY host is macOS and Linux only.";
            }
            else if (!File.Exists("/bin/sh"))
            {
                Skip = "Requires /bin/sh.";
            }
        }
    }
}
