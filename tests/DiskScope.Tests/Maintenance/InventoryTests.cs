using DiskScope.App.ViewModels;
using DiskScope.Maintenance;
using DiskScope.Reports;

namespace DiskScope.Tests.Maintenance;

public sealed class InventoryTests
{
    private static string NoExpand(string s) => s.Replace("%ProgramFiles%", @"C:\Program Files", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --minimized", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Program Files\App\app.exe /background", @"C:\Program Files\App\app.exe")]
    [InlineData(@"%ProgramFiles%\Tool\tool.exe -s", @"C:\Program Files\Tool\tool.exe")]
    [InlineData(@"C:\tools\run.bat C:\data\x.exe", @"C:\tools\run.bat")]
    [InlineData(@"rundll32.exe shell32.dll,Control_RunDLL", "rundll32.exe")]
    [InlineData("noextension arg", "noextension")]
    [InlineData("   ", null)]
    [InlineData("\"\"", null)]
    // Quotes and spaces.
    [InlineData(@"   C:\x.exe   ", @"C:\x.exe")]
    [InlineData(@"C:\Program Files\App\app.exe", @"C:\Program Files\App\app.exe")]
    [InlineData("\"C:\\a b\\x.exe", @"C:\a b\x.exe")]
    [InlineData("\"C:\\a b\\x.exe\"\"-s", @"C:\a b\x.exe")]
    [InlineData(@"C:\My.exe.d\tool.exe -x", @"C:\My.exe.d\tool.exe")]
    // Environment variables: expanded inside quotes, unknown ones left as written.
    [InlineData("\"%ProgramFiles%\\A B\\a.exe\" /x", @"C:\Program Files\A B\a.exe")]
    [InlineData(@"%NOPE%\x.exe -s", @"%NOPE%\x.exe")]
    // B8: task action path already quoted, then quoted again.
    [InlineData("\"\"C:\\a b\\x.exe\"\" -s", @"C:\a b\x.exe")]
    // B9: cmd /c and /k payloads, bare program names, .dll/.msc/.cpl.
    [InlineData(@"cmd /c C:\s\x.bat", @"C:\s\x.bat")]
    [InlineData("cmd.exe /q /c \"C:\\a b\\y.cmd\" arg", @"C:\a b\y.cmd")]
    [InlineData("CMD /K \"%ProgramFiles%\\x y\\s.cmd\"", @"C:\Program Files\x y\s.cmd")]
    [InlineData("cmd /k", "cmd")]
    [InlineData("cmd", "cmd")]
    [InlineData(@"C:\Windows\System32\control.exe C:\x\desk.cpl", @"C:\Windows\System32\control.exe")]
    [InlineData(@"C:\Program Files\Snap In\tool.msc /a", @"C:\Program Files\Snap In\tool.msc")]
    [InlineData(@"C:\Program Files\Lib Dir\plugin.dll arg", @"C:\Program Files\Lib Dir\plugin.dll")]
    [InlineData("rundll32 shell32.dll,Control_RunDLL", "rundll32")]
    [InlineData(@"rundll32 C:\Program Files\x.exe", "rundll32")]
    public void ExtractExecutable(string command, string? expected) =>
        Assert.Equal(expected, CommandLine.ExtractExecutable(command, NoExpand));

    [Theory]
    [InlineData(@"C:\a b\x.exe", "-s", "\"C:\\a b\\x.exe\" -s")]
    [InlineData("\"C:\\a b\\x.exe\"", "-s", "\"C:\\a b\\x.exe\" -s")]
    [InlineData(@"C:\x.exe", null, "\"C:\\x.exe\"")]
    [InlineData(null, null, "")]
    public void CommandLine_Join(string? path, string? arguments, string expected) =>
        Assert.Equal(expected, CommandLine.Join(path, arguments));

    [Fact]
    public void CommandText_OnlyStringValuesAreCommands()
    {
        Assert.Equal(@"C:\x.exe -s", StartupInventory.CommandText(@"C:\x.exe -s"));
        Assert.Null(StartupInventory.CommandText(new byte[] { 1 }));
        Assert.Null(StartupInventory.CommandText(5));
        Assert.Null(StartupInventory.CommandText(new[] { "a" }));
        Assert.Null(StartupInventory.CommandText(null));
    }

    [Fact]
    public void Locate_ChecksQualifiedPathsDirectly()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\a\x.exe" };

        Assert.Equal((true, @"C:\a\x.exe"), StartupInventory.Locate(@"C:\a\x.exe", [], files.Contains));
        Assert.Equal((false, (string?)null), StartupInventory.Locate(@"C:\a\y.exe", [], files.Contains));
    }

    [Fact]
    public void Locate_BareNames_AreSearchedInTheGivenDirectories()
    {
        string[] dirs = [@"C:\fake\sys", @"C:\fake\win"];
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\fake\win\rundll32.exe", @"C:\fake\sys\cmd.exe" };

        Assert.Equal((true, @"C:\fake\win\rundll32.exe"), StartupInventory.Locate("rundll32.exe", dirs, files.Contains));
        Assert.Equal((true, @"C:\fake\sys\cmd.exe"), StartupInventory.Locate("cmd", dirs, files.Contains));
        Assert.Equal((null, (string?)null), StartupInventory.Locate("nothere", dirs, files.Contains));
    }

    [Fact]
    public void Locate_RelativePathOrNothing_IsUnknown()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\fake\sys\tools\x.exe" };

        Assert.Equal((null, (string?)null), StartupInventory.Locate(@"tools\x.exe", [@"C:\fake\sys"], files.Contains));
        Assert.Equal((null, (string?)null), StartupInventory.Locate(null, [@"C:\fake\sys"], files.Contains));
        Assert.Equal((null, (string?)null), StartupInventory.Locate(@"C:x.exe", [@"C:\fake\sys"], files.Contains));
        Assert.Equal((null, (string?)null), StartupInventory.Locate(@"%NOPE%\x.exe", [@"C:\fake\sys"], files.Contains));
        Assert.Equal((null, (string?)null), StartupInventory.Locate(@"\x.exe", [@"C:\fake\sys"], files.Contains));
    }

    [Fact]
    public void Locate_MissingQualifiedPath_IsAbsent_ButMissingBareName_IsUnknown()
    {
        Func<string, bool> none = _ => false;

        Assert.Equal((false, (string?)null), StartupInventory.Locate(@"C:\gone\app.exe", [@"C:\fake\sys"], none));
        Assert.Equal((false, (string?)null), StartupInventory.Locate(@"\\server\share\app.exe", [], none));
        Assert.Equal((null, (string?)null), StartupInventory.Locate("rundll32.exe", [@"C:\fake\sys"], none));
    }

    public sealed class FakeTrigger
    {
        public int Type { get; init; }
    }

    public sealed class FakeExecAction
    {
        public int Type { get; init; }
        public string? Path { get; init; }
        public string? Arguments { get; init; }
    }

    public sealed class FakeDefinition
    {
        public IEnumerable<object> Triggers { get; init; } = [];
        public IEnumerable<object> Actions { get; init; } = [];
    }

    public sealed class FakeTask
    {
        private readonly FakeDefinition? _definition;

        public FakeTask(FakeDefinition? definition) => _definition = definition;

        public string Name { get; init; } = "task";
        public string Path { get; init; } = @"\task";
        public bool Enabled { get; init; } = true;
        public object Definition => _definition ?? throw new UnauthorizedAccessException("access denied");
    }

    public sealed class FakeFolder
    {
        public string Path { get; init; } = @"\";
        public List<object> Tasks { get; } = [];
        public List<object> Folders { get; } = [];
        public bool TasksThrow { get; init; }

        public IEnumerable<object> GetTasks(int flags) => TasksThrow ? throw new UnauthorizedAccessException("tasks denied") : Tasks;

        public IEnumerable<object> GetFolders(int flags) => Folders;
    }

    private static FakeTask StartupTask(int triggerType, string path, string? args) => new(new FakeDefinition
    {
        Triggers = [new FakeTrigger { Type = triggerType }],
        Actions = [new FakeExecAction { Type = 0, Path = path, Arguments = args }],
    });

    [Fact]
    public void WalkFolder_UnreadableTask_IsReported_AndTheOthersStayListed()
    {
        var folder = new FakeFolder();
        folder.Tasks.Add(new FakeTask(null));
        folder.Tasks.Add(StartupTask(9, "\"C:\\a b\\x.exe\"", "-s"));
        var items = new List<StartupEntry>();
        var errors = new List<string>();

        StartupInventory.WalkFolder(folder, items, errors);

        StartupEntry entry = Assert.Single(items);
        Assert.Equal("\"C:\\a b\\x.exe\" -s", entry.Command);
        Assert.Equal(@"C:\a b\x.exe", entry.ExecutablePath);
        Assert.Single(errors);
    }

    [Fact]
    public void WalkFolder_FailedTaskListing_DoesNotSkipSubfolders()
    {
        var folder = new FakeFolder { TasksThrow = true };
        var sub = new FakeFolder { Path = @"\sub" };
        sub.Tasks.Add(StartupTask(8, @"C:\boot.exe", null));
        folder.Folders.Add(sub);
        var items = new List<StartupEntry>();
        var errors = new List<string>();

        StartupInventory.WalkFolder(folder, items, errors);

        Assert.Equal("\"C:\\boot.exe\"", Assert.Single(items).Command);
        Assert.Single(errors);
    }

    [Fact]
    public void WalkFolder_TimeTriggerOnly_IsNotAStartupEntry()
    {
        var folder = new FakeFolder();
        folder.Tasks.Add(StartupTask(1, @"C:\later.exe", null));
        var items = new List<StartupEntry>();
        var errors = new List<string>();

        StartupInventory.WalkFolder(folder, items, errors);

        Assert.Empty(items);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x10 }, null)]
    [InlineData(new byte[0], null)]
    public void StartupApproved_KnownFormatsOnly(byte[] value, bool? expected) =>
        Assert.Equal(expected, StartupInventory.ParseApproved(value));

    [Fact]
    public void StartupApproved_NonBinary_IsUnknown() => Assert.Null(StartupInventory.ParseApproved("2"));

    [Fact]
    public void Csv_QuotesSpecialFields_AndLeavesUnknownEmpty()
    {
        var w = new StringWriter();

        int rows = CsvTable.Write(w, ["a", "b", "c"], [["x,y", "say \"hi\"", null], ["line\nbreak", "plain", "0"]]);

        Assert.Equal(2, rows);
        Assert.Equal("a,b,c\r\n\"x,y\",\"say \"\"hi\"\"\",\r\n\"line\nbreak\",plain,0\r\n", w.ToString());
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+cmd|' /C calc'!A0", "\"'+cmd|' /C calc'!A0\"")]
    [InlineData("-2+3", "\"'-2+3\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("\tx", "\"'\tx\"")]
    [InlineData("-12.5", "-12.5")]
    [InlineData("+3", "+3")]
    [InlineData("a=b", "a=b")]
    public void Csv_NeutralizesFormulaLikeText_ButNotNumbers(string field, string expected)
    {
        var w = new StringWriter();

        CsvTable.Write(w, ["a"], [[field]]);

        Assert.Equal("a\r\n" + expected + "\r\n", w.ToString());
    }

    [Fact]
    public void Csv_RowWidthMismatch_Throws()
    {
        Assert.Throws<ArgumentException>(() => CsvTable.Write(new StringWriter(), ["a", "b"], [["only one"]]));
    }

    [Fact]
    public void StartupExport_UnknownValuesStayEmpty()
    {
        var w = new StringWriter();
        StartupViewModel.WriteStartup(w, [new StartupEntry(StartupSource.RunKey, "HKCU", "x", "x.exe", "HKEY_CURRENT_USER\\Run", "x.exe", null, null, null, "")]);

        Assert.EndsWith("RunKey,HKCU,x,x.exe,HKEY_CURRENT_USER\\Run,x.exe,,,,\r\n", w.ToString());
    }

    [Fact]
    [Trait("Category", "Machine")]
    public void Services_OnThisMachine_ListsTheEventLogService()
    {
        InventoryResult<ServiceEntry> r = ServiceInventory.Read();

        Assert.Empty(r.Errors);
        ServiceEntry eventLog = r.Items.Single(s => s.Name.Equals("EventLog", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Running", eventLog.State);
        Assert.Equal(ServiceStartType.Automatic, eventLog.StartType);
        Assert.NotNull(eventLog.ProcessId);
    }

    [Fact]
    [Trait("Category", "Machine")]
    public void Startup_OnThisMachine_ReadsWithoutThrowing_AndReportsErrorsExplicitly()
    {
        InventoryResult<StartupEntry> r = StartupInventory.Read();

        Assert.NotNull(r.Errors);
        Assert.All(r.Items, e => Assert.False(string.IsNullOrEmpty(e.Location)));
    }

    [Fact]
    public async Task ViewModel_PartialInventory_SaysSo()
    {
        var vm = new StartupViewModel(
            readStartup: () => new InventoryResult<StartupEntry>([new StartupEntry(StartupSource.RunKey, "HKCU", "a", "a.exe", "k", "a.exe", false, null, true, "")], ["HKLM: access denied"]),
            readServices: () => new InventoryResult<ServiceEntry>([], []));

        await vm.RefreshAsync();

        Assert.Single(vm.Startup);
        Assert.Contains("the inventory is partial", vm.Status);
        Assert.Equal("HKLM: access denied", Assert.Single(vm.Errors));

        vm.Filter = "zzz";
        Assert.Empty(vm.Startup);
    }
}
