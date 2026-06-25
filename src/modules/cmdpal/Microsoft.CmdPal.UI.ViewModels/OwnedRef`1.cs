// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;

namespace Microsoft.CmdPal.UI.ViewModels;

/// <summary>
/// A lightweight slot that owns a single <see cref="ExtensionObjectViewModel"/> and
/// automatically calls <see cref="ExtensionObjectViewModel.SafeCleanup"/> on the
/// previous value whenever the stored reference is replaced or explicitly cleared.
/// </summary>
/// <remarks>
/// This can optionally transfer a <see cref="INotifyPropertyChanged.PropertyChanged"/> subscription
/// from the old instance to the new one, so replacement stays leak-safe and event-safe.
/// </remarks>
/// <typeparam name="T">The concrete <see cref="ExtensionObjectViewModel"/> subtype to track.</typeparam>
internal struct OwnedRef<T>
    where T : ExtensionObjectViewModel
{
    private readonly PropertyChangedEventHandler? _propertyChangedHandler;
    private T? _value;

    /// <summary>Gets the current value.</summary>
    public readonly T? Value => _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="OwnedRef{T}"/> struct.
    /// Initializes the slot with optional <paramref name="initial"/> value and optional
    /// <paramref name="propertyChangedHandler"/> that should be moved on replacement.
    /// </summary>
    public OwnedRef(T? initial = null, PropertyChangedEventHandler? propertyChangedHandler = null)
    {
        _value = initial;
        _propertyChangedHandler = propertyChangedHandler;
        if (_value is not null && _propertyChangedHandler is not null)
        {
            _value.PropertyChanged += _propertyChangedHandler;
        }
    }

    /// <summary>Implicit read — lets callers use a <see cref="OwnedRef{T}"/> where a <typeparamref name="T"/>? is expected.</summary>
    public static implicit operator T?(OwnedRef<T> r) => r._value;

    /// <summary>
    /// Replaces the stored value with <paramref name="newValue"/>, transfers PropertyChanged
    /// subscription when configured, and calls <see cref="ExtensionObjectViewModel.SafeCleanup"/>
    /// on the previous value.
    /// </summary>
    public void Set(T? newValue)
    {
        if (ReferenceEquals(_value, newValue))
        {
            return;
        }

        if (_value is not null && _propertyChangedHandler is not null)
        {
            _value.PropertyChanged -= _propertyChangedHandler;
        }

        var old = _value;
        _value = newValue;

        if (_value is not null && _propertyChangedHandler is not null)
        {
            _value.PropertyChanged += _propertyChangedHandler;
        }

        old?.SafeCleanup();
    }

    /// <summary>Clears the stored value and calls <see cref="ExtensionObjectViewModel.SafeCleanup"/> on it.</summary>
    public void Clear()
    {
        Set(null);
    }
}
