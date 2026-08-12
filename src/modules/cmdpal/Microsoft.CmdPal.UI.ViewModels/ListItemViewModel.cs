// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.CmdPal.UI.ViewModels.Commands;
using Microsoft.CmdPal.UI.ViewModels.Models;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace Microsoft.CmdPal.UI.ViewModels;

public partial class ListItemViewModel : CommandItemViewModel
{
    private const int MaxVisibleTags = 3;
    private const int InitializationNotStarted = 0;
    private const int InitializationInProgress = 1;
    private const int InitializationSucceeded = 2;
    private const int InitializationFailed = 3;

    private int _initializationState;
    private TaskCompletionSource<bool>? _initializationCompletion;
    private ListItemInitializationCoordinator? _initializationCoordinator;
    private long _realizationToken;

    public new ExtensionObject<IListItem> Model { get; }

    public List<TagViewModel>? Tags { get; set; }

    // Remember - "observable" properties from the model (via PropChanged)
    // cannot be marked [ObservableProperty]
    public bool HasTags => (Tags?.Count ?? 0) > 0;

    public List<TagViewModel>? VisibleTags { get; private set; }

    private TagViewModel? _overflowTag;
    private PendingTagsUpdate? _pendingTagsUpdate;

    public string TextToSuggest { get; private set; } = string.Empty;

    public string Section { get; private set; } = string.Empty;

    public ListItemType Type { get; private set; }

    public bool IsInteractive => Type == ListItemType.Item;

    public DetailsViewModel? Details { get; private set; }

    [MemberNotNullWhen(true, nameof(Details))]
    public bool HasDetails => Details is not null;

    public string AccessibleName { get; private set; } = string.Empty;

    public bool ShowTitle { get; private set; }

    public bool ShowSubtitle { get; private set; }

