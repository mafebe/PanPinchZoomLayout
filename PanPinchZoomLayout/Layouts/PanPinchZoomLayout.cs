using System;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using BNDK.Maui.Utils;

namespace BNDK.Maui.Layouts;

public class PanPinchZoomLayout : ViewBox
{
    // ReSharper disable once MemberCanBePrivate.Global
    public static readonly BindableProperty GesturesEnabledProperty = BindableProperty.Create(nameof(GesturesEnabled), typeof(bool), typeof(PanPinchZoomLayout), defaultValue: true, propertyChanged: GesturesEnabledPropertyChanged);
    
    // ReSharper disable once MemberCanBePrivate.Global
    public static readonly BindableProperty PanEnabledProperty = BindableProperty.Create(nameof(PanEnabled), typeof(bool), typeof(PanPinchZoomLayout), defaultValue: true, propertyChanged: PanEnabledPropertyChanged);
    
    // ReSharper disable once MemberCanBePrivate.Global
    public static readonly BindableProperty ExtentProperty = BindableProperty.Create(nameof(Extent), typeof(RectF), typeof(PanPinchZoomLayout), defaultValue: null, propertyChanged: ExtentPropertyChanged);
    
    // ReSharper disable once MemberCanBePrivate.Global
    public static readonly BindableProperty MaxScaleProperty = BindableProperty.Create(nameof(MaxScale), typeof(float), typeof(PanPinchZoomLayout), defaultValue: 2f);

