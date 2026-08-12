// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace Microsoft.CmdPal.IconBenchmark;

internal sealed class IconBenchmarkOptions
{
    private const string EnvironmentPrefix = "CMDPAL_ICON_BENCHMARK_";

    public string Scenario { get; }

    public string ProductPath { get; }

    public string OutputPath { get; }

    public IReadOnlyList<string> Pages { get; }

    public IReadOnlyList<int> KeyboardTraversalTimeoutMilliseconds { get; }

    public IReadOnlyList<int> KeyboardWrapCounts { get; }

    public int KeyboardTapDelayMilliseconds { get; }

    public int KeyboardCoarseProbeInterval { get; }

    public int KeyboardFineProbeInterval { get; }

    public IReadOnlyList<int> FastScrollCounts { get; }

    public IReadOnlyList<int> SlowScrollCounts { get; }

    public int NavigationDelayMilliseconds { get; }

    public int SettleMilliseconds { get; }

    public int FastScrollDelayMilliseconds { get; }

    public int SlowScrollDelayMilliseconds { get; }

    public bool UsesKeyboard => string.Equals(Scenario, "A", StringComparison.OrdinalIgnoreCase);

    private IconBenchmarkOptions(
        string scenario,
        string productPath,
        string outputPath,
        IReadOnlyList<string> pages,
        IReadOnlyList<int> keyboardTraversalTimeoutMilliseconds,
        IReadOnlyList<int> keyboardWrapCounts,
        int keyboardTapDelayMilliseconds,
        int keyboardCoarseProbeInterval,
        int keyboardFineProbeInterval,
        IReadOnlyList<int> fastScrollCounts,
        IReadOnlyList<int> slowScrollCounts,
        int navigationDelayMilliseconds,
        int settleMilliseconds,
        int fastScrollDelayMilliseconds,
        int slowScrollDelayMilliseconds)
    {
        Scenario = scenario;
        ProductPath = productPath;
        OutputPath = outputPath;
        Pages = pages;
        KeyboardTraversalTimeoutMilliseconds = keyboardTraversalTimeoutMilliseconds;
        KeyboardWrapCounts = keyboardWrapCounts;
        KeyboardTapDelayMilliseconds = keyboardTapDelayMilliseconds;
        KeyboardCoarseProbeInterval = keyboardCoarseProbeInterval;
        KeyboardFineProbeInterval = keyboardFineProbeInterval;
        FastScrollCounts = fastScrollCounts;
        SlowScrollCounts = slowScrollCounts;
        NavigationDelayMilliseconds = navigationDelayMilliseconds;
        SettleMilliseconds = settleMilliseconds;
        FastScrollDelayMilliseconds = fastScrollDelayMilliseconds;
        SlowScrollDelayMilliseconds = slowScrollDelayMilliseconds;
    }

    public static IconBenchmarkOptions FromEnvironment()
    {
        var scenario = GetRequiredEnvironmentVariable("SCENARIO").ToUpperInvariant();
        if (scenario is not ("A" or "B"))
        {
            throw new InvalidOperationException($"{EnvironmentPrefix}SCENARIO must be A or B.");
        }

        var productPath = ResolveRequiredPath("PRODUCT");
        var outputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(GetRequiredEnvironmentVariable("OUTPUT")));
        var pages = ParseStrings(GetEnvironmentVariable("PAGES") ?? "All apps;Segoe icons");
        var keyboardTimeoutDefaults = pages.Select(GetDefaultKeyboardTraversalTimeoutMilliseconds).ToArray();
        var keyboardWrapDefaults = pages.Select(_ => 2).ToArray();
        var fastScrollDefaults = pages.Select(GetDefaultFastScrollCount).ToArray();
        var slowScrollDefaults = pages.Select(GetDefaultSlowScrollCount).ToArray();