    public bool LayoutShowsTitle
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UpdateShowsTitle();
            }
        }
    }

    public bool LayoutShowsSubtitle
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UpdateShowsSubtitle();
            }
        }
    }

    public ListItemViewModel(IListItem model, WeakReference<IPageContext> context, IContextMenuFactory contextMenuFactory)
        : base(new(model), context, contextMenuFactory)
    {
        Model = new ExtensionObject<IListItem>(model);
    }

    internal bool IsInitializationComplete => Volatile.Read(ref _initializationState) >= InitializationSucceeded;

    internal bool InitializationWasSuccessful => Volatile.Read(ref _initializationState) == InitializationSucceeded;

    internal void AttachInitializationCoordinator(ListItemInitializationCoordinator coordinator)
    {
        Volatile.Write(ref _initializationCoordinator, coordinator);
        Interlocked.Exchange(ref _realizationToken, 0);
    }

    internal bool IsAttachedTo(ListItemInitializationCoordinator coordinator) =>
        ReferenceEquals(Volatile.Read(ref _initializationCoordinator), coordinator);

    public ListItemRealizationRegistration BeginRealization()
    {
        var coordinator = Volatile.Read(ref _initializationCoordinator);
        return coordinator?.BeginRealization(this) ?? default;
    }

    internal bool TrySetRealization(ListItemInitializationCoordinator coordinator, long token)
    {
        if (!IsAttachedTo(coordinator))
        {
            return false;
        }

        Interlocked.Exchange(ref _realizationToken, token);
        if (IsAttachedTo(coordinator))
        {
            return true;
        }

        Interlocked.CompareExchange(ref _realizationToken, 0, token);
        return false;
    }

    internal bool IsCurrentRealization(ListItemInitializationCoordinator coordinator, long token) =>
        IsAttachedTo(coordinator) &&
        Volatile.Read(ref _realizationToken) == token;

    internal void EndRealization(ListItemInitializationCoordinator coordinator, long token)
    {
        if (IsAttachedTo(coordinator))
        {
            Interlocked.CompareExchange(ref _realizationToken, 0, token);
        }
    }

    internal void InitializePropertiesOnce()
    {
        if (Interlocked.CompareExchange(ref _initializationState, InitializationInProgress, InitializationNotStarted) != InitializationNotStarted)
        {
            return;
        }

        var succeeded = false;
        try
        {
            succeeded = SafeInitializeProperties();
        }
        finally
        {
            Volatile.Write(ref _initializationState, succeeded ? InitializationSucceeded : InitializationFailed);
            Volatile.Read(ref _initializationCompletion)?.TrySetResult(succeeded);
        }
    }

    internal Task<bool> WaitForInitializationAsync(CancellationToken cancellationToken)
    {
        var state = Volatile.Read(ref _initializationState);
        if (state >= InitializationSucceeded)
        {
            return Task.FromResult(state == InitializationSucceeded);
        }

        var newCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = Interlocked.CompareExchange(ref _initializationCompletion, newCompletion, null) ?? newCompletion;

        // Initialization may have completed between the first state read and
        // publishing the completion source. Complete it here as the finding
        // thread so no waiter can be stranded by that race.
        state = Volatile.Read(ref _initializationState);
        if (state >= InitializationSucceeded)
        {
            completion.TrySetResult(state == InitializationSucceeded);
        }

        return cancellationToken.CanBeCanceled
            ? completion.Task.WaitAsync(cancellationToken)
            : completion.Task;
    }

    public override void InitializeProperties()
    {
        if (IsInitialized)
        {
            return;
        }

        // This sets IsInitialized = true
        base.InitializeProperties();

        var li = Model.Unsafe;
        if (li is null)
        {
            return; // throw?
        }

        UpdateTags(li.Tags);
        Section = li.Section ?? string.Empty;
        Type = EvaluateType();
        UpdateProperty(nameof(Section), nameof(Type), nameof(IsInteractive));

        UpdateAccessibleName();
    }

    private ListItemType EvaluateType()
    {
        return Command.IsSet
            ? ListItemType.Item
            : string.IsNullOrEmpty(Section) ? ListItemType.Separator : ListItemType.SectionHeader;
    }

    public override void SlowInitializeProperties()
    {
        base.SlowInitializeProperties();
        var model = Model.Unsafe;
        if (model is null)
        {
            return;
        }

        var extensionDetails = model.Details;
        if (extensionDetails is not null)
        {
            Details = new(extensionDetails, PageContext);
            Details.InitializeProperties();
            UpdateProperty(nameof(Details), nameof(HasDetails));
        }

        AddShowDetailsCommands();

        TextToSuggest = model.TextToSuggest;
        UpdateProperty(nameof(TextToSuggest));
    }

    protected override void FetchProperty(string propertyName)
    {
        base.FetchProperty(propertyName);

        var model = this.Model.Unsafe;
        if (model is null)
        {
            return; // throw?
        }

        switch (propertyName)
        {
            case nameof(model.Tags):
                UpdateTags(model.Tags);
                break;
            case nameof(model.TextToSuggest):
                TextToSuggest = model.TextToSuggest ?? string.Empty;
                UpdateProperty(nameof(TextToSuggest));
                break;
            case nameof(model.Section):
                Section = model.Section ?? string.Empty;
                Type = EvaluateType();
                UpdateProperty(nameof(Section), nameof(Type), nameof(IsInteractive));
                break;
            case nameof(model.Command):
                Type = EvaluateType();
                UpdateProperty(nameof(Type), nameof(IsInteractive));
                break;
            case nameof(Details):
                var existingReference = Details;
                var extensionDetails = model.Details;
                Details = extensionDetails is not null ? new(extensionDetails, PageContext) : null;
                Details?.InitializeProperties();
                UpdateProperty(nameof(Details), nameof(HasDetails));
                UpdateShowDetailsCommand();
                existingReference?.SafeCleanup();
                break;
            case nameof(model.MoreCommands):
                AddShowDetailsCommands();
                break;
            case nameof(model.Title):
                UpdateProperty(nameof(Title));
                UpdateShowsTitle();
                UpdateAccessibleName();
                break;
            case nameof(model.Subtitle):
                UpdateProperty(nameof(Subtitle));
                UpdateShowsSubtitle();
                UpdateAccessibleName();
                break;
            default:
                UpdateProperty(propertyName);
                break;
        }
    }

    // TODO: Do we want filters to match descriptions and other properties? Tags, etc... Yes?
    // TODO: Do we want to save off the score here so we can sort by it in our ListViewModel?
    public override string ToString() => $"{Name} ListItemViewModel";

    public override bool Equals(object? obj) => obj is ListItemViewModel vm && vm.Model.Equals(this.Model);

    public override int GetHashCode() => Model.GetHashCode();

    private void AddShowDetailsCommands()
    {
        // If the parent page has ShowDetails = false and we have details,
        // then we should add a show details action in the context menu.
        if (HasDetails &&
            PageContext.TryGetTarget(out var pageContext) &&
            pageContext is ListViewModel listViewModel &&
            !listViewModel.ShowDetails)
        {
            var addedCommand = false;
            lock (MoreCommandsLock)
            {
                // Check if "Show Details" action already exists to prevent duplicates
                if (!UnsafeMoreCommands.Any(cmd => cmd is CommandContextItemViewModel contextItemViewModel &&
                                                  contextItemViewModel.Command.Id == ShowDetailsCommand.ShowDetailsCommandId))
                {
                    var showDetailsCommand = new ShowDetailsCommand(Details);
                    var showDetailsContextItem = new CommandContextItem(showDetailsCommand)
                    {
                        Icon = showDetailsCommand.Icon,
                    };
                    var showDetailsContextItemViewModel = new CommandContextItemViewModel(showDetailsContextItem, PageContext);
                    showDetailsContextItemViewModel.SlowInitializeProperties();
                    UnsafeMoreCommands.Add(showDetailsContextItemViewModel);
                    RefreshMoreCommandStateUnsafe();
                    addedCommand = true;
                }
            }

            if (addedCommand)
            {
                UpdateProperty(nameof(MoreCommands), nameof(AllCommands));
                UpdateProperty(nameof(SecondaryCommand), nameof(SecondaryCommandName), nameof(HasMoreCommands));
            }
        }
    }

    // This method is called when the details change to make sure we
    // have the latest details in the show details command.
    private void UpdateShowDetailsCommand()
    {
        // If the parent page has ShowDetails = false and we have details,
        // then we should add a show details action in the context menu.
        if (HasDetails &&
            PageContext.TryGetTarget(out var pageContext) &&
            pageContext is ListViewModel listViewModel &&
            !listViewModel.ShowDetails)
        {
            CommandContextItemViewModel? oldCommand = null;
            lock (MoreCommandsLock)
            {
                oldCommand = UnsafeMoreCommands
                    .OfType<CommandContextItemViewModel>()
                    .FirstOrDefault(contextItemViewModel => contextItemViewModel.Command.Id == ShowDetailsCommand.ShowDetailsCommandId);

                if (oldCommand is not null)
                {
                    UnsafeMoreCommands.Remove(oldCommand);
                }

                var showDetailsCommand = new ShowDetailsCommand(Details);
                var showDetailsContextItem = new CommandContextItem(showDetailsCommand)
                {
                    Icon = showDetailsCommand.Icon,
                };
                var showDetailsContextItemViewModel = new CommandContextItemViewModel(showDetailsContextItem, PageContext);
                showDetailsContextItemViewModel.SlowInitializeProperties();
                UnsafeMoreCommands.Add(showDetailsContextItemViewModel);
                RefreshMoreCommandStateUnsafe();
            }

            oldCommand?.SafeCleanup();

            UpdateProperty(nameof(MoreCommands), nameof(AllCommands));
            UpdateProperty(nameof(SecondaryCommand), nameof(SecondaryCommandName), nameof(HasMoreCommands));
        }
    }

    private void UpdateTags(ITag[]? newTagsFromModel)
    {
        var newTags = newTagsFromModel?.Select(t =>
        {
            var vm = new TagViewModel(t, PageContext);
            vm.InitializePropertiesBeforePublication();
            return vm;
        })
            .ToList() ?? [];

        var update = CreatePendingTagsUpdate([.. newTags]);

        // Keep list assignment UI-thread-owned, but fold it into the row's
        // existing batched notification instead of posting a dedicated STA task.
        // Concurrent model callbacks coalesce to the last completed snapshot.
        _ = Interlocked.Exchange(ref _pendingTagsUpdate, update);
        UpdateProperty(nameof(Tags), nameof(HasTags), nameof(VisibleTags));
    }

    protected override void ApplyPendingUiState()
    {
        base.ApplyPendingUiState();

        var update = Interlocked.Exchange(ref _pendingTagsUpdate, null);
        if (update is null)
        {
            return;
        }

        _overflowTag?.SafeCleanup();
        _overflowTag = update.OverflowTag;
        Tags = update.Tags;
        VisibleTags = update.VisibleTags;
    }

    private PendingTagsUpdate CreatePendingTagsUpdate(List<TagViewModel> allTags)
    {
        if (allTags.Count == 0)
        {
            return new(allTags, null, null);
        }

        if (allTags.Count <= MaxVisibleTags)
        {
            return new(allTags, [.. allTags], null);
        }

        var visible = allTags.Take(MaxVisibleTags).ToList();
        var overflowCount = allTags.Count - MaxVisibleTags;
        var hiddenTagNames = allTags.Skip(MaxVisibleTags).Select(t => t.Text);
        var overflowTag = new TagViewModel(
            new Tag($"+{overflowCount}")
            {
                ToolTip = string.Join("\n", hiddenTagNames),
            },
            PageContext);
        overflowTag.InitializePropertiesBeforePublication();
        visible.Add(overflowTag);
        return new(allTags, visible, overflowTag);
    }

    private sealed record PendingTagsUpdate(
        List<TagViewModel> Tags,
        List<TagViewModel>? VisibleTags,
        TagViewModel? OverflowTag);

    private void UpdateShowsTitle()
    {
        var oldShowTitle = ShowTitle;
        ShowTitle = LayoutShowsTitle;
        if (oldShowTitle != ShowTitle)
        {
            UpdateProperty(nameof(ShowTitle));
        }
    }

    private void UpdateShowsSubtitle()
    {
        var oldShowSubtitle = ShowSubtitle;
        ShowSubtitle = LayoutShowsSubtitle && !string.IsNullOrWhiteSpace(Subtitle);
        if (oldShowSubtitle != ShowSubtitle)
        {
            UpdateProperty(nameof(ShowSubtitle));
        }
    }

    protected override void UnsafeCleanup()
    {
        base.UnsafeCleanup();

        // Tags don't have event handlers or anything to cleanup
        Tags?.ForEach(t => t.SafeCleanup());
        _overflowTag?.SafeCleanup();
        Details?.SafeCleanup();

        var model = Model.Unsafe;
        if (model is not null)
        {
            // We don't need to revoke the PropChanged event handler here,
            // because we are just overriding CommandItem's FetchProperty and
            // piggy-backing off their PropChanged
        }
    }

    protected void UpdateAccessibleName()
    {
        AccessibleName = Title + ", " + Subtitle;
        UpdateProperty(nameof(AccessibleName));
    }
}
