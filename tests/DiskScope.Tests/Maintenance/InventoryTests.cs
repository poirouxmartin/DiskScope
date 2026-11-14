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
    public void ExtractExecutable(string command, string? expected) =>
        Assert.Equal(expected, CommandLine.ExtractExecutable(command, NoExpand));

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
