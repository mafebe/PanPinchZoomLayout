using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using BNDK.Maui.Utils;

namespace BNDK.Maui.Layouts;

/// <summary>
/// Enum to restrict the scaling along the axes.
/// </summary>
public enum ScaleDirection
{
    /// <summary>
    /// Scales the content to fit the ViewBox, respecting the <see cref="Microsoft.Maui.Controls.Stretch"/> mode.
    /// </summary>
    Both,

    /// <summary>
    /// Scales the content upwards when it is smaller than the ViewBox.<br/>No downward scaling.
    /// </summary>
    Up,

    /// <summary>
    /// Scales the content downwards when it is larger than the ViewBox.<br/>No upward scaling.
    /// </summary>
    Down,
}

[ContentProperty(nameof(Content))]
public class ViewBox : Layout
{
    // ReSharper disable once MemberCanBePrivate.Global
    public static readonly BindableProperty StretchProperty = BindableProperty.Create(nameof(Stretch), typeof(Stretch), typeof(ViewBox), Stretch.Uniform, propertyChanged: StretchPropertyChanged);

    // ReSharper disable once MemberCanBePrivate.Global
    public static readonly BindableProperty ScaleDirectionProperty = BindableProperty.Create(nameof(ScaleDirection), typeof(ScaleDirection), typeof(ViewBox), ScaleDirection.Both, propertyChanged: StretchPropertyChanged);

    /// <summary>
    /// Bindable property for <see cref="P:Microsoft.Maui.Controls.ContentView.Content" />.
    /// </summary>
    // ReSharper disable once MemberCanBePrivate.Global
    public static readonly BindableProperty ContentProperty = BindableProperty.Create(nameof(Content), typeof(View), typeof(ViewBox), propertyChanged: OnContentChanged);

    private static void StretchPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not ViewBox self)
            return;

        self.InvalidateMeasure();
    }

    private static void OnContentChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not ViewBox self || newValue is not IView newView)
            return;

        while (self.Children.Count > 0)
            self.RemoveAt(self.Children.Count - 1);
        self.Add(newView);
    }

    /// <summary>
    /// Gets or sets the content of the ViewBox.
    /// </summary>
    /// <value>A <see cref="T:Microsoft.Maui.Controls.View" /> that contains the content.</value>
    /// <remarks>
    /// </remarks>
    public View? Content
    {
        get => GetValue(ContentProperty) as View;
        set => SetValue(ContentProperty, value);
    }

    public virtual Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public virtual ScaleDirection ScaleDirection
    {
        get => (ScaleDirection)GetValue(ScaleDirectionProperty);
        set => SetValue(ScaleDirectionProperty, value);
    }

    protected override void OnChildAdded(Element child)
    {
        if (child is not View view)
            throw new ArgumentException("Child must be of type View", nameof(child));

        view.PropertyChanged += ViewPropertyChanged;
        base.OnChildAdded(child);
    }

    protected override void OnChildRemoved(Element child, int oldLogicalIndex)
    {
        if (child is View view)
            view.PropertyChanged -= ViewPropertyChanged;

        base.OnChildRemoved(child, oldLogicalIndex);
    }

    private void ViewPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not View)
            return;

        if (e.PropertyName == nameof(VerticalOptions) || e.PropertyName == nameof(HorizontalOptions))
            InvalidateMeasure();
    }

    protected override ILayoutManager CreateLayoutManager()
    {
        return new ViewBoxLayoutManager(this);
    }
}

public class ViewBoxLayoutManager : ILayoutManager
{
    private readonly ViewBox _viewBox;

    private View? InternalChild => _viewBox.Content;

    public ViewBoxLayoutManager(ViewBox viewBox)
    {
        _viewBox = viewBox;
    }

    public Size Measure(double widthConstraint, double heightConstraint)
    {
        Size parentSize = new();
        Size constraint = new(widthConstraint, heightConstraint);
        var child = InternalChild;

        try
        {
            // The "natural" size for the child is required. So the absence of constraints is required.
            // Remark: A constraint *can* be enforced on a child by using e.g. Height/Width-properties 
            if (child is not null)
            {
                child.Measure(double.PositiveInfinity, double.PositiveInfinity);
                var childSize = child.DesiredSize;
                var scaleFactor = ComputeScaleFactor(constraint, childSize, _viewBox.Stretch, _viewBox.ScaleDirection);
                // set the real or scaled size to the parent
                parentSize.Width = scaleFactor.Width * childSize.Width;
                parentSize.Height = scaleFactor.Height * childSize.Height;
            }
        }
        catch (Exception e)
        {
            System.Diagnostics.Trace.TraceError(e.Message);
        }

        return parentSize;
    }

