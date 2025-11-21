// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Effects;
using Windows.UI;

namespace Microsoft.CmdPal.UI.Controls;

public sealed partial class BlurImageControl : Control
{
    private Compositor? _compositor;
    private SpriteVisual? _effectVisual;
    private CompositionEffectBrush? _effectBrush;
    private CompositionSurfaceBrush? _imageBrush;

    public BlurImageControl()
    {
        this.DefaultStyleKey = typeof(BlurImageControl);
        this.Loaded += OnLoaded;
        this.SizeChanged += OnSizeChanged;
    }

    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(
            nameof(ImageSource),
            typeof(ImageSource),
            typeof(BlurImageControl),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty ImageStretchProperty =
        DependencyProperty.Register(
            nameof(ImageStretch),
            typeof(Stretch),
            typeof(BlurImageControl),
            new PropertyMetadata(Stretch.UniformToFill, OnImageStretchChanged));

    public static readonly DependencyProperty ImageOpacityProperty =
        DependencyProperty.Register(
            nameof(ImageOpacity),
            typeof(double),
            typeof(BlurImageControl),
            new PropertyMetadata(1.0, OnOpacityChanged));

    public static readonly DependencyProperty ImageBrightnessProperty =
        DependencyProperty.Register(
            nameof(ImageBrightness),
            typeof(double),
            typeof(BlurImageControl),
            new PropertyMetadata(1.0, OnBrightnessChanged));

    public static readonly DependencyProperty BlurAmountProperty =
        DependencyProperty.Register(
            nameof(BlurAmount),
            typeof(double),
            typeof(BlurImageControl),
            new PropertyMetadata(0.0, OnBlurAmountChanged));

    public static readonly DependencyProperty TintColorProperty =
        DependencyProperty.Register(
            nameof(TintColor),
            typeof(Color),
            typeof(BlurImageControl),
            new PropertyMetadata(Colors.Transparent, OnVisualPropertyChanged));

    public static readonly DependencyProperty TintIntensityProperty =
        DependencyProperty.Register(
            nameof(TintIntensity),
            typeof(double),
            typeof(BlurImageControl),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    private static readonly IEnumerable<string> AnimatableProperties = [
        "Blur.BlurAmount"
    ];

    public ImageSource ImageSource
    {
        get => (ImageSource)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public Stretch ImageStretch
    {
        get => (Stretch)GetValue(ImageStretchProperty);
        set => SetValue(ImageStretchProperty, value);
    }

    public double ImageOpacity
    {
        get => (double)GetValue(ImageOpacityProperty);
        set => SetValue(ImageOpacityProperty, value);
    }

    public double ImageBrightness
    {
        get => (double)GetValue(ImageBrightnessProperty);
        set => SetValue(ImageBrightnessProperty, Math.Clamp(value, -1, 1));
    }

    public double BlurAmount
    {
        get => (double)GetValue(BlurAmountProperty);
        set => SetValue(BlurAmountProperty, value);
    }

    public Color TintColor
    {
        get => (Color)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    public double TintIntensity
    {
        get => (double)GetValue(TintIntensityProperty);
        set => SetValue(TintIntensityProperty, value);
    }

    private static void OnImageStretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BlurImageControl control && control._imageBrush != null)
        {
            control._imageBrush.Stretch = ConvertStretch((Stretch)e.NewValue);
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BlurImageControl control && control._compositor != null)
        {
            control.UpdateEffect();
        }
    }

    private static void OnOpacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BlurImageControl control && control._effectVisual != null)
        {
            control._effectVisual.Opacity = (float)(double)e.NewValue;
        }
    }

