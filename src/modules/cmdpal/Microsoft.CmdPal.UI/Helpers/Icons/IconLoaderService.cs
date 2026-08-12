// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.WinUI;
using ManagedCommon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace Microsoft.CmdPal.UI.Helpers;

internal sealed partial class IconLoaderService : IIconLoaderService
{
    public static readonly Size NoResize = Size.Empty;

    private const DispatcherQueuePriority LoadingPriorityOnDispatcher = DispatcherQueuePriority.Low;
    private const int DefaultIconSize = 256;
    private const int MaxWorkerCount = 4;

    private static readonly int WorkerCount = Math.Clamp(Environment.ProcessorCount / 2, 1, MaxWorkerCount);

    private readonly IconLoadQueue _queue = new(WorkerCount);
    private readonly Task[] _workers;
    private readonly DispatcherQueue _dispatcherQueue;

    public IconLoaderService(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
        _workers = new Task[WorkerCount];

        for (var i = 0; i < WorkerCount; i++)
        {
            _workers[i] = Task.Run(ProcessQueueAsync);
        }

        _ = _queue.Completion.ContinueWith(
            static task => Logger.LogError("Icon load scheduler failed", task.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public bool TryLoadGlyph(
        string? iconString,
        string? fontFamily,
        Size iconSize,
        double scale,
        out IconSource? result)
    {
        result = null;

        // IconSource is a XAML object. If a caller ever reaches the provider away from
        // the UI thread, preserve the existing dispatcher-based path.
        if (!_dispatcherQueue.HasThreadAccess || string.IsNullOrEmpty(iconString))
        {
            return false;
        }

        try
        {
            var glyphKind = FontIconGlyphClassifier.Classify(iconString);
            if (glyphKind is FontIconGlyphKind.Invalid or FontIconGlyphKind.None)
            {
                return false;
            }

            var family = FontIconGlyphClassifier.GetFontFamily(glyphKind, fontFamily);

            var scaledSize = iconSize.IsEmpty
                ? iconSize
                : new Size(iconSize.Width * scale, iconSize.Height * scale);
            var targetSize = scaledSize.IsEmpty
                ? DefaultIconSize
                : (int)Math.Max(scaledSize.Width, scaledSize.Height);

            result = new FontIconSource
            {
                FontFamily = new FontFamily(family),
                FontSize = targetSize,
                Glyph = iconString,
            };
            return true;
        }
        catch
        {
            // The general converter has its own fallback behavior. Let it handle any
            // input that cannot be represented by this narrow glyph fast path.
            result = null;
            return false;
        }
    }

    public bool TryEnqueueLoad(
        string? iconString,
        string? fontFamily,
        IRandomAccessStreamReference? streamRef,
        Size iconSize,
        double scale,
        ElementTheme theme,
        TaskCompletionSource<IconSource?> tcs,
        IconLoadPriority priority = IconLoadPriority.Low,
        IconLoadMeasurement? diagnostics = null,
        IconLoadDemand? demand = null)
    {
        demand ??= IconLoadDemand.CreateDemanded();
        var workItem = () => LoadAndCompleteAsync(iconString, fontFamily, streamRef, iconSize, scale, theme, tcs, diagnostics);
        if (_queue.TryEnqueue(workItem, priority, demand, out var actualPriority))
        {
            diagnostics?.Enqueued(actualPriority, WorkerCount);
#if DEBUG
            if (priority == IconLoadPriority.High && actualPriority == IconLoadPriority.Low)
            {
                Logger.LogDebug("High priority icon queue full, falling back to low priority");
            }
#endif
            return true;
        }

        diagnostics?.Rejected();
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Complete();
        var tasks = new Task[_workers.Length + 1];
        _workers.CopyTo(tasks, 0);
        tasks[^1] = _queue.Completion;
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task ProcessQueueAsync()
    {
        while (await _queue.DequeueAsync().ConfigureAwait(false) is { } workItem)
        {
            try
            {
                await workItem().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to load icon", ex);
            }
        }
    }

    private async Task LoadAndCompleteAsync(
        string? iconString,
        string? fontFamily,
        IRandomAccessStreamReference? streamRef,
        Size iconSize,
        double scale,
        ElementTheme theme,
        TaskCompletionSource<IconSource?> tcs,
        IconLoadMeasurement? diagnostics)
    {
        diagnostics?.WorkerStarted(WorkerCount);

        try
        {
            var result = await LoadIconCoreAsync(iconString, fontFamily, streamRef, iconSize, scale, theme, diagnostics).ConfigureAwait(false);
            diagnostics?.Complete();
            tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            diagnostics?.Fail();
            tcs.TrySetException(ex);
        }
    }

    private async Task<IconSource?> LoadIconCoreAsync(
        string? iconString,
        string? fontFamily,
        IRandomAccessStreamReference? streamRef,
        Size iconSize,
        double scale,
        ElementTheme theme,
        IconLoadMeasurement? diagnostics)
    {
        var scaledSize = iconSize.IsEmpty
            ? iconSize
            : new Size(iconSize.Width * scale, iconSize.Height * scale);

        if (!string.IsNullOrEmpty(iconString))
        {
            var preparationStartedAt = diagnostics?.BeginBackgroundPreparation() ?? 0;
            var targetSize = scaledSize.IsEmpty
                ? DefaultIconSize
                : (int)Math.Max(scaledSize.Width, scaledSize.Height);
            IconProtocolProcessingResult? protocolResult = null;
            IconPathConverter.PreparedIcon? preparedIcon = null;

            try
            {
                if (IconProtocolRegistry.Find(iconString) is not { } protocolProcessor)
                {
                    preparedIcon = IconPathConverter.Prepare(iconString, fontFamily, targetSize, theme);
                }
                else if (!protocolProcessor.TryPrepareSynchronously(iconString, targetSize, theme, out preparedIcon))
                {
                    protocolResult = await protocolProcessor.PrepareAsync(iconString, targetSize, theme).ConfigureAwait(false);
                    if (protocolResult.BitmapStream is { } bitmapStream)
                    {
                        diagnostics?.CompleteBackgroundPreparation(preparationStartedAt);
                        return await CreateImageIconSourceAsync(bitmapStream, scaledSize, diagnostics).ConfigureAwait(false);
                    }

                    preparedIcon = protocolResult.TakePreparedIcon();
                    if (preparedIcon is null && protocolResult.FallbackIconString is { } fallbackIconString)
                    {
                        preparedIcon = IconPathConverter.Prepare(fallbackIconString, fontFamily, targetSize, theme);
                    }
                }

                preparedIcon ??= IconPathConverter.PreparedIcon.Empty();
                diagnostics?.CompleteBackgroundPreparation(preparationStartedAt);

                var dispatcherEnqueuedAt = diagnostics?.BeginDispatcherWait() ?? 0;

                // Keep the dispatcher callback synchronous for glyph and URI sources.
                // The returned ValueTask carries only binary transfer work beyond it.
                var materialization = await _dispatcherQueue
                    .EnqueueAsync(CreateIconSourceOnDispatcher, LoadingPriorityOnDispatcher)
                    .ConfigureAwait(false);
                return await materialization.ConfigureAwait(false);

                ValueTask<IconSource?> CreateIconSourceOnDispatcher()
                {
                    var dispatcherStartedAt = diagnostics?.DispatcherStarted(dispatcherEnqueuedAt) ?? 0;
                    var completionOwnedByCallback = true;
                    try
                    {
                        if (IconPathConverter.TryCreateIconSourceSynchronously(preparedIcon, out var result))
                        {
                            diagnostics?.SetResult(result);
                            return ValueTask.FromResult<IconSource?>(result);
                        }

                        var materializationInner = CompleteAsynchronousMaterializationAsync(dispatcherStartedAt);

                        // The asynchronous continuation now owns the single timing-completion notification.
                        completionOwnedByCallback = false;
                        return materializationInner;
                    }
                    finally
                    {
                        if (completionOwnedByCallback)
                        {
                            diagnostics?.DispatcherCompleted(dispatcherStartedAt);
                        }
                    }
                }

                async ValueTask<IconSource?> CompleteAsynchronousMaterializationAsync(long dispatcherStartedAt)
                {
                    try
                    {
                        var result = await IconPathConverter.CompleteIconSourceCreationAsync(preparedIcon);
                        diagnostics?.SetResult(result);
                        return result;
                    }
                    finally
                    {
                        diagnostics?.DispatcherCompleted(dispatcherStartedAt);
                    }
                }
            }
            finally
            {
                preparedIcon?.Dispose();
                protocolResult?.Dispose();
            }
        }

        if (streamRef != null)
        {
            try
            {
                var preparationStartedAt = diagnostics?.BeginBackgroundPreparation() ?? 0;
                using var bitmapStream = await streamRef.OpenReadAsync().AsTask().ConfigureAwait(false);
                diagnostics?.CompleteBackgroundPreparation(preparationStartedAt);
                return await CreateImageIconSourceAsync(bitmapStream, scaledSize, diagnostics).ConfigureAwait(false);
            }
#pragma warning disable CS0168 // Variable is declared but never used
            catch (Exception ex)
#pragma warning restore CS0168 // Variable is declared but never used
            {
#if DEBUG
                Logger.LogDebug($"Failed to open icon stream: {ex}");
#endif
                return null;
            }
        }

        return null;
    }

    private async Task<IconSource?> CreateImageIconSourceAsync(
        IRandomAccessStream bitmapStream,
        Size scaledSize,
        IconLoadMeasurement? diagnostics)
    {
        var dispatcherEnqueuedAt = diagnostics?.BeginDispatcherWait() ?? 0;
        return await _dispatcherQueue
            .EnqueueAsync(BuildImageSource, LoadingPriorityOnDispatcher)
            .ConfigureAwait(false);

        async Task<IconSource?> BuildImageSource()
        {
            var dispatcherStartedAt = diagnostics?.DispatcherStarted(dispatcherEnqueuedAt) ?? 0;
            try
            {
                var bitmap = new BitmapImage();
                ApplyDecodeSize(bitmap, scaledSize);
                await bitmap.SetSourceAsync(bitmapStream);
                var result = new ImageIconSource { ImageSource = bitmap };
                diagnostics?.SetResult(result);
                return result;
            }
            finally
            {
                diagnostics?.DispatcherCompleted(dispatcherStartedAt);
            }
        }
    }

    private static void ApplyDecodeSize(BitmapImage bitmap, Size size)
    {
        if (size.IsEmpty)
        {
            return;
        }

        if (size.Width >= size.Height)
        {
            bitmap.DecodePixelWidth = (int)size.Width;
        }
        else
        {
            bitmap.DecodePixelHeight = (int)size.Height;
        }
    }
}