    public Size ArrangeChildren(Rect bounds)
    {
        var arrangeSize = bounds.Size;
        var child = InternalChild;
        if (child is null)
            return arrangeSize;

        try
        {
            var childSize = child.DesiredSize;

            // Compute scaling factors from arrange size and the measured child content size
            var scaleFactor = ComputeScaleFactor(arrangeSize, childSize, _viewBox.Stretch, _viewBox.ScaleDirection);

            // set the scaled size from the child
            arrangeSize.Width = scaleFactor.Width * childSize.Width;
            arrangeSize.Height = scaleFactor.Height * childSize.Height;

            child.AnchorX = 0;
            child.AnchorY = 0;
            child.ScaleX = scaleFactor.Width;
            child.ScaleY = scaleFactor.Height;

            var yOffset = child.VerticalOptions.Alignment switch
            {
                LayoutAlignment.Start => 0,
                LayoutAlignment.End => bounds.Height - arrangeSize.Height,
                _ => (bounds.Height - arrangeSize.Height) / 2.0,
            };
            var xOffset = child.HorizontalOptions.Alignment switch
            {
                LayoutAlignment.Start => 0,
                LayoutAlignment.End => bounds.Width - arrangeSize.Width,
                _ => (bounds.Width - arrangeSize.Width) / 2.0,
            };

            // Arrange the child to the desired size 
            var offsetPoint = new Point(xOffset, yOffset);
            var desiredBounds = new Rect(offsetPoint, child.DesiredSize);
            (child as IView).Arrange(desiredBounds);
        }
        catch (Exception e)
        {
            System.Diagnostics.Trace.TraceError(e.Message);
        }

        return arrangeSize;
    }

    /// <summary>
    /// A helper function to computes scale factors
    /// </summary>
    /// <param name="availableContentSize">Size into which the content is being fitted.</param>
    /// <param name="unconstrainedContentSize">Natively measured size of the content.</param>
    /// <param name="stretch">Value of the Stretch property of the element.</param>
    /// <param name="scaleDirection">Value of the ScaleDirection property of the element.</param>
    private static Size ComputeScaleFactor(Size availableContentSize, Size unconstrainedContentSize, Stretch stretch, ScaleDirection scaleDirection)
    {
        var scaleX = 1.0;
        var scaleY = 1.0;

        try
        {
            var isConstrainedWidth = !double.IsPositiveInfinity(availableContentSize.Width);
            var isConstrainedHeight = !double.IsPositiveInfinity(availableContentSize.Height);

            if ((stretch != Stretch.Uniform && stretch != Stretch.UniformToFill && stretch != Stretch.Fill) || (!isConstrainedWidth && !isConstrainedHeight))
                return new Size(1.0, 1.0);

            // Compute scaling factors for both axes
            scaleX = unconstrainedContentSize.Width.IsZero() ? 0.0 : availableContentSize.Width / unconstrainedContentSize.Width;
            scaleY = unconstrainedContentSize.Height.IsZero() ? 0.0 : availableContentSize.Height / unconstrainedContentSize.Height;

            if (!isConstrainedWidth)
            {
                scaleX = scaleY;
            }
            else if (!isConstrainedHeight)
            {
                scaleY = scaleX;
            }
            else
            {
                switch (stretch)
                {
                    case Stretch.Uniform:
                        // Find minimum scale for both axes
                        var minScale = scaleX < scaleY ? scaleX : scaleY;
                        scaleX = scaleY = minScale;
                        break;

                    case Stretch.UniformToFill:
                        // Find maximum scale for both axes
                        var maxScale = scaleX > scaleY ? scaleX : scaleY;
                        scaleX = scaleY = maxScale;
                        break;

                    case Stretch.Fill:
                        // fill scale factors are already calculated
                        break;
                    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
                    case Stretch.None:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(stretch), stretch, null);
                }
            }

            // Apply scale direction by bounding scales.
            // Uniform: scaleX=scaleY, so this clamping will maintain aspect ratio
            // UniformToFill: same result.
            // Fill: change aspect ratio
            switch (scaleDirection)
            {
                case ScaleDirection.Up:
                    // only upward scaling
                    if (scaleX < 1.0)
                        scaleX = 1.0;
                    if (scaleY < 1.0)
                        scaleY = 1.0;
                    break;

                case ScaleDirection.Down:
                    // only downward scaling
                    if (scaleX > 1.0)
                        scaleX = 1.0;
                    if (scaleY > 1.0)
                        scaleY = 1.0;
                    break;

                case ScaleDirection.Both:
                    // scaling in all directions
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(scaleDirection), scaleDirection, null);
            }
        }
        catch (Exception e)
        {
            System.Diagnostics.Trace.TraceError(e.Message);
        }

        return new Size(scaleX, scaleY);
    }
}