    private static void ExtentPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not PanPinchZoomLayout zoomView)
            return;

        zoomView.SetExtent(newValue as RectF?);
    }

    private static void GesturesEnabledPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not PanPinchZoomLayout zoomView)
            return;

        zoomView.UpdateGestureRecognizers();
    }

    private static void PanEnabledPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not PanPinchZoomLayout zoomView)
            return;

        zoomView.UpdatePanGestureRecognizer();
    }

    public bool GesturesEnabled
    {
        get => (bool)GetValue(GesturesEnabledProperty);
        set => SetValue(GesturesEnabledProperty, value);
    }
    
    public bool PanEnabled
    {
        get => (bool)GetValue(PanEnabledProperty);
        set => SetValue(PanEnabledProperty, value);
    }

    public float MinScale { get; set; } = 1;

    public float MaxScale
    {
        get => (float)GetValue(MaxScaleProperty); 
        set => SetValue(MaxScaleProperty, value);
    }
    
    public float DefaultScale { get; set; } = 1;
    
    private readonly PanGestureRecognizer _panGestureRecognizer;
    private readonly TapGestureRecognizer _doubleTapGestureRecognizer;
    private readonly TapGestureRecognizer _singleTapGestureRecognizer;
    private readonly PinchGestureRecognizer _pinchGestureRecognizer;
    
    private double _xOffset;
    private double _yOffset;
    
    private bool _doubleTapped;
    private bool _ignoreNextTap;
    
    private bool _panRunning;
    private double _panStartX;
    private double _panStartY;

    private bool _pinchRunning;
    private double _pinchStartScale;
    private double _pinchLastScaleChange;
    
    private bool _sizeAllocated;
    
    private RectF? _pendingExtent;

    public PanPinchZoomLayout()
    {
        _panGestureRecognizer = new PanGestureRecognizer();
        _panGestureRecognizer.PanUpdated += OnPanUpdated;

        _doubleTapGestureRecognizer = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        _doubleTapGestureRecognizer.Tapped += OnDoubleTappedEvent;
        _singleTapGestureRecognizer = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
        _singleTapGestureRecognizer.Tapped += OnSingleTappedEvent;

        _pinchGestureRecognizer = new PinchGestureRecognizer();
        _pinchGestureRecognizer.PinchUpdated += OnPinchUpdated;
        
        UpdateGestureRecognizers();
        UpdatePanGestureRecognizer();
    }

    public override Stretch Stretch
    {
        get => Stretch.Uniform;
        set => _ = value;
    }

    public RectF? Extent 
    { 
        get => GetValue(ExtentProperty) as RectF?; 
        set => SetValue(ExtentProperty, value); 
    }

    private void SetExtent(RectF? newExtent)
    {
        if (Content == null)
            return;
        
        if (!_sizeAllocated)
        {
            _pendingExtent = newExtent;
            return;
        }
        _pendingExtent = null;
        Task.Run(async () => await SetExtentAsync(newExtent));
    }

    private async Task SetExtentAsync(RectF? newExtent)
    {
        if (Content == null || !newExtent.HasValue)
            return;
        
        var extent = newExtent.Value;
        // reset scaled content to default is extent is empty
        if (extent.IsEmpty)
        {
            await ResetContentScale();
            return;
        }

        // determine required scale factor
        var width = Math.Min(extent.Width, Content.Width);
        var height = Math.Min(extent.Height, Content.Height);
        var fittingScale = CalcFittingScale(width, height, Width, Height);
        var scale = fittingScale / Math.Min(Content.ScaleX, Content.ScaleY);
        // determine translation
        var centerTranslationX = (extent.Center.X * fittingScale) - (Width / 2) + (GetUniformHorizontalSizeDiff() / 2);
        var centerTranslationY = (extent.Center.Y * fittingScale) - (Height / 2) + (GetUniformVerticalSizeDiff() / 2);
        var extentCenterOffsetX = extent.Width / 2;
        var extentCenterOffsetY = extent.Height / 2;
        var translationX = -centerTranslationX.Clamp(0 + extentCenterOffsetX, GetMaxTranslationX() - extentCenterOffsetX);
        var translationY = -centerTranslationY.Clamp(0 + extentCenterOffsetY, GetMaxTranslationY() - extentCenterOffsetY);
        // apply scale
        await ScaleContentTo(scale, translationX, translationY);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        _sizeAllocated = true;
        if (Content == null)
            return;
        // Content not yet initialized and extent has to be applied...
        if (_pendingExtent.HasValue && (Content.Width < 0 || Content.Height < 0))
        {
            Content.SizeChanged += ContentOnSizeChanged;
        }
    }

    private void ContentOnSizeChanged(object? sender, EventArgs e)
    {
        if (Content == null)
            return;
        // immediately unsubscribe sizeChange-Event
        Content.SizeChanged -= ContentOnSizeChanged;
        // check for pending extent and apply it
        if (_pendingExtent.HasValue)
            SetExtent(_pendingExtent);
    }

    private void UpdatePanGestureRecognizer()
    {
        if (GestureRecognizers.Contains(_panGestureRecognizer) && !PanEnabled)
            GestureRecognizers.Remove(_panGestureRecognizer);
        else if (!GestureRecognizers.Contains(_panGestureRecognizer) && PanEnabled)
            GestureRecognizers.Add(_panGestureRecognizer);
    }

    private void UpdateGestureRecognizers()
    {
        if (GestureRecognizers.Count > 0 && !GesturesEnabled)
        {
            GestureRecognizers.Clear();
        }
        else if (GestureRecognizers.Count == 0 && GesturesEnabled)
        {
            GestureRecognizers.Add(_panGestureRecognizer);
            GestureRecognizers.Add(_doubleTapGestureRecognizer);
            GestureRecognizers.Add(_singleTapGestureRecognizer);
            GestureRecognizers.Add(_pinchGestureRecognizer);
        }
    }
    
    private async void OnSingleTappedEvent(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
    {
        if (!IsEnabled)
            return;

        // delay for double tap
        await Task.Delay(300);
        if (_ignoreNextTap)
        {
            _ignoreNextTap = false;
            return;
        }

        if (_doubleTapped)
        {
            _doubleTapped = false;
            _ignoreNextTap = true;
            return;
        }

        var consumed = FireTapped(e, false);
        if (consumed)
            return;
        System.Diagnostics.Debug.WriteLine($"[Gesture] OnSingleTappedEvent {e.GetPosition(sender as Element)}");
    }
    
    private async void OnDoubleTappedEvent(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
    {
        if (!IsEnabled || Content is null)
            return;

        _doubleTapped = true;
        var consumed = FireTapped(e, true);
        if (consumed)
            return;
        
        System.Diagnostics.Debug.WriteLine($"[Gesture] OnDoubleTappedEvent {e.GetPosition(sender as Element)}");

        var startScale = Content.Scale;
        var maxScale = CalcMaxScale();
        // restore original view after double tap on already scaled view
        if (Content.Scale >= maxScale)
        {
            var cancelScaleReset = FireScaleChanging(startScale, DefaultScale);
            if (cancelScaleReset)
                return;
            await ResetContentScale();
            FireScaleChanged(startScale, Content.Scale);
            return;
        }

        if (_panRunning)
        {
            System.Diagnostics.Debug.WriteLine("[Gesture] Unfinished Pan detected");
            _xOffset = Content.TranslationX;
            _yOffset = Content.TranslationY;
            _panRunning = false;
        }

        // determine new scale
        var tapPosition = e.GetPosition(this) ?? new Point(Width / 2, Height / 2);
        var targetScale = Math.Min(maxScale, (float)(startScale * 2));
        
        // fire event
        var cancel = FireScaleChanging(startScale, targetScale);
        if (cancel)
            return;

        await ScaleContentTo(targetScale, tapPosition);
        
        // fire event
        FireScaleChanged(startScale, Content.Scale);
    }

    private async Task ResetContentScale()
    {
        if (Content == null)
            return;
#pragma warning disable CS4014
        Content.ScaleTo(DefaultScale, 250U, Easing.CubicInOut);
#pragma warning restore CS4014
        await Content.TranslateTo(0, 0, 250U, Easing.CubicInOut);
        _xOffset = _yOffset = 0;
    }

    private async Task ScaleContentTo(double targetScale, Point center, uint length = 250U, Easing? easing = null)
    {
        if (Content == null)
            return;

        // Calculate the transformed element pixel coordinates.
        var startScale = Content.Scale;
        var targetX = _xOffset - ((Math.Abs(_xOffset) + center.X) / startScale) * (targetScale - startScale);
        var targetY = _yOffset - ((Math.Abs(_yOffset) + center.Y) / startScale) * (targetScale - startScale);
        targetX += GetUniformHorizontalSizeDiff() / 2;
        targetY += GetUniformVerticalSizeDiff() / 2;

        // Apply translation based on the change in origin.
        var translationX = targetX.Clamp(-Content.Width * (targetScale - DefaultScale), 0);
        var translationY = targetY.Clamp(-Content.Height * (targetScale - DefaultScale), 0);

        await ScaleContentTo(targetScale, translationX, translationY, length, easing);
    }

    private async Task ScaleContentTo(double targetScale, double translationX, double translationY, uint length=250U, Easing? easing=null)
    {    
        if (Content == null)
            return;

        // set default easing method
        easing ??= Easing.CubicInOut;

        // set anchor for scaling
        Content.AnchorX = 0;
        Content.AnchorY = 0;
        
        // execute the scaling
#pragma warning disable 4014
        Content.ScaleTo(targetScale, length, easing);
#pragma warning restore 4014
        await Content.TranslateTo(translationX, translationY, length, easing);

        // save offset
        _xOffset = Content.TranslationX;
        _yOffset = Content.TranslationY;
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (!IsEnabled || Content is null || _pinchRunning)
            return;

        // if (_pinchRunning || Math.Abs(_currentScale - 1) < .1)
        //     return;

        // System.Diagnostics.Debug.WriteLine($"[Gesture] OnPanUpdated: {e.TotalX}|{e.TotalY}");
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                if (_panRunning)
                {
                    // System.Diagnostics.Debug.WriteLine($"[Gesture] Unfinished Pan detected");
                    _xOffset = Content.TranslationX;
                    _yOffset = Content.TranslationY;
                }

                _panRunning = true;
                // System.Diagnostics.Debug.WriteLine($"[Gesture] Pan Starting: startX({_startX})={e.TotalX}|startY({_startY})={e.TotalY}|AnchorX({Content.AnchorX})|AnchorX({Content.AnchorY})");
                _panStartX = e.TotalX;
                _panStartY = e.TotalY;
                
                Content.AnchorX = 0;
                Content.AnchorY = 0;
                
                System.Diagnostics.Debug.WriteLine("[Gesture] Pan Started");
                break;

            case GestureStatus.Running:
                System.Diagnostics.Debug.WriteLine($"[Gesture] Pan Running: xOffset={_xOffset}|yOffset={_yOffset}");
                var maxTranslationX = GetMaxTranslationX();
                if (maxTranslationX > 0)
                {
                    var positionOffsetX = GetUniformHorizontalSizeDiff() / 2f;
                    var translationX = Math.Min(-positionOffsetX, Math.Max(-(maxTranslationX + positionOffsetX), _xOffset + e.TotalX - _panStartX));
                    Content.TranslationX = translationX;
                }

                var maxTranslationY = GetMaxTranslationY();
                if (maxTranslationY > 0)
                {
                    var verticalSizeOffset = GetUniformVerticalSizeDiff() / 2f;
                    var translationY = Math.Min(-verticalSizeOffset, Math.Max(-(maxTranslationY + verticalSizeOffset), _yOffset + e.TotalY - _panStartY));
                    Content.TranslationY = translationY;
                }
                System.Diagnostics.Debug.WriteLine($"[Gesture] Pan Running: TranslationX={Content.TranslationX}|TranslationY={Content.TranslationY}");
                break;

            case GestureStatus.Canceled:
            case GestureStatus.Completed:
                _panRunning = false;
                _xOffset = Content.TranslationX;
                _yOffset = Content.TranslationY;
                System.Diagnostics.Debug.WriteLine($"[Gesture] Pan Completed: xOffset={_xOffset}|yOffset={_yOffset}");
                break;
        }
    }

    private double GetMaxTranslationX()
    {
        if (Content == null)
            return 0;
        return (Content.Width * Content.Scale - (float)Width / (float)Content.ScaleX) * Content.ScaleX;
    }

    private double GetMaxTranslationY()
    {
        if (Content == null)
            return 0;
        return (Content.Height * Content.Scale - (float)Height / (float)Content.ScaleY) * Content.ScaleY;
    }

    private float GetUniformVerticalSizeDiff()
    {
        return Content is null ? 0f : (float)(Height - Content.Height * Content.ScaleY);
    }

    private float GetUniformHorizontalSizeDiff()
    {
        return Content is null ? 0f : (float)(Width - Content.Width * Content.ScaleX);
    }
    
    private async void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (!IsEnabled || Content is null)
            return;

        System.Diagnostics.Debug.WriteLine($"[Gesture] OnPinchUpdated {e.ScaleOrigin}|{e.Scale}|{e.Status}");
        if (_panRunning)
            return;

        switch (e.Status)
        {
            case GestureStatus.Started:
                System.Diagnostics.Debug.WriteLine($"[Gesture] Pinch Started {e.ScaleOrigin}|{e.Scale}|{e.Status}");
                // scaling cannot be cancelled, so ignore return value here
                FireScaleChanging(Content.Scale, -1);
                _pinchRunning = true;
                _pinchStartScale = Content.Scale;
                Content.AnchorX = 0;
                Content.AnchorY = 0;
                break;

            case GestureStatus.Running:
                System.Diagnostics.Debug.WriteLine($"[Gesture] Pinch Running {e.ScaleOrigin}|{e.Scale}|{e.Status}");
                
                var currentScaleChange = (e.Scale - 1) * _pinchStartScale;

                if ((_pinchLastScaleChange < 0 && currentScaleChange > 0) || (_pinchLastScaleChange > 0 && currentScaleChange < 0))
                    currentScaleChange = 0;

                _pinchLastScaleChange = currentScaleChange;

                var maxScale = CalcMaxScale();
                var targetScale = Content.Scale;
                targetScale += currentScaleChange;
                targetScale = Math.Max(MinScale, targetScale);
                targetScale = Math.Min(maxScale, targetScale);

                var deltaX = (Content.X + _xOffset) / Width;
                var deltaWidth = Width / (Content.Width * _pinchStartScale);
                var originX = (e.ScaleOrigin.X - deltaX) * deltaWidth;

                var deltaY = (Content.Y + _yOffset) / Height;
                var deltaHeight = Height / (Content.Height * _pinchStartScale);
                var originY = (e.ScaleOrigin.Y - deltaY) * deltaHeight;

                var targetX = _xOffset - (originX * Content.Width) * (targetScale - _pinchStartScale);
                var targetY = _yOffset - (originY * Content.Height) * (targetScale - _pinchStartScale);

                var translationX = targetX.Clamp(-Content.Width * (targetScale - 1), 0);
                var translationY = targetY.Clamp(-Content.Height * (targetScale - 1), 0);

                Content.TranslationX = translationX;
                Content.TranslationY = translationY;
                Content.Scale = targetScale;
                break;

            case GestureStatus.Completed:
                System.Diagnostics.Debug.WriteLine($"[Gesture] Pinch Completed {e.ScaleOrigin}|{e.Scale}|{e.Status}");
                // delay in order to avoid an accidental pan event (by not perfectly timed removal of fingers)
                await Task.Delay(250);
                _pinchRunning = false;
                _xOffset = Content.TranslationX;
                _yOffset = Content.TranslationY;
                FireScaleChanged(_pinchStartScale, Content.Scale);
                break;
        }
    }

    private float CalcMaxScale()
    {
        if (Content == null)
            return MaxScale;
        // Content.ScaleX works for now because of forced Stretch-Uniform
        return (int)(DefaultScale / Content.ScaleX) * MaxScale;
    }
    
    /// <summary>
    /// Returns the required scale of image width and height in order to fit to the container width and height.
    /// </summary>
    /// <param name="imageWidth">original image width</param>
    /// <param name="imageHeight">original image height</param>
    /// <param name="containerWidth">maximum image width</param>
    /// <param name="containerHeight">maximum image height</param>
    /// <param name="uniformFill">if the image should fill all available space (and not everything of the image is visible)</param>
    /// <returns></returns>
    private static double CalcFittingScale(double imageWidth, double imageHeight, double containerWidth, double containerHeight, bool uniformFill = false)
    {
        double widthScale = 0, heightScale = 0;
        if (imageWidth != 0)
            widthScale = containerWidth / imageWidth;
        if (imageHeight != 0)
            heightScale = containerHeight / imageHeight;

        var scale = uniformFill ? Math.Max(widthScale, heightScale) : Math.Min(widthScale, heightScale);
        return scale;
    }
    
