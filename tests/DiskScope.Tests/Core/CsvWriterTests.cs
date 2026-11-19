using System.IO;
using DiskScope.Core;

namespace DiskScope.Tests.Core;

public sealed class CsvWriterTests
{
    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("@x", "\"'@x\"")]
    [InlineData("+33 6 12 34 56 78", "\"'+33 6 12 34 56 78\"")]
    [InlineData("\tx", "\"'\tx\"")]
    [InlineData("=A1&\"x\"", "\"'=A1&\"\"x\"\"\"")]
    [InlineData("-12.5", "-12.5")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("a;=cmd", "\"a;=cmd\"")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    public void WriteField_NeutralizesFormulasAndQuotesPerRfc4180(string value, string expected)
    {
        var w = new StringWriter();

        CsvWriter.WriteField(w, value);

        Assert.Equal(expected, w.ToString());
    }

    [Fact]
    public void IsFormulaLike_NumbersAreData_OtherSignedTextIsFormula()
    {
        Assert.False(CsvWriter.IsFormulaLike("-12.5"));
        Assert.False(CsvWriter.IsFormulaLike("+3"));
        Assert.False(CsvWriter.IsFormulaLike(string.Empty));
        Assert.True(CsvWriter.IsFormulaLike("-cmd"));
        Assert.True(CsvWriter.IsFormulaLike("\rx"));
    }

    [Fact]
    public void WriteRow_NullFieldIsEmpty_AndRowEndsWithCrlf()
    {
        var w = new StringWriter();

        CsvWriter.WriteRow(w, ["a", null, "=b"]);

        Assert.Equal("a,,\"'=b\"\r\n", w.ToString());
    }
}
