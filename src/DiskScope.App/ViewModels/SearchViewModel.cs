using System.Collections.ObjectModel;
using System.Globalization;
using DiskScope.Core;

namespace DiskScope.App.ViewModels;

public sealed class SearchViewModel : ObservableObject
{
    public const int MaxResults = 1000;

    private const int DebounceMilliseconds = 200;
    private const long BytesPerMegabyte = 1024 * 1024;
    private static readonly char[] ExtensionSeparators = [',', ';', ' ', '\t'];

    private readonly Func<NodeIndex?> _indexProvider;
    private readonly Func<SizeKind> _sizeKindProvider;
    private CancellationTokenSource? _cts;
    private string _nameText = string.Empty;
    private string _extensionsText = string.Empty;
    private string _minSizeMbText = string.Empty;
    private string _maxSizeMbText = string.Empty;
    private DateTime? _modifiedFrom;
    private DateTime? _modifiedTo;
    private KindFilter _kind = KindFilter.Any;
    private string? _inputError;
    private string _resultText = string.Empty;
    private ObservableCollection<FileRowViewModel> _results = new();

    public SearchViewModel(Func<NodeIndex?> indexProvider, Func<SizeKind> sizeKindProvider)
    {
        _indexProvider = indexProvider;
        _sizeKindProvider = sizeKindProvider;
    }

    public IReadOnlyList<KindFilter> KindOptions { get; } = Enum.GetValues<KindFilter>();

    public string NameText
    {
        get => _nameText;
        set => SetInput(ref _nameText, value, nameof(NameText));
    }

    public string ExtensionsText
    {
        get => _extensionsText;
        set => SetInput(ref _extensionsText, value, nameof(ExtensionsText));
    }

    public string MinSizeMbText
    {
        get => _minSizeMbText;
        set => SetInput(ref _minSizeMbText, value, nameof(MinSizeMbText));
    }

    public string MaxSizeMbText
    {
        get => _maxSizeMbText;
        set => SetInput(ref _maxSizeMbText, value, nameof(MaxSizeMbText));
    }

    public DateTime? ModifiedFrom
    {
        get => _modifiedFrom;
        set => SetInput(ref _modifiedFrom, value, nameof(ModifiedFrom));
    }

    public DateTime? ModifiedTo
    {
        get => _modifiedTo;
        set => SetInput(ref _modifiedTo, value, nameof(ModifiedTo));
    }

    public KindFilter Kind
    {
        get => _kind;
        set => SetInput(ref _kind, value, nameof(Kind));
    }

    public ObservableCollection<FileRowViewModel> Results
    {
        get => _results;
        private set => SetProperty(ref _results, value);
    }

    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    public string? InputError
    {
        get => _inputError;
        private set => SetProperty(ref _inputError, value);
    }

    /// <summary>Completes when the search scheduled by the latest input change has finished (tests await it).</summary>
    public Task? PendingSearch { get; private set; }

    public NodeFilter? BuildFilter()
    {
        long? minSize = null;
        long? maxSize = null;
        string? error = null;
        if (!TryParseMegabytes(MinSizeMbText, "Min size", out minSize, out error)
            || !TryParseMegabytes(MaxSizeMbText, "Max size", out maxSize, out error))
        {
            InputError = error;
            return null;
        }

        string[] extensions = ExtensionsText.Split(
            ExtensionSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string name = NameText.Trim();
        InputError = null;

        return new NodeFilter
        {
            NameContains = name.Length == 0 ? null : name,
            Extensions = extensions.Length == 0 ? null : extensions,
            MinSize = minSize,
            MaxSize = maxSize,
            ModifiedFromUtc = ModifiedFrom is { } fromDay ? StartOfDayUtc(fromDay) : null,
            ModifiedToUtc = ModifiedTo is { } toDay ? EndOfDayUtc(toDay) : null,
            Kind = Kind,
            SizeKind = _sizeKindProvider(),
        };
    }

    public async Task RefreshAsync()
    {
        CancellationToken token = BeginRun();

        if (BuildFilter() is not { } filter)
        {
            ClearResults(string.Empty);
            return;
        }

        if (_indexProvider() is not { } index)
        {
            ClearResults("No scan loaded");
            return;
        }

        if (filter.IsEmpty)
        {
            ClearResults("Enter criteria");
            return;
        }

        SearchResult result;
        try
        {
            result = await Task.Run(() => NodeSearch.Find(index, filter, MaxResults, token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        SizeKind kind = filter.SizeKind;
        Results = new ObservableCollection<FileRowViewModel>(
            result.Ids.Select(id => MainViewModel.ToFileRow(index, id, kind)));
        ResultText = FormatResultText(result);
    }

    private void SetInput<T>(ref T field, T value, string name)
    {
        if (SetProperty(ref field, value, name))
        {
            PendingSearch = ScheduleAsync();
        }
    }

    private async Task ScheduleAsync()
    {
        CancellationToken token = BeginRun();
        try
        {
            await Task.Delay(DebounceMilliseconds, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RefreshAsync();
    }

    // Cancels the previous run (search or debounce) so only the latest input can publish results.
    private CancellationToken BeginRun()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    private void ClearResults(string text)
    {
        Results = new ObservableCollection<FileRowViewModel>();
        ResultText = text;
    }

    private static bool TryParseMegabytes(string text, string label, out long? bytes, out string? error)
    {
        bytes = null;
        error = null;
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal megabytes)
            && !decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out megabytes))
        {
            error = $"{label}: not a number";
            return false;
        }

        if (megabytes < 0)
        {
            error = $"{label}: must not be negative";
            return false;
        }

        if (megabytes > long.MaxValue / BytesPerMegabyte)
        {
            error = $"{label}: too large";
            return false;
        }

        bytes = (long)(megabytes * BytesPerMegabyte);
        return true;
    }

    private static DateTime StartOfDayUtc(DateTime day) =>
        DateTime.SpecifyKind(day.Date, DateTimeKind.Local).ToUniversalTime();

    private static DateTime EndOfDayUtc(DateTime day) =>
        DateTime.SpecifyKind(day.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local).ToUniversalTime();

    private static string FormatResultText(SearchResult result)
    {
        string text = FormattableString.Invariant($"{result.TotalMatches:N0} matches");
        if (result.Truncated)
        {
            text += FormattableString.Invariant($", showing first {MaxResults}");
        }

        if (result.UnknownDateExcluded > 0)
        {
            text += FormattableString.Invariant($"; {result.UnknownDateExcluded:N0} with unknown date excluded");
        }

        return text;
    }
}