#region Events

    private bool FireScaleChanging(double currentScale, double newScale)
    {
        var eventArgs = new ScaleChangingEventArgs { OldScale = currentScale, NewScale = newScale, Cancel = false };
        ScaleChanging?.Invoke(this, eventArgs);
        return eventArgs.Cancel;
    }

    private void FireScaleChanged(double oldScale, double currentScale)
    {
        var eventArgs = new ScaleChangedEventArgs { OldScale = oldScale, NewScale = currentScale };
        ScaleChanged?.Invoke(this, eventArgs);
    }

    private bool FireTapped(Microsoft.Maui.Controls.TappedEventArgs originalEventArgs, bool doubleTap)
    {
        var eventArgs = new TappedEventArgs(originalEventArgs, this) { IsDoubleTap = doubleTap };
        Tapped?.Invoke(this, eventArgs);
        return eventArgs.Consumed;
    }

    public event EventHandler<ScaleChangingEventArgs>? ScaleChanging;
    
    public event EventHandler<ScaleChangedEventArgs>? ScaleChanged;

    public event EventHandler<TappedEventArgs>? Tapped;

#endregion
}

public class ScaleChangingEventArgs : EventArgsCancellable
{
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public double OldScale { get; init; }
    
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public double NewScale { get; init; }
}

public class ScaleChangedEventArgs : EventArgs
{
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public double OldScale { get; init; }
    
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public double NewScale { get; init; }
}

