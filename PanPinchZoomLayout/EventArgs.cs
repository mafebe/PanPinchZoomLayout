using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace BNDK.Maui;

public class EventArgsCancellable : EventArgs
{
    // ReSharper disable once PropertyCanBeMadeInitOnly.Global
    public bool Cancel { get; set; }
}

[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class TappedEventArgs : EventArgs
{
    private readonly Microsoft.Maui.Controls.TappedEventArgs? _eventArgs;

    internal TappedEventArgs(Microsoft.Maui.Controls.TappedEventArgs? orgEventArgs, Element? relativeTo)
    {
        _eventArgs = orgEventArgs;
        TapPosition = _eventArgs?.GetPosition(relativeTo);
        Consumed = false;
    }
    
    public bool IsDoubleTap { get; init; }
    
    public Point? TapPosition { get; }

    public bool Consumed { get; set; }

    public object? Parameter => _eventArgs?.Parameter;
    
    public Point? GetPosition(Element? relativeTo) => _eventArgs?.GetPosition(relativeTo);
}