        return new IconBenchmarkOptions(
            scenario,
            productPath,
            outputPath,
            pages,
            ParsePositiveIntegers("KEYBOARD_TIMEOUT_MS", keyboardTimeoutDefaults, pages.Count),
            ParsePositiveIntegers("KEYBOARD_WRAP_COUNTS", keyboardWrapDefaults, pages.Count),
            ParseInteger("KEYBOARD_TAP_DELAY_MS", 0),
            ParsePositiveInteger("KEYBOARD_COARSE_PROBE_INTERVAL", 250),
            ParsePositiveInteger("KEYBOARD_FINE_PROBE_INTERVAL", 10),
            ParseIntegers("FAST_SCROLL_COUNTS", fastScrollDefaults, pages.Count),
            ParseIntegers("SLOW_SCROLL_COUNTS", slowScrollDefaults, pages.Count),
            ParseInteger("NAVIGATION_DELAY_MS", 1_500),
            ParseInteger("SETTLE_MS", 1_500),
            ParseInteger("FAST_SCROLL_DELAY_MS", 5),
            ParseInteger("SLOW_SCROLL_DELAY_MS", 75));
    }

    private static string ResolveRequiredPath(string suffix)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(GetRequiredEnvironmentVariable(suffix)));
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException($"{EnvironmentPrefix}{suffix} does not exist.", path);
    }

    private static string GetRequiredEnvironmentVariable(string suffix)
    {
        return GetEnvironmentVariable(suffix) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{EnvironmentPrefix}{suffix} must be set.");
    }

    private static string? GetEnvironmentVariable(string suffix)
    {
        return Environment.GetEnvironmentVariable(EnvironmentPrefix + suffix);
    }

    private static IReadOnlyList<string> ParseStrings(string value)
    {
        var values = value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return values.Length > 0
            ? values
            : throw new InvalidOperationException($"{EnvironmentPrefix}PAGES must contain at least one page.");
    }

    private static IReadOnlyList<int> ParseIntegers(string suffix, IReadOnlyList<int> defaults, int expectedCount)
    {
        var value = GetEnvironmentVariable(suffix);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaults;
        }

        var values = value
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => ParseNonNegativeInteger(EnvironmentPrefix + suffix, part))
            .ToArray();

        if (values.Length != expectedCount)
        {
            throw new InvalidOperationException($"{EnvironmentPrefix}{suffix} must contain one value per page ({expectedCount}).");
        }

        return values;
    }

    private static IReadOnlyList<int> ParsePositiveIntegers(string suffix, IReadOnlyList<int> defaults, int expectedCount)
    {
        var values = ParseIntegers(suffix, defaults, expectedCount);
        if (values.Any(value => value == 0))
        {
            throw new InvalidOperationException($"{EnvironmentPrefix}{suffix} values must be positive integers.");
        }

        return values;
    }

    private static int ParseInteger(string suffix, int defaultValue)
    {
        var value = GetEnvironmentVariable(suffix);
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : ParseNonNegativeInteger(EnvironmentPrefix + suffix, value);
    }

    private static int ParsePositiveInteger(string suffix, int defaultValue)
    {
        var value = ParseInteger(suffix, defaultValue);
        return value > 0
            ? value
            : throw new InvalidOperationException($"{EnvironmentPrefix}{suffix} must be a positive integer.");
    }

    private static int ParseNonNegativeInteger(string name, string value)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result >= 0
            ? result
            : throw new InvalidOperationException($"{name} must be a non-negative integer.");
    }

    private static int GetDefaultKeyboardTraversalTimeoutMilliseconds(string page)
    {
        return page.Contains("Segoe", StringComparison.OrdinalIgnoreCase) ? 240_000 : 120_000;
    }

    private static int GetDefaultFastScrollCount(string page)
    {
        return page.Contains("Segoe", StringComparison.OrdinalIgnoreCase) ? 240 : 80;
    }

    private static int GetDefaultSlowScrollCount(string page)
    {
        return page.Contains("Segoe", StringComparison.OrdinalIgnoreCase) ? 240 : 80;
    }
}