    private static void OnBlurAmountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BlurImageControl control && control._effectBrush != null)
        {
            control._effectBrush.Properties.InsertScalar("Blur.BlurAmount", (float)(double)e.NewValue);
        }
    }

    private static void OnBrightnessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BlurImageControl control && control._effectBrush != null)
        {
            control.UpdateEffect();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeComposition();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_effectVisual != null)
        {
            _effectVisual.Size = new Vector2(
                (float)Math.Max(1, e.NewSize.Width),
                (float)Math.Max(1, e.NewSize.Height));
        }
    }

    private void InitializeComposition()
    {
        var visual = ElementCompositionPreview.GetElementVisual(this);
        _compositor = visual.Compositor;

        _effectVisual = _compositor.CreateSpriteVisual();
        _effectVisual.Size = new Vector2(
            (float)Math.Max(1, ActualWidth),
            (float)Math.Max(1, ActualHeight));
        _effectVisual.Opacity = (float)ImageOpacity;

        ElementCompositionPreview.SetElementChildVisual(this, _effectVisual);

        UpdateEffect();
    }

    private void UpdateEffect()
    {
        if (_compositor == null)
        {
            return;
        }

        // Build effect graph
        var brightnessFactor = (float)Math.Clamp(ImageBrightness + 1.0f, 0.0f, 2.0f);

        var brightnessMatrix = new Matrix5x4
        {
            // Scale R, G, B by brightnessFactor, leave alpha and offsets unchanged
            M11 = brightnessFactor,
            M22 = brightnessFactor,
            M33 = brightnessFactor,
            M44 = 1.0f,
        };

        // 1) Brightness
        var brightnessEffect = new ColorMatrixEffect
        {
            Name = "Brightness",
            ColorMatrix = brightnessMatrix,
            Source = new CompositionEffectSourceParameter("ImageSource"),
        };

        var blurEffect = new GaussianBlurEffect
        {
            Name = "Blur",
            BlurAmount = (float)BlurAmount,
            BorderMode = EffectBorderMode.Hard,
            Optimization = EffectOptimization.Speed,
            Source = brightnessEffect,
        };

        IGraphicsEffect finalEffect = blurEffect;

        // Add tint if intensity > 0
        if (TintIntensity > 0)
        {
            var tintColor = TintColor;
            var adjustedColor = Color.FromArgb(
                (byte)(TintIntensity * 255),
                tintColor.R,
                tintColor.G,
                tintColor.B);

            finalEffect = new BlendEffect
            {
                Background = finalEffect,
                Foreground = new ColorSourceEffect
                {
                    Name = "Tint",
                    Color = adjustedColor,
                },
                Mode = BlendEffectMode.Multiply,
            };
        }

        var effectFactory = _compositor.CreateEffectFactory(finalEffect, AnimatableProperties);
        _effectBrush?.Dispose();
        _effectBrush = effectFactory.CreateBrush();

        // Set image source
        if (ImageSource != null)
        {
            // Load image into composition surface
            _imageBrush = _compositor.CreateSurfaceBrush();
            LoadImageAsync(ImageSource);
            _effectBrush.SetSourceParameter("ImageSource", _imageBrush);
        }
        else
        {
            // Use backdrop if no image source
            _effectBrush.SetSourceParameter("ImageSource", _compositor.CreateBackdropBrush());
        }

        _effectBrush.Properties.InsertScalar("Blur.BlurAmount", (float)BlurAmount);

        if (_effectVisual != null)
        {
            _effectVisual.Brush = _effectBrush;
        }
    }

    private void LoadImageAsync(ImageSource imageSource)
    {
        try
        {
            if (imageSource is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bitmapImage)
            {
                var loadedSurface = Microsoft.UI.Xaml.Media.LoadedImageSurface.StartLoadFromUri(bitmapImage.UriSource);
                loadedSurface.LoadCompleted += (sender, args) =>
                {
                    if (_imageBrush is not null)
                    {
                        _imageBrush.Surface = loadedSurface;
                        _imageBrush.Stretch = CompositionStretch.UniformToFill;
                    }
                };
            }
        }
        catch (Exception)
        {
            // Handle loading errors
        }
    }

    private static CompositionStretch ConvertStretch(Stretch stretch)
    {
        return stretch switch
        {
            Stretch.None => CompositionStretch.None,
            Stretch.Fill => CompositionStretch.Fill,
            Stretch.Uniform => CompositionStretch.Uniform,
            Stretch.UniformToFill => CompositionStretch.UniformToFill,
            _ => CompositionStretch.UniformToFill,
        };
    }
}
